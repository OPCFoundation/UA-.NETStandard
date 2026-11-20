/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
                return default;
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

            ServerUrisVersion.Update(Server);
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
            return default;
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
            return default;
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
            return new ValueTask<bool>(master.UnregisterNamespaceManager(namespaceUri, nodeManager));
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
            return new ValueTask<bool>(master.UnregisterNamespaceManager(namespaceUri, nodeManager));
        }
    }
}
