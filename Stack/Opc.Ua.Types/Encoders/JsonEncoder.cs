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
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Opc.Ua.Types;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
using System.Buffers;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Writes objects to a JSON stream.
    /// </summary>
    public class JsonEncoder : IJsonEncoder
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public JsonEncoder(
            IServiceMessageContext context,
            JsonEncodingType encoding,
            bool topLevelIsArray = false,
            Stream stream = null,
            bool leaveOpen = false,
            int streamSize = kStreamWriterBufferSize)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<JsonEncoder>();
            m_stream = stream;
            m_leaveOpen = leaveOpen;
            m_topLevelIsArray = topLevelIsArray;
            EncodingToUse = encoding;
            IncludeDefaultValues = encoding == JsonEncodingType.Verbose;
            m_includeDefaultNumberValues = encoding == JsonEncodingType.Verbose;
            m_inVariantWithEncoding = IncludeDefaultValues;

            if (m_stream == null)
            {
                m_memoryStream = new MemoryStream();
                m_writer = new StreamWriter(m_memoryStream, s_utf8Encoding, streamSize, false);
                m_leaveOpen = false;
            }
            else
            {
                m_writer = new StreamWriter(m_stream, s_utf8Encoding, streamSize, m_leaveOpen);
            }

            InitializeWriter();
        }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public JsonEncoder(
            IServiceMessageContext context,
            JsonEncodingType encoding,
            StreamWriter writer,
            bool topLevelIsArray = false)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<JsonEncoder>();
            m_writer = writer;
            m_topLevelIsArray = topLevelIsArray;
            EncodingToUse = encoding;
            IncludeDefaultValues = encoding == JsonEncodingType.Verbose;
            m_includeDefaultNumberValues = encoding == JsonEncodingType.Verbose;
            m_inVariantWithEncoding = IncludeDefaultValues;

            if (m_writer == null)
            {
                m_stream = new MemoryStream();
                m_writer = new StreamWriter(m_stream, s_utf8Encoding, kStreamWriterBufferSize);
            }

            InitializeWriter();
        }

        /// <summary>
        /// Initialize Writer.
        /// </summary>
        private void InitializeWriter()
        {
            if (m_topLevelIsArray)
            {
                m_writer.Write(kLeftSquareBracket);
            }
            else
            {
                m_writer.Write(kLeftCurlyBrace);
            }
        }

        /// <summary>
        /// Encodes a message in a stream.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        public static ArraySegment<byte> EncodeMessage(
            IEncodeable message,
            byte[] buffer,
            IServiceMessageContext context)
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
            using var encoder = new JsonEncoder(context, JsonEncodingType.Verbose, false, stream);
            // encode message
            encoder.EncodeMessage(message);
            int length = encoder.Close();

            return new ArraySegment<byte>(buffer, 0, length);
        }

        /// <inheritdoc/>
        public void EncodeMessage<T>(T message) where T : IEncodeable
        {
            if (EqualityComparer<T>.Default.Equals(message, default))
            {
                throw new ArgumentNullException(nameof(message));
            }

            // convert the namespace uri to an index.
            var typeId = ExpandedNodeId.ToNodeId(message.TypeId, Context.NamespaceUris);

            // write the type id.
            WriteNodeId("TypeId", typeId);

            // write the message.
            WriteEncodeable("Body", message);
        }

        /// <summary>
        /// Initializes the tables used to map namespace and server uris during encoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being encoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being encoded.</param>
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

        /// <summary>
        /// Completes writing and returns the JSON text.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public string CloseAndReturnText()
        {
            try
            {
                InternalClose(false);
                if (m_memoryStream == null)
                {
                    if (m_stream is MemoryStream memoryStream)
                    {
                        return Encoding.UTF8.GetString(memoryStream.ToArray());
                    }
                    throw new NotSupportedException(
                        "Cannot get text from external stream. Use Close or MemoryStream instead.");
                }
                return Encoding.UTF8.GetString(m_memoryStream.ToArray());
            }
            finally
            {
                m_writer?.Dispose();
                m_writer = null;
            }
        }

        /// <summary>
        /// Completes writing and returns the text length.
        /// The StreamWriter is disposed.
        /// </summary>
        public int Close()
        {
            return InternalClose(true);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_writer != null)
                {
                    InternalClose(true);
                    m_writer = null;
                }

                if (!m_leaveOpen)
                {
                    CoreUtils.SilentDispose(m_memoryStream);
                    CoreUtils.SilentDispose(m_stream);
                    m_memoryStream = null;
                    m_stream = null;
                }
            }
        }

        /// <inheritdoc/>
        public JsonEncodingType EncodingToUse { get; private set; }

        /// <inheritdoc/>
        public bool SuppressArtifacts { get; set; }

        /// <inheritdoc/>
        public EncodingType EncodingType => EncodingType.Json;

        /// <summary>
        /// The message context associated with the encoder.
        /// </summary>
        public IServiceMessageContext Context { get; }

        /// <inheritdoc/>
        public bool ForceNamespaceUri { get; set; } = true;

        /// <summary>
        /// The Json encoder default value option.
        /// </summary>
        public bool IncludeDefaultValues { get; }

        /// <summary>
        /// The Json encoder default value option for numbers.
        /// </summary>
        public bool IncludeDefaultNumberValues
            => m_includeDefaultNumberValues || IncludeDefaultValues;

        /// <inheritdoc/>
        public void UsingAlternateEncoding<T>(
            Action<string, T> action,
            string fieldName,
            T value,
            JsonEncodingType useEncodingType)
        {
            JsonEncodingType currentValue = EncodingToUse;
            try
            {
                EncodingToUse = useEncodingType;
                action(fieldName, value);
            }
            finally
            {
                EncodingToUse = currentValue;
            }
        }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri)
        {
            m_namespaces.Push(namespaceUri);
        }

        /// <inheritdoc/>
        public void PopNamespace()
        {
            m_namespaces.Pop();
        }

        /// <inheritdoc/>
        public void WriteBoolean(string fieldName, bool value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && !value)
            {
                return;
            }

            if (value)
            {
                WriteSimpleField(fieldName, "true");
            }
            else
            {
                WriteSimpleField(fieldName, "false");
            }
        }

        /// <inheritdoc/>
        public void WriteSByte(string fieldName, sbyte value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public void WriteByte(string fieldName, byte value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public void WriteInt16(string fieldName, short value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public void WriteUInt16(string fieldName, ushort value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public void WriteInt32(string fieldName, int value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public void WriteUInt32(string fieldName, uint value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public void WriteInt64(string fieldName, long value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(
                fieldName,
                value.ToString(CultureInfo.InvariantCulture),
                EscapeOptions.Quotes);
        }

        /// <inheritdoc/>
        public void WriteUInt64(string fieldName, ulong value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(
                fieldName,
                value.ToString(CultureInfo.InvariantCulture),
                EscapeOptions.Quotes);
        }

        /// <inheritdoc/>
        public void WriteFloat(string fieldName, float value)
        {
            if (fieldName != null &&
                !IncludeDefaultNumberValues &&
                (value > -float.Epsilon) &&
                (value < float.Epsilon))
            {
                return;
            }

            if (float.IsNaN(value))
            {
                WriteSimpleField(fieldName, "\"NaN\"");
            }
            else if (float.IsPositiveInfinity(value))
            {
                WriteSimpleField(fieldName, "\"Infinity\"");
            }
            else if (float.IsNegativeInfinity(value))
            {
                WriteSimpleField(fieldName, "\"-Infinity\"");
            }
            else
            {
                WriteSimpleField(fieldName, value.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        /// <inheritdoc/>
        public void WriteDouble(string fieldName, double value)
        {
            if (fieldName != null &&
                !IncludeDefaultNumberValues &&
                (value > -double.Epsilon) &&
                (value < double.Epsilon))
            {
                return;
            }

            if (double.IsNaN(value))
            {
                WriteSimpleField(fieldName, "\"NaN\"");
            }
            else if (double.IsPositiveInfinity(value))
            {
                WriteSimpleField(fieldName, "\"Infinity\"");
            }
            else if (double.IsNegativeInfinity(value))
            {
                WriteSimpleField(fieldName, "\"-Infinity\"");
            }
            else
            {
                WriteSimpleField(fieldName, value.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        /// <inheritdoc/>
        public void WriteString(string fieldName, string value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == null)
            {
                return;
            }

            WriteSimpleField(fieldName, value, EscapeOptions.Quotes);
        }

        /// <inheritdoc/>
        public void WriteDateTime(string fieldName, DateTime value)
        {
            WriteDateTime(fieldName, value, EscapeOptions.None);
        }

        /// <inheritdoc/>
        public void WriteGuid(string fieldName, Uuid value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == Uuid.Empty)
            {
                return;
            }

            WriteSimpleField(
                fieldName,
                value.ToString(),
                EscapeOptions.Quotes | EscapeOptions.NoValueEscape);
        }

        /// <inheritdoc/>
        public void WriteByteString(string fieldName, ByteString value)
        {
            if (fieldName != null && !IncludeDefaultValues && value.IsNull)
            {
                return;
            }

            if (value.IsNull)
            {
                WriteSimpleField(fieldName, kNull, EscapeOptions.NoValueEscape);
                return;
            }

            // check the length.
            if (Context.MaxByteStringLength > 0 &&
                Context.MaxByteStringLength < value.Length)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            WriteSimpleField(
                fieldName,
                value.ToBase64(),
                EscapeOptions.Quotes | EscapeOptions.NoValueEscape);
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteString(string fieldName, ReadOnlySpan<byte> value)
        {
            // == compares memory reference, comparing to empty means we compare to the default
            // If null array is converted to span the span is default
            bool isNull = value == ReadOnlySpan<byte>.Empty;

            if (fieldName != null && !IncludeDefaultValues && isNull)
            {
                return;
            }

            if (isNull)
            {
                WriteSimpleField(fieldName, kNull, EscapeOptions.NoValueEscape);
                return;
            }

            // check the length.
            if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < value.Length)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            if (value.Length > 0)
            {
                const int maxStackLimit = 1024;
                int length = (value.Length + 2) / 3 * 4;
                char[] arrayPool = null;
                Span<char> chars =
                    length <= maxStackLimit
                        ? stackalloc char[length]
                        : (arrayPool = ArrayPool<char>.Shared.Rent(length)).AsSpan(0, length);
                try
                {
                    bool success = Convert.TryToBase64Chars(
                        value,
                        chars,
                        out int charsWritten,
                        Base64FormattingOptions.None);
                    if (success)
                    {
                        WriteSimpleFieldAsSpan(
                            fieldName,
                            chars[..charsWritten],
                            EscapeOptions.Quotes | EscapeOptions.NoValueEscape);
                        return;
                    }

                    throw new ServiceResultException(
                        StatusCodes.BadEncodingError,
                        "Failed to convert ByteString to Base64");
                }
                finally
                {
                    if (arrayPool != null)
                    {
                        ArrayPool<char>.Shared.Return(arrayPool);
                    }
                }
            }

            WriteSimpleField(
                fieldName,
                string.Empty,
                EscapeOptions.Quotes | EscapeOptions.NoValueEscape);
        }
#endif

        /// <inheritdoc/>
        public void WriteXmlElement(string fieldName, XmlElement value)
        {
            if (fieldName != null && !IncludeDefaultValues && value.IsEmpty)
            {
                return;
            }

            if (value.IsEmpty)
            {
                WriteSimpleField(fieldName, kNull, EscapeOptions.NoValueEscape);
                return;
            }

            string xml = value.OuterXml;

            int count = xml.Length;

            if (Context.MaxStringLength > 0 && Context.MaxStringLength < count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxStringLength {0} < {1}",
                    Context.MaxStringLength,
                    count);
            }

            WriteSimpleField(fieldName, xml, EscapeOptions.Quotes);
        }

        /// <inheritdoc/>
        public void WriteNodeId(string fieldName, NodeId value)
        {
            bool isNull = value.IsNull;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            WriteSimpleField(
                fieldName,
                isNull ? string.Empty : value.Format(Context, ForceNamespaceUri),
                EscapeOptions.Quotes);
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeId(string fieldName, ExpandedNodeId value)
        {
            bool isNull = value.IsNull;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            WriteSimpleField(
                fieldName,
                isNull ? string.Empty : value.Format(Context, ForceNamespaceUri),
                EscapeOptions.Quotes);
        }

        /// <inheritdoc/>
        public void WriteStatusCode(string fieldName, StatusCode value)
        {
            WriteStatusCode(fieldName, value, EscapeOptions.None);
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value)
        {
            WriteDiagnosticInfo(fieldName, value, 0);
        }

        /// <inheritdoc/>
        public void WriteQualifiedName(string fieldName, QualifiedName value)
        {
            bool isNull = value.IsNull;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            WriteSimpleField(
                fieldName,
                isNull ? string.Empty : value.Format(Context, ForceNamespaceUri),
                EscapeOptions.Quotes);
        }

        /// <inheritdoc/>
        public void WriteLocalizedText(string fieldName, LocalizedText value)
        {
            bool isNull = value.IsNullOrEmpty;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            PushStructure(fieldName);

            if (!isNull)
            {
                WriteSimpleField(
                    "Text",
                    value.Text,
                    EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);

                if (!string.IsNullOrEmpty(value.Locale))
                {
                    WriteSimpleField(
                        "Locale",
                        value.Locale,
                        EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);
                }
            }

            PopStructure();
        }

        /// <inheritdoc/>
        public void WriteVariant(string fieldName, Variant value)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                bool isNull =
                    value.TypeInfo.IsUnknown ||
                    value.TypeInfo.BuiltInType == BuiltInType.Null ||
                    value.IsNull;

                if (EncodingToUse is JsonEncodingType.Compact or JsonEncodingType.Verbose)
                {
                    if (fieldName != null && isNull && EncodingToUse == JsonEncodingType.Compact)
                    {
                        return;
                    }

                    PushStructure(fieldName);
                    if (!isNull)
                    {
                        WriteVariantIntoObject("Value", value);
                    }
                    PopStructure();
                }
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public void WriteDataValue(string fieldName, DataValue value)
        {
            bool isNull = value == null;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            PushStructure(fieldName);

            if (!isNull)
            {
                if (!value.WrappedValue.TypeInfo.IsUnknown &&
                    value.WrappedValue.TypeInfo.BuiltInType != BuiltInType.Null)
                {
                    WriteVariantIntoObject("Value", value.WrappedValue);
                }

                if (value.StatusCode != StatusCodes.Good)
                {
                    WriteStatusCode(
                        "StatusCode",
                        value.StatusCode,
                        EscapeOptions.NoFieldNameEscape);
                }

                if (value.SourceTimestamp != DateTime.MinValue)
                {
                    WriteDateTime(
                        "SourceTimestamp",
                        value.SourceTimestamp,
                        EscapeOptions.NoFieldNameEscape);

                    if (value.SourcePicoseconds != 0)
                    {
                        WriteUInt16("SourcePicoseconds", value.SourcePicoseconds);
                    }
                }

                if (value.ServerTimestamp != DateTime.MinValue)
                {
                    WriteDateTime(
                        "ServerTimestamp",
                        value.ServerTimestamp,
                        EscapeOptions.NoFieldNameEscape);

                    if (value.ServerPicoseconds != 0)
                    {
                        WriteUInt16("ServerPicoseconds", value.ServerPicoseconds);
                    }
                }
            }

            PopStructure();
        }

        /// <inheritdoc/>
        public void WriteExtensionObject(string fieldName, ExtensionObject value)
        {
            bool isNull = value.IsNull || value.Encoding == ExtensionObjectEncoding.None;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            value.TryGetEncodeable(out IEncodeable encodeable);

            PushStructure(fieldName);

            ExpandedNodeId typeId = !value.TypeId.IsNull
                ? value.TypeId
                : encodeable?.TypeId ?? NodeId.Null;
            var localTypeId = ExpandedNodeId.ToNodeId(typeId, Context.NamespaceUris);

            if (EncodingToUse is JsonEncodingType.Compact or JsonEncodingType.Verbose)
            {
                if (encodeable != null)
                {
                    if (!SuppressArtifacts && !localTypeId.IsNull)
                    {
                        WriteNodeId("UaTypeId", localTypeId);
                    }

                    encodeable.Encode(this);
                }
                else if (value.TryGetAsJson(out string text))
                {
                    if (!SuppressArtifacts && !localTypeId.IsNull)
                    {
                        WriteNodeId("UaTypeId", localTypeId);
                        m_writer.Write(kComma);
                    }

                    m_writer.Write(text.Trim()[1..^1]);
                }
                else if (value.Encoding == ExtensionObjectEncoding.Binary)
                {
                    if (!SuppressArtifacts && !localTypeId.IsNull)
                    {
                        WriteNodeId("UaTypeId", localTypeId);
                    }

                    WriteByte("UaEncoding", (byte)ExtensionObjectEncoding.Binary);
                    WriteByteString("UaBody", value.TryGetAsBinary(out ByteString b) ? b : default);
                }
                else if (value.Encoding == ExtensionObjectEncoding.Xml)
                {
                    if (!SuppressArtifacts && !localTypeId.IsNull)
                    {
                        WriteNodeId("UaTypeId", localTypeId);
                    }

                    WriteByte("UaEncoding", (byte)ExtensionObjectEncoding.Xml);
                    WriteXmlElement("UaBody", value.TryGetAsXml(out XmlElement x) ? x : default);
                }

                PopStructure();
                return;
            }

            WriteNodeId("TypeId", localTypeId);

            if (encodeable != null)
            {
                WriteEncodeable("Body", encodeable);
            }
            else if (value.TryGetAsJson(out string text))
            {
                m_writer.Write(text.Trim()[1..^1]);
            }
            else
            {
                WriteByte("Encoding", (byte)value.Encoding);

                if (value.Encoding == ExtensionObjectEncoding.Binary)
                {
                    WriteByteString("Body", value.TryGetAsBinary(out ByteString b) ? b : default);
                }
                else if (value.Encoding == ExtensionObjectEncoding.Xml)
                {
                    WriteXmlElement("Body", value.TryGetAsXml(out XmlElement x) ? x : default);
                }
                else if (value.Encoding == ExtensionObjectEncoding.Json)
                {
                    WriteSimpleField("Body", value.TryGetAsJson(out string j) ? j : default);
                }
            }

            PopStructure();
        }

        /// <inheritdoc/>
        public void WriteEncodeableAsExtensionObject<T>(string fieldName, T value) where T : IEncodeable
        {
            WriteExtensionObject(fieldName, new ExtensionObject(value));
        }

        /// <inheritdoc/>
        public void WriteEncodeable<T>(string fieldName, T value) where T : IEncodeable
        {
            bool isNull = EqualityComparer<T>.Default.Equals(value, default);

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            if (m_nestingLevel == 0 &&
                (m_commaRequired || m_topLevelIsArray) &&
                (string.IsNullOrWhiteSpace(fieldName) ^ m_topLevelIsArray))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "With Array as top level, encodeables with fieldname will create invalid json");
            }

            if (m_nestingLevel == 0 &&
                !m_commaRequired &&
                string.IsNullOrWhiteSpace(fieldName) &&
                !m_topLevelIsArray)
            {
                m_writer.Flush();
                if (m_writer.BaseStream.Length == 1) //Opening "{"
                {
                    m_writer.BaseStream.Seek(0, SeekOrigin.Begin);
                }
                m_dontWriteClosing = true;
            }

            CheckAndIncrementNestingLevel();

            try
            {
                PushStructure(fieldName);

                value?.Encode(this);

                PopStructure();
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public void WriteEnumerated<T>(string fieldName, T value) where T : Enum
        {
            int numeric = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            string numericString = numeric.ToString(CultureInfo.InvariantCulture);

            if (EncodingToUse is JsonEncodingType.Compact)
            {
                WriteSimpleField(fieldName, numericString);
            }
            else
            {
                string valueString = value.ToString();

                if (valueString == numericString)
                {
                    WriteSimpleField(fieldName, numericString, EscapeOptions.Quotes);
                }
                else
                {
                    WriteSimpleField(
                        fieldName,
                        CoreUtils.Format("{0}_{1}", valueString, numeric),
                        EscapeOptions.Quotes);
                }
            }
        }

        /// <inheritdoc/>
        public void WriteSwitchField(uint switchField, out string fieldName)
        {
            fieldName = null;

            switch (EncodingToUse)
            {
                case JsonEncodingType.Compact:
                    if (SuppressArtifacts)
                    {
                        return;
                    }
                    break;
                case JsonEncodingType.Verbose:
                    return;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected Encoding type {EncodingToUse}");
            }

            WriteUInt32("SwitchField", switchField);
        }

        /// <inheritdoc/>
        public void WriteEncodingMask(uint encodingMask)
        {
            if (!SuppressArtifacts && EncodingToUse == JsonEncodingType.Compact)
            {
                WriteUInt32("EncodingMask", encodingMask);
            }
        }

        /// <inheritdoc/>
        public void WriteBooleanArray(string fieldName, ArrayOf<bool> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteBoolean(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteSByteArray(string fieldName, ArrayOf<sbyte> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteSByte(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteByteArray(string fieldName, ArrayOf<byte> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteByte(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteInt16Array(string fieldName, ArrayOf<short> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteInt16(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteUInt16Array(string fieldName, ArrayOf<ushort> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteUInt16(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteInt32Array(string fieldName, ArrayOf<int> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteInt32(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteUInt32Array(string fieldName, ArrayOf<uint> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteUInt32(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteInt64Array(string fieldName, ArrayOf<long> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteInt64(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteUInt64Array(string fieldName, ArrayOf<ulong> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteUInt64(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteFloatArray(string fieldName, ArrayOf<float> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteFloat(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteDoubleArray(string fieldName, ArrayOf<double> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDouble(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteStringArray(string fieldName, ArrayOf<string> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteString(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteDateTimeArray(string fieldName, ArrayOf<DateTime> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (values[ii] <= DateTime.MinValue)
                {
                    WriteSimpleFieldNull(null);
                }
                else
                {
                    WriteDateTime(null, values[ii]);
                }
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteGuidArray(string fieldName, ArrayOf<Uuid> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteGuid(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteByteStringArray(string fieldName, ArrayOf<ByteString> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteByteString(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteXmlElementArray(string fieldName, ArrayOf<XmlElement> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteXmlElement(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteNodeIdArray(string fieldName, ArrayOf<NodeId> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteNodeId(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteExpandedNodeIdArray(string fieldName, ArrayOf<ExpandedNodeId> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteExpandedNodeId(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteStatusCodeArray(string fieldName, ArrayOf<StatusCode> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteStatusCode(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteDiagnosticInfoArray(string fieldName, ArrayOf<DiagnosticInfo> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDiagnosticInfo(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteQualifiedNameArray(string fieldName, ArrayOf<QualifiedName> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteQualifiedName(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteLocalizedTextArray(string fieldName, ArrayOf<LocalizedText> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteLocalizedText(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteVariantArray(string fieldName, ArrayOf<Variant> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (values[ii] == Variant.Null)
                {
                    WriteSimpleFieldNull(null);
                    continue;
                }

                WriteVariant(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteDataValueArray(string fieldName, ArrayOf<DataValue> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteDataValue(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteExtensionObjectArray(string fieldName, ArrayOf<ExtensionObject> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                WriteExtensionObject(null, values[ii]);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteEncodeableArrayAsExtensionObjects<T>(string fieldName, ArrayOf<T> values)
            where T : IEncodeable
        {
            WriteExtensionObjectArray(fieldName, values.ConvertAll(v => new ExtensionObject(v)));
        }

        /// <inheritdoc/>
        public void WriteEncodeableArray<T>(string fieldName, ArrayOf<T> values) where T : IEncodeable
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(fieldName) && m_nestingLevel == 0 && !m_topLevelIsArray)
            {
                m_writer.Flush();
                if (m_writer.BaseStream.Length == 1) //Opening "{"
                {
                    m_writer.BaseStream.Seek(0, SeekOrigin.Begin);
                }

                m_nestingLevel++;
                PushArray(fieldName);

                for (int ii = 0; ii < values.Count; ii++)
                {
                    WriteEncodeable(null, values[ii]);
                }

                PopArray();
                m_dontWriteClosing = true;
                m_nestingLevel--;
            }
            else if (!string.IsNullOrWhiteSpace(fieldName) &&
                m_nestingLevel == 0 &&
                m_topLevelIsArray)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "With Array as top level, encodeables array with fieldname will create invalid json");
            }
            else
            {
                PushArray(fieldName);

                // check the length.
                if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }

                for (int ii = 0; ii < values.Count; ii++)
                {
                    WriteEncodeable(null, values[ii]);
                }

                PopArray();
            }
        }

        /// <inheritdoc/>
        public void WriteEnumeratedArray<T>(string fieldName, ArrayOf<T> values) where T : Enum
        {
            if (values.IsNull || values.Count == 0)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            // encode each element in the array.
            foreach (T value in values)
            {
                WriteEnumerated(null, value);
            }

            PopArray();
        }

        /// <inheritdoc/>
        public void WriteVariantValue(string fieldName, Variant value)
        {
            if (value.IsNull)
            {
                return;
            }

            try
            {
                CheckAndIncrementNestingLevel();

                if (!string.IsNullOrEmpty(fieldName))
                {
                    EscapeString(fieldName);
                    m_writer.Write(kQuotationColon);
                    m_commaRequired = false;
                }

                // Write raw, which only writes the value or a matrix as an object
                WriteVariantContents(value, true);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        private void WriteVariantIntoObject(string fieldName, Variant value)
        {
            if (value.IsNull)
            {
                return;
            }

            try
            {
                CheckAndIncrementNestingLevel();

                if (!string.IsNullOrEmpty(fieldName))
                {
                    EscapeString(fieldName);
                    m_writer.Write(kQuotationColon);
                    m_commaRequired = false;
                }

                WriteVariantContents(value, false);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

#if FALSE
        /// <summary>
        /// Encode an array according to its valueRank and BuiltInType
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteArray(
            string fieldName,
            object array,
            int valueRank,
            BuiltInType builtInType)
        {
            // write array.
            if (valueRank == ValueRanks.OneDimension)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                        WriteBooleanArray(fieldName, (bool[])array);
                        return;
                    case BuiltInType.SByte:
                        WriteSByteArray(fieldName, (sbyte[])array);
                        return;
                    case BuiltInType.Byte:
                        WriteByteArray(fieldName, (byte[])array);
                        return;
                    case BuiltInType.Int16:
                        WriteInt16Array(fieldName, (short[])array);
                        return;
                    case BuiltInType.UInt16:
                        WriteUInt16Array(fieldName, (ushort[])array);
                        return;
                    case BuiltInType.Int32:
                        WriteInt32Array(fieldName, (int[])array);
                        return;
                    case BuiltInType.UInt32:
                        WriteUInt32Array(fieldName, (uint[])array);
                        return;
                    case BuiltInType.Int64:
                        WriteInt64Array(fieldName, (long[])array);
                        return;
                    case BuiltInType.UInt64:
                        WriteUInt64Array(fieldName, (ulong[])array);
                        return;
                    case BuiltInType.Float:
                        WriteFloatArray(fieldName, (float[])array);
                        return;
                    case BuiltInType.Double:
                        WriteDoubleArray(fieldName, (double[])array);
                        return;
                    case BuiltInType.String:
                        WriteStringArray(fieldName, (string[])array);
                        return;
                    case BuiltInType.DateTime:
                        WriteDateTimeArray(fieldName, (DateTime[])array);
                        return;
                    case BuiltInType.Guid:
                        WriteGuidArray(fieldName, (Uuid[])array);
                        return;
                    case BuiltInType.ByteString:
                        WriteByteStringArray(fieldName, (ByteString[])array);
                        return;
                    case BuiltInType.XmlElement:
                        WriteXmlElementArray(fieldName, (XmlElement[])array);
                        return;
                    case BuiltInType.NodeId:
                        WriteNodeIdArray(fieldName, (NodeId[])array);
                        return;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeIdArray(fieldName, (ExpandedNodeId[])array);
                        return;
                    case BuiltInType.StatusCode:
                        WriteStatusCodeArray(fieldName, (StatusCode[])array);
                        return;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedNameArray(fieldName, (QualifiedName[])array);
                        return;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedTextArray(fieldName, (LocalizedText[])array);
                        return;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObjectArray(fieldName, (ExtensionObject[])array);
                        return;
                    case BuiltInType.DataValue:
                        WriteDataValueArray(fieldName, (DataValue[])array);
                        return;
                    case BuiltInType.DiagnosticInfo:
                        WriteDiagnosticInfoArray(fieldName, (DiagnosticInfo[])array);
                        return;
                    case BuiltInType.Enumeration:
                        if (array is null or Array)
                        {
                            WriteEnumeratedArray(
                                fieldName,
                                (Array)array,
                                array?.GetType().GetElementType());
                            return;
                        }
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected non Array type encountered while encoding an array of enumeration: {0}",
                            array.GetType());
                    case BuiltInType.Variant:
                        if (array is null or Variant[])
                        {
                            WriteVariantArray(fieldName, (Variant[])array);
                            return;
                        }

                        // try to write IEncodeable Array
                        if (array is IEncodeable[] encodeableArray)
                        {
                            WriteEncodeableArray(
                                fieldName,
                                encodeableArray,
                                array.GetType().GetElementType());
                            return;
                        }

                        if (array is object[] objects)
                        {
                            WriteObjectArray(fieldName, objects);
                            return;
                        }

                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected type encountered while encoding an array of Variants: {0}",
                            array.GetType());
                    case BuiltInType.Null:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        // try to write IEncodeable Array
                        if (array is null or IEncodeable[])
                        {
                            WriteEncodeableArray(
                                fieldName,
                                (IEncodeable[])array,
                                array?.GetType().GetElementType());
                            return;
                        }
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected BuiltInType encountered while encoding an array: {0}",
                            builtInType);
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {builtInType}");
                }
            }
            // write matrix.
            else if (valueRank > ValueRanks.OneDimension)
            {
                if (array is not Matrix matrix)
                {
                    if (array is Array multiArray && multiArray.Rank == valueRank)
                    {
                        matrix = new Matrix(multiArray, builtInType);
                    }
                    else
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected array type encountered while encoding array: {0}",
                            array.GetType().Name);
                    }
                }

                if (EncodingToUse is JsonEncodingType.Compact or JsonEncodingType.Verbose)
                {
                    WriteArrayDimensionMatrix(fieldName, builtInType, matrix);
                }
                else
                {
                    int index = 0;
                    WriteStructureMatrix(fieldName, matrix, 0, ref index, matrix.TypeInfo);
                }
                return;

                // field is omitted
            }
        }

        private void WriteRawExtensionObject(object value)
        {
            if (value is ExtensionObject eo &&
                eo.TryGetEncodeable(out IEncodeable encodeable))
            {
                PushStructure(null);
                encodeable.Encode(this);
                PopStructure();
            }
            else
            {
                if (m_commaRequired)
                {
                    m_writer.Write(kComma);
                }

                m_writer.Write(kNull);
            }

            m_commaRequired = true;
        }

        private void WriteRawVariantArray(object value)
        {
            if (value is IList<Variant> list)
            {
                PushArray(null);

                foreach (Variant ii in list)
                {
                    if (ii is Variant vt)
                    {
                        PushStructure(null);
                        WriteVariantContents(vt.Value, vt.TypeInfo);
                        PopStructure();
                    }
                    else
                    {
                        if (m_commaRequired)
                        {
                            m_writer.Write(kComma);
                        }

                        m_writer.Write(kNull);
                    }
                }

                PopArray();
            }
            else
            {
                m_writer.Write(kNull);
            }

            m_commaRequired = true;
        }
#endif

        /// <summary>
        /// Writes an enumerated Int32 value to the stream.
        /// </summary>
        public void WriteEnumerated(string fieldName, int numeric)
        {
            bool writeNumber = EncodingToUse is JsonEncodingType.Compact;
            string numericString = numeric.ToString(CultureInfo.InvariantCulture);
            WriteSimpleField(
                fieldName,
                numericString,
                writeNumber ? EscapeOptions.None : EscapeOptions.Quotes);
        }

        /// <summary>
        /// Writes the contents of a Variant to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteVariantContents(Variant value, bool raw)
        {
            bool inVariantWithEncoding = m_inVariantWithEncoding;
            try
            {
                bool isNull =
                    value.TypeInfo.IsUnknown ||
                    value.TypeInfo.BuiltInType == BuiltInType.Null ||
                    value.IsNull;

                string fieldName = null;
                if (!raw)
                {
                    if (!isNull)
                    {
                        byte encodingByte = (byte)value.TypeInfo.BuiltInType;

                        if (value.TypeInfo.BuiltInType == BuiltInType.Enumeration)
                        {
                            encodingByte = (byte)BuiltInType.Int32;
                        }

                        if (!SuppressArtifacts)
                        {
                            WriteByte("UaType", encodingByte);
                        }
                    }

                    fieldName = "Value";
                }

                m_inVariantWithEncoding = true;

                // check for null.
                if (value.IsNull)
                {
                    return;
                }

                m_commaRequired = false;

                // write scalar.
                if (value.TypeInfo.IsScalar)
                {
                    switch (value.TypeInfo.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            WriteBoolean(fieldName, value.GetBoolean());
                            return;
                        case BuiltInType.SByte:
                            WriteSByte(fieldName, value.GetSByte());
                            return;
                        case BuiltInType.Byte:
                            WriteByte(fieldName, value.GetByte());
                            return;
                        case BuiltInType.Int16:
                            WriteInt16(fieldName, value.GetInt16());
                            return;
                        case BuiltInType.UInt16:
                            WriteUInt16(fieldName, value.GetUInt16());
                            return;
                        case BuiltInType.Enumeration:
                        case BuiltInType.Int32:
                            WriteInt32(fieldName, value.GetInt32());
                            return;
                        case BuiltInType.UInt32:
                            WriteUInt32(fieldName, value.GetUInt32());
                            return;
                        case BuiltInType.Int64:
                            WriteInt64(fieldName, value.GetInt64());
                            return;
                        case BuiltInType.UInt64:
                            WriteUInt64(fieldName, value.GetUInt64());
                            return;
                        case BuiltInType.Float:
                            WriteFloat(fieldName, value.GetFloat());
                            return;
                        case BuiltInType.Double:
                            WriteDouble(fieldName, value.GetDouble());
                            return;
                        case BuiltInType.String:
                            WriteString(fieldName, value.GetString());
                            return;
                        case BuiltInType.DateTime:
                            WriteDateTime(fieldName, value.GetDateTime());
                            return;
                        case BuiltInType.Guid:
                            WriteGuid(fieldName, value.GetGuid());
                            return;
                        case BuiltInType.ByteString:
                            WriteByteString(fieldName, value.GetByteString());
                            return;
                        case BuiltInType.XmlElement:
                            WriteXmlElement(fieldName, value.GetXmlElement());
                            return;
                        case BuiltInType.NodeId:
                            WriteNodeId(fieldName, value.GetNodeId());
                            return;
                        case BuiltInType.ExpandedNodeId:
                            WriteExpandedNodeId(fieldName, value.GetExpandedNodeId());
                            return;
                        case BuiltInType.StatusCode:
                            WriteStatusCode(fieldName, value.GetStatusCode());
                            return;
                        case BuiltInType.QualifiedName:
                            WriteQualifiedName(fieldName, value.GetQualifiedName());
                            return;
                        case BuiltInType.LocalizedText:
                            WriteLocalizedText(fieldName, value.GetLocalizedText());
                            return;
                        case BuiltInType.ExtensionObject:
                            WriteExtensionObject(fieldName, value.GetExtensionObject());
                            return;
                        case BuiltInType.DataValue:
                            WriteDataValue(fieldName, value.GetDataValue());
                            return;
                        case BuiltInType.Null:
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
                            WriteBooleanArray(fieldName, value.GetBooleanArray());
                            break;
                        case BuiltInType.SByte:
                            WriteSByteArray(fieldName, value.GetSByteArray());
                            break;
                        case BuiltInType.Byte:
                            WriteByteArray(fieldName, value.GetByteArray());
                            break;
                        case BuiltInType.Int16:
                            WriteInt16Array(fieldName, value.GetInt16Array());
                            break;
                        case BuiltInType.UInt16:
                            WriteUInt16Array(fieldName, value.GetUInt16Array());
                            break;
                        case BuiltInType.Int32:
                            WriteInt32Array(fieldName, value.GetInt32Array());
                            break;
                        case BuiltInType.UInt32:
                            WriteUInt32Array(fieldName, value.GetUInt32Array());
                            break;
                        case BuiltInType.Int64:
                            WriteInt64Array(fieldName, value.GetInt64Array());
                            break;
                        case BuiltInType.UInt64:
                            WriteUInt64Array(fieldName, value.GetUInt64Array());
                            break;
                        case BuiltInType.Float:
                            WriteFloatArray(fieldName, value.GetFloatArray());
                            break;
                        case BuiltInType.Double:
                            WriteDoubleArray(fieldName, value.GetDoubleArray());
                            break;
                        case BuiltInType.String:
                            WriteStringArray(fieldName, value.GetStringArray());
                            break;
                        case BuiltInType.DateTime:
                            WriteDateTimeArray(fieldName, value.GetDateTimeArray());
                            break;
                        case BuiltInType.Guid:
                            WriteGuidArray(fieldName, value.GetGuidArray());
                            break;
                        case BuiltInType.ByteString:
                            WriteByteStringArray(fieldName, value.GetByteStringArray());
                            break;
                        case BuiltInType.XmlElement:
                            WriteXmlElementArray(fieldName, value.GetXmlElementArray());
                            break;
                        case BuiltInType.NodeId:
                            WriteNodeIdArray(fieldName, value.GetNodeIdArray());
                            break;
                        case BuiltInType.ExpandedNodeId:
                            WriteExpandedNodeIdArray(fieldName, value.GetExpandedNodeIdArray());
                            break;
                        case BuiltInType.StatusCode:
                            WriteStatusCodeArray(fieldName, value.GetStatusCodeArray());
                            break;
                        case BuiltInType.QualifiedName:
                            WriteQualifiedNameArray(fieldName, value.GetQualifiedNameArray());
                            break;
                        case BuiltInType.LocalizedText:
                            WriteLocalizedTextArray(fieldName, value.GetLocalizedTextArray());
                            break;
                        case BuiltInType.ExtensionObject:
                            WriteExtensionObjectArray(fieldName, value.GetExtensionObjectArray());
                            break;
                        case BuiltInType.DataValue:
                            WriteDataValueArray(fieldName, value.GetDataValueArray());
                            break;
                        case BuiltInType.Enumeration:
                            WriteInt32Array(fieldName, value.GetInt32Array());
                            break;
                        case BuiltInType.Variant:
                            WriteVariantArray(fieldName, value.GetVariantArray());
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
                    if (raw)
                    {
                        PushStructure(null);
                    }
                    switch (value.TypeInfo.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            WriteBooleanArray(fieldName, value.GetBooleanMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.SByte:
                            WriteSByteArray(fieldName, value.GetSByteMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.Byte:
                            WriteByteArray(fieldName, value.GetByteMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.Int16:
                            WriteInt16Array(fieldName, value.GetInt16Matrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.UInt16:
                            WriteUInt16Array(fieldName, value.GetUInt16Matrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.Int32:
                            WriteInt32Array(fieldName, value.GetInt32Matrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.UInt32:
                            WriteUInt32Array(fieldName, value.GetUInt32Matrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.Int64:
                            WriteInt64Array(fieldName, value.GetInt64Matrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.UInt64:
                            WriteUInt64Array(fieldName, value.GetUInt64Matrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.Float:
                            WriteFloatArray(fieldName, value.GetFloatMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.Double:
                            WriteDoubleArray(fieldName, value.GetDoubleMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.String:
                            WriteStringArray(fieldName, value.GetStringMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.DateTime:
                            WriteDateTimeArray(fieldName, value.GetDateTimeMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.Guid:
                            WriteGuidArray(fieldName, value.GetGuidMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.ByteString:
                            WriteByteStringArray(fieldName, value.GetByteStringMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.XmlElement:
                            WriteXmlElementArray(fieldName, value.GetXmlElementMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.NodeId:
                            WriteNodeIdArray(fieldName, value.GetNodeIdMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.ExpandedNodeId:
                            WriteExpandedNodeIdArray(fieldName, value.GetExpandedNodeIdMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.StatusCode:
                            WriteStatusCodeArray(fieldName, value.GetStatusCodeMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.QualifiedName:
                            WriteQualifiedNameArray(fieldName, value.GetQualifiedNameMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.LocalizedText:
                            WriteLocalizedTextArray(fieldName, value.GetLocalizedTextMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.ExtensionObject:
                            WriteExtensionObjectArray(fieldName, value.GetExtensionObjectMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.DataValue:
                            WriteDataValueArray(fieldName, value.GetDataValueMatrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.Enumeration:
                            WriteInt32Array(fieldName, value.GetInt32Matrix().ToArrayOf(out dim));
                            break;
                        case BuiltInType.Variant:
                            WriteVariantArray(fieldName, value.GetVariantMatrix().ToArrayOf(out dim));
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

                    WriteInt32Array("Dimensions", dim);
                    if (raw)
                    {
                        PopStructure();
                    }
                }
            }
            finally
            {
                m_inVariantWithEncoding = inVariantWithEncoding;
            }
        }

        /// <summary>
        /// Push structure with an option to not escape a known fieldname.
        /// </summary>
        private void PushStructure(
            string fieldName,
            EscapeOptions escapeOptions = EscapeOptions.None)
        {
            m_nestingLevel++;

            if (m_commaRequired)
            {
                m_writer.Write(kComma);
            }

            if (!string.IsNullOrEmpty(fieldName))
            {
                if ((escapeOptions & EscapeOptions.NoFieldNameEscape) != 0)
                {
                    m_writer.Write(kQuotation);
                    m_writer.Write(fieldName);
                }
                else
                {
                    EscapeString(fieldName);
                }
                m_writer.Write(kQuotationColon);
            }
            else if (!m_commaRequired)
            {
                if (m_nestingLevel == 1 && !m_topLevelIsArray)
                {
                    m_levelOneSkipped = true;
                    return;
                }
            }

            m_commaRequired = false;
            m_writer.Write(kLeftCurlyBrace);
        }

        /// <summary>
        /// Writes an StatusCode to the stream.
        /// </summary>
        private void WriteStatusCode(
            string fieldName,
            StatusCode value,
            EscapeOptions escapeOptions)
        {
            bool isNull = value == StatusCodes.Good;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            PushStructure(fieldName, escapeOptions);

            if (!isNull)
            {
                WriteUInt32("Code", value.Code);

                if (EncodingToUse is JsonEncodingType.Verbose)
                {
                    string symbolicId = value.SymbolicId;
                    if (!string.IsNullOrEmpty(symbolicId))
                    {
                        WriteSimpleField(
                            "Symbol",
                            symbolicId,
                            EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);
                    }
                }
            }

            PopStructure();
        }

        /// <summary>
        /// Writes a UTC date/time to the stream. Reduce escape overhead for fieldname.
        /// </summary>
        private void WriteDateTime(string fieldName, DateTime value, EscapeOptions escapeOptions)
        {
            if (fieldName != null && !IncludeDefaultValues && value == DateTime.MinValue)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            escapeOptions |= EscapeOptions.NoValueEscape;
            if (value <= DateTime.MinValue)
            {
                WriteSimpleField(fieldName, "\"0001-01-01T00:00:00Z\"", escapeOptions);
            }
            else if (value >= DateTime.MaxValue)
            {
                WriteSimpleField(fieldName, "\"9999-12-31T23:59:59Z\"", escapeOptions);
            }
            else
            {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
                Span<char> valueString = stackalloc char[DateTimeRoundTripKindLength];
                ConvertUniversalTimeToString(value, valueString, out int charsWritten);
                WriteSimpleFieldAsSpan(
                    fieldName,
                    valueString[..charsWritten],
                    escapeOptions | EscapeOptions.Quotes);
#else
                WriteSimpleField(
                    fieldName,
                    ConvertUniversalTimeToString(value),
                    escapeOptions | EscapeOptions.Quotes);
#endif
            }
        }

        /// <summary>
        /// Returns true if a simple field can be written.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private bool CheckForSimpleFieldNull<T>(string fieldName, ArrayOf<T> values)
        {
            // always include default values for non reversible/verbose
            // include default values when encoding in a Variant
            if (values.IsNull ||
                (values.Count == 0 && !m_inVariantWithEncoding && !IncludeDefaultValues))
            {
                WriteSimpleFieldNull(fieldName);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Completes writing and returns the text length.
        /// </summary>
        private int InternalClose(bool dispose)
        {
            if (m_writer == null)
            {
                return 0;
            }

            if (!m_dontWriteClosing)
            {
                if (m_topLevelIsArray)
                {
                    m_writer.Write(kRightSquareBracket);
                }
                else
                {
                    m_writer.Write(kRightCurlyBrace);
                }
            }

            m_writer.Flush();
            int length = (int)m_writer.BaseStream.Position;
            if (dispose)
            {
                m_writer.Dispose();
                m_writer = null;
            }
            return length;
        }

        /// <summary>
        /// Writes a DiagnosticInfo to the stream.
        /// Ignores InnerDiagnosticInfo field if the nesting level
        /// <see cref="DiagnosticInfo.MaxInnerDepth"/> is exceeded.
        /// </summary>
        private void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value, int depth)
        {
            bool isNull = value == null || value.IsNullDiagnosticInfo;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            if (value == null)
            {
                WriteSimpleField(fieldName, kNull, EscapeOptions.NoValueEscape);
                return;
            }

            CheckAndIncrementNestingLevel();

            try
            {
                PushStructure(fieldName);

                if (value.SymbolicId >= 0)
                {
                    WriteSimpleField(
                        "SymbolicId",
                        value.SymbolicId.ToString(CultureInfo.InvariantCulture),
                        EscapeOptions.NoFieldNameEscape);
                }

                if (value.NamespaceUri >= 0)
                {
                    WriteSimpleField(
                        "NamespaceUri",
                        value.NamespaceUri.ToString(CultureInfo.InvariantCulture),
                        EscapeOptions.NoFieldNameEscape);
                }

                if (value.Locale >= 0)
                {
                    WriteSimpleField(
                        "Locale",
                        value.Locale.ToString(CultureInfo.InvariantCulture),
                        EscapeOptions.NoFieldNameEscape);
                }

                if (value.LocalizedText >= 0)
                {
                    WriteSimpleField(
                        "LocalizedText",
                        value.LocalizedText.ToString(CultureInfo.InvariantCulture),
                        EscapeOptions.NoFieldNameEscape);
                }

                if (value.AdditionalInfo != null)
                {
                    WriteSimpleField(
                        "AdditionalInfo",
                        value.AdditionalInfo,
                        EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);
                }

                if (value.InnerStatusCode != StatusCodes.Good)
                {
                    WriteStatusCode("InnerStatusCode", value.InnerStatusCode);
                }

                if (value.InnerDiagnosticInfo != null)
                {
                    if (depth < DiagnosticInfo.MaxInnerDepth)
                    {
                        WriteDiagnosticInfo(
                            "InnerDiagnosticInfo",
                            value.InnerDiagnosticInfo,
                            depth + 1);
                    }
                    else
                    {
                        m_logger.LogWarning(
                            "InnerDiagnosticInfo dropped because nesting exceeds maximum of {MaxInnerDepth}.",
                            DiagnosticInfo.MaxInnerDepth);
                    }
                }

                PopStructure();
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckAndIncrementNestingLevel()
        {
            if (m_nestingLevel > Context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    Context.MaxEncodingNestingLevels);
            }
            m_nestingLevel++;
        }
        /// <inheritdoc/>
        public void PushStructure(string fieldName)
        {
            m_nestingLevel++;

            if (m_commaRequired)
            {
                m_writer.Write(kComma);
            }

            if (!string.IsNullOrEmpty(fieldName))
            {
                EscapeString(fieldName);
                m_writer.Write(kQuotationColon);
            }
            else if (!m_commaRequired)
            {
                if (m_nestingLevel == 1 && !m_topLevelIsArray)
                {
                    m_levelOneSkipped = true;
                    return;
                }
            }

            m_commaRequired = false;
            m_writer.Write(kLeftCurlyBrace);
        }

        /// <inheritdoc/>
        public void PushArray(string fieldName)
        {
            m_nestingLevel++;

            if (m_commaRequired)
            {
                m_writer.Write(kComma);
            }

            if (!string.IsNullOrEmpty(fieldName))
            {
                EscapeString(fieldName);
                m_writer.Write(kQuotationColon);
            }
            else if (!m_commaRequired)
            {
                if (m_nestingLevel == 1 && !m_topLevelIsArray)
                {
                    m_levelOneSkipped = true;
                    return;
                }
            }

            m_commaRequired = false;
            m_writer.Write(kLeftSquareBracket);
        }

        /// <inheritdoc/>
        public void PopStructure()
        {
            if (m_nestingLevel > 1 ||
                m_topLevelIsArray ||
                (m_nestingLevel == 1 && !m_levelOneSkipped))
            {
                m_writer.Write(kRightCurlyBrace);
                m_commaRequired = true;
            }

            m_nestingLevel--;
        }

        /// <inheritdoc/>
        public void PopArray()
        {
            if (m_nestingLevel > 1 ||
                m_topLevelIsArray ||
                (m_nestingLevel == 1 && !m_levelOneSkipped))
            {
                m_writer.Write(kRightSquareBracket);
                m_commaRequired = true;
            }

            m_nestingLevel--;
        }

        /// <summary>
        /// The length of the DateTime string encoded by "o"
        /// </summary>
        public const int DateTimeRoundTripKindLength = 28;

        /// <summary>
        /// the index of the last digit which can be omitted if 0
        /// </summary>
        internal const int DateTimeRoundTripKindLastDigit = DateTimeRoundTripKindLength - 2;

        /// <summary>
        /// the index of the first digit which can be omitted (7 digits total)
        /// </summary>
        internal const int DateTimeRoundTripKindFirstDigit = DateTimeRoundTripKindLastDigit - 7;

        /// <summary>
        /// Write Utc time in the format "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK".
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        public static void ConvertUniversalTimeToString(
            DateTime value,
            Span<char> valueString,
            out int charsWritten)
        {
            // Note: "o" is a shortcut for "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK" and implicitly
            // uses invariant culture and gregorian calendar, but executes up to 10 times faster.
            // But in contrary to the explicit format string, trailing zeroes are not omitted!
            if (value.Kind != DateTimeKind.Utc)
            {
                value.ToUniversalTime()
                    .TryFormat(valueString, out charsWritten, "o", CultureInfo.InvariantCulture);
            }
            else
            {
                value.TryFormat(valueString, out charsWritten, "o", CultureInfo.InvariantCulture);
            }

            System.Diagnostics.Debug.Assert(charsWritten == DateTimeRoundTripKindLength);

            // check if trailing zeroes can be omitted
            int i = DateTimeRoundTripKindLastDigit;
            while (i > DateTimeRoundTripKindFirstDigit)
            {
                if (valueString[i] != '0')
                {
                    break;
                }
                i--;
            }

            if (i < DateTimeRoundTripKindLastDigit)
            {
                // check if the decimal point has to be removed too
                if (i == DateTimeRoundTripKindFirstDigit)
                {
                    i--;
                }
                valueString[i + 1] = 'Z';
                charsWritten = i + 2;
            }
        }
#else
        public static string ConvertUniversalTimeToString(DateTime value)
        {
            // Note: "o" is a shortcut for "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK" and implicitly
            // uses invariant culture and gregorian calendar, but executes up to 10 times faster.
            // But in contrary to the explicit format string, trailing zeroes are not omitted!
            string valueString = value.ToUniversalTime().ToString("o");

            // check if trailing zeroes can be omitted
            int i = DateTimeRoundTripKindLastDigit;
            while (i > DateTimeRoundTripKindFirstDigit)
            {
                if (valueString[i] != '0')
                {
                    break;
                }
                i--;
            }

            if (i < DateTimeRoundTripKindLastDigit)
            {
                // check if the decimal point has to be removed too
                if (i == DateTimeRoundTripKindFirstDigit)
                {
                    i--;
                }
                valueString = valueString.Remove(i + 1, DateTimeRoundTripKindLastDigit - i);
            }

            return valueString;
        }
#endif

        private static readonly char[] s_specialChars
            = [kQuotation, kBackslash, '\n', '\r', '\t', '\b', '\f'];

        private static readonly char[] s_substitution
            = [kQuotation, kBackslash, 'n', 'r', 't', 'b', 'f'];

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Using a span to escape the string, write strings to stream writer if possible.
        /// </summary>
        private void EscapeString(ReadOnlySpan<char> value)
        {
            int lastOffset = 0;

            m_writer.Write(kQuotation);

            for (int i = 0; i < value.Length; i++)
            {
                bool found = false;
                char ch = value[i];

                for (int ii = 0; ii < s_specialChars.Length; ii++)
                {
                    if (s_specialChars[ii] == ch)
                    {
                        WriteSpan(ref lastOffset, value, i);
                        m_writer.Write('\\');
                        m_writer.Write(s_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    WriteSpan(ref lastOffset, value, i);
                    m_writer.Write('\\');
                    m_writer.Write('u');
                    m_writer.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            if (lastOffset == 0)
            {
                m_writer.Write(value);
            }
            else
            {
                WriteSpan(ref lastOffset, value, value.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSpan(ref int lastOffset, ReadOnlySpan<char> valueSpan, int index)
        {
            if (lastOffset < index - 2)
            {
                m_writer.Write(valueSpan[lastOffset..index]);
            }
            else
            {
                while (lastOffset < index)
                {
                    m_writer.Write(valueSpan[lastOffset++]);
                }
            }
            lastOffset = index + 1;
        }
#else
        /// <summary>
        /// Escapes a string and writes it to the stream.
        /// </summary>
        private void EscapeString(string value)
        {
            m_writer.Write(kQuotation);

            foreach (char ch in value)
            {
                bool found = false;

                for (int ii = 0; ii < s_specialChars.Length; ii++)
                {
                    if (s_specialChars[ii] == ch)
                    {
                        m_writer.Write(kBackslash);
                        m_writer.Write(s_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (ch < 32)
                    {
                        m_writer.Write(kBackslash);
                        m_writer.Write('u');
                        m_writer.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                        continue;
                    }
                    m_writer.Write(ch);
                }
            }
        }
#endif

        private void WriteSimpleFieldNull(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                if (m_commaRequired)
                {
                    m_writer.Write(kComma);
                }

                m_writer.Write(kNull);

                m_commaRequired = true;
            }
        }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        private void WriteSimpleField(
            string fieldName,
            string value,
            EscapeOptions options = EscapeOptions.None)
        {
            // unlike Span<byte>, Span<char> can not become null, handle the case here
            if (value == null)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            WriteSimpleFieldAsSpan(fieldName, value.AsSpan(), options);
        }

        private void WriteSimpleFieldAsSpan(
            string fieldName,
            ReadOnlySpan<char> value,
            EscapeOptions options)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                if (m_commaRequired)
                {
                    m_writer.Write(kComma);
                }

                if ((options & EscapeOptions.NoFieldNameEscape) == EscapeOptions.NoFieldNameEscape)
                {
                    m_writer.Write(kQuotation);
                    m_writer.Write(fieldName);
                }
                else
                {
                    EscapeString(fieldName);
                }
                m_writer.Write(kQuotationColon);
            }
            else if (m_commaRequired)
            {
                m_writer.Write(kComma);
            }

            if ((options & EscapeOptions.Quotes) == EscapeOptions.Quotes)
            {
                if ((options & EscapeOptions.NoValueEscape) == EscapeOptions.NoValueEscape)
                {
                    m_writer.Write(kQuotation);
                    m_writer.Write(value);
                }
                else
                {
                    EscapeString(value);
                }
                m_writer.Write(kQuotation);
            }
            else
            {
                m_writer.Write(value);
            }

            m_commaRequired = true;
        }
#else
        private void WriteSimpleField(
            string fieldName,
            string value,
            EscapeOptions options = EscapeOptions.None)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                if (value == null)
                {
                    return;
                }

                if (m_commaRequired)
                {
                    m_writer.Write(kComma);
                }

                if ((options & EscapeOptions.NoFieldNameEscape) == EscapeOptions.NoFieldNameEscape)
                {
                    m_writer.Write(kQuotation);
                    m_writer.Write(fieldName);
                }
                else
                {
                    EscapeString(fieldName);
                }
                m_writer.Write(kQuotationColon);
            }
            else if (m_commaRequired)
            {
                m_writer.Write(kComma);
            }

            if (value != null)
            {
                if ((options & EscapeOptions.Quotes) == EscapeOptions.Quotes)
                {
                    if ((options & EscapeOptions.NoValueEscape) == EscapeOptions.NoValueEscape)
                    {
                        m_writer.Write(kQuotation);
                        m_writer.Write(value);
                    }
                    else
                    {
                        EscapeString(value);
                    }
                    m_writer.Write(kQuotation);
                }
                else
                {
                    m_writer.Write(value);
                }
            }
            else
            {
                m_writer.Write(kNull);
            }

            m_commaRequired = true;
        }
#endif

        private const int kStreamWriterBufferSize = 1024;
        private const string kQuotationColon = "\":";
        private const char kComma = ',';
        private const char kQuotation = '\"';
        private const char kBackslash = '\\';
        private const char kLeftCurlyBrace = '{';
        private const char kRightCurlyBrace = '}';
        private const char kLeftSquareBracket = '[';
        private const char kRightSquareBracket = ']';
        private static readonly UTF8Encoding s_utf8Encoding = new(false);
        private const string kNull = "null";
        private Stream m_stream;
        private MemoryStream m_memoryStream;
        private StreamWriter m_writer;
        private readonly Stack<string> m_namespaces = [];
        private bool m_commaRequired;
        private bool m_inVariantWithEncoding;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        private readonly bool m_topLevelIsArray;
        private readonly ILogger m_logger;
        private bool m_levelOneSkipped;
        private bool m_dontWriteClosing;
        private readonly bool m_leaveOpen;
        private readonly bool m_includeDefaultNumberValues;

        [Flags]
        private enum EscapeOptions
        {
            None = 0,
            Quotes = 1,
            NoValueEscape = 2,
            NoFieldNameEscape = 4
        }
    }
}
