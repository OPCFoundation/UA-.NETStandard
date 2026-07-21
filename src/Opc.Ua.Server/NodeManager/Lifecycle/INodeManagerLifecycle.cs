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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Adds, reloads, and removes lifecycle-managed NodeManagers on a running server.
    /// </summary>
    public interface INodeManagerLifecycle
    {
        /// <summary>
        /// Gets a snapshot of the live registrations owned by this provider.
        /// </summary>
        ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Creates and publishes a NodeManager from an asynchronous factory.
        /// </summary>
        ValueTask<NodeManagerRegistration> AddAsync(
            IAsyncNodeManagerFactory factory,
            CancellationToken ct = default);

        /// <summary>
        /// Creates and publishes a NodeManager from a synchronous factory.
        /// </summary>
        ValueTask<NodeManagerRegistration> AddAsync(
            INodeManagerFactory factory,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new asynchronous factory generation.
        /// </summary>
        ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new synchronous factory generation.
        /// </summary>
        ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new asynchronous factory generation while
        /// allowing the current generation to keep serving monitored items that were
        /// already created on it. New service requests are atomically routed to the
        /// replacement generation as soon as it is committed; the current generation is
        /// retained only for its existing monitored items and any request or continuation
        /// point that already captured it, and is disposed automatically once they drain.
        /// </summary>
        ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new synchronous factory generation while
        /// allowing the current generation to keep serving monitored items that were
        /// already created on it. New service requests are atomically routed to the
        /// replacement generation as soon as it is committed; the current generation is
        /// retained only for its existing monitored items and any request or continuation
        /// point that already captured it, and is disposed automatically once they drain.
        /// </summary>
        ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Removes a live registration from the server.
        /// </summary>
        ValueTask RemoveAsync(
            NodeManagerRegistration registration,
            CancellationToken ct = default);
    }
}
