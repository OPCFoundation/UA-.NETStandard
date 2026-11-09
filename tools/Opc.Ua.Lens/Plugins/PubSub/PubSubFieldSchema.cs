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
using System.IO;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using UaLens.StructuredValues;

namespace UaLens.Plugins.PubSub
{
    /// <summary>
    /// Shared bounded field-shape rules for commissioning, codecs and mapped values.
    /// </summary>
    internal static class PubSubFieldSchema
    {
        /// <summary>
        /// Creates an isolated bounded codec context without changing a borrowed session's quotas.
        /// </summary>
        public static ServiceMessageContext CreateContext(ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            var context = ServiceMessageContext.Create(telemetry);
            context.MaxArrayLength = MaximumElements;
            context.MaxEncodingNestingLevels = 16;
            context.MaxMessageSize = MaximumValueBytes;
            context.MaxByteStringLength = MaximumValueBytes;
            context.MaxStringLength = PubSubConfigurationValidation.MaxValueCharacters;
            return context;
        }

        /// <summary>
        /// Validates a mapped value and returns detached storage before a server writer can see it.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static Variant ValidateAndSnapshot(
            PubSubFieldConfiguration field, Variant value, IServiceMessageContext? context)
        {
            bool nullCollection = field.ValueRank > 0 && !value.TypeInfo.IsScalar && value.ValueIsDefaultOrNull;
            if (value.IsNull ||
                value.TypeInfo.BuiltInType != field.Type ||
                !IsValidShape(field.ValueRank, field.ArrayDimensions) ||
                (!nullCollection &&
                    value.TypeInfo.ValueRank != field.ValueRank &&
                    !(field.ValueRank > 1 && value.TypeInfo.IsMatrix)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The value does not match its field schema.");
            }
            if (nullCollection)
            {
                if (context is null)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "A typed null array requires its declared encoding context.");
                }
                ArrayOf<int> dimensions = field.ValueRank > 1 ? new int[field.ValueRank] : ArrayOf<int>.Empty;
                return new StructuredArrayValue(field.Type, [],
                    dimensions, isNull: true).ToVariant(context);
            }
            if (field.Type == BuiltInType.ExtensionObject)
            {
                if (context is null)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "Structures require their reviewed encoding context.");
                }
                Variant capturedStructure = Snapshot(value, context);
                NodeId expectedType = PubSubStructureSchema.FieldType(field, context.NamespaceUris);
                if (field.ValueRank == ValueRanks.Scalar)
                {
                    RequireStructure(capturedStructure, expectedType, context);
                }
                else
                {
                    var structures = StructuredArrayValue.Read(capturedStructure, context);
                    structures.Validate(field.ValueRank, field.ArrayDimensions, context);
                    foreach (Variant element in structures.Elements)
                    {
                        RequireStructure(element, expectedType, context);
                    }
                }
                return capturedStructure;
            }
            if (field.ValueRank == ValueRanks.Scalar)
            {
                RequireScalar(value);
                return value.Copy();
            }
            if (context is null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Array fields require an explicitly configured encoding context.");
            }
            Variant captured = Snapshot(value, context);
            var array = StructuredArrayValue.Read(captured, context);
            if (array.Elements.Count > MaximumElements)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The array exceeds 1024 elements.");
            }
            array.Validate(field.ValueRank, field.ArrayDimensions, context);
            foreach (Variant element in array.Elements)
            {
                RequireScalar(element);
            }
            return captured;
        }

        /// <summary>
        /// Copies through the stack codec into a fixed-capacity buffer; excess data cannot grow the allocation.
        /// </summary>
        public static Variant Snapshot(Variant value, IServiceMessageContext context)
        {
            return Decode(Encode(value, context), context);
        }

        /// <summary>
        /// Identifies fields whose numeric namespace indexes require an explicit mapping contract.
        /// </summary>
        public static bool UsesNamespaces(BuiltInType type)
        {
            return type is BuiltInType.NodeId or BuiltInType.ExpandedNodeId or BuiltInType.QualifiedName or
                BuiltInType.ExtensionObject;
        }

        /// <summary>
        /// Identifies schemas that require exact adapter metadata and namespace validation.
        /// </summary>
        public static bool RequiresMapping(PubSubConfiguration configuration)
        {
            return !configuration.Structures.IsEmpty ||
                configuration.Fields.Contains(field =>
                field is not null && (field.ValueRank != ValueRanks.Scalar || UsesNamespaces(field.Type)));
        }

        /// <summary>
        /// Encodes at most one bounded field without expandable intermediate storage.
        /// </summary>
        public static ByteString Encode(Variant value, IServiceMessageContext context)
        {
            return Encode(value, context, null);
        }

        /// <summary>
        /// Remaps nested NodeIds through the stack encoder using explicit source and destination namespaces.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static ByteString Encode(
            Variant value, IServiceMessageContext context, NamespaceTable? destinationNamespaces)
        {
            if (destinationNamespaces is not null)
            {
                RequireMappedNamespaces(value, context, destinationNamespaces, 0);
            }
            var buffer = new byte[MaximumValueBytes];
            using var stream = new MemoryStream(buffer, writable: true);
            try
            {
                using var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
                if (destinationNamespaces is not null)
                {
                    encoder.SetMappingTables(destinationNamespaces, null);
                }
                encoder.WriteVariant(null, value);
            }
            catch (NotSupportedException error)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The field exceeds its bounded encoded size.", error);
            }
            return ByteString.From(buffer.AsSpan(0, checked((int)stream.Position)));
        }

        /// <summary>
        /// Decodes one complete field and rejects trailing or oversized data.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static Variant Decode(ByteString content, IServiceMessageContext context)
        {
            if (content.IsNull || content.Length is 0 or > MaximumValueBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            using var stream = new MemoryStream(content.Span.ToArray(), writable: false);
            using var decoder = new BinaryDecoder(stream, context, leaveOpen: true);
            Variant result = decoder.ReadVariant(null);
            if (stream.Position != stream.Length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The field contains trailing encoded data.");
            }
            return result;
        }

        /// <summary>
        /// Accepts concrete ranks while preserving unspecified per-dimension maxima.
        /// </summary>
        public static bool IsValidShape(int rank, ArrayOf<uint> dimensions)
        {
            if (rank == ValueRanks.Scalar)
            {
                return dimensions.Count == 0;
            }
            if (rank is < 1 or > MaximumRank || (dimensions.Count != 0 && dimensions.Count != rank))
            {
                return false;
            }
            ulong count = 1;
            foreach (uint dimension in dimensions)
            {
                if (dimension > MaximumElements)
                {
                    return false;
                }
                count *= dimension == 0 ? 1u : dimension;
                if (count > MaximumElements)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether local generation can use the reviewed shape without guessing sizes.
        /// </summary>
        public static bool HasConcreteDimensions(PubSubFieldConfiguration field)
        {
            if (field.ValueRank < 1 ||
                field.ArrayDimensions.Count != field.ValueRank ||
                !IsValidShape(field.ValueRank, field.ArrayDimensions))
            {
                return false;
            }
            foreach (uint dimension in field.ArrayDimensions)
            {
                if (dimension == 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static void RequireMappedNamespaces(
            Variant value, IServiceMessageContext context, NamespaceTable destination, int depth)
        {
            if (value.IsNull || value.ValueIsDefaultOrNull || !UsesNamespaces(value.TypeInfo.BuiltInType))
            {
                return;
            }
            if (depth > 16)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "Namespace mapping exceeds the bounded value depth.");
            }
            if (!value.TypeInfo.IsScalar)
            {
                var array = StructuredArrayValue.Read(value, context);
                foreach (Variant element in array.Elements)
                {
                    RequireMappedNamespaces(element, context, destination, depth + 1);
                }
            }
            else if (value.TryGetValue(out NodeId node))
            {
                RequireNamespace(
                    context.NamespaceUris.GetString(node.NamespaceIndex), context.NamespaceUris, destination);
            }
            else if (value.TryGetValue(out ExpandedNodeId expanded))
            {
                RequireExpandedNamespace(expanded, context.NamespaceUris, destination);
            }
            else if (value.TryGetValue(out QualifiedName name))
            {
                RequireNamespace(
                    context.NamespaceUris.GetString(name.NamespaceIndex), context.NamespaceUris, destination);
            }
            else if (value.TryGetValue<IEncodeable>(out IEncodeable? body, context) && body is IStructure structure)
            {
                RequireExpandedNamespace(body.BinaryEncodingId, context.NamespaceUris, destination);
                foreach (IStructureField field in structure.GetFields())
                {
                    string fieldName = field.Name ??
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "An unnamed field cannot be mapped.");
                    RequireMappedNamespaces(structure[fieldName], context, destination, depth + 1);
                }
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "A namespace-bearing field requires its reviewed typed value.");
            }
        }

        private static void RequireExpandedNamespace(
            ExpandedNodeId node, NamespaceTable source, NamespaceTable destination)
        {
            if (node.ServerIndex != 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "PubSub commissioning does not define a remote server-index table.");
            }
            RequireNamespace(string.IsNullOrEmpty(node.NamespaceUri)
                ? source.GetString(node.NamespaceIndex) : node.NamespaceUri, source, destination);
        }

        private static void RequireNamespace(string? uri, NamespaceTable source, NamespaceTable destination)
        {
            if (uri is null || source.GetIndex(uri) < 0 || destination.GetIndex(uri) < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                    "The field references a namespace outside its reviewed mapping.");
            }
        }

        private static void RequireStructure(Variant value, NodeId expectedType, IServiceMessageContext context)
        {
            if (!value.TryGetValue<IEncodeable>(out IEncodeable? body, context) ||
                body is not IStructure structure ||
                ExpandedNodeId.ToNodeId(body.TypeId, context.NamespaceUris) != expectedType)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The structure is absent or does not match the reviewed field type.");
            }
            foreach (IStructureField field in structure.GetFields())
            {
                Variant member = structure[field.Name ??
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError, "The structure contains an unnamed field.")];
                if (member.IsNull || member.ValueIsDefaultOrNull)
                {
                    continue;
                }
                if (member.TypeInfo.IsScalar)
                {
                    if (member.TypeInfo.BuiltInType == BuiltInType.ExtensionObject)
                    {
                        if (!member.TryGetValue<IEncodeable>(out IEncodeable? nested, context) || nested is null)
                        {
                            throw new ServiceResultException(StatusCodes.BadDecodingError);
                        }
                        RequireStructure(
                            member, ExpandedNodeId.ToNodeId(nested.TypeId, context.NamespaceUris), context);
                    }
                    else
                    {
                        RequireScalar(member);
                    }
                }
                else
                {
                    var array = StructuredArrayValue.Read(member, context);
                    foreach (Variant element in array.Elements)
                    {
                        if (element.TypeInfo.BuiltInType == BuiltInType.ExtensionObject)
                        {
                            if (!element.TryGetValue<IEncodeable>(out IEncodeable? nested, context) || nested is null)
                            {
                                throw new ServiceResultException(StatusCodes.BadDecodingError);
                            }
                            RequireStructure(
                                element, ExpandedNodeId.ToNodeId(nested.TypeId, context.NamespaceUris), context);
                        }
                        else
                        {
                            RequireScalar(element);
                        }
                    }
                }
            }
        }

        private static void RequireScalar(Variant value)
        {
            if ((!value.IsNull && !value.TypeInfo.IsScalar) ||
                (value.TryGetValue(out double number) && !double.IsFinite(number)) ||
                (value.TryGetValue(out float single) && !float.IsFinite(single)) ||
                PubSubValueDisplay.Create(new DataSetField { Value = value }).Truncated)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "A scalar value or array element is non-finite, oversized or unsupported.");
            }
        }

        /// <summary>The maximum concrete matrix rank.</summary>
        public const int MaximumRank = 8;

        /// <summary>The maximum decoded element count per field.</summary>
        public const int MaximumElements = 1024;

        /// <summary>The maximum encoded size of one captured field.</summary>
        public const int MaximumValueBytes = 16 * 1024;
    }
}
