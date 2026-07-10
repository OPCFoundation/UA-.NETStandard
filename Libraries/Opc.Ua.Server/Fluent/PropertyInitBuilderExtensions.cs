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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Extension methods that set the <see cref="BaseVariableState.Value"/>
    /// attribute on a property child of the node currently focused by the
    /// builder. Use these to bulk-initialise nameplate / identification
    /// properties without scattering <see cref="NodeState"/> field
    /// assignments through user code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The extensions resolve the child by browse-name (case-sensitive,
    /// namespace-agnostic by default), then either:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>set the <c>Value</c> attribute on a
    ///     <see cref="BaseVariableState"/> (typically a
    ///     <see cref="PropertyState"/>), or</description></item>
    ///   <item><description>throw
    ///     <see cref="StatusCodes.BadNodeIdUnknown"/> when the child is
    ///     missing, or
    ///     <see cref="StatusCodes.BadTypeMismatch"/> when it is not a
    ///     variable.</description></item>
    /// </list>
    /// <para>
    /// Each typed overload uses a <see cref="Variant"/> constructor with
    /// a built-in implicit conversion — reflection-free and AOT-safe.
    /// </para>
    /// <para>
    /// Typical usage:
    /// <code>
    /// builder.Node("Pumps/Pump #1/Identification")
    ///        .WithProperty("Manufacturer", "SimPump Corp")
    ///        .WithProperty("SerialNumber", "SN-001")
    ///        .WithProperty("DeviceClass", "Pump");
    /// </code>
    /// </para>
    /// </remarks>
    public static class PropertyInitBuilderExtensions
    {
        /// <summary>
        /// Sets the <c>Value</c> attribute of the property child whose
        /// <see cref="NodeState.BrowseName"/> matches
        /// <paramref name="browseName"/> (any namespace), creating the
        /// property first when it does not yet exist.
        /// </summary>
        /// <remarks>
        /// When no child with the given browse name is present, a new
        /// read-only <see cref="PropertyState"/> is materialised under the
        /// current node (data type inferred from <paramref name="value"/>)
        /// and registered with the owning node manager. This makes the
        /// helper usable on freshly created nodes such as custom functional
        /// groups, not just on nodes whose properties come from a loaded
        /// model. Use <c>Writable()</c> to grant write access afterwards.
        /// </remarks>
        /// <param name="builder">The owning node builder.</param>
        /// <param name="browseName">
        /// Local browse name of the property child.
        /// </param>
        /// <param name="value">
        /// The new value as a <see cref="Variant"/>. Use the typed
        /// overloads below for built-in primitive types.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder,
            string browseName,
            Variant value)
        {
            ValidateArgs(builder, browseName);
            BaseVariableState? existing = TryFindVariableChild(
                builder.Node, browseName, builder.Builder.Context);
            if (existing != null)
            {
                existing.WrappedValue = value;
                return builder;
            }
            CreateProperty(
                builder,
                new QualifiedName(browseName, builder.Node.NodeId.NamespaceIndex),
                value);
            return builder;
        }

        /// <summary>
        /// Sets the <c>Value</c> attribute of the property child whose
        /// <see cref="NodeState.BrowseName"/> equals <paramref name="browseName"/>
        /// (qualified by namespace index), creating the property first when
        /// it does not yet exist.
        /// </summary>
        /// <remarks>
        /// See <see cref="WithProperty(INodeBuilder, string, Variant)"/> for
        /// the create-if-missing semantics.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder,
            QualifiedName browseName,
            Variant value)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentNullException(nameof(browseName));
            }
            NodeState? child = builder.Node.FindChild(builder.Builder.Context, browseName);
            if (child == null)
            {
                CreateProperty(builder, browseName, value);
                return builder;
            }
            if (child is not BaseVariableState variable)
            {
                throw NotVariable(browseName.ToString(), builder.Node.BrowseName, child.GetType().Name);
            }
            variable.WrappedValue = value;
            return builder;
        }

        /// <summary>
        /// Creates or updates the property <paramref name="browseName"/>
        /// with <paramref name="value"/>, then invokes
        /// <paramref name="configure"/> with a builder positioned on that
        /// property. Use this to adjust the new property inline — for
        /// example to grant write access:
        /// <code>
        /// node.WithProperty("LastError", Variant.From(""), p => p.Writable());
        /// </code>
        /// </summary>
        /// <param name="builder">The owning node builder.</param>
        /// <param name="browseName">Local browse name of the property.</param>
        /// <param name="value">The property value as a <see cref="Variant"/>.</param>
        /// <param name="configure">
        /// Callback invoked with a builder focused on the property.
        /// </param>
        /// <returns>
        /// The original (parent) builder, so further properties can be
        /// chained.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder,
            string browseName,
            Variant value,
            Action<INodeBuilder> configure)
        {
            ValidateArgs(builder, browseName);
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.WithProperty(browseName, value);
            configure(builder.Child(
                new QualifiedName(browseName, builder.Node.NodeId.NamespaceIndex)));
            return builder;
        }

        /// <summary>
        /// String value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, string value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// Boolean value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, bool value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// Signed byte value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, sbyte value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// Unsigned byte value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, byte value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// 16-bit signed integer value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, short value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// 16-bit unsigned integer value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, ushort value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// 32-bit signed integer value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, int value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// 32-bit unsigned integer value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, uint value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// 64-bit signed integer value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, long value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// 64-bit unsigned integer value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, ulong value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// Single-precision floating point value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, float value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// Double-precision floating point value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, double value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// DateTimeUtc value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, DateTimeUtc value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// NodeId value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, NodeId value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// LocalizedText value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, LocalizedText value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// QualifiedName value.
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder, string browseName, QualifiedName value)
        {
            return builder.WithProperty(browseName, Variant.From(value));
        }

        /// <summary>
        /// Typed-view variant of <see cref="WithProperty(INodeBuilder, string, Variant)"/>
        /// that preserves the typed builder return value.
        /// </summary>
        /// <typeparam name="TState">Concrete node state class.</typeparam>
        public static INodeBuilder<TState> WithProperty<TState>(
            this INodeBuilder<TState> builder,
            string browseName,
            Variant value)
            where TState : NodeState
        {
            ((INodeBuilder)builder).WithProperty(browseName, value);
            return builder;
        }

        private static void ValidateArgs(INodeBuilder builder, string browseName)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException(
                    "Browse name must not be null or empty.", nameof(browseName));
            }
        }

        private static BaseVariableState? TryFindVariableChild(
            NodeState parent,
            string browseName,
            ISystemContext context)
        {
            var found = new List<BaseInstanceState>();
            parent.GetChildren(context, found);
            foreach (BaseInstanceState child in found)
            {
                if (!string.Equals(child.BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (child is BaseVariableState bvs)
                {
                    return bvs;
                }
                throw NotVariable(browseName, parent.BrowseName, child.GetType().Name);
            }

            return null;
        }

        private static void CreateProperty(
            INodeBuilder builder,
            QualifiedName browseName,
            Variant value)
        {
            NodeState parent = builder.Node;
            ISystemContext context = builder.Builder.Context;
            string name = browseName.Name ?? string.Empty;

            NodeId dataTypeId = TypeInfo.GetDataTypeId(value, context.NamespaceUris);
            if (dataTypeId.IsNull)
            {
                dataTypeId = DataTypeIds.BaseDataType;
            }

            var property = new PropertyState(parent)
            {
                SymbolicName = name,
                BrowseName = browseName,
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ModellingRuleId = NodeId.Null,
                DataType = dataTypeId,
                ValueRank = value.TypeInfo.ValueRank,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate,
                Historizing = false,
                NodeId = new NodeId(
                    string.Concat(parent.NodeId.IdentifierAsString, "_", name),
                    parent.NodeId.NamespaceIndex)
            };

            property.WrappedValue = value;
            parent.AddChild(property);

            // Index the freshly created node so browse / NodeId lookup work
            // when the parent was already registered (e.g. a custom
            // functional group built during DI post-setup). When the owning
            // manager is not an AsyncCustomNodeManager (e.g. a unit-test
            // double) the node remains reachable via its parent.
            if (builder.Builder.NodeManager is AsyncCustomNodeManager manager)
            {
                manager.AddPredefinedNodeSynchronously(property);
            }
        }

        private static ServiceResultException NotVariable(
            string name, QualifiedName parent, string actualKind)
        {
            return ServiceResultException.Create(
                StatusCodes.BadTypeMismatch,
                "Child '{0}' on node '{1}' is '{2}', not a BaseVariableState.",
                name,
                parent,
                actualKind);
        }
    }
}
