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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Binding.Modbus
{
    /// <summary>
    /// A minimal, robust Modbus TCP client sufficient for the WoT Modbus binding
    /// forms: read coils / discrete inputs / holding registers / input registers
    /// and write single / multiple coils and holding registers. It manages the
    /// MBAP header, monotonically increasing transaction ids, request timeouts and
    /// device exception decoding.
    /// </summary>
    public sealed class ModbusTcpClient : IDisposable
    {
        /// <summary>Initializes a new Modbus TCP client.</summary>
        public ModbusTcpClient(string host, int port, TimeSpan timeout)
        {
            m_host = host ?? throw new ArgumentNullException(nameof(host));
            m_port = port <= 0 ? 502 : port;
            m_timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : timeout;
        }

        /// <summary>
        /// Connects (or reconnects) the underlying TCP socket. The connect is
        /// serialised with in-flight transactions so a reconnect after a fault is
        /// deterministic and thread-safe: any prior (possibly faulted) socket is
        /// disposed first and a fresh connection replaces it atomically.
        /// </summary>
        public async ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(m_timeout);
            await m_writeLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                // Dispose any prior (possibly faulted) connection so a reconnect
                // always starts from a clean, deterministic state.
                m_stream?.Dispose();
                m_client?.Dispose();
                m_stream = null;
                m_client = null;

                var client = new TcpClient { NoDelay = true };
                try
                {
                    await client.ConnectAsync(m_host, m_port, timeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
                m_client = client;
                m_stream = client.GetStream();
                m_faulted = false;
            }
            finally
            {
                m_writeLock.Release();
            }
        }

        /// <summary>Reads holding registers (function code 3).</summary>
        public ValueTask<ushort[]> ReadHoldingRegistersAsync(
            byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken)
            => ReadRegistersAsync(0x03, unitId, address, quantity, cancellationToken);

        /// <summary>Reads input registers (function code 4).</summary>
        public ValueTask<ushort[]> ReadInputRegistersAsync(
            byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken)
            => ReadRegistersAsync(0x04, unitId, address, quantity, cancellationToken);

        /// <summary>Reads coils (function code 1).</summary>
        public ValueTask<bool[]> ReadCoilsAsync(
            byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken)
            => ReadBitsAsync(0x01, unitId, address, quantity, cancellationToken);

        /// <summary>Reads discrete inputs (function code 2).</summary>
        public ValueTask<bool[]> ReadDiscreteInputsAsync(
            byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken)
            => ReadBitsAsync(0x02, unitId, address, quantity, cancellationToken);

        /// <summary>Writes a single holding register (function code 6).</summary>
        public async ValueTask WriteSingleRegisterAsync(
            byte unitId, ushort address, ushort value, CancellationToken cancellationToken)
        {
            byte[] pdu = { 0x06, Hi(address), Lo(address), Hi(value), Lo(value) };
            await TransactAsync(unitId, pdu, 0x06, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Writes multiple holding registers (function code 16).</summary>
        public async ValueTask WriteMultipleRegistersAsync(
            byte unitId, ushort address, ushort[] values, CancellationToken cancellationToken)
        {
            int count = values.Length;
            byte byteCount = (byte)(count * 2);
            byte[] pdu = new byte[6 + byteCount];
            pdu[0] = 0x10;
            pdu[1] = Hi(address);
            pdu[2] = Lo(address);
            pdu[3] = Hi((ushort)count);
            pdu[4] = Lo((ushort)count);
            pdu[5] = byteCount;
            for (int i = 0; i < count; i++)
            {
                pdu[6 + (i * 2)] = Hi(values[i]);
                pdu[7 + (i * 2)] = Lo(values[i]);
            }
            await TransactAsync(unitId, pdu, 0x10, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Writes a single coil (function code 5).</summary>
        public async ValueTask WriteSingleCoilAsync(
            byte unitId, ushort address, bool value, CancellationToken cancellationToken)
        {
            byte[] pdu = { 0x05, Hi(address), Lo(address), value ? (byte)0xFF : (byte)0x00, 0x00 };
            await TransactAsync(unitId, pdu, 0x05, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Writes multiple coils (function code 15).</summary>
        public async ValueTask WriteMultipleCoilsAsync(
            byte unitId, ushort address, bool[] values, CancellationToken cancellationToken)
        {
            int count = values.Length;
            byte byteCount = (byte)((count + 7) / 8);
            byte[] pdu = new byte[6 + byteCount];
            pdu[0] = 0x0F;
            pdu[1] = Hi(address);
            pdu[2] = Lo(address);
            pdu[3] = Hi((ushort)count);
            pdu[4] = Lo((ushort)count);
            pdu[5] = byteCount;
            for (int i = 0; i < count; i++)
            {
                if (values[i])
                {
                    pdu[6 + (i / 8)] |= (byte)(1 << (i % 8));
                }
            }
            await TransactAsync(unitId, pdu, 0x0F, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_stream?.Dispose();
            m_client?.Dispose();
            m_writeLock.Dispose();
        }

        private async ValueTask<ushort[]> ReadRegistersAsync(
            byte function, byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken)
        {
            byte[] pdu = { function, Hi(address), Lo(address), Hi(quantity), Lo(quantity) };
            byte[] response = await TransactAsync(unitId, pdu, function, cancellationToken).ConfigureAwait(false);
            // response[0] is the (already validated) function code; response[1] is
            // the byte count. Validate both the byte count and the overall length
            // before indexing so a hostile or truncated frame cannot read out of
            // bounds.
            if (response.Length < 2)
            {
                throw new ModbusException("The Modbus register response is missing its byte count.");
            }
            int byteCount = response[1];
            int expected = quantity * 2;
            if (byteCount != expected || (byteCount & 1) != 0 || response.Length < 2 + byteCount)
            {
                throw new ModbusException(
                    "The Modbus register response byte count is inconsistent with the request.");
            }
            var registers = new ushort[byteCount / 2];
            for (int i = 0; i < registers.Length; i++)
            {
                registers[i] = (ushort)((response[2 + (i * 2)] << 8) | response[3 + (i * 2)]);
            }
            return registers;
        }

        private async ValueTask<bool[]> ReadBitsAsync(
            byte function, byte unitId, ushort address, ushort quantity, CancellationToken cancellationToken)
        {
            byte[] pdu = { function, Hi(address), Lo(address), Hi(quantity), Lo(quantity) };
            byte[] response = await TransactAsync(unitId, pdu, function, cancellationToken).ConfigureAwait(false);
            // response[1] is the packed-bit byte count. Validate it against the
            // requested quantity and the frame length before indexing.
            if (response.Length < 2)
            {
                throw new ModbusException("The Modbus bit response is missing its byte count.");
            }
            int byteCount = response[1];
            int expected = (quantity + 7) / 8;
            if (byteCount != expected || response.Length < 2 + byteCount)
            {
                throw new ModbusException(
                    "The Modbus bit response byte count is inconsistent with the request.");
            }
            var bits = new bool[quantity];
            for (int i = 0; i < quantity; i++)
            {
                int byteIndex = 2 + (i / 8);
                bits[i] = (response[byteIndex] & (1 << (i % 8))) != 0;
            }
            return bits;
        }

        private async ValueTask<byte[]> TransactAsync(
            byte unitId, byte[] pdu, byte expectedFunction, CancellationToken cancellationToken)
        {
            ushort transactionId = unchecked((ushort)Interlocked.Increment(ref m_transaction));
            int length = pdu.Length + 1;
            byte[] frame = new byte[7 + pdu.Length];
            frame[0] = Hi(transactionId);
            frame[1] = Lo(transactionId);
            frame[2] = 0x00;
            frame[3] = 0x00;
            frame[4] = Hi((ushort)length);
            frame[5] = Lo((ushort)length);
            frame[6] = unitId;
            Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(m_timeout);
            await m_writeLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                NetworkStream? stream = m_stream;
                if (stream is null)
                {
                    throw new ModbusException(m_faulted
                        ? "The Modbus connection was faulted by a previous error and must be reconnected."
                        : "The Modbus client is not connected.");
                }

                byte[] responsePdu;
                try
                {
                    await stream.WriteAsync(frame.AsMemory(), timeout.Token).ConfigureAwait(false);
                    await stream.FlushAsync(timeout.Token).ConfigureAwait(false);

                    byte[] header = await ReadExactAsync(stream, 7, timeout.Token).ConfigureAwait(false);
                    if (header[0] != Hi(transactionId) || header[1] != Lo(transactionId))
                    {
                        throw new ModbusException("The Modbus transaction id did not match.");
                    }
                    int responseLength = ((header[4] << 8) | header[5]) - 1;
                    if (responseLength < 1)
                    {
                        throw new ModbusException("The Modbus response length is invalid.");
                    }
                    responsePdu = await ReadExactAsync(stream, responseLength, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (
                    ex is OperationCanceledException or System.IO.IOException or
                          SocketException or ObjectDisposedException or ModbusException)
                {
                    // A timeout, cancellation, transport error, transaction-id
                    // mismatch or truncated/invalid frame leaves the stream in an
                    // unknown, desynchronized state. Fault the connection so every
                    // subsequent operation fails fast until a fresh ConnectAsync
                    // re-establishes the socket.
                    FaultConnection();
                    throw;
                }

                // The response was framed by the MBAP length and read in full, so
                // the stream stays synchronized: a device exception or an
                // unexpected function code is a protocol result, not a desync, and
                // must not fault the connection.
                byte function = responsePdu[0];
                if ((function & 0x80) != 0)
                {
                    byte exceptionCode = responsePdu.Length > 1 ? responsePdu[1] : (byte)0;
                    throw new ModbusException(exceptionCode, DescribeException(exceptionCode));
                }
                if (function != expectedFunction)
                {
                    throw new ModbusException(
                        $"Unexpected Modbus function 0x{function:X2} (expected 0x{expectedFunction:X2}).");
                }
                return responsePdu;
            }
            finally
            {
                m_writeLock.Release();
            }
        }

        /// <summary>
        /// Disposes and clears the current socket after a desynchronizing fault so
        /// the next operation requires a fresh <see cref="ConnectAsync"/>. Always
        /// called while holding <see cref="m_writeLock"/>.
        /// </summary>
        private void FaultConnection()
        {
            m_faulted = true;
            m_stream?.Dispose();
            m_client?.Dispose();
            m_stream = null;
            m_client = null;
        }

        private static async ValueTask<byte[]> ReadExactAsync(
            NetworkStream stream, int count, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await stream
                    .ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new ModbusException("The Modbus connection was closed by the peer.");
                }
                offset += read;
            }
            return buffer;
        }

        private static string DescribeException(byte code)
        {
            return code switch
            {
                0x01 => "Illegal function.",
                0x02 => "Illegal data address.",
                0x03 => "Illegal data value.",
                0x04 => "Server device failure.",
                0x06 => "Server device busy.",
                _ => $"Modbus exception 0x{code:X2}."
            };
        }

        private static byte Hi(ushort value) => (byte)(value >> 8);

        private static byte Lo(ushort value) => (byte)(value & 0xFF);

        private readonly string m_host;
        private readonly int m_port;
        private readonly TimeSpan m_timeout;
        private readonly SemaphoreSlim m_writeLock = new SemaphoreSlim(1, 1);
        private TcpClient? m_client;
        private NetworkStream? m_stream;
        private int m_transaction;
        private bool m_faulted;
    }
}
