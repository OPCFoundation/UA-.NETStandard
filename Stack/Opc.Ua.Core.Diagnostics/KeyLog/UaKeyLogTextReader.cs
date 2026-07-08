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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.KeyLog
{
    /// <summary>
    /// Reads OPC UA channel key material from Wireshark-style text key logs.
    /// </summary>
    public sealed class UaKeyLogTextReader : IKeyLogReader
    {
        /// <summary>
        /// Constructs a key-log reader.
        /// </summary>
        public UaKeyLogTextReader()
        {
        }

        /// <summary>
        /// Constructs a key-log reader bound to the supplied file path.
        /// </summary>
        public UaKeyLogTextReader(string filePath)
            : this(filePath, sessionKey: null)
        {
        }

        /// <summary>
        /// Constructs a key-log reader bound to the supplied file path.
        /// </summary>
        public UaKeyLogTextReader(string filePath, byte[]? sessionKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            FilePath = filePath;
            m_sessionKey = sessionKey;
        }

        /// <summary>
        /// Gets the bound file path, if the reader was constructed with one.
        /// </summary>
        public string? FilePath { get; }

        /// <summary>
        /// Reads all key material from the bound file path.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public IAsyncEnumerable<ChannelKeyMaterial> ReadAllAsync(CancellationToken ct)
        {
            if (FilePath is null)
            {
                throw new InvalidOperationException("The reader is not bound to a file path.");
            }

            return ReadAllAsync(FilePath, ct);
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<ChannelKeyMaterial> ReadAllAsync(
            string filePath,
            CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            return ReadAllFromFileAsync(filePath, ct);
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<ChannelKeyMaterial> ReadAllAsync(Stream stream, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(stream);
            Stream readStream = CreateReadStream(stream, leaveOpen: true);
            return ReadAllFromStreamAsync(readStream, disposeStream: !ReferenceEquals(readStream, stream), ct);
        }

        private async IAsyncEnumerable<ChannelKeyMaterial> ReadAllFromFileAsync(
            string filePath,
            [EnumeratorCancellation] CancellationToken ct)
        {
            FileStream fileStream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using Stream stream = CreateReadStream(fileStream, leaveOpen: false);
            // CA2000: ReadAllFromStreamAsync returns IAsyncEnumerable; the iterator is
            // async-disposed by `await foreach`. The stream above is disposed by `using`.
#pragma warning disable CA2000
            await foreach (ChannelKeyMaterial material in ReadAllFromStreamAsync(
                stream,
                disposeStream: false,
                ct).ConfigureAwait(false))
            {
                yield return material;
            }
#pragma warning restore CA2000
        }

        private Stream CreateReadStream(Stream stream, bool leaveOpen)
        {
            return m_sessionKey is null ? stream : new EncryptedKeyLogStream(stream, m_sessionKey, leaveOpen);
        }

        private static async IAsyncEnumerable<ChannelKeyMaterial> ReadAllFromStreamAsync(
            Stream stream,
            bool disposeStream,
            [EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                using StreamReader reader = new(stream, leaveOpen: true);
                while (true)
                {
                    string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        yield break;
                    }
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    {
                        continue;
                    }

                    yield return ParseLine(line);
                }
            }
            finally
            {
                if (disposeStream)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static ChannelKeyMaterial ParseLine(string line)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 11 || !string.Equals(parts[0], "OPCUA_CHANNEL", StringComparison.Ordinal))
            {
                throw new PcapDiagnosticsException("Invalid OPC UA text key-log record.");
            }
            if (!Enum.TryParse(parts[4], ignoreCase: false, out MessageSecurityMode mode))
            {
                throw new PcapDiagnosticsException($"Invalid OPC UA key-log security mode '{parts[4]}'.");
            }

            return new ChannelKeyMaterial(
                ParseUInt32(parts[1]),
                ParseUInt32(parts[2]),
                parts[3],
                mode,
                DateTime.UtcNow,
                0,
                null,
                null,
                ParseHex(parts[5]),
                ParseHex(parts[6]),
                ParseHex(parts[7]),
                ParseHex(parts[8]),
                ParseHex(parts[9]),
                ParseHex(parts[10]));
        }

        private static uint ParseUInt32(string value)
        {
            if (!value.StartsWith("0x", StringComparison.Ordinal))
            {
                throw new PcapDiagnosticsException($"Invalid OPC UA key-log integer '{value}'.");
            }
            return uint.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static byte[]? ParseHex(string value)
        {
            return value == "-" ? null : Convert.FromHexString(value);
        }

        private readonly byte[]? m_sessionKey;
    }
}
