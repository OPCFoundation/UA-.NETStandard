// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Selects the explicit references of a node for an operation's address-space view.
    /// </summary>
    public interface INodeStateReferenceView
    {
        /// <summary>
        /// Gets the references captured for a node, or declines to replace its live references.
        /// </summary>
        bool TryGetReferences(NodeState node, out ArrayOf<IReference> references);

        /// <summary>
        /// Determines whether target metadata must be resolved through the selected view.
        /// </summary>
        bool IsOverridden(NodeId nodeId);
    }

    /// <summary>
    /// Indicates that browser callbacks must not run without an override-aware contract.
    /// </summary>
    public interface INodeStateSnapshotBrowserGuard
    {
        /// <summary>
        /// Gets whether the operation requires snapshot-safe browser creation.
        /// </summary>
        bool RequiresSnapshotSafeBrowser { get; }
    }
}
