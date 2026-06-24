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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.PubSub.Adapter.Session
{
    /// <summary>
    /// Default <see cref="IServerSession"/> implementation that wraps a
    /// modern <see cref="ManagedSession"/> built from
    /// <see cref="ServerConnectionOptions"/> and an
    /// <see cref="ITelemetryContext"/>. Read/Write/Call services delegate to
    /// the managed session; data-change subscriptions use the session's
    /// <see cref="ISubscriptionManager"/>. Reconnect and keep-alive are owned by
    /// the managed session.
    /// </summary>
    public sealed class ServerSession : IServerSession
    {
        private readonly ServerConnectionOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_connectLock = new(1, 1);
        private readonly ConcurrentDictionary<string, NodeId> m_resolvedPaths = new(StringComparer.Ordinal);
        private ManagedSession? m_session;
        private bool m_disposed;

        /// <summary>
        /// Creates a new external server session for the supplied connection
        /// options and telemetry context. The managed session is created lazily
        /// on the first <see cref="ConnectAsync"/> or service call.
        /// </summary>
        public ServerSession(
            ServerConnectionOptions options,
            ITelemetryContext telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            if (string.IsNullOrWhiteSpace(m_options.EndpointUrl))
            {
                throw new ArgumentException(
                    "EndpointUrl must be specified.", nameof(options));
            }
            m_logger = telemetry.CreateLogger<ServerSession>();
        }

        /// <inheritdoc/>
        public bool IsConnected => m_session?.Connected ?? false;

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            await m_connectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Idempotent: only create the managed session once. A concurrent
                // caller may have established it while this call awaited the lock.
                if (m_session == null)
                {
                    m_session = await CreateSessionAsync(ct).ConfigureAwait(false);
                }
            }
            finally
            {
                m_connectLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<DataValue>> ReadAsync(
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken ct = default)
        {
            ManagedSession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);
            ReadResponse response = await session.ReadAsync(
                null,
                0.0,
                TimestampsToReturn.Both,
                nodesToRead,
                ct).ConfigureAwait(false);
            return response.Results;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<StatusCode>> WriteAsync(
            ArrayOf<WriteValue> nodesToWrite,
            CancellationToken ct = default)
        {
            ManagedSession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);
            WriteResponse response = await session.WriteAsync(
                null,
                nodesToWrite,
                ct).ConfigureAwait(false);
            return response.Results;
        }

        /// <inheritdoc/>
        public async ValueTask<RemoteCallResult> CallAsync(
            NodeId objectId,
            NodeId methodId,
            ArrayOf<Variant> inputArguments,
            CancellationToken ct = default)
        {
            ManagedSession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);

            var request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = inputArguments
            };
            ArrayOf<CallMethodRequest> requests = [request];

            CallResponse response = await session.CallAsync(
                null,
                requests,
                ct).ConfigureAwait(false);

            ArrayOf<CallMethodResult> results = response.Results;
            ClientBase.ValidateResponse(results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);

            CallMethodResult result = results[0];
            return new RemoteCallResult(result.StatusCode, result.OutputArguments);
        }

        /// <inheritdoc/>
        public async ValueTask<IDataChangeSubscription> CreateDataChangeSubscriptionAsync(
            double publishingIntervalMs,
            CancellationToken ct = default)
        {
            ManagedSession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);
            return new DataChangeSubscription(
                session.SubscriptionManager,
                publishingIntervalMs,
                m_telemetry);
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> ResolveNodeIdAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            if (!NodeBrowsePath.IsBrowsePath(nodeId))
            {
                return nodeId;
            }

            string path = nodeId.IdentifierAsString;
            if (m_resolvedPaths.TryGetValue(path, out NodeId cached))
            {
                return cached;
            }

            ManagedSession session = await EnsureConnectedAsync(ct).ConfigureAwait(false);

            var request = new Opc.Ua.BrowsePath
            {
                StartingNode = ObjectIds.ObjectsFolder,
                RelativePath = NodeBrowsePath.ToRelativePath(nodeId)
            };
            ArrayOf<Opc.Ua.BrowsePath> requests = [request];

            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, requests, ct)
                .ConfigureAwait(false);

            ArrayOf<BrowsePathResult> results = response.Results;
            ClientBase.ValidateResponse(results, requests);

            BrowsePathResult result = results[0];
            if (StatusCode.IsBad(result.StatusCode) || result.Targets.IsNull || result.Targets.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNoMatch,
                    "Browse path '{0}' did not resolve to a node ({1}).",
                    path,
                    result.StatusCode);
            }

            NodeId resolved = ExpandedNodeId.ToNodeId(
                result.Targets[0].TargetId,
                session.MessageContext.NamespaceUris);
            m_resolvedPaths[path] = resolved;
            m_logger.LogDebug(
                "Resolved browse path '{Path}' to node {NodeId}.", path, resolved);
            return resolved;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;

            ManagedSession? session = m_session;
            m_session = null;
            if (session != null)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(ex,
                        "ServerSession: managed session dispose failed.");
                }
            }

            m_connectLock.Dispose();
        }

        private async ValueTask<ManagedSession> EnsureConnectedAsync(CancellationToken ct)
        {
            ManagedSession? session = m_session;
            if (session != null)
            {
                return session;
            }
            await ConnectAsync(ct).ConfigureAwait(false);
            return m_session ?? throw ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "External server session is not connected.");
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(ServerSession));
            }
        }

        private async Task<ManagedSession> CreateSessionAsync(CancellationToken ct)
        {
            ApplicationConfiguration configuration = m_options.ApplicationConfiguration
                ?? await BuildApplicationConfigurationAsync(ct).ConfigureAwait(false);

            EndpointDescription selectedEndpoint =
                await SelectEndpointAsync(configuration, ct).ConfigureAwait(false);

            var endpoint = new ConfiguredEndpoint(
                null,
                selectedEndpoint,
                EndpointConfiguration.Create(configuration));

            IUserIdentity? identity = ResolveUserIdentity();

            ManagedSessionBuilder builder = new ManagedSessionBuilder(configuration, m_telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(m_options.SessionName)
                .WithSessionTimeout(TimeSpan.FromMilliseconds(m_options.SessionTimeout));
            if (identity != null)
            {
                builder = builder.WithUserIdentity(identity);
            }

            m_logger.LogInformation(
                "Connecting external server session to {EndpointUrl} ({SecurityMode}).",
                selectedEndpoint.EndpointUrl,
                selectedEndpoint.SecurityMode);

            return await builder.ConnectAsync(ct).ConfigureAwait(false);
        }

        private IUserIdentity? ResolveUserIdentity()
        {
            if (m_options.UserIdentity != null)
            {
                return m_options.UserIdentity;
            }
            if (!string.IsNullOrEmpty(m_options.UserName))
            {
                return new UserIdentity(
                    m_options.UserName!,
                    System.Text.Encoding.UTF8.GetBytes(m_options.Password ?? string.Empty));
            }
            return null;
        }

        private async ValueTask<EndpointDescription> SelectEndpointAsync(
            ApplicationConfiguration configuration,
            CancellationToken ct)
        {
            var requestUri = new Uri(m_options.EndpointUrl);
            var endpointConfiguration = EndpointConfiguration.Create(configuration);

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                configuration,
                requestUri,
                endpointConfiguration,
                ct: ct).ConfigureAwait(false);

            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);

            EndpointDescription? selected = null;
            foreach (EndpointDescription endpoint in endpoints)
            {
                if (endpoint.EndpointUrl == null ||
                    !endpoint.EndpointUrl.StartsWith(
                        requestUri.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (endpoint.SecurityMode != m_options.SecurityMode)
                {
                    continue;
                }
                if (m_options.SecurityPolicyUri != null &&
                    !string.Equals(
                        endpoint.SecurityPolicyUri,
                        m_options.SecurityPolicyUri,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (selected == null || endpoint.SecurityLevel > selected.SecurityLevel)
                {
                    selected = endpoint;
                }
            }

            if (selected == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "No endpoint at {0} matches security mode {1} / policy {2}.",
                    m_options.EndpointUrl,
                    m_options.SecurityMode,
                    m_options.SecurityPolicyUri ?? "(auto)");
            }

            // Preserve the requested host/port: discovery may advertise an
            // endpoint URL with a different host than the one the caller used.
            Uri? selectedUrl = Utils.ParseUri(selected.EndpointUrl);
            if (selectedUrl != null && selectedUrl.Scheme == requestUri.Scheme)
            {
                selected.EndpointUrl = new UriBuilder(selectedUrl)
                {
                    Host = requestUri.IdnHost,
                    Port = requestUri.Port
                }.ToString();
            }

            return selected;
        }

        private async ValueTask<ApplicationConfiguration> BuildApplicationConfigurationAsync(
            CancellationToken ct)
        {
            string pkiRoot = Path.Combine(
                AppContext.BaseDirectory, "pki", "Opc.Ua.PubSub.Adapter");

            var configuration = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = m_options.ApplicationName,
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = string.Format(
                            CultureInfo.InvariantCulture,
                            "CN={0}, O=OPC Foundation",
                            m_options.ApplicationName)
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                },
                TransportQuotas = new TransportQuotas
                {
                    MaxMessageSize = 4 * 1024 * 1024
                },
                ClientConfiguration = new ClientConfiguration()
            };

            await configuration.ValidateAsync(ApplicationType.Client, ct).ConfigureAwait(false);
            return configuration;
        }
    }
}
