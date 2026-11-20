/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// A detached interaction schema and the native type facts resolved in its
    /// owning document. JSON Pointers address schemas, not runtime values.
    /// </summary>
    public sealed class WotPayloadSchema
    {
        /// <summary>
        /// Initializes a new <c>WotPayloadSchema</c> instance.
        /// </summary>
        /// <param name="definition">The definition used to initialize the instance.</param>
        /// <param name="typeBindings">The type bindings used to initialize the instance.</param>
        /// <param name="diagnostics">The diagnostics used to initialize the instance.</param>
        internal WotPayloadSchema(
            JsonElement definition,
            ArrayOf<WotPayloadTypeBinding> typeBindings,
            ArrayOf<WotDiagnostic> diagnostics)
        {
            Definition = definition.Clone();
            TypeBindings = typeBindings;
            Diagnostics = diagnostics;
            foreach (WotPayloadTypeBinding binding in typeBindings)
            {
                m_bindings.Add(binding.JsonPointer, binding);
            }
        }

        /// <summary>
        /// Gets the complete authored interaction, including all payload schemas.
        /// </summary>
        public JsonElement Definition { get; }

        /// <summary>
        /// Gets the native DataType and rank at every captured schema location.
        /// </summary>
        public ArrayOf<WotPayloadTypeBinding> TypeBindings { get; }

        /// <summary>
        /// Gets resolution diagnostics. An unresolved binding is not permission to infer
        /// its declared type from the current runtime value.
        /// </summary>
        public ArrayOf<WotDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Finds the native type facts for a schema's relative JSON Pointer.
        /// </summary>
        /// <param name="jsonPointer">The JSON pointer.</param>
        /// <param name="binding">The WoT payload type binding.</param>
        public bool TryGetTypeBinding(
            string jsonPointer, [NotNullWhen(true)] out WotPayloadTypeBinding? binding)
        {
            return m_bindings.TryGetValue(jsonPointer, out binding);
        }

        private readonly Dictionary<string, WotPayloadTypeBinding> m_bindings = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The resolved native type of one DataSchema. Custom types retain their
    /// portable identity even when their encodeable factory is supplied only at activation.
    /// </summary>
    public sealed class WotPayloadTypeBinding
    {
        /// <summary>
        /// Initializes a new <c>WotPayloadTypeBinding</c> instance.
        /// </summary>
        /// <param name="jsonPointer">The JSON pointer used to initialize the instance.</param>
        /// <param name="dataTypeId">The data type ID used to initialize the instance.</param>
        /// <param name="typeInfo">The type info used to initialize the instance.</param>
        /// <param name="resolvedBrowseName">The resolved browse name used to initialize the instance.</param>
        internal WotPayloadTypeBinding(
            string jsonPointer, ExpandedNodeId dataTypeId, TypeInfo typeInfo, string? resolvedBrowseName)
        {
            JsonPointer = jsonPointer;
            DataTypeId = dataTypeId;
            TypeInfo = typeInfo;
            ResolvedBrowseName = resolvedBrowseName;
        }

        /// <summary>
        /// Gets the schema location relative to the captured interaction.
        /// </summary>
        public string JsonPointer { get; }

        /// <summary>
        /// Gets the portable declared DataType identity.
        /// </summary>
        public ExpandedNodeId DataTypeId { get; }

        /// <summary>
        /// Gets the resolved encoding type and ValueRank. A null built-in type
        /// with a non-null DataTypeId requires that type's registered factory.
        /// </summary>
        public TypeInfo TypeInfo { get; }

        /// <summary>
        /// Gets an authored BrowseName resolved in this schema's original scoped context.
        /// Null means the schema uses its member name.
        /// </summary>
        public string? ResolvedBrowseName { get; }
    }
}
