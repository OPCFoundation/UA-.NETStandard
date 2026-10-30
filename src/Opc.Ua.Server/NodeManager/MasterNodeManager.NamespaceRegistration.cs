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

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Registers a namespace manager without blocking the calling thread while a lifecycle publication is active.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="nodeManager">The node manager that owns nodes in the namespace.</param>
        /// <param name="cancellationToken">A token that cancels the wait for namespace registration.</param>
        /// <returns>A task that completes when the namespace manager is registered.</returns>
        public ValueTask RegisterNamespaceManagerAsync(
            string namespaceUri,
            IAsyncNodeManager nodeManager,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }

            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            IAsyncNodeManager? preparingNodeManager = GetPreparingNodeManager(nodeManager);
            ChangePreparer.StagingServer? staging =
                ChangePreparer.GetServer(preparingNodeManager ?? nodeManager);
            if (staging is { IsStaging: true })
            {
                staging.NamespaceUris.GetIndexOrAppend(namespaceUri);
                staging.StageNamespaceRegistration(namespaceUri);
                return ValueTask.CompletedTask;
            }

            ThrowIfNamespaceRegistrationFromDecision();
            return RegisterNamespaceManagerCoreAsync(
                namespaceUri,
                preparingNodeManager ?? nodeManager,
                preparingNodeManager is null,
                cancellationToken);
        }

        /// <summary>
        /// Registers a namespace manager without blocking the calling thread while a lifecycle publication is active.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="nodeManager">The node manager that owns nodes in the namespace.</param>
        /// <param name="cancellationToken">A token that cancels the wait for namespace registration.</param>
        /// <returns>A task that completes when the namespace manager is registered.</returns>
        public ValueTask RegisterNamespaceManagerAsync(
            string namespaceUri,
            INodeManager nodeManager,
            CancellationToken cancellationToken = default)
        {
            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            return RegisterNamespaceManagerAsync(
                namespaceUri,
                nodeManager.ToAsyncNodeManager(),
                cancellationToken);
        }

        /// <summary>
        /// Unregisters a namespace manager without blocking the calling thread while a lifecycle publication is active.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="nodeManager">The node manager that no longer owns nodes in the namespace.</param>
        /// <param name="cancellationToken">A token that cancels the wait for namespace unregistration.</param>
        /// <returns>A task that completes with whether the namespace manager was unregistered.</returns>
        public ValueTask<bool> UnregisterNamespaceManagerAsync(
            string namespaceUri,
            IAsyncNodeManager nodeManager,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }

            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            return UnregisterNamespaceManagerCoreAsync(
                namespaceUri,
                nodeManager,
                null,
                cancellationToken);
        }

        /// <summary>
        /// Unregisters a namespace manager without blocking the calling thread while a lifecycle publication is active.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="nodeManager">The node manager that no longer owns nodes in the namespace.</param>
        /// <param name="cancellationToken">A token that cancels the wait for namespace unregistration.</param>
        /// <returns>A task that completes with whether the namespace manager was unregistered.</returns>
        public ValueTask<bool> UnregisterNamespaceManagerAsync(
            string namespaceUri,
            INodeManager nodeManager,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }

            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return UnregisterNamespaceManagerCoreAsync(
                namespaceUri,
                null,
                nodeManager,
                cancellationToken);
        }

        private async ValueTask RegisterNamespaceManagerCoreAsync(
            string namespaceUri,
            IAsyncNodeManager nodeManager,
            bool visible,
            CancellationToken cancellationToken)
        {
            await m_namespaceRegistrationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int index = Server.NamespaceUris.GetIndexOrAppend(namespaceUri);
                m_nodeManagers.RegisterNamespace(index, nodeManager, visible);
            }
            finally
            {
                m_namespaceRegistrationSemaphore.Release();
            }
        }

        private async ValueTask<bool> UnregisterNamespaceManagerCoreAsync(
            string namespaceUri,
            IAsyncNodeManager? asyncNodeManager,
            INodeManager? nodeManager,
            CancellationToken cancellationToken)
        {
            IAsyncNodeManager candidate = asyncNodeManager ?? nodeManager!.ToAsyncNodeManager();
            ChangePreparer.StagingServer? staging =
                ChangePreparer.GetServer(GetPreparingNodeManager(candidate) ?? candidate);
            if (staging is { IsStaging: true })
            {
                return staging.UnstageNamespaceRegistration(namespaceUri);
            }

            ThrowIfNamespaceRegistrationFromDecision();
            await m_namespaceRegistrationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int namespaceIndex = Server.NamespaceUris.GetIndex(namespaceUri);
                return namespaceIndex >= 0 && m_nodeManagers.UnregisterNamespace(
                    namespaceIndex,
                    asyncNodeManager,
                    nodeManager);
            }
            finally
            {
                m_namespaceRegistrationSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// Adds asynchronous namespace-manager registration to master node managers.
    /// </summary>
    public static class MasterNodeManagerNamespaceRegistrationExtensions
    {
        /// <summary>
        /// Registers a namespace manager without blocking the calling thread when the master supports asynchronous
        /// namespace registration.
        /// </summary>
        /// <param name="master">The master node manager.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="nodeManager">The node manager that owns nodes in the namespace.</param>
        /// <param name="cancellationToken">A token that cancels the wait for namespace registration.</param>
        /// <returns>A task that completes when the namespace manager is registered.</returns>
        public static ValueTask RegisterNamespaceManagerAsync(
            this IMasterNodeManager master,
            string namespaceUri,
            IAsyncNodeManager nodeManager,
            CancellationToken cancellationToken = default)
        {
            if (master == null)
            {
                throw new ArgumentNullException(nameof(master));
            }

            if (master is MasterNodeManager implementation)
            {
                return implementation.RegisterNamespaceManagerAsync(namespaceUri, nodeManager, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            master.RegisterNamespaceManager(namespaceUri, nodeManager);
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Registers a namespace manager without blocking the calling thread when the master supports asynchronous
        /// namespace registration.
        /// </summary>
        /// <param name="master">The master node manager.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="nodeManager">The node manager that owns nodes in the namespace.</param>
        /// <param name="cancellationToken">A token that cancels the wait for namespace registration.</param>
        /// <returns>A task that completes when the namespace manager is registered.</returns>
        public static ValueTask RegisterNamespaceManagerAsync(
            this IMasterNodeManager master,
            string namespaceUri,
            INodeManager nodeManager,
            CancellationToken cancellationToken = default)
        {
            if (master == null)
            {
                throw new ArgumentNullException(nameof(master));
            }

            if (master is MasterNodeManager implementation)
            {
                return implementation.RegisterNamespaceManagerAsync(namespaceUri, nodeManager, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            master.RegisterNamespaceManager(namespaceUri, nodeManager);
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Unregisters a namespace manager without blocking the calling thread when the master supports asynchronous
        /// namespace registration.
        /// </summary>
        /// <param name="master">The master node manager.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="nodeManager">The node manager that no longer owns nodes in the namespace.</param>
        /// <param name="cancellationToken">A token that cancels the wait for namespace unregistration.</param>
        /// <returns>A task that completes with whether the namespace manager was unregistered.</returns>
        public static ValueTask<bool> UnregisterNamespaceManagerAsync(
            this IMasterNodeManager master,
            string namespaceUri,
            IAsyncNodeManager nodeManager,
            CancellationToken cancellationToken = default)
        {
            if (master == null)
            {
                throw new ArgumentNullException(nameof(master));
            }

            if (master is MasterNodeManager implementation)
            {
                return implementation.UnregisterNamespaceManagerAsync(
                    namespaceUri,
                    nodeManager,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(master.UnregisterNamespaceManager(namespaceUri, nodeManager));
        }

        /// <summary>
        /// Unregisters a namespace manager without blocking the calling thread when the master supports asynchronous
        /// namespace registration.
        /// </summary>
        /// <param name="master">The master node manager.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="nodeManager">The node manager that no longer owns nodes in the namespace.</param>
        /// <param name="cancellationToken">A token that cancels the wait for namespace unregistration.</param>
        /// <returns>A task that completes with whether the namespace manager was unregistered.</returns>
        public static ValueTask<bool> UnregisterNamespaceManagerAsync(
            this IMasterNodeManager master,
            string namespaceUri,
            INodeManager nodeManager,
            CancellationToken cancellationToken = default)
        {
            if (master == null)
            {
                throw new ArgumentNullException(nameof(master));
            }

            if (master is MasterNodeManager implementation)
            {
                return implementation.UnregisterNamespaceManagerAsync(
                    namespaceUri,
                    nodeManager,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(master.UnregisterNamespaceManager(namespaceUri, nodeManager));
        }
    }
}
