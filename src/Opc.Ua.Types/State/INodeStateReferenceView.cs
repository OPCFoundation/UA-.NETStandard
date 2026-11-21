/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

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
