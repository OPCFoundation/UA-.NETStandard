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

namespace Opc.Ua
{
    /// <summary>
    /// Helpers for materialising type instances at runtime.
    /// </summary>
    /// <remarks>
    /// Deliberately a distinct type from <see cref="NodeStateExtensions"/>
    /// (which is redefined per stack assembly) so the source-generated
    /// <c>CreateInstanceOf&lt;Type&gt;</c> factories can reference it by a
    /// single, unambiguous fully-qualified name from any generated model.
    /// </remarks>
    public static class NodeInstanceExtensions
    {
        /// <summary>
        /// Recursively assigns per-instance NodeIds to every descendant of
        /// <paramref name="node"/> using the active
        /// <see cref="ISystemContext.NodeIdFactory"/>.
        /// </summary>
        /// <remarks>
        /// Rebases a dynamically instantiated subtree (created via a
        /// generated <c>CreateInstanceOf&lt;Type&gt;</c> factory, which stamps
        /// TYPE NodeIds on children) onto per-instance NodeIds derived from
        /// the parent, so multiple instances of the same type never collide
        /// on those NodeIds. Walks top-down so each child's NodeId derives
        /// from its already-rebased parent. No-op when the context has no
        /// <see cref="ISystemContext.NodeIdFactory"/>.
        /// </remarks>
        /// <param name="context">
        /// The system context supplying the NodeIdFactory.
        /// </param>
        /// <param name="node">
        /// The instance whose descendants are rebased.
        /// </param>
        public static void AssignInstanceChildNodeIds(
            this ISystemContext context,
            NodeState node)
        {
            if (context?.NodeIdFactory == null || node == null)
            {
                return;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                child.NodeId = context.NodeIdFactory.New(context, child);
                context.AssignInstanceChildNodeIds(child);
            }
        }
    }
}
