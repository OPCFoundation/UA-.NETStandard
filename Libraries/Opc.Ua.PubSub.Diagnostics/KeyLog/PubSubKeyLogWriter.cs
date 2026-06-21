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
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Pcap.KeyLog
{
    /// <summary>
    /// Writes PubSub security key material as JSON-lines records.
    /// </summary>
    public sealed class PubSubKeyLogWriter : IAsyncDisposable
    {
        /// <summary>
        /// Constructs a JSON-lines key-log writer for the supplied file.
        /// </summary>
        /// <param name="filePath">Key-log file path.</param>
        public PubSubKeyLogWriter(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            FilePath = filePath;
            m_fileStream = new FileStream(
                filePath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            m_fileStream.Seek(0, SeekOrigin.End);
            m_writer = new StreamWriter(m_fileStream, System.Text.Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
        }

        /// <summary>
        /// Gets the file path receiving JSON-lines key-log records.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Appends one PubSub key-material record.
        /// </summary>
        /// <param name="material">Key material to persist.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask AppendAsync(
            PubSubKeyMaterial material,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(material);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                string json = JsonSerializer.Serialize(
                    PubSubKeyLogRecord.From(material),
                    PubSubKeyLogJsonContext.Default.PubSubKeyLogRecord);
                await m_writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await FlushCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Flushes buffered key-log records to disk.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await FlushCoreAsync(cancellationToken).ConfigureAwait(false);
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

            await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (m_disposed)
                {
                    return;
                }

                await FlushCoreAsync(CancellationToken.None).ConfigureAwait(false);
                m_disposed = true;
                await m_writer.DisposeAsync().ConfigureAwait(false);
                await m_fileStream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
                m_gate.Dispose();
            }
        }

        private async ValueTask FlushCoreAsync(CancellationToken cancellationToken)
        {
            await m_writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await m_fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(PubSubKeyLogWriter));
            }
        }

        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly FileStream m_fileStream;
        private readonly StreamWriter m_writer;
        private bool m_disposed;
    }

    [JsonSourceGenerationOptions(
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(PubSubKeyLogRecord))]
    internal sealed partial class PubSubKeyLogJsonContext : JsonSerializerContext;

    internal sealed class PubSubKeyLogRecord
    {
        [JsonPropertyName("securityGroupId")]
        public string SecurityGroupId { get; init; } = string.Empty;

        [JsonPropertyName("tokenId")]
        public uint TokenId { get; init; }

        [JsonPropertyName("securityPolicyUri")]
        public string SecurityPolicyUri { get; init; } = string.Empty;

        [JsonPropertyName("encoding")]
        public string Encoding { get; init; } = Base64Encoding;

        [JsonPropertyName("signingKey")]
        public string? SigningKey { get; init; }

        [JsonPropertyName("encryptingKey")]
        public string? EncryptingKey { get; init; }

        [JsonPropertyName("keyNonce")]
        public string? KeyNonce { get; init; }

        public static PubSubKeyLogRecord From(PubSubKeyMaterial material)
        {
            return new PubSubKeyLogRecord
            {
                SecurityGroupId = material.SecurityGroupId,
                TokenId = material.TokenId,
                SecurityPolicyUri = material.SecurityPolicyUri,
                Encoding = Base64Encoding,
                SigningKey = ToBase64(material.SigningKey),
                EncryptingKey = ToBase64(material.EncryptingKey),
                KeyNonce = ToBase64(material.KeyNonce)
            };
        }

        public PubSubKeyMaterial ToMaterial()
        {
            return new PubSubKeyMaterial(
                SecurityGroupId,
                TokenId,
                SecurityPolicyUri,
                Decode(SigningKey),
                Decode(EncryptingKey),
                Decode(KeyNonce));
        }

        private static string? ToBase64(ReadOnlySpan<byte> value)
        {
            return value.Length == 0 ? null : Convert.ToBase64String(value);
        }

        private byte[]? Decode(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            if (string.Equals(Encoding, Base64Encoding, StringComparison.OrdinalIgnoreCase))
            {
                return Convert.FromBase64String(value);
            }
            if (string.Equals(Encoding, HexEncoding, StringComparison.OrdinalIgnoreCase))
            {
                return Convert.FromHexString(value);
            }
            throw new FormatException($"Unsupported PubSub key-log encoding '{Encoding}'.");
        }

        private const string Base64Encoding = "base64";
        private const string HexEncoding = "hex";
    }
}
