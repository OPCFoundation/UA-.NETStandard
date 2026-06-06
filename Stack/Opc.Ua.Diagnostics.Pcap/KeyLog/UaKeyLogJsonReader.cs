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
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Diagnostics.Pcap.Capture;

namespace Opc.Ua.Diagnostics.Pcap.KeyLog
{
    /// <summary>
    /// Reads OPC UA channel key material from JSON-lines key-log files.
    /// </summary>
    public sealed class UaKeyLogJsonReader : IKeyLogReader
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
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                KeyLogRecord? record = JsonSerializer.Deserialize(
                    line,
                    UaKeyLogJsonContext.Default.KeyLogRecord);
                if (record is null)
                {
                    throw new PcapDiagnosticsException("Invalid OPC UA JSON key-log record.");
                }
                yield return record.ToMaterial();
            }
        }
    }
}
