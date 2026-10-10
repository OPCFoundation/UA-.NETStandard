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
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
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
        /// The record protector applied to every encoded session entry
        /// (authenticated encryption). Configure an <see cref="AesCbcHmacRecordProtector"/>
        /// so the shared store can be treated as untrusted. It may be omitted only for an
        /// <see cref="InMemorySharedKeyValueStore"/>; to knowingly store the session
        /// secrets of an external store unprotected, pass
        /// <see cref="NullRecordProtector.Instance"/> explicitly.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="protector"/> is <c>null</c> and <paramref name="store"/> is not
        /// an in-memory store.
        /// </exception>
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
            m_protector = protector ?? DefaultProtectorFor(store);
            m_logger = logger ?? m_context.Telemetry.CreateLogger<SharedKeyValueSessionStore>();
        }

        /// <summary>
        /// The protector used when none is configured: the no-op protector for an
        /// in-memory store; any other store must be configured explicitly, because
        /// session records carry nonces, the client certificate and the identity
        /// continuity key (the same rule as the dependency injection registration).
        /// </summary>
        /// <exception cref="InvalidOperationException">The store is not in memory.</exception>
        internal static IRecordProtector DefaultProtectorFor(ISharedKeyValueStore store)
        {
            if (store is InMemorySharedKeyValueStore)
            {
                return NullRecordProtector.Instance;
            }

            throw new InvalidOperationException(
                $"Session mirroring to the external shared key/value store '{store.GetType().Name}' " +
                "requires an IRecordProtector: session records contain the server and client nonces, " +
                "the client certificate and the identity continuity key, and would otherwise be written " +
                "with neither confidentiality nor integrity protection. Pass a protector (for example an " +
                "AesCbcHmacRecordProtector with a managed key), or pass NullRecordProtector.Instance to " +
                "knowingly accept unprotected storage.");
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
            string key = KeyFor(entry.AuthenticationToken);
            return m_store.SetAsync(key, Protect(key, entry), ct);
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
            return found ? Unprotect(key, value) : null;
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveAsync(NodeId authenticationToken, CancellationToken ct = default)
        {
            return m_store.DeleteAsync(KeyFor(authenticationToken), ct);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> TryReplaceAsync(
            SharedSessionEntry current,
            SharedSessionEntry replacement,
            CancellationToken ct = default)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }
            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }
            if (replacement.AuthenticationToken != current.AuthenticationToken)
            {
                throw new ArgumentException(
                    "The replacement must have the authentication token of the current entry.",
                    nameof(replacement));
            }
            if (!m_storedValues.TryGetValue(current, out StoredValue? stored))
            {
                return false;
            }

            string key = KeyFor(current.AuthenticationToken);
            ByteString value = Protect(key, replacement);
            if (!await CompareAndSwapAsync(key, stored.Value, value, ct).ConfigureAwait(false))
            {
                return false;
            }
            Track(replacement, value);
            return true;
        }

        /// <inheritdoc/>
        public ValueTask<bool> TryRemoveAsync(SharedSessionEntry current, CancellationToken ct = default)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }
            if (!m_storedValues.TryGetValue(current, out StoredValue? stored))
            {
                return new ValueTask<bool>(false);
            }
            return CompareAndSwapAsync(KeyFor(current.AuthenticationToken), stored.Value, default, ct);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<SharedSessionEntry> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (KeyValuePair<string, ByteString> pair in m_store
                .ScanAsync(Prefix, ct)
                .ConfigureAwait(false))
            {
                SharedSessionEntry? entry = Unprotect(pair.Key, pair.Value);
                if (entry != null &&
                    string.Equals(KeyFor(entry.AuthenticationToken), pair.Key, StringComparison.Ordinal))
                {
                    yield return entry;
                }
            }
        }

        private ByteString Protect(string key, SharedSessionEntry entry)
        {
            return m_protector.Protect(RecordProtectionContext.Create("session", key), Encode(entry));
        }

        private SharedSessionEntry? Unprotect(string key, ByteString value)
        {
            if (!m_protector.TryUnprotect(
                    RecordProtectionContext.Create("session", key), value, out ByteString payload))
            {
                return null;
            }

            try
            {
                SharedSessionEntry entry = Decode(payload);
                Track(entry, value);
                return entry;
            }
            catch (Exception ex) when (ex is ServiceResultException or EndOfStreamException)
            {
                m_logger.FailedToDecodeSharedSessionEntry(key, ex);
                return null;
            }
        }

        /// <summary>
        /// Remembers the stored (protected) value an entry instance was read from or
        /// written as, so a conditional write can compare the exact stored bytes: a
        /// protector encrypts with a fresh IV, so re-encoding an entry never reproduces them.
        /// </summary>
        private void Track(SharedSessionEntry entry, ByteString value)
        {
            lock (m_storedValuesLock)
            {
                m_storedValues.Remove(entry);
                m_storedValues.Add(entry, new StoredValue(value));
            }
        }

        /// <summary>
        /// A compare-and-swap that also works over a store without one (the CRDT gossip
        /// store): there the comparison and the write are not atomic, which matches the
        /// eventual consistency of such a store.
        /// </summary>
        private async ValueTask<bool> CompareAndSwapAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct)
        {
            if (!m_compareAndSwapUnsupported)
            {
                try
                {
                    return await m_store.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
                }
                catch (NotSupportedException)
                {
                    m_compareAndSwapUnsupported = true;
                }
            }

            (bool found, ByteString current) = await m_store.TryGetAsync(key, ct).ConfigureAwait(false);
            if (!found || current != expected)
            {
                return false;
            }
            if (value.IsNull)
            {
                return await m_store.DeleteAsync(key, ct).ConfigureAwait(false);
            }
            await m_store.SetAsync(key, value, ct).ConfigureAwait(false);
            return true;
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
            if (entry.SecurityStateVersion >= 4)
            {
                encoder.WriteBoolean(null, entry.ClientCertificateValidated);
            }
            if (entry.SecurityStateVersion >= 5)
            {
                encoder.WriteInt64(null, entry.LastContactAt);
                encoder.WriteString(null, entry.OwnerId);
                encoder.WriteByteString(null, entry.ServerCertificate);
                encoder.WriteBoolean(null, entry.UserTokenRequiresEphemeralKey);
            }
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

            if (securityStateVersion is < SharedSessionEntry.MinimumRestorableSecurityStateVersion or
                > SharedSessionEntry.CurrentSecurityStateVersion)
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

            // Version 3 predates the mirrored certificate provenance: the certificate
            // is treated as not validated (fail closed).
            if (securityStateVersion >= 4)
            {
                decoded = decoded with
                {
                    ClientCertificateValidated = decoder.ReadBoolean(null)
                };
            }

            // Version 5 adds the liveness heartbeat, the owning replica, the original
            // server certificate and whether the user token needs an EphemeralKey.
            if (securityStateVersion >= 5)
            {
                decoded = decoded with
                {
                    LastContactAt = decoder.ReadInt64(null),
                    OwnerId = decoder.ReadString(null),
                    ServerCertificate = decoder.ReadByteString(null),
                    UserTokenRequiresEphemeralKey = decoder.ReadBoolean(null)
                };
            }

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

        /// <summary>
        /// The stored value an entry instance corresponds to.
        /// </summary>
        private sealed class StoredValue
        {
            public StoredValue(ByteString value)
            {
                Value = value;
            }

            public ByteString Value { get; }
        }

        private const string Prefix = "session/";
        private readonly ISharedKeyValueStore m_store;
        private readonly IServiceMessageContext m_context;
        private readonly IRecordProtector m_protector;
        private readonly ILogger<SharedKeyValueSessionStore> m_logger;
        private readonly ConditionalWeakTable<SharedSessionEntry, StoredValue> m_storedValues = new();
        private readonly Lock m_storedValuesLock = new();
        private volatile bool m_compareAndSwapUnsupported;
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
