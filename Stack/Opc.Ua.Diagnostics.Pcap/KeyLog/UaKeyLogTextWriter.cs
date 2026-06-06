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
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Diagnostics.Pcap.KeyLog
{
    /// <summary>
    /// Writes OPC UA channel key material in a Wireshark-style text format.
    /// </summary>
    public sealed class UaKeyLogTextWriter : IKeyLogWriter
    {
        /// <summary>
        /// Constructs a text key-log writer for the supplied file.
        /// </summary>
        public UaKeyLogTextWriter(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            FilePath = filePath;
            m_stream = new FileStream(
                filePath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            m_needsHeader = m_stream.Length == 0;
            m_stream.Seek(0, SeekOrigin.End);
            m_writer = new StreamWriter(m_stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
        }

        /// <inheritdoc/>
        public string FilePath { get; }

        /// <inheritdoc/>
        public async ValueTask AppendAsync(ChannelKeyMaterial material, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(material);
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_needsHeader)
                {
                    await m_writer.WriteLineAsync("# OPC UA channel key log v1".AsMemory(), ct).ConfigureAwait(false);
                    await m_writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, ct).ConfigureAwait(false);
                    m_needsHeader = false;
                }

                await m_writer.WriteLineAsync(FormatLine(material).AsMemory(), ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask FlushAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await m_writer.FlushAsync(ct).ConfigureAwait(false);
                await m_stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
            m_disposed = true;
            await m_writer.DisposeAsync().ConfigureAwait(false);
            await m_stream.DisposeAsync().ConfigureAwait(false);
            m_gate.Dispose();
        }

        internal static string FormatLine(ChannelKeyMaterial material)
        {
            return string.Join(
                ' ',
                "OPCUA_CHANNEL",
                string.Create(CultureInfo.InvariantCulture, $"0x{material.ChannelId:X}"),
                string.Create(CultureInfo.InvariantCulture, $"0x{material.TokenId:X}"),
                material.SecurityPolicyUri,
                material.SecurityMode.ToString(),
                ToHex(material.ClientSigningKey),
                ToHex(material.ClientEncryptingKey),
                ToHex(material.ClientInitializationVector),
                ToHex(material.ServerSigningKey),
                ToHex(material.ServerEncryptingKey),
                ToHex(material.ServerInitializationVector));
        }

        private static string ToHex(byte[]? value)
        {
            return value is { Length: > 0 } ? Convert.ToHexString(value) : "-";
        }

        private readonly FileStream m_stream;
        private readonly StreamWriter m_writer;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private bool m_disposed;
        private bool m_needsHeader;
    }
}
