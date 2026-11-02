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
        internal PadimGroupBuilder(PadimDevice device, BaseObjectState node, BaseObjectState declaration)
        {
            m_device = device;
            Node = node;
            m_declaration = declaration;
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
            var builder = new PadimGroupBuilder(m_device, group, objectDeclaration);
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
            return m_declaration.FindChild(m_device.Context, name) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The selected family does not declare member '{0}'.",
                    name);
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

        /// <summary>The original generated declaration, never mutated by configuration.</summary>
        private readonly BaseObjectState m_declaration;
    }
}
