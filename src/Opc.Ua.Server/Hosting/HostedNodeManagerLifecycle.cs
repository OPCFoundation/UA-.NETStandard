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

namespace Opc.Ua.Server.Hosting
{
    internal sealed class HostedNodeManagerLifecycle : INodeManagerLifecycle
    {
        public ArrayOf<NodeManagerRegistration> Registrations
            => Current.Registrations;

        public ValueTask<NodeManagerRegistration> AddAsync(
            IAsyncNodeManagerFactory factory,
            CancellationToken ct = default)
        {
            return Current.AddAsync(factory, ct);
        }

        public ValueTask<NodeManagerRegistration> AddAsync(
            INodeManagerFactory factory,
            CancellationToken ct = default)
        {
            return Current.AddAsync(factory, ct);
        }

        public ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return Current.ReloadAsync(registration, replacement, ct);
        }

        public ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return Current.ReloadAsync(registration, replacement, ct);
        }

        public ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return Current.ShadowReloadAsync(registration, replacement, ct);
        }

        public ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return Current.ShadowReloadAsync(registration, replacement, ct);
        }

        public ValueTask RemoveAsync(
            NodeManagerRegistration registration,
            CancellationToken ct = default)
        {
            return Current.RemoveAsync(registration, ct);
        }

        internal void Attach(INodeManagerLifecycle lifecycle)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            INodeManagerLifecycle? previous = Interlocked.CompareExchange(
                ref m_current,
                lifecycle,
                null);
            if (previous is not null && !ReferenceEquals(previous, lifecycle))
            {
                throw new InvalidOperationException(
                    "An OPC UA server is already attached to this lifecycle provider.");
            }
        }

        internal void Detach(INodeManagerLifecycle lifecycle)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            Interlocked.CompareExchange(ref m_current, null, lifecycle);
        }

        private INodeManagerLifecycle Current
            => Volatile.Read(ref m_current)
                ?? throw new InvalidOperationException(
                    "The hosted OPC UA server is not running.");

        private INodeManagerLifecycle? m_current;
    }
}
