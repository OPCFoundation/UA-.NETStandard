// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    public sealed partial class NodeManagerLifecycle
    {
        /// <summary>
        /// Admits only removals belonging to an exact, independently serialized shutdown invocation.
        /// </summary>
        private sealed class ShutdownPublication(
            NodeManagerLifecycle owner,
            IServerInternal server,
            LifecycleWriter.ShutdownCleanup cleanup)
        {
            /// <summary>
            /// Creates transferable operation ownership without reopening ordinary lifecycle entry.
            /// </summary>
            internal LifecycleWriter.OperationLifetime EnterOperation()
            {
                return cleanup.EnterOperation();
            }

            /// <summary>
            /// Gets the retained server even after it has left the Running state.
            /// </summary>
            internal (IServerInternal Server, IDynamicNodeManagerHost Host) GetServer()
            {
                Validate(owner, server, m_host);
                return (server, m_host);
            }

            /// <summary>
            /// Prevents this capability from preparing additions, replacements or read images.
            /// </summary>
            internal void ValidateChanges(ArrayOf<NodeManagerBatchChange> changes)
            {
                foreach (NodeManagerBatchChange change in changes)
                {
                    if (change.Current is null || change.Factory is not null)
                    {
                        throw new InvalidOperationException("Shutdown publication admits only generation removals.");
                    }
                }
            }

            /// <summary>
            /// Revalidates exact server, host and invocation ownership at each publication boundary.
            /// </summary>
            internal void Validate(
                NodeManagerLifecycle lifecycle,
                IServerInternal instance,
                IDynamicNodeManagerHost host)
            {
                cleanup.Validate(owner.m_writer);
                if (!ReferenceEquals(owner, lifecycle) ||
                    !ReferenceEquals(server, instance) ||
                    !ReferenceEquals(m_host, host) ||
                    !ReferenceEquals(owner.m_server.CurrentInstance, server) ||
                    !ReferenceEquals(server.NodeManager, host))
                {
                    throw new InvalidOperationException("The shutdown publication does not own this server.");
                }
            }

            /// <summary>The retained host whose generations belong to this shutdown.</summary>
            private readonly IDynamicNodeManagerHost m_host = server.NodeManager as IDynamicNodeManagerHost ??
                throw new NotSupportedException("The server does not support generation removal publication.");
        }
    }
}
