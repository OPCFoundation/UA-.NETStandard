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
    /// Every built-in scalar, array and matrix is detached with its type,
    /// rank and dimensions preserved. Nested <see cref="Variant"/> and
    /// <see cref="DataValue"/> payloads are detached recursively, and an
    /// undecoded binary <see cref="ExtensionObject"/> body is copied. A
    /// decoded structure is copied through <see cref="System.ICloneable.Clone"/>:
    /// the generated implementation allocates new arrays but shares the
    /// buffers of <see cref="ByteString"/> and <see cref="Variant"/> fields,
    /// so a provider must not mutate such fields after publication.
    /// </para>
    /// </remarks>
    internal static class IaValueCopy
    {
        /// <summary>
        /// Returns a value whose storage is owned by the address space.
        /// </summary>
        /// <exception cref="ServiceResultException">The value has a built-in
        /// type whose ownership cannot be established.</exception>
        public static Variant Own(in Variant value)
        {
            if (value.IsNull || value.IsEmptyArray)
            {
                // No element remains that a provider could change.
                return value;
            }
            if (value.TypeInfo.IsScalar)
            {
                return OwnScalar(value);
            }
            return value.TypeInfo.IsMatrix ? OwnMatrix(value) : OwnArray(value);
        }

        /// <summary>
        /// Detaches a scalar; other built-in scalars are immutable.
        /// </summary>
        private static Variant OwnScalar(in Variant value)
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
                    return new Variant(Own(v));
                default:
                    return value;
            }
        }

        /// <summary>
        /// Copies a one-dimensional array and the mutable payload of its elements.
        /// </summary>
        private static Variant OwnArray(in Variant value)
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
                    return new Variant(Detach(v));
                case BuiltInType.ExtensionObject when value.TryGetValue(out ArrayOf<ExtensionObject> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.DataValue when value.TryGetValue(out ArrayOf<DataValue> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.Variant when value.TryGetValue(out ArrayOf<Variant> v):
                    return new Variant(v.ConvertAll(e => Own(e)));
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    // Copy allocates new storage and the elements are numeric scalars.
                    return value.Copy();
                default:
                    throw Unsupported(value);
            }
        }

        /// <summary>
        /// Copies a multi-dimensional matrix with its dimensions and the mutable
        /// payload of its elements.
        /// </summary>
        private static Variant OwnMatrix(in Variant value)
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
                    return new Variant(Detach(v));
                case BuiltInType.ExtensionObject when value.TryGetValue(out MatrixOf<ExtensionObject> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.DataValue when value.TryGetValue(out MatrixOf<DataValue> v):
                    return new Variant(v.ConvertAll(Own));
                case BuiltInType.Variant when value.TryGetValue(out MatrixOf<Variant> v):
                    return new Variant(v.ConvertAll(e => Own(e)));
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    // Copy allocates new storage and the elements are numeric scalars.
                    return value.Copy();
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
        /// Copies an extension object and its undecoded binary body.
        /// </summary>
        private static ExtensionObject Own(ExtensionObject value)
        {
            if (value.TryGetValue(out IEncodeable? body))
            {
                return new ExtensionObject(Own(value.TypeId), body, true);
            }
            if (value.TryGetAsBinary(out ByteString binary))
            {
                return new ExtensionObject(Own(value.TypeId), Own(binary));
            }
            // A null, XML or JSON body is immutable.
            return value;
        }

        /// <summary>
        /// Detaches the value wrapped by a data value.
        /// </summary>
        private static DataValue Own(DataValue value)
        {
            return value.IsNull ? value : value.WithWrappedValue(Own(value.WrappedValue));
        }

        /// <summary>
        /// Copies the element storage of an array.
        /// </summary>
        private static ArrayOf<T> Detach<T>(ArrayOf<T> value)
        {
            return ArrayOf.Create(value.Span);
        }

        /// <summary>
        /// Copies the element storage of a matrix and keeps its dimensions.
        /// </summary>
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