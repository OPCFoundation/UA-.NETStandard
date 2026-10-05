/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// A client that opens a secured channel must not accept an
    /// OpenSecureChannel response secured with another policy - in
    /// particular an unsigned SecurityPolicy#None response a man in the
    /// middle can forge without any key (OPC 10000-4 5.6.2.1,
    /// OPC 10000-6 6.7.2.3). A session reconnect builds a new client
    /// channel the same way, so the same check covers it.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class ClientOpenSecureChannelDowngradeTests
    {
        [Test]
        [CancelAfter(30000)]
        public async Task SecuredOpenRejectsUnsignedNoneResponseAsync()
        {
            using var server = new ForgingServer(NUnitTelemetryContext.Create());
            server.Start();
            using X509Certificate2 serverCertificate = CreateCertificate("CN=server");
            using X509Certificate2 clientCertificate = CreateCertificate("CN=client");

            using UaSCUaBinaryTransportChannel channel = CreateClient();
            TransportChannelSettings settings = CreateSettings(
                server.Url,
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Basic256Sha256,
                serverCertificate,
                clientCertificate);

            Assert.That(
                async () => await channel.OpenAsync(server.Url, settings, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>());
            Assert.That(server.ForgedResponses, Is.EqualTo(1), "the forged response was never sent");
            Assert.That(
                server.CleartextMessages,
                Is.Zero,
                "the client sent a service request over the downgraded channel");
        }

        [Test]
        [CancelAfter(30000)]
        public async Task UnsecuredOpenStillAcceptsNoneResponseAsync()
        {
            using var server = new ForgingServer(NUnitTelemetryContext.Create());
            server.Start();

            using UaSCUaBinaryTransportChannel channel = CreateClient();
            TransportChannelSettings settings = CreateSettings(
                server.Url,
                MessageSecurityMode.None,
                SecurityPolicies.None,
                null,
                null);

            await channel.OpenAsync(server.Url, settings, CancellationToken.None).ConfigureAwait(false);

            Assert.That(server.ForgedResponses, Is.EqualTo(1));
        }

        private static UaSCUaBinaryTransportChannel CreateClient()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new UaSCUaBinaryTransportChannel(new TcpMessageSocketFactory(telemetry), telemetry)
            {
                OperationTimeout = 5000
            };
        }

        private static X509Certificate2 CreateCertificate(string subject)
        {
            return CertificateFactory
                .CreateCertificate(
                    "urn:localhost:" + subject.Substring(3),
                    subject.Substring(3),
                    subject,
                    null)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static TransportChannelSettings CreateSettings(
            Uri url,
            MessageSecurityMode securityMode,
            string securityPolicyUri,
            X509Certificate2 serverCertificate,
            X509Certificate2 clientCertificate)
        {
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;
            configuration.MaxMessageSize = 64 * 1024;
            configuration.MaxBufferSize = 64 * 1024;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;

            return new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = url.ToString(),
                    SecurityMode = securityMode,
                    SecurityPolicyUri = securityPolicyUri,
                    TransportProfileUri = Profiles.UaTcpTransport,
                    ServerCertificate = serverCertificate?.RawData
                },
                Configuration = configuration,
                ClientCertificate = clientCertificate,
                ServerCertificate = serverCertificate,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create()
            };
        }

        /// <summary>
        /// A man in the middle that answers the client's Hello and
        /// OpenSecureChannel request itself with an unsigned
        /// SecurityPolicy#None response.
        /// </summary>
        private sealed class ForgingServer : IDisposable
        {
            public ForgingServer(ITelemetryContext telemetry)
            {
                m_context = new ServiceMessageContext(telemetry);
                m_listener = new TcpListener(IPAddress.Loopback, 0);
                m_listener.Start();
                Url = new Uri($"opc.tcp://127.0.0.1:{((IPEndPoint)m_listener.LocalEndpoint).Port}");
            }

            public Uri Url { get; }

            public int ForgedResponses => Volatile.Read(ref m_forgedResponses);

            public int CleartextMessages => Volatile.Read(ref m_cleartextMessages);

            public void Start()
            {
                m_acceptLoop = Task.Run(AcceptLoopAsync);
            }

            public void Dispose()
            {
                m_cts.Cancel();
                m_listener.Stop();
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
                    catch (Exception)
                    {
                        return;
                    }

                    _ = AnswerWithNoneAsync(client);
                }
            }

            private async Task AnswerWithNoneAsync(TcpClient client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();

                    byte[] hello = await ReadChunkAsync(stream).ConfigureAwait(false);
                    Assert.That(
                        BinaryPrimitives.ReadUInt32LittleEndian(hello),
                        Is.EqualTo(TcpMessageType.Hello));
                    byte[] acknowledge = BuildAcknowledge();
                    await stream.WriteAsync(acknowledge, 0, acknowledge.Length, m_cts.Token)
                        .ConfigureAwait(false);

                    byte[] open = await ReadChunkAsync(stream).ConfigureAwait(false);
                    Assert.That(
                        BinaryPrimitives.ReadUInt32LittleEndian(open),
                        Is.EqualTo(TcpMessageType.Open | TcpMessageType.Final));
                    byte[] response = BuildNoneOpenResponse();
                    await stream.WriteAsync(response, 0, response.Length, m_cts.Token)
                        .ConfigureAwait(false);
                    Interlocked.Increment(ref m_forgedResponses);

                    // anything the client sends from now on would travel in cleartext.
                    while (!m_cts.IsCancellationRequested)
                    {
                        byte[] chunk = await ReadChunkAsync(stream).ConfigureAwait(false);
                        if ((BinaryPrimitives.ReadUInt32LittleEndian(chunk) &
                            TcpMessageType.MessageTypeMask) == TcpMessageType.Message)
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
                await ReadExactlyAsync(stream, chunk, header.Length, size - header.Length)
                    .ConfigureAwait(false);
                return chunk;
            }

            private async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
                    int read = await stream.ReadAsync(buffer, offset, count, m_cts.Token)
                        .ConfigureAwait(false);
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
                    encoder.WriteUInt32(null, 65536); // receive buffer size
                    encoder.WriteUInt32(null, 65536); // send buffer size
                    encoder.WriteUInt32(null, 0); // max message size
                    encoder.WriteUInt32(null, 0); // max chunk count
                });
            }

            private byte[] BuildNoneOpenResponse()
            {
                var response = new OpenSecureChannelResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                    ServerProtocolVersion = 0,
                    SecurityToken = new ChannelSecurityToken
                    {
                        ChannelId = 77,
                        TokenId = 1,
                        RevisedLifetime = 600000
                    },
                    ServerNonce = []
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
                    encoder.WriteRawBytes(body, 0, body.Length);
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

            private readonly IServiceMessageContext m_context;
            private readonly TcpListener m_listener;
            private readonly CancellationTokenSource m_cts = new();
            private Task m_acceptLoop;
            private int m_forgedResponses;
            private int m_cleartextMessages;
        }
    }
}
