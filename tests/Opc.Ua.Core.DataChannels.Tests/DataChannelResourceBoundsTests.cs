/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

#if NET9_0_OR_GREATER
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;
#endif

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    public sealed class DataChannelResourceBoundsTests
    {
        [Test]
        public async Task OversizedDataFrameWithoutMessageStartIsRejectedByNegotiatedMaxFrameSize()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var bufferManager = new BufferManager("data-channel-resource-bounds", 65536, telemetry);
            await using var manager = new DataChannelManager(
                new FlowControlledTransport(bufferManager),
                isServer: true,
                telemetry);
            DataChannel channel = manager.Register(
                1,
                new NodeId(1u),
                new DataChannelSettings
                {
                    Direction = DataChannelDirection.SourceToSink,
                    DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                    MaxFrameSize = 4,
                    InitialCredit = 1024
                },
                isSource: false);
            manager.MarkOpen(channel.ChannelId);

            manager.HandleFrame(DataChannelFrame.Data(
                channel.ChannelId,
                1,
                DataChannelFrameFlags.None,
                new byte[5]));

            DataChannelDiagnosticsDataType diagnostics = channel.GetDiagnostics();
            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(channel.Status, Is.EqualTo((StatusCode)StatusCodes.BadDataChannelLimitsExceeded));
                Assert.That(diagnostics.FramesReceived, Is.Zero);
                Assert.That(diagnostics.BytesReceived, Is.Zero);
            });
        }

        /// <summary>
        /// Part 6 errata §5.8 makes backpressure per channel: "a consumer
        /// that cannot keep up with a video stream stalls that stream and
        /// nothing else". Under inline framing one reader carries both the
        /// frames and the Service traffic, so a receiver that waits for the
        /// application before returning would stall MSG, OPN and CLO on the
        /// whole SecureChannel. Frames far more numerous than the queue used
        /// to hold must therefore be admitted without blocking.
        /// </summary>
        [Test]
        public async Task SmallFramesWithinCreditAreDeliveredWithoutBlockingTheReceivePath()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var bufferManager = new BufferManager("data-channel-delivery-bound", 65536, telemetry);
            await using var manager = new DataChannelManager(
                new FlowControlledTransport(bufferManager),
                isServer: true,
                telemetry);
            DataChannel channel = manager.Register(
                1,
                new NodeId(1u),
                new DataChannelSettings
                {
                    Direction = DataChannelDirection.SourceToSink,
                    DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                    MaxFrameSize = 65536,
                    InitialCredit = 1024 * 1024
                },
                isSource: false);
            manager.MarkOpen(channel.ChannelId);

            // The queue used to be sized in frames from a byte credit, which
            // left room for sixteen. Nothing consumes while these arrive.
            const int frameCount = 256;

            var delivery = Task.Run(() =>
            {
                for (uint ii = 0; ii < frameCount; ii++)
                {
                    manager.HandleFrame(DataChannelFrame.Data(
                        channel.ChannelId,
                        ii + 1,
                        DataChannelFrameFlags.None,
                        new byte[1]));
                }
            });

            Assert.That(
                await Task.WhenAny(delivery, Task.Delay(TimeSpan.FromSeconds(10)))
                    .ConfigureAwait(false),
                Is.SameAs(delivery),
                "The receive path blocked waiting for the application to consume.");

            await delivery.ConfigureAwait(false);

            DataChannelDiagnosticsDataType diagnostics = channel.GetDiagnostics();
            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(diagnostics.FramesReceived, Is.EqualTo((ulong)frameCount));
            });
        }

        /// <summary>
        /// The queue budget is bounded by encoded frame bytes rather than by
        /// payload, because a frame carrying no payload consumes no credit
        /// while still occupying a header and a slot (§7.4 draws the same
        /// distinction). A peer that floods empty frames is reset rather than
        /// allowed to grow the queue without limit.
        /// </summary>
        [Test]
        public async Task EmptyFramesBeyondTheDeliveryBudgetResetTheChannelRatherThanBlocking()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var bufferManager = new BufferManager("data-channel-delivery-flood", 65536, telemetry);
            await using var manager = new DataChannelManager(
                new FlowControlledTransport(bufferManager),
                isServer: true,
                telemetry);
            DataChannel channel = manager.Register(
                1,
                new NodeId(1u),
                new DataChannelSettings
                {
                    Direction = DataChannelDirection.SourceToSink,
                    DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                    MaxFrameSize = 16,
                    InitialCredit = 16
                },
                isSource: false);
            manager.MarkOpen(channel.ChannelId);

            var flood = Task.Run(() =>
            {
                for (uint ii = 0; ii < 1000 && channel.State == DataChannelState.Open; ii++)
                {
                    manager.HandleFrame(DataChannelFrame.Data(
                        channel.ChannelId,
                        ii + 1,
                        DataChannelFrameFlags.None,
                        Array.Empty<byte>()));
                }
            });

            Assert.That(
                await Task.WhenAny(flood, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false),
                Is.SameAs(flood),
                "The receive path blocked instead of bounding the queue.");

            await flood.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(
                    channel.Status,
                    Is.EqualTo((StatusCode)StatusCodes.BadDataChannelCreditExceeded));
            });
        }

        private sealed class FlowControlledTransport : IDataChannelTransport
        {
            public FlowControlledTransport(BufferManager bufferManager)
            {
                BufferManager = bufferManager;
            }

            public DataChannelFramingMode FramingMode => DataChannelFramingMode.Inline;

            public int MaxFrameBodySize => 65536;

            public bool HasTransportFlowControl => true;

            public BufferManager BufferManager { get; }

            public TimeProvider TimeProvider => TimeProvider.System;

            public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
            {
                return default;
            }

            public void OnProtocolFault(DataChannelFrameError error)
            {
            }
        }
    }

