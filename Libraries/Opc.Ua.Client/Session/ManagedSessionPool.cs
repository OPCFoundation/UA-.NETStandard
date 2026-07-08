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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Default keyed managed-session pool.
    /// </summary>
    public sealed class ManagedSessionPool : IManagedSessionPool
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public ManagedSessionPool(IManagedSessionFactory factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <inheritdoc/>
        public Task<ManagedSession> GetOrConnectAsync(
            string key,
            ConfiguredEndpoint endpoint,
            CancellationToken ct = default)
        {
            return GetOrConnectAsync(key, endpoint, _ => { }, ct);
        }

        /// <inheritdoc/>
        public Task<ManagedSession> GetOrConnectAsync(
            string key,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A non-empty key is required.", nameof(key));
            }
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            Lazy<Task<ManagedSession>> created = new(
                () => ConnectAndEvictOnFailureAsync(key, endpoint, configure, ct),
                LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<Task<ManagedSession>> lazy = m_sessions.GetOrAdd(key, created);
            return lazy.Value;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A non-empty key is required.", nameof(key));
            }

            if (!m_sessions.TryRemove(key, out Lazy<Task<ManagedSession>>? lazy))
            {
                return false;
            }

            ManagedSession session = await lazy.Value.ConfigureAwait(false);
            await session.CloseAsync(ct).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (string key in m_sessions.Keys)
            {
                if (m_sessions.TryRemove(key, out Lazy<Task<ManagedSession>>? lazy) &&
                    lazy.IsValueCreated &&
                    lazy.Value.Status == TaskStatus.RanToCompletion)
                {
                    lazy.Value.GetAwaiter().GetResult().Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (string key in m_sessions.Keys)
            {
                await RemoveAsync(key).ConfigureAwait(false);
            }
        }

        private async Task<ManagedSession> ConnectAndEvictOnFailureAsync(
            string key,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct)
        {
            try
            {
                return await m_factory.ConnectAsync(endpoint, configure, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                m_sessions.TryRemove(key, out _);
                throw;
            }
        }

        private readonly IManagedSessionFactory m_factory;
        private readonly ConcurrentDictionary<string, Lazy<Task<ManagedSession>>> m_sessions =
            new(StringComparer.Ordinal);
    }
}
