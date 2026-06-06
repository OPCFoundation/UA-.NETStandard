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
using Opc.Ua.Diagnostics.Pcap.Capture;

namespace Opc.Ua.Diagnostics.Pcap.KeyLog
{
    /// <summary>
    /// Reads OPC UA channel key material from Wireshark-style text key logs.
    /// </summary>
    public sealed class UaKeyLogTextReader : IKeyLogReader
    {
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
            return ReadAllFromStreamAsync(stream, ct);
        }

        private async IAsyncEnumerable<ChannelKeyMaterial> ReadAllFromFileAsync(
            string filePath,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await foreach (ChannelKeyMaterial material in ReadAllFromStreamAsync(stream, ct).ConfigureAwait(false))
            {
                yield return material;
            }
        }

        private static async IAsyncEnumerable<ChannelKeyMaterial> ReadAllFromStreamAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken ct)
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
    }
}
