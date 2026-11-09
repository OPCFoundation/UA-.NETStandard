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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Push-side SKS key provider populated by the Part 14 §9.1.3.3 SetSecurityKeys Method.
    /// </summary>
    public sealed class PushSecurityKeyProvider : IPubSubSecurityKeyProvider, IAsyncDisposable
    {
        private readonly Lock m_lock = new();
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly Dictionary<uint, PubSubSecurityKey> m_keys = [];
        private uint m_currentTokenId;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="PushSecurityKeyProvider"/>.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Time source.</param>
        public PushSecurityKeyProvider(
            string securityGroupId,
            ITelemetryContext? telemetry = null,
            TimeProvider? timeProvider = null)
        {
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException("SecurityGroupId must be non-empty.", nameof(securityGroupId));
            }

            SecurityGroupId = securityGroupId;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry is null
                ? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
                : telemetry.CreateLogger<PushSecurityKeyProvider>();
        }

        /// <inheritdoc/>
        public string SecurityGroupId { get; }

        /// <inheritdoc/>
        public event EventHandler<PubSubKeyRotatedEventArgs>? KeyRotated;

        /// <summary>
        /// Receives keys pushed by the SKS using SetSecurityKeys.
        /// </summary>
        /// <exception cref="OpcUaSksException"></exception>
        public ValueTask SetSecurityKeysAsync(
            string securityPolicyUri,
            uint currentTokenId,
            ByteString currentKey,
            ArrayOf<ByteString> futureKeys,
            TimeSpan timeToNextKey,
            TimeSpan keyLifetime,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "SecurityPolicyUri must be non-empty.");
            }
            if (currentTokenId == 0)
            {
                throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "CurrentTokenId must be non-zero.");
            }
            if (currentKey.IsNull)
            {
                throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "CurrentKey must not be null.");
            }
            if (keyLifetime <= TimeSpan.Zero)
            {
                throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "KeyLifetime must be positive.");
            }
            if (timeToNextKey < TimeSpan.Zero || timeToNextKey > keyLifetime)
            {
                throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "TimeToNextKey must be within KeyLifetime.");
            }
            if (futureKeys.Count > 4095)
            {
                throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "Too many future keys.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (timeToNextKey == TimeSpan.Zero)
            {
                timeToNextKey = keyLifetime;
            }
            IPubSubSecurityPolicy? policy = PubSubSecurityPolicyRegistry.Default.GetByUri(securityPolicyUri);
            if (policy is null || policy.SigningKeyLength == 0)
            {
                throw new OpcUaSksException(StatusCodes.BadSecurityPolicyRejected, "Unsupported pushed key policy.");
            }
            int packedLength = policy.SigningKeyLength + policy.EncryptingKeyLength +
                AesCtrNonceLayout.GetKeyNonceLength(policy);
            if (currentKey.Length != packedLength)
            {
                throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "Invalid packed current key length.");
            }
            for (int i = 0; i < futureKeys.Count; i++)
            {
                if (futureKeys[i].IsNull || futureKeys[i].Length != packedLength)
                {
                    throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "Invalid packed future key.");
                }
            }
            // Validate the entire schedule before allocating keys or changing the live ring.
            try
            {
                _ = m_timeProvider.GetUtcNow().AddTicks(
                    checked(timeToNextKey.Ticks + (keyLifetime.Ticks * futureKeys.Count)));
            }
            catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
            {
                throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "Pushed key schedule is out of range.");
            }
            var packed = new List<byte[]>(futureKeys.Count + 1)
            {
                currentKey.Span.ToArray()
            };
            for (int i = 0; i < futureKeys.Count; i++)
            {
                ByteString futureKey = futureKeys[i];
                if (futureKey.IsNull)
                {
                    throw new OpcUaSksException(StatusCodes.BadInvalidArgument, "Future keys must not be null.");
                }
                packed.Add(futureKey.Span.ToArray());
            }

            SksKeyResponse response = new(
                securityPolicyUri,
                currentTokenId,
                packed,
                timeToNextKey,
                keyLifetime);
            ArrayOf<PubSubSecurityKey> keys;
            try
            {
                keys = response.Unpack(m_timeProvider);
            }
            finally
            {
                foreach (byte[] material in packed)
                {
                    CryptoUtils.ZeroMemory(material);
                }
            }
            if (keys.Count == 0)
            {
                throw new OpcUaSksException(
                    StatusCodes.BadSecurityPolicyRejected,
                    $"SecurityPolicyUri '{securityPolicyUri}' is not supported for pushed keys.");
            }

            uint previousTokenId;
            try
            {
                lock (m_lock)
                {
                    ThrowIfDisposed();
                    cancellationToken.ThrowIfCancellationRequested();
                    previousTokenId = m_currentTokenId;
                    var scheduled = new List<PubSubSecurityKey>(keys.Count);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        PubSubSecurityKey key = keys[i];
                        DateTimeUtc startsAt = key.IssuedAt +
                            (i == 0 ? timeToNextKey - keyLifetime :
                                timeToNextKey + TimeSpan.FromTicks(checked(keyLifetime.Ticks * (i - 1))));
                        uint tokenId = unchecked(currentTokenId + (uint)i);
                        if (tokenId < currentTokenId)
                        {
                            tokenId = unchecked(tokenId + 1);
                        }
                        scheduled.Add(new PubSubSecurityKey(
                            tokenId, key.SigningKey, key.EncryptingKey, key.KeyNonce, startsAt, keyLifetime));
                    }
                    PubSubSecurityKey? previous = null;
                    if (currentTokenId != m_currentTokenId &&
                        m_keys.TryGetValue(m_currentTokenId, out PubSubSecurityKey? candidate) &&
                        IsValid(candidate) && !scheduled.Exists(key => key.TokenId == candidate.TokenId))
                    {
                        previous = candidate;
                        m_keys.Remove(candidate.TokenId);
                    }
                    // Retain at most one previous token, only until its original expiry.
                    DisposeKeysLocked();
                    if (previous is not null)
                    {
                        m_keys[previous.TokenId] = previous;
                    }
                    foreach (PubSubSecurityKey key in scheduled)
                    {
                        m_keys[key.TokenId] = key;
                    }
                    m_currentTokenId = currentTokenId;
                }
            }
            catch
            {
                foreach (PubSubSecurityKey key in keys)
                {
                    key.Dispose();
                }
                throw;
            }

            m_logger.ReceivedPushedSksKeys(keys.Count, SecurityGroupId);
            KeyRotated?.Invoke(
                this,
                new PubSubKeyRotatedEventArgs(
                    currentTokenId,
                    previousTokenId == 0 ? null : previousTokenId,
                    DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime)));
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<PubSubSecurityKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                ThrowIfDisposed();
                PruneExpiredLocked();
                PubSubSecurityKey? key = null;
                foreach (PubSubSecurityKey candidate in m_keys.Values)
                {
                    if (IsValid(candidate) && (key is null || candidate.IssuedAt > key.IssuedAt ||
                        (candidate.IssuedAt == key.IssuedAt && candidate.TokenId == m_currentTokenId)))
                    {
                        key = candidate;
                    }
                }
                if (key is not null)
                {
                    return new ValueTask<PubSubSecurityKey>(key);
                }
            }

            throw new InvalidOperationException(
                $"No pushed current key available for SecurityGroupId '{SecurityGroupId}'.");
        }

        /// <inheritdoc/>
        public ValueTask<PubSubSecurityKey?> TryGetKeyAsync(
            uint tokenId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                ThrowIfDisposed();
                PruneExpiredLocked();
                return new ValueTask<PubSubSecurityKey?>(
                    m_keys.TryGetValue(tokenId, out PubSubSecurityKey? key) && IsValid(key) ? key : null);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return default;
                }
                DisposeKeysLocked();
                m_disposed = true;
            }

            return default;
        }

        /// <summary>
        /// Acquires an owned copy so rotation cannot zeroize an in-flight cryptographic operation.
        /// </summary>
        internal PubSubSecurityKey? GetSnapshot(uint? tokenId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                ThrowIfDisposed();
                PruneExpiredLocked();
                PubSubSecurityKey? key = null;
                foreach (PubSubSecurityKey candidate in m_keys.Values)
                {
                    if (IsValid(candidate) &&
                        (tokenId.HasValue ? candidate.TokenId == tokenId.Value :
                            key is null || candidate.IssuedAt > key.IssuedAt ||
                                (candidate.IssuedAt == key.IssuedAt && candidate.TokenId == m_currentTokenId)))
                    {
                        key = candidate;
                    }
                }
                return key is null ? null : new PubSubSecurityKey(
                    key.TokenId,
                    ByteString.Create(key.SigningKey.Span),
                    ByteString.Create(key.EncryptingKey.Span),
                    ByteString.Create(key.KeyNonce.Span),
                    key.IssuedAt,
                    key.Lifetime);
            }
        }

        private void DisposeKeysLocked()
        {
            foreach (PubSubSecurityKey key in m_keys.Values)
            {
                key.Dispose();
            }
            m_keys.Clear();
        }

        private bool IsValid(PubSubSecurityKey key)
        {
            return DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime) >= key.IssuedAt &&
                !key.IsExpired(m_timeProvider);
        }

        private void PruneExpiredLocked()
        {
            var remove = new List<uint>();
            foreach (KeyValuePair<uint, PubSubSecurityKey> entry in m_keys)
            {
                if (entry.Value.IsExpired(m_timeProvider))
                {
                    remove.Add(entry.Key);
                }
            }
            for (int i = 0; i < remove.Count; i++)
            {
                if (m_keys.Remove(remove[i], out PubSubSecurityKey? key))
                {
                    key.Dispose();
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(PushSecurityKeyProvider));
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="PushSecurityKeyProvider"/>.
    /// </summary>
    internal static partial class PushSecurityKeyProviderLog
    {
        [LoggerMessage(EventId = PubSubEventIds.PushSecurityKeyProvider + 0, Level = LogLevel.Information,
            Message = "Received {Count} pushed SKS key(s) for SecurityGroupId {GroupId}.")]
        public static partial void ReceivedPushedSksKeys(this ILogger logger, int count, string groupId);
    }

}
