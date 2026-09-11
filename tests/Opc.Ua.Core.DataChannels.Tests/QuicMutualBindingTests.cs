#if NET9_0_OR_GREATER
/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    [Category("Quic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class QuicMutualBindingTests
    {
        [SetUp]
        public void SetUp()
        {
            QuicTestSupport.SkipUnlessAvailable();

            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager(
                nameof(QuicMutualBindingTests),
                TcpMessageLimits.DefaultMaxBufferSize,
                m_telemetry);
            m_serverCertificate = CreateCertificate("QuicMutualServer");
            m_clientCertificate = CreateCertificate("QuicMutualClient");
            m_serverRegistry = new InMemoryCertificateRegistry(m_serverCertificate);
            m_callback = new EchoCallback();
        }

        [TearDown]
        public void TearDown()
        {
            m_serverRegistry?.Dispose();
            m_serverCertificate?.Dispose();
            m_clientCertificate?.Dispose();
        }

        [Test]
        [CancelAfter(30000)]
        public async Task ClientWithoutTlsCertificateCompletesTheConnectionSoDiscoveryStaysReachableAsync()
        {
            await using QuicTransportListener listener = await OpenListenerAsync("NoClientCertificate")
                .ConfigureAwait(false);

            var options = new QuicClientConnectionOptions
            {
                RemoteEndPoint = new DnsEndPoint("localhost", listener.EndpointUrl.Port),
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                    TargetHost = "localhost",
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                },
                DefaultStreamErrorCode = 0x0A,
                DefaultCloseErrorCode = 0x0B
            };

            // §7.6.1 obliges the TLS server to *request* a client
            // certificate, and separately forbids accepting OpenDataChannel
            // on a connection that completed without one. Those are two
            // rules, not one: the Discovery Services run on a SecurityPolicy
            // None channel that has no certificate to present, so refusing
            // the handshake here would make GetEndpoints unreachable over
            // opc.quic. The refusal belongs at OpenDataChannel, which the
            // test below covers.
            await using QuicConnection connection = await QuicConnection
                .ConnectAsync(options, TimeoutToken())
                .ConfigureAwait(false);

            Assert.That(
                connection.RemoteCertificate,
                Is.Not.Null,
                "The Server still authenticates itself to the Client.");
        }

        [Test]
        public void OpenDataChannelIsRefusedOnAConnectionWithoutATlsClientCertificate()
        {
            var transport = new QuicMultiplexedTransport(
                m_bufferManager!,
                TcpMessageLimits.DefaultMaxMessageSize,
                m_telemetry!,
                new QuicClientOptions());

            const string secureChannelId = "quic-no-client-certificate";
            QuicServerDataChannelTransport.BindSecureChannel(secureChannelId, transport);

            try
            {
                var serverTransport = new QuicServerDataChannelTransport();
                var context = new SecureChannelContext(
                    secureChannelId,
                    new EndpointDescription
                    {
                        TransportProfileUri = Profiles.UaQuicTransport,
                        SecurityMode = MessageSecurityMode.SignAndEncrypt
                    },
                    RequestEncoding.Binary);

                ServiceResultException? exception = Assert.Throws<ServiceResultException>(
                    () => serverTransport.TryGetManager(
                        context,
                        new DataChannelServerCapabilities(),
                        m_telemetry!,
                        out _,
                        out _,
                        out _));

                Assert.Multiple(() =>
                {
                    Assert.That(
                        exception!.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadSecurityChecksFailed));

                    // Falling back to a Service-only transport instead of
                    // refusing would be a silent downgrade, which is exactly
                    // what the binding exists to prevent.
                    Assert.That(
                        exception.Message,
                        Does.Contain("TLS client certificate"));
                });
            }
            finally
            {
                QuicServerDataChannelTransport.UnbindSecureChannel(secureChannelId, transport);
            }
        }

        [Test]
        [CancelAfter(30000)]
        public async Task TlsClientKeyDifferentFromOpenSecureChannelKeyIsRefusedAsync()
        {
            await using QuicTransportListener listener = await OpenListenerAsync("ClientKeyMismatch")
                .ConfigureAwait(false);
            using Certificate tlsCertificate = CreateCertificate("QuicMutualDifferentTlsKey");
            using X509Certificate2 tlsX509 = tlsCertificate.AsX509Certificate2();
            using QuicTransportChannel channel = CreateClientChannel(tlsX509);

            EndpointDescription endpoint = CreateEndpoint(listener.EndpointUrl);
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel
                    .OpenAsync(
                        listener.EndpointUrl,
                        CreateChannelSettings(endpoint),
                        TimeoutToken())
                    .ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null);
                Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
                Assert.That(
                    QuicPeerBinding.ToStatusCode(QuicPeerBindingResult.SecureChannelKeyMismatch),
                    Is.EqualTo((StatusCode)StatusCodes.BadSecurityChecksFailed));
                Assert.That(GetChannels(listener), Is.Empty);
            });
        }

        [Test]
        [CancelAfter(30000)]
        public async Task MatchingTlsClientKeyConnectsAndCanUseAQuicStreamAsync()
        {
            await using QuicTransportListener listener = await OpenListenerAsync("HonestClient")
                .ConfigureAwait(false);
            using QuicTransportChannel channel = CreateClientChannel();
            var statusSource = new TaskCompletionSource<ConnectionStatusEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            listener.ConnectionStatusChanged += (_, e) =>
            {
                if (!e.Closed)
                {
                    statusSource.TrySetResult(e);
                }
            };

            EndpointDescription endpoint = CreateEndpoint(listener.EndpointUrl);
            await channel
                .OpenAsync(listener.EndpointUrl, CreateChannelSettings(endpoint), TimeoutToken())
                .ConfigureAwait(false);
            await WithTimeoutAsync(statusSource.Task).ConfigureAwait(false);

            IMultiplexedByteTransport client = GetMultiplexedTransport(channel.Transport!);
            IMultiplexedByteTransport server = GetServerTransport(listener);
            ulong streamId = await client.OpenStreamAsync(bidirectional: true, TimeoutToken())
                .ConfigureAwait(false);
            byte[] frame = [0x44, 0x43, 0x46, 0x46, 0x0B, 0x00, 0x00, 0x00, 0x41, 0x42, 0x43];

            await client.SendOnStreamAsync(streamId, frame, TimeoutToken()).ConfigureAwait(false);
            ulong accepted = await server.AcceptStreamAsync(TimeoutToken()).ConfigureAwait(false);
            ArraySegment<byte> received = await server.ReceiveOnStreamAsync(accepted, TimeoutToken())
                .ConfigureAwait(false);

            Assert.That(received.AsSpan().SequenceEqual(frame), Is.True);
        }

        private async Task<QuicTransportListener> OpenListenerAsync(string path)
        {
            var listener = new QuicTransportListener(m_telemetry!);
            try
            {
                var endpointUrl = new Uri($"opc.quic://localhost:0/{path}");
                await listener
                    .OpenAsync(
                        endpointUrl,
                        CreateListenerSettings(endpointUrl),
                        m_callback!,
                        TimeoutToken())
                    .ConfigureAwait(false);
                Assert.That(listener.EndpointUrl.Port, Is.Not.Zero);
                return listener;
            }
            catch
            {
                await listener.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private QuicTransportChannel CreateClientChannel(X509Certificate2? tlsCertificate = null)
        {
            return new QuicTransportChannel(
                m_telemetry!,
                DefaultBufferManagerFactory.Instance,
                new QuicClientOptions
                {
                    ClientCertificate = tlsCertificate,
                    HandshakeTimeout = TimeSpan.FromSeconds(10),
                    ServerCertificateValidation = (_, _, _, _) => true
                })
            {
                OperationTimeout = 5000
            };
        }

        private TransportListenerSettings CreateListenerSettings(Uri endpointUrl)
        {
            EndpointDescription endpoint = CreateEndpoint(endpointUrl);
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;
            configuration.MaxMessageSize = 64 * 1024;
            configuration.MaxBufferSize = 64 * 1024;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;

            return new TransportListenerSettings
            {
                Descriptions = [endpoint],
                Configuration = configuration,
                ServerCertificates = m_serverRegistry,
                CertificateValidator = new AcceptAllCertificateValidator(),
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create(),
                MaxChannelCount = 10
            };
        }

        private TransportChannelSettings CreateChannelSettings(EndpointDescription endpoint)
        {
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;
            configuration.MaxMessageSize = 64 * 1024;
            configuration.MaxBufferSize = 64 * 1024;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;

            return new TransportChannelSettings
            {
                Description = endpoint,
                Configuration = configuration,
                ClientCertificate = m_clientCertificate!.AddRef(),
                ClientCertificateChain = new CertificateCollection(),
                ServerCertificate = m_serverCertificate!.AddRef(),
                CertificateValidator = new AcceptAllCertificateValidator(),
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create()
            };
        }

        private EndpointDescription CreateEndpoint(Uri endpointUrl)
        {
            return new EndpointDescription
            {
                EndpointUrl = endpointUrl.ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = Profiles.UaQuicTransport,
                ServerCertificate = m_serverCertificate!.RawData.ToByteString(),
                Server = new ApplicationDescription
                {
                    ApplicationName = new LocalizedText("Opc.Ua.Core.DataChannels.Tests"),
                    ApplicationType = ApplicationType.Server,
                    ApplicationUri = "urn:localhost:Opc.Ua.Core.DataChannels.Tests",
                    ProductUri = "urn:opcfoundation.org:Opc.Ua.Core.DataChannels.Tests"
                },
                UserIdentityTokens = new ArrayOf<UserTokenPolicy>()
            };
        }

        private static IMultiplexedByteTransport GetMultiplexedTransport(object transport)
        {
            if (transport is QuicPeerBindingTransport bound)
            {
                return bound.Inner;
            }

            return (IMultiplexedByteTransport)transport;
        }

        private static IMultiplexedByteTransport GetServerTransport(QuicTransportListener listener)
        {
            object channel = GetChannels(listener)[0];
            object transport = channel
                .GetType()
                .GetProperty("Transport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(channel)!;
            return GetMultiplexedTransport(transport);
        }

        private static List<object> GetChannels(QuicTransportListener listener)
        {
            FieldInfo field = typeof(QuicTransportListener).GetField(
                "m_channels",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var channels = new List<object>();

            foreach (object entry in (IEnumerable)field.GetValue(listener)!)
            {
                channels.Add(entry.GetType().GetProperty("Value")!.GetValue(entry)!);
            }

            return channels;
        }

        private static async Task WithTimeoutAsync(Task task)
        {
            Task completed = await Task
                .WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)))
                .ConfigureAwait(false);
            if (completed != task)
            {
                Assert.Fail("Timed out waiting for QUIC listener operation.");
            }

            await task.ConfigureAwait(false);
        }

        private static async Task<Exception> WaitForConnectionOperationFailureAsync(
            QuicConnection connection)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (!timeout.IsCancellationRequested)
            {
                Exception? exception = Assert.CatchAsync(
                    async () =>
                    {
                        await using QuicStream stream = await connection
                            .OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TimeoutToken())
                            .ConfigureAwait(false);
                    });
                if (exception != null)
                {
                    return exception;
                }

                await Task.Delay(25, CancellationToken.None).ConfigureAwait(false);
            }

            Assert.Fail("Timed out waiting for rejected QUIC connection to close.");
            throw new InvalidOperationException();
        }

        private static CancellationToken TimeoutToken()
        {
            return new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
        }

        private static Certificate CreateCertificate(string commonName)
        {
            return DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    $"urn:localhost:{commonName}",
                    commonName,
                    $"CN={commonName}",
                    ["localhost"])
                .SetLifeTime(TimeSpan.FromDays(1))
                .CreateForRSA();
        }

        private ITelemetryContext? m_telemetry;
        private BufferManager? m_bufferManager;
        private Certificate? m_serverCertificate;
        private Certificate? m_clientCertificate;
        private InMemoryCertificateRegistry? m_serverRegistry;
        private EchoCallback? m_callback;

        private sealed class EchoCallback : ITransportListenerCallback
        {
            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IServiceResponse>(
                    new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                            ServiceResult = StatusCodes.Good
                        }
                    });
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(
                NodeId authenticationToken,
                out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
            }
        }

        private sealed class InMemoryCertificateRegistry : ICertificateRegistry, IDisposable
        {
            private readonly CertificateEntry m_entry;
            private readonly CertificateEntry[] m_entries;

            public InMemoryCertificateRegistry(Certificate certificate)
            {
                using var issuerChain = new CertificateCollection();
                m_entry = new CertificateEntry(
                    certificate,
                    issuerChain,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType);
                m_entries = [m_entry];
            }

            public bool SendCertificateChain => false;

            public CertificateEntryCollection SnapshotApplicationCertificates()
            {
                return new CertificateEntryCollection(m_entries);
            }

            public CertificateEntry? AcquireApplicationCertificateByType(NodeId certificateType)
            {
                _ = certificateType;
                return m_entry.AddRef();
            }

            public CertificateEntry? AcquireApplicationCertificateBySecurityPolicy(string securityPolicy)
            {
                _ = securityPolicy;
                return m_entry.AddRef();
            }

            public Task<bool> GetIssuersAsync(
                Certificate certificate,
                IList<CertificateIssuerReference> issuers,
                CancellationToken ct = default)
            {
                _ = certificate;
                _ = issuers;
                _ = ct;
                return Task.FromResult(false);
            }

            public void Dispose()
            {
                m_entry.Dispose();
            }
        }

        private sealed class AcceptAllCertificateValidator : ICertificateValidatorEx
        {
            public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }

            public Task<CertificateValidationResult> ValidateAsync(
                CertificateCollection chain,
                TrustListIdentifier? trustList = null,
                Security.Certificates.CertificateValidationOptions? options = null,
                CancellationToken ct = default)
            {
                _ = chain;
                _ = trustList;
                _ = options;
                _ = ct;
                return Task.FromResult(Accepted());
            }

            public Task<CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                _ = certificate;
                _ = trustList;
                _ = ct;
                return Task.FromResult(Accepted());
            }

            private static CertificateValidationResult Accepted()
            {
                return new CertificateValidationResult(true, StatusCodes.Good, [], false);
            }
        }
    }
}
#endif
