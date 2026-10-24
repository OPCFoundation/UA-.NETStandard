/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.DataChannels;
using Opc.Ua.Configuration;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Server.DataChannels;
using Opc.Ua.PubSub.Transports.DataChannels;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.DataChannels;
using Opc.Ua.Server.Hosting;
using ClientSession = Opc.Ua.Client.ISession;
using ServerSession = Opc.Ua.Server.ISession;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Composes production server, client, certificate and PubSub APIs over real loopback TCP.
    /// </summary>
    internal static class SecureChannelSample
    {
        /// <summary>
        /// Starts both peers in one process without replacing either peer's sockets or protocol stack.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static async Task<RunEvidence> RunDemoAsync(
            SampleOptions options,
            SamplePki pki,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ready = new TaskCompletionSource<ServerReady>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<EndpointEvidence> server = RunServerAsync(options, pki, telemetry, ready, lifetime.Token);
            ServerReady endpoint;
            try
            {
                endpoint = await ready.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
            }
            catch
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
                await server.ConfigureAwait(false);
                throw;
            }
            Task<EndpointEvidence> client = RunClientAsync(options, pki, telemetry, endpoint, lifetime.Token);
            Task first = await Task.WhenAny(server, client).ConfigureAwait(false);
            if (!first.IsCompletedSuccessfully)
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
            }
            EndpointEvidence[] peers = await Task.WhenAll(server, client).ConfigureAwait(false);
            EndpointEvidence publisher = peers.Single(peer => peer.Publisher);
            EndpointEvidence subscriber = peers.Single(peer => !peer.Publisher);
            if (publisher.ContextId != subscriber.ContextId ||
                publisher.ChannelId != subscriber.ChannelId ||
                !publisher.Sent.SequenceEqual(subscriber.Received, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("The exact emitted and received NetworkMessages differ.");
            }
            return new RunEvidence { Endpoints = peers };
        }

        /// <summary>
        /// Hosts the standard server and its sole production PubSub accept router.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="DataChannelException"></exception>
        internal static async Task<EndpointEvidence> RunServerAsync(
            SampleOptions options,
            SamplePki pki,
            ITelemetryContext telemetry,
            TaskCompletionSource<ServerReady>? ready,
            CancellationToken cancellationToken)
        {
            try
            {
                if (options.ReadyFile != null && File.Exists(options.ReadyFile))
                {
                    throw new InvalidOperationException("Use a fresh --ready path; stale readiness is not reused.");
                }
                int port = options.Port == 0 ? FreePort() : options.Port;
                int controlPort = FreePort();
                while (controlPort == port)
                {
                    controlPort = FreePort();
                }
                string flowEndpoint = $"opc.tcp://localhost:{port}" +
                    (options.Shared ? DataChannelProfiles.SharedResource : DataChannelProfiles.DedicatedResource);
                string controlEndpoint = $"opc.tcp://localhost:{controlPort}/control";
                bool needServices = options.Shared || options.SessionAuthorization;
                string[] addresses = needServices ? [flowEndpoint, controlEndpoint] : [flowEndpoint];
                ApplicationInstance application = await pki.OpenApplicationAsync(
                    true, addresses, telemetry, false, cancellationToken).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable applicationLifetime = application.ConfigureAwait(false);
                ICertificateManager certificates = SamplePki.Manager(application);
                using Certificate peer = await SamplePki.PeerCertificateAsync(
                    certificates, SampleConfiguration.ClientApplicationUri, cancellationToken).ConfigureAwait(false);
                var sources = new DataChannelSourceRegistry();
                sources.Register(PubSubDataChannelSource.Create(
                    SampleConfiguration.Build(options, !options.Reverse).Connections[0],
                    new SampleSourcePolicy(
                        DataChannelCertificateIdentity.GetKeyThumbprint(peer), options.SessionAuthorization),
                    maximumFlows: 1,
                    sourceNodeId: options.SessionAuthorization ? SourceId : default));
                var provider = new ServerDataChannelProvider(
                    sources, new DataChannelOptions(), new DataChannelApplicationBudget(),
                    TimeProvider.System, telemetry);
                await using ConfiguredAsyncDisposable providerLifetime = provider.ConfigureAwait(false);
                var server = new StandardServer(telemetry);
                await using ConfiguredAsyncDisposable serverLifetime = server.ConfigureAwait(false);
                server.UseDataChannels(provider, enableModel: options.SessionAuthorization);
                await application.StartAsync(server, cancellationToken).ConfigureAwait(false);
                server.CurrentInstance.IdentityRegistry.Register(new X509Authenticator(certificates));
                var sessions = new ConcurrentQueue<ServerSession>();
                void SessionCreated(ServerSession session, SessionEventReason reason) => sessions.Enqueue(session);
                server.CurrentInstance.SessionManager.SessionCreated += SessionCreated;
                try
                {
                    bool model = server.EnableDataChannelModel;
                    var endpoint = new ServerReady
                    {
                        FlowEndpoint = flowEndpoint,
                        ControlEndpoint = needServices ? controlEndpoint : string.Empty,
                        Shared = options.Shared,
                        SessionAuthorization = options.SessionAuthorization,
                        Reverse = options.Reverse,
                        Count = options.Count
                    };
                    if (options.ReadyFile != null)
                    {
                        await EvidenceFiles.WriteAsync(options.ReadyFile,
                            JsonSerializer.Serialize(endpoint, EvidenceJsonContext.Default.ServerReady),
                            cancellationToken).ConfigureAwait(false);
                    }
                    ready?.TrySetResult(endpoint);
                    Console.WriteLine($"LISTENING {flowEndpoint} profile=" +
                        DataChannelPubSubConfiguration.GetTransportProfile(options.Carrier));
                    DataChannelContext context = await provider.WaitForContextAsync(
                        SampleConfiguration.ClientApplicationUri, options.Carrier, cancellationToken)
                        .ConfigureAwait(false);
                    DataChannelContextIdentity identity = context.Identity;
                    var peerClosed = new TaskCompletionSource<DataChannelResult>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    void ContextClosed(DataChannelResult result) => peerClosed.TrySetResult(result);
                    context.Closed += ContextClosed;
                    if (!context.IsReady)
                    {
                        throw new InvalidOperationException("The peer closed its context before the workload started.");
                    }
                    EndpointEvidence result = await PubSubWorkload.RunAsync(
                        options, true, new ServerDataChannelPubSubFlowProvider(provider), telemetry, null,
                        cancellationToken).ConfigureAwait(false);
                    int reads = 0;
                    int publishes = 0;
                    int calls = 0;
                    foreach (ServerSession session in sessions)
                    {
                        reads += session.ReadDiagnostics(data => checked((int)data.ReadCount.TotalCount));
                        publishes += session.ReadDiagnostics(data => checked((int)data.PublishCount.TotalCount));
                        calls += session.ReadDiagnostics(data => checked((int)data.CallCount.TotalCount));
                    }
                    if ((!needServices && !sessions.IsEmpty) ||
                        (options.Shared && (sessions.Count != 1 || reads == 0 || publishes == 0)) ||
                        (options.SessionAuthorization && calls < 2))
                    {
                        throw new InvalidOperationException(
                            "Actual server Session/Read/Publish/Call accounting differs.");
                    }
                    try
                    {
                        DataChannelResult closed = await peerClosed.Task.WaitAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (closed is not (DataChannelResult.Good or DataChannelResult.Closed))
                        {
                            throw new DataChannelException(closed);
                        }
                    }
                    finally
                    {
                        context.Closed -= ContextClosed;
                    }
                    return result with
                    {
                        Sessions = sessions.Count,
                        Reads = reads,
                        Publishes = publishes,
                        Calls = calls,
                        ModelEnabled = model,
                        TransportInitiator = identity.Peer.IsTransportInitiator,
                        PeerApplicationUri = identity.Peer.PeerApplicationUri
                    };
                }
                finally
                {
                    server.CurrentInstance.SessionManager.SessionCreated -= SessionCreated;
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await application.StopAsync(cleanup.Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                ready?.TrySetException(exception);
                throw;
            }
        }

        /// <summary>
        /// Opens the selected UASC data endpoint directly, with no discovery or Services on a dedicated endpoint.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="DataChannelException"></exception>
        internal static async Task<EndpointEvidence> RunClientAsync(
            SampleOptions options,
            SamplePki pki,
            ITelemetryContext telemetry,
            ServerReady ready,
            CancellationToken cancellationToken)
        {
            ApplicationInstance application = await pki.OpenApplicationAsync(
                false, [], telemetry, false, cancellationToken).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable applicationLifetime = application.ConfigureAwait(false);
            ApplicationConfiguration configuration = application.ApplicationConfiguration!;
            ICertificateManager certificates = SamplePki.Manager(application);
            using CertificateEntry own = SamplePki.OwnCertificate(certificates);
            using Certificate peer = await SamplePki.PeerCertificateAsync(
                certificates, SampleConfiguration.ServerApplicationUri, cancellationToken).ConfigureAwait(false);
            var policy = new SampleClientPolicy(options, DataChannelCertificateIdentity.GetKeyThumbprint(peer));
            var sessionAuthorizer = new SessionDataChannelAuthorizer(policy, TimeProvider.System, telemetry);
            await using ConfiguredAsyncDisposable authorizerLifetime = sessionAuthorizer.ConfigureAwait(false);
            var factory = new DataChannelContextFactory(
                sessionAuthorizer,
                new DataChannelOptions(), new DataChannelApplicationBudget(), TimeProvider.System, telemetry);
            EndpointDescription endpoint = DescribeEndpoint(ready.FlowEndpoint, options, peer, false);
            ServiceMessageContext messageContext = configuration.CreateMessageContext();
            var settings = new TransportChannelSettings
            {
                Description = endpoint,
                Configuration = EndpointConfiguration.Create(configuration),
                CertificateValidator = certificates,
                NamespaceUris = messageContext.NamespaceUris,
                Factory = messageContext.Factory,
                DataChannels = factory
            };
            var connector = new UascDataChannelClient(new TcpTransportChannelFactory(), telemetry);
            UascDataChannelConnection connection;
            Certificate? channelClient = null;
            Certificate? channelServer = null;
            try
            {
                channelClient = own.Certificate.AddRef();
                channelServer = peer.AddRef();
                settings.ClientCertificate = channelClient;
                settings.ServerCertificate = channelServer;
                connection = await connector.ConnectAsync(
                    new Uri(ready.FlowEndpoint), settings, cancellationToken).ConfigureAwait(false);
                channelClient = null;
                channelServer = null;
            }
            finally
            {
                channelClient?.Dispose();
                channelServer?.Dispose();
            }
            await using ConfiguredAsyncDisposable connectionLifetime = connection.ConfigureAwait(false);
            uint secureChannelId = connection.SecureChannelId;
            ClientSession? session = options.Shared || options.SessionAuthorization
                ? await OpenSessionAsync(options, pki, telemetry, configuration, certificates, own, peer,
                    ready, connection, cancellationToken).ConfigureAwait(false)
                : null;
            var serviceScope = new SessionScope(session, options.Shared ? connection : null);
            await using ConfiguredAsyncDisposable serviceLifetime = serviceScope.ConfigureAwait(false);
            if (session != null)
            {
                await CheckOptionalModelAsync(session, options.SessionAuthorization, cancellationToken)
                    .ConfigureAwait(false);
            }
            DataChannelSessionControl? control = options.SessionAuthorization
                ? new DataChannelSessionControl(session!, sessionAuthorizer, TimeProvider.System)
                : null;
            SampleGrantProvider? grants = control != null
                ? new SampleGrantProvider(control, ExpandedNodeId.ToNodeId(SourceId, session!.NamespaceUris))
                : null;
            var flows = new DataChannelContextPubSubFlowProvider(
                connection.Context, openLocally: true, grantProvider: grants);
            await using ConfiguredAsyncDisposable flowLifetime = flows.ConfigureAwait(false);
            var probe = new SharedServiceProbe();
            int revisionCalls = 0;
            bool sameTransport = session != null && ReferenceEquals(session.TransportChannel, connection.Services);
            uint serviceChannelId = (session?.TransportChannel as ISecureChannel)?.CurrentToken?.ChannelId ?? 0;
            if (options.Shared && (!sameTransport || secureChannelId == 0 || serviceChannelId != secureChannelId))
            {
                throw new InvalidOperationException("Services and PubSub did not select the same SecureChannel.");
            }
            if (options.Shared &&
                (session!.ConfiguredEndpoint.Description.TransportProfileUri != DataChannelProfiles.SharedUasc ||
                    session.ConfiguredEndpoint.Description.EndpointUrl != ready.FlowEndpoint))
            {
                throw new InvalidOperationException(
                    "The shared Session changed the selected flow endpoint or profile.");
            }
            if (!options.Shared && connection.Services != null)
            {
                throw new InvalidOperationException("The dedicated data endpoint unexpectedly exposes Services.");
            }
            async Task ReviseAsync(DataChannel channel, CancellationToken token)
            {
                if (options.Shared)
                {
                    await probe.RunAsync(session!, token).ConfigureAwait(false);
                }
                DataChannelParametersDataType reduced = channel.Parameters;
                reduced.MaxMessageSize = 16384;
                reduced.MaxReceiveBytes = 131072;
                if (control != null)
                {
                    DataChannelStatusDataType modified = await control.ModifyAsync(channel, 1, reduced, token)
                        .ConfigureAwait(false);
                    revisionCalls++;
                    if (modified.Revision != 2)
                    {
                        throw new InvalidOperationException("The Session Call did not finish the data revision.");
                    }
                }
                else
                {
                    DataChannelResult result = await channel.UpdateAsync(reduced, 1, token).ConfigureAwait(false);
                    if (result != DataChannelResult.Good)
                    {
                        throw new DataChannelException(result);
                    }
                }
            }
            EndpointEvidence result = await PubSubWorkload.RunAsync(
                options, false, flows, telemetry, ReviseAsync, cancellationToken).ConfigureAwait(false);
            if (connection.SecureChannelId != secureChannelId ||
                (options.Shared &&
                    (session!.TransportChannel as ISecureChannel)?.CurrentToken?.ChannelId != secureChannelId))
            {
                throw new InvalidOperationException(
                    "The flow or ordinary traffic silently changed SecureChannels.");
            }
            return result with
            {
                SecureChannelId = secureChannelId,
                ServiceSecureChannelId = serviceChannelId,
                SameServiceTransport = sameTransport,
                Sessions = session == null ? 0 : 1,
                Reads = probe.Reads,
                Publishes = probe.Publishes,
                Notifications = probe.Notifications,
                Calls = (grants?.Calls ?? 0) + revisionCalls,
                TransportInitiator = connection.Context.Identity.Peer.IsTransportInitiator,
                PeerApplicationUri = connection.Context.Identity.Peer.PeerApplicationUri
            };
        }

        /// <summary>
        /// Attaches a Session to the exact shared transport, or opens a separate ordinary control connection.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static async Task<ClientSession> OpenSessionAsync(
            SampleOptions options,
            SamplePki pki,
            ITelemetryContext telemetry,
            ApplicationConfiguration configuration,
            ICertificateManager certificates,
            CertificateEntry own,
            Certificate peer,
            ServerReady ready,
            UascDataChannelConnection connection,
            CancellationToken cancellationToken)
        {
            CertificateIdentifier identifier = configuration.SecurityConfiguration.ApplicationCertificates[0];
            using Certificate? materialized = await certificates.CertificateProvider.GetPrivateKeyCertificateAsync(
                identifier, pki.Password, configuration.ApplicationUri, cancellationToken).ConfigureAwait(false) ??
                throw new InvalidOperationException(
                    "The manager could not load the X.509 user identity.");
            UserIdentity identity = await UserIdentity.CreateAsync(
                identifier, pki.Password, certificates.CertificateProvider, cancellationToken).ConfigureAwait(false);
            ISessionFactory sessions = new DefaultSessionFactory(telemetry)
                .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance);
            EndpointDescription description = options.Shared
                ? DescribeEndpoint(ready.FlowEndpoint, options, peer, false)
                : DescribeEndpoint(ready.ControlEndpoint, options, peer, true);
            var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(configuration));
            if (!options.Shared)
            {
                return await sessions.CreateAsync(
                    configuration, endpoint, false, "SecureChannelPubSubControl", 60000, identity, [],
                    cancellationToken).ConfigureAwait(false);
            }
            ClientSession session = sessions.Create(
                connection.Services!, configuration, endpoint, own.Certificate.AddRef());
            try
            {
                await session.OpenAsync("SecureChannelPubSubShared", 60000, identity, [],
                    checkDomain: true, closeChannel: false, cancellationToken).ConfigureAwait(false);
                return session;
            }
            catch
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await connection.CloseAsync(cleanup.Token).ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Verifies optional model presence through the actual ordinary Session, never the diagnostics node manager.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static async Task CheckOptionalModelAsync(
            ClientSession session,
            bool expected,
            CancellationToken cancellationToken)
        {
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = ObjectIds.DataChannelManager, AttributeId = Attributes.NodeClass }],
                cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != 1 ||
                (expected &&
                    (StatusCode.IsBad(response.Results[0].StatusCode) ||
                        !response.Results[0].WrappedValue.TryGetValue(out int nodeClass) ||
                        nodeClass != (int)NodeClass.Object)) ||
                (!expected && response.Results[0].StatusCode != StatusCodes.BadNodeIdUnknown))
            {
                throw new InvalidOperationException("The optional model did not match its actual Session Read.");
            }
        }

        /// <summary>
        /// Constructs only the explicitly selected endpoint using independently pinned application evidence.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static EndpointDescription DescribeEndpoint(
            string address,
            SampleOptions options,
            Certificate peer,
            bool control)
        {
            var uri = new Uri(address);
            string expectedPath = control ? "/control" :
                options.Shared ? DataChannelProfiles.SharedResource : DataChannelProfiles.DedicatedResource;
            if (uri.Scheme != Utils.UriSchemeOpcTcp ||
                !uri.IsLoopback ||
                uri.AbsolutePath != expectedPath ||
                uri.Query.Length != 0 ||
                uri.Fragment.Length != 0 ||
                uri.UserInfo.Length != 0)
            {
                throw new InvalidOperationException("Only the selected exact loopback UASC resource is supported.");
            }
            return new EndpointDescription
            {
                EndpointUrl = address,
                TransportProfileUri = control ? Profiles.UaTcpTransport :
                    DataChannelPubSubConfiguration.GetTransportProfile(options.Carrier),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ServerCertificate = new ByteString(peer.RawData),
                Server = new ApplicationDescription
                {
                    ApplicationUri = SampleConfiguration.ServerApplicationUri,
                    ApplicationName = new LocalizedText("SecureChannelPubSubServer"),
                    ProductUri = SampleConfiguration.ModelNamespace,
                    ApplicationType = ApplicationType.Server
                },
                UserIdentityTokens = control || options.Shared
                    ?
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "certificate",
                            TokenType = UserTokenType.Certificate,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        }
                    ]
                    : []
            };
        }

        /// <summary>
        /// Chooses a bounded local port; actual readiness is signalled only after the standard listener opens.
        /// </summary>
        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>
        /// Disposes an optional Session before its owning UASC connection.
        /// </summary>
        private sealed class SessionScope(
            ClientSession? session,
            UascDataChannelConnection? sharedConnection) : IAsyncDisposable
        {
            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (session != null)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        StatusCode result = await session.CloseAsync(10000, false, cleanup.Token)
                            .ConfigureAwait(false);
                        if (!StatusCode.IsGood(result))
                        {
                            throw new ServiceResultException(result, "The ordinary Session did not close cleanly.");
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (sharedConnection != null)
                            {
                                await sharedConnection.CloseAsync(cleanup.Token).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            await session.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        private static readonly ExpandedNodeId SourceId = new("Telemetry", SampleConfiguration.ModelNamespace);
    }
}
