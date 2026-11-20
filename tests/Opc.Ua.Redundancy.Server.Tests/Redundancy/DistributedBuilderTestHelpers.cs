// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Redundancy;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Minimal <see cref="IOpcUaServerBuilder"/> over a bare
    /// <see cref="IServiceCollection"/> for exercising the fluent registration
    /// extensions without a hosted server.
    /// </summary>
    internal sealed class DiTestServerBuilder : IOpcUaServerBuilder
    {
        public DiTestServerBuilder()
            : this(new ServiceCollection())
        {
        }

        public DiTestServerBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }

        public IOpcUaServerBuilder AddNodeManager<TFactory>()
            where TFactory : class, IAsyncNodeManagerFactory
        {
            return this;
        }

        public IOpcUaServerBuilder AddSyncNodeManager<TFactory>()
            where TFactory : class, INodeManagerFactory
        {
            return this;
        }

        public IOpcUaServerBuilder AddNodeManager(
            string namespaceUri,
            Action<Fluent.INodeManagerBuilder> build)
        {
            return this;
        }
    }

    /// <summary>
    /// A shared key/value store decorator that fails a chosen operation so the
    /// error-handling paths of the redundancy publishers can be exercised
    /// deterministically. All other operations delegate to the inner store.
    /// </summary>
    internal sealed class ThrowingSharedKeyValueStore : ISharedKeyValueStore, IDisposable
    {
        public ThrowingSharedKeyValueStore(ISharedKeyValueStore inner, bool throwOnSet = false)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            ThrowOnSet = throwOnSet;
        }

        public bool ThrowOnSet { get; set; }

        public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            return m_inner.TryGetAsync(key, ct);
        }

        public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            if (ThrowOnSet)
            {
                throw new InvalidOperationException("Injected store failure.");
            }
            return m_inner.SetAsync(key, value, ct);
        }

        public ValueTask<bool> CompareAndSwapAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct = default)
        {
            return m_inner.CompareAndSwapAsync(key, expected, value, ct);
        }

        public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            return m_inner.DeleteAsync(key, ct);
        }

        public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            CancellationToken ct = default)
        {
            return m_inner.ScanAsync(keyPrefix, ct);
        }

        public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
        {
            return m_inner.WatchAsync(keyPrefix, ct);
        }

        public void Dispose()
        {
            (m_inner as IDisposable)?.Dispose();
        }

        private readonly ISharedKeyValueStore m_inner;
    }
}
