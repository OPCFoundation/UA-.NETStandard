// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

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
                PubSubKeyLogJsonContext.Default.PubSubKeyLogRecord) ??
                throw new FormatException("Invalid PubSub JSON key-log record.");
            return record.ToMaterial();
        }
    }
}
