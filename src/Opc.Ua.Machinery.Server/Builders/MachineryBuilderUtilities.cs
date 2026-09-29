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
using System.Collections.Generic;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Placement helpers shared by the Machinery builders.
    /// </summary>
    internal static class MachineryBuilderUtilities
    {
        /// <summary>
        /// The OPC 10000-3 Annex C <c>HasAddIn</c> reference type. OPC 40001-1
        /// attaches every building block — identification, monitoring,
        /// components, equipment, notifications — to its machinery item with
        /// this reference, not with <c>HasComponent</c>.
        /// </summary>
        public static NodeId HasAddIn => Opc.Ua.Types.ReferenceTypeIds.HasAddIn;

        public static TState AddAddIn<TState>(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName,
            Func<ISystemContext, NodeState, QualifiedName, TState> factory)
            where TState : BaseInstanceState
        {
            TState state = factory(context, parent, browseName);
            state.SymbolicName = browseName.Name ?? string.Empty;
            state.BrowseName = browseName;
            state.DisplayName = new LocalizedText(browseName.Name);
            state.ReferenceTypeId = HasAddIn;
            state.ModellingRuleId = NodeId.Null;
            parent.AddChild(state);
            return state;
        }

        /// <summary>
        /// Adds a plain <c>FolderType</c> child the models declare no factory
        /// for — the OPC 40001-1 <c>MachineryBuildingBlocks</c> organizer and
        /// the OPC 40001-4 resource folders both live at instance level only.
        /// </summary>
        public static FolderState AddFolderChild(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName,
            NodeId referenceTypeId)
        {
            var folder = new FolderState(parent)
            {
                SymbolicName = browseName.Name ?? string.Empty,
                BrowseName = browseName,
                DisplayName = new LocalizedText(browseName.Name),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType,
                ReferenceTypeId = referenceTypeId,
                ModellingRuleId = NodeId.Null,
                EventNotifier = EventNotifiers.None
            };
            folder.NodeId = context.NodeIdFactory!.New(context, folder);
            parent.AddChild(folder);
            return folder;
        }

        public static TState AddComponentChild<TState>(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName,
            Func<ISystemContext, NodeState, QualifiedName, TState> factory)
            where TState : BaseInstanceState
        {
            TState state = factory(context, parent, browseName);
            state.SymbolicName = browseName.Name ?? string.Empty;
            state.BrowseName = browseName;
            state.DisplayName = new LocalizedText(browseName.Name);
            state.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            state.ModellingRuleId = NodeId.Null;
            parent.AddChild(state);
            return state;
        }

        /// <summary>
        /// Mints instance NodeIds for a subtree the generated placeholder
        /// factories produced. Those factories keep the type-level declaration
        /// NodeId — unlike <c>CreateInstanceOf*</c>, which rebases when it is
        /// given a browse name — so a placeholder child would otherwise land in
        /// the model namespace instead of the application-owned one.
        /// </summary>
        public static void AssignInstanceNodeIds(ISystemContext context, NodeState node)
        {
            NodeId previousNodeId = context.AssignInstanceNodeId(node);
            context.AssignInstanceChildNodeIds(node, previousNodeId);
        }

        public static T? FindChild<T>(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName)
            where T : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is T typed &&
                    (typed.BrowseName == browseName ||
                        string.Equals(
                            typed.SymbolicName,
                            browseName.Name,
                            StringComparison.Ordinal)))
                {
                    return typed;
                }
            }
            return null;
        }

        public static T FindRequiredChild<T>(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName)
            where T : BaseInstanceState
        {
            return FindChild<T>(context, parent, browseName) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The generated child '{0}' is missing below '{1}'.",
                    browseName,
                    parent.BrowseName);
        }

        /// <summary>
        /// Strips the modelling rule from a subtree. Instance children the
        /// generated factories create inherit the type declaration's
        /// modelling rule, which has no meaning on an instance and shows up
        /// in address-space compliance checks.
        /// </summary>
        public static void ClearModellingRules(ISystemContext context, NodeState root)
        {
            var nodes = new List<NodeState> { root };
            var children = new List<BaseInstanceState>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                NodeState node = nodes[ii];
                if (node is BaseInstanceState instance)
                {
                    instance.ModellingRuleId = NodeId.Null;
                }
                children.Clear();
                node.GetChildren(context, children);
                for (int childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    nodes.Add(children[childIndex]);
                }
            }
        }

        public static ushort NamespaceIndex(ISystemContext context, string namespaceUri)
        {
            int index = context.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The namespace '{0}' is not registered.",
                    namespaceUri);
            }
            return (ushort)index;
        }
    }
}
