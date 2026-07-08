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

namespace Opc.Ua.PubSub.Pcap.KeyLog
{
    /// <summary>
    /// Reads PubSub security key material from JSON-lines key-log files.
    /// </summary>
    public sealed class PubSubKeyLogReader
    {
        /// <summary>
        /// Constructs a key-log reader.
        /// </summary>
        public PubSubKeyLogReader()
        {
        }

        /// <summary>
        /// Constructs a key-log reader bound to the supplied file path.
        /// </summary>
        /// <param name="filePath">Key-log file path.</param>
        public PubSubKeyLogReader(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            FilePath = filePath;
        }

        /// <summary>
        /// Gets the bound file path, if the reader was constructed with one.
        /// </summary>
        public string? FilePath { get; }

        /// <summary>
        /// Reads all key material from the bound file path.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="InvalidOperationException"></exception>
        public IAsyncEnumerable<PubSubKeyMaterial> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            if (FilePath is null)
            {
                throw new InvalidOperationException("The reader is not bound to a file path.");
            }

            return ReadAllAsync(FilePath, cancellationToken);
        }

        /// <summary>
        /// Reads all key material from the supplied file path.
        /// </summary>
        /// <param name="filePath">Key-log file path.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public IAsyncEnumerable<PubSubKeyMaterial> ReadAllAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            return ReadAllFromFileAsync(filePath, cancellationToken);
        }

        /// <summary>
        /// Reads all key material from the supplied stream.
        /// </summary>
        /// <param name="stream">Stream containing JSON-lines key-log records.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public IAsyncEnumerable<PubSubKeyMaterial> ReadAllAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return ReadAllFromStreamAsync(stream, disposeStream: false, cancellationToken);
        }

        private static async IAsyncEnumerable<PubSubKeyMaterial> ReadAllFromFileAsync(
            string filePath,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using StreamReader reader = new(stream, leaveOpen: false);
            while (true)
            {
                string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    yield break;
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                yield return Deserialize(line);
            }
        }

        private static async IAsyncEnumerable<PubSubKeyMaterial> ReadAllFromStreamAsync(
            Stream stream,
            bool disposeStream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                using StreamReader reader = new(stream, leaveOpen: true);
                while (true)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        yield break;
                    }
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    yield return Deserialize(line);
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

        private static PubSubKeyMaterial Deserialize(string line)
        {
            PubSubKeyLogRecord? record = JsonSerializer.Deserialize(
                line,
                PubSubKeyLogJsonContext.Default.PubSubKeyLogRecord);
            if (record is null)
            {
                throw new FormatException("Invalid PubSub JSON key-log record.");
            }
            return record.ToMaterial();
        }
    }
}
