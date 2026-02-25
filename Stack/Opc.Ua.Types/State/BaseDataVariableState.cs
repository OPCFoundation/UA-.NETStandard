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
using System.Collections.Generic;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A typed base class for all data variable nodes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class BaseDataVariableState : BaseVariableState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public BaseDataVariableState(NodeState parent)
            : base(parent)
        {
            if (parent != null)
            {
                StatusCode = StatusCodes.BadWaitingForInitialData;
                ReferenceTypeId = ReferenceTypeIds.HasComponent;
            }
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new BaseDataVariableState(parent);
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SymbolicName = CoreUtils.Format("{0}_Instance1", BrowseNames.BaseDataVariableType);
            NodeId = default;
            BrowseName = new QualifiedName(SymbolicName, 1);
            DisplayName = new LocalizedText(SymbolicName);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            ReferenceTypeId = ReferenceTypeIds.HasComponent;
            TypeDefinitionId = GetDefaultTypeDefinitionId(context.NamespaceUris);
            NumericId = VariableTypes.BaseDataVariableType;
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
            return VariableTypeIds.BaseDataVariableType;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = (BaseDataVariableState)Activator.CreateInstance(GetType(), Parent);
            CopyTo(clone);
            return clone;
        }

        /// <inheritdoc/>
        public override bool DeepEquals(NodeState node)
        {
            if (node is not BaseDataVariableState state)
            {
                return false;
            }
            return
                base.DeepEquals(state) &&
                state.EnumStrings == EnumStrings;
        }

        /// <inheritdoc/>
        public override int DeepGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.DeepGetHashCode());
            hash.Add(EnumStrings);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        protected override void CopyTo(NodeState target)
        {
            if (target is BaseDataVariableState state)
            {
                state.EnumStrings = EnumStrings;
            }
            base.CopyTo(target);
        }

        /// <summary>
        /// The strings that describe the values for an enumeration.
        /// </summary>
        public PropertyState<ArrayOf<LocalizedText>> EnumStrings
        {
            get => m_enumStrings;
            set
            {
                if (!ReferenceEquals(m_enumStrings, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_enumStrings = value;
            }
        }

        /// <summary>
        /// Populates a list with the children that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="children">The list of children to populate.</param>
        public override void GetChildren(ISystemContext context, IList<BaseInstanceState> children)
        {
            if (m_enumStrings != null)
            {
                children.Add(m_enumStrings);
            }

            base.GetChildren(context, children);
        }

        /// <summary>
        /// Finds the child with the specified browse name.
        /// </summary>
        protected override BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (browseName.IsNull)
            {
                return null;
            }

            BaseInstanceState instance = null;
            switch (browseName.Name)
            {
                case BrowseNames.EnumStrings:
                    instance = !createOrReplace ?
                        EnumStrings : CreateOrReplaceEnumStrings(context, replacement);
                    break;
            }
            return instance ?? base.FindChild(context, browseName, createOrReplace, replacement);
        }

        /// <summary>
        /// Create or replace enum strings
        /// </summary>
        public PropertyState<ArrayOf<LocalizedText>> CreateOrReplaceEnumStrings(
            ISystemContext context,
            BaseInstanceState replacement)
        {
            if (EnumStrings == null)
            {
                if (replacement is not PropertyState<ArrayOf<LocalizedText>> child)
                {
                    child = PropertyState<ArrayOf<LocalizedText>>.With<VariantBuilder>(this);
                    if (replacement != null)
                    {
                        child.Create(context, replacement);
                    }
                }
                EnumStrings = child;
            }
            return EnumStrings;
        }

        private PropertyState<ArrayOf<LocalizedText>> m_enumStrings;
    }

    /// <summary>
    /// A typed base class for all data variable nodes.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public abstract class BaseDataVariableState<T> : BaseDataVariableState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        protected BaseDataVariableState(NodeState parent)
            : base(parent)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="BaseDataVariableState{T}"/>
        /// class with a built-in value type and associated TBuilder to
        /// extract the value from the Variant and create a new Variant from it.
        /// </summary>
        /// <typeparam name="TBuilder">The builder to use for T</typeparam>
        public static BaseDataVariableState<T> With<TBuilder>(NodeState parent)
            where TBuilder : struct, IVariantBuilder<T>
        {
            return new Implementation<TBuilder>(parent);
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        /// <param name="context">An object that describes how access the system containing the data. </param>
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
        public class Implementation<TBuilder> : BaseDataVariableState<T>
            where TBuilder : struct, IVariantBuilder<T>
        {
            /// <inheritdoc/>
            public Implementation(NodeState parent)
                : base(parent)
            {
                Value = default;
            }

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
