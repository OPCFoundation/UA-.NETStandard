// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <summary>
    /// Declares exact nodes whose ownership is replaced while this generation is published.
    /// </summary>
    /// <remarks>
    /// The declaration must remain unchanged for the lifetime of the node manager.
    /// </remarks>
    public interface INodeManagerNodeOverrides
    {
        /// <summary>
        /// Gets the exact node identifiers replaced by this manager.
        /// </summary>
        ArrayOf<NodeId> OverriddenNodeIds { get; }
    }
}
