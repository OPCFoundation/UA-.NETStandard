// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Commits native source ownership under the base manager's existing mutation gate.
    /// Subscription ownership and unresolved policy remain outside the native item manager.
    /// </summary>
    internal interface IMonitoredItemSourceManager
    {
        /// <summary>
        /// Releases native registration without disposing the subscription item.
        /// </summary>
        (ServiceResult Result, bool Changed) ReleaseSource(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            long sourceRevision,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache);

        /// <summary>
        /// Adopts an item only while the caller's captured source incarnation is still current.
        /// </summary>
        (ServiceResult Result, bool Changed) AdoptSource(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache,
            long sourceRevision,
            MonitoredItemAdoption? adoption = null);
    }
}
