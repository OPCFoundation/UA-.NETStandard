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
