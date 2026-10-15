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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PubSub.Security;
using UaLens.Workspace;

namespace UaLens.Plugins.PubSub
{
    /// <summary>
    /// Revalidates every key lookup for the document's AES-256-CTR policy and owns only its call/event lifetime.
    /// </summary>
    internal sealed class PubSubValidatedKeyProvider : IPubSubSecurityKeyProvider, IAsyncDisposable
    {
        /// <summary>
        /// Borrows a configured provider while pinning its security group and the document clock.
        /// </summary>
        public PubSubValidatedKeyProvider(
            IPubSubSecurityKeyProvider provider, string securityGroupId,
            TimeProvider clock, PubSubObservationStore observations)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
            m_observations = observations ?? throw new ArgumentNullException(nameof(observations));
            ArgumentException.ThrowIfNullOrEmpty(securityGroupId);
            SecurityGroupId = securityGroupId;
            m_provider.KeyRotated += OnKeyRotated;
        }

        /// <inheritdoc/>
        public string SecurityGroupId { get; }

        /// <inheritdoc/>
        public event EventHandler<PubSubKeyRotatedEventArgs>? KeyRotated;

        /// <inheritdoc/>
        public ValueTask<PubSubSecurityKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
        {
            return m_operations.ExecuteAsync(async token =>
            {
                RequireGroup();
                PubSubSecurityKey key = await m_provider.GetCurrentKeyAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                RequireGroup();
                RequireMaterial(key);
                if (key.IssuedAt > DateTimeUtc.From(m_clock.GetUtcNow()) || key.IsExpired(m_clock))
                {
                    throw Reject("The configured current key is not active; publication requires a valid next key.");
                }
                return key;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<PubSubSecurityKey?> TryGetKeyAsync(
            uint tokenId, CancellationToken cancellationToken = default)
        {
            return m_operations.ExecuteAsync<PubSubSecurityKey?>(async token =>
            {
                RequireGroup();
                PubSubSecurityKey? key = await m_provider.TryGetKeyAsync(tokenId, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                RequireGroup();
                if (key is not null)
                {
                    RequireMaterial(key);
                    if (key.TokenId != tokenId)
                    {
                        throw Reject(
                            "The key provider returned a different token than the received message requested.");
                    }
                }
                // Part 14 8.3.2 permits retained previous/next keys on receive for delivery and clock overlap.
                return key;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_detached, 1) == 0)
            {
                m_provider.KeyRotated -= OnKeyRotated;
                KeyRotated = null;
            }
            return m_operations.DisposeAsync();
        }

        private void OnKeyRotated(object? sender, PubSubKeyRotatedEventArgs args)
        {
            if (Volatile.Read(ref m_detached) == 0)
            {
                KeyRotated?.Invoke(this, args);
            }
        }

        private void RequireGroup()
        {
            if (m_provider.SecurityGroupId != SecurityGroupId)
            {
                throw Reject("The configured key provider changed its security group; restart with reviewed settings.");
            }
        }

        private void RequireMaterial(PubSubSecurityKey key)
        {
            if (key is null ||
                key.TokenId == 0 ||
                key.SigningKey.Length != 32 ||
                key.EncryptingKey.Length != 32 ||
                key.KeyNonce.Length != 4)
            {
                throw Reject("The key provider returned invalid material for the selected AES-256-CTR policy.");
            }
        }

        private ServiceResultException Reject(string message)
        {
            m_observations.RecordEvidence("Security key", StatusCodes.BadSecurityChecksFailed, message);
            return new ServiceResultException(StatusCodes.BadSecurityChecksFailed, message);
        }

        private readonly IPubSubSecurityKeyProvider m_provider;
        private readonly TimeProvider m_clock;
        private readonly PubSubObservationStore m_observations;
        private readonly OwnedOperationScope m_operations = new("The selected key binding has stopped.");
        private int m_detached;
    }
}
