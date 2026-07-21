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

namespace Opc.Ua.WotCon.Binding.Tests.Support
{
    /// <summary>
    /// A minimal in-process Modbus TCP server / simulator supporting the read and
    /// write function codes required by the WoT Modbus binding (FC 1/3/4/5/6/16).
    /// </summary>
    public sealed class TestModbusServer : IDisposable
    {
        public TestModbusServer()
        {
            m_listener = new TcpListener(IPAddress.Loopback, 0);
            m_listener.Start();
            Port = ((IPEndPoint)m_listener.LocalEndpoint).Port;
            m_loop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public ushort[] HoldingRegisters { get; } = new ushort[1024];

        public ushort[] InputRegisters { get; } = new ushort[1024];

        public bool[] Coils { get; } = new bool[1024];

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
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
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
                        byte[] responsePdu = Process(pdu);
                        byte[] frame = BuildFrame(header[0], header[1], unit, responsePdu);
                        await stream.WriteAsync(frame).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                    }
                }
                catch (System.IO.IOException)
                {
                    // Client disconnected.
                }
            }
        }

        private byte[] Process(byte[] pdu)
        {
            byte function = pdu[0];
            switch (function)
            {
                case 0x01:
                    return ReadBits(pdu, Coils, function);
                case 0x03:
                    return ReadRegisters(pdu, HoldingRegisters, function);
                case 0x04:
                    return ReadRegisters(pdu, InputRegisters, function);
                case 0x05:
                {
                    int address = (pdu[1] << 8) | pdu[2];
                    Coils[address] = pdu[3] == 0xFF;
                    return new[] { function, pdu[1], pdu[2], pdu[3], pdu[4] };
                }
                case 0x06:
                {
                    int address = (pdu[1] << 8) | pdu[2];
                    HoldingRegisters[address] = (ushort)((pdu[3] << 8) | pdu[4]);
                    return new[] { function, pdu[1], pdu[2], pdu[3], pdu[4] };
                }
                case 0x10:
                {
                    int address = (pdu[1] << 8) | pdu[2];
                    int quantity = (pdu[3] << 8) | pdu[4];
                    for (int i = 0; i < quantity; i++)
                    {
                        HoldingRegisters[address + i] = (ushort)((pdu[6 + (i * 2)] << 8) | pdu[7 + (i * 2)]);
                    }
                    return new[] { function, pdu[1], pdu[2], pdu[3], pdu[4] };
                }
                default:
                    return new[] { (byte)(function | 0x80), (byte)0x01 };
            }
        }

        private static byte[] ReadRegisters(byte[] pdu, ushort[] store, byte function)
        {
            int address = (pdu[1] << 8) | pdu[2];
            int quantity = (pdu[3] << 8) | pdu[4];
            byte[] response = new byte[2 + (quantity * 2)];
            response[0] = function;
            response[1] = (byte)(quantity * 2);
            for (int i = 0; i < quantity; i++)
            {
                response[2 + (i * 2)] = (byte)(store[address + i] >> 8);
                response[3 + (i * 2)] = (byte)(store[address + i] & 0xFF);
            }
            return response;
        }

        private static byte[] ReadBits(byte[] pdu, bool[] store, byte function)
        {
            int address = (pdu[1] << 8) | pdu[2];
            int quantity = (pdu[3] << 8) | pdu[4];
            int byteCount = (quantity + 7) / 8;
            byte[] response = new byte[2 + byteCount];
            response[0] = function;
            response[1] = (byte)byteCount;
            for (int i = 0; i < quantity; i++)
            {
                if (store[address + i])
                {
                    response[2 + (i / 8)] |= (byte)(1 << (i % 8));
                }
            }
            return response;
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

        private readonly TcpListener m_listener;
        private readonly Task m_loop;
        private readonly CancellationTokenSource m_cts = new CancellationTokenSource();
    }
}