#if NET9_0_OR_GREATER
    [TestFixture]
    [Category("DataChannels")]
    [Category("Quic")]
    [NonParallelizable]
    public sealed class QuicListenerResourceBoundsTests
    {
        [SetUp]
        public void SetUp()
        {
            QuicTestSupport.SkipUnlessAvailable();

            m_telemetry = NUnitTelemetryContext.Create();
            m_serverCertificate = CreateCertificate("QuicResourceBoundsServer");
            m_serverRegistry = new InMemoryCertificateRegistry(m_serverCertificate);
            m_callback = new EchoCallback();
        }

        [TearDown]
        public void TearDown()
        {
            m_serverRegistry?.Dispose();
            m_serverCertificate?.Dispose();
        }

        [Test]
        [CancelAfter(30000)]
        public async Task AbandonedHandshakeAdmissionStateExpiresAtHandshakeTimeoutAsync()
        {
            var callbackEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCallback = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using QuicTransportListener listener = await OpenListenerAsync(
                "AbandonedHandshake",
                TimeSpan.FromMilliseconds(200),
                (_, _) =>
                {
                    callbackEntered.TrySetResult(true);
                    return new ValueTask(releaseCallback.Task);
                }).ConfigureAwait(false);
            using var abandonedConnect = new CancellationTokenSource();

            Task<QuicConnection> connecting = QuicConnection.ConnectAsync(
                new QuicClientConnectionOptions
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
                },
                abandonedConnect.Token).AsTask();

            await WithTimeoutAsync(callbackEntered.Task).ConfigureAwait(false);
            Assert.That(listener.PendingConnectionAdmissionCount, Is.EqualTo(1));

            abandonedConnect.Cancel();
            await WaitUntilAsync(
                () => listener.PendingConnectionAdmissionCount == 0,
                "abandoned handshake admission state did not expire").ConfigureAwait(false);
            releaseCallback.SetResult(true);

            try
            {
                await connecting.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (QuicException)
            {
            }
        }

        private async Task<QuicTransportListener> OpenListenerAsync(
            string path,
            TimeSpan handshakeTimeout,
            Func<QuicConnection, long, ValueTask> admissionPause)
        {
            var listener = new QuicTransportListener(m_telemetry!)
            {
                AdmissionCallbackPauseForTesting = admissionPause,
                PendingAdmissionHandshakeTimeout = handshakeTimeout
            };

            try
            {
                var endpointUrl = new Uri($"opc.quic://localhost:0/{path}");
                await listener.OpenAsync(
                    endpointUrl,
                    CreateListenerSettings(endpointUrl),
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

        private Certificate? m_serverCertificate;
        private InMemoryCertificateRegistry? m_serverRegistry;
        private EchoCallback? m_callback;
        private ITelemetryContext? m_telemetry;

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
#endif
}
