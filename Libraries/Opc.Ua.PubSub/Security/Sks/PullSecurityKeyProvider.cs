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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// SKS-pull <see cref="IPubSubSecurityKeyProvider"/> that caches
    /// keys in a <see cref="PubSubSecurityKeyRing"/> and refreshes
    /// them in the background. Designed so that
    /// <see cref="GetCurrentKeyAsync"/> never performs I/O on the
    /// publish hot-path: the cached current key is served
    /// synchronously from the ring while a separate scheduler
    /// drives <c>GetSecurityKeys</c> calls just before the active
    /// token expires.
    /// </summary>
    /// <remarks>
    /// Implements the SKS pull profile defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.2">
    /// Part 14 §8.3.2 GetSecurityKeys</see>. When the SKS is
    /// unavailable the provider keeps serving the last-known key
    /// (so encryption / verification continues), while raising a
    /// <see cref="ISecurityKeyService.AvailabilityChanged"/> event
    /// that lets the security subsystem move the WriterGroup /
    /// ReaderGroup into <c>PreOperational</c>.
    /// </remarks>
    public sealed class PullSecurityKeyProvider : IPubSubSecurityKeyProvider, IAsyncDisposable
    {
        private readonly ISecurityKeyService m_sks;
        private readonly bool m_ownsSecurityKeyService;
        private readonly IPubSubSecurityPolicy m_policy;
        private readonly PullSecurityKeyProviderOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly PubSubSecurityKeyRing m_ring;
        private readonly CancellationTokenSource m_disposeCts = new();
        private readonly SemaphoreSlim m_refreshSemaphore = new(1, 1);
        private readonly Lock m_stateLock = new();
        private Task? m_backgroundTask;
        private int m_consecutiveFailures;
        private uint m_highestKnownTokenId;
        private bool m_started;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="PullSecurityKeyProvider"/>.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="sksClient">SKS pull client.</param>
        /// <param name="policy">Security policy bundle.</param>
        /// <param name="options">Provider options.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Time source.</param>
        /// <param name="ownsSecurityKeyService">
        /// When <see langword="true"/> the provider owns
        /// <paramref name="sksClient"/> and disposes it (if it is
        /// <see cref="IAsyncDisposable"/>) as part of
        /// <see cref="DisposeAsync"/>. Set to <see langword="false"/>
        /// (the default) when the client is owned by the caller or the
        /// dependency-injection container.
        /// </param>
        public PullSecurityKeyProvider(
            string securityGroupId,
            ISecurityKeyService sksClient,
            IPubSubSecurityPolicy policy,
            PullSecurityKeyProviderOptions options,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            bool ownsSecurityKeyService = false)
        {
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException(
                    "SecurityGroupId must be non-empty.",
                    nameof(securityGroupId));
            }
            if (sksClient is null)
            {
                throw new ArgumentNullException(nameof(sksClient));
            }
            if (policy is null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            SecurityGroupId = securityGroupId;
            m_sks = sksClient;
            m_ownsSecurityKeyService = ownsSecurityKeyService;
            m_policy = policy;
            m_options = options;
            m_timeProvider = timeProvider;
            m_logger = telemetry.CreateLogger<PullSecurityKeyProvider>();
            m_ring = new PubSubSecurityKeyRing(securityGroupId, timeProvider);
            m_ring.Rotated += OnRingRotated;
        }

        /// <inheritdoc/>
        public string SecurityGroupId { get; }

        /// <inheritdoc/>
        public event EventHandler<PubSubKeyRotatedEventArgs>? KeyRotated;

        /// <summary>
        /// Underlying ring exposed for diagnostics. Tests may inspect
        /// the populated keys; do not mutate the ring directly.
        /// </summary>
        internal PubSubSecurityKeyRing Ring => m_ring;

        /// <summary>
        /// Performs the initial pull from the SKS and starts the
        /// background refresh task.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            lock (m_stateLock)
            {
                if (m_started)
                {
                    return;
                }
                m_started = true;
            }

            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            m_backgroundTask = Task.Run(
                () => RunBackgroundLoopAsync(m_disposeCts.Token),
                CancellationToken.None);
        }

        /// <inheritdoc/>
        public ValueTask<PubSubSecurityKey> GetCurrentKeyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            PubSubSecurityKey? current = m_ring.Current;
            if (current is null)
            {
                throw new InvalidOperationException(
                    $"No current key available for SecurityGroupId '{SecurityGroupId}'.");
            }
            return new ValueTask<PubSubSecurityKey>(current);
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubSecurityKey?> TryGetKeyAsync(
            uint tokenId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            PubSubSecurityKey? key = m_ring.TryGetByTokenId(tokenId);
            if (key is not null)
            {
                return key;
            }
            uint highest;
            lock (m_stateLock)
            {
                highest = m_highestKnownTokenId;
            }
            if (tokenId <= highest)
            {
                return null;
            }
            try
            {
                await TryRefreshOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OpcUaSksException ex)
            {
                m_logger.LogDebug(
                    ex,
                    "Opportunistic SKS refresh for TokenId {TokenId} failed.",
                    tokenId);
                return null;
            }
            return m_ring.TryGetByTokenId(tokenId);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_stateLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }
            try
            {
                m_disposeCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            Task? bg = m_backgroundTask;
            if (bg is not null)
            {
                try
                {
                    await bg.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(
                        ex,
                        "Background SKS refresh loop terminated with exception.");
                }
            }
            m_ring.Rotated -= OnRingRotated;
            m_ring.Dispose();
            m_disposeCts.Dispose();
            m_refreshSemaphore.Dispose();
            if (m_ownsSecurityKeyService && m_sks is IAsyncDisposable disposableSks)
            {
                await disposableSks.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task RunBackgroundLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TimeSpan delay;
                try
                {
                    delay = ComputeNextDelay();
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        ex,
                        "Failed to compute next SKS refresh delay; falling back to ReconnectDelay.");
                    delay = m_options.ReconnectDelay;
                }
                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await m_timeProvider.Delay(delay, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                int failures;
                lock (m_stateLock)
                {
                    failures = m_consecutiveFailures;
                }
                if (failures >= m_options.MaxConsecutiveFailures && m_options.MaxConsecutiveFailures > 0)
                {
                    m_logger.LogWarning(
                        "Background SKS refresh paused after {Failures} consecutive failures.",
                        failures);
                    return;
                }

                try
                {
                    await RefreshAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        ex,
                        "Background SKS refresh failed for SecurityGroupId {GroupId}.",
                        SecurityGroupId);
                }
            }
        }

        private TimeSpan ComputeNextDelay()
        {
            int failures;
            lock (m_stateLock)
            {
                failures = m_consecutiveFailures;
            }
            if (failures > 0)
            {
                return m_options.ReconnectDelay;
            }
            PubSubSecurityKey? current = m_ring.Current;
            if (current is null)
            {
                return m_options.ReconnectDelay;
            }
            var now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
            DateTimeUtc refreshAt = current.IssuedAt + (current.Lifetime - m_options.RefreshLeadTime);
            TimeSpan remaining = refreshAt - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        private async Task RefreshAsync(CancellationToken ct)
        {
            await m_refreshSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await TryRefreshOnceAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                m_refreshSemaphore.Release();
            }
        }

        private async Task TryRefreshOnceAsync(CancellationToken ct)
        {
            uint requestedKeyCount = (uint)Math.Max(1, m_options.RequestedFutureKeyCount + 1);
            uint startingTokenId;
            lock (m_stateLock)
            {
                startingTokenId = m_highestKnownTokenId == 0 ? 0u : unchecked(m_highestKnownTokenId + 1u);
            }
            var request = new SksKeyRequest(SecurityGroupId, startingTokenId, requestedKeyCount);
            SksKeyResponse response;
            try
            {
                response = await m_sks
                    .GetSecurityKeysAsync(request, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (OpcUaSksException)
            {
                lock (m_stateLock)
                {
                    m_consecutiveFailures++;
                }
                throw;
            }
            catch (Exception ex)
            {
                lock (m_stateLock)
                {
                    m_consecutiveFailures++;
                }
                throw new OpcUaSksException(
                    StatusCodes.BadCommunicationError,
                    $"SKS refresh for SecurityGroupId '{SecurityGroupId}' failed.",
                    ex);
            }

            ApplyResponse(response);
            lock (m_stateLock)
            {
                m_consecutiveFailures = 0;
            }
        }

        private void ApplyResponse(SksKeyResponse response)
        {
            ArrayOf<PubSubSecurityKey> keys = response.Unpacked;
            if (keys.Count == 0)
            {
                m_logger.LogDebug(
                    "SKS response for SecurityGroupId {GroupId} contained no usable keys.",
                    SecurityGroupId);
                return;
            }

            uint? previousHighest;
            lock (m_stateLock)
            {
                previousHighest = m_highestKnownTokenId == 0 ? null : m_highestKnownTokenId;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                PubSubSecurityKey key = keys[i];
                if (previousHighest is uint h && key.TokenId <= h)
                {
                    continue;
                }
                if (m_ring.Current is null)
                {
                    m_ring.SetCurrent(key);
                }
                else
                {
                    m_ring.AddFuture(key);
                }
                lock (m_stateLock)
                {
                    if (key.TokenId > m_highestKnownTokenId)
                    {
                        m_highestKnownTokenId = key.TokenId;
                    }
                }
            }

            PubSubSecurityKey? current = m_ring.Current;
            if (current is not null && current.IsExpired(m_timeProvider))
            {
                m_ring.RotateToNextFuture();
            }
        }

        private void OnRingRotated(object? sender, PubSubKeyRotatedEventArgs e)
        {
            KeyRotated?.Invoke(this, e);
        }

        private void ThrowIfDisposed()
        {
            lock (m_stateLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(PullSecurityKeyProvider));
                }
            }
        }
    }
}
