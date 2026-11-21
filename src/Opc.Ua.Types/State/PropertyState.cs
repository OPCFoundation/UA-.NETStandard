// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A typed base class for all data variable nodes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class PropertyState : BaseVariableState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public PropertyState(NodeState? parent)
            : base(parent)
        {
            StatusCode = StatusCodes.BadWaitingForInitialData;
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new PropertyState(parent);
        }

        /// <inheritdoc/>
        protected override NodeState CreateCopy()
        {
            return new PropertyState(Parent);
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SymbolicName = CoreUtils.Format("{0}_Instance1", BrowseNames.PropertyType);
            NodeId = default;
            BrowseName = new QualifiedName(SymbolicName, 1);
            DisplayName = new LocalizedText(SymbolicName);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            ReferenceTypeId = ReferenceTypeIds.HasProperty;
            TypeDefinitionId = GetDefaultTypeDefinitionId(context.NamespaceUris);
            NumericId = VariableTypes.PropertyType;
            Value = Variant.Null;
            DataType = GetDefaultDataTypeId(context.NamespaceUris);
            ValueRank = GetDefaultValueRank();
            ArrayDimensions = default;
            AccessLevel = AccessLevels.CurrentReadOrWrite;
            UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            MinimumSamplingInterval = MinimumSamplingIntervals.Continuous;
            Historizing = false;
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return VariableTypeIds.PropertyType;
        }
    }

    /// <summary>
    /// A typed base class for all data variable nodes.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public abstract class PropertyState<T> : PropertyState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        protected PropertyState(NodeState? parent)
            : base(parent)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="PropertyState{T}"/>
        /// class with a built-in value type and associated TBuilder to
        /// extract the value from the Variant and create a new Variant from it.
        /// </summary>
        /// <typeparam name="TBuilder">The builder to use for T</typeparam>
        public static PropertyState<T> With<TBuilder>(NodeState parent)
            where TBuilder : struct, IVariantBuilder<T>
        {
            return new Implementation<TBuilder>(parent);
        }

        /// <summary>
        /// Add property with value
        /// </summary>
        /// <typeparam name="TBuilder"></typeparam>
        public static PropertyState<T> With<TBuilder>(NodeState parent, T value)
            where TBuilder : struct, IVariantBuilder<T>
        {
            return new Implementation<TBuilder>(parent) { Value = value };
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            base.Initialize<T>(context);
        }

        /// <inheritdoc/>
        public new abstract T Value { get; set; }

        /// <summary>
        /// Adds builder which extracts T from Variant or creates new
        /// Variant with type T. This is public so it can be overridden
        /// by classes outside of the namespace.
        /// </summary>
        /// <typeparam name="TBuilder">The builder to use for T</typeparam>
        public class Implementation<TBuilder> : PropertyState<T>
            where TBuilder : struct, IVariantBuilder<T>
        {
            /// <inheritdoc/>
            public Implementation(NodeState? parent)
                : base(parent)
            {
                Value = default!;
            }

            /// <inheritdoc/>
            public override T Value
            {
                get => m_builder.GetValue(WrappedValue);
                set => WrappedValue = m_builder.WithValue(value);
            }

            /// <inheritdoc/>
            protected override NodeState CreateCopy()
            {
                return new Implementation<TBuilder>(Parent);
            }

            private readonly TBuilder m_builder = new();
        }
    }
}
