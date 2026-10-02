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
using System.Globalization;
using UaBrowseNames = Opc.Ua.BrowseNames;

namespace Opc.Ua.Pumps.Server.Builders
{
    /// <summary>
    /// The one group builder used for every OPC 40223 group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Children are materialised through
    /// <see cref="NodeState.CreateChild(ISystemContext, QualifiedName, bool)"/>,
    /// which the generated state classes implement for every optional child
    /// the model declares. That is what lets one builder cover all ~3000 of
    /// them instead of needing a method per child.
    /// </para>
    /// <para>
    /// That generic path creates a child of the right CLR type and browse
    /// name but does not stamp the modelling metadata: the child comes back
    /// with no reference type, no type definition and <c>BaseDataType</c> as
    /// its data type. Such a node can be read by NodeId and looks fine from
    /// inside the server, yet it is invisible to a client, because a Browse
    /// filtered on <c>HierarchicalReferences</c> - which is every browse a
    /// client makes - matches on the reference type the child does not have.
    /// Each new child is therefore completed from its instance declaration on
    /// the type before it is registered.
    /// </para>
    /// </remarks>
    internal sealed class PumpGroupBuilder : IPumpGroupBuilder
    {
        private readonly ISystemContext m_context;
        private readonly PumpNamespaceIndices m_namespaces;
        private readonly Action<NodeState>? m_register;
        private readonly Func<NodeId, NodeState?>? m_findNode;
        private readonly NodeState? m_declaration;
        private const string kVibrationPlaceholder = "<Vibration>";

        internal PumpGroupBuilder(
            ISystemContext context,
            NodeState node,
            PumpNamespaceIndices namespaces,
            Action<NodeState>? register,
            Func<NodeId, NodeState?>? findNode,
            NodeState? declaration = null)
        {
            m_context = context;
            Node = node;
            m_namespaces = namespaces;
            m_register = register;
            m_findNode = findNode;
            m_declaration = declaration ?? ResolveDeclaration(node, findNode);
        }

        /// <inheritdoc/>
        public NodeState Node { get; }

        /// <inheritdoc/>
        public IPumpGroupBuilder Add(params string[] browseNames)
        {
            if (browseNames is null)
            {
                throw new ArgumentNullException(nameof(browseNames));
            }
            foreach (string browseName in browseNames)
            {
                Require(browseName);
            }
            return this;
        }

        /// <inheritdoc/>
        public IPumpGroupBuilder Set(string browseName, Variant value)
        {
            BaseInstanceState child = Require(browseName);
            if (child is not BaseVariableState variable)
            {
                throw new ArgumentException(
                    Invariant($"'{browseName}' is not a variable of '{Describe()}'."),
                    nameof(browseName));
            }
            variable.WrappedValue = value;
            Touch(variable);
            return this;
        }

        /// <inheritdoc/>
        public IPumpGroupBuilder SetAnalog(
            string browseName,
            double value,
            EUInformation? engineeringUnits = null,
            Range? euRange = null)
        {
            BaseInstanceState child = Require(browseName);
            if (child is not BaseVariableState variable)
            {
                throw new ArgumentException(
                    Invariant($"'{browseName}' is not a variable of '{Describe()}'."),
                    nameof(browseName));
            }
            variable.WrappedValue = Variant.From(value);
            Touch(variable);

            if (engineeringUnits != null)
            {
                SetMetadata(variable, browseName, UaBrowseNames.EngineeringUnits, engineeringUnits);
            }
            if (euRange != null)
            {
                SetMetadata(variable, browseName, UaBrowseNames.EURange, euRange);
            }
            return this;
        }

        /// <inheritdoc/>
        public IPumpGroupBuilder SetDiscrete(string browseName, bool value)
        {
            BaseInstanceState child = Require(browseName);

            // A TwoStateDiscreteType variable carries the state itself.
            if (child is BaseVariableState variable)
            {
                variable.WrappedValue = Variant.From(value);
                Touch(variable);
                return this;
            }

            // A DiscreteInput/DiscreteOutputObjectType carries it one level
            // down. Which of the two children exists is decided by the
            // object's type, so both are tried.
            NodeState? childDeclaration = DeclarationOf(browseName);
            BaseInstanceState? state =
                MaterialiseChild(child, childDeclaration, BrowseNames.DiscreteInputValue) ??
                MaterialiseChild(child, childDeclaration, BrowseNames.DiscreteOutputValue);

            if (state is not BaseVariableState discrete)
            {
                throw new ArgumentException(
                    Invariant(
                        $"'{browseName}' of '{Describe()}' carries no discrete value."),
                    nameof(browseName));
            }
            discrete.WrappedValue = Variant.From(value);
            Touch(discrete);
            return this;
        }

        /// <inheritdoc/>
        public IPumpGroupBuilder With(string browseName, Action<BaseInstanceState> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(Require(browseName));
            return this;
        }

