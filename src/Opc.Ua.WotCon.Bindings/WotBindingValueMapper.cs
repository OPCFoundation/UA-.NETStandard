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
using System.Text;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Translates namespace-bearing values using the stack's URI-based JSON
    /// codec. The codec preserves arrays, matrices, optional fields and union
    /// switches; no reflection or boxed value access is required.
    /// </summary>
    public static class WotBindingValueMapper
    {
        /// <summary>
        /// Resolves a selected field's portable BrowseName without modifying the namespace table.
        /// </summary>
        public static QualifiedName ResolveBrowseName(string element, NamespaceTable namespaceUris)
        {
            if (element is null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            string? namespaceUri = null;
            string name = element;
            if (element.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int separator = element.IndexOf(';', 4);
                if (separator <= 4 || separator + 1 >= element.Length)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadBrowseNameInvalid,
                        $"The event select-clause path element '{element}' is not a valid " +
                        "NamespaceUri-qualified browse name.");
                }
                namespaceUri = CoreUtils.UnescapeUri(element.AsSpan(4, separator - 4));
                name = element.Substring(separator + 1);
            }
            else if (element.Length > 0 && element[0] == '{')
            {
                int separator = element.IndexOf('}', 1);
                if (separator <= 1 || separator + 1 >= element.Length)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadBrowseNameInvalid,
                        $"The event select-clause path element '{element}' is not a valid " +
                        "NamespaceUri-qualified browse name.");
                }
                namespaceUri = element.Substring(1, separator - 1);
                name = element.Substring(separator + 1);
            }
            if (namespaceUri is null)
            {
                return QualifiedName.From(name);
            }
            int index = namespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadBrowseNameInvalid,
                    $"The event select clause names the namespace '{namespaceUri}', which the " +
                    "Server's namespace table does not hold.");
            }
            return new QualifiedName(name, checked((ushort)index));
        }

        /// <summary>
        /// Gets whether a value can contain namespace or server indexes.
        /// </summary>
        public static bool RequiresContext(in Variant value)
        {
            return !value.IsNull && value.TypeInfo.BuiltInType is
                BuiltInType.NodeId or BuiltInType.ExpandedNodeId or BuiltInType.QualifiedName or
                BuiltInType.ExtensionObject or BuiltInType.DataValue or BuiltInType.Variant;
        }

        /// <summary>
        /// Translates a value into the target context. Remote namespace tables
        /// are never extended: an unknown namespace requiring a target-local
        /// index fails explicitly; absolute ExpandedNodeIds can remain portable.
        /// Local projection contexts may opt into adding received namespaces.
        /// </summary>
        public static Variant Translate(
            in Variant value,
            IServiceMessageContext source,
            IServiceMessageContext target,
            bool allowNamespaceGrowth = false)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (!RequiresContext(value))
            {
                return value;
            }
            ValidateReferences(value, source, target, allowNamespaceGrowth, 0);
            using var encoder = new JsonEncoder(source, s_encoding);
            encoder.WriteVariant("Value", value);
            string encoded = encoder.CloseAndReturnText();
            if (source.MaxMessageSize > 0 && Encoding.UTF8.GetByteCount(encoded) > source.MaxMessageSize)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            using var decoder = new JsonDecoder(encoded, target, new JsonDecoderOptions
            {
                ParseStrict = true,
                UpdateNamespaceTable = false
            });
            return decoder.ReadVariant("Value");
        }

        private static void ValidateReferences(
            in Variant value,
            IServiceMessageContext context,
            IServiceMessageContext target,
            bool allowGrowth,
            int depth)
        {
            if (!RequiresContext(value))
            {
                return;
            }
            if (depth >= context.MaxEncodingNestingLevels)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            if (value.TypeInfo.ValueRank != ValueRanks.Scalar)
            {
                foreach (Variant item in Elements(value))
                {
                    ValidateReferences(item, context, target, allowGrowth, depth + 1);
                }
                return;
            }
            if (value.TryGetValue(out NodeId node))
            {
                ValidateIndex(node.NamespaceIndex, context.NamespaceUris);
                RequireNamespace(node.NamespaceIndex, context.NamespaceUris, target.NamespaceUris, allowGrowth);
            }
            else if (value.TryGetValue(out QualifiedName name))
            {
                ValidateIndex(name.NamespaceIndex, context.NamespaceUris);
                RequireNamespace(name.NamespaceIndex, context.NamespaceUris, target.NamespaceUris, allowGrowth);
            }
            else if (value.TryGetValue(out ExpandedNodeId expanded))
            {
                ValidateExpanded(expanded, context, target, allowGrowth, encodingId: false);
            }
            else if (value.TryGetValue(out DataValue data))
            {
                ValidateReferences(data.WrappedValue, context, target, allowGrowth, depth + 1);
            }
            else if (value.TryGetValue(out ExtensionObject extension))
            {
                ValidateExpanded(extension.TypeId, context, target, allowGrowth, encodingId: true);
                if (extension.Encoding == ExtensionObjectEncoding.None)
                {
                    return;
                }
                if (extension.Encoding != ExtensionObjectEncoding.EncodeableObject ||
                    !extension.TryGetValue(out IEncodeable? body, context) || body is not IStructure structure)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "Namespace translation requires decoded structured values, not opaque ExtensionObjects.");
                }
                foreach (IStructureField field in structure.GetFields())
                {
                    string? fieldName = field.Name;
                    if (string.IsNullOrEmpty(fieldName))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "A structured invocation value contains an unnamed field.");
                    }
                    ValidateReferences(structure[fieldName], context, target, allowGrowth, depth + 1);
                }
            }
        }

        private static void ValidateExpanded(
            ExpandedNodeId value,
            IServiceMessageContext context,
            IServiceMessageContext target,
            bool allowGrowth,
            bool encodingId)
        {
            if (string.IsNullOrEmpty(value.NamespaceUri))
            {
                ValidateIndex(value.NamespaceIndex, context.NamespaceUris);
                if (encodingId)
                {
                    RequireNamespace(value.NamespaceIndex, context.NamespaceUris, target.NamespaceUris, allowGrowth);
                }
            }
            else if (encodingId)
            {
                RequireUri(value.NamespaceUri!, target.NamespaceUris, allowGrowth);
            }
            ValidateIndex(value.ServerIndex, context.ServerUris);
            if (value.ServerIndex != 0)
            {
                RequireUri(context.ServerUris.GetString(value.ServerIndex)!, target.ServerUris, allowGrowth);
            }
        }

        private static void RequireNamespace(
            ushort index, NamespaceTable source, NamespaceTable target, bool allowGrowth)
        {
            if (index != 0)
            {
                RequireUri(source.GetString(index)!, target, allowGrowth);
            }
        }

        private static void RequireUri(string uri, StringTable target, bool allowGrowth)
        {
            if (target.GetIndex(uri) >= 0)
            {
                return;
            }
            if (!allowGrowth)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "An invocation value names a namespace absent from its target.");
            }
            target.GetIndexOrAppend(uri);
        }

        private static void ValidateIndex(uint index, StringTable table)
        {
            if (index != 0 && (index >= table.Count || string.IsNullOrEmpty(table.GetString(index))))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "An invocation value has no source namespace or server mapping.");
            }
        }

        private static ArrayOf<Variant> Elements(in Variant value)
        {
            if (value.TryGetValue(out ArrayOf<NodeId> nodes))
            {
                return nodes.ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out ArrayOf<ExpandedNodeId> expanded))
            {
                return expanded.ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out ArrayOf<QualifiedName> names))
            {
                return names.ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out ArrayOf<DataValue> data))
            {
                return data.ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out ArrayOf<ExtensionObject> extensions))
            {
                return extensions.ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out ArrayOf<Variant> variants))
            {
                return variants;
            }
            if (value.TryGetValue(out MatrixOf<NodeId> nodeMatrix))
            {
                return nodeMatrix.ToArrayOf(out _).ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out MatrixOf<ExpandedNodeId> expandedMatrix))
            {
                return expandedMatrix.ToArrayOf(out _).ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out MatrixOf<QualifiedName> nameMatrix))
            {
                return nameMatrix.ToArrayOf(out _).ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out MatrixOf<DataValue> dataMatrix))
            {
                return dataMatrix.ToArrayOf(out _).ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out MatrixOf<ExtensionObject> extensionMatrix))
            {
                return extensionMatrix.ToArrayOf(out _).ConvertAll(static item => new Variant(item));
            }
            if (value.TryGetValue(out MatrixOf<Variant> variantMatrix))
            {
                return variantMatrix.ToArrayOf(out _);
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
        }

        private static readonly JsonEncoderOptions s_encoding = JsonEncoderOptions.Verbose with
        {
            IgnoreUnionSwitchField = false,
            IgnoreOptionalFieldEncodingMask = false,
            EnumerationAsNumber = true
        };
    }
}
