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

#if NET9_0_OR_GREATER

using System;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// A real QUIC connection between two
    /// <see cref="QuicMultiplexedTransport"/> instances on the loopback
    /// interface, with the OPC UA ALPN identifier negotiated and the
    /// control stream established.
    /// </summary>
    internal sealed class QuicLoopback : IAsyncDisposable
    {
        private QuicLoopback(
            QuicListener listener,
            QuicMultiplexedTransport client,
            QuicMultiplexedTransport server,
            int port)
        {
            m_listener = listener;
            Client = client;
            Server = server;
            Port = port;
        }

        /// <summary>
        /// The client side of the connection.
        /// </summary>
        public QuicMultiplexedTransport Client { get; }

        /// <summary>
        /// The server side of the connection.
        /// </summary>
        public QuicMultiplexedTransport Server { get; }

        /// <summary>
        /// The UDP port the listener bound to.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Starts a listener on an ephemeral port, connects a client, and
        /// establishes the control stream on both sides.
        /// </summary>
        /// <param name="certificate">The certificate the server presents,
        /// which stands in for its Application Instance Certificate.</param>
        /// <param name="bufferManager">The pool both sides rent from.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public static Task<QuicLoopback> StartAsync(
            X509Certificate2 certificate,
            BufferManager bufferManager,
            ITelemetryContext telemetry)
        {
            return StartAsync(certificate, bufferManager, telemetry, reverseConnect: false);
        }

        /// <summary>
        /// Starts a reverse-connect loopback. The OPC UA Server owns the
        /// QUIC client role, while the OPC UA Client owns the QUIC server
        /// role.
        /// </summary>
        public static Task<QuicLoopback> StartReverseAsync(
            X509Certificate2 certificate,
            BufferManager bufferManager,
            ITelemetryContext telemetry)
        {
            return StartAsync(certificate, bufferManager, telemetry, reverseConnect: true);
        }

        private static async Task<QuicLoopback> StartAsync(
            X509Certificate2 certificate,
            BufferManager bufferManager,
            ITelemetryContext telemetry,
            bool reverseConnect)
        {
            var listenerOptions = new QuicListenerOptions
            {
                // Dual stack, like QuicTransportListener: the client below
                // connects to "localhost", which resolves to ::1 first on some
                // hosts, and a listener bound to the IPv4 loopback would never
                // see the handshake. .NET reports that as an ALPN failure
                // rather than a connect failure, which is thoroughly
                // misleading (dotnet/runtime#85412).
                ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0),
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
                            ServerCertificate = certificate,

                            // The loopback stands in for a conforming
                            // deployment, and Part 6 errata §7.6.1 makes the
                            // binding mutual: the TLS server requests a
                            // client certificate, and a connection that
                            // completes without one cannot carry data
                            // channels. Leaving it off here would let the
                            // data channel tests run in a configuration the
                            // specification forbids.
                            ClientCertificateRequired = true,
                            RemoteCertificateValidationCallback = (_, _, _, _) => true
                        }
                    })
            };

            QuicListener listener = await QuicListener
                .ListenAsync(listenerOptions)
                .ConfigureAwait(false);

            int port = listener.LocalEndPoint.Port;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            Task<QuicConnection> accept = listener
                .AcceptConnectionAsync(timeout.Token)
                .AsTask();

            var connector = new QuicMultiplexedTransport(
                bufferManager,
                65536,
                telemetry,
                new QuicClientOptions
                {
                    ServerCertificateValidation = (_, _, _, _) => true,
                    ClientCertificate = certificate
                });

            await connector
                .ConnectAsync(QuicTransport.CreateUrl("localhost", port), timeout.Token)
                .ConfigureAwait(false);

            QuicConnection acceptedConnection = await accept.ConfigureAwait(false);

            // A QUIC stream only materializes on the wire when something
            // is written to it, so the control stream is primed with one
            // minimal chunk. A real peer primes it with HEL or RHE; the
            // harness consumes the primer so every test starts from a
            // clean stream.
            await connector
                .SendChunkAsync(BuildPrimingChunk(), timeout.Token)
                .ConfigureAwait(false);

            QuicStream acceptedControl = await acceptedConnection
                .AcceptInboundStreamAsync(timeout.Token)
                .ConfigureAwait(false);

            var acceptor = new QuicMultiplexedTransport(
                acceptedConnection,
                acceptedControl,
                bufferManager,
                65536,
                telemetry);

            ArraySegment<byte> primer = await acceptor
                .ReceiveChunkAsync(timeout.Token)
                .ConfigureAwait(false);

            bufferManager.ReturnBuffer(primer.Array, nameof(StartAsync));

            return reverseConnect
                ? new QuicLoopback(listener, acceptor, connector, port)
                : new QuicLoopback(listener, connector, acceptor, port);
        }

        /// <summary>
        /// The smallest well-formed chunk: a message type and the size
        /// that follows it, and nothing else.
        /// </summary>
        private static byte[] BuildPrimingChunk()
        {
            byte[] chunk = new byte[8];
            chunk[0] = (byte)'A';
            chunk[1] = (byte)'C';
            chunk[2] = (byte)'K';
            chunk[3] = (byte)'F';
            BitConverter.GetBytes(8).CopyTo(chunk, 4);
            return chunk;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync().ConfigureAwait(false);
            await Server.DisposeAsync().ConfigureAwait(false);
            await m_listener.DisposeAsync().ConfigureAwait(false);
        }

        private readonly QuicListener m_listener;
    }
}

#endif
