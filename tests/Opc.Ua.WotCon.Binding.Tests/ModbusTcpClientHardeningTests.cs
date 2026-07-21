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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding.Modbus;

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>
    /// Hardening tests for <see cref="ModbusTcpClient"/>: hostile / truncated
    /// responses must map to a <see cref="ModbusException"/> (never an out-of-range
    /// index), and a timeout must fault the connection so a fresh reconnect is
    /// required and works deterministically.
    /// </summary>
    [TestFixture]
    public sealed class ModbusTcpClientHardeningTests
    {
        [Test]
        public async Task TruncatedRegisterResponse_ThrowsModbusException()
        {
            // A register-read response whose declared byte count (4) exceeds the
            // register bytes actually present in the frame. Before the bounds
            // check this indexed out of range; now it maps to a ModbusException.
            using var server = new ScriptedModbusServer((_, pdu) =>
                pdu[0] == 0x03
                    ? new byte[] { 0x03, 0x04, 0x00, 0x2A } // byteCount 4, only 1 register present
                    : null);

            using var client = new ModbusTcpClient("127.0.0.1", server.Port, TimeSpan.FromSeconds(2));
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.ThrowsAsync<ModbusException>(async () =>
                await client.ReadHoldingRegistersAsync(1, 0, 2, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public async Task TruncatedBitResponse_ThrowsModbusException()
        {
            // A coil-read response claiming a byte count (2) larger than the frame
            // carries, so a naive read would index past the end of the buffer.
            using var server = new ScriptedModbusServer((_, pdu) =>
                pdu[0] == 0x01
                    ? new byte[] { 0x01, 0x02, 0x01 } // byteCount 2, only 1 data byte present
                    : null);

            using var client = new ModbusTcpClient("127.0.0.1", server.Port, TimeSpan.FromSeconds(2));
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.ThrowsAsync<ModbusException>(async () =>
                await client.ReadCoilsAsync(1, 0, 9, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public async Task Timeout_FaultsConnection_ThenReconnectSucceeds()
        {
            using var server = new ScriptedModbusServer((connection, pdu) =>
            {
                // First connection: never respond so the client times out. Second
                // (reconnect) connection: answer the register read normally.
                if (connection == 0)
                {
                    return null;
                }
                return new byte[] { 0x03, 0x02, 0x12, 0x34 };
            });

            using var client = new ModbusTcpClient("127.0.0.1", server.Port, TimeSpan.FromMilliseconds(300));
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            // The silent server causes the request to time out.
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await client.ReadHoldingRegistersAsync(1, 0, 1, CancellationToken.None).ConfigureAwait(false));

            // The connection is now faulted: a follow-up operation fails fast and
            // deterministically (it does not hang or reuse the desynced stream)
            // and reports that a reconnect is required.
            ModbusException? fault = Assert.ThrowsAsync<ModbusException>(async () =>
                await client.ReadHoldingRegistersAsync(1, 0, 1, CancellationToken.None).ConfigureAwait(false));
            Assert.That(fault!.Message, Does.Contain("reconnect").IgnoreCase);

            // A fresh connect re-establishes the socket and the read now succeeds.
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            ushort[] registers = await client
                .ReadHoldingRegistersAsync(1, 0, 1, CancellationToken.None).ConfigureAwait(false);
            Assert.That(registers, Has.Length.EqualTo(1));
            Assert.That(registers[0], Is.EqualTo((ushort)0x1234));
        }

        /// <summary>
        /// A minimal Modbus TCP listener whose per-request response is supplied by
        /// a script. The script receives the zero-based accepted-connection index
        /// and the request PDU and returns the response PDU, or <c>null</c> to hold
        /// the connection open without responding (used to provoke a timeout).
        /// </summary>
        private sealed class ScriptedModbusServer : IDisposable
        {
            public ScriptedModbusServer(Func<int, byte[], byte[]?> responder)
            {
                m_responder = responder;
                m_listener = new TcpListener(IPAddress.Loopback, 0);
                m_listener.Start();
                Port = ((IPEndPoint)m_listener.LocalEndpoint).Port;
                m_loop = Task.Run(AcceptLoopAsync);
            }

            public int Port { get; }

            public void Dispose()
            {
                m_cts.Cancel();
                m_listener.Stop();
                m_listener.Dispose();
                try
                {
                    m_loop.Wait(2000);
                }
                catch (AggregateException)
                {
                    // Ignore teardown faults.
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
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        return;
                    }
                    int connection = m_connections++;
                    _ = Task.Run(() => ServeAsync(client, connection));
                }
            }

            private async Task ServeAsync(TcpClient client, int connection)
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    try
                    {
                        while (!m_cts.IsCancellationRequested)
                        {
                            byte[]? header = await ReadExactAsync(stream, 6).ConfigureAwait(false);
                            if (header is null)
                            {
                                return;
                            }
                            int length = (header[4] << 8) | header[5];
                            byte[]? rest = await ReadExactAsync(stream, length).ConfigureAwait(false);
                            if (rest is null)
                            {
                                return;
                            }
                            byte unit = rest[0];
                            byte[] pdu = new byte[rest.Length - 1];
                            Array.Copy(rest, 1, pdu, 0, pdu.Length);

                            byte[]? responsePdu = m_responder(connection, pdu);
                            if (responsePdu is null)
                            {
                                // Hold the connection open without answering so the
                                // client's request times out.
                                await Task.Delay(Timeout.Infinite, m_cts.Token).ConfigureAwait(false);
                                return;
                            }

                            byte[] frame = BuildFrame(header[0], header[1], unit, responsePdu);
                            await stream.WriteAsync(frame).ConfigureAwait(false);
                            await stream.FlushAsync().ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Server shutting down.
                    }
                    catch (System.IO.IOException)
                    {
                        // Client disconnected.
                    }
                }
            }

            private static byte[] BuildFrame(byte txnHi, byte txnLo, byte unit, byte[] pdu)
            {
                int length = pdu.Length + 1;
                byte[] frame = new byte[7 + pdu.Length];
                frame[0] = txnHi;
                frame[1] = txnLo;
                frame[2] = 0x00;
                frame[3] = 0x00;
                frame[4] = (byte)(length >> 8);
                frame[5] = (byte)(length & 0xFF);
                frame[6] = unit;
                Array.Copy(pdu, 0, frame, 7, pdu.Length);
                return frame;
            }

            private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count)
            {
                byte[] buffer = new byte[count];
                int offset = 0;
                while (offset < count)
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset)).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return null;
                    }
                    offset += read;
                }
                return buffer;
            }

            private readonly Func<int, byte[], byte[]?> m_responder;
            private readonly TcpListener m_listener;
            private readonly Task m_loop;
            private readonly CancellationTokenSource m_cts = new CancellationTokenSource();
            private int m_connections;
        }
    }
}
