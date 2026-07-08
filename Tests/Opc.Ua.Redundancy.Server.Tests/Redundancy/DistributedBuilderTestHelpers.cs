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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;
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
            Action<Opc.Ua.Server.Fluent.INodeManagerBuilder> build)
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
