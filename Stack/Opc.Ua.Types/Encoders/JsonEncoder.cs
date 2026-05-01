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

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Writes types as json
    /// </summary>
    public sealed class JsonEncoder : IEncoder
    {
        /// <summary>
        /// Create a new encoder
        /// </summary>
        public JsonEncoder(
            Stream stream,
            IServiceMessageContext context,
            JsonEncoderOptions? options = null)
            : this(context, stream, options)
        {
        }

        /// <summary>
        /// Create a new encoder over a <see cref="IBufferWriter{T}"/>
        /// </summary>
        public JsonEncoder(
            IBufferWriter<byte> writer,
            IServiceMessageContext context,
            JsonEncoderOptions? options = null)
            : this(new Utf8JsonWriter(writer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = options?.Indented ?? false
            }), context, options)
        {
            m_leaveOpen = false;
        }

        /// <summary>
        /// Create a new encoder over internally managed memory
        /// </summary>
        public JsonEncoder(
            IServiceMessageContext context,
            JsonEncoderOptions? options = null)
            : this(context, null, options)
        {
        }

        /// <summary>
        /// Create a new encoder over a <see cref="Utf8JsonWriter"/>
        /// </summary>
        public JsonEncoder(
            Utf8JsonWriter writer,
            IServiceMessageContext context,
            JsonEncoderOptions? options = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<JsonEncoder>();
            m_options = options ?? JsonEncoderOptions.Verbose;
            m_stream = null;
            m_writer = writer;

            StartObject();
        }

        /// <summary>
        /// Create an encoder over a stream or optionally create internal buffer
        /// </summary>
        private JsonEncoder(
            IServiceMessageContext context,
            Stream? stream = null,
            JsonEncoderOptions? options = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<JsonEncoder>();
            m_options = options ?? JsonEncoderOptions.Verbose;

            if (stream == null)
            {
                // TODO: Use pooledBufferWriter instead
                m_stream = new MemoryStream();
                m_leaveOpen = false;
            }
            else
            {
                m_stream = stream;
                m_leaveOpen = true;
            }

            m_writer = new Utf8JsonWriter(m_stream, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = m_options.Indented
            });

            StartObject();
        }

        /// <inheritdoc/>
        public EncodingType EncodingType => EncodingType.Json;

        /// <inheritdoc/>
        public bool CanOmitFields => true;

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                Close();
            }
        }

        /// <inheritdoc/>
        public int Close()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException("Encoder has been disposed already.");
            }

            EndObject();
            m_writer.Flush();
            int length = (int)m_writer.BytesCommitted;

            // If a stream was passed and we should not leave it open dispose it.
            if (m_stream != null && !m_leaveOpen)
            {
                m_stream.Dispose();
            }
            // if a stream was passed and we created a writer - or -
            // if a writer was passed and we should not leve it open, dispose writer
            if (m_stream != null || !m_leaveOpen)
            {
                m_writer.Dispose();
            }
            m_disposed = true;
            return length;
        }

        /// <inheritdoc/>
        public string CloseAndReturnText()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException("Encoder has been disposed already.");
            }
            try
            {
                if (m_stream is not MemoryStream memory)
                {
                    throw new NotSupportedException(
                        "Cannot get text from encoder created with external stream.");
                }
                EndObject();
                m_writer.Flush();
                return Encoding.UTF8.GetString(memory.ToArray());
            }
            finally
            {
                // If a stream was passed and we should not leave it open dispose it.
                if (m_stream != null && !m_leaveOpen)
                {
                    m_stream.Dispose();
                }
                // if a stream was passed and we created a writer - or -
                // if a writer was passed and we should not leve it open, dispose writer
                if (m_stream != null || !m_leaveOpen)
                {
                    m_writer.Dispose();
                }
                m_disposed = true;
            }
        }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri)
        {
        }

        /// <inheritdoc/>
        public void PopNamespace()
        {
        }

        /// <inheritdoc/>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
        {
            m_namespaceMappings = null;

            if (namespaceUris != null && Context.NamespaceUris != null)
            {
                m_namespaceMappings = namespaceUris.CreateMapping(Context.NamespaceUris, false);
            }

            m_serverMappings = null;

            if (serverUris != null && Context.ServerUris != null)
            {
                m_serverMappings = serverUris.CreateMapping(Context.ServerUris, false);
            }
        }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message) where T : IEncodeable, new()
        {
            if (EqualityComparer<T>.Default.Equals(message, default!))
            {
                throw new ArgumentNullException(nameof(message));
            }

            // convert the namespace uri to an index.
            var typeId = ExpandedNodeId.ToNodeId(message.TypeId, Context.NamespaceUris);

            // write the type id.
            WriteNodeId(JsonProperties.UaTypeId, typeId);

            // write the message.
            WriteEncodeable(JsonProperties.UaBody, message);
        }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (EqualityComparer<T>.Default.Equals(message, default!))
            {
                throw new ArgumentNullException(nameof(message));
            }

            // convert the namespace uri to an index.
            var typeId = ExpandedNodeId.ToNodeId(encodeableTypeId, Context.NamespaceUris);

            // write the type id.
            WriteNodeId(JsonProperties.UaTypeId, typeId);

            // write the message.
            WriteEncodeable(JsonProperties.UaBody, message, encodeableTypeId);
        }

        /// <inheritdoc/>
        public void WriteBoolean(string? fieldName, bool value)
        {
            if (WriteValueProperty(fieldName, value))
            {
                m_writer.WriteBooleanValue(value);
            }
        }

        /// <inheritdoc/>
        public void WriteBooleanArray(string? fieldName, ArrayOf<bool> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteBooleanArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteByte(string? fieldName, byte value)
        {
            if (WriteValueProperty(fieldName, value))
            {
                m_writer.WriteNumberValue(value);
            }
        }

        /// <inheritdoc/>
        public void WriteByteString(string? fieldName, ByteString value)
        {
            if (value.IsNull)
            {
                WriteNull(fieldName);
                return;
            }
            CheckByteStringLength(value.Length);
            m_writer.WritePropertyName(fieldName!);
            m_writer.WriteStringValue(value.ToBase64());
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <inheritdoc/>
        public void WriteByteString(string? fieldName, ReadOnlySpan<byte> value)
        {
            WriteByteString(fieldName, ByteString.From(value));
        }
#endif

        /// <inheritdoc/>
        public void WriteByteStringArray(string? fieldName, ArrayOf<ByteString> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteByteStringArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteByteArray(string? fieldName, ArrayOf<byte> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteByteArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteDataValue(string? fieldName, DataValue? value)
        {
            if (value == null)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteDataValue(value);
        }

        /// <inheritdoc/>
        public void WriteDataValueArray(string? fieldName, ArrayOf<DataValue> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteDataValueArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteDateTime(string? fieldName, DateTimeUtc value)
        {
            if (value == default)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteDateTime(value);
        }

        /// <inheritdoc/>
        public void WriteDateTimeArray(string? fieldName, ArrayOf<DateTimeUtc> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteDateTimeArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfo(string? fieldName, DiagnosticInfo? value)
        {
            if (value == null)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteDiagnosticInfo(value, 0);
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfoArray(string? fieldName, ArrayOf<DiagnosticInfo> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteDiagnosticInfoArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteDouble(string? fieldName, double value)
        {
            if (m_options.IgnoreDefaultValues && Math.Abs(value) < double.Epsilon)
            {
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteDouble(value);
        }

        /// <inheritdoc/>
        public void WriteDoubleArray(string? fieldName, ArrayOf<double> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteDoubleArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteEnumerated<T>(string fieldName, T value)
             where T : struct, Enum
        {
            m_writer.WritePropertyName(fieldName!);
            WriteEnumerated(value);
        }

        /// <inheritdoc/>
        public void WriteEnumerated(string? fieldName, EnumValue value)
        {
            m_writer.WritePropertyName(fieldName!);
            WriteEnumerated(value);
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray<T>(string fieldName, ArrayOf<T> values)
             where T : struct, Enum
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteEnumeratedArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray(string? fieldName, ArrayOf<EnumValue> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteEnumeratedArray(values);
            }
        }
        /// <inheritdoc/>
        public void WriteExpandedNodeId(string? fieldName, ExpandedNodeId value)
        {
            if (value.IsNull)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteExpandedNodeId(value);
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeIdArray(string? fieldName,
            ArrayOf<ExpandedNodeId> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteExpandedNodeIdArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteExtensionObject(string? fieldName, ExtensionObject value)
        {
            // check for null.
            if (value.IsNull)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteExtensionObject(value);
        }

        /// <inheritdoc/>
        public void WriteExtensionObjectArray(string? fieldName,
            ArrayOf<ExtensionObject> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteExtensionObjectArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteFloat(string? fieldName, float value)
        {
            if (m_options.IgnoreDefaultValues && Math.Abs(value) < float.Epsilon)
            {
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteFloat(value);
        }

        /// <inheritdoc/>
        public void WriteFloatArray(string? fieldName, ArrayOf<float> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteFloatArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteGuid(string? fieldName, Uuid value)
        {
            if (value == Uuid.Empty)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            m_writer.WriteStringValue(value.ToString());
        }

        /// <inheritdoc/>
        public void WriteGuidArray(string? fieldName, ArrayOf<Uuid> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteGuidArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteInt16(string? fieldName, short value)
        {
            if (WriteValueProperty(fieldName, value))
            {
                m_writer.WriteNumberValue(value);
            }
        }

        /// <inheritdoc/>
        public void WriteInt16Array(string? fieldName, ArrayOf<short> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteInt16Array(values);
            }
        }

        /// <inheritdoc/>
        public void WriteInt32(string? fieldName, int value)
        {
            if (WriteValueProperty(fieldName, value))
            {
                m_writer.WriteNumberValue(value);
            }
        }

        /// <inheritdoc/>
        public void WriteInt32Array(string? fieldName, ArrayOf<int> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteInt32Array(values);
            }
        }

        /// <inheritdoc/>
        public void WriteInt64(string? fieldName, long value)
        {
            if (WriteValueProperty(fieldName, value))
            {
                WriteInt64(value);
            }
        }

        /// <inheritdoc/>
        public void WriteInt64Array(string? fieldName, ArrayOf<long> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteInt64Array(values);
            }
        }

        /// <inheritdoc/>
        public void WriteLocalizedText(string? fieldName, LocalizedText value)
        {
            if (value.IsNullOrEmpty)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteLocalizedText(value);
        }

        /// <inheritdoc/>
        public void WriteLocalizedTextArray(string? fieldName,
            ArrayOf<LocalizedText> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteLocalizedTextArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteNodeId(string? fieldName, NodeId value)
        {
            if (value.IsNull)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteNodeId(value);
        }

        /// <inheritdoc/>
        public void WriteNodeIdArray(string? fieldName, ArrayOf<NodeId> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteNodeIdArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteQualifiedName(string? fieldName, QualifiedName value)
        {
            if (value.IsNull)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteQualifiedName(value);
        }

        /// <inheritdoc/>
        public void WriteQualifiedNameArray(string? fieldName, ArrayOf<QualifiedName> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteQualifiedNameArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteSByte(string? fieldName, sbyte value)
        {
            if (WriteValueProperty(fieldName, value))
            {
                m_writer.WriteNumberValue(value);
            }
        }

        /// <inheritdoc/>
        public void WriteSByteArray(string? fieldName,
            ArrayOf<sbyte> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteSByteArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteStatusCode(string? fieldName, StatusCode value)
        {
            if (value == StatusCodes.Good && m_options.IgnoreDefaultValues)
            {
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteStatusCode(value);
        }

        /// <inheritdoc/>
        public void WriteStatusCodeArray(string? fieldName, ArrayOf<StatusCode> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteStatusCodeArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string fieldName, T value)
            where T : IEncodeable, new()
        {
            WriteEncodeable(fieldName, value, default);
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string fieldName, T value, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (EqualityComparer<T>.Default.Equals(value, default!))
            {
                WriteNull(fieldName);
                return;
            }
            if (!string.IsNullOrEmpty(fieldName))
            {
                m_writer.WritePropertyName(fieldName!);
            }
            StartObject();
            value.Encode(this);
            EndObject();
        }

        /// <inheritdoc/>
        public void WriteEncodeableAsExtensionObject<T>(string fieldName, T value)
            where T : IEncodeable
        {
            if (EqualityComparer<T>.Default.Equals(value, default!))
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteEncodeableAsExtensionObject(value);
        }

        /// <inheritdoc/>
        public void WriteEncodeableArrayAsExtensionObjects<T>(string fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteEncodeableArrayAsExtensionObjects(values);
            }
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string fieldName, ArrayOf<T> values)
            where T : IEncodeable, new()
        {
            WriteEncodeableArray(fieldName, values, default);
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(
            string fieldName,
            ArrayOf<T> values,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteEncodeableArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(string fieldName, MatrixOf<T> values)
            where T : IEncodeable, new()
        {
            WriteEncodeableMatrix(fieldName, values, default);
        }

        /// <inheritdoc/>
        public void WriteEncodeableMatrix<T>(
            string fieldName,
            MatrixOf<T> values,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            if (values.IsNull)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            StartObject();
            WriteInt32Array(JsonProperties.Dimensions, values.Dimensions);
            m_writer.WritePropertyName(JsonProperties.Array);
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteEncodeable(values.Span[i]);
            }
            EndArray();
            EndObject();
        }

        /// <inheritdoc/>
        public void WriteUInt16(string? fieldName, ushort value)
        {
            if (WriteValueProperty(fieldName, value))
            {
                m_writer.WriteNumberValue(value);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt16Array(string? fieldName, ArrayOf<ushort> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteUInt16Array(values);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt32(string? fieldName, uint value)
        {
            if (WriteValueProperty(fieldName, value))
            {
                m_writer.WriteNumberValue(value);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt32Array(string? fieldName, ArrayOf<uint> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteUInt32Array(values);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt64(string? fieldName, ulong value)
        {
            if (WriteValueProperty(fieldName, value))
            {
                WriteUInt64(value);
            }
        }

        /// <inheritdoc/>
        public void WriteUInt64Array(string? fieldName, ArrayOf<ulong> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteUInt64Array(values);
            }
        }

        /// <inheritdoc/>
        public void WriteString(string? fieldName, string? value)
        {
            if (value == null)
            {
                WriteNull(fieldName);
                return;
            }
            CheckStringLength(value.Length);
            m_writer.WritePropertyName(fieldName!);
            m_writer.WriteStringValue(value);
        }

        /// <inheritdoc/>
        public void WriteStringArray(string? fieldName,
            ArrayOf<string> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteStringArray(values);
            }
        }

        /// <inheritdoc/>
        public void WriteVariant(string? fieldName, Variant value)
        {
            if (value.IsNull)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            // Never suppress artifacts when called during structure encoding
            WriteVariant(value, suppressUaType: false);
        }

        /// <inheritdoc/>
        public void WriteVariantArray(string? fieldName, ArrayOf<Variant> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                // Never suppress artifacts when called during structure encoding
                WriteVariantArray(values, suppressUaType: false);
            }
        }

        /// <inheritdoc/>
        public void WriteVariantValue(string? fieldName, Variant value)
        {
            if (m_options.IgnoreDefaultValues && value.ValueIsDefaultOrNull)
            {
                return;
            }
            if (value.IsNull)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteVariantContents(value, true, m_options.SuppressArtifacts);
        }

        /// <inheritdoc/>
        public void WriteXmlElement(string? fieldName, XmlElement value)
        {
            if (value.IsNull)
            {
                WriteNull(fieldName);
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            WriteXmlElement(value);
        }

        /// <inheritdoc/>
        public void WriteXmlElementArray(string? fieldName, ArrayOf<XmlElement> values)
        {
            if (WriteArrayProperty(fieldName, values))
            {
                WriteXmlElementArray(values);
            }
        }

        /// <summary>
        /// Encodes a message in a stream.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="message"/> is <c>null</c>.</exception>
        public static ArraySegment<byte> EncodeMessage<T>(
            T message,
            byte[] buffer,
            IServiceMessageContext context) where T : IEncodeable
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var stream = new MemoryStream(buffer, true);
            using var encoder = new JsonEncoder(stream, context);
            // encode message
            encoder.EncodeMessage(message, message.TypeId);
            int length = encoder.Close();

            return new ArraySegment<byte>(buffer, 0, length);
        }

        /// <summary>
        /// Write boolean values
        /// </summary>
        /// <param name="values"></param>
        private void WriteBooleanArray(ArrayOf<bool> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                m_writer.WriteBooleanValue(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write byte string
        /// </summary>
        /// <param name="value"></param>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteByteString(ByteString value)
        {
            if (value.IsNull)
            {
                m_writer.WriteNullValue();
                return;
            }
            CheckByteStringLength(value.Length);
            m_writer.WriteStringValue(value.ToBase64());
        }

        /// <summary>
        /// Write byte string values
        /// </summary>
        /// <param name="values"></param>
        private void WriteByteStringArray(ArrayOf<ByteString> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteByteString(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write byte values
        /// </summary>
        /// <param name="values"></param>
        private void WriteByteArray(ArrayOf<byte> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                m_writer.WriteNumberValue(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write data value
        /// </summary>
        /// <param name="value"></param>
        private void WriteDataValue(DataValue value)
        {
            if (value == null)
            {
                m_writer.WriteNullValue();
                return;
            }
            StartObject();
            // The DataValue is an encoded variant with extra fields in essence.
            // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.2.18
            // Write the UaType field, Value and then Dimension (last one is
            // written inside the WriteVariantContents.
            if (!value.WrappedValue.TypeInfo.IsUnknown &&
                value.WrappedValue.TypeInfo.BuiltInType != BuiltInType.Null)
            {
                if (!m_options.SuppressArtifacts)
                {
                    WriteVariantUaTypeByte(value.WrappedValue);
                }
                if (!m_options.IgnoreDefaultValues || !value.WrappedValue.ValueIsDefaultOrNull)
                {
                    m_writer.WritePropertyName(JsonProperties.Value);
                    WriteVariantContents(value.WrappedValue, false, m_options.SuppressArtifacts);
                }
            }
            // Now write the remainder of the data value fields
            if (value.StatusCode != StatusCodes.Good)
            {
                WriteStatusCode(JsonProperties.StatusCode, value.StatusCode);
            }
            if (value.SourceTimestamp != DateTimeUtc.MinValue)
            {
                WriteDateTime(JsonProperties.SourceTimestamp, value.SourceTimestamp);
                if (value.SourcePicoseconds != 0)
                {
                    WriteUInt16(JsonProperties.SourcePicoseconds, value.SourcePicoseconds);
                }
            }
            if (value.ServerTimestamp != DateTimeUtc.MinValue)
            {
                WriteDateTime(JsonProperties.ServerTimestamp, value.ServerTimestamp);
                if (value.ServerPicoseconds != 0)
                {
                    WriteUInt16(JsonProperties.ServerPicoseconds, value.ServerPicoseconds);
                }
            }

            EndObject();
        }

        /// <summary>
        /// Write data value values
        /// </summary>
        /// <param name="values"></param>
        private void WriteDataValueArray(ArrayOf<DataValue> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteDataValue(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write date time value
        /// </summary>
        /// <param name="value"></param>
        private void WriteDateTime(DateTimeUtc value)
        {
            // TODO: Optimize using datetimeutc
            //  m_writer.WriteStringValue(value.ToString(
            //      "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
            //      CultureInfo.InvariantCulture));

            if (value <= DateTimeUtc.MinValue)
            {
                m_writer.WriteStringValue("0001-01-01T00:00:00Z");
            }
            else if (value >= DateTimeUtc.MaxValue)
            {
                m_writer.WriteStringValue("9999-12-31T23:59:59Z");
            }
            else
            {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                Span<char> valueString = stackalloc char[DateTimeHelper.DateTimeRoundTripKindLength];
                DateTimeHelper.ConvertUniversalTimeToString((DateTime)value, valueString, out int charsWritten);
                m_writer.WriteStringValue(valueString[..charsWritten]);
#else
                m_writer.WriteStringValue(DateTimeHelper.ConvertUniversalTimeToString((DateTime)value));
#endif
            }
        }

        /// <summary>
        /// Write date time values
        /// </summary>
        private void WriteDateTimeArray(ArrayOf<DateTimeUtc> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteDateTime(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write diagnostic info
        /// </summary>
        private void WriteDiagnosticInfo(DiagnosticInfo value, int depth = 0)
        {
            if (value == null)
            {
                m_writer.WriteNullValue();
                return;
            }

            StartObject();
            if (value.SymbolicId >= 0)
            {
                WriteInt32(JsonProperties.SymbolicId, value.SymbolicId);
            }
            if (value.NamespaceUri >= 0)
            {
                WriteInt32(JsonProperties.NamespaceUri, value.NamespaceUri);
            }
            if (value.Locale >= 0)
            {
                WriteInt32(JsonProperties.Locale, value.Locale);
            }
            if (value.LocalizedText >= 0)
            {
                WriteInt32(JsonProperties.LocalizedText, value.LocalizedText);
            }
            if (value.AdditionalInfo != null)
            {
                WriteString(JsonProperties.AdditionalInfo, value.AdditionalInfo);
            }
            if (value.InnerStatusCode != StatusCodes.Good)
            {
                WriteStatusCode(JsonProperties.InnerStatusCode, value.InnerStatusCode);
            }
            if (value.InnerDiagnosticInfo != null)
            {
                if (depth < DiagnosticInfo.MaxInnerDepth)
                {
                    m_writer.WritePropertyName(JsonProperties.InnerDiagnosticInfo);
                    WriteDiagnosticInfo(value, ++depth);
                }
                else
                {
                    m_logger.LogWarning(
                        "InnerDiagnosticInfo dropped because nesting exceeds maximum of {MaxInnerDepth}.",
                        DiagnosticInfo.MaxInnerDepth);
                }
            }
            EndObject();
        }

        /// <summary>
        /// Write diagnostic info values
        /// </summary>
        private void WriteDiagnosticInfoArray(ArrayOf<DiagnosticInfo> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteDiagnosticInfo(values.Span[i], 0);
            }
            EndArray();
        }

        /// <summary>
        /// Write double
        /// </summary>
        private void WriteDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                m_writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
                return;
            }
#if NET8_0_OR_GREATER
            m_writer.WriteNumberValue(value);
#else
            m_writer.WriteRawValue(value.ToString("R", CultureInfo.InvariantCulture));
#endif
        }

        /// <summary>
        /// Write double values
        /// </summary>
        private void WriteDoubleArray(ArrayOf<double> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteDouble(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write enumeration
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private void WriteEnumerated<T>(T value) where T : struct, Enum
        {
            int numeric = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (m_options.EnumerationAsNumber)
            {
                m_writer.WriteNumberValue(numeric);
                return;
            }
            m_writer.WriteStringValue(CoreUtils.Format("{0}_{1}", value, numeric));
        }

        /// <summary>
        /// Write enumeration as integer
        /// </summary>
        private void WriteEnumerated(EnumValue value)
        {
            int numeric = value.Value;
            if (m_options.EnumerationAsNumber)
            {
                m_writer.WriteNumberValue(numeric);
                return;
            }
            string? symbol = value.Symbol;
            if (string.IsNullOrEmpty(symbol))
            {
                m_writer.WriteStringValue(CoreUtils.Format("{0}", numeric));
                return;
            }
            m_writer.WriteStringValue(CoreUtils.Format("{0}_{1}", symbol, numeric));
        }

        /// <summary>
        /// Write enumeration values
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private void WriteEnumeratedArray<T>(ArrayOf<T> values) where T : struct, Enum
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteEnumerated(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write enumeration values as integers
        /// </summary>
        private void WriteEnumeratedArray(ArrayOf<EnumValue> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteEnumerated(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write node id
        /// </summary>
        private void WriteExpandedNodeId(in ExpandedNodeId value)
        {
            if (value.IsNull)
            {
                m_writer.WriteNullValue();
                return;
            }

            m_writer.WriteStringValue(value.Format(Context, m_options.ForceNamespaceUri));
        }

        /// <summary>
        /// Write expanded node id values
        /// </summary>
        private void WriteExpandedNodeIdArray(ArrayOf<ExpandedNodeId> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteExpandedNodeId(in values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write extension object
        /// </summary>
        private void WriteExtensionObject(ExtensionObject value)
        {
            if (value.IsNull)
            {
                m_writer.WriteNullValue();
                return;
            }
            value.TryGetEncodeable(out IEncodeable encodeable);
            ExpandedNodeId typeId = !value.TypeId.IsNull
                ? value.TypeId
                : encodeable?.TypeId ?? NodeId.Null;
            var localTypeId = ExpandedNodeId.ToNodeId(typeId, Context.NamespaceUris);

            StartObject();

            if (!m_options.SuppressArtifacts && !localTypeId.IsNull)
            {
                WriteNodeId(JsonProperties.UaTypeId, localTypeId);
            }
            switch (value.Encoding)
            {
                case ExtensionObjectEncoding.Json:
                    if (!value.TryGetAsJson(out string rawJson))
                    {
                        break;
                    }
                    // Write inline without object curly braces
                    rawJson = rawJson.Trim();
                    if (rawJson.Length > 1 && rawJson[0] == '{' && rawJson[^1] == '}')
                    {
                        m_writer.WriteRawValue(rawJson[1..^1]);
                    }
                    break;
                case ExtensionObjectEncoding.Binary:
                    WriteByte(JsonProperties.UaEncoding, (byte)ExtensionObjectEncoding.Binary);
                    WriteByteString(
                        JsonProperties.UaBody,
                        value.TryGetAsBinary(out ByteString b) ? b : default);
                    break;
                case ExtensionObjectEncoding.Xml:
                    WriteByte(JsonProperties.UaEncoding, (byte)ExtensionObjectEncoding.Xml);
                    WriteXmlElement(
                        JsonProperties.UaBody,
                        value.TryGetAsXml(out XmlElement x) ? x : default);
                    break;
                default:
                    // Encode inline into the current object or do nothing here
                    encodeable?.Encode(this);
                    break;
            }

            EndObject();
        }

        /// <summary>
        /// Write extension object values
        /// </summary>
        private void WriteExtensionObjectArray(ArrayOf<ExtensionObject> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteExtensionObject(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write float
        /// </summary>
        private void WriteFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                m_writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
                return;
            }
#if NET8_0_OR_GREATER
            m_writer.WriteNumberValue(value);
#else
            m_writer.WriteRawValue(value.ToString("R", CultureInfo.InvariantCulture));
#endif
        }

        /// <summary>
        /// Write float values
        /// </summary>
        private void WriteFloatArray(ArrayOf<float> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteFloat(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write guid
        /// </summary>
        private void WriteGuid(Uuid value)
        {
            if (value == Uuid.Empty)
            {
                m_writer.WriteNullValue();
                return;
            }
            m_writer.WriteStringValue(value.ToString());
        }

        /// <summary>
        /// Write guid values
        /// </summary>
        private void WriteGuidArray(ArrayOf<Uuid> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteGuid(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write short values
        /// </summary>
        private void WriteInt16Array(ArrayOf<short> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                m_writer.WriteNumberValue(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write integer values
        /// </summary>
        private void WriteInt32Array(ArrayOf<int> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                m_writer.WriteNumberValue(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write long
        /// </summary>
        private void WriteInt64(long value)
        {
            m_writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Write long values
        /// </summary>
        private void WriteInt64Array(ArrayOf<long> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteInt64(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write localized text
        /// </summary>
        private void WriteLocalizedText(LocalizedText value)
        {
            if (value.IsNullOrEmpty)
            {
                m_writer.WriteNullValue();
                return;
            }
            StartObject();
            WriteString(JsonProperties.Text, value.Text);
            if (!string.IsNullOrEmpty(value.Locale))
            {
                WriteString(JsonProperties.Locale, value.Locale);
            }
            EndObject();
        }

        /// <summary>
        /// Write localized text values
        /// </summary>
        private void WriteLocalizedTextArray(ArrayOf<LocalizedText> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteLocalizedText(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write node id
        /// </summary>
        private void WriteNodeId(NodeId value)
        {
            if (value.IsNull)
            {
                m_writer.WriteNullValue();
                return;
            }

            m_writer.WriteStringValue(value.Format(Context, m_options.ForceNamespaceUri));
        }

        /// <summary>
        /// Write node id values
        /// </summary>
        private void WriteNodeIdArray(ArrayOf<NodeId> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteNodeId(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write qualified name
        /// </summary>
        private void WriteQualifiedName(QualifiedName value)
        {
            if (value.IsNull)
            {
                m_writer.WriteNullValue();
                return;
            }

            WriteString(value.Format(Context, m_options.ForceNamespaceUri));
        }

        /// <summary>
        /// Write qualified name values
        /// </summary>
        private void WriteQualifiedNameArray(ArrayOf<QualifiedName> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteQualifiedName(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write sbyte values
        /// </summary>
        private void WriteSByteArray(ArrayOf<sbyte> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                m_writer.WriteNumberValue(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write status code
        /// </summary>
        private void WriteStatusCode(StatusCode value)
        {
            // This is intentional, StatusCode type is not nullable and
            // the default (Good) is represented by an empty object
            // see https://reference.opcfoundation.org/Core/Part6/v105/docs/5.1.2
            StartObject();
            if (value != StatusCodes.Good)
            {
                WriteUInt32(JsonProperties.Code, value.Code);
                if (m_options == JsonEncoderOptions.Verbose)
                {
                    string symbolicId = value.SymbolicId;
                    if (!string.IsNullOrEmpty(symbolicId))
                    {
                        WriteString(JsonProperties.Symbol, symbolicId);
                    }
                }
            }
            EndObject();
        }

        /// <summary>
        /// Write status code values
        /// </summary>
        private void WriteStatusCodeArray(ArrayOf<StatusCode> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteStatusCode(values.Span[i]);
            }
            EndArray();
        }

        /// <inheritdoc/>
        public void WriteSwitchField(uint switchField, out string? fieldName)
        {
            fieldName = null;
            if (!m_options.IgnoreUnionSwitchField && !m_options.SuppressArtifacts)
            {
                WriteUInt32(JsonProperties.SwitchField, switchField);
            }
        }

        /// <inheritdoc/>
        public void WriteEncodingMask(uint encodingMask)
        {
            if (!m_options.IgnoreOptionalFieldEncodingMask && !m_options.SuppressArtifacts)
            {
                WriteUInt32(JsonProperties.EncodingMask, encodingMask);
            }
        }

        /// <summary>
        /// Write structure
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteEncodeable<T>(T value)
            where T : IEncodeable
        {
            StartObject();
            value.Encode(this);
            EndObject();
        }

        /// <summary>
        /// Write structure as extension object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private void WriteEncodeableAsExtensionObject<T>(T value) where T : IEncodeable
        {
            StartObject();
            var typeId = ExpandedNodeId.ToNodeId(value.TypeId, Context.NamespaceUris);
            // Disregard RawDataMode as per 5.4.1 (encoding of Structure or Variant)
            if (!typeId.IsNull)
            {
                WriteNodeId(JsonProperties.UaTypeId, typeId);
            }
            value.Encode(this);
            EndObject();
        }

        /// <summary>
        /// Write structure as extension object values
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private void WriteEncodeableArrayAsExtensionObjects<T>(ArrayOf<T> values)
            where T : IEncodeable
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteEncodeableAsExtensionObject(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write structure values
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private void WriteEncodeableArray<T>(ArrayOf<T> values) where T : IEncodeable
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteEncodeable(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write unsigned short values
        /// </summary>
        private void WriteUInt16Array(ArrayOf<ushort> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                m_writer.WriteNumberValue(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write unsigned integer values
        /// </summary>
        private void WriteUInt32Array(ArrayOf<uint> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                m_writer.WriteNumberValue(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write unsigned long
        /// </summary>
        private void WriteUInt64(ulong value)
        {
            m_writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Write unsigned long values
        /// </summary>
        private void WriteUInt64Array(ArrayOf<ulong> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteUInt64(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write string
        /// </summary>
        private void WriteString(string value)
        {
            if (value == null)
            {
                m_writer.WriteNullValue();
                return;
            }
            CheckStringLength(value.Length);
            m_writer.WriteStringValue(value);
        }

        /// <summary>
        /// Write string values
        /// </summary>
        private void WriteStringArray(ArrayOf<string> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteString(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write variant
        /// </summary>
        private void WriteVariant(Variant value, bool suppressUaType)
        {
            if (value.IsNull)
            {
                m_writer.WriteNullValue();
                return;
            }
            StartObject();
            if (!suppressUaType)
            {
                WriteVariantUaTypeByte(value);
            }
            if (!m_options.IgnoreDefaultValues || !value.ValueIsDefaultOrNull)
            {
                m_writer.WritePropertyName(JsonProperties.Value);
                WriteVariantContents(value, false, suppressUaType);
            }
            EndObject();
        }

        /// <summary>
        /// Write variant values
        /// </summary>
        private void WriteVariantArray(ArrayOf<Variant> values, bool suppressUaType)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteVariant(values.Span[i], suppressUaType);
            }
            EndArray();
        }

        /// <summary>
        /// Write xml element
        /// </summary>
        private void WriteXmlElement(XmlElement value)
        {
            if (value.IsEmpty)
            {
                m_writer.WriteNullValue();
                return;
            }
            WriteString(value.OuterXml!);
        }

        /// <summary>
        /// Write xml element values
        /// </summary>
        private void WriteXmlElementArray(ArrayOf<XmlElement> values)
        {
            StartArray(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteXmlElement(values.Span[i]);
            }
            EndArray();
        }

        /// <summary>
        /// Write null
        /// </summary>
        private void WriteNull(string? fieldName)
        {
            if (m_options.IgnoreNullValues || m_options.IgnoreDefaultValues)
            {
                return;
            }
            m_writer.WritePropertyName(fieldName!);
            m_writer.WriteNullValue();
        }

        /// <summary>
        /// Writes the contents of a Variant to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteVariantContents(
            Variant value,
            bool writeRawValue,
            bool suppressUaType)
        {
            // write scalar.
            if (value.TypeInfo.IsScalar)
            {
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Null:
                        m_writer.WriteNullValue();
                        return;
                    case BuiltInType.Boolean:
                        m_writer.WriteBooleanValue(value.GetBoolean());
                        return;
                    case BuiltInType.SByte:
                        m_writer.WriteNumberValue(value.GetSByte());
                        return;
                    case BuiltInType.Byte:
                        m_writer.WriteNumberValue(value.GetByte());
                        return;
                    case BuiltInType.Int16:
                        m_writer.WriteNumberValue(value.GetInt16());
                        return;
                    case BuiltInType.UInt16:
                        m_writer.WriteNumberValue(value.GetUInt16());
                        return;
                    case BuiltInType.Enumeration when writeRawValue:
                        WriteEnumerated(value.GetEnumeration());
                        break;
                    // see https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.4
                    case BuiltInType.Enumeration:
                    case BuiltInType.Int32:
                        m_writer.WriteNumberValue(value.GetInt32());
                        return;
                    case BuiltInType.UInt32:
                        m_writer.WriteNumberValue(value.GetUInt32());
                        return;
                    case BuiltInType.Int64:
                        WriteInt64(value.GetInt64());
                        return;
                    case BuiltInType.UInt64:
                        WriteUInt64(value.GetUInt64());
                        return;
                    case BuiltInType.Float:
                        WriteFloat(value.GetFloat());
                        return;
                    case BuiltInType.Double:
                        WriteDouble(value.GetDouble());
                        return;
                    case BuiltInType.String:
                        WriteString(value.GetString());
                        return;
                    case BuiltInType.DateTime:
                        WriteDateTime(value.GetDateTime());
                        return;
                    case BuiltInType.Guid:
                        WriteGuid(value.GetGuid());
                        return;
                    case BuiltInType.ByteString:
                        WriteByteString(value.GetByteString());
                        return;
                    case BuiltInType.XmlElement:
                        WriteXmlElement(value.GetXmlElement());
                        return;
                    case BuiltInType.NodeId:
                        WriteNodeId(value.GetNodeId());
                        return;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeId(value.GetExpandedNodeId());
                        return;
                    case BuiltInType.StatusCode:
                        WriteStatusCode(value.GetStatusCode());
                        return;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedName(value.GetQualifiedName());
                        return;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedText(value.GetLocalizedText());
                        return;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObject(value.GetExtensionObject());
                        return;
                    case BuiltInType.DataValue:
                        WriteDataValue(value.GetDataValue());
                        return;
                    case BuiltInType.Variant:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.DiagnosticInfo:
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Variant: {0}",
                            value.TypeInfo.BuiltInType);
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {value.TypeInfo.BuiltInType}");
                }
            }
            // write array
            else if (value.TypeInfo.IsArray)
            {
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        WriteBooleanArray(value.GetBooleanArray());
                        break;
                    case BuiltInType.SByte:
                        WriteSByteArray(value.GetSByteArray());
                        break;
                    case BuiltInType.Byte:
                        WriteByteArray(value.GetByteArray());
                        break;
                    case BuiltInType.Int16:
                        WriteInt16Array(value.GetInt16Array());
                        break;
                    case BuiltInType.UInt16:
                        WriteUInt16Array(value.GetUInt16Array());
                        break;
                    case BuiltInType.Enumeration when writeRawValue:
                        WriteEnumeratedArray(value.GetEnumerationArray());
                        break;
                    case BuiltInType.Enumeration:
                    case BuiltInType.Int32:
                        WriteInt32Array(value.GetInt32Array());
                        break;
                    case BuiltInType.UInt32:
                        WriteUInt32Array(value.GetUInt32Array());
                        break;
                    case BuiltInType.Int64:
                        WriteInt64Array(value.GetInt64Array());
                        break;
                    case BuiltInType.UInt64:
                        WriteUInt64Array(value.GetUInt64Array());
                        break;
                    case BuiltInType.Float:
                        WriteFloatArray(value.GetFloatArray());
                        break;
                    case BuiltInType.Double:
                        WriteDoubleArray(value.GetDoubleArray());
                        break;
                    case BuiltInType.String:
                        WriteStringArray(value.GetStringArray());
                        break;
                    case BuiltInType.DateTime:
                        WriteDateTimeArray(value.GetDateTimeArray());
                        break;
                    case BuiltInType.Guid:
                        WriteGuidArray(value.GetGuidArray());
                        break;
                    case BuiltInType.ByteString:
                        WriteByteStringArray(value.GetByteStringArray());
                        break;
                    case BuiltInType.XmlElement:
                        WriteXmlElementArray(value.GetXmlElementArray());
                        break;
                    case BuiltInType.NodeId:
                        WriteNodeIdArray(value.GetNodeIdArray());
                        break;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeIdArray(value.GetExpandedNodeIdArray());
                        break;
                    case BuiltInType.StatusCode:
                        WriteStatusCodeArray(value.GetStatusCodeArray());
                        break;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedNameArray(value.GetQualifiedNameArray());
                        break;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedTextArray(value.GetLocalizedTextArray());
                        break;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObjectArray(value.GetExtensionObjectArray());
                        break;
                    case BuiltInType.DataValue:
                        WriteDataValueArray(value.GetDataValueArray());
                        break;
                    case BuiltInType.Variant:
                        WriteVariantArray(value.GetVariantArray(), suppressUaType);
                        break;
                    case BuiltInType.DiagnosticInfo:
                    case BuiltInType.Null:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Variant: {0}",
                            value.TypeInfo.BuiltInType);
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {value.TypeInfo.BuiltInType}");
                }
            }
            // Write multi dimension
            else
            {
                int[] dim;
                if (writeRawValue)
                {
                    // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.5
                    StartObject();
                    m_writer.WritePropertyName(JsonProperties.Array);
                }
                switch (value.TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        WriteBooleanArray(value.GetBooleanMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.SByte:
                        WriteSByteArray(value.GetSByteMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Byte:
                        WriteByteArray(value.GetByteMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Int16:
                        WriteInt16Array(value.GetInt16Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.UInt16:
                        WriteUInt16Array(value.GetUInt16Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Enumeration when writeRawValue:
                        WriteEnumeratedArray(value.GetEnumerationMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Enumeration:
                    case BuiltInType.Int32:
                        WriteInt32Array(value.GetInt32Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.UInt32:
                        WriteUInt32Array(value.GetUInt32Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Int64:
                        WriteInt64Array(value.GetInt64Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.UInt64:
                        WriteUInt64Array(value.GetUInt64Matrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Float:
                        WriteFloatArray(value.GetFloatMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Double:
                        WriteDoubleArray(value.GetDoubleMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.String:
                        WriteStringArray(value.GetStringMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.DateTime:
                        WriteDateTimeArray(value.GetDateTimeMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Guid:
                        WriteGuidArray(value.GetGuidMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.ByteString:
                        WriteByteStringArray(value.GetByteStringMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.XmlElement:
                        WriteXmlElementArray(value.GetXmlElementMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.NodeId:
                        WriteNodeIdArray(value.GetNodeIdMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeIdArray(value.GetExpandedNodeIdMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.StatusCode:
                        WriteStatusCodeArray(value.GetStatusCodeMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedNameArray(value.GetQualifiedNameMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedTextArray(value.GetLocalizedTextMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObjectArray(value.GetExtensionObjectMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.DataValue:
                        WriteDataValueArray(value.GetDataValueMatrix().ToArrayOf(out dim));
                        break;
                    case BuiltInType.Variant:
                        WriteVariantArray(value.GetVariantMatrix().ToArrayOf(out dim), suppressUaType);
                        break;
                    case BuiltInType.DiagnosticInfo:
                    case BuiltInType.Null:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding a Variant: {0}",
                            value.TypeInfo.BuiltInType);
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {value.TypeInfo.BuiltInType}");
                }

                WriteInt32Array(JsonProperties.Dimensions, dim);
                if (writeRawValue)
                {
                    EndObject();
                }
            }
        }

        /// <summary>
        /// Write the UaType byte
        /// </summary>
        /// <param name="value"></param>
        private void WriteVariantUaTypeByte(Variant value)
        {
            if (!value.IsNull &&
                value.TypeInfo.BuiltInType != BuiltInType.Null)
            {
                byte uaType = (byte)value.TypeInfo.BuiltInType;
                if (uaType == (byte)BuiltInType.Enumeration)
                {
                    uaType = (byte)BuiltInType.Int32;
                }
                WriteByte(JsonProperties.UaType, uaType);
            }
        }

        /// <summary>
        /// Check whether to write the simple value and if so write proeprty.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>true if should write value.</returns>
        private bool WriteValueProperty<T>(string? fieldName, T value)
            where T : struct
        {
            if (m_options.IgnoreDefaultValues &&
                EqualityComparer<T>.Default.Equals(value, default))
            {
                return false;
            }
            m_writer.WritePropertyName(fieldName!);
            return true;
        }

        /// <summary>
        /// Check whether to write the array value. If it is null, write null.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>true if should write value.</returns>
        private bool WriteArrayProperty<T>(string? fieldName, ArrayOf<T> values)
        {
            if (values.IsNull)
            {
                WriteNull(fieldName);
                return false;
            }
            m_writer.WritePropertyName(fieldName!);
            return true;
        }

        /// <summary>
        /// Start new object
        /// </summary>
        internal void StartObject()
        {
            CheckNestingLevel();
            m_writer.WriteStartObject();
        }

        /// <summary>
        /// End structure
        /// </summary>
        internal void EndObject()
        {
            m_writer.WriteEndObject();
        }

        /// <summary>
        /// Start new array
        /// </summary>
        /// <param name="count"></param>
        private void StartArray(int count)
        {
            CheckArrayLength(count);
            m_writer.WriteStartArray();
        }

        /// <summary>
        /// End array
        /// </summary>
        private void EndArray()
        {
            m_writer.WriteEndArray();
        }

        /// <summary>
        /// Check array length
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckArrayLength(int length)
        {
            if (length > Context.MaxArrayLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    $"MaxArrayLength {Context.MaxArrayLength} < {length}");
            }
        }

        /// <summary>
        /// Check byte string
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckByteStringLength(int length)
        {
            if (length > Context.MaxByteStringLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    $"{length} bytes > max (= {Context.MaxByteStringLength})");
            }
        }

        /// <summary>
        /// Check nesting
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckNestingLevel()
        {
            // check the nesting level for avoiding a stack overflow.
            if (m_writer.CurrentDepth > Context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    $"Maximum nesting level of {Context.MaxEncodingNestingLevels} exceeded.");
            }
        }

        /// <summary>
        /// Check string
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckStringLength(int length)
        {
            if (length > Context.MaxStringLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    $"{length} characters > max (= {Context.MaxStringLength})");
            }
        }

        private readonly Stream? m_stream;
        private readonly bool m_leaveOpen;
        private readonly ILogger m_logger;
        private readonly JsonEncoderOptions m_options;
        private readonly Utf8JsonWriter m_writer;
        private ushort[]? m_namespaceMappings;
        private ushort[]? m_serverMappings;
        private bool m_disposed;
    }
}
