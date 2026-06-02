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
        // ── Variant overload — universal escape hatch ────────────────

        /// <summary>
        /// Sets the <c>Value</c> attribute of the property child whose
        /// <see cref="NodeState.BrowseName"/> matches
        /// <paramref name="browseName"/> (any namespace).
        /// </summary>
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
            BaseVariableState property = ResolveVariableChildByName(
                builder.Node, browseName, builder.Builder.Context);
            property.WrappedValue = value;
            return builder;
        }

        /// <summary>
        /// Sets the <c>Value</c> attribute of the property child whose
        /// <see cref="NodeState.BrowseName"/> equals <paramref name="browseName"/>
        /// (qualified by namespace index).
        /// </summary>
        public static INodeBuilder WithProperty(
            this INodeBuilder builder,
            QualifiedName browseName,
            Variant value)
        {
            if (builder == null) { throw new ArgumentNullException(nameof(builder)); }
            if (browseName.IsNull)
            {
                throw new ArgumentNullException(nameof(browseName));
            }

            NodeState? child = builder.Node.FindChild(builder.Builder.Context, browseName);
            if (child == null)
            {
                throw NotFound(browseName.ToString(), builder.Node.BrowseName);
            }
            if (child is not BaseVariableState variable)
            {
                throw NotVariable(browseName.ToString(), builder.Node.BrowseName, child.GetType().Name);
            }
            variable.WrappedValue = value;
            return builder;
        }

        // ── Typed overloads — pick the right Variant.From by overload ──

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

        // ── Helpers ──────────────────────────────────────────────────

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

        private static BaseVariableState ResolveVariableChildByName(
            NodeState parent,
            string browseName,
            ISystemContext context)
        {
            var found = new List<BaseInstanceState>();
            parent.GetChildren(context, found);
            BaseVariableState? variable = null;
            string? mismatchKind = null;
            foreach (BaseInstanceState child in found)
            {
                if (!string.Equals(child.BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (child is BaseVariableState bvs)
                {
                    variable = bvs;
                    break;
                }
                mismatchKind ??= child.GetType().Name;
            }

            if (variable != null)
            {
                return variable;
            }

            if (mismatchKind != null)
            {
                throw NotVariable(browseName, parent.BrowseName, mismatchKind);
            }
            throw NotFound(browseName, parent.BrowseName);
        }

        private static ServiceResultException NotFound(string name, QualifiedName parent)
        {
            return ServiceResultException.Create(
                StatusCodes.BadNodeIdUnknown,
                "Property '{0}' was not found on node '{1}'.",
                name,
                parent);
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

