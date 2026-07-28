/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Default <see cref="ISharedSessionStore"/> over an
    /// <see cref="ISharedKeyValueStore"/>. Each entry is binary-encoded and
    /// then passed through an <see cref="IRecordProtector"/> so the session
    /// secret material is encrypted and integrity-protected at rest; a tampered
    /// or forged entry fails verification and is treated as absent (fail-closed).
    /// The store key is the SHA-256 digest of the authentication token, not the
    /// token itself, so the secret-bearing keyspace stays one-way (the raw token
    /// is never exposed via a backend's key enumeration / monitoring / dumps).
    /// </summary>
    public sealed class SharedKeyValueSessionStore : ISharedSessionStore
    {
        /// <summary>
        /// Creates a session store over a shared key/value backend.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="context">The message context for encoding.</param>
        /// <param name="protector">
        /// Optional record protector applied to every encoded session entry
        /// (authenticated encryption); defaults to a no-op pass-through.
        /// Configure an <see cref="AesCbcHmacRecordProtector"/> in production
        /// so the shared store can be treated as untrusted.
        /// </param>
        public SharedKeyValueSessionStore(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector? protector = null)
            : this(store, context, protector, null)
        {
        }

        internal SharedKeyValueSessionStore(
            ISharedKeyValueStore store,
            IServiceMessageContext context,
            IRecordProtector? protector,
            ILogger<SharedKeyValueSessionStore>? logger)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_protector = protector ?? NullRecordProtector.Instance;
            m_logger = logger ?? m_context.Telemetry.CreateLogger<SharedKeyValueSessionStore>();
        }

        /// <inheritdoc/>
        public ValueTask PutAsync(SharedSessionEntry entry, CancellationToken ct = default)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            if (entry.AuthenticationToken.IsNull)
            {
                throw new ArgumentException(
                    "Session entry must have a non-null authentication token.",
                    nameof(entry));
            }
            return m_store.SetAsync(
                KeyFor(entry.AuthenticationToken), m_protector.Protect(Encode(entry)), ct);
        }

        /// <inheritdoc/>
        public async ValueTask<SharedSessionEntry?> TryGetAsync(
            NodeId authenticationToken,
            CancellationToken ct = default)
        {
            string key = KeyFor(authenticationToken);
            (bool found, ByteString value) = await m_store
                .TryGetAsync(key, ct)
                .ConfigureAwait(false);
            if (found && m_protector.TryUnprotect(value, out ByteString payload))
            {
                try
                {
                    return Decode(payload);
                }
                catch (Exception ex) when (ex is ServiceResultException or EndOfStreamException)
                {
                    m_logger.FailedToDecodeSharedSessionEntry(key, ex);
                    return null;
                }
            }
            return null;
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveAsync(NodeId authenticationToken, CancellationToken ct = default)
        {
            return m_store.DeleteAsync(KeyFor(authenticationToken), ct);
        }

        /// <summary>
        /// Computes the shared-store key for an authentication token: the
        /// configured prefix followed by the SHA-256 digest of the token, so the
        /// raw token never appears in the keyspace.
        /// </summary>
        /// <param name="authenticationToken">The session authentication token.</param>
        /// <returns>The opaque store key.</returns>
        internal static string KeyFor(NodeId authenticationToken)
        {
            byte[] data = Encoding.UTF8.GetBytes(authenticationToken.ToString());
#if NET8_0_OR_GREATER
            byte[] hash = SHA256.HashData(data);
#else
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(data);
            }
#endif
            return Prefix + Convert.ToBase64String(hash);
        }

        private ByteString Encode(SharedSessionEntry entry)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteNodeId(null, entry.SessionId);
            encoder.WriteNodeId(null, entry.AuthenticationToken);
            encoder.WriteString(null, entry.SessionName);
            encoder.WriteInt64(null, entry.CreatedAt);
            encoder.WriteInt64(null, entry.LastActivatedAt);
            encoder.WriteByteString(null, entry.ServerNonce);
            encoder.WriteByteString(null, entry.ClientNonce);
            encoder.WriteByteString(null, entry.ClientCertificateChain);
            encoder.WriteString(null, entry.SecurityPolicyUri);
            encoder.WriteInt32(null, entry.SecurityMode);
            encoder.WriteString(null, entry.EndpointUrl);
            encoder.WriteDouble(null, entry.SessionTimeout);
            encoder.WriteEncodeable(null, entry.ClientDescription ?? new ApplicationDescription());
            encoder.WriteByteString(null, entry.SecretMaterial);
            encoder.WriteUInt32(null, entry.SecurityStateVersion);
            encoder.WriteByteString(null, entry.OriginalClientChannelCertificate);
            encoder.WriteString(null, entry.ClientUserId);
            encoder.WriteInt32(null, (int)entry.ClientUserTokenType);
            encoder.WriteBoolean(null, entry.HasActivatedUserIdentity);
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : new ByteString(buffer);
        }

        private SharedSessionEntry Decode(ByteString payload)
        {
            using var decoder = new BinaryDecoder(payload.ToArray(), m_context);
            var entry = new SharedSessionEntry
            {
                SessionId = decoder.ReadNodeId(null),
                AuthenticationToken = decoder.ReadNodeId(null),
                SessionName = decoder.ReadString(null) ?? string.Empty,
                CreatedAt = decoder.ReadInt64(null),
                LastActivatedAt = decoder.ReadInt64(null),
                ServerNonce = decoder.ReadByteString(null),
                ClientNonce = decoder.ReadByteString(null),
                ClientCertificateChain = decoder.ReadByteString(null),
                SecurityPolicyUri = decoder.ReadString(null) ?? string.Empty,
                SecurityMode = decoder.ReadInt32(null),
                EndpointUrl = decoder.ReadString(null) ?? string.Empty,
                SessionTimeout = decoder.ReadDouble(null),
                ClientDescription = decoder.ReadEncodeable<ApplicationDescription>(null),
                SecretMaterial = decoder.ReadByteString(null)
            };

            if (decoder.Position == payload.Length)
            {
                return entry;
            }

            uint securityStateVersion = decoder.ReadUInt32(null);
            if (securityStateVersion == 1)
            {
                SharedSessionEntry versionOne = entry with
                {
                    SecurityStateVersion = securityStateVersion,
                    OriginalClientChannelCertificate = decoder.ReadByteString(null),
                    ClientUserId = decoder.ReadString(null)
                };
                EnsureFullyDecoded(decoder, payload);
                return versionOne;
            }

            if (securityStateVersion != SharedSessionEntry.CurrentSecurityStateVersion)
            {
                return entry with
                {
                    SecurityStateVersion = securityStateVersion
                };
            }

            SharedSessionEntry decoded = entry with
            {
                SecurityStateVersion = securityStateVersion,
                OriginalClientChannelCertificate = decoder.ReadByteString(null),
                ClientUserId = decoder.ReadString(null),
                ClientUserTokenType = (UserTokenType)decoder.ReadInt32(null),
                HasActivatedUserIdentity = decoder.ReadBoolean(null)
            };

            EnsureFullyDecoded(decoder, payload);
            return decoded;
        }

        private static void EnsureFullyDecoded(BinaryDecoder decoder, ByteString payload)
        {
            if (decoder.Position != payload.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Unexpected trailing data in a shared Session entry.");
            }
        }

        private const string Prefix = "session/";
        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly ILogger<SharedKeyValueSessionStore> m_logger;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="SharedKeyValueSessionStore"/>.
    /// </summary>
    internal static partial class SharedKeyValueSessionStoreLog
    {
        [LoggerMessage(EventId = RedundancyServerEventIds.SharedKeyValueSessionStore,
            Level = LogLevel.Warning,
            Message = "Failed to decode shared Session entry {Key}; treating it as absent.")]
        public static partial void FailedToDecodeSharedSessionEntry(
            this ILogger logger,
            string key,
            Exception exception);
    }
}
