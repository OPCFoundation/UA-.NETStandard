#if NET9_0_OR_GREATER
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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
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
    /// <summary>
    /// Exercises <see cref="QuicTransportListener"/> through real loopback
    /// QUIC connections.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    [Category("Quic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class QuicListenerTests
    {
        [SetUp]
        public void SetUp()
        {
            if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            {
                Assert.Ignore("QUIC is unavailable on this platform (msquic missing).");
            }

            m_telemetry = NUnitTelemetryContext.Create();
            m_serverCertificate = CreateCertificate("QuicListenerServer");
            m_clientCertificate = CreateCertificate("QuicListenerClient");
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
        [CancelAfter(20000)]
        public async Task OpenClosePopulatesPropertiesAndReleasesUdpPortAsync()
        {
            int port = GetFreeUdpPort();
            Uri endpointUrl = EndpointUrl(port, "Lifecycle");

            await using var listener = new QuicTransportListener(m_telemetry!);
            await listener.OpenAsync(
                endpointUrl,
                CreateListenerSettings(endpointUrl, m_serverRegistry!),
                m_callback!,
                TimeoutToken()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(listener.UriScheme, Is.EqualTo(Utils.UriSchemeOpcQuic));
                Assert.That(listener.ListenerId, Is.Not.Empty);
                Assert.That(listener.EndpointUrl, Is.EqualTo(endpointUrl));
            });

            await listener.CloseAsync(TimeoutToken()).ConfigureAwait(false);
            await AssertCanBindQuicPortAsync(port).ConfigureAwait(false);
            await listener.DisposeAsync().ConfigureAwait(false);
            await AssertCanBindQuicPortAsync(port).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(30000)]
        public async Task QuicTransportChannelConnectsAndEstablishesControlStreamAsync()
        {
            int port = GetFreeUdpPort();
            Uri endpointUrl = EndpointUrl(port, "RealClient");
            await using var listener = await OpenListenerAsync(endpointUrl).ConfigureAwait(false);
            using var channel = CreateClientChannel();

            ConnectionStatusEventArgs status = await ConnectClientAndWaitForStatusAsync(
                listener,
                channel,
                endpointUrl,
                secure: false).ConfigureAwait(false);

            IServiceResponse response = await channel.SendRequestAsync(
                new ReadRequest
                {
                    RequestHeader = new RequestHeader { TimeoutHint = 5000 },
                    NodesToRead = new ArrayOf<ReadValueId>()
                },
                TimeoutToken()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(status.Closed, Is.False);
                Assert.That(status.EndpointUrl, Is.EqualTo(endpointUrl));
                Assert.That(response, Is.InstanceOf<ReadResponse>());
                Assert.That(m_callback!.RequestCount, Is.EqualTo(1));
                Assert.That(GetChannels(listener), Has.Count.EqualTo(1));
            });
        }

        [Test]
        [CancelAfter(20000)]
        public async Task ForeignAlpnIsRejectedBeforeAnOpcUaChannelIsAcceptedAsync()
        {
            int port = GetFreeUdpPort();
            Uri endpointUrl = EndpointUrl(port, "Alpn");
            await using var listener = await OpenListenerAsync(endpointUrl).ConfigureAwait(false);

            var authentication = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol("h3")],
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            };
            var options = new QuicClientConnectionOptions
            {
                RemoteEndPoint = new DnsEndPoint("localhost", port),
                ClientAuthenticationOptions = authentication,
                DefaultStreamErrorCode = 0x0A,
                DefaultCloseErrorCode = 0x0B
            };

            Assert.That(
                async () =>
                {
                    QuicConnection connection = await QuicConnection
                        .ConnectAsync(options, TimeoutToken())
                        .ConfigureAwait(false);
                    await connection.DisposeAsync().ConfigureAwait(false);
                },
                Throws.InstanceOf<Exception>());

            await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            Assert.That(GetChannels(listener), Is.Empty);
        }

        [Test]
        [CancelAfter(30000)]
        public async Task CertificateUpdateChangesTheCertificateRegistryUsedForRotationAsync()
        {
            int port = GetFreeUdpPort();
            Uri endpointUrl = EndpointUrl(port, "CertificateUpdate");
            await using var listener = await OpenListenerAsync(endpointUrl).ConfigureAwait(false);
            using var channel = CreateClientChannel();

            await ConnectClientAndWaitForStatusAsync(
                listener,
                channel,
                endpointUrl,
                secure: true).ConfigureAwait(false);
            string globalChannelId = GetSingleChannel(listener).GlobalChannelId;
            using Certificate newCertificate = CreateCertificate("QuicListenerServerNew");
            using var newRegistry = new InMemoryCertificateRegistry(newCertificate);

            listener.CertificateUpdate(new AcceptAllCertificateValidator(), newRegistry);
            IReadOnlyList<string> closed = await listener
                .CloseChannelsForCertificateAsync(m_serverCertificate!, TimeoutToken())
                .ConfigureAwait(false);

            Assert.That(closed, Is.EqualTo(new[] { globalChannelId }));
        }

        [Test]
        [CancelAfter(30000)]
        public async Task ChannelClosedRemovesLiveChannelAndReportsClosedStatusAsync()
        {
            int port = GetFreeUdpPort();
            Uri endpointUrl = EndpointUrl(port, "ChannelClosed");
            await using var listener = await OpenListenerAsync(endpointUrl).ConfigureAwait(false);
            using var channel = CreateClientChannel();

            await ConnectClientAndWaitForStatusAsync(
                listener,
                channel,
                endpointUrl,
                secure: false).ConfigureAwait(false);
            ListenerChannel liveChannel = GetSingleChannel(listener);
            var closedSource = new TaskCompletionSource<ConnectionStatusEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            listener.ConnectionStatusChanged += (_, e) =>
            {
                if (e.Closed)
                {
                    closedSource.TrySetResult(e);
                }
            };

            listener.ChannelClosed(liveChannel.ChannelId);
            ConnectionStatusEventArgs closed = await WithTimeoutAsync(closedSource.Task).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(closed.Closed, Is.True);
                Assert.That(GetChannels(listener), Is.Empty);
            });
        }

        [Test]
        [CancelAfter(30000)]
        public async Task UpdateChannelLastActiveTimeRefreshesTheMatchingLiveChannelAsync()
        {
            int port = GetFreeUdpPort();
            Uri endpointUrl = EndpointUrl(port, "LastActive");
            await using var listener = await OpenListenerAsync(endpointUrl).ConfigureAwait(false);
            using var channel = CreateClientChannel();

            await ConnectClientAndWaitForStatusAsync(
                listener,
                channel,
                endpointUrl,
                secure: false).ConfigureAwait(false);
            ListenerChannel liveChannel = GetSingleChannel(listener);
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
            int before = liveChannel.ElapsedSinceLastActiveTime;

            listener.UpdateChannelLastActiveTime(liveChannel.GlobalChannelId);

            Assert.That(
                liveChannel.ElapsedSinceLastActiveTime,
                Is.LessThanOrEqualTo(before));
        }

        [Test]
        [CancelAfter(30000)]
        public async Task CloseChannelsForCertificateReturnsAffectedChannelIdsAsync()
        {
            int port = GetFreeUdpPort();
            Uri endpointUrl = EndpointUrl(port, "CertificateRotation");
            await using var listener = await OpenListenerAsync(endpointUrl).ConfigureAwait(false);
            using var channel = CreateClientChannel();

            await ConnectClientAndWaitForStatusAsync(
                listener,
                channel,
                endpointUrl,
                secure: true).ConfigureAwait(false);
            string globalChannelId = GetSingleChannel(listener).GlobalChannelId;

            IReadOnlyList<string> closed = await listener
                .CloseChannelsForCertificateAsync(m_serverCertificate!, TimeoutToken())
                .ConfigureAwait(false);

            Assert.That(closed, Is.EqualTo(new[] { globalChannelId }));
        }

        [Test]
        [CancelAfter(30000)]
        public async Task CloseChannelsForUntrustedPeersReturnsAffectedChannelIdsAsync()
        {
            int port = GetFreeUdpPort();
            Uri endpointUrl = EndpointUrl(port, "PeerTrust");
            await using var listener = await OpenListenerAsync(endpointUrl).ConfigureAwait(false);
            using var channel = CreateClientChannel();

            await ConnectClientAndWaitForStatusAsync(
                listener,
                channel,
                endpointUrl,
                secure: true).ConfigureAwait(false);
            string globalChannelId = GetSingleChannel(listener).GlobalChannelId;

            IReadOnlyList<string> closed = await listener
                .CloseChannelsForUntrustedPeersAsync(
                    (_, _) => new ValueTask<bool>(false),
                    TimeoutToken())
                .ConfigureAwait(false);

            Assert.That(closed, Is.EqualTo(new[] { globalChannelId }));
        }

        [Test]
        [CancelAfter(20000)]
        public async Task CreateReverseConnectionFailsClosedWhenReversePeerTlsIsUntrustedAsync()
        {
            int listenerPort = GetFreeUdpPort();
            Uri endpointUrl = EndpointUrl(listenerPort, "ReverseSource");
            await using var listener = await OpenListenerAsync(endpointUrl).ConfigureAwait(false);
            await using QuicListener reversePeer = await CreateRawQuicListenerAsync().ConfigureAwait(false);
            Task<QuicConnection> accepted = reversePeer.AcceptConnectionAsync(TimeoutToken()).AsTask();
            bool connectionWaitingRaised = false;
            listener.ConnectionWaiting += (_, _) =>
            {
                connectionWaitingRaised = true;
                return Task.CompletedTask;
            };

            listener.CreateReverseConnection(
                QuicTransport.CreateUrl("localhost", reversePeer.LocalEndPoint.Port),
                500);

            Assert.That(
                async () =>
                {
                    await using QuicConnection connection = await WithTimeoutAsync(accepted)
                        .ConfigureAwait(false);
                },
                Throws.InstanceOf<Exception>());
            Assert.That(connectionWaitingRaised, Is.False);
        }

        [Test]
        public async Task ReconnectToExistingChannelThrowsBecauseQuicUsesConnectionMigrationAsync()
        {
            await using var listener = new QuicTransportListener(NUnitTelemetryContext.Create());

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                listener.ReconnectToExistingChannel(
                    null!,
                    1,
                    1,
                    1,
                    null!,
                    new ChannelToken(),
                    new OpenSecureChannelRequest()))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTcpSecureChannelUnknown));
        }

        private async Task<QuicTransportListener> OpenListenerAsync(Uri endpointUrl)
        {
            var listener = new QuicTransportListener(m_telemetry!);
            try
            {
                await listener.OpenAsync(
                    endpointUrl,
                    CreateListenerSettings(endpointUrl, m_serverRegistry!),
                    m_callback!,
                    TimeoutToken()).ConfigureAwait(false);
                return listener;
            }
            catch
            {
                await listener.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task<ConnectionStatusEventArgs> ConnectClientAndWaitForStatusAsync(
            QuicTransportListener listener,
            QuicTransportChannel channel,
            Uri endpointUrl,
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

            EndpointDescription endpoint = CreateEndpoint(endpointUrl, secure);
            await channel.OpenAsync(
                endpointUrl,
                CreateChannelSettings(endpoint, secure),
                TimeoutToken()).ConfigureAwait(false);

            return await WithTimeoutAsync(statusSource.Task).ConfigureAwait(false);
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

        private static Uri EndpointUrl(int port, string path)
        {
            return new Uri(
                $"opc.quic://localhost:{port.ToString(CultureInfo.InvariantCulture)}/{path}");
        }

        private static async Task AssertCanBindQuicPortAsync(int port)
        {
            await using QuicListener listener = await CreateRawQuicListenerAsync(port).ConfigureAwait(false);
            Assert.That(listener.LocalEndPoint.Port, Is.EqualTo(port));
        }

        private static async Task<QuicListener> CreateRawQuicListenerAsync(int port = 0)
        {
            using Certificate certificate = CreateCertificate("QuicRawListener");
            X509Certificate2 tlsCertificate = ToTlsCertificate(certificate);
            try
            {
                var options = new QuicListenerOptions
                {
                    ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, port),
                    ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                    ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                        new QuicServerConnectionOptions
                        {
                            DefaultStreamErrorCode = 0x0A,
                            DefaultCloseErrorCode = 0x0B,
                            MaxInboundBidirectionalStreams = 32,
                            MaxInboundUnidirectionalStreams = 32,
                            ServerAuthenticationOptions = new SslServerAuthenticationOptions
                            {
                                ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                                ServerCertificate = tlsCertificate
                            }
                        })
                };

                return await QuicListener.ListenAsync(options, TimeoutToken()).ConfigureAwait(false);
            }
            catch
            {
                tlsCertificate.Dispose();
                throw;
            }
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
                int elapsedSinceLastActiveTime = (int)valueType
                    .GetProperty("ElapsedSinceLastActiveTime")!
                    .GetValue(value)!;
                channels.Add(new ListenerChannel(key, globalChannelId, elapsedSinceLastActiveTime));
            }

            return channels;
        }

        private static ListenerChannel GetSingleChannel(QuicTransportListener listener)
        {
            List<ListenerChannel> channels = GetChannels(listener);
            Assert.That(channels, Has.Count.EqualTo(1));
            return channels[0];
        }

        private static CancellationToken TimeoutToken()
        {
            return new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
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

        private static int GetFreeUdpPort()
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.IPv6Any, 0));
            return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
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

        private static X509Certificate2 ToTlsCertificate(Certificate certificate)
        {
            byte[] pfx = certificate.AsX509Certificate2().Export(X509ContentType.Pfx);
            return X509CertificateLoader.LoadPkcs12(
                pfx,
                null,
                X509KeyStorageFlags.Exportable);
        }

        private sealed record ListenerChannel(
            uint ChannelId,
            string GlobalChannelId,
            int ElapsedSinceLastActiveTime);

        private sealed class EchoCallback : ITransportListenerCallback
        {
            public int RequestCount { get; private set; }

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                RequestCount++;
                Assert.That(secureChannelContext, Is.Not.Null);
                Assert.That(request, Is.InstanceOf<ReadRequest>());
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

        private ITelemetryContext? m_telemetry;
        private Certificate? m_serverCertificate;
        private Certificate? m_clientCertificate;
        private InMemoryCertificateRegistry? m_serverRegistry;
        private EchoCallback? m_callback;
    }
}
#endif