        /// <inheritdoc/>
        public BaseInstanceState? Child(string browseName)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException(
                    "A browse name is required.",
                    nameof(browseName));
            }
            return MaterialiseChild(Node, m_declaration, browseName);
        }

        /// <inheritdoc/>
        public IPumpGroupBuilder Nested(string browseName)
        {
            BaseInstanceState child = Require(browseName);
            if (child is BaseVariableState)
            {
                throw new ArgumentException(
                    Invariant($"'{browseName}' of '{Describe()}' is not a group."),
                    nameof(browseName));
            }
            return new PumpGroupBuilder(
                m_context,
                child,
                m_namespaces,
                m_register,
                m_findNode,
                DeclarationOf(browseName));
        }

        /// <inheritdoc/>
        public IPumpGroupBuilder AddVibration(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A vibration measurement name is required.", nameof(name));
            }
            if (PlaceholderDeclaration(kVibrationPlaceholder) == null)
            {
                throw new ArgumentException(
                    Invariant($"'{Describe()}' declares no {kVibrationPlaceholder} placeholder; vibration measurements belong to a Measurements group."),
                    nameof(name));
            }

            var browseName = new QualifiedName(name, m_namespaces.Pumps);
            if (Node.FindChild(m_context, browseName) is BaseInstanceState existing)
            {
                return new PumpGroupBuilder(m_context, existing, m_namespaces, m_register, m_findNode);
            }

            // <Vibration> is an OptionalPlaceholder (OPC 40223 §7.32): each
            // instance is created from VibrationMeasurementType and named by
            // the application, the way AddPort does it for PortsGroupType.
            BaseInstanceState vibration = m_context.CreateInstanceOfVibrationMeasurementType(Node, browseName);
            vibration.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            Node.AddChild(vibration);
            m_register?.Invoke(vibration);
            return new PumpGroupBuilder(m_context, vibration, m_namespaces, m_register, m_findNode);
        }

        /// <summary>
        /// Materialises a child of <paramref name="parent"/>, completes it
        /// from <paramref name="declaration"/> and registers it.
        /// </summary>
        /// <remarks>
        /// OPC 40223 children are spread over four namespaces - the nameplate
        /// alone spans three - so the browse name is tried in each in turn. A
        /// miss creates nothing, so probing is free of side effects.
        /// </remarks>
        private BaseInstanceState? MaterialiseChild(
            NodeState parent,
            NodeState? declaration,
            string browseName)
        {
            foreach (ushort namespaceIndex in m_namespaces.SearchOrder)
            {
                var qualifiedName = new QualifiedName(browseName, namespaceIndex);

                // An already-materialised child is returned as it is. Going
                // straight to CreateChild would work too, but every call would
                // then re-complete and re-register the subtree, which turns
                // filling a group of 78 variables into 78 walks of it.
                BaseInstanceState? existing = parent.FindChild(m_context, qualifiedName);
                if (existing != null)
                {
                    return existing;
                }

                BaseInstanceState? child = parent.CreateChild(m_context, qualifiedName);
                if (child != null)
                {
                    CompleteFromDeclaration(child, FindDeclarationChild(declaration, child));

                    // A child materialised after the pump was registered is
                    // reachable through its parent but unknown to the node
                    // manager, so a client could browse to it and then fail to
                    // read it. Registering it here closes that gap.
                    m_register?.Invoke(child);
                    return child;
                }
            }
            return null;
        }

        /// <summary>
        /// Copies the modelling metadata the generic create path leaves
        /// unset from the type's instance declaration.
        /// </summary>
        /// <remarks>
        /// Only unset members are filled, so a child the model did create
        /// completely is never disturbed. Without a declaration to copy from
        /// the child is left as it is: a half-guessed data type would be
        /// worse than an honest <c>BaseDataType</c>.
        /// </remarks>
        private static void CompleteFromDeclaration(
            BaseInstanceState child,
            BaseInstanceState? source)
        {
            if (source == null)
            {
                return;
            }

            if (child.ReferenceTypeId.IsNull)
            {
                child.ReferenceTypeId = source.ReferenceTypeId;
            }
            if (child.TypeDefinitionId.IsNull)
            {
                child.TypeDefinitionId = source.TypeDefinitionId;
            }
            if (child.Description.IsNullOrEmpty)
            {
                child.Description = source.Description;
            }

            if (child is not BaseVariableState variable ||
                source is not BaseVariableState declared)
            {
                return;
            }

            // BaseDataType with an Any value rank is what the generic path
            // leaves behind, and it is what a client sees as "this server
            // does not know what this variable is".
            if (variable.DataType.IsNull || variable.DataType == Opc.Ua.DataTypeIds.BaseDataType)
            {
                variable.DataType = declared.DataType;
            }
            if (variable.ValueRank == ValueRanks.Any)
            {
                variable.ValueRank = declared.ValueRank;
                variable.ArrayDimensions = declared.ArrayDimensions;
            }
            if (declared.AccessLevel != AccessLevels.None)
            {
                variable.AccessLevel = declared.AccessLevel;
                variable.UserAccessLevel = declared.UserAccessLevel;
            }
            variable.MinimumSamplingInterval = declared.MinimumSamplingInterval;
        }

        /// <summary>
        /// Resolves the node whose children are the instance declarations of
        /// <paramref name="node"/>'s children - the node's type definition.
        /// </summary>
        private static NodeState? ResolveDeclaration(
            NodeState node,
            Func<NodeId, NodeState?>? findNode)
        {
            if (findNode == null ||
                node is not BaseInstanceState instance ||
                instance.TypeDefinitionId.IsNull)
            {
                return null;
            }
            return findNode(instance.TypeDefinitionId);
        }

        /// <summary>
        /// Finds the instance declaration of an already-materialised child.
        /// </summary>
        private BaseInstanceState? FindDeclarationChild(
            NodeState? declaration,
            BaseInstanceState child)
        {
            return declaration?.FindChild(m_context, child.BrowseName);
        }

        /// <summary>
        /// Finds the instance declaration of a child by browse name, trying
        /// each namespace the OPC 40223 model spans.
        /// </summary>
        private BaseInstanceState? DeclarationOf(string browseName)
        {
            if (m_declaration == null)
            {
                return null;
            }
            foreach (ushort namespaceIndex in m_namespaces.SearchOrder)
            {
                BaseInstanceState? declaration = m_declaration.FindChild(
                    m_context,
                    new QualifiedName(browseName, namespaceIndex));
                if (declaration != null)
                {
                    return declaration;
                }
            }
            return null;
        }

        private BaseInstanceState Require(string browseName)
        {
            BaseInstanceState? child = Child(browseName);
            if (child != null)
            {
                return child;
            }
            if (PlaceholderDeclaration("<" + browseName + ">") != null)
            {
                // A placeholder is a template for instances the application
                // names, not a child that can be materialised by its name.
                throw new ArgumentException(
                    Invariant(
                        $"'{Describe()}' declares '<{browseName}>' as a placeholder, not as a child - create named instances with {PlaceholderMethod(browseName)}."),
                    nameof(browseName));
            }
            throw new ArgumentException(
                Invariant(
                    $"'{Describe()}' declares no child named '{browseName}' - use a generated Opc.Ua.Pumps.BrowseNames constant."),
                nameof(browseName));
        }

        /// <summary>
        /// Finds a placeholder the group's type declares. Placeholders are not
        /// copied into instance declarations, so the group's own type
        /// definition is searched as well - <c>&lt;Vibration&gt;</c> lives on
        /// <c>MeasurementsType</c>, not on the <c>Measurements</c> instance
        /// declaration below <c>OperationalGroupType</c>.
        /// </summary>
        private BaseInstanceState? PlaceholderDeclaration(string placeholder)
        {
            if (DeclarationOf(placeholder) is BaseInstanceState declared)
            {
                return declared;
            }
            if (m_findNode == null ||
                Node is not BaseInstanceState instance ||
                instance.TypeDefinitionId.IsNull ||
                m_findNode(instance.TypeDefinitionId) is not NodeState type)
            {
                return null;
            }
            foreach (ushort namespaceIndex in m_namespaces.SearchOrder)
            {
                BaseInstanceState? child = type.FindChild(
                    m_context,
                    new QualifiedName(placeholder, namespaceIndex));
                if (child != null)
                {
                    return child;
                }
            }
            return null;
        }

        private static string PlaceholderMethod(string placeholder)
        {
            return placeholder switch
            {
                "Vibration" => "AddVibration(name)",
                "Drive" or "InletConnection" or "OutletConnection" => "IPumpBuilder.AddPort(kind, name)",
                _ => "a node manager that instantiates the placeholder's type"
            };
        }

        /// <summary>
        /// Attaches a metadata property - <c>EngineeringUnits</c> or
        /// <c>EURange</c> - when the variable's type declares one.
        /// </summary>
        private void SetMetadata(
            BaseVariableState variable,
            string variableBrowseName,
            string browseName,
            IEncodeable value)
        {
            if (MaterialiseChild(variable, DeclarationOf(variableBrowseName), browseName)
                is BaseVariableState property)
            {
                property.WrappedValue = Variant.From(new ExtensionObject(value));
                Touch(property);
            }
        }

        /// <summary>
        /// Stamps a written value with a current timestamp and a good status,
        /// so it does not read back as never-written, and reports the change
        /// to monitored items.
        /// </summary>
        /// <remarks>
        /// The node manager reports data changes by exception, so a value
        /// written after a client subscribed reaches the client only once the
        /// change is published. Doing it here means an application that keeps
        /// its pumps current through the builder never has to.
        /// </remarks>
        private void Touch(BaseVariableState variable)
        {
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(m_context, includeChildren: false);
        }

        private string Describe()
        {
            return Node.BrowseName.Name ?? Node.NodeId.ToString();
        }

        private static string Invariant(FormattableString text)
        {
            return text.ToString(CultureInfo.InvariantCulture);
        }
    }
}
