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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Owns the lifetime of a single in-process PubSub capture: it creates
    /// the <see cref="InProcessPubSubCaptureSource"/>, installs it on the
    /// shared <see cref="IPubSubCaptureRegistry"/>, and exposes the captured
    /// frames / keys for dissection. Only one capture may be active at a time.
    /// </summary>
    public sealed class PubSubCaptureSessionManager : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubCaptureSessionManager"/>.
        /// </summary>
        /// <param name="registry">
        /// The capture registry shared with the PubSub transports.
        /// </param>
        /// <param name="loggerFactory">Optional logger factory.</param>
        public PubSubCaptureSessionManager(
            IPubSubCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(registry);
            m_registry = registry;
            m_loggerFactory = loggerFactory;
        }

        /// <summary>
        /// The active capture source, or <see langword="null"/> when no
        /// capture is running.
        /// </summary>
        public IPubSubCaptureSource? ActiveSource => Volatile.Read(ref m_active)?.Source;

        /// <summary>
        /// Starts a new in-process capture session. Throws if one is already
        /// running.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The started capture source.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public ValueTask<IPubSubCaptureSource> StartAsync(
            CancellationToken cancellationToken = default)
        {
            return StartForCallerAsync("host", cancellationToken);
        }

        /// <summary>
        /// Starts a capture owned by the supplied host-local caller key.
        /// </summary>
        /// <param name="ownerKey">Stable owner key established by the host.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The started capture source.</returns>
        public async ValueTask<IPubSubCaptureSource> StartForCallerAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateOwnerKey(ownerKey);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_active is not null)
                {
                    throw new InvalidOperationException(
                        "A PubSub capture session is already active.");
                }
                var source = new InProcessPubSubCaptureSource(
                    m_registry,
                    m_loggerFactory?.CreateLogger<InProcessPubSubCaptureSource>());
                try
                {
                    await source.StartAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
                Volatile.Write(ref m_active, new ActiveCapture(ownerKey, source));
                return source;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Stops the active capture session if one is running. The returned
        /// source remains readable for replay until disposed.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The stopped source, or <see langword="null"/> if none was active.
        /// </returns>
        public ValueTask<IPubSubCaptureSource?> StopAsync(
            CancellationToken cancellationToken = default)
        {
            return StopForCallerAsync("host", enforceOwner: false, cancellationToken);
        }

        /// <summary>
        /// Stops the active capture only when it belongs to the supplied caller key.
        /// </summary>
        /// <param name="ownerKey">Stable owner key established by the host.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The stopped source, or <see langword="null"/> if none is active.</returns>
        public ValueTask<IPubSubCaptureSource?> StopForCallerAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateOwnerKey(ownerKey);
            return StopForCallerAsync(ownerKey, enforceOwner: true, cancellationToken);
        }

        private async ValueTask<IPubSubCaptureSource?> StopForCallerAsync(
            string ownerKey,
            bool enforceOwner,
            CancellationToken cancellationToken)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ActiveCapture? active = m_active;
                if (active is null)
                {
                    return null;
                }
                if (enforceOwner && !string.Equals(active.OwnerKey, ownerKey, StringComparison.Ordinal))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                        "The active PubSub capture belongs to another caller.");
                }
                await active.Source.StopAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_active, null);
                return active.Source;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Gets the active source only when it belongs to the supplied caller key.
        /// </summary>
        /// <param name="ownerKey">Stable owner key established by the host.</param>
        /// <returns>The active source owned by the caller, or null.</returns>
        public IPubSubCaptureSource? GetActiveSourceForCaller(string ownerKey)
        {
            ValidateOwnerKey(ownerKey);
            ActiveCapture? active = Volatile.Read(ref m_active);
            return active is not null && string.Equals(active.OwnerKey, ownerKey, StringComparison.Ordinal)
                ? active.Source
                : null;
        }

        /// <summary>
        /// Replaces the stopped capture snapshot retained for one caller.
        /// </summary>
        /// <param name="ownerKey">Stable owner key established by the host.</param>
        /// <param name="source">The caller's stopped snapshot.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask StoreStoppedSourceForCallerAsync(
            string ownerKey,
            IPubSubCaptureSource source,
            CancellationToken cancellationToken = default)
        {
            ValidateOwnerKey(ownerKey);
            ArgumentNullException.ThrowIfNull(source);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                m_stoppedSources.TryGetValue(ownerKey, out IPubSubCaptureSource? previous);
                m_stoppedSources[ownerKey] = source;
                if (previous is not null && !ReferenceEquals(previous, source))
                {
                    await previous.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Gets a stopped capture snapshot only for its recorded caller.
        /// </summary>
        /// <param name="ownerKey">Stable owner key established by the host.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The caller's stopped snapshot, or null.</returns>
        public async ValueTask<IPubSubCaptureSource?> GetStoppedSourceForCallerAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateOwnerKey(ownerKey);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return m_stoppedSources.GetValueOrDefault(ownerKey);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Clears a caller's stopped snapshot.
        /// </summary>
        /// <param name="ownerKey">Stable owner key established by the host.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask ClearStoppedSourceForCallerAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateOwnerKey(ownerKey);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_stoppedSources.Remove(ownerKey, out IPubSubCaptureSource? source))
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Stores a defensive, zeroizable copy of one caller's key material.
        /// </summary>
        /// <param name="ownerKey">Stable owner key established by the host.</param>
        /// <param name="keyMaterial">Key snapshots to retain.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask StoreKeyMaterialForCallerAsync(
            string ownerKey,
            ArrayOf<PubSubKeyMaterial> keyMaterial,
            CancellationToken cancellationToken = default)
        {
            ValidateOwnerKey(ownerKey);
            if (keyMaterial.IsNull)
            {
                throw new ArgumentException("Key material must be explicit.", nameof(keyMaterial));
            }

            var copies = new List<PubSubKeyMaterial>(keyMaterial.Count);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (PubSubKeyMaterial key in keyMaterial)
                {
                    copies.Add(CopyKeyMaterial(key));
                }
                ClearKeys(ownerKey);
                m_loadedKeyMaterial.Add(ownerKey, copies.ToArray());
            }
            catch
            {
                DisposeKeys(copies);
                throw;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Gets defensive copies of only the caller's retained key material.
        /// </summary>
        /// <param name="ownerKey">Stable owner key established by the host.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Copies of the caller's loaded keys.</returns>
        public async ValueTask<ArrayOf<PubSubKeyMaterial>> CopyKeyMaterialForCallerAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateOwnerKey(ownerKey);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return m_loadedKeyMaterial.TryGetValue(ownerKey, out PubSubKeyMaterial[]? keys)
                    ? new ArrayOf<PubSubKeyMaterial>(keys.Select(CopyKeyMaterial).ToArray())
                    : [];
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Removes and zeroes a caller's retained key material.
        /// </summary>
        /// <param name="ownerKey">Stable owner key established by the host.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask ClearKeyMaterialForCallerAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateOwnerKey(ownerKey);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ClearKeys(ownerKey);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ActiveCapture? active = Interlocked.Exchange(ref m_active, null);
                if (active is not null)
                {
                    await active.Source.DisposeAsync().ConfigureAwait(false);
                }
                foreach (IPubSubCaptureSource source in m_stoppedSources.Values)
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
                m_stoppedSources.Clear();
                foreach (string ownerKey in m_loadedKeyMaterial.Keys.ToArray())
                {
                    ClearKeys(ownerKey);
                }
            }
            finally
            {
                m_gate.Release();
            }
            m_gate.Dispose();
        }

        private static void ValidateOwnerKey(string ownerKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(ownerKey.Length, 8192);
        }

        private static PubSubKeyMaterial CopyKeyMaterial(PubSubKeyMaterial key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return new PubSubKeyMaterial(
                key.SecurityGroupId,
                key.TokenId,
                key.SecurityPolicyUri,
                key.SigningKey.ToArray(),
                key.EncryptingKey.ToArray(),
                key.KeyNonce.ToArray());
        }

        private void ClearKeys(string ownerKey)
        {
            if (m_loadedKeyMaterial.Remove(ownerKey, out PubSubKeyMaterial[]? keys))
            {
                DisposeKeys(keys);
            }
        }

        private static void DisposeKeys(IEnumerable<PubSubKeyMaterial> keys)
        {
            foreach (PubSubKeyMaterial key in keys)
            {
                key.Dispose();
            }
        }

        private readonly IPubSubCaptureRegistry m_registry;
        private readonly ILoggerFactory? m_loggerFactory;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Dictionary<string, IPubSubCaptureSource> m_stoppedSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PubSubKeyMaterial[]> m_loadedKeyMaterial = new(StringComparer.Ordinal);
        private ActiveCapture? m_active;

        /// <summary>
        /// Atomically publishes a capture source together with its immutable owner.
        /// </summary>
        private sealed record ActiveCapture(string OwnerKey, InProcessPubSubCaptureSource Source);
    }
}
