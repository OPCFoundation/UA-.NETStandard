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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Encodes OPC UA values into the Protobuf wire format defined by the Part 6 Protobuf DataEncoding.
    /// </summary>
    public sealed class ProtobufEncoder : IEncoder
    {
        /// <summary>
        /// Initializes a new ProtobufEncoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        public ProtobufEncoder(IServiceMessageContext context)
            : this(new MemoryStream(), context, false) { }

        /// <summary>
        /// Initializes a new ProtobufEncoder instance for the experimental OPC UA encoding support.
        /// </summary>
        /// <param name = "stream">The stream that receives or supplies the encoded payload.</param>
        /// <param name = "context">The service message context that supplies namespace, server URI, and encodeable type resolution tables.</param>
        /// <param name = "leaveOpen">True to leave the caller-owned stream open when the codec is closed.</param>
        public ProtobufEncoder(Stream stream, IServiceMessageContext context, bool leaveOpen = true)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_stream = stream ?? throw new ArgumentNullException(nameof(stream));
            m_leaveOpen = leaveOpen;
            m_stack.Push(new Frame(new BinaryWriter(m_stream, Encoding.UTF8, true)));
        }

        /// <inheritdoc/>
        public EncodingType EncodingType
        {
            get { return EncodingType.Protobuf; }
        }

        /// <inheritdoc/>
        public bool CanOmitFields
        {
            get { return true; }
        }

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// Returns the bytes written by the internally managed memory stream.
        /// </summary>
        /// <returns>The encoded Protobuf payload bytes.</returns>
        public byte[] ToArray()
        {
            if (m_stream is MemoryStream ms)
            {
                return ms.ToArray();
            }

            throw new NotSupportedException("ProtobufEncoder.ToArray requires the internally managed MemoryStream.");
        }

        /// <inheritdoc/>
        public int Close()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(ProtobufEncoder));
            }

            m_stack.Peek().Writer.Flush();
            int length = m_stream is MemoryStream ms ? checked((int)ms.Length) : 0;
            if (!m_leaveOpen)
            {
                m_stream.Dispose();
            }

            m_disposed = true;
            return length;
        }

        /// <inheritdoc/>
        public string? CloseAndReturnText()
        {
            return EncoderCompat.ToLowerHexString(ToArray());
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                Close();
            }
        }

        /// <inheritdoc/>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
        {
            m_namespaceMappings = namespaceUris?.CreateMapping(Context.NamespaceUris, false);
            m_serverMappings = serverUris?.CreateMapping(Context.ServerUris, false);
        }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri) { }

        /// <inheritdoc/>
        public void PopNamespace() { }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message)
            where T : IEncodeable, new()
        {
            EncodeMessage(message, message.TypeId);
        }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteNodeId("type_id", ExpandedNodeId.ToNodeId(encodeableTypeId, Context.NamespaceUris));
            WriteEncodeable("body", message, encodeableTypeId);
        }

        /// <inheritdoc/>
        public void WriteBoolean(string? fieldName, bool value)
        {
            WriteVarint(fieldName, value ? 1UL : 0UL);
        }

        /// <inheritdoc/>
        public void WriteSByte(string? fieldName, sbyte value)
        {
            WriteSignedVarint(fieldName, value);
        }

        /// <inheritdoc/>
        public void WriteByte(string? fieldName, byte value)
        {
            WriteVarint(fieldName, value);
        }

        /// <inheritdoc/>
        public void WriteInt16(string? fieldName, short value)
        {
            WriteSignedVarint(fieldName, value);
        }

        /// <inheritdoc/>
        public void WriteUInt16(string? fieldName, ushort value)
        {
            WriteVarint(fieldName, value);
        }

        /// <inheritdoc/>
        public void WriteInt32(string? fieldName, int value)
        {
            WriteSignedVarint(fieldName, value);
        }

        /// <inheritdoc/>
        public void WriteUInt32(string? fieldName, uint value)
        {
            WriteVarint(fieldName, value);
        }

        /// <inheritdoc/>
        public void WriteInt64(string? fieldName, long value)
        {
            WriteSignedVarint(fieldName, value);
        }

        /// <inheritdoc/>
        public void WriteUInt64(string? fieldName, ulong value)
        {
            WriteVarint(fieldName, value);
        }

        /// <inheritdoc/>
        public void WriteFloat(string? fieldName, float value)
        {
            WriteFixed32(fieldName, EncoderCompat.SingleToUInt32Bits(value));
        }

        /// <inheritdoc/>
        public void WriteDouble(string? fieldName, double value)
        {
            WriteFixed64(fieldName, EncoderCompat.DoubleToUInt64Bits(value));
        }

        /// <inheritdoc/>
        public void WriteString(string? fieldName, string? value)
        {
            WriteMessage(
                fieldName,
                w =>
                {
                    if (value != null)
                    {
                        Proto.WriteTag(w, 1, 2);
                        Proto.WriteString(w, value);
                    }
                }
            );
        }

        /// <inheritdoc/>
        public void WriteDateTime(string? fieldName, DateTimeUtc value)
        {
            WriteFixed64(fieldName, unchecked((ulong)(long)value));
        }

        /// <inheritdoc/>
        public void WriteGuid(string? fieldName, Uuid value)
        {
            WriteBytes(fieldName, value.ToByteArray());
        }

        /// <inheritdoc/>
        public void WriteByteString(string? fieldName, ByteString value)
        {
            WriteMessage(
                fieldName,
                w =>
                {
                    if (!value.IsNull)
                    {
                        Proto.WriteTag(w, 1, 2);
                        Proto.WriteBytes(w, value.Span);
                    }
                }
            );
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <inheritdoc/>
        public void WriteByteString(string? fieldName, ReadOnlySpan<byte> value)
        {
            WriteBytes(fieldName, value);
        }
#endif

        /// <inheritdoc/>
        public void WriteXmlElement(string? fieldName, XmlElement value)
        {
            WriteString(fieldName, value.OuterXml);
        }

        /// <inheritdoc/>
        public void WriteStatusCode(string? fieldName, StatusCode value)
        {
            WriteFixed32(fieldName, value.Code);
        }

        /// <inheritdoc/>
        public void WriteEnumerated<T>(string? fieldName, T value)
            where T : struct, Enum
        {
            WriteInt32(fieldName, Convert.ToInt32(value, CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public void WriteEnumerated(string? fieldName, EnumValue value)
        {
            WriteInt32(fieldName, value.Value);
        }

        /// <inheritdoc/>
        public void WriteSwitchField(uint switchField, out string? fieldName)
        {
            fieldName = null;
            m_stack.Peek().UnionSwitch = switchField;
            // Persist the union discriminator on the wire so a union whose selected member is not the
            // first one decodes correctly (the positional field probe alone always resolves member 1).
            Proto.WriteTag(Current, Proto.UnionSwitchField, 0);
            Proto.WriteVarint(Current, switchField);
        }

        /// <inheritdoc/>
        public void WriteEncodingMask(uint encodingMask)
        {
            m_stack.Peek().EncodingMask = encodingMask;
            // Persist the optional-field presence mask so structures with multiple optionals (where an
            // earlier optional is absent) round-trip without relying on positional presence probing.
            Proto.WriteTag(Current, Proto.UnionMaskField, 0);
            Proto.WriteVarint(Current, encodingMask);
        }

        /// <inheritdoc/>
        public void WriteNodeId(string? fieldName, NodeId value)
        {
            WriteMessage(fieldName, w => EncodeNodeId(w, value));
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeId(string? fieldName, ExpandedNodeId value)
        {
            WriteMessage(
                fieldName,
                w =>
                {
                    EncodeNodeIdField(w, 1, value.InnerNodeId);
                    if (value.NamespaceUri != null)
                    {
                        Proto.WriteTag(w, 2, 2);
                        Proto.WriteString(w, value.NamespaceUri);
                    }

                    if (value.ServerIndex != 0)
                    {
                        Proto.WriteTag(w, 3, 0);
                        Proto.WriteVarint(w, value.ServerIndex);
                    }
                }
            );
        }

        /// <inheritdoc/>
        public void WriteQualifiedName(string? fieldName, QualifiedName value)
        {
            WriteMessage(
                fieldName,
                w =>
                {
                    if (value.NamespaceIndex != 0)
                    {
                        Proto.WriteTag(w, 1, 0);
                        Proto.WriteVarint(w, value.NamespaceIndex);
                    }

                    if (value.Name != null)
                    {
                        Proto.WriteTag(w, 2, 2);
                        Proto.WriteString(w, value.Name);
                    }
                }
            );
        }

        /// <inheritdoc/>
        public void WriteLocalizedText(string? fieldName, LocalizedText value)
        {
            WriteMessage(
                fieldName,
                w =>
                {
                    if (value.Locale != null)
                    {
                        Proto.WriteTag(w, 1, 2);
                        Proto.WriteString(w, value.Locale);
                    }

                    if (value.Text != null)
                    {
                        Proto.WriteTag(w, 2, 2);
                        Proto.WriteString(w, value.Text);
                    }
                }
            );
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfo(string? fieldName, DiagnosticInfo? value)
        {
            if (value == null)
            {
                return;
            }

            WriteMessage(fieldName, w => EncodeDiagnosticInfo(w, value));
        }

        /// <inheritdoc/>
        public void WriteDataValue(string? fieldName, in DataValue value)
        {
            DataValue copy = value;
            WriteMessage(fieldName, w => EncodeDataValue(w, copy));
        }

        /// <inheritdoc/>
        public void WriteExtensionObject(string? fieldName, ExtensionObject value)
        {
            WriteMessage(fieldName, w => EncodeExtensionObject(w, value));
        }

        /// <inheritdoc/>
        public void WriteVariant(string? fieldName, in Variant value)
        {
            Variant copy = value;
            WriteMessage(fieldName, w => EncodeVariant(w, copy));
        }

        /// <inheritdoc/>
        public void WriteVariantValue(string? fieldName, in Variant value)
        {
            WriteVariant(fieldName, value);
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string? fieldName, T value)
            where T : IEncodeable, new()
        {
            WriteEncodeable(fieldName, value, value.TypeId);
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string? fieldName, T value, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (EqualityComparer<T>.Default.Equals(value, default!))
            {
                return;
            }

            WriteMessage(fieldName, w => WithFrame(w, () => value.Encode(this)));
        }

        /// <inheritdoc/>
        public void WriteEncodeableAsExtensionObject<T>(string? fieldName, T value)
            where T : IEncodeable
        {
            WriteExtensionObject(fieldName, new ExtensionObject(value));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values)
            where T : IEncodeable, new()
        {
            WriteArray(fieldName, values, values => WriteMessageRaw(1, w => WithFrame(w, () => values.Encode(this))));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string? fieldName, ArrayOf<T> values, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteArray(fieldName, values, values => WriteMessageRaw(1, w => WithFrame(w, () => values.Encode(this))));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArrayAsExtensionObjects<T>(string? fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            WriteArray(
                fieldName,
                values,
                values =>
                {
                    var eo = new ExtensionObject(values);
                    WriteMessageRaw(1, w => EncodeExtensionObject(w, eo));
                }
            );
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values)
            where T : IEncodeable, new()
        {
            WriteMatrix(fieldName, values, values => WriteMessageRaw(2, w => WithFrame(w, () => values.Encode(this))));
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(string? fieldName, MatrixOf<T> values, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            WriteMatrix(fieldName, values, values => WriteMessageRaw(2, w => WithFrame(w, () => values.Encode(this))));
        }

        /// <inheritdoc/>
        public void WriteBooleanArray(string? fieldName, ArrayOf<bool> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteVarint(Current, x ? 1UL : 0UL);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteSByteArray(string? fieldName, ArrayOf<sbyte> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteSignedVarint(Current, x);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteByteArray(string? fieldName, ArrayOf<byte> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteVarint(Current, x);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteInt16Array(string? fieldName, ArrayOf<short> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteSignedVarint(Current, x);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteUInt16Array(string? fieldName, ArrayOf<ushort> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteVarint(Current, x);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteInt32Array(string? fieldName, ArrayOf<int> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteSignedVarint(Current, x);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteUInt32Array(string? fieldName, ArrayOf<uint> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteVarint(Current, x);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteInt64Array(string? fieldName, ArrayOf<long> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteSignedVarint(Current, x);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteUInt64Array(string? fieldName, ArrayOf<ulong> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteVarint(Current, x);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteFloatArray(string? fieldName, ArrayOf<float> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 5);
                    Proto.WriteFixed32(Current, EncoderCompat.SingleToUInt32Bits(x));
                }
            );
        }

        /// <inheritdoc/>
        public void WriteDoubleArray(string? fieldName, ArrayOf<double> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 1);
                    Proto.WriteFixed64(Current, EncoderCompat.DoubleToUInt64Bits(x));
                }
            );
        }

        /// <inheritdoc/>
        public void WriteStringArray(string? fieldName, ArrayOf<string> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                    WriteMessageRaw(
                        1,
                        w =>
                        {
                            if (x != null)
                            {
                                Proto.WriteTag(w, 1, 2);
                                Proto.WriteString(w, x);
                            }
                        }
                    )
            );
        }

        /// <inheritdoc/>
        public void WriteDateTimeArray(string? fieldName, ArrayOf<DateTimeUtc> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 1);
                    Proto.WriteFixed64(Current, unchecked((ulong)(long)x));
                }
            );
        }

        /// <inheritdoc/>
        public void WriteGuidArray(string? fieldName, ArrayOf<Uuid> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 2);
                    Proto.WriteBytes(Current, x.ToByteArray());
                }
            );
        }

        /// <inheritdoc/>
        public void WriteByteStringArray(string? fieldName, ArrayOf<ByteString> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                    WriteMessageRaw(
                        1,
                        w =>
                        {
                            if (!x.IsNull)
                            {
                                Proto.WriteTag(w, 1, 2);
                                Proto.WriteBytes(w, x.Span);
                            }
                        }
                    )
            );
        }

        /// <inheritdoc/>
        public void WriteXmlElementArray(string? fieldName, ArrayOf<XmlElement> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                    WriteMessageRaw(
                        1,
                        w =>
                        {
                            if (x.OuterXml != null)
                            {
                                Proto.WriteTag(w, 1, 2);
                                Proto.WriteString(w, x.OuterXml);
                            }
                        }
                    )
            );
        }

        /// <inheritdoc/>
        public void WriteNodeIdArray(string? fieldName, ArrayOf<NodeId> values)
        {
            WriteArray(fieldName, values, x => WriteMessageRaw(1, w => EncodeNodeId(w, x)));
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeIdArray(string? fieldName, ArrayOf<ExpandedNodeId> values)
        {
            WriteArray(fieldName, values, x => WriteExpandedNodeId(null, x));
        }

        /// <inheritdoc/>
        public void WriteStatusCodeArray(string? fieldName, ArrayOf<StatusCode> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 5);
                    Proto.WriteFixed32(Current, x.Code);
                }
            );
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfoArray(string? fieldName, ArrayOf<DiagnosticInfo> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                    WriteMessageRaw(
                        1,
                        w =>
                        {
                            if (x != null)
                            {
                                EncodeDiagnosticInfo(w, x);
                            }
                        }
                    )
            );
        }

        /// <inheritdoc/>
        public void WriteQualifiedNameArray(string? fieldName, ArrayOf<QualifiedName> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                    WriteMessageRaw(
                        1,
                        w =>
                        {
                            if (x.NamespaceIndex != 0)
                            {
                                Proto.WriteTag(w, 1, 0);
                                Proto.WriteVarint(w, x.NamespaceIndex);
                            }

                            if (x.Name != null)
                            {
                                Proto.WriteTag(w, 2, 2);
                                Proto.WriteString(w, x.Name);
                            }
                        }
                    )
            );
        }

        /// <inheritdoc/>
        public void WriteLocalizedTextArray(string? fieldName, ArrayOf<LocalizedText> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                    WriteMessageRaw(
                        1,
                        w =>
                        {
                            if (x.Locale != null)
                            {
                                Proto.WriteTag(w, 1, 2);
                                Proto.WriteString(w, x.Locale);
                            }

                            if (x.Text != null)
                            {
                                Proto.WriteTag(w, 2, 2);
                                Proto.WriteString(w, x.Text);
                            }
                        }
                    )
            );
        }

        /// <inheritdoc/>
        public void WriteVariantArray(string? fieldName, ArrayOf<Variant> values)
        {
            WriteArray(fieldName, values, x => WriteMessageRaw(1, w => EncodeVariant(w, x)));
        }

        /// <inheritdoc/>
        public void WriteDataValueArray(string? fieldName, ArrayOf<DataValue> values)
        {
            WriteArray(fieldName, values, x => WriteMessageRaw(1, w => EncodeDataValue(w, x)));
        }

        /// <inheritdoc/>
        public void WriteExtensionObjectArray(string? fieldName, ArrayOf<ExtensionObject> values)
        {
            WriteArray(fieldName, values, x => WriteMessageRaw(1, w => EncodeExtensionObject(w, x)));
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray<T>(string? fieldName, ArrayOf<T> values)
            where T : struct, Enum
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteSignedVarint(Current, Convert.ToInt32(x, CultureInfo.InvariantCulture));
                }
            );
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray(string? fieldName, ArrayOf<EnumValue> values)
        {
            WriteArray(
                fieldName,
                values,
                x =>
                {
                    Proto.WriteTag(Current, 1, 0);
                    Proto.WriteSignedVarint(Current, x.Value);
                }
            );
        }

        private BinaryWriter Current
        {
            get { return m_stack.Peek().Writer; }
        }

        private void WriteVarint(string? name, ulong value)
        {
            Proto.WriteTag(Current, m_stack.Peek().Next(name), 0);
            Proto.WriteVarint(Current, value);
        }

        private void WriteSignedVarint(string? name, long value)
        {
            Proto.WriteTag(Current, m_stack.Peek().Next(name), 0);
            Proto.WriteSignedVarint(Current, value);
        }

        private void WriteFixed32(string? name, uint value)
        {
            Proto.WriteTag(Current, m_stack.Peek().Next(name), 5);
            Proto.WriteFixed32(Current, value);
        }

        private void WriteFixed64(string? name, ulong value)
        {
            Proto.WriteTag(Current, m_stack.Peek().Next(name), 1);
            Proto.WriteFixed64(Current, value);
        }

        private void WriteBytes(string? name, ReadOnlySpan<byte> bytes)
        {
            Proto.WriteTag(Current, m_stack.Peek().Next(name), 2);
            Proto.WriteBytes(Current, bytes);
        }

        private void WriteMessage(string? name, Action<BinaryWriter> encode)
        {
            WriteMessageRaw(m_stack.Peek().Next(name), encode);
        }

        private void WriteMessageRaw(int field, Action<BinaryWriter> encode)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
            encode(bw);
            bw.Flush();
            Proto.WriteTag(Current, field, 2);
            Proto.WriteBytes(Current, ms.ToArray());
        }

        private void WithFrame(BinaryWriter writer, Action action)
        {
            m_stack.Push(new Frame(writer));
            try
            {
                action();
            }
            finally
            {
                m_stack.Pop();
            }
        }

        private void WriteArray<T>(string? name, ArrayOf<T> values, Action<T> write)
        {
            if (values.IsNull)
            {
                return;
            }

            WriteMessage(
                name,
                w =>
                    WithFrame(
                        w,
                        () =>
                        {
                            for (int i = 0; i < values.Count; i++)
                            {
                                write(values.Span[i]);
                            }
                        }
                    )
            );
        }

        private void WriteMatrix<T>(string? name, MatrixOf<T> values, Action<T> write)
        {
            if (values.IsNull)
            {
                return;
            }

            WriteMessage(
                name,
                w =>
                    WithFrame(
                        w,
                        () =>
                        {
                            foreach (int d in values.Dimensions)
                            {
                                Proto.WriteTag(Current, 1, 0);
                                Proto.WriteSignedVarint(Current, d);
                            }

                            for (int i = 0; i < values.Count; i++)
                            {
                                write(values.Span[i]);
                            }
                        }
                    )
            );
        }

        private static void EncodeNodeIdField(BinaryWriter w, int field, NodeId n)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
            EncodeNodeId(bw, n);
            bw.Flush();
            Proto.WriteTag(w, field, 2);
            Proto.WriteBytes(w, ms.ToArray());
        }

        private static void EncodeNodeId(BinaryWriter w, NodeId n)
        {
            if (n.NamespaceIndex != 0)
            {
                Proto.WriteTag(w, 1, 0);
                Proto.WriteVarint(w, n.NamespaceIndex);
            }

            switch (n.IdType)
            {
                case IdType.Numeric:
                    Proto.WriteTag(w, 2, 0);
                    n.TryGetValue(out uint numericIdentifier);
                    Proto.WriteVarint(w, numericIdentifier);
                    break;
                case IdType.String:
                    Proto.WriteTag(w, 3, 2);
                    n.TryGetValue(out string stringIdentifier);
                    Proto.WriteString(w, stringIdentifier);
                    break;
                case IdType.Guid:
                    Proto.WriteTag(w, 4, 2);
                    n.TryGetValue(out Guid guidIdentifier);
                    Proto.WriteBytes(w, new Uuid(guidIdentifier).ToByteArray());
                    break;
                case IdType.Opaque:
                    Proto.WriteTag(w, 5, 2);
                    n.TryGetValue(out ByteString opaqueIdentifier);
                    Proto.WriteBytes(w, opaqueIdentifier.Span);
                    break;
            }
        }

        private void EncodeDiagnosticInfo(BinaryWriter w, DiagnosticInfo d)
        {
            if (d.SymbolicId >= 0)
            {
                Proto.WriteTag(w, 1, 0);
                Proto.WriteSignedVarint(w, d.SymbolicId);
            }

            if (d.NamespaceUri >= 0)
            {
                Proto.WriteTag(w, 2, 0);
                Proto.WriteSignedVarint(w, d.NamespaceUri);
            }

            if (d.Locale >= 0)
            {
                Proto.WriteTag(w, 3, 0);
                Proto.WriteSignedVarint(w, d.Locale);
            }

            if (d.LocalizedText >= 0)
            {
                Proto.WriteTag(w, 4, 0);
                Proto.WriteSignedVarint(w, d.LocalizedText);
            }

            if (d.AdditionalInfo != null)
            {
                Proto.WriteTag(w, 5, 2);
                Proto.WriteString(w, d.AdditionalInfo);
            }

            if (d.InnerStatusCode.Code != 0)
            {
                Proto.WriteTag(w, 6, 5);
                Proto.WriteFixed32(w, d.InnerStatusCode.Code);
            }

            if (d.InnerDiagnosticInfo != null)
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
                EncodeDiagnosticInfo(bw, d.InnerDiagnosticInfo);
                bw.Flush();
                Proto.WriteTag(w, 7, 2);
                Proto.WriteBytes(w, ms.ToArray());
            }
        }

        private void EncodeDataValue(BinaryWriter w, DataValue d)
        {
            if (d.IsNull)
            {
                return;
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
            if (!d.WrappedValue.IsNull)
            {
                EncodeVariantField(w, 1, d.WrappedValue);
            }

            Proto.WriteTag(w, 2, 5);
            Proto.WriteFixed32(w, d.StatusCode.Code);
            Proto.WriteTag(w, 3, 1);
            Proto.WriteFixed64(w, unchecked((ulong)(long)d.SourceTimestamp));
            if (d.SourcePicoseconds != 0)
            {
                Proto.WriteTag(w, 4, 0);
                Proto.WriteVarint(w, d.SourcePicoseconds);
            }

            Proto.WriteTag(w, 5, 1);
            Proto.WriteFixed64(w, unchecked((ulong)(long)d.ServerTimestamp));
            if (d.ServerPicoseconds != 0)
            {
                Proto.WriteTag(w, 6, 0);
                Proto.WriteVarint(w, d.ServerPicoseconds);
            }
        }

        private void EncodeExtensionObject(BinaryWriter w, ExtensionObject e)
        {
            if (e.IsNull)
            {
                return;
            }

            using (var ms = new MemoryStream())
            {
                using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
                EncodeNodeId(bw, ExpandedNodeId.ToNodeId(e.TypeId, Context.NamespaceUris));
                bw.Flush();
                Proto.WriteTag(w, 1, 2);
                Proto.WriteBytes(w, ms.ToArray());
            }

            if (e.TryGetAsBinary(out ByteString bs, Context) && !bs.IsNull)
            {
                Proto.WriteTag(w, 3, 2);
                Proto.WriteBytes(w, bs.Span);
            }
#pragma warning disable CS0618 // Justification: raw byte[] bodies have no non-obsolete type-safe accessor.

            else if (e.Body is byte[] bytes)
#pragma warning restore CS0618
            {
                Proto.WriteTag(w, 3, 2);
                Proto.WriteBytes(w, bytes);
            }
            else if (e.TryGetValue(out IEncodeable? enc, Context))
            {
                using var ms = new MemoryStream();
                using var be = new BinaryEncoder(ms, Context, true);
                enc.Encode(be);
                be.Close();
                Proto.WriteTag(w, 2, 2);
                Proto.WriteBytes(w, ms.ToArray());
            }
        }

        private void EncodeVariantField(BinaryWriter w, int field, Variant values)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
            EncodeVariant(bw, values);
            bw.Flush();
            Proto.WriteTag(w, field, 2);
            Proto.WriteBytes(w, ms.ToArray());
        }

        private void EncodeVariant(BinaryWriter w, Variant values)
        {
            if (values.IsNull)
            {
                return;
            }

            BuiltInType t = values.TypeInfo.BuiltInType;
            Proto.WriteTag(w, 1, 0);
            Proto.WriteVarint(w, (uint)t);
            object? o = values.AsBoxedObject(Variant.BoxingBehavior.None);
            if (o == null)
            {
                return;
            }

            if (values.TypeInfo.IsMatrix)
            {
                WriteVariantMatrix(w, t, values);
                return;
            }

            if (values.TypeInfo.IsArray)
            {
                WriteVariantArray(w, t, values);
                return;
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
            WithFrame(bw, () => WriteObjectAsField1(t, o));
            bw.Flush();
            Proto.WriteTag(w, 2, 2);
            Proto.WriteBytes(w, ms.ToArray());
        }

        private void WriteVariantArray(BinaryWriter w, BuiltInType type, Variant values)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
            WithFrame(bw, () => WriteVariantArrayValues(type, values));
            bw.Flush();
            Proto.WriteTag(w, 3, 2);
            Proto.WriteBytes(w, ms.ToArray());
        }

        private void WriteVariantMatrix(BinaryWriter w, BuiltInType type, Variant values)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
            WithFrame(bw, () => WriteVariantMatrixValues(type, values));
            bw.Flush();
            Proto.WriteTag(w, 4, 2);
            Proto.WriteBytes(w, ms.ToArray());
        }

        private void WriteVariantArrayValues(BuiltInType type, Variant values)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    WriteVariantElements(1, type, values.GetBooleanArray());
                    break;
                case BuiltInType.SByte:
                    WriteVariantElements(1, type, values.GetSByteArray());
                    break;
                case BuiltInType.Byte:
                    WriteVariantElements(1, type, values.GetByteArray());
                    break;
                case BuiltInType.Int16:
                    WriteVariantElements(1, type, values.GetInt16Array());
                    break;
                case BuiltInType.UInt16:
                    WriteVariantElements(1, type, values.GetUInt16Array());
                    break;
                case BuiltInType.Int32:
                    WriteVariantElements(1, type, values.GetInt32Array());
                    break;
                case BuiltInType.Enumeration:
                    WriteVariantElements(1, type, values.GetEnumerationArray());
                    break;
                case BuiltInType.UInt32:
                    WriteVariantElements(1, type, values.GetUInt32Array());
                    break;
                case BuiltInType.Int64:
                    WriteVariantElements(1, type, values.GetInt64Array());
                    break;
                case BuiltInType.UInt64:
                    WriteVariantElements(1, type, values.GetUInt64Array());
                    break;
                case BuiltInType.Float:
                    WriteVariantElements(1, type, values.GetFloatArray());
                    break;
                case BuiltInType.Double:
                    WriteVariantElements(1, type, values.GetDoubleArray());
                    break;
                case BuiltInType.String:
                    WriteVariantElements(1, type, values.GetStringArray());
                    break;
                case BuiltInType.DateTime:
                    WriteVariantElements(1, type, values.GetDateTimeArray());
                    break;
                case BuiltInType.Guid:
                    WriteVariantElements(1, type, values.GetGuidArray());
                    break;
                case BuiltInType.ByteString:
                    WriteVariantElements(1, type, values.GetByteStringArray());
                    break;
                case BuiltInType.XmlElement:
                    WriteVariantElements(1, type, values.GetXmlElementArray());
                    break;
                case BuiltInType.NodeId:
                    WriteVariantElements(1, type, values.GetNodeIdArray());
                    break;
                case BuiltInType.ExpandedNodeId:
                    WriteVariantElements(1, type, values.GetExpandedNodeIdArray());
                    break;
                case BuiltInType.StatusCode:
                    WriteVariantElements(1, type, values.GetStatusCodeArray());
                    break;
                case BuiltInType.QualifiedName:
                    WriteVariantElements(1, type, values.GetQualifiedNameArray());
                    break;
                case BuiltInType.LocalizedText:
                    WriteVariantElements(1, type, values.GetLocalizedTextArray());
                    break;
                case BuiltInType.ExtensionObject:
                    WriteVariantElements(1, type, values.GetExtensionObjectArray());
                    break;
                case BuiltInType.DataValue:
                    WriteVariantElements(1, type, values.GetDataValueArray());
                    break;
                default:
                    throw new NotSupportedException(
                        $"Variant array type {type} is not supported by the Protobuf reference encoder."
                    );
            }
        }

        private void WriteVariantMatrixValues(BuiltInType type, Variant values)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    WriteVariantMatrixElements(type, values.GetBooleanMatrix());
                    break;
                case BuiltInType.SByte:
                    WriteVariantMatrixElements(type, values.GetSByteMatrix());
                    break;
                case BuiltInType.Byte:
                    WriteVariantMatrixElements(type, values.GetByteMatrix());
                    break;
                case BuiltInType.Int16:
                    WriteVariantMatrixElements(type, values.GetInt16Matrix());
                    break;
                case BuiltInType.UInt16:
                    WriteVariantMatrixElements(type, values.GetUInt16Matrix());
                    break;
                case BuiltInType.Int32:
                    WriteVariantMatrixElements(type, values.GetInt32Matrix());
                    break;
                case BuiltInType.Enumeration:
                    WriteVariantMatrixElements(type, values.GetEnumerationMatrix());
                    break;
                case BuiltInType.UInt32:
                    WriteVariantMatrixElements(type, values.GetUInt32Matrix());
                    break;
                case BuiltInType.Int64:
                    WriteVariantMatrixElements(type, values.GetInt64Matrix());
                    break;
                case BuiltInType.UInt64:
                    WriteVariantMatrixElements(type, values.GetUInt64Matrix());
                    break;
                case BuiltInType.Float:
                    WriteVariantMatrixElements(type, values.GetFloatMatrix());
                    break;
                case BuiltInType.Double:
                    WriteVariantMatrixElements(type, values.GetDoubleMatrix());
                    break;
                case BuiltInType.String:
                    WriteVariantMatrixElements(type, values.GetStringMatrix());
                    break;
                case BuiltInType.DateTime:
                    WriteVariantMatrixElements(type, values.GetDateTimeMatrix());
                    break;
                case BuiltInType.Guid:
                    WriteVariantMatrixElements(type, values.GetGuidMatrix());
                    break;
                case BuiltInType.ByteString:
                    WriteVariantMatrixElements(type, values.GetByteStringMatrix());
                    break;
                case BuiltInType.XmlElement:
                    WriteVariantMatrixElements(type, values.GetXmlElementMatrix());
                    break;
                case BuiltInType.NodeId:
                    WriteVariantMatrixElements(type, values.GetNodeIdMatrix());
                    break;
                case BuiltInType.ExpandedNodeId:
                    WriteVariantMatrixElements(type, values.GetExpandedNodeIdMatrix());
                    break;
                case BuiltInType.StatusCode:
                    WriteVariantMatrixElements(type, values.GetStatusCodeMatrix());
                    break;
                case BuiltInType.QualifiedName:
                    WriteVariantMatrixElements(type, values.GetQualifiedNameMatrix());
                    break;
                case BuiltInType.LocalizedText:
                    WriteVariantMatrixElements(type, values.GetLocalizedTextMatrix());
                    break;
                case BuiltInType.ExtensionObject:
                    WriteVariantMatrixElements(type, values.GetExtensionObjectMatrix());
                    break;
                case BuiltInType.DataValue:
                    WriteVariantMatrixElements(type, values.GetDataValueMatrix());
                    break;
                default:
                    throw new NotSupportedException(
                        $"Variant matrix type {type} is not supported by the Protobuf reference encoder."
                    );
            }
        }

        private void WriteVariantElements<T>(int field, BuiltInType type, ArrayOf<T> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                WriteVariantElement(field, type, values.Span[i]);
            }
        }

        private void WriteVariantMatrixElements<T>(BuiltInType type, MatrixOf<T> values)
        {
            foreach (int d in values.Dimensions)
            {
                Proto.WriteTag(Current, 1, 0);
                Proto.WriteSignedVarint(Current, d);
            }

            for (int i = 0; i < values.Count; i++)
            {
                WriteVariantElement(2, type, values.Span[i]);
            }
        }

        private void WriteVariantElement(int field, BuiltInType type, object? value)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
            if (value != null)
            {
                WithFrame(bw, () => WriteObjectAsField1(type, value));
            }

            bw.Flush();
            Proto.WriteTag(Current, field, 2);
            Proto.WriteBytes(Current, ms.ToArray());
        }

        private void WriteObjectAsField1(BuiltInType t, object o)
        {
            switch (t)
            {
                case BuiltInType.Boolean:
                    WriteBoolean("values", (bool)o);
                    break;
                case BuiltInType.SByte:
                    WriteSByte("values", (sbyte)o);
                    break;
                case BuiltInType.Byte:
                    WriteByte("values", (byte)o);
                    break;
                case BuiltInType.Int16:
                    WriteInt16("values", (short)o);
                    break;
                case BuiltInType.UInt16:
                    WriteUInt16("values", (ushort)o);
                    break;
                case BuiltInType.Int32:
                    WriteInt32("values", Convert.ToInt32(o, CultureInfo.InvariantCulture));
                    break;
                case BuiltInType.Enumeration:
                    // Array/matrix enumeration variants box their elements as EnumValue (which does
                    // not implement IConvertible), so extract the integer directly; the scalar path
                    // may still hand us a boxed enum/int, which Convert.ToInt32 handles.
                    WriteInt32(
                        "values",
                        o is EnumValue enumValue
                            ? enumValue.Value
                            : Convert.ToInt32(o, CultureInfo.InvariantCulture));
                    break;
                case BuiltInType.UInt32:
                    WriteUInt32("values", (uint)o);
                    break;
                case BuiltInType.Int64:
                    WriteInt64("values", (long)o);
                    break;
                case BuiltInType.UInt64:
                    WriteUInt64("values", (ulong)o);
                    break;
                case BuiltInType.Float:
                    WriteFloat("values", (float)o);
                    break;
                case BuiltInType.Double:
                    WriteDouble("values", (double)o);
                    break;
                case BuiltInType.String:
                    WriteString("values", (string?)o);
                    break;
                case BuiltInType.DateTime:
                    WriteDateTime("values", (DateTimeUtc)o);
                    break;
                case BuiltInType.Guid:
                    WriteGuid("values", (Uuid)o);
                    break;
                case BuiltInType.ByteString:
                    WriteByteString("values", (ByteString)o);
                    break;
                case BuiltInType.XmlElement:
                    WriteXmlElement("values", (XmlElement)o);
                    break;
                case BuiltInType.NodeId:
                    WriteNodeId("values", (NodeId)o);
                    break;
                case BuiltInType.ExpandedNodeId:
                    WriteExpandedNodeId("values", (ExpandedNodeId)o);
                    break;
                case BuiltInType.StatusCode:
                    WriteStatusCode("values", (StatusCode)o);
                    break;
                case BuiltInType.QualifiedName:
                    WriteQualifiedName("values", (QualifiedName)o);
                    break;
                case BuiltInType.LocalizedText:
                    WriteLocalizedText("values", (LocalizedText)o);
                    break;
                case BuiltInType.ExtensionObject:
                    WriteExtensionObject("values", (ExtensionObject)o);
                    break;
                case BuiltInType.DataValue:
                    WriteDataValue("values", (DataValue)o);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Variant payload type {t} is not supported by the Protobuf reference encoder."
                    );
            }
        }

        private sealed class Frame
        {
            /// <summary>
            /// Initializes a new Frame instance for the experimental OPC UA encoding support.
            /// </summary>
            /// <param name = "writer">The binary writer used by the nested frame.</param>
            public Frame(BinaryWriter writer)
            {
                Writer = writer;
            }

            /// <summary>
            /// Writes r to the experimental encoded representation.
            /// </summary>
            public BinaryWriter Writer;

            /// <summary>
            /// Stores the next positional Protobuf field number assigned by this frame.
            /// </summary>
            public int NextField = 1;

            /// <summary>
            /// Stores the union encoding mask captured for the current frame.
            /// </summary>
            public uint EncodingMask;

            /// <summary>
            /// Stores the union switch captured for the current frame.
            /// </summary>
            public uint UnionSwitch;

            /// <summary>
            /// Returns the Protobuf field number for the supplied OPC UA field name.
            /// </summary>
            /// <param name = "name">The field or column name to assign.</param>
            /// <returns>The result produced by this codec helper.</returns>
            public int Next(string? name)
            {
                return NextField++;
            }
        }

        private readonly Stack<Frame> m_stack = new();
        private readonly Stream m_stream;
        private readonly bool m_leaveOpen;
        private bool m_disposed;
        private ushort[]? m_namespaceMappings;
        private ushort[]? m_serverMappings;
    }
}
