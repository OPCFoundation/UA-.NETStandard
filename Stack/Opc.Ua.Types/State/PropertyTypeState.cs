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

            private readonly TBuilder m_builder = new();
        }
    }
}
