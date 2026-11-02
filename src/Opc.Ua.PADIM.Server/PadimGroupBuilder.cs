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

namespace Opc.Ua.PADIM.Server
{
    /// <summary>
    /// Selects supported fields from a generated calibration or condition declaration.
    /// </summary>
    /// <remarks>
    /// The published declaration supplies CLR state types, wire types, ranks and semantic references.
    /// Only requested optional children are attached to the instance.
    /// </remarks>
    public sealed class PadimGroupBuilder
    {
        /// <summary>
        /// Creates a selector over the compiled declaration and its configured instance.
        /// </summary>
        internal PadimGroupBuilder(
            PadimDevice device,
            BaseObjectState node,
            ArrayOf<BaseObjectState> declarations)
        {
            m_device = device;
            Node = node;
            m_declarations = declarations;
            foreach (BaseObjectState declaration in declarations)
            {
                var references = new List<IReference>();
                declaration.GetReferences(device.Context, references);
                foreach (IReference reference in references)
                {
                    if (!reference.IsInverse &&
                        reference.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.HasInterface &&
                        !node.ReferenceExists(reference.ReferenceTypeId, false, reference.TargetId))
                    {
                        node.AddReference(reference.ReferenceTypeId, false, reference.TargetId);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the generated object instance with its declared interfaces.
        /// </summary>
        public BaseObjectState Node { get; }

        /// <summary>
        /// Binds a declared variable without changing its type, rank or dictionary classification.
        /// </summary>
        public PadimValueBinding Bind(
            QualifiedName browseName,
            string key,
            IPadimValueProvider provider)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A provider key is required.", nameof(key));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            BaseInstanceState declaration = RequireDeclaration(browseName);
            if (declaration is not BaseVariableState)
            {
                throw new ArgumentException("The selected member must be a variable.", nameof(browseName));
            }
            BaseInstanceState child = GetOrCreate(declaration, browseName);
            return m_device.BindValue((BaseVariableState)child, key, provider);
        }

        /// <summary>
        /// Selects a declared nested group, such as a condition set or calibration point set.
        /// </summary>
        public PadimGroupBuilder Group(
            QualifiedName declarationName,
            Action<PadimGroupBuilder> configure,
            QualifiedName instanceName = default)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            BaseInstanceState declaration = RequireDeclaration(declarationName);
            if (declaration is not BaseObjectState objectDeclaration)
            {
                throw new ArgumentException("The selected member must be an object.", nameof(declarationName));
            }
            bool placeholder =
                declaration.ModellingRuleId == Opc.Ua.Types.ObjectIds.ModellingRule_OptionalPlaceholder ||
                declaration.ModellingRuleId == Opc.Ua.Types.ObjectIds.ModellingRule_MandatoryPlaceholder;
            if (placeholder && instanceName.IsNull)
            {
                throw new ArgumentException("A placeholder instance requires a browse name.", nameof(instanceName));
            }
            QualifiedName name = instanceName.IsNull ? declarationName : instanceName;
            if (!placeholder && name != declarationName)
            {
                throw new ArgumentException("Only a placeholder may be renamed.", nameof(instanceName));
            }
            var group = (BaseObjectState)GetOrCreate(objectDeclaration, name);
            var declarations = new List<BaseObjectState>();
            foreach (BaseInstanceState candidate in FindDeclarations(declarationName))
            {
                if (candidate is BaseObjectState objectCandidate)
                {
                    declarations.Add(objectCandidate);
                }
            }
            var builder = new PadimGroupBuilder(m_device, group, [.. declarations]);
            configure(builder);
            return this;
        }

        /// <summary>
        /// Copies generated state metadata and assigns fresh instance identifiers using the common instancer.
        /// </summary>
        internal static BaseInstanceState Instantiate(
            ISystemContext context,
            BaseInstanceState declaration,
            NodeState parent,
            QualifiedName browseName)
        {
            if (declaration.Clone() is not BaseInstanceState instance)
            {
                throw new InvalidOperationException("The compiled declaration did not clone to an instance.");
            }
            instance.BrowseName = browseName;
            if (declaration.ModellingRuleId == Opc.Ua.Types.ObjectIds.ModellingRule_OptionalPlaceholder ||
                declaration.ModellingRuleId == Opc.Ua.Types.ObjectIds.ModellingRule_MandatoryPlaceholder)
            {
                instance.DisplayName = new LocalizedText(browseName.Name);
            }
            PruneOptionalChildren(context, instance);
            parent.AddChild(instance);
            NodeId previousId = context.AssignInstanceNodeId(instance);
            context.AssignInstanceChildNodeIds(instance, previousId, parent);
            return instance;
        }

        /// <summary>
        /// Removes unconfigured optional declarations while preserving mandatory children and metadata.
        /// </summary>
        private static void PruneOptionalChildren(ISystemContext context, BaseInstanceState instance)
        {
            instance.ModellingRuleId = NodeId.Null;
            var children = new List<BaseInstanceState>();
            instance.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child.ModellingRuleId == Opc.Ua.Types.ObjectIds.ModellingRule_Optional ||
                    child.ModellingRuleId == Opc.Ua.Types.ObjectIds.ModellingRule_OptionalPlaceholder ||
                    child.ModellingRuleId == Opc.Ua.Types.ObjectIds.ModellingRule_MandatoryPlaceholder)
                {
                    instance.RemoveChild(child);
                }
                else
                {
                    PruneOptionalChildren(context, child);
                }
            }
        }

        /// <summary>
        /// Resolves only published children of this family's actual declaration.
        /// </summary>
        private BaseInstanceState RequireDeclaration(QualifiedName name)
        {
            if (name.IsNull)
            {
                throw new ArgumentException("A declared browse name is required.", nameof(name));
            }
            foreach (BaseInstanceState declaration in FindDeclarations(name))
            {
                return declaration;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNotSupported,
                "The selected family does not declare member '{0}'.",
                name);
        }

        /// <summary>
        /// Resolves explicit overrides before inherited declarations and their declared object types.
        /// </summary>
        private List<BaseInstanceState> FindDeclarations(QualifiedName name)
        {
            var result = new List<BaseInstanceState>();
            var visited = new HashSet<NodeId>();
            foreach (BaseObjectState declaration in m_declarations)
            {
                Include(declaration);
            }
            return result;

            void Include(NodeState declaration)
            {
                if (!visited.Add(declaration.NodeId))
                {
                    return;
                }
                if (declaration.FindChild(m_device.Context, name) is BaseInstanceState child)
                {
                    result.Add(child);
                }
                NodeId typeId = declaration is BaseInstanceState instance
                    ? instance.TypeDefinitionId
                    : m_device.Context.TypeTable.FindSuperType(declaration.NodeId);
                if (!typeId.IsNull && m_device.Manager.FindPredefinedNode(typeId) is NodeState type)
                {
                    Include(type);
                }
            }
        }

        /// <summary>
        /// Reuses a selected mandatory member or materializes the requested optional declaration.
        /// </summary>
        private BaseInstanceState GetOrCreate(BaseInstanceState declaration, QualifiedName name)
        {
            BaseInstanceState? existing = Node.FindChild(m_device.Context, name);
            if (existing != null)
            {
                return existing;
            }
            BaseInstanceState created = Instantiate(m_device.Context, declaration, Node, name);
            m_device.DisableUnboundWrites(created);
            return created;
        }

        /// <summary>The owning device and provider pipeline.</summary>
        private readonly PadimDevice m_device;

        /// <summary>The generated declarations in override order, never mutated by configuration.</summary>
        private readonly ArrayOf<BaseObjectState> m_declarations;
    }
}
