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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings.Pcap.Audit
{
    /// <summary>
    /// Verification result for a tamper-evident Pcap audit ledger.
    /// </summary>
    public sealed record AuditChainVerification
    {
        /// <summary>
        /// Constructs an audit chain verification result.
        /// </summary>
        public AuditChainVerification(int linesVerified, int firstCorruptLine, string? corruptionReason)
        {
            LinesVerified = linesVerified;
            FirstCorruptLine = firstCorruptLine;
            CorruptionReason = corruptionReason;
        }

        /// <summary>
        /// Gets the number of lines verified before corruption was detected.
        /// </summary>
        public int LinesVerified { get; init; }

        /// <summary>
        /// Gets the first corrupt line number, or -1 when the chain is valid.
        /// </summary>
        public int FirstCorruptLine { get; init; }

        /// <summary>
        /// Gets the corruption reason, or <c>null</c> when the chain is valid.
        /// </summary>
        public string? CorruptionReason { get; init; }
    }

    /// <summary>
    /// Writes Pcap audit events to a JSON-lines ledger with per-line HMAC chaining.
    /// </summary>
    public sealed class HashChainedAuditFileSink : IPcapAuditSink, IAsyncDisposable
    {
        private const int HmacLength = 32;

        private readonly FileStream m_stream;
        private readonly byte[] m_hmacKey;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private byte[] m_previousHmac;
        private bool m_disposed;

        /// <summary>
        /// Constructs a tamper-evident audit sink for the supplied JSON-lines ledger.
        /// </summary>
        public HashChainedAuditFileSink(
            string filePath,
            byte[] hmacKey,
            ILogger<HashChainedAuditFileSink>? logger)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(hmacKey);

            if (hmacKey.Length != HmacLength)
            {
                throw new ArgumentException("The audit HMAC key must be 32 bytes.", nameof(hmacKey));
            }

            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            m_hmacKey = (byte[])hmacKey.Clone();
            m_previousHmac = LoadPreviousHmac(filePath, logger);
            m_stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            }
        }

        /// <inheritdoc/>
        public async ValueTask OnEventAsync(PcapAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(auditEvent);
            ObjectDisposedException.ThrowIf(m_disposed, this);

            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);

                byte[] eventBytes = SerializeEvent(auditEvent);
                byte[] previousHmac = (byte[])m_previousHmac.Clone();
                byte[] newHmac = ComputeHmac(m_hmacKey, previousHmac, eventBytes);
                byte[] lineBytes = BuildLedgerLine(eventBytes, previousHmac, newHmac);

                await m_stream.WriteAsync(lineBytes, cancellationToken).ConfigureAwait(false);
                await m_stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                m_previousHmac = newHmac;
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

                await m_stream.DisposeAsync().ConfigureAwait(false);
                m_disposed = true;
            }
            finally
            {
                m_gate.Release();
                m_gate.Dispose();
            }
        }

        /// <summary>
        /// Verifies every line in a tamper-evident audit ledger.
        /// </summary>
        public static AuditChainVerification VerifyChain(string filePath, byte[] hmacKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(hmacKey);

            if (hmacKey.Length != HmacLength)
            {
                throw new ArgumentException("The audit HMAC key must be 32 bytes.", nameof(hmacKey));
            }

            int linesVerified = 0;
            byte[]? expectedPreviousHmac = null;

            // Open with FileShare.ReadWrite so verification can run
            // while another process — or another component in the same
            // process such as the live HashChainedAuditFileSink — still
            // holds the file open for append. The writer uses
            // FileShare.Read so the only side that needs the extra
            // permissiveness is THIS reader.
            using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            for (int lineNumber = 1; ; lineNumber++)
            {
                string? line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    return Corrupt(linesVerified, lineNumber, "Audit ledger line is empty.");
                }

                if (!TryReadLedgerLine(line, out byte[] eventBytes, out byte[] previousHmac, out byte[] hmac,
                    out string? error))
                {
                    return Corrupt(linesVerified, lineNumber, error);
                }

                if (expectedPreviousHmac is not null && !CryptographicOperations.FixedTimeEquals(
                    previousHmac,
                    expectedPreviousHmac))
                {
                    return Corrupt(linesVerified, lineNumber, "Previous HMAC does not match the prior line HMAC.");
                }

                byte[] computedHmac = ComputeHmac(hmacKey, previousHmac, eventBytes);
                if (!CryptographicOperations.FixedTimeEquals(hmac, computedHmac))
                {
                    return Corrupt(linesVerified, lineNumber, "Line HMAC does not match the event payload.");
                }

                linesVerified++;
                expectedPreviousHmac = hmac;
            }

            return new AuditChainVerification(linesVerified, -1, null);
        }

        private static AuditChainVerification Corrupt(int linesVerified, int lineNumber, string? reason)
        {
            return new AuditChainVerification(linesVerified, lineNumber, reason ?? "Audit ledger line is corrupt.");
        }

        private static byte[] LoadPreviousHmac(string filePath, ILogger<HashChainedAuditFileSink>? logger)
        {
            if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
            {
                return RandomNumberGenerator.GetBytes(HmacLength);
            }

            string? lastLine = null;
            // Open with FileShare.ReadWrite for symmetry with VerifyChain.
            using (var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                while (reader.ReadLine() is { } line)
                {
                    if (line.Length > 0)
                    {
                        lastLine = line;
                    }
                }
            }

            if (lastLine is null)
            {
                return RandomNumberGenerator.GetBytes(HmacLength);
            }

            if (TryReadLedgerLine(lastLine, out _, out _, out byte[] hmac, out string? error))
            {
                logger?.LogDebug(
                    "Continuing tamper-evident Pcap audit chain from existing ledger {FilePath}.",
                    filePath);
                return hmac;
            }

            throw new InvalidDataException("Existing audit ledger has an unreadable final HMAC: " + error);
        }

        private static byte[] SerializeEvent(PcapAuditEvent auditEvent)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("kind", auditEvent.Kind.ToString());
                writer.WriteString("timestamp", auditEvent.Timestamp);
                WriteNullableString(writer, "sessionId", auditEvent.SessionId);
                WriteNullableString(writer, "resourcePath", auditEvent.ResourcePath);
                WriteNullableString(writer, "remoteEndpoint", auditEvent.RemoteEndpoint);
                WriteProperties(writer, auditEvent.Properties);
                writer.WriteEndObject();
            }

            return buffer.WrittenSpan.ToArray();
        }

        private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
        {
            if (value is null)
            {
                writer.WriteNull(propertyName);
                return;
            }

            writer.WriteString(propertyName, value);
        }

        private static void WriteProperties(Utf8JsonWriter writer, IReadOnlyDictionary<string, string>? properties)
        {
            writer.WritePropertyName("properties");
            if (properties is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            foreach (KeyValuePair<string, string> property in properties)
            {
                writer.WriteString(property.Key, property.Value);
            }

            writer.WriteEndObject();
        }

        private static byte[] BuildLedgerLine(byte[] eventBytes, byte[] previousHmac, byte[] hmac)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("event");
                // Write the event payload as raw UTF-8 JSON so the bytes
                // embedded in the ledger line are byte-identical to the
                // bytes that ComputeHmac saw on the write path. A
                // round-trip through JsonDocument.Parse + WriteTo would
                // re-emit via the writer's own encoder and could differ
                // by character escaping or member ordering — that breaks
                // HMAC chain verification because VerifyChain recovers
                // the event bytes via JsonElement.GetRawText() which
                // returns the exact substring stored in the line.
                writer.WriteRawValue(eventBytes, skipInputValidation: true);

                writer.WriteString("hmac", Convert.ToBase64String(hmac));
                writer.WriteString("prev", Convert.ToBase64String(previousHmac));
                writer.WriteEndObject();
            }

            byte[] lineBytes = new byte[buffer.WrittenCount + 1];
            buffer.WrittenSpan.CopyTo(lineBytes);
            lineBytes[^1] = (byte)'\n';
            return lineBytes;
        }

        private static byte[] ComputeHmac(byte[] hmacKey, byte[] previousHmac, byte[] eventBytes)
        {
            using var hmac = new HMACSHA256(hmacKey);
            hmac.TransformBlock(previousHmac, 0, previousHmac.Length, outputBuffer: null, outputOffset: 0);
            hmac.TransformFinalBlock(eventBytes, 0, eventBytes.Length);
            return hmac.Hash ?? throw new CryptographicException("HMAC-SHA256 did not produce a hash.");
        }

        private static bool TryReadLedgerLine(
            string line,
            out byte[] eventBytes,
            out byte[] previousHmac,
            out byte[] hmac,
            out string? error)
        {
            eventBytes = [];
            previousHmac = [];
            hmac = [];
            error = null;

            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    error = "Audit ledger line is not a JSON object.";
                    return false;
                }

                if (!root.TryGetProperty("event", out JsonElement eventElement))
                {
                    error = "Audit ledger line is missing the event property.";
                    return false;
                }

                if (!TryReadBase64Hmac(root, "prev", out previousHmac, out error) ||
                    !TryReadBase64Hmac(root, "hmac", out hmac, out error))
                {
                    return false;
                }

                eventBytes = Encoding.UTF8.GetBytes(eventElement.GetRawText());
                return true;
            }
            catch (JsonException ex)
            {
                error = "Audit ledger line is not valid JSON: " + ex.Message;
                return false;
            }
        }

        private static bool TryReadBase64Hmac(
            JsonElement root,
            string propertyName,
            out byte[] value,
            out string? error)
        {
            value = [];
            if (!root.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                error = "Audit ledger line is missing the " + propertyName + " HMAC property.";
                return false;
            }

            string? encodedValue = property.GetString();
            if (encodedValue is null)
            {
                error = "Audit ledger line has a null " + propertyName + " HMAC.";
                return false;
            }

            try
            {
                value = Convert.FromBase64String(encodedValue);
            }
            catch (FormatException ex)
            {
                error = "Audit ledger line has an invalid " + propertyName + " HMAC: " + ex.Message;
                return false;
            }

            if (value.Length != HmacLength)
            {
                error = "Audit ledger line has a " + propertyName + " HMAC with the wrong length.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
