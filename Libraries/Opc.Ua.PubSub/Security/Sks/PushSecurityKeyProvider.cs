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

            cancellationToken.ThrowIfCancellationRequested();
            var packed = new List<byte[]>(futureKeys.Count + 1)
            {
                currentKey.Span.ToArray()
            };
            for (int i = 0; i < futureKeys.Count; i++)
            {
                ByteString futureKey = futureKeys[i];
                if (!futureKey.IsNull)
                {
                    packed.Add(futureKey.Span.ToArray());
                }
            }

            SksKeyResponse response = new(
                securityPolicyUri,
                currentTokenId,
                packed,
                timeToNextKey,
                keyLifetime);
            ArrayOf<PubSubSecurityKey> keys = response.Unpacked;
            if (keys.Count == 0)
            {
                throw new OpcUaSksException(
                    StatusCodes.BadSecurityPolicyRejected,
                    $"SecurityPolicyUri '{securityPolicyUri}' is not supported for pushed keys.");
            }

            uint previousTokenId;
            lock (m_lock)
            {
                ThrowIfDisposed();
                previousTokenId = m_currentTokenId;
                if (!m_keys.ContainsKey(currentTokenId))
                {
                    DisposeKeysLocked();
                }
                else
                {
                    RemoveDuplicateAndNewerLocked(currentTokenId);
                }

                for (int i = 0; i < keys.Count; i++)
                {
                    PubSubSecurityKey key = keys[i];
                    m_keys[key.TokenId] = key;
                }
                m_currentTokenId = currentTokenId;
            }

            m_logger.LogInformation(
                "Received {Count} pushed SKS key(s) for SecurityGroupId {GroupId}.",
                keys.Count,
                SecurityGroupId);
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
                if (m_currentTokenId != 0 && m_keys.TryGetValue(m_currentTokenId, out PubSubSecurityKey? key))
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
                return new ValueTask<PubSubSecurityKey?>(
                    m_keys.TryGetValue(tokenId, out PubSubSecurityKey? key) ? key : null);
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

        private void DisposeKeysLocked()
        {
            foreach (PubSubSecurityKey key in m_keys.Values)
            {
                key.Dispose();
            }
            m_keys.Clear();
        }

        private void RemoveDuplicateAndNewerLocked(uint currentTokenId)
        {
            var remove = new List<uint>();
            foreach (uint tokenId in m_keys.Keys)
            {
                if (tokenId >= currentTokenId)
                {
                    remove.Add(tokenId);
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
}
