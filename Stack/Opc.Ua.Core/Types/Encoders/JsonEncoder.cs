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
using System.Xml;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
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
        private bool m_forceNamespaceUri;
        private bool m_forceNamespaceUriForIndex1;
        private bool m_includeDefaultNumberValues;
        private bool m_includeDefaultValues;
        private bool m_encodeNodeIdAsString;

        [Flags]
        private enum EscapeOptions
        {
            None = 0,
            Quotes = 1,
            NoValueEscape = 2,
            NoFieldNameEscape = 4
        }

        /// <summary>
        /// Initializes the object with default values.
        /// Selects the reversible or non reversible encoding.
        /// </summary>
        public JsonEncoder(IServiceMessageContext context, bool useReversibleEncoding)
            : this(
                context,
                useReversibleEncoding
                    ? JsonEncodingType.Reversible
                    : JsonEncodingType.NonReversible,
                false,
                null,
                false)
        {
        }

        /// <summary>
        /// Initializes the object with default values.
        /// Selects the reversible or non reversible encoding.
        /// </summary>
        public JsonEncoder(
            IServiceMessageContext context,
            bool useReversibleEncoding,
            bool topLevelIsArray = false,
            Stream stream = null,
            bool leaveOpen = false,
            int streamSize = kStreamWriterBufferSize)
            : this(
                context,
                useReversibleEncoding
                    ? JsonEncodingType.Reversible
                    : JsonEncodingType.NonReversible,
                topLevelIsArray,
                stream,
                leaveOpen,
                streamSize)
        {
        }

        /// <summary>
        /// Initializes the object with default values.
        /// Selects the reversible or non reversible encoding.
        /// </summary>
        public JsonEncoder(
            IServiceMessageContext context,
            bool useReversibleEncoding,
            StreamWriter streamWriter,
            bool topLevelIsArray = false)
            : this(
                context,
                useReversibleEncoding
                    ? JsonEncodingType.Reversible
                    : JsonEncodingType.NonReversible,
                streamWriter,
                topLevelIsArray)
        {
        }

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
             : this(encoding)
        {
            Context = context;
            m_logger = context.Telemetry.CreateLogger<JsonEncoder>();
            m_stream = stream;
            m_leaveOpen = leaveOpen;
            m_topLevelIsArray = topLevelIsArray;

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
            : this(encoding)
        {
            Context = context;
            m_logger = context.Telemetry.CreateLogger<JsonEncoder>();
            m_writer = writer;
            m_topLevelIsArray = topLevelIsArray;

            if (m_writer == null)
            {
                m_stream = new MemoryStream();
                m_writer = new StreamWriter(m_stream, s_utf8Encoding, kStreamWriterBufferSize);
            }

            InitializeWriter();
        }

        /// <summary>
        /// Sets default values.
        /// </summary>
        private JsonEncoder(JsonEncodingType encoding)
        {
            // defaults for JSON encoding
            EncodingToUse = encoding;
            if (encoding is JsonEncodingType.Reversible or JsonEncodingType.NonReversible)
            {
                // defaults for reversible and non reversible JSON encoding
                // -- encode namespace index for reversible encoding / uri for non reversible
                // -- do not include default values for reversible encoding
                // -- include default values for non reversible encoding
                m_forceNamespaceUri =
                    m_forceNamespaceUriForIndex1 =
                    m_includeDefaultValues =
                        encoding == JsonEncodingType.NonReversible;
                m_includeDefaultNumberValues = true;
                m_encodeNodeIdAsString = false;
            }
            else
            {
                // defaults for compact and verbose JSON encoding, properties throw exception if modified
                m_forceNamespaceUri = true;
                m_forceNamespaceUriForIndex1 = true;
                m_includeDefaultValues = encoding == JsonEncodingType.Verbose;
                m_includeDefaultNumberValues = encoding == JsonEncodingType.Verbose;
                m_encodeNodeIdAsString = true;
            }
            m_inVariantWithEncoding = IncludeDefaultValues;
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
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var stream = new MemoryStream(buffer, true);
            using var encoder = new JsonEncoder(context, true, false, stream);
            // encode message
            encoder.EncodeMessage(message);
            int length = encoder.Close();

            return new ArraySegment<byte>(buffer, 0, length);
        }

        /// <summary>
        /// Encodes a message with its header.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
        public void EncodeMessage(IEncodeable message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            // convert the namespace uri to an index.
            var typeId = ExpandedNodeId.ToNodeId(message.TypeId, Context.NamespaceUris);

            // write the type id.
            WriteNodeId("TypeId", typeId);

            // write the message.
            WriteEncodeable("Body", message, message.GetType());
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

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
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
                    Utils.SilentDispose(m_memoryStream);
                    Utils.SilentDispose(m_stream);
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

        /// <inheritdoc/>
        [Obsolete(
            "Non/Reversible encoding is deprecated. Use UsingAlternateEncoding instead to support new encoding types."
        )]
        public void UsingReversibleEncoding<T>(
            Action<string, T> action,
            string fieldName,
            T value,
            bool useReversibleEncoding)
        {
            JsonEncodingType currentValue = EncodingToUse;
            try
            {
                EncodingToUse = useReversibleEncoding
                    ? JsonEncodingType.Reversible
                    : JsonEncodingType.NonReversible;
                action(fieldName, value);
            }
            finally
            {
                EncodingToUse = currentValue;
            }
        }

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
                case JsonEncodingType.Reversible:
                    fieldName = "Value";
                    break;
                case JsonEncodingType.Verbose:
                case JsonEncodingType.NonReversible:
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
            if ((!SuppressArtifacts && EncodingToUse == JsonEncodingType.Compact) ||
                EncodingToUse == JsonEncodingType.Reversible)
            {
                WriteUInt32("EncodingMask", encodingMask);
            }
        }

        /// <inheritdoc/>
        public EncodingType EncodingType => EncodingType.Json;

        /// <inheritdoc/>
        public bool UseReversibleEncoding => EncodingToUse != JsonEncodingType.NonReversible;

        /// <summary>
        /// The message context associated with the encoder.
        /// </summary>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// The Json encoder to encoder namespace URI instead of
        /// namespace Index in NodeIds.
        /// </summary>
        public bool ForceNamespaceUri
        {
            get => m_forceNamespaceUri;
            set => m_forceNamespaceUri = ThrowIfCompactOrVerbose(value);
        }

        /// <summary>
        /// The Json encoder to encode namespace URI for all
        /// namespaces
        /// </summary>
        public bool ForceNamespaceUriForIndex1
        {
            get => m_forceNamespaceUriForIndex1;
            set => m_forceNamespaceUriForIndex1 = ThrowIfCompactOrVerbose(value);
        }

        /// <summary>
        /// The Json encoder default value option.
        /// </summary>
        public bool IncludeDefaultValues
        {
            get => m_includeDefaultValues;
            set => m_includeDefaultValues = ThrowIfCompactOrVerbose(value);
        }

        /// <summary>
        /// The Json encoder default value option for numbers.
        /// </summary>
        public bool IncludeDefaultNumberValues
        {
            get => m_includeDefaultNumberValues || m_includeDefaultValues;
            set => m_includeDefaultNumberValues = ThrowIfCompactOrVerbose(value);
        }

        /// <summary>
        /// The Json encoder default encoding for NodeId as string or object.
        /// </summary>
        public bool EncodeNodeIdAsString
        {
            get => m_encodeNodeIdAsString;
            set => m_encodeNodeIdAsString = ThrowIfCompactOrVerbose(value);
        }

        /// <summary>
        /// Pushes a namespace onto the namespace stack.
        /// </summary>
        public void PushNamespace(string namespaceUri)
        {
            m_namespaces.Push(namespaceUri);
        }

        /// <summary>
        /// Pops a namespace from the namespace stack.
        /// </summary>
        public void PopNamespace()
        {
            m_namespaces.Pop();
        }

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

        /// <summary>
        /// Writes a boolean to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes a sbyte to the stream.
        /// </summary>
        public void WriteSByte(string fieldName, sbyte value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes a byte to the stream.
        /// </summary>
        public void WriteByte(string fieldName, byte value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes a short to the stream.
        /// </summary>
        public void WriteInt16(string fieldName, short value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes a ushort to the stream.
        /// </summary>
        public void WriteUInt16(string fieldName, ushort value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes an int to the stream.
        /// </summary>
        public void WriteInt32(string fieldName, int value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes a uint to the stream.
        /// </summary>
        public void WriteUInt32(string fieldName, uint value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes a long to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes a ulong to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes a float to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes a double to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes a string to the stream.
        /// </summary>
        public void WriteString(string fieldName, string value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == null)
            {
                return;
            }

            WriteSimpleField(fieldName, value, EscapeOptions.Quotes);
        }

        /// <summary>
        /// Writes a UTC date/time to the stream.
        /// </summary>
        public void WriteDateTime(string fieldName, DateTime value)
        {
            WriteDateTime(fieldName, value, EscapeOptions.None);
        }

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
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

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        public void WriteByteString(string fieldName, byte[] value)
        {
            WriteByteString(fieldName, value, 0, (value?.Length) ?? 0);
        }

        /// <summary>
        /// Writes a byte string to the stream with a given index and count.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteString(string fieldName, byte[] value, int index, int count)
        {
            if (fieldName != null && !IncludeDefaultValues && value == null)
            {
                return;
            }

            if (value == null)
            {
                WriteSimpleField(fieldName, kNull, EscapeOptions.NoValueEscape);
                return;
            }

            // check the length.
            if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            WriteSimpleField(
                fieldName,
                Convert.ToBase64String(value, index, count),
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

        /// <summary>
        /// Writes an XmlElement to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteXmlElement(string fieldName, XmlElement value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == null)
            {
                return;
            }

            if (value == null)
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

        private void WriteNamespaceIndex(string fieldName, ushort namespaceIndex)
        {
            if (namespaceIndex == 0)
            {
                return;
            }

            if ((!UseReversibleEncoding || ForceNamespaceUri) &&
                namespaceIndex > (ForceNamespaceUriForIndex1 ? 0 : 1))
            {
                string uri = Context.NamespaceUris.GetString(namespaceIndex);
                if (!string.IsNullOrEmpty(uri))
                {
                    WriteSimpleField(fieldName, uri, EscapeOptions.Quotes);
                    return;
                }
            }

            if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
            {
                namespaceIndex = m_namespaceMappings[namespaceIndex];
            }

            if (namespaceIndex != 0)
            {
                WriteUInt16(fieldName, namespaceIndex);
            }
        }

        private void WriteNodeIdContents(NodeId value, string namespaceUri = null)
        {
            if (value.IdType > IdType.Numeric)
            {
                WriteInt32("IdType", (int)value.IdType);
            }
            if (value.TryGetIdentifier(out uint numericId))
            {
                WriteUInt32("Id", numericId);
            }
            else if (value.TryGetIdentifier(out string stringId))
            {
                WriteString("Id", stringId);
            }
            else if (value.TryGetIdentifier(out Guid guidIdentifier))
            {
                WriteGuid("Id", guidIdentifier);
            }
            else if (value.TryGetIdentifier(out byte[] opaqueId))
            {
                WriteByteString("Id", opaqueId);
            }
            else
            {
                throw ServiceResultException.Unexpected(
                    $"Unexpected Node IdType {value.IdType}");
            }
            if (namespaceUri != null)
            {
                WriteString("Namespace", namespaceUri);
            }
            else
            {
                WriteNamespaceIndex("Namespace", value.NamespaceIndex);
            }
        }

        /// <summary>
        /// Writes an NodeId to the stream.
        /// </summary>
        public void WriteNodeId(string fieldName, NodeId value)
        {
            bool isNull = value.IsNullNodeId;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            if (m_encodeNodeIdAsString)
            {
                WriteSimpleField(
                    fieldName,
                    isNull ? string.Empty : value.Format(Context, ForceNamespaceUri),
                    EscapeOptions.Quotes);
                return;
            }

            PushStructure(fieldName);

            if (!isNull)
            {
                ushort namespaceIndex = value.NamespaceIndex;
                if (ForceNamespaceUri && namespaceIndex > (ForceNamespaceUriForIndex1 ? 0 : 1))
                {
                    string namespaceUri = Context.NamespaceUris.GetString(namespaceIndex);
                    WriteNodeIdContents(value, namespaceUri);
                }
                else
                {
                    WriteNodeIdContents(value);
                }
            }

            PopStructure();
        }

        /// <summary>
        /// Writes an ExpandedNodeId to the stream.
        /// </summary>
        public void WriteExpandedNodeId(string fieldName, ExpandedNodeId value)
        {
            bool isNull = value.IsNull;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            if (m_encodeNodeIdAsString)
            {
                WriteSimpleField(
                    fieldName,
                    isNull ? string.Empty : value.Format(Context, ForceNamespaceUri),
                    EscapeOptions.Quotes);
                return;
            }

            PushStructure(fieldName);

            if (!isNull)
            {
                string namespaceUri = value.NamespaceUri;
                ushort namespaceIndex = value.InnerNodeId.NamespaceIndex;
                if (ForceNamespaceUri &&
                    namespaceUri == null &&
                    namespaceIndex > (ForceNamespaceUriForIndex1 ? 0 : 1))
                {
                    namespaceUri = Context.NamespaceUris.GetString(namespaceIndex);
                }
                WriteNodeIdContents(value.InnerNodeId, namespaceUri);

                uint serverIndex = value.ServerIndex;

                if (serverIndex >= 1)
                {
                    if (EncodingToUse == JsonEncodingType.NonReversible)
                    {
                        string uri = Context.ServerUris.GetString(serverIndex);

                        if (!string.IsNullOrEmpty(uri))
                        {
                            WriteSimpleField(
                                "ServerUri",
                                uri,
                                EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);
                        }

                        PopStructure();
                        return;
                    }

                    if (m_serverMappings != null && m_serverMappings.Length > serverIndex)
                    {
                        serverIndex = m_serverMappings[serverIndex];
                    }

                    if (serverIndex != 0)
                    {
                        WriteUInt32("ServerUri", serverIndex);
                    }
                }
            }

            PopStructure();
        }

        /// <summary>
        /// Writes an StatusCode to the stream.
        /// </summary>
        public void WriteStatusCode(string fieldName, StatusCode value)
        {
            WriteStatusCode(fieldName, value, EscapeOptions.None);
        }

        /// <summary>
        /// Writes a DiagnosticInfo to the stream.
        /// </summary>
        public void WriteDiagnosticInfo(string fieldName, DiagnosticInfo value)
        {
            WriteDiagnosticInfo(fieldName, value, 0);
        }

        /// <summary>
        /// Writes an QualifiedName to the stream.
        /// </summary>
        public void WriteQualifiedName(string fieldName, QualifiedName value)
        {
            bool isNull = value.IsNullQn;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            if (m_encodeNodeIdAsString)
            {
                WriteSimpleField(
                    fieldName,
                    isNull ? string.Empty : value.Format(Context, ForceNamespaceUri),
                    EscapeOptions.Quotes);
                return;
            }

            PushStructure(fieldName);

            if (!isNull)
            {
                WriteString("Name", value.Name);
                WriteNamespaceIndex("Uri", value.NamespaceIndex);
            }

            PopStructure();
        }

        /// <summary>
        /// Writes an LocalizedText to the stream.
        /// </summary>
        public void WriteLocalizedText(string fieldName, LocalizedText value)
        {
            bool isNull = value.IsNullOrEmpty;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            if (EncodingToUse == JsonEncodingType.NonReversible)
            {
                WriteSimpleField(
                    fieldName,
                    isNull ? string.Empty : value.Text,
                    EscapeOptions.Quotes);
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

        /// <summary>
        /// Writes an Variant to the stream.
        /// </summary>
        public void WriteVariant(string fieldName, Variant value)
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
                return;
            }

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            CheckAndIncrementNestingLevel();

            try
            {
                if (!isNull && EncodingToUse != JsonEncodingType.NonReversible)
                {
                    PushStructure(fieldName);

                    // encode enums as int32.
                    byte encodingByte = (byte)value.TypeInfo.BuiltInType;

                    if (value.TypeInfo.BuiltInType == BuiltInType.Enumeration)
                    {
                        encodingByte = (byte)BuiltInType.Int32;
                    }

                    if (!SuppressArtifacts)
                    {
                        WriteByte("Type", encodingByte);
                    }

                    fieldName = "Body";
                }

                if (m_commaRequired)
                {
                    m_writer.Write(kComma);
                }

                if (!string.IsNullOrEmpty(fieldName))
                {
                    EscapeString(fieldName);
                    m_writer.Write(kQuotationColon);
                }

                WriteVariantContents(value.Value, value.TypeInfo);

                if (!isNull && EncodingToUse != JsonEncodingType.NonReversible)
                {
                    if (value.Value is Matrix matrix)
                    {
                        WriteInt32Array("Dimensions", matrix.Dimensions);
                    }

                    PopStructure();
                }
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        private void WriteVariantIntoObject(string fieldName, Variant value)
        {
            if (Variant.Null == value)
            {
                return;
            }

            try
            {
                CheckAndIncrementNestingLevel();

                bool isNull =
                    value.TypeInfo.IsUnknown ||
                    value.TypeInfo.BuiltInType == BuiltInType.Null ||
                    value.IsNull;

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

                if (m_commaRequired)
                {
                    m_writer.Write(kComma);
                }

                if (!string.IsNullOrEmpty(fieldName))
                {
                    EscapeString(fieldName);
                    m_writer.Write(kQuotationColon);
                    m_commaRequired = false;
                }

                if (value.Value is Matrix matrix)
                {
                    WriteVariantContents(value.Value, value.TypeInfo);
                    WriteInt32Array("Dimensions", matrix.Dimensions);
                    return;
                }

                WriteVariantContents(value.Value, value.TypeInfo);
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
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
                    if (EncodingToUse is not JsonEncodingType.Compact and not JsonEncodingType.Verbose)
                    {
                        WriteVariant("Value", value.WrappedValue);
                    }
                    else
                    {
                        WriteVariantIntoObject("Value", value.WrappedValue);
                    }
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

        /// <summary>
        /// Writes an ExtensionObject to the stream.
        /// </summary>
        public void WriteExtensionObject(string fieldName, ExtensionObject value)
        {
            bool isNull = value.IsNull || value.Encoding == ExtensionObjectEncoding.None;

            if (fieldName != null && isNull && !IncludeDefaultValues)
            {
                return;
            }

            var encodeable = value.Body as IEncodeable;

            if (encodeable != null && EncodingToUse == JsonEncodingType.NonReversible)
            {
                // non reversible encoding, only the content of the Body field is encoded.
                if (value.Body is IStructureTypeInfo structureType &&
                    structureType.StructureType == StructureType.Union)
                {
                    if (m_commaRequired)
                    {
                        m_writer.Write(kComma);
                    }

                    if (string.IsNullOrEmpty(fieldName))
                    {
                        fieldName = "Value";
                    }

                    EscapeString(fieldName);
                    m_writer.Write(kQuotationColon);
                    encodeable.Encode(this);
                    return;
                }

                PushStructure(fieldName);
                encodeable.Encode(this);
                PopStructure();
                return;
            }

            PushStructure(fieldName);

            ExpandedNodeId typeId = !value.TypeId.IsNull
                ? value.TypeId
                : encodeable?.TypeId ?? NodeId.Null;
            var localTypeId = ExpandedNodeId.ToNodeId(typeId, Context.NamespaceUris);

            if (EncodingToUse is JsonEncodingType.Compact or JsonEncodingType.Verbose)
            {
                if (encodeable != null)
                {
                    if (!SuppressArtifacts && !localTypeId.IsNullNodeId)
                    {
                        WriteNodeId("UaTypeId", localTypeId);
                    }

                    encodeable.Encode(this);
                }
                else if (value.Body is JObject json)
                {
                    if (!SuppressArtifacts && !localTypeId.IsNullNodeId)
                    {
                        WriteNodeId("UaTypeId", localTypeId);
                        m_writer.Write(kComma);
                    }

                    string text = json.ToString(Newtonsoft.Json.Formatting.None);
                    m_writer.Write(text[1..^1]);
                }
                else if (value.Encoding == ExtensionObjectEncoding.Binary)
                {
                    if (!SuppressArtifacts && !localTypeId.IsNullNodeId)
                    {
                        WriteNodeId("UaTypeId", localTypeId);
                    }

                    WriteByte("UaEncoding", (byte)ExtensionObjectEncoding.Binary);
                    WriteByteString("UaBody", value.Body as byte[]);
                }
                else if (value.Encoding == ExtensionObjectEncoding.Xml)
                {
                    if (!SuppressArtifacts && !localTypeId.IsNullNodeId)
                    {
                        WriteNodeId("UaTypeId", localTypeId);
                    }

                    WriteByte("UaEncoding", (byte)ExtensionObjectEncoding.Xml);
                    WriteXmlElement("UaBody", value.Body as XmlElement);
                }

                PopStructure();
                return;
            }

            WriteNodeId("TypeId", localTypeId);

            if (encodeable != null)
            {
                WriteEncodeable("Body", encodeable, null);
            }
            else if (value.Body is JObject json)
            {
                string text = json.ToString(Newtonsoft.Json.Formatting.None);
                m_writer.Write(text[1..^1]);
            }
            else
            {
                WriteByte("Encoding", (byte)value.Encoding);

                if (value.Encoding == ExtensionObjectEncoding.Binary)
                {
                    WriteByteString("Body", value.Body as byte[]);
                }
                else if (value.Encoding == ExtensionObjectEncoding.Xml)
                {
                    WriteXmlElement("Body", value.Body as XmlElement);
                }
                else if (value.Encoding == ExtensionObjectEncoding.Json)
                {
                    WriteSimpleField("Body", value.Body as string);
                }
            }

            PopStructure();
        }

        /// <summary>
        /// Writes an encodeable object to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteEncodeable(string fieldName, IEncodeable value, Type systemType)
        {
            bool isNull = value == null;

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

        /// <summary>
        /// Writes an enumerated value to the stream.
        /// </summary>
        public void WriteEnumerated(string fieldName, Enum value)
        {
            int numeric = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            string numericString = numeric.ToString(CultureInfo.InvariantCulture);

            if (EncodingToUse is JsonEncodingType.Reversible or JsonEncodingType.Compact)
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
                        Utils.Format("{0}_{1}", valueString, numeric),
                        EscapeOptions.Quotes);
                }
            }
        }

        /// <summary>
        /// Writes an enumerated Int32 value to the stream.
        /// </summary>
        public void WriteEnumerated(string fieldName, int numeric)
        {
            bool writeNumber
                = EncodingToUse is JsonEncodingType.Reversible or JsonEncodingType.Compact;
            string numericString = numeric.ToString(CultureInfo.InvariantCulture);
            WriteSimpleField(
                fieldName,
                numericString,
                writeNumber ? EscapeOptions.None : EscapeOptions.Quotes);
        }

        /// <summary>
        /// Writes a boolean array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteBooleanArray(string fieldName, IList<bool> values)
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

        /// <summary>
        /// Writes a sbyte array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteSByteArray(string fieldName, IList<sbyte> values)
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

        /// <summary>
        /// Writes a byte array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteArray(string fieldName, IList<byte> values)
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

        /// <summary>
        /// Writes a short array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteInt16Array(string fieldName, IList<short> values)
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

        /// <summary>
        /// Writes a ushort array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteUInt16Array(string fieldName, IList<ushort> values)
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

        /// <summary>
        /// Writes a int array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteInt32Array(string fieldName, IList<int> values)
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

        /// <summary>
        /// Writes a uint array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteUInt32Array(string fieldName, IList<uint> values)
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

        /// <summary>
        /// Writes a long array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteInt64Array(string fieldName, IList<long> values)
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

        /// <summary>
        /// Writes a ulong array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteUInt64Array(string fieldName, IList<ulong> values)
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

        /// <summary>
        /// Writes a float array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteFloatArray(string fieldName, IList<float> values)
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

        /// <summary>
        /// Writes a double array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteDoubleArray(string fieldName, IList<double> values)
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

        /// <summary>
        /// Writes a string array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteStringArray(string fieldName, IList<string> values)
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

        /// <summary>
        /// Writes a UTC date/time array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteDateTimeArray(string fieldName, IList<DateTime> values)
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

        /// <summary>
        /// Writes a GUID array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteGuidArray(string fieldName, IList<Uuid> values)
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

        /// <summary>
        /// Writes a byte string array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteByteStringArray(string fieldName, IList<byte[]> values)
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

        /// <summary>
        /// Writes an XmlElement array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteXmlElementArray(string fieldName, IList<XmlElement> values)
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

        /// <summary>
        /// Writes an NodeId array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteNodeIdArray(string fieldName, IList<NodeId> values)
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

        /// <summary>
        /// Writes an ExpandedNodeId array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteExpandedNodeIdArray(string fieldName, IList<ExpandedNodeId> values)
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

        /// <summary>
        /// Writes an StatusCode array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteStatusCodeArray(string fieldName, IList<StatusCode> values)
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
                if (!UseReversibleEncoding && values[ii] == StatusCodes.Good)
                {
                    WriteSimpleFieldNull(null);
                }
                else
                {
                    WriteStatusCode(null, values[ii]);
                }
            }

            PopArray();
        }

        /// <summary>
        /// Writes an DiagnosticInfo array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteDiagnosticInfoArray(string fieldName, IList<DiagnosticInfo> values)
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

        /// <summary>
        /// Writes an QualifiedName array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteQualifiedNameArray(string fieldName, IList<QualifiedName> values)
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

        /// <summary>
        /// Writes an LocalizedText array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteLocalizedTextArray(string fieldName, IList<LocalizedText> values)
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

        /// <summary>
        /// Writes an Variant array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteVariantArray(string fieldName, IList<Variant> values)
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

        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteDataValueArray(string fieldName, IList<DataValue> values)
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

        /// <summary>
        /// Writes an extension object array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteExtensionObjectArray(string fieldName, IList<ExtensionObject> values)
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

        /// <summary>
        /// Writes an encodeable object array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteEncodeableArray(
            string fieldName,
            IList<IEncodeable> values,
            Type systemType)
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
                    WriteEncodeable(null, values[ii], systemType);
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
                    WriteEncodeable(null, values[ii], systemType);
                }

                PopArray();
            }
        }

        /// <summary>
        /// Writes an enumerated value array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteEnumeratedArray(string fieldName, Array values, Type systemType)
        {
            if (values == null || values.Length == 0)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < values.Length)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            // encode each element in the array.
            Type arrayType = values.GetType().GetElementType();
            if (arrayType.IsEnum)
            {
                foreach (Enum value in values)
                {
                    WriteEnumerated(null, value);
                }
            }
            else
            {
                if (arrayType != typeof(int))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingError,
                        Utils.Format(
                            "Type '{0}' is not allowed in an Enumeration.",
                            arrayType.FullName));
                }
                foreach (int value in values)
                {
                    WriteEnumerated(null, value);
                }
            }

            PopArray();
        }

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
                        WriteByteStringArray(fieldName, (byte[][])array);
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

        /// <summary>
        /// Writes a raw value.
        /// </summary>
        public void WriteRawValue(FieldMetaData field, DataValue dv, DataSetFieldContentMask mask)
        {
            m_nestingLevel++;

            try
            {
                if (m_commaRequired)
                {
                    m_writer.Write(kComma);
                }

                EscapeString(field.Name);
                m_writer.Write(kQuotationColon);
                m_commaRequired = false;
                bool dimensionsInline = false;

                if (mask is not DataSetFieldContentMask.None and not DataSetFieldContentMask.RawData)
                {
                    m_writer.Write(kLeftCurlyBrace);
                    m_writer.Write(kQuotation);
                    m_writer.Write("Value");
                    m_writer.Write(kQuotationColon);
                    dimensionsInline = true;
                }

                if (mask == DataSetFieldContentMask.None && StatusCode.IsBad(dv.StatusCode))
                {
                    dv = new DataValue { WrappedValue = dv.StatusCode };
                }

                WriteRawValueContents(field, dv, dimensionsInline);

                if (mask is not DataSetFieldContentMask.None and not DataSetFieldContentMask.RawData)
                {
                    if ((mask & DataSetFieldContentMask.StatusCode) != 0 &&
                        dv.StatusCode != StatusCodes.Good)
                    {
                        WriteStatusCode(nameof(dv.StatusCode), dv.StatusCode);
                    }

                    if ((mask & DataSetFieldContentMask.SourceTimestamp) != 0 &&
                        dv.SourceTimestamp != DateTime.MinValue)
                    {
                        WriteDateTime(nameof(dv.SourceTimestamp), dv.SourceTimestamp);

                        if (dv.SourcePicoseconds != 0)
                        {
                            WriteUInt16(nameof(dv.SourcePicoseconds), dv.SourcePicoseconds);
                        }
                    }

                    if ((mask & DataSetFieldContentMask.ServerTimestamp) != 0 &&
                        dv.ServerTimestamp != DateTime.MinValue)
                    {
                        WriteDateTime(nameof(dv.ServerTimestamp), dv.ServerTimestamp);

                        if (dv.ServerPicoseconds != 0)
                        {
                            WriteUInt16(nameof(dv.ServerPicoseconds), dv.ServerPicoseconds);
                        }
                    }

                    m_writer.Write(kRightCurlyBrace);
                }

                m_commaRequired = true;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        private void WriteRawExtensionObject(object value)
        {
            if (value is ExtensionObject eo)
            {
                value = eo.Body;
            }

            if (value is IEncodeable encodeable)
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

        private void WriteRawValueContents(FieldMetaData field, DataValue dv, bool dimensionsInline)
        {
            object value = dv.Value;
            TypeInfo typeInfo = dv.WrappedValue.TypeInfo;

            if (dv.WrappedValue == Variant.Null)
            {
                value = TypeInfo.GetDefaultValue(field.BuiltInType, field.ValueRank);
                typeInfo = new TypeInfo((BuiltInType)field.BuiltInType, field.ValueRank);

                if (value != null)
                {
                    WriteVariantContents(value, typeInfo);
                }
                else if (field.ValueRank >= 0)
                {
                    m_writer.Write(kLeftSquareBracket);
                    m_writer.Write(kRightSquareBracket);
                }
                else if (field.BuiltInType == (byte)BuiltInType.ExtensionObject)
                {
                    m_writer.Write(kLeftCurlyBrace);
                    m_writer.Write(kRightCurlyBrace);
                }
                else
                {
                    m_writer.Write(kNull);
                }

                m_commaRequired = true;
                return;
            }

            if (field.ValueRank == ValueRanks.Scalar)
            {
                if (field.BuiltInType == (byte)BuiltInType.ExtensionObject)
                {
                    WriteRawExtensionObject(value);
                    return;
                }
            }
            else
            {
                if (value is Matrix matrix)
                {
                    if (!dimensionsInline)
                    {
                        PushStructure(null);
                    }

                    PushArray(!dimensionsInline ? "Array" : null);

                    foreach (object ii in matrix.Elements)
                    {
                        if (m_commaRequired)
                        {
                            m_writer.Write(kComma);
                        }

                        if (field.BuiltInType == (byte)BuiltInType.ExtensionObject)
                        {
                            m_commaRequired = false;
                            WriteRawExtensionObject(ii);
                            m_commaRequired = true;
                            continue;
                        }
                        else if (field.BuiltInType == (byte)BuiltInType.Variant)
                        {
                            m_commaRequired = false;

                            if (ii is Variant vt)
                            {
                                WriteVariant(null, vt);
                            }
                            else
                            {
                                m_writer.Write(kNull);
                            }

                            m_commaRequired = true;
                            continue;
                        }

                        WriteVariantContents(
                            ii,
                            new TypeInfo((BuiltInType)field.BuiltInType, ValueRanks.Scalar));
                        m_commaRequired = true;
                    }

                    PopArray();
                    WriteInt32Array("Dimensions", matrix.Dimensions);
                    if (!dimensionsInline)
                    {
                        PopStructure();
                    }

                    m_commaRequired = true;
                    return;
                }

                if (field.BuiltInType == (byte)BuiltInType.ExtensionObject &&
                    value is IList<ExtensionObject> list)
                {
                    PushArray(null);

                    foreach (ExtensionObject element in list)
                    {
                        WriteRawExtensionObject(element);
                    }

                    PopArray();
                    m_commaRequired = true;
                    return;
                }

                if (field.BuiltInType == (byte)BuiltInType.Variant && value is IList<Variant>)
                {
                    WriteRawVariantArray(value);
                    return;
                }
            }

            WriteVariantContents(value, typeInfo);

            if (EncodingToUse == JsonEncodingType.Reversible)
            {
                if (dv.Value is Matrix matrix)
                {
                    WriteInt32Array("Dimensions", matrix.Dimensions);
                }

                m_writer.Write(kRightCurlyBrace);
            }
        }

        /// <summary>
        /// Writes the contents of a Variant to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteVariantContents(object value, TypeInfo typeInfo)
        {
            bool inVariantWithEncoding = m_inVariantWithEncoding;
            try
            {
                m_inVariantWithEncoding = true;

                // check for null.
                if (value == null)
                {
                    return;
                }

                m_commaRequired = false;

                // write scalar.
                if (typeInfo.ValueRank < 0)
                {
                    switch (typeInfo.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            WriteBoolean(null, (bool)value);
                            return;
                        case BuiltInType.SByte:
                            WriteSByte(null, (sbyte)value);
                            return;
                        case BuiltInType.Byte:
                            WriteByte(null, (byte)value);
                            return;
                        case BuiltInType.Int16:
                            WriteInt16(null, (short)value);
                            return;
                        case BuiltInType.UInt16:
                            WriteUInt16(null, (ushort)value);
                            return;
                        case BuiltInType.Int32:
                            WriteInt32(null, (int)value);
                            return;
                        case BuiltInType.UInt32:
                            WriteUInt32(null, (uint)value);
                            return;
                        case BuiltInType.Int64:
                            WriteInt64(null, (long)value);
                            return;
                        case BuiltInType.UInt64:
                            WriteUInt64(null, (ulong)value);
                            return;
                        case BuiltInType.Float:
                            WriteFloat(null, (float)value);
                            return;
                        case BuiltInType.Double:
                            WriteDouble(null, (double)value);
                            return;
                        case BuiltInType.String:
                            WriteString(null, (string)value);
                            return;
                        case BuiltInType.DateTime:
                            WriteDateTime(null, (DateTime)value);
                            return;
                        case BuiltInType.Guid:
                            WriteGuid(null, (Uuid)value);
                            return;
                        case BuiltInType.ByteString:
                            WriteByteString(null, (byte[])value);
                            return;
                        case BuiltInType.XmlElement:
                            WriteXmlElement(null, (XmlElement)value);
                            return;
                        case BuiltInType.NodeId:
                            WriteNodeId(null, (NodeId)value);
                            return;
                        case BuiltInType.ExpandedNodeId:
                            WriteExpandedNodeId(null, (ExpandedNodeId)value);
                            return;
                        case BuiltInType.StatusCode:
                            WriteStatusCode(null, (StatusCode)value);
                            return;
                        case BuiltInType.QualifiedName:
                            WriteQualifiedName(null, (QualifiedName)value);
                            return;
                        case BuiltInType.LocalizedText:
                            WriteLocalizedText(null, (LocalizedText)value);
                            return;
                        case BuiltInType.ExtensionObject:
                            WriteExtensionObject(null, (ExtensionObject)value);
                            return;
                        case BuiltInType.DataValue:
                            WriteDataValue(null, (DataValue)value);
                            return;
                        case BuiltInType.Enumeration:
                            WriteEnumerated(null, (Enum)value);
                            return;
                        case BuiltInType.DiagnosticInfo:
                            WriteDiagnosticInfo(null, (DiagnosticInfo)value);
                            return;
                        case BuiltInType.Null:
                        case BuiltInType.Variant:
                        case BuiltInType.Number:
                        case BuiltInType.Integer:
                        case BuiltInType.UInteger:
                            // Should this not throw?
                            break;
                        default:
                            throw ServiceResultException.Unexpected(
                                $"Unexpected BuiltInType {typeInfo.BuiltInType}");
                    }
                }
                // write array
                else if (typeInfo.ValueRank >= ValueRanks.OneDimension)
                {
                    int valueRank = typeInfo.ValueRank;
                    if (EncodingToUse != JsonEncodingType.NonReversible && value is Matrix matrix)
                    {
                        // linearize the matrix
                        value = matrix.Elements;
                        valueRank = ValueRanks.OneDimension;
                    }
                    WriteArray(null, value, valueRank, typeInfo.BuiltInType);
                }
            }
            finally
            {
                m_inVariantWithEncoding = inVariantWithEncoding;
            }
        }

        /// <summary>
        /// Writes a Variant array to the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void WriteObjectArray(string fieldName, IList<object> values)
        {
            if (CheckForSimpleFieldNull(fieldName, values))
            {
                return;
            }

            PushArray(fieldName);

            if (values != null &&
                Context.MaxArrayLength > 0 &&
                Context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            if (values != null)
            {
                for (int ii = 0; ii < values.Count; ii++)
                {
                    WriteVariant("Variant", new Variant(values[ii]));
                }
            }

            PopArray();
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

            if (EncodingToUse == JsonEncodingType.Reversible)
            {
                WriteUInt32(fieldName, value.Code);
                return;
            }

            PushStructure(fieldName, escapeOptions);

            if (!isNull)
            {
                WriteUInt32("Code", value.Code);

                if (EncodingToUse is JsonEncodingType.NonReversible or JsonEncodingType.Verbose)
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
        private bool CheckForSimpleFieldNull<T>(string fieldName, IList<T> values)
        {
            // always include default values for non reversible/verbose
            // include default values when encoding in a Variant
            if (values == null ||
                (values.Count == 0 && !m_inVariantWithEncoding && !m_includeDefaultValues))
            {
                WriteSimpleFieldNull(fieldName);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Called on properties which can only be modified for the deprecated encoding.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        private bool ThrowIfCompactOrVerbose(bool value)
        {
            if (EncodingToUse is JsonEncodingType.Compact or JsonEncodingType.Verbose)
            {
                throw new NotSupportedException(
                    $"This property can not be modified with {EncodingToUse} encoding.");
            }
            return value;
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
        /// Encode the Matrix as Dimensions/Array element.
        /// Writes the matrix as a flattended array with dimensions.
        /// Validates the dimensions and array size.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteArrayDimensionMatrix(
            string fieldName,
            BuiltInType builtInType,
            Matrix matrix)
        {
            // check if matrix is well formed
            (bool valid, int sizeFromDimensions) = Matrix.ValidateDimensions(
                true,
                matrix.Dimensions,
                Context.MaxArrayLength,
                m_logger);

            if (!valid || (sizeFromDimensions != matrix.Elements.Length))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "The number of elements in the matrix does not match the dimensions.");
            }

            PushStructure(fieldName);
            WriteInt32Array("Dimensions", matrix.Dimensions);
            WriteArray("Array", matrix.Elements, 1, builtInType);
            PopStructure();
        }

        /// <summary>
        /// Write multi dimensional array in structure.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WriteStructureMatrix(
            string fieldName,
            Matrix matrix,
            int dim,
            ref int index,
            TypeInfo typeInfo)
        {
            // check if matrix is well formed
            (bool valid, int sizeFromDimensions) = Matrix.ValidateDimensions(
                true,
                matrix.Dimensions,
                Context.MaxArrayLength,
                m_logger);

            if (!valid || (sizeFromDimensions != matrix.Elements.Length))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "The number of elements in the matrix does not match the dimensions.");
            }

            CheckAndIncrementNestingLevel();

            try
            {
                int arrayLen = matrix.Dimensions[dim];
                if (dim == matrix.Dimensions.Length - 1)
                {
                    // Create a slice of values for the top dimension
                    var copy = Array.CreateInstance(
                        matrix.Elements.GetType().GetElementType(),
                        arrayLen);
                    Array.Copy(matrix.Elements, index, copy, 0, arrayLen);
                    // Write slice as value rank
                    if (m_commaRequired)
                    {
                        m_writer.Write(kComma);
                    }
                    WriteVariantContents(copy, TypeInfo.CreateArray(typeInfo.BuiltInType));
                    index += arrayLen;
                }
                else
                {
                    PushArray(fieldName);
                    for (int i = 0; i < arrayLen; i++)
                    {
                        WriteStructureMatrix(null, matrix, dim + 1, ref index, typeInfo);
                    }
                    PopArray();
                }
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

        /// <summary>
        /// The length of the DateTime string encoded by "o"
        /// </summary>
        internal const int DateTimeRoundTripKindLength = 28;

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
        internal static void ConvertUniversalTimeToString(
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
        internal static string ConvertUniversalTimeToString(DateTime value)
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
    }
}
