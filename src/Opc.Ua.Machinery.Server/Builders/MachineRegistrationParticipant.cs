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
using Opc.Ua.Di.Server;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Reuses a machine scope while another companion owns publication and rollback of the tree.
    /// </summary>
    /// <typeparam name="TState">The externally staged root type.</typeparam>
    internal sealed class MachineRegistrationParticipant<TState> : ICompanionRegistrationParticipant
        where TState : BaseObjectState
    {
        /// <summary>
        /// Captures the unregistered root and deferred binding configuration.
        /// </summary>
        internal MachineRegistrationParticipant(
            MachineryBuildContext context,
            TState root,
            Action<IMachineBuilder<TState>> configure)
        {
            m_context = context;
            m_root = root;
            m_configure = configure;
        }

        /// <inheritdoc/>
        public DiNodeManager Manager => m_context.Manager;

        /// <inheritdoc/>
        public BaseObjectState Root => m_root;

        /// <inheritdoc/>
        public async ValueTask PrepareAsync(CancellationToken cancellationToken = default)
        {
            if (m_started || m_disposed)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidState, "The binding is single-use.");
            }
            m_started = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (m_root.BrowseName.IsNull || m_root.NodeId.IsNull ||
                m_root.NodeId.NamespaceIndex != m_context.InstanceNamespaceIndex ||
                Manager.FindPredefinedNode(m_root.NodeId) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The binding requires an unregistered root in the context's instance namespace.");
            }
            m_lease = MachineryBuildScope.AcquireBuildLease(m_context);
            m_scope = new MachineryBuildScope(
                m_context,
                m_context.MachinesFolder,
                m_root.BrowseName,
                externallyRegistered: true);
            var builder = new MachineBuilder<TState>(m_scope, m_root);
            NodeState? parent = m_root.Parent;
            NodeId reference = m_root.ReferenceTypeId;
            NodeId nodeId = m_root.NodeId;
            QualifiedName browseName = m_root.BrowseName;
            m_configure(builder);
            if (!ReferenceEquals(m_root.Parent, parent) || m_root.ReferenceTypeId != reference ||
                m_root.NodeId != nodeId || m_root.BrowseName != browseName)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A binding cannot change the externally owned root's identity.");
            }
            await m_scope.PrepareExternalAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask BindAsync(CancellationToken cancellationToken = default)
        {
            if (m_scope == null || m_bound || m_disposed)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidState, "The binding is not prepared.");
            }
            await m_scope.BindExternalAsync(cancellationToken).ConfigureAwait(false);
            m_bound = true;
        }

        /// <inheritdoc/>
        public void Commit()
        {
            if (m_bound && !m_disposed)
            {
                m_scope!.CommitExternal();
                m_lease?.Dispose();
                m_lease = null;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                if (m_scope != null)
                {
                    await m_scope.AbortExternalAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_lease?.Dispose();
                m_lease = null;
            }
        }

        private readonly MachineryBuildContext m_context;
        private readonly TState m_root;
        private readonly Action<IMachineBuilder<TState>> m_configure;
        private MachineryBuildScope? m_scope;
        private IDisposable? m_lease;
        private bool m_started;
        private bool m_bound;
        private bool m_disposed;
    }
}
