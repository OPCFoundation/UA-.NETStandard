// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        internal FunctionalGroupBuilder(
            FunctionalGroupState group,
            INodeBuilder node,
            ISystemContext context)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public FunctionalGroupState Group { get; }
        public INodeBuilder Node { get; }
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
            configure(Node);
            return this;
        }
    }
}
