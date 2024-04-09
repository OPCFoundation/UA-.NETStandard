/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Writes objects to a JSON stream.
    /// </summary>
    public class JsonEncoder : IJsonEncoder
    {
        #region Private Fields
        private const int kStreamWriterBufferSize = 1024;
        private static readonly string s_quotationColon = "\":";
        private static readonly char s_comma = ',';
        private static readonly char s_quotation = '\"';
        private static readonly char s_backslash = '\\';
        private static readonly char s_leftCurlyBrace = '{';
        private static readonly char s_rightCurlyBrace = '}';
        private static readonly char s_leftSquareBracket = '[';
        private static readonly char s_rightSquareBracket = ']';
        private static readonly UTF8Encoding s_utf8Encoding = new UTF8Encoding(false);
        private static readonly string s_null = "null";
        private Stream m_stream;
        private MemoryStream m_memoryStream;
        private StreamWriter m_writer;
        private Stack<string> m_namespaces;
        private bool m_commaRequired;
        private bool m_inVariantWithEncoding;
        private IServiceMessageContext m_context;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;
        private bool m_topLevelIsArray;
        private bool m_levelOneSkipped;
        private bool m_dontWriteClosing;
        private bool m_leaveOpen;

        [Flags]
        private enum EscapeOptions : int
        {
            None = 0,
            Quotes = 1,
            NoValueEscape = 2,
            NoFieldNameEscape = 4,
        }
        #endregion

        #region Constructors
        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="useReversibleEncoding"></param>
        public JsonEncoder(
            IServiceMessageContext context,
            bool useReversibleEncoding) :
            this(context, useReversibleEncoding, false, null, false)
        {
        }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public JsonEncoder(
            IServiceMessageContext context,
            bool useReversibleEncoding,
            bool topLevelIsArray = false,
            Stream stream = null,
            bool leaveOpen = false,
            int streamSize = kStreamWriterBufferSize
            )
        {
            Initialize();

            m_context = context;
            m_stream = stream;
            m_leaveOpen = leaveOpen;
            UseReversibleEncoding = useReversibleEncoding;
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
            bool useReversibleEncoding,
            StreamWriter writer,
            bool topLevelIsArray = false)
        {
            Initialize();

            m_context = context;
            m_writer = writer;
            UseReversibleEncoding = useReversibleEncoding;
            m_topLevelIsArray = topLevelIsArray;

            if (m_writer == null)
            {
                m_stream = new MemoryStream();
                m_writer = new StreamWriter(m_stream, s_utf8Encoding, kStreamWriterBufferSize);
            }

            InitializeWriter();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_stream = null;
            m_writer = null;
            m_namespaces = new Stack<string>();
            m_commaRequired = false;
            m_leaveOpen = false;
            m_nestingLevel = 0;
            m_levelOneSkipped = false;

            // defaults for JSON encoding
            // -- encode namespace index for reversible encoding
            // -- do not include default values for built in types
            //    which are not a Number or a bool
            // -- include default values for numbers and bool
            ForceNamespaceUri = false;
            IncludeDefaultValues = false;
            IncludeDefaultNumberValues = true;
        }

        /// <summary>
        /// Initialize Writer.
        /// </summary>
        private void InitializeWriter()
        {
            if (m_topLevelIsArray)
            {
                m_writer.Write(s_leftSquareBracket);
            }
            else
            {
                m_writer.Write(s_leftCurlyBrace);
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Encodes a session-less message to a buffer.
        /// </summary>
        public static void EncodeSessionLessMessage(IEncodeable message, Stream stream, IServiceMessageContext context, bool leaveOpen)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (context == null) throw new ArgumentNullException(nameof(context));

            // create encoder.
            JsonEncoder encoder = new JsonEncoder(context, true, false, stream, leaveOpen);
            try
            {
                long start = stream.Position;

                // write the message.
                var envelope = new SessionLessServiceMessage {
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris,
                    Message = message
                };

                envelope.Encode(encoder);

                // check that the max message size was not exceeded.
                if (context.MaxMessageSize > 0 && context.MaxMessageSize < (int)(stream.Position - start))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "MaxMessageSize {0} < {1}",
                        context.MaxMessageSize,
                        (int)(stream.Position - start));
                }

                encoder.Close();
            }
            finally
            {
                if (leaveOpen)
                {
                    stream.Position = 0;
                }
                encoder.Dispose();
            }
        }

        /// <summary>
        /// Encodes a message in a stream.
        /// </summary>
        public static ArraySegment<byte> EncodeMessage(IEncodeable message, byte[] buffer, IServiceMessageContext context)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (context == null) throw new ArgumentNullException(nameof(context));

            using (MemoryStream stream = new MemoryStream(buffer, true))
            using (var encoder = new JsonEncoder(context, true, false, stream))
            {
                // encode message
                encoder.EncodeMessage(message);
                int length = encoder.Close();

                return new ArraySegment<byte>(buffer, 0, length);
            }
        }

        /// <summary>
        /// Encodes a message with its header.
        /// </summary>
        public void EncodeMessage(IEncodeable message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            // convert the namespace uri to an index.
            NodeId typeId = ExpandedNodeId.ToNodeId(message.TypeId, m_context.NamespaceUris);

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

            if (namespaceUris != null && m_context.NamespaceUris != null)
            {
                m_namespaceMappings = namespaceUris.CreateMapping(m_context.NamespaceUris, false);
            }

            m_serverMappings = null;

            if (serverUris != null && m_context.ServerUris != null)
            {
                m_serverMappings = serverUris.CreateMapping(m_context.ServerUris, false);
            }
        }

        /// <summary>
        /// Completes writing and returns the JSON text.
        /// </summary>
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
                    throw new NotSupportedException("Cannot get text from external stream. Use Close or MemoryStream instead.");
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
        public int Close() => InternalClose(true);
        #endregion

        #region IDisposable Members
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
        #endregion

        #region IJsonEncodeable Members
        /// <inheritdoc/>
        public void PushStructure(string fieldName)
        {
            m_nestingLevel++;

            if (m_commaRequired)
            {
                m_writer.Write(s_comma);
            }

            if (!string.IsNullOrEmpty(fieldName))
            {
                m_writer.Write(s_quotation);
                EscapeString(fieldName);
                m_writer.Write(s_quotationColon);
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
            m_writer.Write(s_leftCurlyBrace);
        }

        /// <inheritdoc/>
        public void PushArray(string fieldName)
        {
            m_nestingLevel++;

            if (m_commaRequired)
            {
                m_writer.Write(s_comma);
            }

            if (!string.IsNullOrEmpty(fieldName))
            {
                m_writer.Write(s_quotation);
                EscapeString(fieldName);
                m_writer.Write(s_quotationColon);
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
            m_writer.Write(s_leftSquareBracket);
        }

        /// <inheritdoc/>
        public void PopStructure()
        {
            if (m_nestingLevel > 1 || m_topLevelIsArray ||
               (m_nestingLevel == 1 && !m_levelOneSkipped))
            {
                m_writer.Write(s_rightCurlyBrace);
                m_commaRequired = true;
            }

            m_nestingLevel--;
        }

        /// <inheritdoc/>
        public void PopArray()
        {
            if (m_nestingLevel > 1 || m_topLevelIsArray ||
               (m_nestingLevel == 1 && !m_levelOneSkipped))
            {
                m_writer.Write(s_rightSquareBracket);
                m_commaRequired = true;
            }

            m_nestingLevel--;
        }

        /// <inheritdoc/>
        public void UsingReversibleEncoding<T>(Action<string, T> action, string fieldName, T value, bool useReversibleEncoding)
        {
            bool currentValue = UseReversibleEncoding;
            try
            {
                UseReversibleEncoding = useReversibleEncoding;
                action(fieldName, value);
            }
            finally
            {
                UseReversibleEncoding = currentValue;
            }
        }
        #endregion

        #region IEncoder Members
        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Json;

        /// <summary>
        /// The message context associated with the encoder.
        /// </summary>
        public IServiceMessageContext Context => m_context;

        /// <summary>
        /// The Json encoder reversible encoding option
        /// </summary>
        public bool UseReversibleEncoding { get; private set; }

        /// <summary>
        /// The Json encoder to encoder namespace URI instead of
        /// namespace Index in NodeIds.
        /// </summary>
        public bool ForceNamespaceUri { get; set; }

        /// <summary>
        /// The Json encoder to encode namespace URI for all
        /// namespaces
        /// </summary>
        public bool ForceNamespaceUriForIndex1 { get; set; }

        /// <summary>
        /// The Json encoder default value option.
        /// </summary>
        public bool IncludeDefaultValues { get; set; }

        /// <summary>
        /// The Json encoder default value option.
        /// </summary>
        public bool IncludeDefaultNumberValues { get; set; }

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

        private static readonly char[] m_specialChars = new char[] { s_quotation, s_backslash, '\n', '\r', '\t', '\b', '\f', };
        private static readonly char[] m_substitution = new char[] { s_quotation, s_backslash, 'n', 'r', 't', 'b', 'f' };

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Using a span to escape the string, write strings to stream writer if possible.
        /// </summary>
        /// <param name="value"></param>
        private void EscapeString(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();
            int lastOffset = 0;

            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        WriteSpan(ref lastOffset, charSpan, i);
                        m_writer.Write('\\');
                        m_writer.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    WriteSpan(ref lastOffset, charSpan, i);
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
                WriteSpan(ref lastOffset, charSpan, charSpan.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSpan(ref int lastOffset, ReadOnlySpan<char> valueSpan, int index)
        {
            if (lastOffset < index - 2)
            {
                m_writer.Write(valueSpan.Slice(lastOffset, index - lastOffset));
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
        /// <param name="value"></param>
        private void EscapeString(string value)
        {
            foreach (char ch in value)
            {
                bool found = false;

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        m_writer.Write(s_backslash);
                        m_writer.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (ch < 32)
                    {
                        m_writer.Write(s_backslash);
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
                    m_writer.Write(s_comma);
                }

                m_writer.Write(s_null);

                m_commaRequired = true;
            }
        }

        private void WriteSimpleField(string fieldName, string value)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                if (value == null)
                {
                    return;
                }

                if (m_commaRequired)
                {
                    m_writer.Write(s_comma);
                }

                m_writer.Write(s_quotation);
                EscapeString(fieldName);
                m_writer.Write(s_quotationColon);
            }
            else
            {
                if (m_commaRequired)
                {
                    m_writer.Write(s_comma);
                }
            }

            if (value != null)
            {
                m_writer.Write(value);
            }
            else
            {
                m_writer.Write(s_null);
            }

            m_commaRequired = true;
        }

        private void WriteSimpleField(string fieldName, string value, EscapeOptions options)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                if (value == null)
                {
                    return;
                }

                if (m_commaRequired)
                {
                    m_writer.Write(s_comma);
                }

                m_writer.Write(s_quotation);
                if ((options & EscapeOptions.NoFieldNameEscape) == EscapeOptions.NoFieldNameEscape)
                {
                    m_writer.Write(fieldName);
                }
                else
                {
                    EscapeString(fieldName);
                }
                m_writer.Write(s_quotationColon);
            }
            else
            {
                if (m_commaRequired)
                {
                    m_writer.Write(s_comma);
                }
            }

            if (value != null)
            {
                if ((options & EscapeOptions.Quotes) == EscapeOptions.Quotes)
                {
                    m_writer.Write(s_quotation);
                    if ((options & EscapeOptions.NoValueEscape) == EscapeOptions.NoValueEscape)
                    {
                        m_writer.Write(value);
                    }
                    else
                    {
                        EscapeString(value);
                    }
                    m_writer.Write(s_quotation);
                }
                else
                {
                    m_writer.Write(value);
                }
            }
            else
            {
                m_writer.Write(s_null);
            }

            m_commaRequired = true;
        }

        /// <summary>
        /// Writes a boolean to the stream.
        /// </summary>
        public void WriteBoolean(string fieldName, bool value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && !value)
            {
                WriteSimpleFieldNull(fieldName);
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
                WriteSimpleFieldNull(fieldName);
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
                WriteSimpleFieldNull(fieldName);
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
                WriteSimpleFieldNull(fieldName);
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
                WriteSimpleFieldNull(fieldName);
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
                WriteSimpleFieldNull(fieldName);
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
                WriteSimpleFieldNull(fieldName);
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
                WriteSimpleFieldNull(fieldName);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), EscapeOptions.Quotes);
        }

        /// <summary>
        /// Writes a ulong to the stream.
        /// </summary>
        public void WriteUInt64(string fieldName, ulong value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && value == 0)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(CultureInfo.InvariantCulture), EscapeOptions.Quotes);
        }

        /// <summary>
        /// Writes a float to the stream.
        /// </summary>
        public void WriteFloat(string fieldName, float value)
        {
            if (fieldName != null && !IncludeDefaultNumberValues && (value > -Single.Epsilon) && (value < Single.Epsilon))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            if (Single.IsNaN(value))
            {
                WriteSimpleField(fieldName, "\"NaN\"");
            }
            else if (Single.IsPositiveInfinity(value))
            {
                WriteSimpleField(fieldName, "\"Infinity\"");
            }
            else if (Single.IsNegativeInfinity(value))
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
            if (fieldName != null && !IncludeDefaultNumberValues && (value > -Double.Epsilon) && (value < Double.Epsilon))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            if (Double.IsNaN(value))
            {
                WriteSimpleField(fieldName, "\"NaN\"");
            }
            else if (Double.IsPositiveInfinity(value))
            {
                WriteSimpleField(fieldName, "\"Infinity\"");
            }
            else if (Double.IsNegativeInfinity(value))
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
                WriteSimpleFieldNull(fieldName);
                return;
            }

            WriteSimpleField(fieldName, value, EscapeOptions.Quotes);
        }

        /// <summary>
        /// Writes a UTC date/time to the stream.
        /// </summary>
        public void WriteDateTime(string fieldName, DateTime value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == DateTime.MinValue)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            if (value <= DateTime.MinValue)
            {
                WriteSimpleField(fieldName, "\"0001-01-01T00:00:00Z\"");
            }
            else if (value >= DateTime.MaxValue)
            {
                WriteSimpleField(fieldName, "\"9999-12-31T23:59:59Z\"");
            }
            else
            {
                WriteSimpleField(fieldName, ConvertUniversalTimeToString(value), EscapeOptions.Quotes | EscapeOptions.NoValueEscape);
            }
        }

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        public void WriteGuid(string fieldName, Uuid value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == Uuid.Empty)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(), EscapeOptions.Quotes | EscapeOptions.NoValueEscape);
        }

        /// <summary>
        /// Writes a GUID to the stream.
        /// </summary>
        public void WriteGuid(string fieldName, Guid value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == Guid.Empty)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            WriteSimpleField(fieldName, value.ToString(), EscapeOptions.Quotes | EscapeOptions.NoValueEscape);
        }

        /// <summary>
        /// Writes a byte string to the stream.
        /// </summary>
        public void WriteByteString(string fieldName, byte[] value)
        {
            if (value == null)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            // check the length.
            if (m_context.MaxByteStringLength > 0 && m_context.MaxByteStringLength < value.Length)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            WriteSimpleField(fieldName, Convert.ToBase64String(value), EscapeOptions.Quotes | EscapeOptions.NoValueEscape);
        }

        /// <summary>
        /// Writes an XmlElement to the stream.
        /// </summary>
        public void WriteXmlElement(string fieldName, XmlElement value)
        {
            if (value == null)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            var xml = value.OuterXml;
            var bytes = Encoding.UTF8.GetBytes(xml);

            WriteSimpleField(fieldName, Convert.ToBase64String(bytes), EscapeOptions.Quotes | EscapeOptions.NoValueEscape);
        }

        private void WriteNamespaceIndex(string fieldName, ushort namespaceIndex)
        {
            if (namespaceIndex == 0)
            {
                return;
            }

            if ((!UseReversibleEncoding || ForceNamespaceUri) && namespaceIndex > (ForceNamespaceUriForIndex1 ? 0 : 1))

            {
                var uri = m_context.NamespaceUris.GetString(namespaceIndex);
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

            switch (value.IdType)
            {
                case IdType.Numeric:
                {
                    WriteUInt32("Id", (uint)value.Identifier);
                    break;
                }

                case IdType.String:
                {
                    WriteString("Id", (string)value.Identifier);
                    break;
                }

                case IdType.Guid:
                {
                    WriteGuid("Id", (Guid)value.Identifier);
                    break;
                }

                case IdType.Opaque:
                {
                    WriteByteString("Id", (byte[])value.Identifier);
                    break;
                }
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
            if (value == null ||
                (NodeId.IsNull(value) && (value.IdType == IdType.Numeric)))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushStructure(fieldName);

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
            PopStructure();
        }

        /// <summary>
        /// Writes an ExpandedNodeId to the stream.
        /// </summary>
        public void WriteExpandedNodeId(string fieldName, ExpandedNodeId value)
        {
            if (value == null || value.InnerNodeId == null ||
                (!UseReversibleEncoding && NodeId.IsNull(value)))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushStructure(fieldName);

            string namespaceUri = value.NamespaceUri;
            ushort namespaceIndex = value.InnerNodeId.NamespaceIndex;
            if (ForceNamespaceUri && namespaceUri == null && namespaceIndex > (ForceNamespaceUriForIndex1 ? 0 : 1))
            {
                namespaceUri = Context.NamespaceUris.GetString(namespaceIndex);
            }
            WriteNodeIdContents(value.InnerNodeId, namespaceUri);

            uint serverIndex = value.ServerIndex;

            if (serverIndex >= 1)
            {
                var uri = m_context.ServerUris.GetString(serverIndex);

                if (!string.IsNullOrEmpty(uri))
                {
                    WriteSimpleField("ServerUri", uri, EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);
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

            PopStructure();
        }


        /// <summary>
        /// Writes an StatusCode to the stream.
        /// </summary>
        public void WriteStatusCode(string fieldName, StatusCode value)
        {
            if (fieldName != null && !IncludeDefaultValues && value == StatusCodes.Good)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            if (UseReversibleEncoding)
            {
                WriteUInt32(fieldName, value.Code);
                return;
            }

            if (value != StatusCodes.Good)
            {
                PushStructure(fieldName);
                WriteSimpleField("Code", value.Code.ToString(CultureInfo.InvariantCulture), EscapeOptions.NoFieldNameEscape);
                WriteSimpleField("Symbol", StatusCode.LookupSymbolicId(value.CodeBits), EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);
                PopStructure();
            }
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
            if (QualifiedName.IsNull(value))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushStructure(fieldName);

            WriteString("Name", value.Name);

            WriteNamespaceIndex("Uri", value.NamespaceIndex);

            PopStructure();
        }

        /// <summary>
        /// Writes an LocalizedText to the stream.
        /// </summary>
        public void WriteLocalizedText(string fieldName, LocalizedText value)
        {
            if (LocalizedText.IsNullOrEmpty(value))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            if (UseReversibleEncoding)
            {
                PushStructure(fieldName);

                WriteSimpleField("Text", value.Text, EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);

                if (!string.IsNullOrEmpty(value.Locale))
                {
                    WriteSimpleField("Locale", value.Locale, EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);
                }

                PopStructure();
            }
            else
            {
                WriteSimpleField(fieldName, value.Text, EscapeOptions.Quotes);
            }
        }

        /// <summary>
        /// Writes an Variant to the stream.
        /// </summary>
        public void WriteVariant(string fieldName, Variant value)
        {
            if (Variant.Null == value)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            CheckAndIncrementNestingLevel();

            try
            {

                bool isNull = (value.TypeInfo == null || value.TypeInfo.BuiltInType == BuiltInType.Null || value.Value == null);

                if (UseReversibleEncoding && !isNull)
                {
                    PushStructure(fieldName);
                    // encode enums as int32.
                    byte encodingByte = (byte)value.TypeInfo.BuiltInType;
                    if (value.TypeInfo.BuiltInType == BuiltInType.Enumeration)
                    {
                        encodingByte = (byte)BuiltInType.Int32;
                    }

                    WriteByte("Type", encodingByte);
                    fieldName = "Body";
                }

                if (m_commaRequired)
                {
                    m_writer.Write(s_comma);
                }

                if (!string.IsNullOrEmpty(fieldName))
                {
                    m_writer.Write(s_quotation);
                    EscapeString(fieldName);
                    m_writer.Write(s_quotationColon);
                }

                WriteVariantContents(value.Value, value.TypeInfo);

                if (UseReversibleEncoding && !isNull)
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

        /// <summary>
        /// Writes an DataValue array to the stream.
        /// </summary>
        public void WriteDataValue(string fieldName, DataValue value)
        {
            if (value == null)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushStructure(fieldName);

            if (value != null)
            {
                if (value.WrappedValue.TypeInfo != null && value.WrappedValue.TypeInfo.BuiltInType != BuiltInType.Null)
                {
                    WriteVariant("Value", value.WrappedValue);
                }

                if (value.StatusCode != StatusCodes.Good)
                {
                    WriteStatusCode("StatusCode", value.StatusCode);
                }

                if (value.SourceTimestamp != DateTime.MinValue)
                {
                    WriteDateTime("SourceTimestamp", value.SourceTimestamp);

                    if (value.SourcePicoseconds != 0)
                    {
                        WriteUInt16("SourcePicoseconds", value.SourcePicoseconds);
                    }
                }

                if (value.ServerTimestamp != DateTime.MinValue)
                {
                    WriteDateTime("ServerTimestamp", value.ServerTimestamp);

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
            if (value == null || value.Encoding == ExtensionObjectEncoding.None)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            var encodeable = value.Body as IEncodeable;

            if (!UseReversibleEncoding && encodeable != null)
            {
                // non reversible encoding, only the content of the Body field is encoded
                if (value.Body is IStructureTypeInfo structureType &&
                    structureType.StructureType == StructureType.Union)
                {
                    encodeable.Encode(this);
                    return;
                }

                PushStructure(fieldName);
                encodeable.Encode(this);
                PopStructure();
                return;
            }

            PushStructure(fieldName);

            var typeId = value.TypeId;

            if (encodeable != null)
            {
                switch (value.Encoding)
                {
                    case ExtensionObjectEncoding.Binary: { typeId = encodeable.BinaryEncodingId; break; }
                    case ExtensionObjectEncoding.Xml: { typeId = encodeable.XmlEncodingId; break; }
                    default: { typeId = encodeable.TypeId; break; }
                }
            }

            var localTypeId = ExpandedNodeId.ToNodeId(typeId, Context.NamespaceUris);

            if (UseReversibleEncoding)
            {
                WriteNodeId("TypeId", localTypeId);
            }
            else
            {
                WriteExpandedNodeId("TypeId", typeId);
            }

            if (encodeable != null)
            {
                WriteEncodeable("Body", encodeable, null);
            }
            else
            {
                if (value.Body != null)
                {
                    if (value.Encoding == ExtensionObjectEncoding.Json)
                    {
                        WriteSimpleField("Body", value.Body as string, EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);
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
                    }
                }
            }

            PopStructure();
        }

        /// <summary>
        /// Writes an encodeable object to the stream.
        /// </summary>
        public void WriteEncodeable(string fieldName, IEncodeable value, System.Type systemType)
        {
            if (value == null)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            if (m_nestingLevel == 0 && (m_commaRequired || m_topLevelIsArray))
            {
                if (string.IsNullOrWhiteSpace(fieldName) ^ m_topLevelIsArray)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingError,
                        "With Array as top level, encodeables with fieldname will create invalid json");
                }
            }

            if (m_nestingLevel == 0 && !m_commaRequired)
            {
                if (string.IsNullOrWhiteSpace(fieldName) && !m_topLevelIsArray)
                {
                    m_writer.Flush();
                    if (m_writer.BaseStream.Length == 1) //Opening "{"
                    {
                        m_writer.BaseStream.Seek(0, SeekOrigin.Begin);
                    }
                    m_dontWriteClosing = true;
                }
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
            var numericString = numeric.ToString(CultureInfo.InvariantCulture);
            if (UseReversibleEncoding)
            {
                WriteSimpleField(fieldName, numericString);
            }
            else
            {
                var valueString = value.ToString();
                if (valueString == numericString)
                {
                    WriteSimpleField(fieldName, numericString, EscapeOptions.Quotes);
                }
                else
                {
                    WriteSimpleField(fieldName, Utils.Format("{0}_{1}", value.ToString(), numeric), EscapeOptions.Quotes);
                }
            }
        }

        /// <summary>
        /// Writes an enumerated Int32 value to the stream.
        /// </summary>
        public void WriteEnumerated(string fieldName, int numeric)
        {
            var numericString = numeric.ToString(CultureInfo.InvariantCulture);
            WriteSimpleField(fieldName, numericString, !UseReversibleEncoding ? EscapeOptions.Quotes : EscapeOptions.None);
        }

        /// <summary>
        /// Writes a boolean array to the stream.
        /// </summary>
        public void WriteBooleanArray(string fieldName, IList<bool> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteSByteArray(string fieldName, IList<sbyte> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteByteArray(string fieldName, IList<byte> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteInt16Array(string fieldName, IList<short> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteUInt16Array(string fieldName, IList<ushort> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteInt32Array(string fieldName, IList<int> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteUInt32Array(string fieldName, IList<uint> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteInt64Array(string fieldName, IList<long> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteUInt64Array(string fieldName, IList<ulong> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteFloatArray(string fieldName, IList<float> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteDoubleArray(string fieldName, IList<double> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteStringArray(string fieldName, IList<string> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteDateTimeArray(string fieldName, IList<DateTime> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteGuidArray(string fieldName, IList<Uuid> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        /// Writes a GUID array to the stream.
        /// </summary>
        public void WriteGuidArray(string fieldName, IList<Guid> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteByteStringArray(string fieldName, IList<byte[]> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteXmlElementArray(string fieldName, IList<XmlElement> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteNodeIdArray(string fieldName, IList<NodeId> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteExpandedNodeIdArray(string fieldName, IList<ExpandedNodeId> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteStatusCodeArray(string fieldName, IList<StatusCode> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (!UseReversibleEncoding &&
                    values[ii] == StatusCodes.Good)
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
        public void WriteDiagnosticInfoArray(string fieldName, IList<DiagnosticInfo> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteQualifiedNameArray(string fieldName, IList<QualifiedName> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteLocalizedTextArray(string fieldName, IList<LocalizedText> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteVariantArray(string fieldName, IList<Variant> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteDataValueArray(string fieldName, IList<DataValue> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteExtensionObjectArray(string fieldName, IList<ExtensionObject> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteEncodeableArray(string fieldName, IList<IEncodeable> values, System.Type systemType)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
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
            else if (!string.IsNullOrWhiteSpace(fieldName) && m_nestingLevel == 0 && m_topLevelIsArray)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingError,
                    "With Array as top level, encodeables array with filename will create invalid json");
            }
            else
            {
                PushArray(fieldName);

                // check the length.
                if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        public void WriteEnumeratedArray(string fieldName, Array values, System.Type systemType)
        {
            if (values == null || values.Length == 0)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            // check the length.
            if (m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Length)
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
                if (arrayType != typeof(Int32))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingError,
                        Utils.Format("Type '{0}' is not allowed in an Enumeration.", arrayType.FullName));
                }
                foreach (Int32 value in values)
                {
                    WriteEnumerated(null, value);
                }
            }

            PopArray();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Writes the contents of an Variant to the stream.
        /// </summary>
        public void WriteVariantContents(object value, TypeInfo typeInfo)
        {
            try
            {
                m_inVariantWithEncoding = UseReversibleEncoding;

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
                        case BuiltInType.Boolean: { WriteBoolean(null, (bool)value); return; }
                        case BuiltInType.SByte: { WriteSByte(null, (sbyte)value); return; }
                        case BuiltInType.Byte: { WriteByte(null, (byte)value); return; }
                        case BuiltInType.Int16: { WriteInt16(null, (short)value); return; }
                        case BuiltInType.UInt16: { WriteUInt16(null, (ushort)value); return; }
                        case BuiltInType.Int32: { WriteInt32(null, (int)value); return; }
                        case BuiltInType.UInt32: { WriteUInt32(null, (uint)value); return; }
                        case BuiltInType.Int64: { WriteInt64(null, (long)value); return; }
                        case BuiltInType.UInt64: { WriteUInt64(null, (ulong)value); return; }
                        case BuiltInType.Float: { WriteFloat(null, (float)value); return; }
                        case BuiltInType.Double: { WriteDouble(null, (double)value); return; }
                        case BuiltInType.String: { WriteString(null, (string)value); return; }
                        case BuiltInType.DateTime: { WriteDateTime(null, (DateTime)value); return; }
                        case BuiltInType.Guid: { WriteGuid(null, (Uuid)value); return; }
                        case BuiltInType.ByteString: { WriteByteString(null, (byte[])value); return; }
                        case BuiltInType.XmlElement: { WriteXmlElement(null, (XmlElement)value); return; }
                        case BuiltInType.NodeId: { WriteNodeId(null, (NodeId)value); return; }
                        case BuiltInType.ExpandedNodeId: { WriteExpandedNodeId(null, (ExpandedNodeId)value); return; }
                        case BuiltInType.StatusCode: { WriteStatusCode(null, (StatusCode)value); return; }
                        case BuiltInType.QualifiedName: { WriteQualifiedName(null, (QualifiedName)value); return; }
                        case BuiltInType.LocalizedText: { WriteLocalizedText(null, (LocalizedText)value); return; }
                        case BuiltInType.ExtensionObject: { WriteExtensionObject(null, (ExtensionObject)value); return; }
                        case BuiltInType.DataValue: { WriteDataValue(null, (DataValue)value); return; }
                        case BuiltInType.Enumeration: { WriteEnumerated(null, (Enum)value); return; }
                        case BuiltInType.DiagnosticInfo: { WriteDiagnosticInfo(null, (DiagnosticInfo)value); return; }
                    }
                }
                // write array
                else if (typeInfo.ValueRank >= ValueRanks.OneDimension)
                {
                    int valueRank = typeInfo.ValueRank;
                    if (UseReversibleEncoding && value is Matrix matrix)
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
                m_inVariantWithEncoding = false;
            }
        }

        /// <summary>
        /// Writes an Variant array to the stream.
        /// </summary>
        public void WriteObjectArray(string fieldName, IList<object> values)
        {
            if (values == null || (values.Count == 0 && !m_inVariantWithEncoding))
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            PushArray(fieldName);

            if (values != null && m_context.MaxArrayLength > 0 && m_context.MaxArrayLength < values.Count)
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
        /// Encode an array according to its valueRank and BuiltInType
        /// </summary>
        public void WriteArray(string fieldName, object array, int valueRank, BuiltInType builtInType)
        {
            // write array.
            if (valueRank == ValueRanks.OneDimension)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean: { WriteBooleanArray(fieldName, (bool[])array); return; }
                    case BuiltInType.SByte: { WriteSByteArray(fieldName, (sbyte[])array); return; }
                    case BuiltInType.Byte: { WriteByteArray(fieldName, (byte[])array); return; }
                    case BuiltInType.Int16: { WriteInt16Array(fieldName, (short[])array); return; }
                    case BuiltInType.UInt16: { WriteUInt16Array(fieldName, (ushort[])array); return; }
                    case BuiltInType.Int32: { WriteInt32Array(fieldName, (int[])array); return; }
                    case BuiltInType.UInt32: { WriteUInt32Array(fieldName, (uint[])array); return; }
                    case BuiltInType.Int64: { WriteInt64Array(fieldName, (long[])array); return; }
                    case BuiltInType.UInt64: { WriteUInt64Array(fieldName, (ulong[])array); return; }
                    case BuiltInType.Float: { WriteFloatArray(fieldName, (float[])array); return; }
                    case BuiltInType.Double: { WriteDoubleArray(fieldName, (double[])array); return; }
                    case BuiltInType.String: { WriteStringArray(fieldName, (string[])array); return; }
                    case BuiltInType.DateTime: { WriteDateTimeArray(fieldName, (DateTime[])array); return; }
                    case BuiltInType.Guid: { WriteGuidArray(fieldName, (Uuid[])array); return; }
                    case BuiltInType.ByteString: { WriteByteStringArray(fieldName, (byte[][])array); return; }
                    case BuiltInType.XmlElement: { WriteXmlElementArray(fieldName, (XmlElement[])array); return; }
                    case BuiltInType.NodeId: { WriteNodeIdArray(fieldName, (NodeId[])array); return; }
                    case BuiltInType.ExpandedNodeId: { WriteExpandedNodeIdArray(fieldName, (ExpandedNodeId[])array); return; }
                    case BuiltInType.StatusCode: { WriteStatusCodeArray(fieldName, (StatusCode[])array); return; }
                    case BuiltInType.QualifiedName: { WriteQualifiedNameArray(fieldName, (QualifiedName[])array); return; }
                    case BuiltInType.LocalizedText: { WriteLocalizedTextArray(fieldName, (LocalizedText[])array); return; }
                    case BuiltInType.ExtensionObject: { WriteExtensionObjectArray(fieldName, (ExtensionObject[])array); return; }
                    case BuiltInType.DataValue: { WriteDataValueArray(fieldName, (DataValue[])array); return; }
                    case BuiltInType.DiagnosticInfo: { WriteDiagnosticInfoArray(fieldName, (DiagnosticInfo[])array); return; }
                    case BuiltInType.Enumeration:
                    {
                        if (!(array is Array enumArray))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadEncodingError,
                                "Unexpected non Array type encountered while encoding an array of enumeration.");
                        }
                        WriteEnumeratedArray(fieldName, enumArray, enumArray.GetType().GetElementType());
                        return;
                    }
                    case BuiltInType.Variant:
                    {
                        if (array is Variant[] variants)
                        {
                            WriteVariantArray(fieldName, variants);
                            return;
                        }

                        // try to write IEncodeable Array
                        if (array is IEncodeable[] encodeableArray)
                        {
                            WriteEncodeableArray(fieldName, encodeableArray, array.GetType().GetElementType());
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
                    }
                    default:
                    {
                        // try to write IEncodeable Array
                        if (array is IEncodeable[] encodeableArray)
                        {
                            WriteEncodeableArray(fieldName, encodeableArray, array.GetType().GetElementType());
                            return;
                        }
                        if (array == null)
                        {
                            WriteSimpleFieldNull(fieldName);
                            return;
                        }
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingError,
                            "Unexpected BuiltInType encountered while encoding an array: {0}",
                            builtInType);
                    }
                }
            }
            // write matrix.
            else if (valueRank > ValueRanks.OneDimension)
            {
                if (!(array is Matrix matrix))
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

                if (matrix != null)
                {
                    int index = 0;
                    WriteStructureMatrix(fieldName, matrix, 0, ref index, matrix.TypeInfo);
                    return;
                }

                // field is omitted
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Completes writing and returns the text length.
        /// </summary>
        private int InternalClose(bool dispose)
        {
            if (!m_dontWriteClosing)
            {
                if (m_topLevelIsArray)
                {
                    m_writer.Write(s_rightSquareBracket);
                }
                else
                {
                    m_writer.Write(s_rightCurlyBrace);
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
            if (value == null || value.IsNullDiagnosticInfo)
            {
                WriteSimpleFieldNull(fieldName);
                return;
            }

            CheckAndIncrementNestingLevel();

            try
            {
                PushStructure(fieldName);

                if (value.SymbolicId >= 0)
                {
                    WriteSimpleField("SymbolicId", value.SymbolicId.ToString(CultureInfo.InvariantCulture), EscapeOptions.NoFieldNameEscape);
                }

                if (value.NamespaceUri >= 0)
                {
                    WriteSimpleField("NamespaceUri", value.NamespaceUri.ToString(CultureInfo.InvariantCulture), EscapeOptions.NoFieldNameEscape);
                }

                if (value.Locale >= 0)
                {
                    WriteSimpleField("Locale", value.Locale.ToString(CultureInfo.InvariantCulture), EscapeOptions.NoFieldNameEscape);
                }

                if (value.LocalizedText >= 0)
                {
                    WriteSimpleField("LocalizedText", value.LocalizedText.ToString(CultureInfo.InvariantCulture), EscapeOptions.NoFieldNameEscape);
                }

                if (value.AdditionalInfo != null)
                {
                    WriteSimpleField("AdditionalInfo", value.AdditionalInfo, EscapeOptions.Quotes | EscapeOptions.NoFieldNameEscape);
                }

                if (value.InnerStatusCode != StatusCodes.Good)
                {
                    WriteStatusCode("InnerStatusCode", value.InnerStatusCode);
                }

                if (value.InnerDiagnosticInfo != null)
                {
                    if (depth < DiagnosticInfo.MaxInnerDepth)
                    {
                        WriteDiagnosticInfo("InnerDiagnosticInfo", value.InnerDiagnosticInfo, depth + 1);
                    }
                    else
                    {
                        Utils.LogWarning("InnerDiagnosticInfo dropped because nesting exceeds maximum of {0}.",
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
        /// Write multi dimensional array in structure.
        /// </summary>
        private void WriteStructureMatrix(
            string fieldName,
            Matrix matrix,
            int dim,
            ref int index,
            TypeInfo typeInfo)
        {
            // check if matrix is well formed
            (bool valid, int sizeFromDimensions) = Matrix.ValidateDimensions(true, matrix.Dimensions, Context.MaxArrayLength);

            if (!valid || (sizeFromDimensions != matrix.Elements.Length))
            {
                throw new ArgumentException("The number of elements in the matrix does not match the dimensions.");
            }

            CheckAndIncrementNestingLevel();

            try
            {
                var arrayLen = matrix.Dimensions[dim];
                if (dim == matrix.Dimensions.Length - 1)
                {
                    // Create a slice of values for the top dimension
                    var copy = Array.CreateInstance(
                        matrix.Elements.GetType().GetElementType(), arrayLen);
                    Array.Copy(matrix.Elements, index, copy, 0, arrayLen);
                    // Write slice as value rank
                    if (m_commaRequired)
                    {
                        m_writer.Write(s_comma);
                    }
                    WriteVariantContents(copy, new TypeInfo(typeInfo.BuiltInType, 1));
                    index += arrayLen;
                }
                else
                {
                    PushArray(fieldName);
                    for (var i = 0; i < arrayLen; i++)
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckAndIncrementNestingLevel()
        {
            if (m_nestingLevel > m_context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} was exceeded",
                    m_context.MaxEncodingNestingLevels);
            }
            m_nestingLevel++;
        }

        /// <summary>
        /// Write Utc time in the format "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK".
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string ConvertUniversalTimeToString(DateTime value)
        {
            // The length of the DateTime string encoded by "o"
            const int DateTimeRoundTripKindLength = 28;
            // the index of the last digit which can be omitted if 0
            const int DateTimeRoundTripKindLastDigit = DateTimeRoundTripKindLength - 2;
            // the index of the first digit which can be omitted (7 digits total)
            const int DateTimeRoundTripKindFirstDigit = DateTimeRoundTripKindLastDigit - 7;

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
        #endregion
    }
}
