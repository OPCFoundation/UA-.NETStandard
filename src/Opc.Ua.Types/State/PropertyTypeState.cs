// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A base class for all property variable type nodes.
    /// </summary>
    public class PropertyTypeState : BaseVariableTypeState
    {
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public PropertyTypeState()
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new PropertyTypeState();
        }

        /// <inheritdoc/>
        protected override NodeState CreateCopy()
        {
            return new PropertyTypeState();
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SuperTypeId = NodeId.Create(
                VariableTypes.BaseVariableType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            NodeId = NodeId.Create(
                VariableTypes.PropertyType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            BrowseName = QualifiedName.Create(
                BrowseNames.PropertyType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            DisplayName = new LocalizedText(
                BrowseNames.PropertyType,
                string.Empty,
                BrowseNames.PropertyType);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
            Value = Variant.Null;
            DataType = NodeId.Create(
                DataTypes.BaseDataType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            ValueRank = ValueRanks.Any;
            ArrayDimensions = default;
        }
    }

    /// <summary>
    /// A typed base class for all property variable type nodes.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class PropertyTypeState<T> : PropertyTypeState
    {
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        protected PropertyTypeState()
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="PropertyTypeState{T}"/>
        /// class with a built-in value type and associated TBuilder to
        /// extract the value from the Variant and create a new Variant from it.
        /// </summary>
        /// <typeparam name="TBuilder">The builder to use for T</typeparam>
        public static PropertyTypeState<T> With<TBuilder>()
            where TBuilder : struct, IVariantBuilder<T>
        {
            return new Implementation<TBuilder>();
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
        public class Implementation<TBuilder> : PropertyTypeState<T>
            where TBuilder : struct, IVariantBuilder<T>
        {
            /// <inheritdoc/>
            public override T Value
            {
                get => m_builder.GetValue(WrappedValue);
                set => WrappedValue = m_builder.WithValue(value);
            }

            /// <inheritdoc/>
            protected override NodeState CreateCopy()
            {
                return new Implementation<TBuilder>();
            }

            private readonly TBuilder m_builder = new();
        }
    }
}
