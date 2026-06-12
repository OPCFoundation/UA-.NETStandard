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

using System;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Default <see cref="IFunctionalGroupBuilder"/> implementation backed
    /// by a <see cref="FunctionalGroupState"/> instance attached to a
    /// parent device.
    /// </summary>
    internal sealed class FunctionalGroupBuilder : IFunctionalGroupBuilder
    {
        private readonly INodeBuilder m_node;

        internal FunctionalGroupBuilder(
            FunctionalGroupState group,
            INodeBuilder node,
            ISystemContext context)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            m_node = node ?? throw new ArgumentNullException(nameof(node));
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public FunctionalGroupState Group { get; }
        public INodeBuilder Node => m_node;
        public ISystemContext Context { get; }

        public IFunctionalGroupBuilder Organizes(NodeState target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            Group.AddReference(Types.ReferenceTypeIds.Organizes, false, target.NodeId);
            target.AddReference(Types.ReferenceTypeIds.Organizes, true, Group.NodeId);
            return this;
        }

        public IFunctionalGroupBuilder Organizes(NodeId targetNodeId)
        {
            if (targetNodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(targetNodeId));
            }
            Group.AddReference(Types.ReferenceTypeIds.Organizes, false, targetNodeId);
            return this;
        }

        public IFunctionalGroupBuilder Configure(Action<INodeBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(m_node);
            return this;
        }
    }
}
