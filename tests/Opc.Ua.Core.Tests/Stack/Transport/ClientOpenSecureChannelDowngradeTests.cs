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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// A client that opens a secured channel must not accept an
    /// OpenSecureChannel response secured with another policy - in
    /// particular an unsigned SecurityPolicy#None response a man in the
    /// middle can forge without any key (OPC 10000-4 §5.6.2.1,
    /// OPC 10000-6 §6.7.2.3).
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class ClientOpenSecureChannelDowngradeTests
    {
        private static readonly ICertificateFactory s_certificateFactory = DefaultCertificateFactory.Instance;

        [Test]
        [CancelAfter(30000)]
        public async Task SecuredOpenRejectsUnsignedNoneResponseAsync()
        {
            using var fixture = new DowngradeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            fixture.Proxy.Downgrade = true;

            using UaSCUaBinaryTransportChannel channel = fixture.CreateClient();

            Assert.That(
                async () => await channel.OpenAsync(
                    fixture.ProxyUrl, fixture.ClientSettings, CancellationToken.None).ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>());
            Assert.That(fixture.Proxy.ForgedResponses, Is.EqualTo(1), "the forged response was never sent");
            Assert.That(
                fixture.Proxy.CleartextMessages,
                Is.Zero,
                "the client sent a service request over the downgraded channel");

            await fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(30000)]
        public async Task SecuredReconnectRejectsUnsignedNoneResponseAsync()
        {
            using var fixture = new DowngradeFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            using UaSCUaBinaryTransportChannel channel = fixture.CreateClient();
            await channel.OpenAsync(
                fixture.ProxyUrl, fixture.ClientSettings, CancellationToken.None).ConfigureAwait(false);
            await SendReadAsync(channel).ConfigureAwait(false);
            Assert.That(fixture.Callback.RequestCount, Is.EqualTo(1));

            // the man in the middle tears the connection down and answers the
            // reconnect itself. The reconnect builds a fresh client channel.
            fixture.Proxy.Downgrade = true;
            fixture.Proxy.DropConnections();

            Assert.That(
                async () => await channel.ReconnectAsync(null, CancellationToken.None).ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>());
            Assert.That(fixture.Proxy.ForgedResponses, Is.EqualTo(1), "the forged response was never sent");

            Assert.That(
                async () => await SendReadAsync(channel).ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>());
            Assert.That(
                fixture.Proxy.CleartextMessages,
                Is.Zero,
                "the client sent a service request over the downgraded channel");
            Assert.That(fixture.Callback.RequestCount, Is.EqualTo(1));

            await fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(30000)]
        public async Task UnsecuredOpenStillAcceptsNoneResponseAsync()
        {
            using var fixture = new DowngradeFixture(MessageSecurityMode.None);
            await fixture.StartAsync().ConfigureAwait(false);
            fixture.Proxy.Downgrade = true;

            using UaSCUaBinaryTransportChannel channel = fixture.CreateClient();
            await channel.OpenAsync(
                fixture.ProxyUrl, fixture.ClientSettings, CancellationToken.None).ConfigureAwait(false);

            Assert.That(fixture.Proxy.ForgedResponses, Is.EqualTo(1));

            await fixture.StopAsync().ConfigureAwait(false);
        }

        private static async Task SendReadAsync(UaSCUaBinaryTransportChannel channel)
        {
            IServiceResponse response = await channel.SendRequestAsync(
                new ReadRequest
                {
                    RequestHeader = new RequestHeader { TimeoutHint = 5000 },
                    NodesToRead = new ArrayOf<ReadValueId>()
                },
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.InstanceOf<ReadResponse>());
        }

        private sealed class DowngradeFixture : IDisposable
        {
            public DowngradeFixture(
                MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt)
            {
                m_telemetry = NUnitTelemetryContext.Create();
                m_securityMode = securityMode;
                m_securityPolicyUri = securityMode == MessageSecurityMode.None
                    ? SecurityPolicies.None
                    : SecurityPolicies.Basic256Sha256;
                m_serverCertificate = s_certificateFactory.CreateCertificate("CN=server").CreateForRSA();
                m_clientCertificate = s_certificateFactory.CreateCertificate("CN=client").CreateForRSA();
                m_serverUrl = new Uri($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
                Proxy = new DowngradeProxy(m_serverUrl.Port, m_telemetry);
                ProxyUrl = new Uri($"opc.tcp://127.0.0.1:{Proxy.Port}");
                m_endpoint = new EndpointDescription
                {
                    EndpointUrl = ProxyUrl.ToString(),
                    SecurityMode = m_securityMode,
                    SecurityPolicyUri = m_securityPolicyUri,
                    TransportProfileUri = Profiles.UaTcpTransport,
                    ServerCertificate = m_serverCertificate.RawData.ToByteString()
                };
                m_configuration = EndpointConfiguration.Create();
                m_configuration.OperationTimeout = 5000;
                m_configuration.MaxMessageSize = 64 * 1024;
                m_configuration.MaxBufferSize = 64 * 1024;
                m_configuration.ChannelLifetime = 60000;
                m_configuration.SecurityTokenLifetime = 60000;

                var validator = new Mock<ICertificateValidatorEx>();
                validator
                    .Setup(v => v.ValidateAsync(
                        It.IsAny<Certificate>(),
                        It.IsAny<TrustListIdentifier?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult(CertificateValidationResult.Success));
                validator
                    .Setup(v => v.ValidateAsync(
                        It.IsAny<CertificateCollection>(),
                        It.IsAny<TrustListIdentifier?>(),
                        It.IsAny<Opc.Ua.Security.Certificates.CertificateValidationOptions?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult(CertificateValidationResult.Success));
                m_validator = validator.Object;

                ClientSettings = new TransportChannelSettings
                {
                    Description = m_endpoint,
                    Configuration = m_configuration,
                    ClientCertificate = m_securityMode == MessageSecurityMode.None ? null : m_clientCertificate,
                    ClientCertificateChain = m_securityMode == MessageSecurityMode.None ? null : m_clientChain,
                    ServerCertificate = m_securityMode == MessageSecurityMode.None ? null : m_serverCertificate,
                    CertificateValidator = m_validator,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create()
                };
            }

            public DowngradeProxy Proxy { get; }

            public Uri ProxyUrl { get; }

            public TransportChannelSettings ClientSettings { get; }

            public CountingCallback Callback { get; } = new();

            public async Task StartAsync()
            {
                var certificateRegistry = new Mock<ICertificateRegistry>();
                certificateRegistry.SetupGet(r => r.SendCertificateChain).Returns(false);
                certificateRegistry
                    .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()))
                    .Returns(() => m_securityMode == MessageSecurityMode.None
                        ? null
                        : new CertificateEntry(
                            m_serverCertificate,
                            m_serverChain,
                            ObjectTypeIds.RsaSha256ApplicationCertificateType));

                m_listener = new TcpTransportListener(m_telemetry);
                await m_listener.OpenAsync(
                    m_serverUrl,
                    new TransportListenerSettings
                    {
                        Descriptions = new List<EndpointDescription> { m_endpoint },
                        Configuration = m_configuration,
                        ServerCertificates = certificateRegistry.Object,
                        CertificateValidator = m_validator,
                        NamespaceUris = new NamespaceTable(),
                        Factory = EncodeableFactory.Create(),
                        MaxChannelCount = 10
                    },
                    Callback,
                    CancellationToken.None).ConfigureAwait(false);

                Proxy.Start();
            }

            public UaSCUaBinaryTransportChannel CreateClient()
            {
                return new UaSCUaBinaryTransportChannel(new TcpByteTransportFactory(m_telemetry), m_telemetry)
                {
                    OperationTimeout = 5000
                };
            }

            public async Task StopAsync()
            {
                Proxy.Dispose();
                if (m_listener != null)
                {
                    await m_listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    await m_listener.DisposeAsync().ConfigureAwait(false);
                    m_listener = null;
                }
            }

            public void Dispose()
            {
                Proxy.Dispose();
                m_listener?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                m_serverCertificate.Dispose();
                m_clientCertificate.Dispose();
                m_serverChain.Dispose();
                m_clientChain.Dispose();
            }


            private readonly ITelemetryContext m_telemetry;
            private readonly MessageSecurityMode m_securityMode;
            private readonly string m_securityPolicyUri;
            private readonly Certificate m_serverCertificate;
            private readonly Certificate m_clientCertificate;
            private readonly CertificateCollection m_serverChain = [];
            private readonly CertificateCollection m_clientChain = [];
            private readonly Uri m_serverUrl;
            private readonly EndpointDescription m_endpoint;
            private readonly EndpointConfiguration m_configuration;
            private readonly ICertificateValidatorEx m_validator;
            private TcpTransportListener? m_listener;
        }

        /// <summary>
        /// A man in the middle between the client and the server: it relays
        /// connections verbatim until told to downgrade, after which it
        /// answers the client's Hello and OpenSecureChannel request itself
        /// with an unsigned SecurityPolicy#None response.
        /// </summary>
        private sealed class DowngradeProxy : IDisposable
        {
            public DowngradeProxy(int serverPort, ITelemetryContext telemetry)
            {
                m_serverPort = serverPort;
                m_context = ServiceMessageContext.Create(telemetry);
                m_listener = new TcpListener(IPAddress.Loopback, 0);
                m_listener.Start();
                Port = ((IPEndPoint)m_listener.LocalEndpoint).Port;
            }

            public int Port { get; }

            public bool Downgrade
            {
                get => Volatile.Read(ref m_downgrade);
                set => Volatile.Write(ref m_downgrade, value);
            }

            public int ForgedResponses => Volatile.Read(ref m_forgedResponses);

            public int CleartextMessages => Volatile.Read(ref m_cleartextMessages);

            public void Start()
            {
                m_acceptLoop = Task.Run(AcceptLoopAsync);
            }

            public void DropConnections()
            {
                lock (m_lock)
                {
                    foreach (TcpClient client in m_connections)
                    {
                        client.Dispose();
                    }
                    m_connections.Clear();
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }

                m_cts.Cancel();
                m_listener.Stop();
                // TcpListener is IDisposable only on .NET 8+.
                (m_listener as IDisposable)?.Dispose();
                DropConnections();
                try
                {
                    m_acceptLoop?.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                    // the loop ends with the listener.
                }
                m_cts.Dispose();
            }

            private async Task AcceptLoopAsync()
            {
                while (!m_cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await m_listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (Exception) when (m_cts.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    Track(client);
                    _ = Downgrade ? AnswerWithNoneAsync(client) : RelayAsync(client);
                }
            }

            private void Track(TcpClient client)
            {
                lock (m_lock)
                {
                    m_connections.Add(client);
                }
            }

            private async Task RelayAsync(TcpClient client)
            {
                var server = new TcpClient();
                Track(server);
                try
                {
                    await server.ConnectAsync(IPAddress.Loopback, m_serverPort).ConfigureAwait(false);
                    NetworkStream fromClient = client.GetStream();
                    NetworkStream toServer = server.GetStream();
                    await Task.WhenAny(
                        fromClient.CopyToAsync(toServer, 81920, m_cts.Token),
                        toServer.CopyToAsync(fromClient, 81920, m_cts.Token)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // a dropped connection ends the relay.
                }
                finally
                {
                    client.Dispose();
                    server.Dispose();
                }
            }

            private async Task AnswerWithNoneAsync(TcpClient client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();

                    byte[] hello = await ReadChunkAsync(stream).ConfigureAwait(false);
                    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(hello), Is.EqualTo(TcpMessageType.Hello));
                    byte[] acknowledge = BuildAcknowledge();
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    await stream.WriteAsync(acknowledge.AsMemory(), m_cts.Token).ConfigureAwait(false);
#else
                    await stream.WriteAsync(acknowledge, 0, acknowledge.Length, m_cts.Token).ConfigureAwait(false);
#endif

                    byte[] open = await ReadChunkAsync(stream).ConfigureAwait(false);
                    Assert.That(
                        BinaryPrimitives.ReadUInt32LittleEndian(open),
                        Is.EqualTo(TcpMessageType.Open | TcpMessageType.Final));
                    byte[] response = BuildNoneOpenResponse();
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    await stream.WriteAsync(response.AsMemory(), m_cts.Token).ConfigureAwait(false);
#else
                    await stream.WriteAsync(response, 0, response.Length, m_cts.Token).ConfigureAwait(false);
#endif
                    Interlocked.Increment(ref m_forgedResponses);

                    // anything the client sends from now on would travel in cleartext.
                    while (!m_cts.IsCancellationRequested)
                    {
                        byte[] chunk = await ReadChunkAsync(stream).ConfigureAwait(false);
                        if ((BinaryPrimitives.ReadUInt32LittleEndian(chunk) & TcpMessageType.MessageTypeMask) ==
                            TcpMessageType.Message)
                        {
                            Interlocked.Increment(ref m_cleartextMessages);
                        }
                    }
                }
                catch (Exception)
                {
                    // the client closed the connection.
                }
                finally
                {
                    client.Dispose();
                }
            }

            private async Task<byte[]> ReadChunkAsync(NetworkStream stream)
            {
                byte[] header = new byte[8];
                await ReadExactlyAsync(stream, header, 0, header.Length).ConfigureAwait(false);
                int size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                byte[] chunk = new byte[size];
                Buffer.BlockCopy(header, 0, chunk, 0, header.Length);
                await ReadExactlyAsync(stream, chunk, header.Length, size - header.Length).ConfigureAwait(false);
                return chunk;
            }

            private async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    int read = await stream.ReadAsync(buffer.AsMemory(offset, count), m_cts.Token).ConfigureAwait(false);
#else
                    int read = await stream.ReadAsync(buffer, offset, count, m_cts.Token).ConfigureAwait(false);
#endif
                    if (read == 0)
                    {
                        throw new EndOfStreamException();
                    }
                    offset += read;
                    count -= read;
                }
            }

            private byte[] BuildAcknowledge()
            {
                return BuildChunk(encoder =>
                {
                    encoder.WriteUInt32(null, TcpMessageType.Acknowledge);
                    encoder.WriteUInt32(null, 0); // size placeholder
                    encoder.WriteUInt32(null, 0); // protocol version
                    encoder.WriteUInt32(null, 8192); // receive buffer size
                    encoder.WriteUInt32(null, 8192); // send buffer size
                    encoder.WriteUInt32(null, 0); // max message size
                    encoder.WriteUInt32(null, 0); // max chunk count
                });
            }

            private byte[] BuildNoneOpenResponse()
            {
                var response = new OpenSecureChannelResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Good
                    },
                    ServerProtocolVersion = 0,
                    SecurityToken = new ChannelSecurityToken
                    {
                        ChannelId = 77,
                        TokenId = 1,
                        RevisedLifetime = 600000
                    },
                    ServerNonce = ByteString.Empty
                };
                byte[] body = BinaryEncoder.EncodeMessage(response, m_context);

                return BuildChunk(encoder =>
                {
                    encoder.WriteUInt32(null, TcpMessageType.Open | TcpMessageType.Final);
                    encoder.WriteUInt32(null, 0); // size placeholder
                    encoder.WriteUInt32(null, 77); // secure channel id
                    encoder.WriteString(null, SecurityPolicies.None);
                    encoder.WriteInt32(null, -1); // no sender certificate
                    encoder.WriteInt32(null, -1); // no receiver thumbprint
                    encoder.WriteUInt32(null, 1); // sequence number
                    encoder.WriteUInt32(null, 1); // request id
                    foreach (byte b in body)
                    {
                        encoder.WriteByte(null, b);
                    }
                });
            }

            private byte[] BuildChunk(Action<BinaryEncoder> write)
            {
                using var stream = new MemoryStream();
                using (var encoder = new BinaryEncoder(stream, m_context, true))
                {
                    write(encoder);
                }
                byte[] chunk = stream.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), chunk.Length);
                return chunk;
            }

            private readonly int m_serverPort;
            private readonly IServiceMessageContext m_context;
            private readonly TcpListener m_listener;
            private readonly CancellationTokenSource m_cts = new();
            private readonly Lock m_lock = new();
            private readonly List<TcpClient> m_connections = [];
            private Task? m_acceptLoop;
            private bool m_downgrade;
            private int m_forgedResponses;
            private int m_cleartextMessages;
            private int m_disposed;
        }

        private sealed class CountingCallback : ITransportListenerCallback
        {
            public int RequestCount => Volatile.Read(ref m_requestCount);

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_requestCount);
                return new ValueTask<IServiceResponse>(
                    new ReadResponse { ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good } });
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(NodeId authenticationToken, out uint channelId)
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

            private int m_requestCount;
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
