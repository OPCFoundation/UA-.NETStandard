// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Exercises authenticated native UASC and direct delivery without reflection-based test doubles.
    /// The identical scenario runs under NUnit and NativeAOT.
    /// </summary>
    internal static class NativeUascLiveScenario
    {
        /// <summary>
        /// Verifies Services, exporter binding, both direct delivery modes and joined resource release.
        /// </summary>
        internal static async ValueTask<ArrayOf<string>> RunAsync(INativeQuicBackend backend)
        {
            var checks = new List<string>();
            await using NativeFlowTestEnvironment environment =
                await NativeFlowTestEnvironment.CreateAsync(backend).ConfigureAwait(false);
            using Certificate tlsCertificate = environment.CreateApplicationCertificate("urn:test:tls-uasc-aot");
            using RSA rsa = tlsCertificate.GetRSAPrivateKey()!;
            byte[] exported = rsa.ExportPkcs8PrivateKey();
            using NativeQuicPrivateKey key = CreateKey(exported);
            NativeQuicCredentials serverCredentials = NativeQuicCredentials.ForTlsServer(
                new ByteString(tlsCertificate.RawData), default, key);
            NativeQuicCredentials clientCredentials =
                NativeQuicCredentials.ForTlsClient(new ByteString(environment.Root.RawData));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var serverTransport = new NativeQuicUascTransport(
                backend, environment.ServerBudget, environment.BindingOptions);
            var clientTransport = new NativeQuicUascTransport(
                backend, environment.ClientBudget, environment.BindingOptions);
            await using NativeQuicUascListener listener = await serverTransport.ListenAsync(
                new Uri("opc.quic://127.0.0.1:0/uasc?aot=authenticated"), serverCredentials)
                .ConfigureAwait(false);
            const string policy = SecurityPolicies.Basic256Sha256;
            using CertificateEntry serverCertificate =
                environment.ServerManager.AcquireApplicationCertificateBySecurityPolicy(policy)!;
            using CertificateEntry clientCertificate =
                environment.ClientManager.AcquireApplicationCertificateBySecurityPolicy(policy)!;
            using var chain = new CertificateCollection { clientCertificate.Certificate };
            foreach (Certificate issuer in clientCertificate.IssuerChain)
            {
                chain.Add(issuer);
            }
            var authorizer = new ConfiguredAuthorizer();
            var clientFactory = new DataChannelContextFactory(authorizer, environment.Options,
                environment.ClientBudget, TimeProvider.System, environment.Telemetry);
            var serverFactory = new DataChannelContextFactory(authorizer, environment.Options,
                environment.ServerBudget, TimeProvider.System, environment.Telemetry);
            var endpoint = new EndpointDescription
            {
                EndpointUrl = listener.EndpointUrl.ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = policy,
                ServerCertificate = serverCertificate.Certificate.RawData.ToByteString(),
                TransportProfileUri = DataChannelProfiles.UascQuic,
                Server = new ApplicationDescription { ApplicationUri = NativeFlowTestEnvironment.ServerUri }
            };
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 10000;
            configuration.MaxMessageSize = 65536;
            configuration.MaxBufferSize = 65536;
            var callback = new ReadCallback(clientCertificate.Certificate.RawData.ToByteString());
            await using var uascListener = new TcpTransportListener(environment.Telemetry);
            uascListener.OpenExternal(listener.EndpointUrl, new TransportListenerSettings
            {
                Descriptions = new List<EndpointDescription> { endpoint },
                Configuration = configuration,
                ServerCertificates = environment.ServerManager,
                CertificateValidator = environment.ServerManager,
                DataChannels = serverFactory,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create(),
                MaxChannelCount = 4
            }, callback);
            Task<NativeQuicUascConnection> accepting = listener.AcceptAsync(timeout.Token).AsTask();
            await using NativeQuicUascConnection client = await clientTransport.ConnectAsync(
                listener.EndpointUrl, clientCredentials, cancellationToken: timeout.Token).ConfigureAwait(false);
            using ITransportChannel channel = client.Create(environment.Telemetry);
            Task opening = ((ISecureChannel)channel).OpenAsync(listener.EndpointUrl, new TransportChannelSettings
            {
                Description = endpoint,
                Configuration = configuration,
                ClientCertificate = clientCertificate.Certificate,
                ClientCertificateChain = chain,
                ServerCertificate = serverCertificate.Certificate,
                CertificateValidator = environment.ClientManager,
                DataChannels = clientFactory,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create()
            }, timeout.Token).AsTask();
            await using NativeQuicUascConnection server = await accepting.ConfigureAwait(false);
            TcpListenerChannel accepted =
                uascListener.AcceptTransport(server, environment.ServerManager, configuration.MaxBufferSize);
            await opening.ConfigureAwait(false);
            await listener.DisposeAsync().ConfigureAwait(false);
            Check(!new ByteString(tlsCertificate.RawData).Equals(endpoint.ServerCertificate) &&
                !new ByteString(tlsCertificate.RawData).Equals(new ByteString(clientCertificate.Certificate.RawData)),
                "IndependentApplicationAndTransportCertificates");
            await CheckReadAsync("ServicesBeforeDirectDelivery").ConfigureAwait(false);
            DataChannelContext clientContext = ((IDataChannelTransportChannel)channel).DataChannels ??
                throw new InvalidOperationException("The authenticated client context is missing.");
            DataChannelContext serverContext = accepted.DataChannels ??
                throw new InvalidOperationException("The authenticated server context is missing.");
            Check(clientContext.CarrierKind == DataChannelCarrierKind.QuicUasc &&
                serverContext.CarrierKind == DataChannelCarrierKind.QuicUasc, "NativeUascCarrierBothRoles");
            Check(await clientContext.WaitForDirectBindingAsync(timeout.Token).ConfigureAwait(false) ==
                DataChannelResult.Good &&
                await serverContext.WaitForDirectBindingAsync(timeout.Token).ConfigureAwait(false) ==
                DataChannelResult.Good, "AuthenticatedExporterBindingBothRoles");
            using IDisposable serverRoute = serverContext.RegisterUascPublisherRoute(
                NativeFlowTestEnvironment.ResourceUri, new Variant(42u));
            using IDisposable clientRoute = clientContext.RegisterUascPublisherRoute(
                NativeFlowTestEnvironment.ResourceUri, new Variant(17u));
            await CheckFlowAsync(clientContext, serverContext, DataChannelDeliveryMode.Ordered).ConfigureAwait(false);
            await CheckFlowAsync(serverContext, clientContext, DataChannelDeliveryMode.Datagram).ConfigureAwait(false);
            await CheckFlowAsync(clientContext, serverContext, DataChannelDeliveryMode.Ordered,
                DataChannelProfiles.UadpOpaqueApplicationPayload).ConfigureAwait(false);
            await CheckFlowAsync(serverContext, clientContext, DataChannelDeliveryMode.Datagram,
                DataChannelProfiles.UadpOpaqueApplicationPayload).ConfigureAwait(false);
            await CheckFlowAsync(serverContext, clientContext, DataChannelDeliveryMode.Ordered,
                DataChannelProfiles.UadpAvApplicationPayload).ConfigureAwait(false);
            await CheckFlowAsync(clientContext, serverContext, DataChannelDeliveryMode.Datagram,
                DataChannelProfiles.UadpAvApplicationPayload).ConfigureAwait(false);
            await CheckReadAsync("ServicesAfterDirectDelivery").ConfigureAwait(false);
            Check(callback.Reads == 8 && callback.AuthenticatedOpens > 0 && callback.AuditErrors == 0,
                "ApplicationCertificateAndServiceAudit");
            await channel.CloseAsync(timeout.Token).ConfigureAwait(false);
            await uascListener.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
            await server.DisposeAsync().ConfigureAwait(false);
            await NativeFlowTestEnvironment.AssertNoNativeOwnershipAsync(backend).ConfigureAwait(false);
            Check(await backend.GetCountsAsync().ConfigureAwait(false) == default, "ZeroNativeOwnershipAfterUasc");
            Check(environment.ClientBudget.Usage == default && environment.ServerBudget.Usage == default,
                "ZeroUascAndDirectReservations");
            return new ArrayOf<string>(checks.ToArray().AsMemory());

            async Task CheckReadAsync(string name)
            {
                IServiceResponse response = await channel.SendRequestAsync(new ReadRequest
                {
                    RequestHeader = new RequestHeader { TimeoutHint = 10000 },
                    NodesToRead = new ArrayOf<ReadValueId>()
                }, timeout.Token).ConfigureAwait(false);
                Check(response is ReadResponse && StatusCode.IsGood(response.ResponseHeader.ServiceResult), name);
            }

            async Task CheckFlowAsync(
                DataChannelContext opener, DataChannelContext acceptor, DataChannelDeliveryMode delivery,
                string protocol = DataChannelProfiles.UadpPayload)
            {
                string checkPrefix = protocol + " " + delivery;
                Task<DataChannel?> acceptingFlow = acceptor.AcceptAsync(timeout.Token).AsTask();
                DataChannelParametersDataType parameters = NativeFlowTestEnvironment.Parameters(delivery);
                parameters.Protocol = protocol;
                parameters.ContentType = protocol switch
                {
                    DataChannelProfiles.UadpAvApplicationPayload => "video/h264",
                    DataChannelProfiles.UadpOpaqueApplicationPayload => "application/octet-stream",
                    _ => "application/opcua+uadp"
                };
                await using DataChannel source = await opener.OpenAsync(
                    parameters, cancellationToken: timeout.Token).ConfigureAwait(false);
                await using DataChannel sink = await acceptingFlow.ConfigureAwait(false) ??
                    throw new InvalidOperationException("The native UASC flow closed before READY.");
                Check(source.State == DataChannelState.Open && sink.State == DataChannelState.Open,
                    checkPrefix + "ReadyBothRoles");
                byte[] bytes = protocol == DataChannelProfiles.UadpPayload
                    ? [0x91, 0x82, 0, 42, 0, 0, 0, 0x10, 0x20, 0, 0x40, 0xff]
                    : [0x91, 0x82, 0x0c, 42, 0, 0, 0, 0, 0xff, 0x01, 0x7f];
                bytes[3] = (byte)(ReferenceEquals(opener, clientContext) ? 42 : 17);
                ByteString payload = new(bytes);
                byte[] reverseBytes = [.. bytes];
                reverseBytes[3] = (byte)(ReferenceEquals(acceptor, clientContext) ? 42 : 17);
                ByteString reversePayload = new(reverseBytes);
                Check(await source.SendAsync(payload, cancellationToken: timeout.Token).ConfigureAwait(false) ==
                    DataChannelResult.Good, checkPrefix + "ForwardSend");
                using (DataChannelMessage? received = await sink.ReadAsync(timeout.Token).ConfigureAwait(false))
                {
                    Check(received != null && received.Payload == payload && received.Revision == 1,
                        checkPrefix + "ExactForwardBytesAndRevision");
                }
                Check(await sink.SendAsync(reversePayload, cancellationToken: timeout.Token).ConfigureAwait(false) ==
                    DataChannelResult.Good, checkPrefix + "ReverseSend");
                using (DataChannelMessage? received = await source.ReadAsync(timeout.Token).ConfigureAwait(false))
                {
                    Check(received != null && received.Payload == reversePayload && received.Revision == 1,
                        checkPrefix + "ExactReverseBytesAndRevision");
                }
                DataChannelParametersDataType revised = source.Parameters;
                revised.MaxMessageSize = 128;
                var peerUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnPeerUpdated(DataChannelStateChangedEventArgs notification)
                {
                    if (notification.Status.Revision == 2 && notification.Status.State == DataChannelState.Open)
                    {
                        peerUpdated.TrySetResult();
                    }
                }
                sink.StateChanged += OnPeerUpdated;
                try
                {
                    Check(await source.UpdateAsync(revised, cancellationToken: timeout.Token).ConfigureAwait(false) ==
                        DataChannelResult.Good, checkPrefix + "RevisionUpdate");
                    await peerUpdated.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                finally
                {
                    sink.StateChanged -= OnPeerUpdated;
                }
                Check(source.Revision == 2 && sink.Revision == 2 && sink.State == DataChannelState.Open,
                    checkPrefix + "PeerRevisionCompleted");
                Check(await source.SendAsync(payload, cancellationToken: timeout.Token).ConfigureAwait(false) ==
                    DataChannelResult.Good, checkPrefix + "RevisionTwoSend");
                using (DataChannelMessage? received = await sink.ReadAsync(timeout.Token).ConfigureAwait(false))
                {
                    Check(received != null && received.Payload == payload && received.Revision == 2,
                        checkPrefix + "ExactRevisionTwoBytes");
                }
                await CheckReadAsync(checkPrefix + "ServicesWhileFlowOpen").ConfigureAwait(false);
                ulong chargedBytes = (ulong)(payload.Length + (delivery == DataChannelDeliveryMode.Ordered ? 5 : 24));
                DataChannelDiagnosticsDataType sourceDiagnostics = source.GetDiagnostics();
                DataChannelDiagnosticsDataType sinkDiagnostics = sink.GetDiagnostics();
                Check(sourceDiagnostics.MessagesSent == 2 && sourceDiagnostics.BytesSent == 2 * chargedBytes &&
                    sourceDiagnostics.MessagesReceived == 1 && sourceDiagnostics.BytesReceived == chargedBytes &&
                    sinkDiagnostics.MessagesSent == 1 && sinkDiagnostics.BytesSent == chargedBytes &&
                    sinkDiagnostics.MessagesReceived == 2 && sinkDiagnostics.BytesReceived == 2 * chargedBytes,
                    checkPrefix + "CarrierChargedByteCounters");
                Check(await source.EndAsync(timeout.Token).ConfigureAwait(false) == DataChannelResult.Good,
                    checkPrefix + "ForwardEnd");
                using (DataChannelMessage? ended = await sink.ReadAsync(timeout.Token).ConfigureAwait(false))
                {
                    Check(ended == null, checkPrefix + "ForwardEndOfStream");
                }
                Check(await sink.EndAsync(timeout.Token).ConfigureAwait(false) == DataChannelResult.Good,
                    checkPrefix + "ReverseEnd");
                using (DataChannelMessage? ended = await source.ReadAsync(timeout.Token).ConfigureAwait(false))
                {
                    Check(ended == null, checkPrefix + "ReverseEndOfStream");
                }
            }

            void Check(bool condition, string name)
            {
                if (!condition)
                {
                    throw new InvalidOperationException("Native UASC check failed: " + name);
                }
                checks.Add(name);
            }
        }

        /// <summary>
        /// Copies the test private key into its native owner and clears the exported temporary.
        /// </summary>
        private static NativeQuicPrivateKey CreateKey(byte[] exported)
        {
            try
            {
                return new NativeQuicPrivateKey(exported);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(exported);
            }
        }

        /// <summary>
        /// Allows only explicitly configured UADP test resources, without an implicit grant.
        /// </summary>
        private sealed class ConfiguredAuthorizer : IDataChannelAuthorizer
        {
            /// <inheritdoc/>
            public ValueTask<DataChannelAuthorization> AuthorizeAsync(
                DataChannelAuthorizationRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(request.Mode == DataChannelAuthorizationMode.Configured &&
                    request.Parameters.ResourceUri == NativeFlowTestEnvironment.ResourceUri &&
                    request.Parameters.Protocol is DataChannelProfiles.UadpPayload or
                        DataChannelProfiles.UadpOpaqueApplicationPayload or DataChannelProfiles.UadpAvApplicationPayload
                        ? new DataChannelAuthorization(DataChannelResult.Good, request.Parameters)
                        : new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            }
        }

        /// <summary>
        /// Handles a real Read service and records authenticated OPN audit evidence without dynamic proxies.
        /// </summary>
        private sealed class ReadCallback(ByteString expectedClientCertificate) : ITransportListenerCallback
        {
            /// <summary>
            /// Gets the number of actual Read requests processed.
            /// </summary>
            internal int Reads => Volatile.Read(ref m_reads);

            /// <summary>
            /// Gets successful audited opens using the expected application certificate.
            /// </summary>
            internal int AuthenticatedOpens => Volatile.Read(ref m_authenticatedOpens);

            /// <summary>
            /// Gets unexpected certificate, open or close audit failures.
            /// </summary>
            internal int AuditErrors => Volatile.Read(ref m_auditErrors);

            /// <inheritdoc/>
            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext, IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(secureChannelContext);
                if (request is not ReadRequest)
                {
                    throw new InvalidOperationException("Expected a real Read request.");
                }
                Interlocked.Increment(ref m_reads);
                return ValueTask.FromResult<IServiceResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                });
            }

            /// <inheritdoc/>
            public bool TryGetSecureChannelIdForAuthenticationToken(NodeId authenticationToken, out uint channelId)
            {
                channelId = 0;
                return false;
            }

            /// <inheritdoc/>
            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId, EndpointDescription endpointDescription, OpenSecureChannelRequest request,
                Certificate clientCertificate, Exception exception)
            {
                if (exception == null && request.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                    clientCertificate != null && new ByteString(clientCertificate.RawData) == expectedClientCertificate)
                {
                    Interlocked.Increment(ref m_authenticatedOpens);
                }
                else
                {
                    Interlocked.Increment(ref m_auditErrors);
                }
            }

            /// <inheritdoc/>
            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
                if (exception != null)
                {
                    Interlocked.Increment(ref m_auditErrors);
                }
            }

            /// <inheritdoc/>
            public void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
                Interlocked.Increment(ref m_auditErrors);
            }

            /// <summary>Actual service request count.</summary>
            private int m_reads;
            /// <summary>Authenticated open audit count.</summary>
            private int m_authenticatedOpens;
            /// <summary>Unexpected audit failure count.</summary>
            private int m_auditErrors;
        }
    }
}
