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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Parse json into opc ua types
    /// </summary>
    public sealed class JsonDecoder : IDecoder
    {
        /// <summary>
        /// Root element
        /// </summary>
        public JsonElement Root => m_document.RootElement;

        /// <summary>
        /// Create decoder over utf8json buffer
        /// </summary>
        public JsonDecoder(
            in ReadOnlySequence<byte> utf8Json,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_options = options ?? new JsonDecoderOptions();
            m_logger = context.Telemetry.CreateLogger<JsonDecoder>();
            try
            {
                m_document = JsonDocument.Parse(
                    utf8Json,
                    ParseOptions(Context.MaxEncodingNestingLevels));
                m_stack.Push(m_document.RootElement);
            }
            catch (Exception ex)
            {
                HandleParsingError(ex);
            }
        }

        /// <summary>
        /// Create decoder with a stream
        /// </summary>
        public JsonDecoder(
            Stream stream,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_options = options ?? new JsonDecoderOptions();
            m_logger = context.Telemetry.CreateLogger<JsonDecoder>();
            try
            {
                m_document = JsonDocument.Parse(
                    stream,
                    ParseOptions(Context.MaxEncodingNestingLevels));
                m_stack.Push(m_document.RootElement);
            }
            catch (Exception ex)
            {
                HandleParsingError(ex);
            }
        }

        /// <summary>
        /// Create decoder from json string
        /// </summary>
        public JsonDecoder(
            string json,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_options = options ?? new JsonDecoderOptions();
            m_logger = context.Telemetry.CreateLogger<JsonDecoder>();
            try
            {
                m_document = JsonDocument.Parse(
                    json,
                    ParseOptions(Context.MaxEncodingNestingLevels));
                m_stack.Push(m_document.RootElement);
            }
            catch (Exception ex)
            {
                HandleParsingError(ex);
            }
        }

        /// <summary>
        /// Create decoder over an already parsed json document.
        /// </summary>
        /// <remarks>
        /// The assumption is that the parsing already checked the
        /// desired nesting limits and that the document is well
        /// formed.
        /// </remarks>
        internal JsonDecoder(
            JsonDocument document,
            IServiceMessageContext context,
            JsonDecoderOptions? options = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_options = options ?? new JsonDecoderOptions();
            m_logger = context.Telemetry.CreateLogger<JsonDecoder>();
            m_document = document;
            m_stack.Push(m_document.RootElement);
        }

        /// <inheritdoc/>
        public EncodingType EncodingType => EncodingType.Json;

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
        public void Close()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException("Decoder already disposed.");
            }
            m_document.Dispose();
            m_disposed = true;
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
        public void SetMappingTables(NamespaceTable? namespaceUris, StringTable? serverUris)
        {
            m_namespaceMappings = null;

            if (namespaceUris != null && Context.NamespaceUris != null)
            {
                ushort[] namespaceMappings = new ushort[namespaceUris.Count];

                for (uint ii = 0; ii < namespaceUris.Count; ii++)
                {
                    string? uri = namespaceUris.GetString(ii);

                    if (m_options.UpdateNamespaceTable)
                    {
                        namespaceMappings[ii] =
                            Context.NamespaceUris.GetIndexOrAppend(uri!);
                    }
                    else
                    {
                        int index =
                            Context.NamespaceUris.GetIndex(namespaceUris.GetString(ii)!);
                        namespaceMappings[ii] =
                            index >= 0 ? (ushort)index : ushort.MaxValue;
                    }
                }

                m_namespaceMappings = namespaceMappings;
            }

            m_serverMappings = null;

            if (serverUris != null && Context.ServerUris != null)
            {
                ushort[] serverMappings = new ushort[serverUris.Count];

                for (uint ii = 0; ii < serverUris.Count; ii++)
                {
                    string? uri = serverUris.GetString(ii);

                    if (m_options.UpdateNamespaceTable)
                    {
                        serverMappings[ii] =
                            Context.ServerUris.GetIndexOrAppend(uri!);
                    }
                    else
                    {
                        int index =
                            Context.ServerUris.GetIndex(serverUris.GetString(ii)!);
                        serverMappings[ii] =
                            index >= 0 ? (ushort)index : ushort.MaxValue;
                    }
                }

                m_serverMappings = serverMappings;
            }
        }

        /// <summary>
        /// Decodes a message from a buffer.
        /// </summary>
        /// <typeparam name="T">The type of the message to read</typeparam>
        public static T DecodeMessage<T>(
            byte[] buffer,
            IServiceMessageContext context) where T : IEncodeable
        {
            return DecodeMessage<T>(new ReadOnlySequence<byte>(buffer), context);
        }

        /// <summary>
        /// Decodes a message from a buffer.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <typeparam name="T">The type of the message to read</typeparam>
        public static T DecodeMessage<T>(
            ReadOnlySequence<byte> buffer,
            IServiceMessageContext context) where T : IEncodeable
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // check that the max message size was not exceeded.
            if (context.MaxMessageSize > 0 &&
                context.MaxMessageSize < buffer.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize {0} < {1}",
                    context.MaxMessageSize,
                    buffer.Length);
            }

            using var decoder = new JsonDecoder(buffer, context);
            return decoder.DecodeMessage<T>();
        }

        /// <inheritdoc/>
        public T DecodeMessage<T>() where T : IEncodeable
        {
            if (TryGetMessageFromElement(Root, out T value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public bool ReadBoolean(string? fieldName)
        {
            if (TryGetBooleanFromElement(
                GetPropertyElement(fieldName),
                out bool value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<bool> ReadBooleanArray(string? fieldName)
        {
            if (TryGetBooleanArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<bool> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public byte ReadByte(string? fieldName)
        {
            if (TryGetByteFromElement(
                GetPropertyElement(fieldName),
                out byte value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<byte> ReadByteArray(string? fieldName)
        {
            if (TryGetByteArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<byte> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public ByteString ReadByteString(string? fieldName)
        {
            if (TryGetByteStringFromElement(
                GetPropertyElement(fieldName),
                out ByteString value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<ByteString> ReadByteStringArray(string? fieldName)
        {
            if (TryGetByteStringArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<ByteString> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public DataValue? ReadDataValue(string? fieldName)
        {
            if (TryGetDataValueFromElement(
                GetPropertyElement(fieldName),
                out DataValue? value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<DataValue?> ReadDataValueArray(string? fieldName)
        {
            if (TryGetDataValueArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<DataValue?> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public DateTimeUtc ReadDateTime(string? fieldName)
        {
            if (TryGetDateTimeFromElement(
                GetPropertyElement(fieldName),
                out DateTimeUtc value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<DateTimeUtc> ReadDateTimeArray(string? fieldName)
        {
            if (TryGetDateTimeArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<DateTimeUtc> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public DiagnosticInfo? ReadDiagnosticInfo(string? fieldName)
        {
            if (TryGetDiagnosticInfoFromElement(
                GetPropertyElement(fieldName),
                out DiagnosticInfo? value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<DiagnosticInfo?> ReadDiagnosticInfoArray(string? fieldName)
        {
            if (TryGetDiagnosticInfoArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<DiagnosticInfo?> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public double ReadDouble(string? fieldName)
        {
            if (TryGetDoubleFromElement(
                GetPropertyElement(fieldName),
                out double value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<double> ReadDoubleArray(string? fieldName)
        {
            if (TryGetDoubleArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<double> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public T ReadEnumerated<T>(string fieldName) where T : struct, Enum
        {
            if (TryGetEnumerationFromElement(
                GetPropertyElement(fieldName),
                out T value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEnumeratedArray<T>(string fieldName)
            where T : struct, Enum
        {
            if (TryGetEnumerationArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<T> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public EnumValue ReadEnumerated(string? fieldName)
        {
            if (TryGetEnumerationFromElement(
                GetPropertyElement(fieldName),
                out EnumValue value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<EnumValue> ReadEnumeratedArray(string? fieldName)
        {
            if (TryGetEnumerationArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<EnumValue> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public ExpandedNodeId ReadExpandedNodeId(string? fieldName)
        {
            if (TryGetExpandedNodeIdFromElement(
                GetPropertyElement(fieldName),
                out ExpandedNodeId value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string? fieldName)
        {
            if (TryGetExpandedNodeIdArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<ExpandedNodeId> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public ExtensionObject ReadExtensionObject(string? fieldName)
        {
            if (TryGetExtensionObjectFromElement(
                GetPropertyElement(fieldName),
                out ExtensionObject value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string? fieldName)
        {
            if (TryGetExtensionObjectArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<ExtensionObject> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public float ReadFloat(string? fieldName)
        {
            if (TryGetFloatFromElement(
                GetPropertyElement(fieldName),
                out float value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<float> ReadFloatArray(string? fieldName)
        {
            if (TryGetFloatArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<float> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public Uuid ReadGuid(string? fieldName)
        {
            if (TryGetGuidFromElement(
                GetPropertyElement(fieldName),
                out Uuid value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<Uuid> ReadGuidArray(string? fieldName)
        {
            if (TryGetGuidArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<Uuid> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public short ReadInt16(string? fieldName)
        {
            if (TryGetInt16FromElement(
                GetPropertyElement(fieldName),
                out short value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<short> ReadInt16Array(string? fieldName)
        {
            if (TryGetInt16ArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<short> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public int ReadInt32(string? fieldName)
        {
            if (TryGetInt32FromElement(
                GetPropertyElement(fieldName),
                out int value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<int> ReadInt32Array(string? fieldName)
        {
            if (TryGetInt32ArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<int> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public long ReadInt64(string? fieldName)
        {
            if (TryGetInt64FromElement(
                GetPropertyElement(fieldName),
                out long value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<long> ReadInt64Array(string? fieldName)
        {
            if (TryGetInt64ArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<long> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public LocalizedText ReadLocalizedText(string? fieldName)
        {
            if (TryGetLocalizedTextFromElement(
                GetPropertyElement(fieldName),
                out LocalizedText value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string? fieldName)
        {
            if (TryGetLocalizedTextArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<LocalizedText> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string? fieldName)
        {
            if (TryGetNodeIdFromElement(
                GetPropertyElement(fieldName),
                out NodeId value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> ReadNodeIdArray(string? fieldName)
        {
            if (TryGetNodeIdArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<NodeId> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public QualifiedName ReadQualifiedName(string? fieldName)
        {
            if (TryGetQualifiedNameFromElement(
                GetPropertyElement(fieldName),
                out QualifiedName value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string? fieldName)
        {
            if (TryGetQualifiedNameArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<QualifiedName> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public sbyte ReadSByte(string? fieldName)
        {
            if (TryGetSByteFromElement(
                GetPropertyElement(fieldName),
                out sbyte value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<sbyte> ReadSByteArray(string? fieldName)
        {
            if (TryGetSByteArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<sbyte> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public StatusCode ReadStatusCode(string? fieldName)
        {
            if (TryGetStatusCodeFromElement(
                GetPropertyElement(fieldName),
                out StatusCode value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<StatusCode> ReadStatusCodeArray(string? fieldName)
        {
            if (TryGetStatusCodeArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<StatusCode> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public string? ReadString(string? fieldName)
        {
            if (TryGetStringFromElement(
                GetPropertyElement(fieldName),
                out string? value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<string?> ReadStringArray(string? fieldName)
        {
            if (TryGetStringArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<string?> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (TryGetEncodeableFromElement(
                GetPropertyElement(fieldName),
                encodeableTypeId,
                out T value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string fieldName)
            where T : IEncodeable, new()
        {
            if (TryGetEncodeableFromElement(
                GetPropertyElement(fieldName),
                out T value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public T ReadEncodeableAsExtensionObject<T>(string fieldName)
            where T : IEncodeable
        {
            if (TryGetEncodeableAsExtensionObjectFromElement(
                GetPropertyElement(fieldName),
                out T value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArrayAsExtensionObjects<T>(string fieldName)
            where T : IEncodeable
        {
            if (TryGetEncodeableAsExtensionObjectArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<T> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(
            string fieldName,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            if (TryGetEncodeableArrayFromElement(
                GetPropertyElement(fieldName),
                encodeableTypeId,
                out ArrayOf<T> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(string fieldName)
            where T : IEncodeable, new()
        {
            if (TryGetEncodeableArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<T> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public MatrixOf<T> ReadEncodeableMatrix<T>(
            string fieldName,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            if (TryGetEncodeableMatrixFromElement(
                GetPropertyElement(fieldName),
                encodeableTypeId,
                out MatrixOf<T> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public ushort ReadUInt16(string? fieldName)
        {
            if (TryGetUInt16FromElement(
                GetPropertyElement(fieldName),
                out ushort value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<ushort> ReadUInt16Array(string? fieldName)
        {
            if (TryGetUInt16ArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<ushort> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public uint ReadUInt32(string? fieldName)
        {
            if (TryGetUInt32FromElement(
                GetPropertyElement(fieldName),
                out uint value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<uint> ReadUInt32Array(string? fieldName)
        {
            if (TryGetUInt32ArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<uint> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public ulong ReadUInt64(string? fieldName)
        {
            if (TryGetUInt64FromElement(
                GetPropertyElement(fieldName),
                out ulong value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<ulong> ReadUInt64Array(string? fieldName)
        {
            if (TryGetUInt64ArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<ulong> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public Variant ReadVariant(string? fieldName)
        {
            if (TryGetVariantFromElement(
                GetPropertyElement(fieldName),
                out Variant value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<Variant> ReadVariantArray(string? fieldName)
        {
            if (TryGetVariantArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<Variant> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue(string fieldName, TypeInfo typeInfo)
        {
            if (TryGetVariantValueFromElement(
                GetPropertyElement(fieldName),
                typeInfo,
                true,
                out Variant value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public XmlElement ReadXmlElement(string? fieldName)
        {
            if (TryGetXmlElementFromElement(
                GetPropertyElement(fieldName),
                out XmlElement value))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> ReadXmlElementArray(string? fieldName)
        {
            if (TryGetXmlElementArrayFromElement(
                GetPropertyElement(fieldName),
                out ArrayOf<XmlElement> values))
            {
                return values;
            }
            return DefaultOrThrow(values);
        }

        /// <inheritdoc/>
        public uint ReadEncodingMask(IList<string> masks)
        {
            if (TryGetEncodingMaskFromElement(
                m_stack.Peek(),
                out uint value,
                masks))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public uint ReadSwitchField(IList<string> switches, out string? fieldName)
        {
            if (TryGetSwitchFieldFromElement(
                m_stack.Peek(),
                out fieldName,
                out uint value,
                switches))
            {
                return value;
            }
            return DefaultOrThrow(value);
        }

        /// <inheritdoc/>
        public bool HasField(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName) || m_stack.Count == 0)
            {
                return true;
            }

            JsonElement top = m_stack.Peek();
            return top.ValueKind == JsonValueKind.Object &&
                top.TryGetProperty(fieldName, out _);
        }

        /// <summary>
        /// Get boolean from element
        /// </summary>
        private static bool TryGetBooleanFromElement(JsonElement element, out bool value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.True or JsonValueKind.False:
                    value = element.ValueKind == JsonValueKind.True;
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get boolean values from element
        /// </summary>
        private static bool TryGetBooleanArrayFromElement(
            JsonElement element,
            out ArrayOf<bool> values)
        {
            if (TryGetArrayElements(
                element,
                out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                bool[] result = new bool[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetBooleanFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get byte from element
        /// </summary>
        private static bool TryGetByteFromElement(
            JsonElement element,
            out byte value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Number:
                    return element.TryGetByte(out value);
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get byte values from element
        /// </summary>
        private static bool TryGetByteArrayFromElement(
            JsonElement element,
            out ArrayOf<byte> values)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    values = default;
                    return true;
                case JsonValueKind.String:
                    if (element.TryGetBytesFromBase64(out byte[]? base64))
                    {
                        values = base64;
                        return true;
                    }
                    break;
                case JsonValueKind.Array:
                    bool success = TryGetArrayElements(
                        element,
                        out ArrayOf<JsonElement> elements);
                    Debug.Assert(success, "Must be an array");
                    byte[] bytes = new byte[elements.Count];
                    for (int i = 0; i < elements.Count; i++)
                    {
                        if (!TryGetByteFromElement(elements[i], out bytes[i]))
                        {
                            values = default;
                            return false;
                        }
                    }
                    values = bytes;
                    return true;
                case JsonValueKind.Number:
                    if (TryGetByteFromElement(element, out byte b))
                    {
                        values = [b];
                        return true;
                    }
                    break;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get byte string from element
        /// </summary>
        private static bool TryGetByteStringFromElement(
            JsonElement element,
            out ByteString value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.String:
                    if (!element.TryGetBytesFromBase64(out byte[]? result))
                    {
                        value = default;
                        return false;
                    }
                    value = ByteString.From(result);
                    return true;
                case JsonValueKind.Array:
                    if (!TryGetByteArrayFromElement(
                        element,
                        out ArrayOf<byte> array))
                    {
                        value = default;
                        return false;
                    }
                    value = ByteString.From(array.Span);
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get byte string values from element
        /// </summary>
        private static bool TryGetByteStringArrayFromElement(
            JsonElement element,
            out ArrayOf<ByteString> values)
        {
            if (TryGetArrayElements(
                element,
                out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new ByteString[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetByteStringFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get data value from element
        /// </summary>
        private bool TryGetDataValueFromElement(
            JsonElement element,
            out DataValue? value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Object:
                    // The DataValue is an encoded variant with extra fields in essence.
                    // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.2.18
                    // Read the UaType, Value and optional dimension here and then read
                    // the rest of the fields after pushing the element to the stack.
                    if (!TryGetVariantFromElement(element, out Variant variant))
                    {
                        value = default;
                        return false;
                    }
                    m_stack.Push(element);
                    try
                    {
                        JsonElement statusCodeElement =
                            GetPropertyElement(JsonProperties.StatusCode);
                        JsonElement sourceTimesstampElement =
                            GetPropertyElement(JsonProperties.SourceTimestamp);
                        JsonElement serverTimestampElement =
                            GetPropertyElement(JsonProperties.ServerTimestamp);

                        if (!TryGetStatusCodeFromElement(
                                statusCodeElement,
                                out StatusCode statusCode) ||
                            !TryGetDateTimeFromElement(
                                sourceTimesstampElement,
                                out DateTimeUtc sourceTimestamp) ||
                            !TryGetDateTimeFromElement(
                                serverTimestampElement,
                                out DateTimeUtc serverTimestamp))
                        {
                            value = default;
                            return false;
                        }

                        ushort sourcePicoseconds;
                        if (m_options.ParseStrict &&
                            sourceTimesstampElement.ValueKind == JsonValueKind.Undefined)
                        {
                            // Pico seconds only allowed if source timestamp is present
                            sourcePicoseconds = 0;
                        }
                        else if (!TryGetUInt16FromElement(
                            GetPropertyElement(JsonProperties.SourcePicoseconds),
                            out sourcePicoseconds))
                        {
                            value = default;
                            return false;
                        }
                        ushort serverPicoseconds;
                        if (m_options.ParseStrict &&
                            serverTimestampElement.ValueKind == JsonValueKind.Undefined)
                        {
                            // Pico seconds only allowed if server timestamp is present
                            serverPicoseconds = 0;
                        }
                        else if (!TryGetUInt16FromElement(
                            GetPropertyElement(JsonProperties.ServerPicoseconds),
                            out serverPicoseconds))
                        {
                            value = default;
                            return false;
                        }
                        value = new DataValue(
                            variant,
                            statusCode,
                            sourceTimestamp,
                            serverTimestamp)
                        {
                            SourcePicoseconds = sourcePicoseconds,
                            ServerPicoseconds = serverPicoseconds
                        };
                        return true;
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get data values from element
        /// </summary>
        private bool TryGetDataValueArrayFromElement(
            JsonElement element,
            out ArrayOf<DataValue?> values)
        {
            if (TryGetArrayElements(
                element,
                out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new DataValue?[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetDataValueFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get date time from json element
        /// </summary>
        private static bool TryGetDateTimeFromElement(
            JsonElement element,
            out DateTimeUtc value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = DateTimeUtc.MinValue;
                    return true;
                case JsonValueKind.String when element.TryGetDateTime(out DateTime dt):
                    value = dt;
                    return true;
                default:
                    value = DateTimeUtc.MinValue;
                    return false;
            }
        }

        /// <summary>
        /// Get date time values from element
        /// </summary>
        private static bool TryGetDateTimeArrayFromElement(
            JsonElement element,
            out ArrayOf<DateTimeUtc> values)
        {
            if (TryGetArrayElements(
                element,
                out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new DateTimeUtc[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetDateTimeFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get diagnostic info from element
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private bool TryGetDiagnosticInfoFromElement(
            JsonElement element,
            out DiagnosticInfo? value,
            int depth = 0)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Object:
                    if (depth >= DiagnosticInfo.MaxInnerDepth)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "Maximum nesting level of InnerDiagnosticInfo was exceeded");
                    }
                    m_stack.Push(element);
                    try
                    {
                        JsonElement elem = GetPropertyElement(JsonProperties.SymbolicId);
                        int symbolicId;
                        if (elem.ValueKind == JsonValueKind.Undefined)
                        {
                            symbolicId = -1;
                        }
                        else if (!TryGetInt32FromElement(elem, out symbolicId))
                        {
                            goto default;
                        }
                        int namespaceUri;
                        elem = GetPropertyElement(JsonProperties.NamespaceUri);
                        if (elem.ValueKind == JsonValueKind.Undefined)
                        {
                            namespaceUri = -1;
                        }
                        else if (!TryGetInt32FromElement(elem, out namespaceUri))
                        {
                            goto default;
                        }
                        int locale;
                        elem = GetPropertyElement(JsonProperties.Locale);
                        if (elem.ValueKind == JsonValueKind.Undefined)
                        {
                            locale = -1;
                        }
                        else if (!TryGetInt32FromElement(elem, out locale))
                        {
                            goto default;
                        }
                        int localizedText;
                        elem = GetPropertyElement(JsonProperties.LocalizedText);
                        if (elem.ValueKind == JsonValueKind.Undefined)
                        {
                            localizedText = -1;
                        }
                        else if (!TryGetInt32FromElement(elem, out localizedText))
                        {
                            goto default;
                        }
                        if (!TryGetStringFromElement(
                                GetPropertyElement(JsonProperties.AdditionalInfo),
                                out string? additionalInfo) ||
                            !TryGetStatusCodeFromElement(
                                GetPropertyElement(JsonProperties.InnerStatusCode),
                                out StatusCode innerStatusCode) ||
                            !TryGetDiagnosticInfoFromElement(
                                GetPropertyElement(JsonProperties.InnerDiagnosticInfo),
                                out DiagnosticInfo? innerDiagnosticInfo,
                                ++depth))
                        {
                            goto default;
                        }
                        value = new DiagnosticInfo
                        {
                            SymbolicId = symbolicId,
                            NamespaceUri = namespaceUri,
                            Locale = locale,
                            LocalizedText = localizedText,
                            AdditionalInfo = additionalInfo,
                            InnerStatusCode = innerStatusCode,
                            InnerDiagnosticInfo = innerDiagnosticInfo
                        };
                        return true;
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get diagnostic info values from element
        /// </summary>
        private bool TryGetDiagnosticInfoArrayFromElement(JsonElement element,
            out ArrayOf<DiagnosticInfo?> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new DiagnosticInfo?[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetDiagnosticInfoFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get double from element
        /// </summary>
        private static bool TryGetDoubleFromElement(JsonElement element, out double value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.String when TryGetStringFromElement(
                    element,
                    out string? stringEncoded):
                    return double.TryParse(stringEncoded, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out value);
                case JsonValueKind.Number when element.TryGetDouble(out value):
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get double values from element
        /// </summary>
        private static bool TryGetDoubleArrayFromElement(JsonElement element,
            out ArrayOf<double> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                double[] result = new double[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetDoubleFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get enumeration from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private static bool TryGetEnumerationFromElement<T>(JsonElement element, out T value)
            where T : struct, Enum
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.String:
                    string? text = element.GetString();
                    if (text == null)
                    {
                        value = default;
                        return false;
                    }
                    // Verbose encoding
                    // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.4.1.2
                    int split = text.LastIndexOf('_');
                    string? symbol = text;
                    if (split >= 0)
                    {
                        symbol = text[..split];
                        text = text[(split + 1)..];
                    }
                    if (int.TryParse(text, out int enumValue))
                    {
                        value = EnumHelper.Int32ToEnum<T>(enumValue);
                        return true;
                    }
                    if (Enum.TryParse(symbol, true, out T o))
                    {
                        value = o;
                        return true;
                    }
                    value = default;
                    return false;
                case JsonValueKind.Number when element.TryGetInt32(out int i):
                    // Compact encoding
                    // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.4.1.1
                    value = EnumHelper.Int32ToEnum<T>(i);
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get enumeration from element
        /// </summary>
        private static bool TryGetEnumerationFromElement(JsonElement element, out EnumValue value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.String:
                    string? text = element.GetString();
                    if (text == null)
                    {
                        value = default;
                        return false;
                    }
                    // Verbose encoding
                    // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.4.1.2
                    int split = text.LastIndexOf('_');
                    string? symbol = text;
                    if (split >= 0)
                    {
                        symbol = text[..split];
                        text = text[(split + 1)..];
                    }
                    if (int.TryParse(text, out int enumValue))
                    {
                        value = new EnumValue(enumValue, symbol);
                        return true;
                    }
                    value = default;
                    return false;
                case JsonValueKind.Number when element.TryGetInt32(out int i):
                    // Compact or Variant encoding
                    // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.4.1.1
                    value = (EnumValue)i;
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get enumeration values from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private static bool TryGetEnumerationArrayFromElement<T>(
            JsonElement element,
            out ArrayOf<T> values) where T : struct, Enum
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new T[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetEnumerationFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get enumeration values from element
        /// </summary>
        private static bool TryGetEnumerationArrayFromElement(
            JsonElement element,
            out ArrayOf<EnumValue> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                EnumValue[] result = new EnumValue[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetEnumerationFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get expanded node id from element
        /// </summary>
        private bool TryGetExpandedNodeIdFromElement(
            JsonElement element,
            out ExpandedNodeId value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = ExpandedNodeId.Null;
                    return true;
                case JsonValueKind.String:
                    return ExpandedNodeId.TryParse(
                        Context,
                        element.GetString()!,
                        new NodeIdParsingOptions
                        {
                            UpdateTables = m_options.UpdateNamespaceTable,
                            NamespaceMappings = m_namespaceMappings,
                            ServerMappings = m_serverMappings
                        },
                        out value);
                case JsonValueKind.Number when element.TryGetUInt32(out uint id):
                    value = new ExpandedNodeId(id);
                    return true;
                default:
                    value = ExpandedNodeId.Null;
                    return false;
            }
        }

        /// <summary>
        /// Get expanded node id values from element
        /// </summary>
        private bool TryGetExpandedNodeIdArrayFromElement(
            JsonElement element,
            out ArrayOf<ExpandedNodeId> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new ExpandedNodeId[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetExpandedNodeIdFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get extension object from element
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private bool TryGetExtensionObjectFromElement(
            JsonElement element,
            out ExtensionObject value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = ExtensionObject.Null;
                    return true;
                case JsonValueKind.Object:
                    m_stack.Push(element);
                    try
                    {
                        bool artifactsSuppressed = !TryGetExpandedNodeIdFromElement(
                            GetPropertyElement(JsonProperties.UaTypeId),
                            out ExpandedNodeId typeId);
                        ExpandedNodeId absoluteId = typeId.IsAbsolute
                            ? typeId
                            : NodeId.ToExpandedNodeId(typeId.InnerNodeId, Context.NamespaceUris);
                        if (!typeId.IsNull && absoluteId.IsNull)
                        {
                            m_logger.LogWarning(
                                "Cannot de-serialized extension objects if the " +
                                "NamespaceUri is not in the NamespaceTable: Type = {Type}",
                                typeId);
                        }
                        else
                        {
                            typeId = absoluteId;
                        }
                        if (TryGetByteFromElement(
                            GetPropertyElement(JsonProperties.UaEncoding),
                            out byte encoding))
                        {
                            JsonElement uaBody = GetPropertyElement(JsonProperties.UaBody);
                            switch (encoding)
                            {
                                case 1: // binary
                                    if (TryGetByteStringFromElement(uaBody, out ByteString bytes))
                                    {
                                        value = new ExtensionObject(typeId, bytes);
                                        return true;
                                    }
                                    break;
                                case 2: // xml
                                    if (TryGetXmlElementFromElement(uaBody, out XmlElement xml))
                                    {
                                        value = new ExtensionObject(typeId, xml);
                                        return true;
                                    }
                                    break;
                                case 0: // default
                                case 3: // json
                                    if (!typeId.IsNull && // if artifacts were suppressed (rawdata mode)
                                        Context.Factory.TryGetEncodeableType(
                                            typeId,
                                            out IEncodeableType? activator))
                                    {
                                        IEncodeable encodeable = activator.CreateInstance() ??
                                            throw ServiceResultException.Create(
                                                StatusCodes.BadDecodingError,
                                                "Type does not support IEncodeable interface: '{0}'",
                                                typeId);
                                        try
                                        {
                                            if (!m_options.ParseStrict &&
                                                uaBody.ValueKind != JsonValueKind.Undefined)
                                            {
                                                m_stack.Push(uaBody);
                                            }
                                            encodeable.Decode(this);
                                            value = new ExtensionObject(encodeable);
                                            return true;
                                        }
                                        catch (Exception ex)
                                        {
                                            m_logger.LogInformation(
                                                ex,
                                                "Cannot de-serialized extension object from body.");
                                        }
                                        finally
                                        {
                                            if (!m_options.ParseStrict &&
                                                uaBody.ValueKind != JsonValueKind.Undefined)
                                            {
                                                m_stack.Pop();
                                            }
                                        }
                                    }
                                    // Wrap the raw json inside an extension object
                                    if (!m_options.ParseStrict &&
                                        uaBody.ValueKind != JsonValueKind.Undefined)
                                    {
                                        element = uaBody;
                                    }
                                    value = new ExtensionObject(typeId, element.GetRawText());
                                    return true;
                                default:
                                    throw ServiceResultException.Create(
                                        StatusCodes.BadDecodingError,
                                        $"Encountered unknown encoding type {encoding}.");
                            }
                        }
                        goto default;
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                default:
                    value = ExtensionObject.Null;
                    return false;
            }
        }

        /// <summary>
        /// Get extension object values from element
        /// </summary>
        private bool TryGetExtensionObjectArrayFromElement(
            JsonElement element,
            out ArrayOf<ExtensionObject> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new ExtensionObject[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetExtensionObjectFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get float from element
        /// </summary>
        /// <param name="element"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryGetFloatFromElement(JsonElement element, out float value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.String when TryGetStringFromElement(
                    element,
                    out string? stringEncoded):
                    return float.TryParse(stringEncoded, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out value);
                case JsonValueKind.Number when element.TryGetSingle(out value):
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get float values from element
        /// </summary>
        private static bool TryGetFloatArrayFromElement(JsonElement element,
            out ArrayOf<float> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                float[] result = new float[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetFloatFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get guid from element
        /// </summary>
        private static bool TryGetGuidFromElement(JsonElement element, out Uuid value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = Guid.Empty;
                    return true;
                case JsonValueKind.String:
#if NET9_0_OR_GREATER
                    ReadOnlySpan<byte> utf8Text =
                        JsonMarshal.GetRawUtf8Value(element).Trim((byte)'"');
                    Span<char> chars = stackalloc char[78];
                    if (System.Text.Encoding.UTF8.TryGetChars(utf8Text, chars, out int written))
                    {
                        chars = chars[..written];
                        if (Guid.TryParse(chars, out Guid guid))
                        {
                            value = new Uuid(guid);
                            return true;
                        }
                        Span<byte> bytes = stackalloc byte[16];
                        if (Convert.TryFromBase64Chars(chars, bytes, out written) &&
                            written == 16)
                        {
                            value = new Uuid(new Guid(bytes));
                            return true;
                        }
                    }
#else
                    if (Guid.TryParse(element.GetString(), out Guid guid))
                    {
                        value = new Uuid(guid);
                        return true;
                    }
                    if (element.TryGetBytesFromBase64(out byte[]? bytes) &&
                        bytes.Length == 16)
                    {
                        value = new Uuid(new Guid(bytes));
                        return true;
                    }
#endif
                    value = default;
                    return false;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get guid values from element
        /// </summary>
        private static bool TryGetGuidArrayFromElement(
            JsonElement element,
            out ArrayOf<Uuid> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new Uuid[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetGuidFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get signed short from element
        /// </summary>
        private static bool TryGetInt16FromElement(JsonElement element, out short value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Number:
                    return element.TryGetInt16(out value);
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get short values from element
        /// </summary>
        /// <returns></returns>
        private static bool TryGetInt16ArrayFromElement(
            JsonElement element,
            out ArrayOf<short> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                short[] result = new short[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetInt16FromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get signed integer from element
        /// </summary>
        private static bool TryGetInt32FromElement(JsonElement element, out int value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Number:
                    return element.TryGetInt32(out value);
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get integer values from element
        /// </summary>
        private static bool TryGetInt32ArrayFromElement(
            JsonElement element,
            out ArrayOf<int> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                int[] result = new int[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetInt32FromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get signed long from element
        /// </summary>
        private bool TryGetInt64FromElement(JsonElement element, out long value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.String
                when TryGetStringFromElement(element, out string? stringEncoded):
                    // As per 5.4.2.3, formatted as a decimal number encoded as a JSON string
                    return long.TryParse(stringEncoded, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out value);
                case JsonValueKind.Number
                when !m_options.ParseStrict && element.TryGetInt64(out value):
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get long values from element
        /// </summary>
        private bool TryGetInt64ArrayFromElement(
            JsonElement element,
            out ArrayOf<long> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                long[] result = new long[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetInt64FromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get localized text from element
        /// </summary>
        private bool TryGetLocalizedTextFromElement(
            JsonElement element,
            out LocalizedText value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = LocalizedText.Null;
                    return true;
                case JsonValueKind.Object:
                    m_stack.Push(element);
                    try
                    {
                        if (TryGetStringFromElement(
                                GetPropertyElement(JsonProperties.Locale),
                                out string? locale) &&
                            TryGetStringFromElement(
                                GetPropertyElement(JsonProperties.Text),
                                out string? text))
                        {
                            value = new LocalizedText(locale!, text!);
                            return true;
                        }
                        value = LocalizedText.Null;
                        return false;
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                case JsonValueKind.String:
                    value = LocalizedText.From(element.GetString()!);
                    return true;
                default:
                    value = LocalizedText.Null;
                    return false;
            }
        }

        /// <summary>
        /// Get localized text values from element
        /// </summary>
        private bool TryGetLocalizedTextArrayFromElement(
            JsonElement element,
            out ArrayOf<LocalizedText> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new LocalizedText[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetLocalizedTextFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get node id from element
        /// </summary>
        private bool TryGetNodeIdFromElement(
            JsonElement element,
            out NodeId value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = NodeId.Null;
                    return true;
                case JsonValueKind.String:
                    if (ExpandedNodeId.TryParse(
                        Context,
                        element.GetString()!,
                        new NodeIdParsingOptions
                        {
                            UpdateTables = m_options.UpdateNamespaceTable,
                            NamespaceMappings = m_namespaceMappings,
                            ServerMappings = m_serverMappings
                        },
                        out ExpandedNodeId expandedNodeId))
                    {
                        value = ExpandedNodeId.ToNodeId(
                            expandedNodeId,
                            Context.NamespaceUris,
                            m_options.UpdateNamespaceTable);
                        return true;
                    }
                    value = default;
                    return false;
                case JsonValueKind.Number when element.TryGetUInt32(out uint id):
                    value = new NodeId(id);
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get node id values from element
        /// </summary>
        private bool TryGetNodeIdArrayFromElement(
            JsonElement element,
            out ArrayOf<NodeId> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new NodeId[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetNodeIdFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get qualified name from element
        /// </summary>
        private bool TryGetQualifiedNameFromElement(
            JsonElement element,
            out QualifiedName value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = QualifiedName.Null;
                    return true;
                case JsonValueKind.String:
                    value = QualifiedName.Parse(
                        Context,
                        element.GetString()!,
                        m_options.UpdateNamespaceTable);
                    return true;
                default:
                    value = QualifiedName.Null;
                    return false;
            }
        }

        /// <summary>
        /// Get qualified name values from element
        /// </summary>
        private bool TryGetQualifiedNameArrayFromElement(
            JsonElement element,
            out ArrayOf<QualifiedName> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new QualifiedName[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetQualifiedNameFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get signed byte from element
        /// </summary>
        private static bool TryGetSByteFromElement(JsonElement element, out sbyte value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Number:
                    return element.TryGetSByte(out value);
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get sbyte values from element
        /// </summary>
        private static bool TryGetSByteArrayFromElement(
            JsonElement element,
            out ArrayOf<sbyte> values)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    values = default;
                    return true;
                case JsonValueKind.String:
                    if (!element.TryGetBytesFromBase64(out byte[]? bytes))
                    {
                        values = default;
                        return false;
                    }
                    values = MemoryMarshal.Cast<byte, sbyte>(bytes).ToArray();
                    return true;
                case JsonValueKind.Array:
                    bool success = TryGetArrayElements(
                        element,
                        out ArrayOf<JsonElement> elements);
                    Debug.Assert(success, "Can only be array here");
                    sbyte[] result = new sbyte[elements.Count];
                    for (int i = 0; i < elements.Count; i++)
                    {
                        if (!TryGetSByteFromElement(elements[i], out result[i]))
                        {
                            values = default;
                            return false;
                        }
                    }
                    values = result;
                    return true;
                case JsonValueKind.Number when element.TryGetSByte(out sbyte b):
                    values = [b];
                    return true;
                default:
                    values = default;
                    return false;
            }
        }

        /// <summary>
        /// Get status code from element
        /// </summary>
        private bool TryGetStatusCodeFromElement(
            JsonElement element,
            out StatusCode value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = StatusCodes.Good;
                    return true;
                case JsonValueKind.Object:
                    m_stack.Push(element);
                    try
                    {
                        // Read non reversable encoding
                        if (TryGetUInt32FromElement(GetPropertyElement(JsonProperties.Code), out uint code))
                        {
                            value = new StatusCode(code);
                            return true;
                        }
                        value = default;
                        return false;
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                case JsonValueKind.Number when element.TryGetUInt32(out uint v):
                    value = new StatusCode(v);
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get status code values from element
        /// </summary>
        private bool TryGetStatusCodeArrayFromElement(JsonElement element,
            out ArrayOf<StatusCode> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new StatusCode[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetStatusCodeFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get string from element
        /// </summary>
        private static bool TryGetStringFromElement(JsonElement element, out string? value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.String:
                    value = element.GetString();
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get utf-8 string values from element
        /// </summary>
        /// <param name="element"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        private static bool TryGetStringArrayFromElement(JsonElement element,
            out ArrayOf<string?> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                string?[] result = new string?[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetStringFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get structure as extension object from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private bool TryGetEncodeableAsExtensionObjectFromElement<T>(JsonElement element, out T value)
            where T : IEncodeable
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default!;
                    return true;
                case JsonValueKind.Object:
                    m_stack.Push(element);
                    try
                    {
                        bool artifactsSuppressed = !TryGetExpandedNodeIdFromElement(
                            GetPropertyElement(JsonProperties.UaTypeId),
                            out ExpandedNodeId typeId);
                        ExpandedNodeId absoluteId = typeId.IsAbsolute
                            ? typeId
                            : NodeId.ToExpandedNodeId(typeId.InnerNodeId, Context.NamespaceUris);
                        if (!typeId.IsNull && absoluteId.IsNull)
                        {
                            m_logger.LogWarning(
                                "Cannot de-serialized extension objects if the " +
                                "NamespaceUri is not in the NamespaceTable: Type = {Type}",
                                typeId);
                            goto default;
                        }
                        typeId = absoluteId;
                        if (TryGetByteFromElement(
                            GetPropertyElement(JsonProperties.UaEncoding),
                            out byte encoding))
                        {
                            JsonElement uaBody = GetPropertyElement(JsonProperties.UaBody);
                            switch (encoding)
                            {
                                case 1: // binary
                                    if (TryGetByteStringFromElement(uaBody, out ByteString bytes))
                                    {
                                        using var decoder = new BinaryDecoder(bytes.ToArray(), Context);
                                        value = decoder.ReadEncodeable<T>(null!, typeId);
                                        return true;
                                    }
                                    break;
                                case 2: // xml
                                    if (TryGetXmlElementFromElement(uaBody, out XmlElement xml))
                                    {
                                        System.Xml.XmlElement? xmlElement = xml.AsXmlElement();
                                        if (xmlElement == null)
                                        {
                                            break;
                                        }
                                        using var decoder = new XmlDecoder(xmlElement, Context);
                                        decoder.PushNamespace(xmlElement.NamespaceURI);
                                        value = decoder.ReadEncodeable<T>(xmlElement.LocalName, typeId);
                                        decoder.PopNamespace();
                                        return true;
                                    }
                                    break;
                                case 0: // default
                                case 3: // json
                                    if (Context.Factory.TryGetEncodeableType(
                                        typeId,
                                        out IEncodeableType? activator))
                                    {
                                        value = (T)activator.CreateInstance() ??
                                            throw ServiceResultException.Create(
                                                StatusCodes.BadDecodingError,
                                                "Type does not support IEncodeable interface: '{0}'",
                                                typeId);

                                        if (!m_options.ParseStrict)
                                        {
                                            // Use ua body with json content and decode it
                                            switch (uaBody.ValueKind)
                                            {
                                                case JsonValueKind.Null:
                                                    // Return default
                                                    return true;
                                                case JsonValueKind.Object:
                                                    m_stack.Push(uaBody);
                                                    value.Decode(this);
                                                    m_stack.Pop();
                                                    return true;
                                            }
                                        }

                                        value.Decode(this);
                                        return true;
                                    }
                                    break;
                                default:
                                    throw ServiceResultException.Create(
                                        StatusCodes.BadDecodingError,
                                        $"Encountered unknown encoding type {encoding}.");
                            }
                        }
                        goto default;
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                default:
                    value = default!;
                    return false;
            }
        }

        /// <summary>
        /// Get structure values from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private bool TryGetEncodeableAsExtensionObjectArrayFromElement<T>(JsonElement element, out ArrayOf<T> values)
            where T : IEncodeable
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new T[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetEncodeableAsExtensionObjectFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get structure from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        internal bool TryGetEncodeableFromElement<T>(
            JsonElement element,
            ExpandedNodeId encodeableTypeId,
            out T value)
            where T : IEncodeable
        {
            if (!Context.Factory.TryGetEncodeableType(
                encodeableTypeId,
                out IEncodeableType? activator))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Cannot decode type '{0}'.",
                    encodeableTypeId);
            }

            value = (T)activator.CreateInstance();
            return TryGetEncodeableFromElement(value, element);
        }

        /// <summary>
        /// Get structure from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        internal bool TryGetEncodeableFromElement<T>(JsonElement element, out T value)
            where T : IEncodeable, new()
        {
            value = new T();
            return TryGetEncodeableFromElement(value, element);
        }

        /// <summary>
        /// Get structure from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        internal bool TryGetEncodeableFromElement<T>(T encodeable, JsonElement element)
            where T : IEncodeable
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    return true;
                case JsonValueKind.Object:
                    m_stack.Push(element);
                    try
                    {
                        encodeable.Decode(this);
                        return true;
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                default:
                    return false;
            }
        }

        /// <summary>
        /// Get structure values from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private bool TryGetEncodeableArrayFromElement<T>(
            JsonElement element,
            out ArrayOf<T> values) where T : IEncodeable, new()
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new T[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetEncodeableFromElement(
                        elements[i],
                        out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get structure values from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private bool TryGetEncodeableArrayFromElement<T>(
            JsonElement element,
            ExpandedNodeId encodeableTypeId,
            out ArrayOf<T> values) where T : IEncodeable
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new T[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetEncodeableFromElement(
                        elements[i],
                        encodeableTypeId,
                        out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get structure matrix from element
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private bool TryGetEncodeableMatrixFromElement<T>(
            JsonElement element,
            ExpandedNodeId encodeableTypeId,
            out MatrixOf<T> values) where T : IEncodeable
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    values = default;
                    return true;
                case JsonValueKind.Object:
                    m_stack.Push(element);
                    try
                    {
                        if (TryGetInt32ArrayFromElement(
                                GetPropertyElement(JsonProperties.Dimensions),
                                out ArrayOf<int> dimensions) &&
                            TryGetEncodeableArrayFromElement(
                                GetPropertyElement(JsonProperties.Array),
                                encodeableTypeId,
                                out ArrayOf<T> structures))
                        {
                            values = structures.ToMatrix(dimensions);
                            return true;
                        }
                        values = default;
                        return false;
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                default:
                    values = default;
                    return false;
            }
        }

        /// <summary>
        /// Get unsigned short from element
        /// </summary>
        private static bool TryGetUInt16FromElement(JsonElement element, out ushort value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Number:
                    return element.TryGetUInt16(out value);
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get unsigned short values from element
        /// </summary>
        private static bool TryGetUInt16ArrayFromElement(JsonElement element, out ArrayOf<ushort> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                ushort[] result = new ushort[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetUInt16FromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get unsigned integer from element
        /// </summary>
        private static bool TryGetUInt32FromElement(JsonElement element, out uint value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Number:
                    return element.TryGetUInt32(out value);
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get unsigned int values from element
        /// </summary>
        private static bool TryGetUInt32ArrayFromElement(JsonElement element, out ArrayOf<uint> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                uint[] result = new uint[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetUInt32FromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get unsigned long from element
        /// </summary>
        private bool TryGetUInt64FromElement(JsonElement element, out ulong value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.String
                when TryGetStringFromElement(element, out string? stringEncoded):
                    // As per 5.4.2.3, formatted as a decimal number encoded as a JSON string
                    return ulong.TryParse(stringEncoded, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out value);
                case JsonValueKind.Number
                when !m_options.ParseStrict && element.TryGetUInt64(out value):
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get unsigned long values from element
        /// </summary>
        /// <param name="element"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        private bool TryGetUInt64ArrayFromElement(JsonElement element, out ArrayOf<ulong> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                ulong[] result = new ulong[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetUInt64FromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get variant from element
        /// </summary>
        private bool TryGetVariantFromElement(JsonElement element, out Variant value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Object:
                    m_stack.Push(element);
                    try
                    {
                        JsonElement uaTypeElement = GetPropertyElement(JsonProperties.UaType);
                        JsonElement valueElement = GetPropertyElement(JsonProperties.Value);
                        if (uaTypeElement.ValueKind == JsonValueKind.Undefined)
                        {
                            // To reverse we must have the ua type field. It might have
                            // been suppressed during encoding but could also be that the
                            // value is null, return default here for now without checking
                            // the value element being present (we could do that too)
                            value = default;
                            return true;
                        }

                        if (uaTypeElement.ValueKind != JsonValueKind.Number ||
                            !uaTypeElement.TryGetByte(out byte uaType))
                        {
                            // Throw later the correct exception for bad type
                            uaType = byte.MaxValue;
                        }

                        // Determine the type info to guide how we read the variant
                        JsonElement dimensions =
                            GetPropertyElement(JsonProperties.Dimensions);

                        // Scalar, Array or Matrix (because dimensions might be specified)
                        var typeInfo = TypeInfo.Create(
                            (BuiltInType)uaType,
                            valueElement.ValueKind != JsonValueKind.Array ?
                                ValueRanks.Scalar :
                            dimensions.ValueKind == JsonValueKind.Undefined ?
                                ValueRanks.OneDimension : ValueRanks.TwoDimensions);

                        // Decode the variant
                        if (TryGetVariantValueFromElement(
                            valueElement,
                            typeInfo,
                            false,
                            out value,
                            dimensions))
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        m_stack.Pop();
                    }
                    goto default;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Try get variant value from element
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private bool TryGetVariantValueFromElement(
            JsonElement element,
            TypeInfo typeInfo,
            bool readRawValue,
            out Variant value,
            JsonElement dimensionElement = default)
        {
            if (typeInfo.IsScalar)
            {
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Null:
                        value = Variant.Null;
                        return true;
                    case BuiltInType.Boolean when TryGetBooleanFromElement(
                        element,
                        out bool v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.SByte when TryGetSByteFromElement(
                        element,
                        out sbyte v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Byte when TryGetByteFromElement(
                        element,
                        out byte v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Int16 when TryGetInt16FromElement(
                        element,
                        out short v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.UInt16 when TryGetUInt16FromElement(
                        element,
                        out ushort v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Enumeration when TryGetEnumerationFromElement(
                        element,
                        out EnumValue v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Int32 when TryGetInt32FromElement(
                        element,
                        out int v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.UInt32 when TryGetUInt32FromElement(
                        element,
                        out uint v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Int64 when TryGetInt64FromElement(
                        element,
                        out long v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.UInt64 when TryGetUInt64FromElement(
                        element,
                        out ulong v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Float when TryGetFloatFromElement(
                        element,
                        out float v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Double when TryGetDoubleFromElement(
                        element,
                        out double v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.String when TryGetStringFromElement(
                        element,
                        out string? v):
                        value = Variant.From(v!);
                        return true;
                    case BuiltInType.DateTime when TryGetDateTimeFromElement(
                        element,
                        out DateTimeUtc v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Guid when TryGetGuidFromElement(
                        element,
                        out Uuid v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.ByteString when TryGetByteStringFromElement(
                        element,
                        out ByteString v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.XmlElement when TryGetXmlElementFromElement(
                        element,
                        out XmlElement v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.NodeId when TryGetNodeIdFromElement(
                        element,
                        out NodeId v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.ExpandedNodeId when TryGetExpandedNodeIdFromElement(
                        element,
                        out ExpandedNodeId v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.StatusCode when TryGetStatusCodeFromElement(
                        element,
                        out StatusCode v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.QualifiedName when TryGetQualifiedNameFromElement(
                        element,
                        out QualifiedName v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.LocalizedText when TryGetLocalizedTextFromElement(
                        element,
                        out LocalizedText v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.DataValue when TryGetDataValueFromElement(
                        element,
                        out DataValue? v):
                        value = Variant.From(v!);
                        return true;
                    case BuiltInType.ExtensionObject when TryGetExtensionObjectFromElement(
                        element,
                        out ExtensionObject v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Variant:
                    case BuiltInType.DiagnosticInfo:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unsupported built in type for Variant content ({0}).",
                            typeInfo);
                    default:
                        if (typeInfo.BuiltInType <= BuiltInType.Enumeration)
                        {
                            value = default;
                            return false;
                        }
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unexpected scalar built in type ({0}).",
                            typeInfo);
                }
            }
            else if (typeInfo.IsArray)
            {
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Null:
                        value = Variant.Null;
                        return true;
                    case BuiltInType.Boolean when TryGetBooleanArrayFromElement(
                        element,
                        out ArrayOf<bool> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.SByte when TryGetSByteArrayFromElement(
                        element,
                        out ArrayOf<sbyte> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Byte when TryGetByteArrayFromElement(
                        element,
                        out ArrayOf<byte> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Int16 when TryGetInt16ArrayFromElement(
                        element,
                        out ArrayOf<short> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.UInt16 when TryGetUInt16ArrayFromElement(
                        element,
                        out ArrayOf<ushort> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Enumeration when TryGetEnumerationArrayFromElement(
                        element,
                        out ArrayOf<EnumValue> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Int32 when TryGetInt32ArrayFromElement(
                        element,
                        out ArrayOf<int> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.UInt32 when TryGetUInt32ArrayFromElement(
                        element,
                        out ArrayOf<uint> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Int64 when TryGetInt64ArrayFromElement(
                        element,
                        out ArrayOf<long> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.UInt64 when TryGetUInt64ArrayFromElement(
                        element,
                        out ArrayOf<ulong> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Float when TryGetFloatArrayFromElement(
                        element,
                        out ArrayOf<float> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Double when TryGetDoubleArrayFromElement(
                        element,
                        out ArrayOf<double> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.String when TryGetStringArrayFromElement(
                        element,
                        out ArrayOf<string?> v):
#pragma warning disable CS8620 // Argument cannot be used due to nullability differences. ArrayOf<string?> and ArrayOf<string> share runtime layout; null elements are tolerated by Variant.
                        value = Variant.From(v);
#pragma warning restore CS8620
                        return true;
                    case BuiltInType.DateTime when TryGetDateTimeArrayFromElement(
                        element,
                        out ArrayOf<DateTimeUtc> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Guid when TryGetGuidArrayFromElement(
                        element,
                        out ArrayOf<Uuid> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.ByteString when TryGetByteStringArrayFromElement(
                        element, out ArrayOf<ByteString> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.XmlElement when TryGetXmlElementArrayFromElement(
                        element,
                        out ArrayOf<XmlElement> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.NodeId when TryGetNodeIdArrayFromElement(
                        element,
                        out ArrayOf<NodeId> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.ExpandedNodeId when TryGetExpandedNodeIdArrayFromElement(
                        element,
                        out ArrayOf<ExpandedNodeId> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.StatusCode when TryGetStatusCodeArrayFromElement(
                        element,
                        out ArrayOf<StatusCode> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.QualifiedName when TryGetQualifiedNameArrayFromElement(
                        element,
                        out ArrayOf<QualifiedName> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.LocalizedText when TryGetLocalizedTextArrayFromElement(
                        element,
                        out ArrayOf<LocalizedText> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.DataValue when TryGetDataValueArrayFromElement(
                        element,
                        out ArrayOf<DataValue?> v):
#pragma warning disable CS8620 // Argument cannot be used due to nullability differences. ArrayOf<DataValue?> and ArrayOf<DataValue> share runtime layout; null elements are tolerated by Variant.
                        value = Variant.From(v);
#pragma warning restore CS8620
                        return true;
                    case BuiltInType.ExtensionObject when TryGetExtensionObjectArrayFromElement(
                        element,
                        out ArrayOf<ExtensionObject> v):
                        value = Variant.From(v);
                        return true;
                    case BuiltInType.Variant:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        if (TryGetVariantArrayFromElement(element, out ArrayOf<Variant> varray))
                        {
                            value = Variant.From(varray);
                            return true;
                        }
                        goto default;
                    case BuiltInType.DiagnosticInfo:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unsupported built in type for Variant content ({0}).",
                            typeInfo);
                    default:
                        if (typeInfo.BuiltInType <= BuiltInType.Enumeration)
                        {
                            value = default;
                            return false;
                        }
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Unexpected scalar built in type ({0}).",
                            typeInfo);
                }
            }
            else
            {
                if (readRawValue)
                {
                    // If reading raw value, then the eleemnt we are reading is encoded
                    // using array encoding with both Array and Dimensions properties
                    // see https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.5
                    m_stack.Push(element);
                    element = GetPropertyElement(JsonProperties.Array);
                    dimensionElement = GetPropertyElement(JsonProperties.Dimensions);
                }
                else if (dimensionElement.ValueKind == JsonValueKind.Undefined)
                {
                    // If we read a variant, then the dimenions is part of the parent
                    // object (e.g. DataValue or Variant. To read pop back to parent
                    // get dimension and push back parent to the stack. But only if
                    // the dimension element was not passed already (short cut)
                    JsonElement parent = m_stack.Pop();
                    dimensionElement = GetPropertyElement(JsonProperties.Dimensions);
                    m_stack.Push(parent);
                }

                // Read dimension array
                if (!TryGetInt32ArrayFromElement(
                    dimensionElement,
                    out ArrayOf<int> dims) ||
                    dims.Count < 2) // Must have at least 2 dimensions
                {
                    value = default;
                    return false;
                }
                try
                {
                    switch (typeInfo.BuiltInType)
                    {
                        case BuiltInType.Null:
                            value = Variant.Null;
                            return true;
                        case BuiltInType.Boolean when TryGetBooleanArrayFromElement(
                            element,
                            out ArrayOf<bool> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.SByte when TryGetSByteArrayFromElement(
                            element,
                            out ArrayOf<sbyte> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.Byte when TryGetByteArrayFromElement(
                            element,
                            out ArrayOf<byte> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.Int16 when TryGetInt16ArrayFromElement(
                            element,
                            out ArrayOf<short> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.UInt16 when TryGetUInt16ArrayFromElement(
                            element,
                            out ArrayOf<ushort> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.Enumeration when TryGetEnumerationArrayFromElement(
                            element,
                            out ArrayOf<EnumValue> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.Int32 when TryGetInt32ArrayFromElement(
                            element,
                            out ArrayOf<int> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.UInt32 when TryGetUInt32ArrayFromElement(
                            element,
                            out ArrayOf<uint> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.Int64 when TryGetInt64ArrayFromElement(
                            element,
                            out ArrayOf<long> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.UInt64 when TryGetUInt64ArrayFromElement(
                            element,
                            out ArrayOf<ulong> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.Float when TryGetFloatArrayFromElement(
                            element,
                            out ArrayOf<float> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.Double when TryGetDoubleArrayFromElement(
                            element,
                            out ArrayOf<double> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.String when TryGetStringArrayFromElement(
                            element,
                            out ArrayOf<string?> v):
#pragma warning disable CS8620 // Argument cannot be used due to nullability differences. MatrixOf<string?> and MatrixOf<string> share runtime layout; null elements are tolerated by Variant.
                            value = Variant.From(v.ToMatrix(dims));
#pragma warning restore CS8620
                            return true;
                        case BuiltInType.DateTime when TryGetDateTimeArrayFromElement(
                            element,
                            out ArrayOf<DateTimeUtc> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.Guid when TryGetGuidArrayFromElement(
                            element,
                            out ArrayOf<Uuid> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.ByteString when TryGetByteStringArrayFromElement(
                            element, out ArrayOf<ByteString> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.XmlElement when TryGetXmlElementArrayFromElement(
                            element,
                            out ArrayOf<XmlElement> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.NodeId when TryGetNodeIdArrayFromElement(
                            element,
                            out ArrayOf<NodeId> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.ExpandedNodeId when TryGetExpandedNodeIdArrayFromElement(
                            element,
                            out ArrayOf<ExpandedNodeId> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.StatusCode when TryGetStatusCodeArrayFromElement(
                            element,
                            out ArrayOf<StatusCode> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.QualifiedName when TryGetQualifiedNameArrayFromElement(
                            element,
                            out ArrayOf<QualifiedName> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.LocalizedText when TryGetLocalizedTextArrayFromElement(
                            element,
                            out ArrayOf<LocalizedText> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.DataValue when TryGetDataValueArrayFromElement(
                            element,
                            out ArrayOf<DataValue?> v):
#pragma warning disable CS8620 // Argument cannot be used due to nullability differences. MatrixOf<DataValue?> and MatrixOf<DataValue> share runtime layout; null elements are tolerated by Variant.
                            value = Variant.From(v.ToMatrix(dims));
#pragma warning restore CS8620
                            return true;
                        case BuiltInType.ExtensionObject when TryGetExtensionObjectArrayFromElement(
                            element,
                            out ArrayOf<ExtensionObject> v):
                            value = Variant.From(v.ToMatrix(dims));
                            return true;
                        case BuiltInType.Variant:
                        case BuiltInType.Number:
                        case BuiltInType.Integer:
                        case BuiltInType.UInteger:
                            if (TryGetVariantArrayFromElement(element, out ArrayOf<Variant> varray))
                            {
                                value = Variant.From(varray.ToMatrix(dims));
                                return true;
                            }
                            goto default;
                        case BuiltInType.DiagnosticInfo:
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Unsupported built in type for Variant content ({0}).",
                                typeInfo);
                        default:
                            if (typeInfo.BuiltInType <= BuiltInType.Enumeration)
                            {
                                value = default;
                                return false;
                            }
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Unexpected scalar built in type ({0}).",
                                typeInfo);
                    }
                }
                finally
                {
                    if (readRawValue)
                    {
                        // Pop the array object
                        m_stack.Pop();
                    }
                }
            }
        }

        /// <summary>
        /// Get variant values from element
        /// </summary>
        /// <param name="element"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        private bool TryGetVariantArrayFromElement(
            JsonElement element,
            out ArrayOf<Variant> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new Variant[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetVariantFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Get xml element from json element
        /// </summary>
        private static bool TryGetXmlElementFromElement(
            JsonElement element,
            out XmlElement value)
        {
            if (TryGetStringFromElement(element, out string? xmlString))
            {
                value = new XmlElement(xmlString);
                return true;
            }
            value = XmlElement.Empty;
            return false;
        }

        /// <summary>
        /// Get xml element values from element
        /// </summary>
        private static bool TryGetXmlElementArrayFromElement(
            JsonElement element,
            out ArrayOf<XmlElement> values)
        {
            if (TryGetArrayElements(element, out ArrayOf<JsonElement> elements))
            {
                if (elements.IsNull)
                {
                    values = default;
                    return true;
                }
                var result = new XmlElement[elements.Count];
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryGetXmlElementFromElement(elements[i], out result[i]))
                    {
                        values = default;
                        return false;
                    }
                }
                values = result;
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>
        /// Try get switch field from current element
        /// </summary>
        private bool TryGetSwitchFieldFromElement(
            JsonElement element,
            out string? fieldName,
            out uint value,
            IList<string>? switches = null)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    fieldName = default;
                    value = default;
                    return true;
                case JsonValueKind.Object:
                    long index = -1;
                    if (TryGetUInt32FromElement(
                        GetPropertyElement(JsonProperties.SwitchField),
                        out uint switchFieldIndex))
                    {
                        index = switchFieldIndex;
                    }

                    fieldName = default;
                    if (switches == null)
                    {
                        value = 0;
                        return true;
                    }
                    if (index >= switches.Count)
                    {
                        value = (uint)index;
                        return true;
                    }

                    if (index >= 0)
                    {
                        // Switch field index found, resolve it
                        JsonElement valueElement = GetPropertyElement("Value");
                        if (valueElement.ValueKind == JsonValueKind.Undefined)
                        {
                            fieldName = switches[(int)(index - 1)];
                        }
                        else
                        {
                            fieldName = "Value";
                        }
                        value = (uint)index;
                        return true;
                    }

                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (property.NameEquals(JsonProperties.UaTypeId))
                        {
                            continue;
                        }
                        index = switches.IndexOf(property.Name);
                        if (index >= 0)
                        {
                            fieldName = property.Name;
                            value = (uint)(index + 1);
                            return true;
                        }
                    }
                    value = 0;
                    return true;
                default:
                    value = default;
                    fieldName = default;
                    return false;
            }
        }

        /// <summary>
        /// Try get encoding mask from the current object
        /// </summary>
        private bool TryGetEncodingMaskFromElement(
            JsonElement element,
            out uint value,
            IList<string>? masks = null)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    value = default;
                    return true;
                case JsonValueKind.Object:
                    if (TryGetUInt32FromElement(
                        GetPropertyElement(JsonProperties.EncodingMask),
                        out value))
                    {
                        return true;
                    }
                    value = default;
                    if (masks != null)
                    {
                        foreach (string fieldName in masks)
                        {
                            JsonElement found = GetPropertyElement(fieldName);
                            if (found.ValueKind != JsonValueKind.Undefined)
                            {
                                int index = masks.IndexOf(fieldName);
                                if (index >= 0)
                                {
                                    value |= (uint)(1 << index);
                                }
                            }
                        }
                    }
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        /// <summary>
        /// Get message from element
        /// </summary>
        /// <typeparam name="T">The type of the message to read</typeparam>
        public bool TryGetMessageFromElement<T>(JsonElement element, out T value)
            where T : IEncodeable
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (m_options.UpdateNamespaceTable)
                    {
                        TryGetStringArrayFromElement(
                            GetPropertyElement("NamespaceUris"),
                            out ArrayOf<string?> namespaceUris);
                        TryGetStringArrayFromElement(
                            GetPropertyElement("ServerUris"),
                            out ArrayOf<string?> serverUris);
                        if (namespaceUris.Count > 0 || serverUris.Count > 0)
                        {
                            NamespaceTable namespaces =
                                namespaceUris.Count == 0
                                    ? Context.NamespaceUris
                                    : new NamespaceTable(namespaceUris.ToArray()!.Cast<string>());
                            StringTable servers =
                                serverUris.Count == 0
                                    ? Context.ServerUris
                                    : new StringTable(serverUris.ToArray()!.Cast<string>());

                            SetMappingTables(namespaces, servers);
                        }
                    }
                    if (TryGetNodeIdFromElement(
                            GetPropertyElement(JsonProperties.TypeId),
                            out NodeId typeId) &&
                        !typeId.IsNull &&
                        TryGetEncodeableFromElement(
                            GetPropertyElement(JsonProperties.UaBody),
                            typeId,
                            out value))
                    {
                        return true;
                    }
                    goto default;
                default:
                    value = default!;
                    return false;
            }
        }

        /// <summary>
        /// Returns a default value or throws
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
#pragma warning disable IDE0060 // Remove unused parameter
        private T DefaultOrThrow<T>(T returnedValue)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            if (m_options.ParseStrict)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Parsing encountered invalid information. {0}",
                    m_stack.Peek().GetRawText());
            }
            return default!;
        }

        /// <summary>
        /// Try get top element or named element from object
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private JsonElement GetPropertyElement(string? fieldName)
        {
            if (m_stack.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Expected element at top of stack");
            }

            JsonElement o = m_stack.Peek();
            if (o.ValueKind != JsonValueKind.Object)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Expected object at top of stack");
            }
            else if (o.TryGetProperty(fieldName!, out JsonElement element))
            {
                return element;
            }
#if CASE_INSENSITIVE_FIELD_MATCHING // Perf - make it an option
            else
            {
                // Try case insensitive
                var pn = Encoding.UTF8.GetString(fieldName);
                foreach (var p in o.EnumerateObject())
                {
                    if (p.Name.Equals(pn, StringComparison.OrdinalIgnoreCase))
                    {
                        return p.Value;
                    }
                }
            }
#endif
            return default;
        }

        /// <summary>
        /// Get array elements
        /// </summary>
        /// <param name="element"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        private static bool TryGetArrayElements(
            JsonElement element,
            out ArrayOf<JsonElement> values)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    values = default;
                    return true; // Default is empty array
                case JsonValueKind.Array:
                    if (element.GetArrayLength() == 0)
                    {
                        values = [];
                        return true;
                    }
                    var result = new List<JsonElement>(element.GetArrayLength());
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Array)
                        {
                            GetValuesFromArray(item, ref result);
                        }
                        else
                        {
                            result.Add(item);
                        }

                        static void GetValuesFromArray(JsonElement array,
                            ref List<JsonElement> elements)
                        {
                            foreach (JsonElement element in array.EnumerateArray())
                            {
                                if (element.ValueKind == JsonValueKind.Array)
                                {
                                    GetValuesFromArray(element, ref elements);
                                }
                                else
                                {
                                    elements.Add(element);
                                }
                            }
                        }
                    }
                    values = result;
                    return true;
                default:
                    values = default;
                    return false;
            }
        }

        /// <summary>
        /// Create parsing options
        /// </summary>
        private static JsonDocumentOptions ParseOptions(int maxDepth)
        {
            return new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                MaxDepth = maxDepth
            };
        }

        /// <summary>
        /// Handle parser exceptions
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [DoesNotReturn]
        private static void HandleParsingError(Exception ex)
        {
            switch (ex)
            {
                case JsonException jre when jre.Message.Contains(
                    "maximum configured depth",
                    StringComparison.Ordinal):
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "Invalid json document.", jre);
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "Invalid json document.", ex);
            }
        }

        private readonly ILogger m_logger;
        private ushort[]? m_namespaceMappings;
        private ushort[]? m_serverMappings;
        private readonly Stack<JsonElement> m_stack = new();
        private readonly JsonDocument m_document;
        private readonly JsonDecoderOptions m_options;
        private bool m_disposed;
    }
}
