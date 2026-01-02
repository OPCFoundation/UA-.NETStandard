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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The interface for the core node manager.
    /// </summary>
    /// <remarks>
    /// This interface defines the contract for the core node manager that handles
    /// the built-in OPC UA nodes (namespace 0) and the server's dynamic namespace.
    /// It extends INodeManager2 with additional methods specific to the core node manager.
    /// </remarks>
    public interface ICoreNodeManager : INodeManager2
    {
        /// <summary>
        /// Acquires the lock on the node manager.
        /// </summary>
        /// <remarks>
        /// This lock should be used when accessing or modifying the node manager's internal state.
        /// </remarks>
        object DataLock { get; }

        /// <summary>
        /// Imports the nodes from a collection of NodeState objects.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="predefinedNodes">The predefined nodes to import.</param>
        /// <remarks>
        /// This method is used to add nodes to the core node manager's address space.
        /// It is typically called during initialization or when loading predefined nodes.
        /// </remarks>
        void ImportNodes(ISystemContext context, IEnumerable<NodeState> predefinedNodes);

        /// <summary>
        /// Creates a unique node identifier.
        /// </summary>
        /// <returns>A new unique NodeId in the dynamic namespace.</returns>
        /// <remarks>
        /// This method generates unique node identifiers for dynamically created nodes.
        /// The NodeIds are created in the server's dynamic namespace.
        /// </remarks>
        NodeId CreateUniqueNodeId();

        /// <summary>
        /// Returns the namespace index used for dynamically created nodes.
        /// </summary>
        /// <value>The dynamic namespace index.</value>
        ushort DynamicNamespaceIndex { get; }
    }
}
