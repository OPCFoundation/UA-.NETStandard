// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Exercises secured UASC Services and bidirectional DATA over managed capsule HTTP/3.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    [TestFixture]
    [NonParallelizable]
    public sealed class ManagedWebTransportUascServiceLoopbackTests
    {
        /// <summary>
        /// Requires mutual HELLO, then exchanges Services and bidirectional DATA after flow admission.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task ManagedCapsuleSession_ConfirmsContextsAndExchangesSecuredUascServiceAsync()
        {
            if (!QuicConnection.IsSupported || !QuicListener.IsSupported)
            {
                Assert.Ignore("The .NET QUIC runtime is unavailable on this platform.");
            }

            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            using Certificate serverCertificate = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:opcfoundation:tests:managed-webtransport-uasc-server",
                    "Managed WebTransport UASC Server",
                    "CN=127.0.0.1",
                    ["127.0.0.1"])
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.AddHours(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate clientCertificate = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:opcfoundation:tests:managed-webtransport-uasc-client",
                    "Managed WebTransport UASC Client",
                    "CN=managed-client",
                    ["managed-client"])
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetNotAfter(DateTime.UtcNow.AddHours(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using var serverChain = new CertificateCollection();
            using var clientChain = new CertificateCollection();
            using X509Certificate2 tlsCertificate = serverCertificate.AsX509Certificate2();

            const string securityPolicy = SecurityPolicies.Basic256Sha256;
            const int operationTimeout = 10000;
            const int bufferSize = 65536;
            string expectedTlsThumbprint = tlsCertificate.GetCertHashString(HashAlgorithmName.SHA256);
            var h3 = new SslApplicationProtocol("h3");
            var serverTls = new SslServerAuthenticationOptions
            {
                ApplicationProtocols = [h3],
                ServerCertificate = tlsCertificate
            };
            var clientTls = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [h3],
                RemoteCertificateValidationCallback = (_, peerCertificate, _, _) =>
                    string.Equals(
                        peerCertificate?.GetCertHashString(HashAlgorithmName.SHA256),
                        expectedTlsThumbprint,
                        StringComparison.OrdinalIgnoreCase),
                TargetHost = IPAddress.Loopback.ToString()
            };
            var endpointUrl = new Uri("https://127.0.0.1:0/ua/wt");
            var endpoint = new EndpointDescription
            {
                EndpointUrl = endpointUrl.AbsoluteUri,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = securityPolicy,
                ServerCertificate = serverCertificate.RawData.ToByteString(),
                TransportProfileUri = DataChannelProfiles.UascWebTransport,
                SecurityLevel = 1,
                Server = new ApplicationDescription
                {
                    ApplicationUri = "urn:opcfoundation:tests:managed-webtransport-uasc-server",
                    ApplicationName = new LocalizedText("Managed WebTransport UASC Server"),
                    ApplicationType = ApplicationType.Server
                },
                UserIdentityTokens = []
            };
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = operationTimeout;
            configuration.MaxMessageSize = bufferSize;
            configuration.MaxBufferSize = bufferSize;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;

            var certificateRegistry = new Mock<ICertificateRegistry>();
            certificateRegistry.SetupGet(registry => registry.SendCertificateChain).Returns(false);
            certificateRegistry
                .Setup(registry => registry.AcquireApplicationCertificateBySecurityPolicy(securityPolicy))
                .Returns(() => new CertificateEntry(
                    serverCertificate,
                    serverChain,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType));
            var validator = new Mock<ICertificateValidatorEx>();
            validator
                .Setup(value => value.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(CertificateValidationResult.Success));
            validator
                .Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<Opc.Ua.Security.Certificates.CertificateValidationOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(CertificateValidationResult.Success));

            var dataChannelAuthorizer = new Mock<IDataChannelAuthorizer>();
            dataChannelAuthorizer
                .Setup(authorizer => authorizer.AuthorizeAsync(
                    It.IsAny<DataChannelAuthorizationRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((DataChannelAuthorizationRequest request, CancellationToken _) =>
                    ValueTask.FromResult(new DataChannelAuthorization(
                        DataChannelResult.Good, request.Parameters)));
            var dataChannelOptions = new DataChannelOptions
            {
                HelloTimeout = TimeSpan.FromSeconds(1)
            };
            var clientBudget = new DataChannelApplicationBudget();
            var serverBudget = new DataChannelApplicationBudget();
            var clientDataChannels = new DataChannelContextFactory(
                dataChannelAuthorizer.Object,
                dataChannelOptions,
                clientBudget,
                TimeProvider.System,
                telemetry);
            var serverDataChannels = new DataChannelContextFactory(
                dataChannelAuthorizer.Object,
                dataChannelOptions,
                serverBudget,
                TimeProvider.System,
                telemetry);

            int requestCount = 0;
            var callback = new Mock<ITransportListenerCallback>();
            callback
                .Setup(value => value.ProcessRequestAsync(
                    It.IsAny<SecureChannelContext>(),
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((SecureChannelContext _, IServiceRequest request, CancellationToken _) =>
                {
                    Interlocked.Increment(ref requestCount);
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            RequestHandle = request.RequestHeader.RequestHandle,
                            ServiceResult = StatusCodes.Good
                        },
                        Results = []
                    });
                });

            var listenerSettings = new TransportListenerSettings
            {
                Descriptions = [endpoint],
                Configuration = configuration,
                ServerCertificates = certificateRegistry.Object,
                CertificateValidator = validator.Object,
                DataChannels = serverDataChannels,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create(),
                MaxChannelCount = 4
            };

            var webTransportServer = new ManagedWebTransportUascServer();
            var managedTransport = new ManagedWebTransportUascTransport();
            var legacyEndpoint = new Uri("opc.wt://127.0.0.1:4840/ua/wt");
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await managedTransport.ConnectAsync(legacyEndpoint, clientTls).ConfigureAwait(false))
                .ConfigureAwait(false);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await webTransportServer.ListenAsync(
                    legacyEndpoint,
                    new IPEndPoint(IPAddress.Loopback, 0),
                    serverTls,
                    static (_, _) => ValueTask.FromResult(true))
                    .ConfigureAwait(false))
                .ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await using ManagedWebTransportUascListener originRestrictedListener =
                await webTransportServer.ListenAsync(
                    new Uri("https://127.0.0.1:0/restricted"),
                    new IPEndPoint(IPAddress.Loopback, 0),
                    serverTls,
                    static (_, _) => ValueTask.FromResult(true),
                    WebTransportOriginPolicy.RequiredExact,
                    "https://allowed.example")
                    .ConfigureAwait(false);
            Task<ManagedWebTransportUascConnection> originRejectedAccept =
                originRestrictedListener.AcceptAsync(timeout.Token).AsTask();
            await Assert.ThrowsAsync<System.IO.InvalidDataException>(async () =>
                await managedTransport.ConnectAsync(
                    originRestrictedListener.EndpointUrl, clientTls, timeout.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(async () =>
                await originRejectedAccept.ConfigureAwait(false)).ConfigureAwait(false);

            Uri? deniedAuthorizationEndpoint = null;
            await using (ManagedWebTransportUascListener deniedListener =
                await webTransportServer.ListenAsync(
                    new Uri("https://127.0.0.1:0/denied"),
                    new IPEndPoint(IPAddress.Loopback, 0),
                    serverTls,
                    (uri, _) =>
                    {
                        deniedAuthorizationEndpoint = uri;
                        return ValueTask.FromResult(false);
                    })
                    .ConfigureAwait(false))
            {
                Task<ManagedWebTransportUascConnection> deniedAccept =
                    deniedListener.AcceptAsync(timeout.Token).AsTask();
                await Assert.ThrowsAsync<System.IO.InvalidDataException>(async () =>
                    await managedTransport.ConnectAsync(
                        deniedListener.EndpointUrl, clientTls, timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(async () =>
                    await deniedAccept.ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(deniedAuthorizationEndpoint, Is.EqualTo(deniedListener.EndpointUrl));
            }

            await using (ManagedWebTransportUascListener oneSidedListener =
                await webTransportServer.ListenAsync(
                    new Uri("https://127.0.0.1:0/one-sided"),
                    new IPEndPoint(IPAddress.Loopback, 0),
                    serverTls,
                    static (_, _) => ValueTask.FromResult(true))
                    .ConfigureAwait(false))
            {
                endpoint.EndpointUrl = oneSidedListener.EndpointUrl.AbsoluteUri;
                listenerSettings.Descriptions = [endpoint];
                listenerSettings.DataChannels = null;
                await using var oneSidedUascListener = new TcpTransportListener(telemetry);
                oneSidedUascListener.OpenExternal(
                    oneSidedListener.EndpointUrl, listenerSettings, callback.Object);

                Task<ManagedWebTransportUascConnection> oneSidedAcceptTask =
                    oneSidedListener.AcceptAsync(timeout.Token).AsTask();
                await using ManagedWebTransportUascConnection oneSidedClientConnection =
                    await managedTransport.ConnectAsync(
                        oneSidedListener.EndpointUrl, clientTls, timeout.Token).ConfigureAwait(false);
                await using ManagedWebTransportUascConnection oneSidedServerConnection =
                    await oneSidedAcceptTask.ConfigureAwait(false);
                _ = oneSidedUascListener.AcceptTransport(
                    oneSidedServerConnection, validator.Object, bufferSize);
                using var oneSidedChannel =
                    (UaSCUaBinaryTransportChannel)oneSidedClientConnection.Create(telemetry);

                Task<DataChannelException?> failedOpen = Assert.ThrowsAsync<DataChannelException>(async () =>
                    await oneSidedChannel.OpenAsync(
                        oneSidedListener.EndpointUrl,
                        new TransportChannelSettings
                        {
                            Description = endpoint,
                            Configuration = configuration,
                            ClientCertificate = clientCertificate.AddRef(),
                            ClientCertificateChain = clientChain.AddRef(),
                            ServerCertificate = serverCertificate.AddRef(),
                            CertificateValidator = validator.Object,
                            DataChannels = clientDataChannels,
                            NamespaceUris = new NamespaceTable(),
                            Factory = EncodeableFactory.Create()
                        },
                        timeout.Token).ConfigureAwait(false));
                DataChannelException timeoutException = (await failedOpen.ConfigureAwait(false))!;
                Assert.That(timeoutException.Result, Is.EqualTo(DataChannelResult.Timeout));
                await oneSidedUascListener.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
            DataChannelResourceAssertions.AreReleased(clientBudget.Usage);

            await using ManagedWebTransportUascListener webTransportListener =
                await webTransportServer.ListenAsync(
                    endpointUrl,
                    new IPEndPoint(IPAddress.Loopback, 0),
                    serverTls,
                    static (_, _) => ValueTask.FromResult(true))
                    .ConfigureAwait(false);
            listenerSettings.DataChannels = serverDataChannels;
            endpoint.EndpointUrl = webTransportListener.EndpointUrl.AbsoluteUri;
            listenerSettings.Descriptions = [endpoint];

            await using var uascListener = new TcpTransportListener(telemetry);
            uascListener.OpenExternal(webTransportListener.EndpointUrl, listenerSettings, callback.Object);

            Task<ManagedWebTransportUascConnection> acceptTask =
                webTransportListener.AcceptAsync(timeout.Token).AsTask();
            await using ManagedWebTransportUascConnection clientConnection =
                await managedTransport.ConnectAsync(webTransportListener.EndpointUrl, clientTls, timeout.Token)
                    .ConfigureAwait(false);
            await using ManagedWebTransportUascConnection serverConnection =
                await acceptTask.ConfigureAwait(false);
            TcpListenerChannel acceptedChannel =
                uascListener.AcceptTransport(serverConnection, validator.Object, bufferSize);

            using var clientChannel = (UaSCUaBinaryTransportChannel)clientConnection.Create(telemetry);
            await clientChannel.OpenAsync(
                webTransportListener.EndpointUrl,
                new TransportChannelSettings
                {
                    Description = endpoint,
                    Configuration = configuration,
                    ClientCertificate = clientCertificate.AddRef(),
                    ClientCertificateChain = clientChain.AddRef(),
                    ServerCertificate = serverCertificate.AddRef(),
                    CertificateValidator = validator.Object,
                    DataChannels = clientDataChannels,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create()
                },
                timeout.Token).ConfigureAwait(false);

            IServiceResponse response = await clientChannel.SendRequestAsync(
                new ReadRequest
                {
                    RequestHeader = new RequestHeader { TimeoutHint = operationTimeout },
                    NodesToRead = []
                },
                timeout.Token).ConfigureAwait(false);

            DataChannelContext clientContext =
                ((IDataChannelTransportChannel)clientChannel).DataChannels ??
                throw new InvalidOperationException("The client data context was not attached.");
            DataChannelContext serverContext = acceptedChannel.DataChannels ??
                throw new InvalidOperationException("The server data context was not attached.");
            using IDisposable clientRoute = clientContext.RegisterUascPublisherRoute(
                "urn:opcfoundation:tests:managed-webtransport-uasc-data", new Variant(42u));
            using IDisposable serverRoute = serverContext.RegisterUascPublisherRoute(
                "urn:opcfoundation:tests:managed-webtransport-uasc-data", new Variant(42u));
            Task<DataChannel?> acceptingFlow = serverContext.AcceptAsync(timeout.Token).AsTask();
            await using (DataChannel clientFlow = await clientContext.OpenAsync(
                new DataChannelParametersDataType
                {
                    ResourceUri = "urn:opcfoundation:tests:managed-webtransport-uasc-data",
                    Protocol = DataChannelProfiles.UadpPayload,
                    ContentType = "application/opcua+uadp",
                    Direction = DataChannelDirection.Duplex,
                    Delivery = DataChannelDeliveryMode.Ordered,
                    Protection = DataChannelProtectionMode.Confidential,
                    MaxMessageSize = 4096,
                    MaxReceiveBytes = 8192,
                    Priority = 128
                },
                cancellationToken: timeout.Token).ConfigureAwait(false))
            await using (DataChannel serverFlow = await acceptingFlow.ConfigureAwait(false) ??
                throw new InvalidOperationException("The server context closed before accepting the flow."))
            {
                var payload = new ByteString(
                    new byte[] { 0x91, 0x82, 0, 42, 0, 0, 0, 0x10, 0x20, 0, 0x40, 0xff });
                Assert.That(
                    await clientFlow.SendAsync(payload, cancellationToken: timeout.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                using DataChannelMessage received =
                    await serverFlow.ReadAsync(timeout.Token).ConfigureAwait(false) ??
                    throw new InvalidOperationException("The server flow ended before receiving DATA.");
                Assert.That(received.Payload, Is.EqualTo(payload));
                Assert.That(
                    await serverFlow.SendAsync(payload, cancellationToken: timeout.Token).ConfigureAwait(false),
                    Is.EqualTo(DataChannelResult.Good));
                using DataChannelMessage reply =
                    await clientFlow.ReadAsync(timeout.Token).ConfigureAwait(false) ??
                    throw new InvalidOperationException("The client flow ended before receiving reverse DATA.");
                Assert.That(reply.Payload, Is.EqualTo(payload));
            }
            Assert.Multiple(() =>
            {
                Assert.That(response, Is.InstanceOf<ReadResponse>());
                Assert.That(((ReadResponse)response).ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(requestCount, Is.EqualTo(1));
                Assert.That(clientChannel.ClientChannelCertificate, Is.EqualTo(clientCertificate.RawData));
                Assert.That(clientChannel.ServerChannelCertificate, Is.EqualTo(serverCertificate.RawData));
                Assert.That(clientChannel.SupportedFeatures, Is.Not.EqualTo(TransportChannelFeatures.None));
                Assert.That(clientContext.IsReady, Is.True);
                Assert.That(serverContext.IsReady, Is.True);
                Assert.That(clientContext.CarrierKind, Is.EqualTo(DataChannelCarrierKind.WebTransportUasc));
                Assert.That(serverContext.CarrierKind, Is.EqualTo(DataChannelCarrierKind.WebTransportUasc));
            });

            await clientChannel.CloseAsync(timeout.Token).ConfigureAwait(false);
            await uascListener.CloseAsync(timeout.Token).ConfigureAwait(false);
            DataChannelResourceAssertions.AreReleased(clientBudget.Usage);
            DataChannelResourceAssertions.AreReleased(serverBudget.Usage);
        }
    }
}
#endif
