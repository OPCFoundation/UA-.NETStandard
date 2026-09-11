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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
    public sealed class QuicListenerLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            QuicTestSupport.SkipUnlessAvailable();

            m_telemetry = NUnitTelemetryContext.Create();
            m_serverCertificate = CreateCertificate("QuicLifecycleServer");
            m_clientCertificate = CreateCertificate("QuicLifecycleClient");
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
        public async Task CertificateActivationClosesSupersededKeyWithSecurityErrorAsync()
        {
            await using QuicTransportListener listener = await OpenListenerAsync("RotationClose")
                .ConfigureAwait(false);
            using var channel = CreateClientChannel();

            await ConnectClientAndWaitForStatusAsync(listener, channel, secure: true)
                .ConfigureAwait(false);
            QuicConnection connection = GetClientConnection(channel);
            await using QuicStream observedStream = await connection
                .OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TimeoutToken())
                .ConfigureAwait(false);

            using Certificate replacement = CreateCertificate("QuicLifecycleServerRotated");
            using var rotated = new InMemoryCertificateRegistry(replacement);
            listener.CertificateUpdate(new AcceptAllCertificateValidator(), rotated);

            await WaitUntilAsync(() => GetChannels(listener).Count == 0, "rotation did not close channel")
                .ConfigureAwait(false);
            Exception exception = await StreamOperationFailsAsync(observedStream).ConfigureAwait(false);

            Assert.That(
                ApplicationErrorCode(exception),
                Is.EqualTo((long)StatusCodes.BadSecurityChecksFailed.Code));
        }

        [Test]
        [CancelAfter(30000)]
        public async Task OwnCertificateInvalidationClosesConnectionWithSecurityErrorAsync()
        {
            await using QuicTransportListener listener = await OpenListenerAsync("OwnInvalid")
                .ConfigureAwait(false);
            using var channel = CreateClientChannel();

            await ConnectClientAndWaitForStatusAsync(listener, channel, secure: true)
                .ConfigureAwait(false);
            string globalChannelId = GetSingleChannel(listener).GlobalChannelId;

            IReadOnlyList<string> closed = await listener
                .CloseChannelsForOwnCertificateAsync((_, _) => new ValueTask<bool>(false), TimeoutToken())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(closed, Is.EqualTo(new[] { globalChannelId }));
                Assert.That(GetChannels(listener), Is.Empty);
            });
        }

        [Test]
        [CancelAfter(30000)]
        public async Task HandshakeStartedBeforeActivationIsNotAdmittedAsync()
        {
            var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using QuicTransportListener listener = await OpenListenerAsync(
                "EpochFence",
                (connection, epoch) =>
                {
                    callbackEntered.TrySetResult();
                    return new ValueTask(releaseCallback.Task);
                }).ConfigureAwait(false);

            using X509Certificate2 clientTlsCertificate = m_clientCertificate!.AsX509Certificate2();
            Task<QuicConnection> connecting = QuicConnection.ConnectAsync(
                new QuicClientConnectionOptions
                {
                    RemoteEndPoint = new DnsEndPoint(
                        "localhost",
                        listener.EndpointUrl.Port),
                    ClientAuthenticationOptions = new SslClientAuthenticationOptions
                    {
                        ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                        TargetHost = "localhost",
                        RemoteCertificateValidationCallback = (_, _, _, _) => true,
                        ClientCertificates = [clientTlsCertificate]
                    },
                    DefaultStreamErrorCode = 0x0A,
                    DefaultCloseErrorCode = 0x0B
                },
                TimeoutToken()).AsTask();

            await WithTimeoutAsync(callbackEntered.Task).ConfigureAwait(false);
            using Certificate replacement = CreateCertificate("QuicLifecycleEpochReplacement");
            using var rotated = new InMemoryCertificateRegistry(replacement);
            listener.CertificateUpdate(new AcceptAllCertificateValidator(), rotated);
            releaseCallback.SetResult();

            await using QuicConnection connection = await WithTimeoutAsync(connecting).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(GetChannels(listener), Is.Empty);
            });
        }

        [Test]
        [CancelAfter(30000)]
        public async Task SameKeyCertificateActivationDoesNotCloseConnectionAsync()
        {
            await using QuicTransportListener listener = await OpenListenerAsync("SameKeyActivation")
                .ConfigureAwait(false);
            using var channel = CreateClientChannel();

            await ConnectClientAndWaitForStatusAsync(listener, channel, secure: true)
                .ConfigureAwait(false);
            string globalChannelId = GetSingleChannel(listener).GlobalChannelId;

            using Certificate reissue = CreateSameKeyReissue(m_serverCertificate!);
            using var registry = new InMemoryCertificateRegistry(reissue);
            listener.CertificateUpdate(new AcceptAllCertificateValidator(), registry);
            await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(GetChannels(listener), Has.Count.EqualTo(1));
                Assert.That(GetSingleChannel(listener).GlobalChannelId, Is.EqualTo(globalChannelId));
            });
        }

        [Test]
        [CancelAfter(30000)]
        public async Task ReverseConnectRejectsPeerWhoseTlsKeyDiffersFromOpenSecureChannelAsync()
        {
            using Certificate reversePeerTls = CreateCertificate("QuicLifecycleReversePeerTls");
            using Certificate openSecureChannelPeer = CreateCertificate("QuicLifecycleReversePeerOpcUa");
            var bufferManager = new BufferManager("reverse-peer-binding", 65536, m_telemetry!);
            await using QuicLoopback loopback = await QuicLoopback
                .StartReverseAsync(
                    reversePeerTls.AsX509Certificate2(),
                    bufferManager,
                    m_telemetry!)
                .ConfigureAwait(false);
            var boundTransport = new QuicPeerBindingTransport(
                loopback.Client,
                bufferManager,
                endpointDescription: null,
                bindToOpenSecureChannelOnly: true);

            await loopback.Server
                .SendChunkAsync(
                    BuildOpenSecureChannelChunk(openSecureChannelPeer.AsX509Certificate2()),
                    TimeoutToken())
                .ConfigureAwait(false);

            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await boundTransport.ReceiveChunkAsync(TimeoutToken()).ConfigureAwait(false));

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        private async Task<QuicTransportListener> OpenListenerAsync(
            string path,
            Func<QuicConnection, long, ValueTask>? admissionPause = null)
        {
            var listener = new QuicTransportListener(m_telemetry!);
            listener.AdmissionCallbackPauseForTesting = admissionPause;
            try
            {
                var endpointUrl = new Uri($"opc.quic://localhost:0/{path}");
                await listener.OpenAsync(
                    endpointUrl,
                    CreateListenerSettings(endpointUrl, m_serverRegistry!),
                    m_callback!,
                    TimeoutToken()).ConfigureAwait(false);
                Assert.That(listener.EndpointUrl.Port, Is.Not.Zero);
                return listener;
            }
            catch
            {
                await listener.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task ConnectClientAndWaitForStatusAsync(
            QuicTransportListener listener,
            QuicTransportChannel channel,
            bool secure)
        {
            var statusSource = new TaskCompletionSource<ConnectionStatusEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            listener.ConnectionStatusChanged += (_, e) =>
            {
                if (!e.Closed)
                {
                    statusSource.TrySetResult(e);
                }
            };

            EndpointDescription endpoint = CreateEndpoint(listener.EndpointUrl, secure);
            await channel.OpenAsync(
                listener.EndpointUrl,
                CreateChannelSettings(endpoint, secure),
                TimeoutToken()).ConfigureAwait(false);

            await WithTimeoutAsync(statusSource.Task).ConfigureAwait(false);
        }

        private QuicTransportChannel CreateClientChannel()
        {
            return new QuicTransportChannel(
                m_telemetry!,
                DefaultBufferManagerFactory.Instance,
                new QuicClientOptions
                {
                    HandshakeTimeout = TimeSpan.FromSeconds(10),
                    ServerCertificateValidation = (_, _, _, _) => true
                })
            {
                OperationTimeout = 5000
            };
        }

        private TransportListenerSettings CreateListenerSettings(
            Uri endpointUrl,
            ICertificateRegistry certificateRegistry)
        {
            EndpointDescription noneEndpoint = CreateEndpoint(endpointUrl, secure: false);
            EndpointDescription secureEndpoint = CreateEndpoint(endpointUrl, secure: true);
            secureEndpoint.ServerCertificate = m_serverCertificate!.RawData.ToByteString();
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;
            configuration.MaxMessageSize = 64 * 1024;
            configuration.MaxBufferSize = 64 * 1024;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;

            return new TransportListenerSettings
            {
                Descriptions = [noneEndpoint, secureEndpoint],
                Configuration = configuration,
                ServerCertificates = certificateRegistry,
                CertificateValidator = new AcceptAllCertificateValidator(),
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create(),
                MaxChannelCount = 10
            };
        }

        private TransportChannelSettings CreateChannelSettings(
            EndpointDescription endpoint,
            bool secure)
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
                ClientCertificate = secure ? m_clientCertificate!.AddRef() : null,
                ClientCertificateChain = secure ? new CertificateCollection() : null,
                ServerCertificate = secure ? m_serverCertificate!.AddRef() : null,
                CertificateValidator = new AcceptAllCertificateValidator(),
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create()
            };
        }

        private EndpointDescription CreateEndpoint(Uri endpointUrl, bool secure)
        {
            return new EndpointDescription
            {
                EndpointUrl = endpointUrl.ToString(),
                SecurityMode = secure ? MessageSecurityMode.SignAndEncrypt : MessageSecurityMode.None,
                SecurityPolicyUri = secure ? SecurityPolicies.Basic256Sha256 : SecurityPolicies.None,
                TransportProfileUri = Profiles.UaQuicTransport,
                ServerCertificate = secure ? m_serverCertificate!.RawData.ToByteString() : ByteString.Empty,
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

        private static async Task<Exception> ConnectionOperationFailsAsync(QuicConnection connection)
        {
            Exception? exception = Assert.CatchAsync(
                async () =>
                {
                    await using QuicStream stream = await connection
                        .OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TimeoutToken())
                        .ConfigureAwait(false);
                });
            Assert.That(exception, Is.Not.Null);
            return exception!;
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

            Assert.Fail("Timed out waiting for stale QUIC connection to close.");
            throw new InvalidOperationException();
        }

        private static async Task<Exception> StreamOperationFailsAsync(QuicStream stream)
        {
            byte[] buffer = new byte[1];
            Exception? exception = Assert.CatchAsync(
                async () => await stream.ReadAsync(buffer, TimeoutToken()).ConfigureAwait(false));
            Assert.That(exception, Is.Not.Null);
            return exception!;
        }

        private static QuicConnection GetClientConnection(QuicTransportChannel channel)
        {
            object transport = channel.Transport!;
            if (transport is not QuicMultiplexedTransport)
            {
                FieldInfo inner = transport.GetType().GetField(
                    "m_inner",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                transport = inner.GetValue(transport)!;
            }

            FieldInfo field = typeof(QuicMultiplexedTransport).GetField(
                "m_connection",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (QuicConnection)field.GetValue(transport)!;
        }

        private static List<ListenerChannel> GetChannels(QuicTransportListener listener)
        {
            FieldInfo field = typeof(QuicTransportListener).GetField(
                "m_channels",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var channels = new List<ListenerChannel>();

            foreach (object entry in (IEnumerable)field.GetValue(listener)!)
            {
                Type entryType = entry.GetType();
                uint key = (uint)entryType.GetProperty("Key")!.GetValue(entry)!;
                object value = entryType.GetProperty("Value")!.GetValue(entry)!;
                Type valueType = value.GetType();
                string globalChannelId = (string)valueType
                    .GetProperty("GlobalChannelId")!
                    .GetValue(value)!;
                channels.Add(new ListenerChannel(key, globalChannelId));
            }

            return channels;
        }

        private static ListenerChannel GetSingleChannel(QuicTransportListener listener)
        {
            List<ListenerChannel> channels = GetChannels(listener);
            Assert.That(channels, Has.Count.EqualTo(1));
            return channels[0];
        }

        private static long? ApplicationErrorCode(Exception exception)
        {
            Exception probe = exception;

            while (probe is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            {
                probe = aggregate.InnerExceptions[0];
            }

            object? value = probe.GetType().GetProperty("ApplicationErrorCode")?.GetValue(probe);
            return value switch
            {
                long signed => signed,
                ulong unsigned => unchecked((long)unsigned),
                _ => null
            };
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, string failure)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (!predicate())
            {
                if (timeout.IsCancellationRequested)
                {
                    Assert.Fail(failure);
                }

                await Task.Delay(25, CancellationToken.None).ConfigureAwait(false);
            }
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

        private static async Task<T> WithTimeoutAsync<T>(Task<T> task)
        {
            Task completed = await Task
                .WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)))
                .ConfigureAwait(false);
            if (completed != task)
            {
                Assert.Fail("Timed out waiting for QUIC listener operation.");
            }

            return await task.ConfigureAwait(false);
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

        private static byte[] BuildOpenSecureChannelChunk(X509Certificate2 senderCertificate)
        {
            byte[] policy = Encoding.UTF8.GetBytes(SecurityPolicies.Basic256Sha256);
            byte[] certificate = senderCertificate.RawData;
            int size = 12 +
                sizeof(int) + policy.Length +
                sizeof(int) + certificate.Length +
                sizeof(int);
            byte[] chunk = new byte[size];
            chunk[0] = (byte)'O';
            chunk[1] = (byte)'P';
            chunk[2] = (byte)'N';
            chunk[3] = (byte)'F';
            BitConverter.GetBytes(size).CopyTo(chunk, 4);
            int offset = 12;
            WriteUaBytes(chunk, ref offset, policy);
            WriteUaBytes(chunk, ref offset, certificate);
            BitConverter.GetBytes(-1).CopyTo(chunk, offset);
            return chunk;
        }

        private static void WriteUaBytes(byte[] chunk, ref int offset, byte[] value)
        {
            BitConverter.GetBytes(value.Length).CopyTo(chunk, offset);
            offset += sizeof(int);
            Buffer.BlockCopy(value, 0, chunk, offset, value.Length);
            offset += value.Length;
        }

        private static Certificate CreateSameKeyReissue(Certificate source)
        {
            using X509Certificate2 sourceX509 = source.AsX509Certificate2();
            using RSA publicKey = sourceX509.GetRSAPublicKey()!;
            using RSA privateKey = sourceX509.GetRSAPrivateKey()!;
            using Certificate publicOnly = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:localhost:QuicLifecycleServerReissue",
                    "QuicLifecycleServerReissue",
                    "CN=QuicLifecycleServerReissue",
                    ["localhost"])
                .SetLifeTime(TimeSpan.FromDays(2))
                .SetIssuer(source)
                .SetRSAPublicKey(publicKey)
                .CreateForRSA();

            X509Certificate2 withPrivateKey = publicOnly
                .AsX509Certificate2()
                .CopyWithPrivateKey(privateKey);
            return Certificate.From(withPrivateKey);
        }

        private sealed record ListenerChannel(uint ChannelId, string GlobalChannelId);

        private ITelemetryContext? m_telemetry;
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
                return m_entry.AddRef();
            }

            public CertificateEntry? AcquireApplicationCertificateBySecurityPolicy(string securityPolicyUri)
            {
                return m_entry.AddRef();
            }

            public Task<bool> GetIssuersAsync(
                Certificate certificate,
                IList<CertificateIssuerReference> issuers,
                CancellationToken ct = default)
            {
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
                return Task.FromResult(CertificateValidationResult.Success);
            }

            public Task<CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(CertificateValidationResult.Success);
            }
        }
    }
}
#endif
