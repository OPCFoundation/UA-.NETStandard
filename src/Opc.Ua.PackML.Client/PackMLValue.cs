// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PackML.Client
{
    /// <summary>
    /// A typed PackML value together with its unmodified quality and timestamps.
    /// </summary>
    /// <typeparam name="T">The field's decoded value type.</typeparam>
    public sealed class PackMLValue<T>
    {
        /// <summary>
        /// Creates a field result without substituting defaults for unavailable values.
        /// </summary>
        internal PackMLValue(NodeId nodeId, in DataValue dataValue, bool hasValue, T value)
        {
            NodeId = nodeId;
            DataValue = dataValue;
            HasValue = hasValue;
            m_value = value;
        }

        /// <summary>
        /// Gets the resolved field node, or a null NodeId when the field is absent.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// Gets whether the server exposes the field, independently of its current quality.
        /// </summary>
        public bool IsPresent => !NodeId.IsNull;

        /// <summary>
        /// Gets the original value, status, source/server timestamps and picoseconds.
        /// </summary>
        public DataValue DataValue { get; }

        /// <summary>
        /// Gets whether the value decodes as the requested type, independently of its quality.
        /// </summary>
        public bool HasValue { get; }

        /// <summary>
        /// Gets whether the value is present, decodable and has Good quality.
        /// </summary>
        public bool IsGood => IsPresent && HasValue && StatusCode.IsGood(DataValue.StatusCode);

        /// <summary>
        /// Gets the decoded value. Inspect quality before using it as equipment state.
        /// </summary>
        public T Value => HasValue ? m_value : throw new InvalidOperationException("The field has no typed value.");

        /// <summary>The decoded value, used only when HasValue is true.</summary>
        private readonly T m_value;
    }

    /// <summary>
    /// A typed relative field of a PackML unit.
    /// </summary>
    /// <typeparam name="T">The field's decoded value type.</typeparam>
    public sealed class PackMLField<T>
    {
        /// <summary>
        /// Creates a field descriptor.
        /// </summary>
        internal PackMLField(string path, Func<Variant, IServiceMessageContext, (bool Success, T Value)> decode)
        {
            Path = path;
            m_decode = decode;
        }

        /// <summary>
        /// Gets the relative browse path; a leading colon denotes a namespace-zero element.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Decodes a value while retaining all service metadata.
        /// </summary>
        internal PackMLValue<T> Decode(NodeId nodeId, in DataValue dataValue, IServiceMessageContext context)
        {
            (bool success, T value) = m_decode(dataValue.WrappedValue, context);
            return new PackMLValue<T>(nodeId, dataValue, !nodeId.IsNull && success, value);
        }

        /// <summary>The AOT-safe decoder for the field's declared type.</summary>
        private readonly Func<Variant, IServiceMessageContext, (bool Success, T Value)> m_decode;
    }

    /// <summary>
    /// A discovered PackML unit, preserving vendor-qualified names and its concrete type definition.
    /// </summary>
    /// <param name="NodeId">The unit instance.</param>
    /// <param name="BrowseName">The qualified instance browse name.</param>
    /// <param name="DisplayName">The localized display name.</param>
    /// <param name="TypeDefinition">The concrete type, including vendor subtypes.</param>
    public sealed record PackMLUnit(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinition);
}
