/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Runtime.CompilerServices;
using Opc.Ua.Server;

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Detaches provider values from caller-owned buffers before publication.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Variant.Copy"/> clones structured payloads but shares the
    /// storage of primitive arrays and matrices, of <see cref="ByteString"/>
    /// values and of opaque node identifiers. A provider that reuses such a
    /// buffer would then change the published value without a notification.
    /// </para>
    /// <para>
    /// Every supported built-in scalar, array and matrix is detached with its type,
    /// rank and dimensions preserved. Nested <see cref="Variant"/> and
    /// <see cref="DataValue"/> payloads are detached recursively. Localized text
    /// uses <see cref="LocalizedText.Copy"/> to retain deferred translation with
    /// detached dictionary and supported immutable formatting arguments. An
    /// <see cref="ExtensionObject"/> gets a copied type identifier and body.
    /// Exact <see cref="EnumValueType"/> and <see cref="Argument"/> bodies use
    /// field-wise copies. Their <see cref="LocalizedText"/> fields use
    /// <see cref="LocalizedText.Copy"/>. An <see cref="Argument"/> with a
    /// runtime-only value, and other decoded structures, are rejected before
    /// invoking custom code. Nonempty arrays and matrices tagged with an abstract
    /// numeric type are rejected rather than inferred or cloned.
    /// Failed reads of mutable scalar payloads are rejected. Recursive copies
    /// enforce the server's encoding nesting limit, or the SDK default for a
    /// context without server quotas, and check remaining stack before descending.
    /// </para>
    /// </remarks>
    internal static class IaValueCopy
    {
        /// <summary>
        /// Returns a value whose storage is owned by the address space.
        /// </summary>
        /// <exception cref="ServiceResultException">The value has a built-in
        /// type whose ownership cannot be established.</exception>
        public static Variant Own(in Variant value, ISystemContext context)
        {
            int maxDepth = context is ServerSystemContext serverContext
                ? serverContext.Server.MessageContext.MaxEncodingNestingLevels
                : DefaultEncodingLimits.MaxEncodingNestingLevels;
            return Own(value, maxDepth, 0);
        }

        /// <summary>
        /// Bounds recursive copies using the codec nesting and remaining-stack checks.
        /// </summary>
        private static Variant Own(in Variant value, int maxDepth, int depth)
        {
            if (value.IsNull || value.IsEmptyArray)
            {
                // No element remains that a provider could change.
                return value;
            }
            if (depth > maxDepth)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The statistics value exceeds the maximum nesting level.");
            }
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "Insufficient stack to copy a nested statistics value.");
            }
            if (value.TypeInfo.IsScalar)
            {
                return OwnScalar(value, maxDepth, depth);
            }
            return value.TypeInfo.IsMatrix ? OwnMatrix(value, maxDepth, depth) : OwnArray(value, maxDepth, depth);
        }

        /// <summary>
        /// Detaches a scalar; other built-in scalars are immutable.
        /// </summary>
        private static Variant OwnScalar(in Variant value, int maxDepth, int depth)
        {
            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.ByteString when value.TryGetValue(out ByteString v):
                    return new Variant(Own(v));
                case BuiltInType.NodeId when value.TryGetValue(out NodeId v):
                    return new Variant(Own(v));
                case BuiltInType.ExpandedNodeId when value.TryGetValue(out ExpandedNodeId v):
                    return new Variant(Own(v));
                case BuiltInType.ExtensionObject when value.TryGetValue(out ExtensionObject v):
                    return new Variant(Own(v));
                case BuiltInType.DataValue when value.TryGetValue(out DataValue v):
                    return new Variant(Own(v, maxDepth, depth));
                case BuiltInType.LocalizedText when value.TryGetValue(out LocalizedText v):
                    return new Variant(v.Copy());
                case BuiltInType.ByteString:
                case BuiltInType.NodeId:
                case BuiltInType.ExpandedNodeId:
                case BuiltInType.ExtensionObject:
                case BuiltInType.DataValue:
                case BuiltInType.LocalizedText:
                    throw Unsupported(value);
                default:
                    return value;
            }
        }

        /// <summary>
        /// Copies a one-dimensional array and the mutable payload of its elements.
        /// </summary>
        private static Variant OwnArray(in Variant value, int maxDepth, int depth)
        {
            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean when value.TryGetValue(out ArrayOf<bool> v):
                    return new Variant(Detach(v));
                case BuiltInType.SByte when value.TryGetValue(out ArrayOf<sbyte> v):
                    return new Variant(Detach(v));
                case BuiltInType.Byte when value.TryGetValue(out ArrayOf<byte> v):
                    return new Variant(Detach(v));
                case BuiltInType.Int16 when value.TryGetValue(out ArrayOf<short> v):
                    return new Variant(Detach(v));
                case BuiltInType.UInt16 when value.TryGetValue(out ArrayOf<ushort> v):
                    return new Variant(Detach(v));
                case BuiltInType.Int32 when value.TryGetValue(out ArrayOf<int> v):
                    return new Variant(Detach(v));
                case BuiltInType.Enumeration when value.TryGetValue(out ArrayOf<EnumValue> v):
                    return new Variant(Detach(v));
                case BuiltInType.UInt32 when value.TryGetValue(out ArrayOf<uint> v):
                    return new Variant(Detach(v));
                case BuiltInType.Int64 when value.TryGetValue(out ArrayOf<long> v):
                    return new Variant(Detach(v));
                case BuiltInType.UInt64 when value.TryGetValue(out ArrayOf<ulong> v):
                    return new Variant(Detach(v));
                case BuiltInType.Float when value.TryGetValue(out ArrayOf<float> v):
                    return new Variant(Detach(v));
                case BuiltInType.Double when value.TryGetValue(out ArrayOf<double> v):
                    return new Variant(Detach(v));
                case BuiltInType.String when value.TryGetValue(out ArrayOf<string> v):
                    return new Variant(Detach(v));
                case BuiltInType.DateTime when value.TryGetValue(out ArrayOf<DateTimeUtc> v):
                    return new Variant(Detach(v));
                case BuiltInType.Guid when value.TryGetValue(out ArrayOf<Uuid> v):
                    return new Variant(Detach(v));
                case BuiltInType.ByteString when value.TryGetValue(out ArrayOf<ByteString> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.XmlElement when value.TryGetValue(out ArrayOf<XmlElement> v):
                    return new Variant(Detach(v));
                case BuiltInType.NodeId when value.TryGetValue(out ArrayOf<NodeId> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.ExpandedNodeId when value.TryGetValue(out ArrayOf<ExpandedNodeId> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.StatusCode when value.TryGetValue(out ArrayOf<StatusCode> v):
                    return new Variant(Detach(v));
                case BuiltInType.QualifiedName when value.TryGetValue(out ArrayOf<QualifiedName> v):
                    return new Variant(Detach(v));
                case BuiltInType.LocalizedText when value.TryGetValue(out ArrayOf<LocalizedText> v):
                    return new Variant(v.ConvertAll(e => e.Copy()));
                case BuiltInType.ExtensionObject when value.TryGetValue(out ArrayOf<ExtensionObject> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.DataValue when value.TryGetValue(out ArrayOf<DataValue> v):
                    return new Variant(v.ConvertAll(e => Own(e, maxDepth, depth + 1)));
                case BuiltInType.Variant when value.TryGetValue(out ArrayOf<Variant> v):
                    return new Variant(v.ConvertAll(e => Own(e, maxDepth, depth + 1)));
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    throw Unsupported(value);
                default:
                    throw Unsupported(value);
            }
        }

        /// <summary>
        /// Copies a multi-dimensional matrix with its dimensions and the mutable
        /// payload of its elements.
        /// </summary>
        private static Variant OwnMatrix(in Variant value, int maxDepth, int depth)
        {
            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean when value.TryGetValue(out MatrixOf<bool> v):
                    return new Variant(Detach(v));
                case BuiltInType.SByte when value.TryGetValue(out MatrixOf<sbyte> v):
                    return new Variant(Detach(v));
                case BuiltInType.Byte when value.TryGetValue(out MatrixOf<byte> v):
                    return new Variant(Detach(v));
                case BuiltInType.Int16 when value.TryGetValue(out MatrixOf<short> v):
                    return new Variant(Detach(v));
                case BuiltInType.UInt16 when value.TryGetValue(out MatrixOf<ushort> v):
                    return new Variant(Detach(v));
                case BuiltInType.Int32 when value.TryGetValue(out MatrixOf<int> v):
                    return new Variant(Detach(v));
                case BuiltInType.Enumeration when value.TryGetValue(out MatrixOf<EnumValue> v):
                    return new Variant(Detach(v));
                case BuiltInType.UInt32 when value.TryGetValue(out MatrixOf<uint> v):
                    return new Variant(Detach(v));
                case BuiltInType.Int64 when value.TryGetValue(out MatrixOf<long> v):
                    return new Variant(Detach(v));
                case BuiltInType.UInt64 when value.TryGetValue(out MatrixOf<ulong> v):
                    return new Variant(Detach(v));
                case BuiltInType.Float when value.TryGetValue(out MatrixOf<float> v):
                    return new Variant(Detach(v));
                case BuiltInType.Double when value.TryGetValue(out MatrixOf<double> v):
                    return new Variant(Detach(v));
                case BuiltInType.String when value.TryGetValue(out MatrixOf<string> v):
                    return new Variant(Detach(v));
                case BuiltInType.DateTime when value.TryGetValue(out MatrixOf<DateTimeUtc> v):
                    return new Variant(Detach(v));
                case BuiltInType.Guid when value.TryGetValue(out MatrixOf<Uuid> v):
                    return new Variant(Detach(v));
                case BuiltInType.ByteString when value.TryGetValue(out MatrixOf<ByteString> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.XmlElement when value.TryGetValue(out MatrixOf<XmlElement> v):
                    return new Variant(Detach(v));
                case BuiltInType.NodeId when value.TryGetValue(out MatrixOf<NodeId> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.ExpandedNodeId when value.TryGetValue(out MatrixOf<ExpandedNodeId> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.StatusCode when value.TryGetValue(out MatrixOf<StatusCode> v):
                    return new Variant(Detach(v));
                case BuiltInType.QualifiedName when value.TryGetValue(out MatrixOf<QualifiedName> v):
                    return new Variant(Detach(v));
                case BuiltInType.LocalizedText when value.TryGetValue(out MatrixOf<LocalizedText> v):
                    return new Variant(v.ConvertAll(e => e.Copy()));
                case BuiltInType.ExtensionObject when value.TryGetValue(out MatrixOf<ExtensionObject> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.DataValue when value.TryGetValue(out MatrixOf<DataValue> v):
                    return new Variant(v.ConvertAll(e => Own(e, maxDepth, depth + 1)));
                case BuiltInType.Variant when value.TryGetValue(out MatrixOf<Variant> v):
                    return new Variant(v.ConvertAll(e => Own(e, maxDepth, depth + 1)));
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    throw Unsupported(value);
                default:
                    throw Unsupported(value);
            }
        }

        /// <summary>
        /// Copies the bytes of a non-empty byte string.
        /// </summary>
        private static ByteString Own(ByteString value)
        {
            return value.IsEmpty ? value : ByteString.From(value.Span);
        }

        /// <summary>
        /// Copies the identifier of an opaque node id.
        /// </summary>
        private static NodeId Own(NodeId value)
        {
            return value.IdType == IdType.Opaque && value.TryGetValue(out ByteString identifier)
                ? new NodeId(Own(identifier), value.NamespaceIndex)
                : value;
        }

        /// <summary>
        /// Copies the identifier of an opaque expanded node id.
        /// </summary>
        private static ExpandedNodeId Own(ExpandedNodeId value)
        {
            return value.IdType == IdType.Opaque
                ? new ExpandedNodeId(Own(value.InnerNodeId), value.NamespaceUri, value.ServerIndex)
                : value;
        }

        /// <summary>
        /// Copies an extension object, its type identifier and its body.
        /// </summary>
        private static ExtensionObject Own(ExtensionObject value)
        {
            if (value.IsNull)
            {
                return value;
            }
            ExpandedNodeId typeId = Own(value.TypeId);
            if (value.TryGetValue(out IEncodeable? body))
            {
                return new ExtensionObject(typeId, Own(body));
            }
            if (value.TryGetAsBinary(out ByteString binary))
            {
                return new ExtensionObject(typeId, Own(binary));
            }
            // XML and JSON bodies are immutable strings.
            if (value.TryGetAsXml(out XmlElement xml))
            {
                return new ExtensionObject(typeId, xml);
            }
            if (value.TryGetAsJson(out string? json))
            {
                return new ExtensionObject(typeId, json);
            }
            return new ExtensionObject(typeId);
        }

        /// <summary>
        /// Copies an explicitly supported structure field by field.
        /// </summary>
        /// <exception cref="ServiceResultException">The structure has no safe field-wise copy.</exception>
        private static IEncodeable Own(IEncodeable body)
        {
            if (body.GetType() == typeof(EnumValueType) && body is EnumValueType enumValue)
            {
                return new EnumValueType
                {
                    Value = enumValue.Value,
                    DisplayName = enumValue.DisplayName.Copy(),
                    Description = enumValue.Description.Copy()
                };
            }
            if (body.GetType() == typeof(Argument) && body is Argument argument)
            {
                if (argument.Value != null)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "An Argument with a runtime-only value cannot be copied safely.");
                }
                return new Argument
                {
                    Name = argument.Name,
                    DataType = Own(argument.DataType),
                    ValueRank = argument.ValueRank,
                    ArrayDimensions = Detach(argument.ArrayDimensions),
                    Description = argument.Description.Copy()
                };
            }
            throw new ServiceResultException(StatusCodes.BadNotSupported,
                "The decoded structure has no supported field-wise copy.");
        }

        /// <summary>
        /// Detaches the value wrapped by a data value.
        /// </summary>
        private static DataValue Own(DataValue value, int maxDepth, int depth)
        {
            return value.IsNull ? value : value.WithWrappedValue(Own(value.WrappedValue, maxDepth, depth + 1));
        }

        /// <summary>
        /// Copies the element storage of an array.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        private static ArrayOf<T> Detach<T>(ArrayOf<T> value)
        {
            if (value.IsNull)
            {
                return ArrayOf<T>.Null;
            }
            return ArrayOf.Create(value.Span);
        }

        /// <summary>
        /// Copies the element storage of a matrix and keeps its dimensions.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        private static MatrixOf<T> Detach<T>(MatrixOf<T> value)
        {
            return ArrayOf.Create(value.Span).ToMatrix(value.Dimensions);
        }

        /// <summary>
        /// Reports a value whose storage cannot be detached.
        /// </summary>
        private static ServiceResultException Unsupported(in Variant value)
        {
            return new ServiceResultException(StatusCodes.BadNotSupported,
                $"A {value.TypeInfo} value cannot be detached from its provider.");
        }
    }
}