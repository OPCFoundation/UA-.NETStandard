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
using System.Runtime.CompilerServices;
using System.Threading;
using Opc.Ua.Server;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Materializes generated optional children using the published instance declarations,
    /// including method arguments and namespace-qualified AddIn references.
    /// </summary>
    internal static class MachineVisionNodes
    {
        /// <summary>
        /// Publishes a root name reservation before asynchronous node registration.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static void AttachRoot(IVisionBuildContext context, BaseInstanceState state)
        {
            lock (s_rootGates.GetValue(context.Root, static _ => new Lock()))
            {
                if (context.Root.FindChild(context.Context, state.BrowseName) != null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated, "The vision instance browse name already exists.");
                }
                context.Root.AddChild(state);
            }
        }

        /// <summary>
        /// Resolves or creates one declared child and preserves its exact wire metadata.
        /// </summary>
        /// <typeparam name="T">The expected generated child state type.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        public static T Require<T>(AsyncCustomNodeManager manager, NodeState parent, string name,
            Func<ISystemContext, NodeState, string, BaseInstanceState?>? createAddIn = null)
            where T : BaseInstanceState
        {
            string[] namespaces = new[]
            {
                MachineVisionModels.ControlNamespaceUri,
                MachineVisionModels.AssetNamespaceUri,
                Di.Namespaces.OpcUaDi,
                Machinery.Namespaces.Machinery,
                Namespaces.OpcUa
            };
            ServerSystemContext context = manager.SystemContext;
            foreach (string uri in namespaces)
            {
                int index = context.NamespaceUris.GetIndex(uri);
                if (index < 0)
                {
                    continue;
                }
                var browseName = new QualifiedName(name, (ushort)index);
                if (parent.FindChild(context, browseName) is T existing)
                {
                    return existing;
                }
                BaseInstanceState? declaration = FindDeclaration(manager, parent, browseName);
                if (declaration == null)
                {
                    continue;
                }
                if (parent.CreateChild(context, browseName) is not T child)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch, "The generated child '{0}' has an incompatible state type.", name);
                }
                child.Create(context, NodeId.Null, browseName, new LocalizedText(name), true);
                CopyMetadata(context, child, declaration);
                Rebase(context, child);
                return child;
            }
            if (createAddIn?.Invoke(context, parent, name) is T addIn)
            {
                addIn.ReferenceTypeId = ReferenceTypeIds.HasAddIn;
                parent.AddChild(addIn);
                return addIn;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadBrowseNameInvalid, "'{0}' declares no child named '{1}'.", parent.BrowseName, name);
        }

        /// <summary>
        /// Initializes a value without substituting missing data.
        /// The owning runtime publishes accumulated change masks asynchronously.
        /// </summary>
        public static void Set(
            ISystemContext context,
            BaseVariableState variable,
            Variant value,
            DateTimeUtc timestamp)
        {
            ValidateValue(context, variable, value);
            variable.WrappedValue = value;
            variable.StatusCode = value.IsNull ? StatusCodes.BadWaitingForInitialData : StatusCodes.Good;
            variable.Timestamp = timestamp;
        }

        /// <summary>
        /// Checks a proposed value without changing or notifying its published variable.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static void ValidateValue(ISystemContext context, BaseVariableState variable, Variant value)
        {
            if (!value.IsNull &&
                TypeInfo.IsInstanceOfDataType(
                    value, variable.DataType, variable.ValueRank, context.NamespaceUris, context.TypeTable).IsUnknown)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch, "The supplied value does not match '{0}'.", variable.BrowseName);
            }
        }

        /// <summary>
        /// Rebases a legacy Create-initialized node away from declaration identifiers.
        /// </summary>
        public static void Rebase(ISystemContext context, NodeState node)
        {
            NodeId previous = context.AssignInstanceNodeId(node);
            context.AssignInstanceChildNodeIds(node, previous);
        }

        /// <summary>
        /// Finds a fixed child declaration through the type's supertype chain.
        /// </summary>
        private static BaseInstanceState? FindDeclaration(
            AsyncCustomNodeManager manager,
            NodeState parent,
            QualifiedName name)
        {
            NodeId type = parent is BaseInstanceState instance
                ? instance.TypeDefinitionId
                : parent.NodeId;
            var visited = new HashSet<NodeId>();
            while (!type.IsNull && visited.Add(type))
            {
                NodeState? declaration = manager.FindPredefinedNode<NodeState>(type);
                if (declaration == null &&
                    type.NamespaceIndex == 0 &&
                    manager.Server.CoreNodeManager is AsyncCustomNodeManager core)
                {
                    declaration = core.FindPredefinedNode<NodeState>(type);
                }
                BaseInstanceState? child = declaration?.FindChild(manager.SystemContext, name);
                if (child != null)
                {
                    return child;
                }
                type = manager.SystemContext.TypeTable.FindSuperType(type);
            }
            return null;
        }

        /// <summary>
        /// Restores the declaration-specific restrictions omitted by a generic child constructor.
        /// </summary>
        private static void CopyMetadata(
            ISystemContext context,
            BaseInstanceState child,
            BaseInstanceState declaration)
        {
            child.ReferenceTypeId = declaration.ReferenceTypeId;
            child.TypeDefinitionId = declaration.TypeDefinitionId;
            child.Description = declaration.Description;
            if (child is BaseVariableState variable && declaration is BaseVariableState source)
            {
                variable.DataType = source.DataType;
                variable.ValueRank = source.ValueRank;
                variable.ArrayDimensions = source.ArrayDimensions;
                variable.AccessLevel = source.AccessLevel;
                variable.UserAccessLevel = source.UserAccessLevel;
                variable.MinimumSamplingInterval = source.MinimumSamplingInterval;
                variable.WrappedValue = CoreUtils.Clone(source.WrappedValue);
            }
            if (child is MethodState method && declaration is MethodState declaredMethod)
            {
                method.MethodDeclarationId = declaredMethod.NodeId;
                if (declaredMethod.InputArguments != null)
                {
                    method.CreateOrReplaceInputArguments(context, null).Value =
                        CoreUtils.Clone(declaredMethod.InputArguments.Value);
                }
                if (declaredMethod.OutputArguments != null)
                {
                    method.CreateOrReplaceOutputArguments(context, null).Value =
                        CoreUtils.Clone(declaredMethod.OutputArguments.Value);
                }
            }
        }

        private static readonly ConditionalWeakTable<NodeState, Lock> s_rootGates = new();
    }
}
