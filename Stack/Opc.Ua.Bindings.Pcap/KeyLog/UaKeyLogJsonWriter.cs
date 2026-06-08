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
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Pcap.Capture;

namespace Opc.Ua.Bindings.Pcap.KeyLog
{
    /// <summary>
    /// Writes OPC UA channel key material as JSON-lines records.
    /// </summary>
    public sealed class UaKeyLogJsonWriter : IKeyLogWriter
    {
        /// <summary>
        /// Constructs a JSON-lines key-log writer for the supplied file.
        /// </summary>
        public UaKeyLogJsonWriter(string filePath)
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
                string json = JsonSerializer.Serialize(
                    KeyLogRecord.From(material),
                    UaKeyLogJsonContext.Default.KeyLogRecord);
                await m_writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
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

        private readonly FileStream m_stream;
        private readonly StreamWriter m_writer;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private bool m_disposed;
    }

    [JsonSourceGenerationOptions(
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(KeyLogRecord))]
    internal sealed partial class UaKeyLogJsonContext : JsonSerializerContext;

    internal sealed class KeyLogRecord
    {
        [JsonPropertyName("channelId")]
        public uint ChannelId { get; init; }

        [JsonPropertyName("tokenId")]
        public uint TokenId { get; init; }

        [JsonPropertyName("securityPolicyUri")]
        public string SecurityPolicyUri { get; init; } = string.Empty;

        [JsonPropertyName("securityMode")]
        public string SecurityMode { get; init; } = nameof(MessageSecurityMode.None);

        [JsonPropertyName("createdAt")]
        public DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("lifetimeMs")]
        public int LifetimeMs { get; init; }

        [JsonPropertyName("clientNonce")]
        public string? ClientNonce { get; init; }

        [JsonPropertyName("serverNonce")]
        public string? ServerNonce { get; init; }

        [JsonPropertyName("clientSigningKey")]
        public string? ClientSigningKey { get; init; }

        [JsonPropertyName("clientEncryptingKey")]
        public string? ClientEncryptingKey { get; init; }

        [JsonPropertyName("clientInitializationVector")]
        public string? ClientInitializationVector { get; init; }

        [JsonPropertyName("serverSigningKey")]
        public string? ServerSigningKey { get; init; }

        [JsonPropertyName("serverEncryptingKey")]
        public string? ServerEncryptingKey { get; init; }

        [JsonPropertyName("serverInitializationVector")]
        public string? ServerInitializationVector { get; init; }

        public static KeyLogRecord From(ChannelKeyMaterial material)
        {
            return new KeyLogRecord
            {
                ChannelId = material.ChannelId,
                TokenId = material.TokenId,
                SecurityPolicyUri = material.SecurityPolicyUri,
                SecurityMode = material.SecurityMode.ToString(),
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(material.CreatedAt, DateTimeKind.Utc)),
                LifetimeMs = material.Lifetime,
                ClientNonce = ToBase64(material.ClientNonce),
                ServerNonce = ToBase64(material.ServerNonce),
                ClientSigningKey = ToBase64(material.ClientSigningKey),
                ClientEncryptingKey = ToBase64(material.ClientEncryptingKey),
                ClientInitializationVector = ToBase64(material.ClientInitializationVector),
                ServerSigningKey = ToBase64(material.ServerSigningKey),
                ServerEncryptingKey = ToBase64(material.ServerEncryptingKey),
                ServerInitializationVector = ToBase64(material.ServerInitializationVector)
            };
        }

        public ChannelKeyMaterial ToMaterial()
        {
            if (!Enum.TryParse(SecurityMode, ignoreCase: false, out MessageSecurityMode mode))
            {
                throw new PcapDiagnosticsException($"Invalid OPC UA key-log security mode '{SecurityMode}'.");
            }

            return new ChannelKeyMaterial(
                ChannelId,
                TokenId,
                SecurityPolicyUri,
                mode,
                CreatedAt.UtcDateTime,
                LifetimeMs,
                FromBase64(ClientNonce),
                FromBase64(ServerNonce),
                FromBase64(ClientSigningKey),
                FromBase64(ClientEncryptingKey),
                FromBase64(ClientInitializationVector),
                FromBase64(ServerSigningKey),
                FromBase64(ServerEncryptingKey),
                FromBase64(ServerInitializationVector));
        }

        private static string? ToBase64(byte[]? value)
        {
            return value is { Length: > 0 } ? Convert.ToBase64String(value) : null;
        }

        private static byte[]? FromBase64(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : Convert.FromBase64String(value);
        }
    }
}
