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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Opc.Ua
{
    /// <summary>
    /// Reads objects from a JSON stream.
    /// </summary>
    public class JsonDecoder : IJsonDecoder
    {
        /// <summary>
        /// The name of the Root array if the json is defined as an array
        /// </summary>
        public const string RootArrayName = "___root_array___";

        /// <summary>
        /// If TRUE then the NamespaceUris and ServerUris tables are updated with new URIs read from the JSON stream.
        /// </summary>
        public bool UpdateNamespaceTable { get; set; }

        private JsonTextReader m_reader;
        private readonly ILogger m_logger;
        private readonly Dictionary<string, object> m_root;
        private readonly Stack<object> m_stack;
        private ushort[] m_namespaceMappings;
        private ushort[] m_serverMappings;
        private uint m_nestingLevel;

        /// <summary>
        /// JSON encoded value of: “9999-12-31T23:59:59Z”
        /// </summary>
        private readonly DateTime m_dateTimeMaxJsonValue = new(3155378975990000000);

        private enum JTokenNullObject
        {
            Undefined = 0,
            Object = 1,
            Array = 2
        }

        /// <summary>
        /// Create a JSON decoder to decode a string.
        /// </summary>
        /// <param name="json">The JSON encoded string.</param>
        /// <param name="context">The service message context to use.</param>
        public JsonDecoder(string json, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<JsonDecoder>();
            m_nestingLevel = 0;
            m_reader = new JsonTextReader(new StringReader(json));
            m_root = ReadObject();
            m_stack = new Stack<object>();
            m_stack.Push(m_root);
        }

        /// <summary>
        /// Create a JSON decoder to decode a <see cref="Type"/>from a <see cref="JsonTextReader"/>.
        /// </summary>
        /// <param name="reader">The text reader.</param>
        /// <param name="context">The service message context to use.</param>
        public JsonDecoder(JsonTextReader reader, IServiceMessageContext context)
        {
            Context = context;
            m_logger = context.Telemetry.CreateLogger<JsonDecoder>();
            m_nestingLevel = 0;
            m_reader = reader;
            m_root = ReadObject();
            m_stack = new Stack<object>();
            m_stack.Push(m_root);
        }

        /// <summary>
        /// Decodes a message from a buffer.
        /// </summary>
        /// <typeparam name="T">The type of the message to read</typeparam>
        public static T DecodeMessage<T>(
            byte[] buffer,
            IServiceMessageContext context) where T : IEncodeable
        {
            return DecodeMessage<T>(new ArraySegment<byte>(buffer), context);
        }

        /// <summary>
        /// Decodes a message from a buffer.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <typeparam name="T">The type of the message to read</typeparam>
        public static T DecodeMessage<T>(
            ArraySegment<byte> buffer,
            IServiceMessageContext context) where T : IEncodeable
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // check that the max message size was not exceeded.
            if (context.MaxMessageSize > 0 && context.MaxMessageSize < buffer.Count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize {0} < {1}",
                    context.MaxMessageSize,
                    buffer.Count);
            }

            using var decoder = new JsonDecoder(
                Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count),
                context);
            return decoder.DecodeMessage<T>();
        }

        /// <inheritdoc/>
        public T DecodeMessage<T>() where T : IEncodeable
        {
            StringCollection namespaceUris = ReadStringArray("NamespaceUris");
            StringCollection serverUris = ReadStringArray("ServerUris");

            if ((namespaceUris != null && namespaceUris.Count > 0) ||
                (serverUris != null && serverUris.Count > 0))
            {
                NamespaceTable namespaces =
                    namespaceUris == null || namespaceUris.Count == 0
                        ? Context.NamespaceUris
                        : new NamespaceTable(namespaceUris);
                StringTable servers =
                    serverUris == null || serverUris.Count == 0
                        ? Context.ServerUris
                        : new StringTable(serverUris);

                SetMappingTables(namespaces, servers);
            }

            // read the node id.
            NodeId typeId = ReadNodeId("TypeId");
            // convert to absolute node id.
            var absoluteId = NodeId.ToExpandedNodeId(typeId, Context.NamespaceUris);
            // Read the message.
            return ReadEncodeable<T>("Body", absoluteId);
        }

        /// <inheritdoc/>
        public void SetMappingTables(NamespaceTable namespaceUris, StringTable serverUris)
        {
            m_namespaceMappings = null;

            if (namespaceUris != null && Context.NamespaceUris != null)
            {
                ushort[] namespaceMappings = new ushort[namespaceUris.Count];

                for (uint ii = 0; ii < namespaceUris.Count; ii++)
                {
                    string uri = namespaceUris.GetString(ii);

                    if (UpdateNamespaceTable)
                    {
                        namespaceMappings[ii] = Context.NamespaceUris.GetIndexOrAppend(uri);
                    }
                    else
                    {
                        int index = Context.NamespaceUris.GetIndex(namespaceUris.GetString(ii));
                        namespaceMappings[ii] = index >= 0 ? (ushort)index : ushort.MaxValue;
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
                    string uri = serverUris.GetString(ii);

                    if (UpdateNamespaceTable)
                    {
                        serverMappings[ii] = Context.ServerUris.GetIndexOrAppend(uri);
                    }
                    else
                    {
                        int index = Context.ServerUris.GetIndex(serverUris.GetString(ii));
                        serverMappings[ii] = index >= 0 ? (ushort)index : ushort.MaxValue;
                    }
                }

                m_serverMappings = serverMappings;
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            m_reader.Close();
        }

        /// <summary>
        /// Closes the stream used for reading.
        /// </summary>
        public void Close(bool checkEof)
        {
            if (checkEof && m_reader.TokenType != JsonToken.EndObject)
            {
                while (m_reader.Read() && m_reader.TokenType != JsonToken.EndObject)
                {
                }
            }

            m_reader.Close();
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
                Utils.SilentDispose(m_reader);
                m_reader = null;
            }
        }

        /// <inheritdoc/>
        public EncodingType EncodingType => EncodingType.Json;

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; }

        /// <inheritdoc/>
        public void PushNamespace(string namespaceUri)
        {
        }

        /// <inheritdoc/>
        public void PopNamespace()
        {
        }

        /// <inheritdoc/>
        public bool ReadField(string fieldName, out object token)
        {
            token = null;

            if (string.IsNullOrEmpty(fieldName))
            {
                token = m_stack.Peek();
                return true;
            }

            return (m_stack.Peek() is Dictionary<string, object> context) &&
                context.TryGetValue(fieldName, out token);
        }

        /// <inheritdoc/>
        public bool ReadBoolean(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return false;
            }

            bool? value = token as bool?;

            if (value == null)
            {
                return false;
            }

            return (bool)token;
        }

        /// <inheritdoc/>
        public sbyte ReadSByte(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                return 0;
            }

            if (value is < sbyte.MinValue or > sbyte.MaxValue)
            {
                return 0;
            }

            return (sbyte)value;
        }

        /// <inheritdoc/>
        public byte ReadByte(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                return 0;
            }

            if (value is < byte.MinValue or > byte.MaxValue)
            {
                return 0;
            }

            return (byte)value;
        }

        /// <inheritdoc/>
        public short ReadInt16(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                return 0;
            }

            if (value is < short.MinValue or > short.MaxValue)
            {
                return 0;
            }

            return (short)value;
        }

        /// <inheritdoc/>
        public ushort ReadUInt16(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                return 0;
            }

            if (value is < ushort.MinValue or > ushort.MaxValue)
            {
                return 0;
            }

            return (ushort)value;
        }

        /// <inheritdoc/>
        public int ReadInt32(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                return ReadEnumeratedString<int>(token, int.TryParse);
            }

            if (value is < int.MinValue or > int.MaxValue)
            {
                return 0;
            }

            return (int)value;
        }

        /// <inheritdoc/>
        public uint ReadUInt32(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                return ReadEnumeratedString<uint>(token, uint.TryParse);
            }

            if (value is < uint.MinValue or > uint.MaxValue)
            {
                return 0;
            }

            return (uint)value;
        }

        /// <inheritdoc/>
        public long ReadInt64(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                if (token is not string text ||
                    !long.TryParse(
                        text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out long number))
                {
                    return 0;
                }

                return number;
            }

            return (long)value;
        }

        /// <inheritdoc/>
        public ulong ReadUInt64(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                if (token is not string text ||
                    !ulong.TryParse(
                        text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out ulong number))
                {
                    return 0;
                }

                return number;
            }

            if (value < 0)
            {
                return 0;
            }

            return (ulong)value;
        }

        /// <inheritdoc/>
        public float ReadFloat(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            double? value = token as double?;

            if (value == null)
            {
                string text = token as string;
                if (text == null ||
                    !float.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float number))
                {
                    if (text != null)
                    {
                        if (string.Equals(text, "Infinity", StringComparison.OrdinalIgnoreCase))
                        {
                            return float.PositiveInfinity;
                        }
                        else if (string.Equals(
                            text,
                            "-Infinity",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return float.NegativeInfinity;
                        }
                        else if (string.Equals(text, "NaN", StringComparison.OrdinalIgnoreCase))
                        {
                            return float.NaN;
                        }
                    }

                    long? integer = token as long?;
                    if (integer == null)
                    {
                        return 0;
                    }

                    return (float)integer;
                }

                return number;
            }

            float floatValue = (float)value;
            if (floatValue is >= float.MinValue and <= float.MaxValue)
            {
                return (float)value;
            }

            return 0;
        }

        /// <inheritdoc/>
        public double ReadDouble(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return 0;
            }

            double? value = token as double?;

            if (value == null)
            {
                string text = token as string;
                if (text == null ||
                    !double.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double number))
                {
                    if (text != null)
                    {
                        if (string.Equals(text, "Infinity", StringComparison.OrdinalIgnoreCase))
                        {
                            return double.PositiveInfinity;
                        }
                        else if (string.Equals(
                            text,
                            "-Infinity",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return double.NegativeInfinity;
                        }
                        else if (string.Equals(text, "NaN", StringComparison.OrdinalIgnoreCase))
                        {
                            return double.NaN;
                        }
                    }

                    long? integer = token as long?;

                    if (integer == null)
                    {
                        return 0;
                    }

                    return (double)integer;
                }

                return number;
            }

            return (double)value;
        }

        /// <inheritdoc/>
        public string ReadString(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return null;
            }

            if (token is not string value)
            {
                return null;
            }

            if (Context.MaxStringLength > 0 && Context.MaxStringLength < value.Length)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            return value;
        }

        /// <inheritdoc/>
        public DateTime ReadDateTime(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return DateTime.MinValue;
            }

            var value = token as DateTime?;
            if (value != null)
            {
                return value.Value >= m_dateTimeMaxJsonValue ? DateTime.MaxValue : value.Value;
            }

            if (token is string text)
            {
                try
                {
                    var result = XmlConvert.ToDateTime(text, XmlDateTimeSerializationMode.Utc);
                    return result >= m_dateTimeMaxJsonValue ? DateTime.MaxValue : result;
                }
                catch (FormatException fe)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Failed to decode DateTime: {0}",
                        fe.Message);
                }
            }

            return DateTime.MinValue;
        }

        /// <inheritdoc/>
        public Uuid ReadGuid(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return Uuid.Empty;
            }

            if (token is not string value)
            {
                return Uuid.Empty;
            }

            try
            {
                return Uuid.Parse(value);
            }
            catch (FormatException fe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Failed to create Guid: {0}",
                    fe.Message);
            }
        }

        /// <inheritdoc/>
        public ByteString ReadByteString(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return default;
            }

            if (token is JTokenNullObject)
            {
                return default;
            }

            if (token is not string value)
            {
                return ByteString.Empty;
            }

            byte[] bytes = SafeConvertFromBase64String(value);

            if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < bytes.Length)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            return ByteString.From(bytes);
        }

        /// <inheritdoc/>
        public XmlElement ReadXmlElement(string fieldName)
        {
            if (!ReadField(fieldName, out object token) || token is not string value)
            {
                return XmlElement.Empty;
            }

            return (XmlElement)value;
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return NodeId.Null;
            }

            if (token is string text)
            {
                NodeId nodeId;

                try
                {
                    nodeId = NodeId.Parse(
                        Context,
                        text,
                        new NodeIdParsingOptions
                        {
                            UpdateTables = UpdateNamespaceTable,
                            NamespaceMappings = m_namespaceMappings,
                            ServerMappings = m_serverMappings
                        });
                }
                catch
                {
                    // fallback on error. this allows the application to sort out the problem.
                    nodeId = new NodeId(text, 0);
                }

                return nodeId;
            }

            if (token is not Dictionary<string, object> value)
            {
                return NodeId.Null;
            }

            IdType idType = IdType.Numeric;
            ushort namespaceIndex = 0;

            try
            {
                m_stack.Push(value);

                if (value.ContainsKey("IdType"))
                {
                    idType = (IdType)ReadInt32("IdType");
                }

                if (ReadField("Namespace", out object namespaceToken))
                {
                    long? index = namespaceToken as long?;

                    if (index == null)
                    {
                        if (namespaceToken is string namespaceUri)
                        {
                            namespaceIndex = ToNamespaceIndex(namespaceUri);
                        }
                    }
                    else if (index.Value is >= 0 and < ushort.MaxValue)
                    {
                        namespaceIndex = ToNamespaceIndex(index.Value);
                    }
                }

                if (value.ContainsKey("Id"))
                {
                    switch (idType)
                    {
                        case IdType.Numeric:
                            return new NodeId(ReadUInt32("Id"), namespaceIndex);
                        case IdType.Opaque:
                            return new NodeId(ReadByteString("Id"), namespaceIndex);
                        case IdType.String:
                            return new NodeId(ReadString("Id"), namespaceIndex);
                        case IdType.Guid:
                            return new NodeId(ReadGuid("Id"), namespaceIndex);
                        default:
                            throw ServiceResultException.Unexpected(
                                "Unexpected IdType value: {0}", idType);
                    }
                }
                return DefaultNodeId(idType, namespaceIndex);
            }
            finally
            {
                m_stack.Pop();
            }
        }

        /// <inheritdoc/>
        public ExpandedNodeId ReadExpandedNodeId(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return ExpandedNodeId.Null;
            }

            if (token is string text)
            {
                try
                {
                    return ExpandedNodeId.Parse(
                        Context,
                        text,
                        new NodeIdParsingOptions
                        {
                            UpdateTables = UpdateNamespaceTable,
                            NamespaceMappings = m_namespaceMappings,
                            ServerMappings = m_serverMappings
                        });
                }
                catch
                {
                    // fallback on error. this allows the application to sort out the problem.
                    _ = new NodeId(text, 0);
                }
            }

            if (token is not Dictionary<string, object> value)
            {
                return ExpandedNodeId.Null;
            }

            IdType idType = IdType.Numeric;
            ushort namespaceIndex = 0;
            string namespaceUri = null;
            uint serverIndex = 0;

            try
            {
                m_stack.Push(value);

                if (value.ContainsKey("IdType"))
                {
                    idType = (IdType)ReadInt32("IdType");
                }

                if (ReadField("Namespace", out object namespaceToken))
                {
                    long? index = namespaceToken as long?;

                    if (index == null)
                    {
                        namespaceUri = namespaceToken as string;
                    }
                    else if (index.Value is >= 0 and < ushort.MaxValue)
                    {
                        namespaceIndex = ToNamespaceIndex(index.Value);
                    }
                }

                if (ReadField("ServerUri", out object serverUriToken))
                {
                    long? index = serverUriToken as long?;

                    if (index == null)
                    {
                        serverIndex = ToServerIndex(serverUriToken as string);
                    }
                    else if (index.Value is >= 0 and < uint.MaxValue)
                    {
                        serverIndex = ToServerIndex(index.Value);
                    }
                }

                if (namespaceUri != null)
                {
                    namespaceIndex = ToNamespaceIndex(namespaceUri);

                    if (ushort.MaxValue != namespaceIndex)
                    {
                        namespaceUri = null;
                    }
                    else
                    {
                        namespaceIndex = 0;
                    }
                }

                if (value.ContainsKey("Id"))
                {
                    switch (idType)
                    {
                        case IdType.Numeric:
                            return new ExpandedNodeId(
                                ReadUInt32("Id"),
                                namespaceIndex,
                                namespaceUri,
                                serverIndex);
                        case IdType.Opaque:
                            return new ExpandedNodeId(
                                ReadByteString("Id"),
                                namespaceIndex,
                                namespaceUri,
                                serverIndex);
                        case IdType.String:
                            return new ExpandedNodeId(
                                ReadString("Id"),
                                namespaceIndex,
                                namespaceUri,
                                serverIndex);
                        case IdType.Guid:
                            return new ExpandedNodeId(
                                ReadGuid("Id"),
                                namespaceIndex,
                                namespaceUri,
                                serverIndex);
                        default:
                            throw ServiceResultException.Unexpected(
                                "Unexpected IdType value: {0}", idType);
                    }
                }

                return new ExpandedNodeId(
                    DefaultNodeId(idType, namespaceIndex),
                    namespaceUri,
                    serverIndex);
            }
            finally
            {
                m_stack.Pop();
            }
        }

        /// <inheritdoc/>
        public StatusCode ReadStatusCode(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                // the status code was not found
                return StatusCodes.Good;
            }

            if (token is long code)
            {
                return (StatusCode)(uint)code;
            }

            bool wasPush = PushStructure(fieldName);

            try
            {
                // try to read the non reversible Code
                if (ReadField("Code", out token))
                {
                    return (StatusCode)ReadUInt32("Code");
                }

                // read the uint code
                return ReadUInt32(null);
            }
            finally
            {
                if (wasPush)
                {
                    Pop();
                }
            }
        }

        /// <inheritdoc/>
        public DiagnosticInfo ReadDiagnosticInfo(string fieldName)
        {
            return ReadDiagnosticInfo(fieldName, 0);
        }

        /// <inheritdoc/>
        public QualifiedName ReadQualifiedName(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return QualifiedName.Null;
            }

            if (token is string text)
            {
                QualifiedName qn;

                try
                {
                    qn = QualifiedName.Parse(Context, text, UpdateNamespaceTable);

                    if (qn.NamespaceIndex != 0)
                    {
                        ushort ns = ToNamespaceIndex(qn.NamespaceIndex);

                        if (ns != qn.NamespaceIndex)
                        {
                            qn = new QualifiedName(qn.Name, ns);
                        }
                    }
                }
                catch (Exception)
                {
                    // fallback on error. this allows the application to sort out the problem.
                    qn = new QualifiedName(text, 0);
                }

                return qn;
            }

            if (token is not Dictionary<string, object> value)
            {
                return QualifiedName.Null;
            }

            ushort namespaceIndex = 0;
            string name = null;
            try
            {
                m_stack.Push(value);

                if (value.ContainsKey("Name"))
                {
                    name = ReadString("Name");
                }

                if (ReadField("Uri", out object namespaceToken))
                {
                    long? index = namespaceToken as long?;

                    if (index == null)
                    {
                        if (namespaceToken is string namespaceUri)
                        {
                            namespaceIndex = ToNamespaceIndex(namespaceUri);
                        }
                    }
                    else if (index.Value is >= 0 and < ushort.MaxValue)
                    {
                        namespaceIndex = ToNamespaceIndex(index.Value);
                    }
                }
            }
            finally
            {
                m_stack.Pop();
            }

            return new QualifiedName(name, namespaceIndex);
        }

        /// <inheritdoc/>
        public LocalizedText ReadLocalizedText(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return LocalizedText.Null;
            }

            string locale = null;
            string text = null;

            if (token is not Dictionary<string, object> value)
            {
                // read non reversible encoding
                text = token as string;

                if (text != null)
                {
                    return new LocalizedText(text);
                }

                return LocalizedText.Null;
            }

            try
            {
                m_stack.Push(value);

                if (value.ContainsKey("Locale"))
                {
                    locale = ReadString("Locale");
                }

                if (value.ContainsKey("Text"))
                {
                    text = ReadString("Text");
                }
            }
            finally
            {
                m_stack.Pop();
            }

            return new LocalizedText(locale, text);
        }

        private Variant ReadVariantFromObject(
            string valueName,
            BuiltInType builtInType,
            Dictionary<string, object> value)
        {
            if (value.TryGetValue(valueName, out object innerValue))
            {
                if (innerValue is List<object>)
                {
                    Variant array = ReadVariantArrayBody(valueName, builtInType);

                    if (value.ContainsKey("Dimensions"))
                    {
                        Int32Collection dimensions = ReadInt32Array("Dimensions");

                        try
                        {
                            return new Variant(
                                new Matrix(array.Value as Array, builtInType, [.. dimensions]));
                        }
                        catch (ArgumentException e)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadEncodingLimitsExceeded,
                                e);
                        }
                        catch (Exception e)
                        {
                            throw new ServiceResultException(StatusCodes.BadDecodingError, e);
                        }
                    }

                    return array;
                }

                return ReadVariantScalarBody(valueName, builtInType);
            }

            return Variant.Null;
        }

        /// <inheritdoc/>
        public Variant ReadVariant(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return Variant.Null;
            }

            if (token is not Dictionary<string, object> value)
            {
                return Variant.Null;
            }

            CheckAndIncrementNestingLevel();

            try
            {
                m_stack.Push(value);
                BuiltInType builtInType = value.ContainsKey("UaType")
                    ? (BuiltInType)ReadByte("UaType")
                    : (BuiltInType)ReadByte("Type");

                if (value.ContainsKey("Value"))
                {
                    return ReadVariantFromObject("Value", builtInType, value);
                }

                return ReadVariantFromObject("Body", builtInType, value);
            }
            finally
            {
                m_nestingLevel--;
                m_stack.Pop();
            }
        }

        /// <inheritdoc/>
        public DataValue ReadDataValue(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return null;
            }

            if (token is not Dictionary<string, object> value)
            {
                return null;
            }

            var dv = new DataValue();

            try
            {
                m_stack.Push(value);

                if (value.ContainsKey("UaType"))
                {
                    var builtInType = (BuiltInType)ReadByte("UaType");
                    dv.WrappedValue = ReadVariantFromObject("Value", builtInType, value);
                }
                else
                {
                    dv.WrappedValue = ReadVariant("Value");
                }

                dv.StatusCode = ReadStatusCode("StatusCode");
                dv.SourceTimestamp = ReadDateTime("SourceTimestamp");
                dv.SourcePicoseconds =
                    dv.SourceTimestamp != DateTime.MinValue
                        ? ReadUInt16("SourcePicoseconds")
                        : (ushort)0;
                dv.ServerTimestamp = ReadDateTime("ServerTimestamp");
                dv.ServerPicoseconds =
                    dv.ServerTimestamp != DateTime.MinValue
                        ? ReadUInt16("ServerPicoseconds")
                        : (ushort)0;
            }
            finally
            {
                m_stack.Pop();
            }

            return dv;
        }

        /// <inheritdoc/>
        public ExtensionObject ReadExtensionObject(string fieldName)
        {
            ExtensionObject extension = ExtensionObject.Null;
            if (!ReadField(fieldName, out object token))
            {
                return extension;
            }

            if ((token is not Dictionary<string, object> value) || (value.Count == 0))
            {
                return extension;
            }

            try
            {
                m_stack.Push(value);

                bool inlineValues = true;
                ExpandedNodeId typeId = ReadExpandedNodeId("UaTypeId");

                if (typeId.IsNull)
                {
                    typeId = ReadExpandedNodeId("TypeId");
                    inlineValues = false;
                }

                ExpandedNodeId absoluteId = typeId.IsAbsolute
                    ? typeId
                    : NodeId.ToExpandedNodeId(typeId.InnerNodeId, Context.NamespaceUris);

                if (!typeId.IsNull && absoluteId.IsNull)
                {
                    m_logger.LogWarning(
                        "Cannot de-serialized extension objects if the NamespaceUri is not in the NamespaceTable: Type = {Type}",
                        typeId);
                }
                else
                {
                    typeId = absoluteId;
                }

                ExtensionObjectEncoding encoding = 0;
                string encodingFieldName = inlineValues ? "UaEncoding" : "Encoding";

                encoding = (ExtensionObjectEncoding)ReadByte(encodingFieldName);

                if (value.ContainsKey(encodingFieldName))
                {
                    encoding = (ExtensionObjectEncoding)ReadByte(encodingFieldName);

                    if (encoding == ExtensionObjectEncoding.None)
                    {
                        return extension;
                    }
                }

                if (encoding == ExtensionObjectEncoding.Binary)
                {
                    ByteString bytes = ReadByteString(inlineValues ? "UaBody" : "Body");
                    return new ExtensionObject(typeId, bytes);
                }

                if (encoding == ExtensionObjectEncoding.Xml)
                {
                    XmlElement xml = ReadXmlElement(inlineValues ? "UaBody" : "Body");
                    if (xml.IsEmpty)
                    {
                        return extension;
                    }
                    return new ExtensionObject(typeId, xml);
                }

                if (encoding == ExtensionObjectEncoding.Json)
                {
                    string json = ReadString(inlineValues ? "UaBody" : "Body");
                    if (string.IsNullOrEmpty(json))
                    {
                        return extension;
                    }
                    return new ExtensionObject(typeId, json);
                }

                if (Context.Factory.TryGetEncodeableType(typeId, out IEncodeableType activator))
                {
                    IEncodeable encodeable = null;

                    if (inlineValues)
                    {
                        encodeable = activator.CreateInstance() ??
                            throw new ServiceResultException(
                                StatusCodes.BadDecodingError,
                                Utils.Format(
                                    "Type does not support IEncodeable interface: '{0}'",
                                    typeId));
                        encodeable.Decode(this);
                    }
                    else
                    {
                        encodeable = ReadEncodeable<IEncodeable>("Body", typeId);
                        if (encodeable == null)
                        {
                            return extension;
                        }
                    }

                    return new ExtensionObject(encodeable);
                }

                using var ostrm = new MemoryStream();
                using (var stream = new StreamWriter(ostrm))
                using (var writer = new JsonTextWriter(stream))
                {
                    EncodeAsJson(writer, token);
                }
                // Close the writer before retrieving the data
                return new ExtensionObject(typeId, ByteString.From(ostrm.ToArray()));
            }
            finally
            {
                m_stack.Pop();
            }
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string fieldName) where T : IEncodeable, new()
        {
            if (!ReadField(fieldName, out object token))
            {
                return default;
            }

            CheckAndIncrementNestingLevel();

            T value = new();
            try
            {
                m_stack.Push(token);
                value.Decode(this);
            }
            finally
            {
                m_stack.Pop();
                m_nestingLevel--;
            }
            return value;
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (!ReadField(fieldName, out object token))
            {
                return default;
            }

            if (!Context.Factory.TryGetEncodeableType(
                encodeableTypeId,
                out IEncodeableType activator))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Cannot decode type '{0}'.",
                    encodeableTypeId);
            }

            var value = (T)activator.CreateInstance();

            // set type identifier for custom complex data types before decode.
            if (value is IComplexTypeInstance complexTypeInstance)
            {
                complexTypeInstance.TypeId = encodeableTypeId;
            }

            CheckAndIncrementNestingLevel();

            try
            {
                m_stack.Push(token);
                value.Decode(this);
            }
            finally
            {
                m_stack.Pop();
                m_nestingLevel--;
            }

            return value;
        }

        /// <inheritdoc/>
        public T ReadEnumerated<T>(string fieldName) where T : struct, Enum
        {
            if (!ReadField(fieldName, out object token))
            {
                return default;
            }

            if (token is long code)
            {
                return EnumHelper.Int32ToEnum<T>((int)code);
            }

            if (token is string text)
            {
                int index = text.LastIndexOf('_');

                if (index > 0 && long.TryParse(text[(index + 1)..], out code))
                {
                    return (T)Enum.ToObject(typeof(T), code);
                }
            }

            return default;
        }

        /// <inheritdoc/>
        public ArrayOf<bool> ReadBooleanArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            bool[] values = new bool[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadBoolean(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<sbyte> ReadSByteArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            sbyte[] values = new sbyte[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadSByte(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<byte> ReadByteArray(string fieldName)
        {
            string value = ReadString(fieldName);
            if (value != null)
            {
                return SafeConvertFromBase64String(value);
            }

            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            byte[] values = new byte[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadByte(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<short> ReadInt16Array(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            short[] values = new short[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadInt16(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ushort> ReadUInt16Array(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            ushort[] values = new ushort[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadUInt16(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<int> ReadInt32Array(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            int[] values = new int[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadInt32(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<uint> ReadUInt32Array(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            uint[] values = new uint[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadUInt32(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<long> ReadInt64Array(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            long[] values = new long[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadInt64(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ulong> ReadUInt64Array(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            ulong[] values = new ulong[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadUInt64(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<float> ReadFloatArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            float[] values = new float[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadFloat(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<double> ReadDoubleArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            double[] values = new double[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadDouble(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<string> ReadStringArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            string[] values = new string[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadString(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<DateTime> ReadDateTimeArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new DateTime[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadDateTime(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<Uuid> ReadGuidArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new Uuid[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadGuid(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ByteString> ReadByteStringArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new ByteString[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadByteString(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> ReadXmlElementArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new XmlElement[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadXmlElement(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> ReadNodeIdArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new NodeId[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadNodeId(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new ExpandedNodeId[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadExpandedNodeId(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<StatusCode> ReadStatusCodeArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new StatusCode[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadStatusCode(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<DiagnosticInfo> ReadDiagnosticInfoArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new DiagnosticInfo[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadDiagnosticInfo(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new QualifiedName[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadQualifiedName(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new LocalizedText[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadLocalizedText(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<Variant> ReadVariantArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new Variant[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadVariant(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<DataValue> ReadDataValueArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new DataValue[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadDataValue(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string fieldName)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new ExtensionObject[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadExtensionObject(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(string fieldName) where T : IEncodeable, new()
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new T[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadEncodeable<T>(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(string fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new T[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadEncodeable<T>(null, encodeableTypeId);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEnumeratedArray<T>(string fieldName) where T : struct, Enum
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return default;
            }

            var values = new T[token.Count];

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values[ii] = ReadEnumerated<T>(null);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue(string fieldName, TypeInfo typeInfo)
        {
            if (typeInfo.IsScalar)
            {
                return ReadVariantScalarBody(fieldName, typeInfo.BuiltInType);
            }
            if (typeInfo.IsArray)
            {
                return ReadVariantArrayBody(fieldName, typeInfo.BuiltInType);
            }
#if FALSE
            if (typeInfo.IsMatrix)
            {
                if (!ReadField(fieldName, out object token))
                {
                    return default;
                }

                if (token is Dictionary<string, object> value)
                {
                    m_stack.Push(value);
                    Int32Collection dimensions2;
                    if (value.ContainsKey("Dimensions"))
                    {
                        dimensions2 = ReadInt32Array("Dimensions");
                    }
                    else
                    {
                        dimensions2 = new Int32Collection(typeInfo.ValueRank);
                    }

                    Array array2 = ReadArray("Array", 1, builtInType, systemType, encodeableTypeId);
                    m_stack.Pop();

                    try
                    {
                        var matrix2 = new Matrix(array2, builtInType, [.. dimensions2]);
                        return matrix2.ToArray();
                    }
                    catch (ArgumentException e)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, e);
                    }
                    catch (Exception e)
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError, e);
                    }
                }

                if (token is not List<object> array)
                {
                    return default;
                }

                var elements = new List<object>();
                var dimensions = new List<int>();
                if (builtInType is BuiltInType.Enumeration or BuiltInType.Variant or BuiltInType.Null)
                {
                    DetermineIEncodeableSystemType(ref systemType, encodeableTypeId);
                }
                ReadMatrixPart(
                    fieldName,
                    array,
                    builtInType,
                    ref elements,
                    ref dimensions,
                    0,
                    systemType,
                    encodeableTypeId);

                if (dimensions.Count == 0)
                {
                    // for an empty element create the empty dimension array
                    dimensions = new int[valueRank].ToList();
                }
                else if (dimensions.Count < ValueRanks.TwoDimensions)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "The ValueRank {0} of the decoded array doesn't match the desired ValueRank {1}.",
                        dimensions.Count,
                        valueRank);
                }

                Matrix matrix;
                try
                {
                    switch (builtInType)
                    {
                        case BuiltInType.Boolean:
                            matrix = new Matrix(
                                elements.Cast<bool>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.SByte:
                            matrix = new Matrix(
                                elements.Cast<sbyte>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.Byte:
                            matrix = new Matrix(
                                elements.Cast<byte>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.Int16:
                            matrix = new Matrix(
                                elements.Cast<short>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.UInt16:
                            matrix = new Matrix(
                                elements.Cast<ushort>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.Int32:
                            matrix = new Matrix(
                                elements.Cast<int>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.UInt32:
                            matrix = new Matrix(
                                elements.Cast<uint>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.Int64:
                            matrix = new Matrix(
                                elements.Cast<long>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.UInt64:
                            matrix = new Matrix(
                                elements.Cast<ulong>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.Float:
                            matrix = new Matrix(
                                elements.Cast<float>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.Double:
                            matrix = new Matrix(
                                elements.Cast<double>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.String:
                            matrix = new Matrix(
                                elements.Cast<string>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.DateTime:
                            matrix = new Matrix(
                                elements.Cast<DateTime>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.Guid:
                            matrix = new Matrix(
                                elements.Cast<Uuid>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.ByteString:
                            matrix = new Matrix(
                                elements.Cast<byte[]>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.XmlElement:
                            matrix = new Matrix(
                                elements.Cast<XmlElement>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.NodeId:
                            matrix = new Matrix(
                                elements.Cast<NodeId>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.ExpandedNodeId:
                            matrix = new Matrix(
                                elements.Cast<ExpandedNodeId>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.StatusCode:
                            matrix = new Matrix(
                                elements.Cast<StatusCode>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.QualifiedName:
                            matrix = new Matrix(
                                elements.Cast<QualifiedName>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.LocalizedText:
                            matrix = new Matrix(
                                elements.Cast<LocalizedText>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.DataValue:
                            matrix = new Matrix(
                                elements.Cast<DataValue>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.Enumeration:
                        {
                            if (systemType?.IsEnum == true)
                            {
                                var newElements = Array.CreateInstance(systemType, elements.Count);
                                int ii = 0;
                                foreach (object element in elements)
                                {
                                    newElements.SetValue(
                                        Convert.ChangeType(
                                            element,
                                            systemType,
                                            CultureInfo.InvariantCulture),
                                        ii++);
                                }
                                matrix = new Matrix(newElements, builtInType, [.. dimensions]);
                            }
                            else
                            {
                                matrix = new Matrix(
                                    elements.Cast<int>().ToArray(),
                                    builtInType,
                                    [.. dimensions]);
                            }
                            break;
                        }
                        case BuiltInType.Variant:
                        {
                            if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                            {
                                var newElements = Array.CreateInstance(systemType, elements.Count);
                                for (int i = 0; i < elements.Count; i++)
                                {
                                    newElements.SetValue(
                                        Convert.ChangeType(
                                            elements[i],
                                            systemType,
                                            CultureInfo.InvariantCulture),
                                        i);
                                }
                                matrix = new Matrix(newElements, builtInType, [.. dimensions]);
                                break;
                            }
                            matrix = new Matrix(
                                elements.Cast<Variant>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        }
                        case BuiltInType.ExtensionObject:
                            matrix = new Matrix(
                                elements.Cast<ExtensionObject>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.DiagnosticInfo:
                            matrix = new Matrix(
                                elements.Cast<DiagnosticInfo>().ToArray(),
                                builtInType,
                                [.. dimensions]);
                            break;
                        case BuiltInType.Null:
                        case BuiltInType.Number:
                        case BuiltInType.Integer:
                        case BuiltInType.UInteger:
                            if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                            {
                                var newElements = Array.CreateInstance(systemType, elements.Count);
                                for (int i = 0; i < elements.Count; i++)
                                {
                                    newElements.SetValue(
                                        Convert.ChangeType(
                                            elements[i],
                                            systemType,
                                            CultureInfo.InvariantCulture),
                                        i);
                                }
                                matrix = new Matrix(newElements, builtInType, [.. dimensions]);
                                break;
                            }
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Cannot decode unknown type in Array object with BuiltInType: {0}.",
                                builtInType);
                        default:
                            throw ServiceResultException.Unexpected(
                                $"Unexpected BuiltInType {builtInType}");
                    }
                }
                catch (ArgumentException e)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, e);
                }
                catch (Exception e)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, e);
                }

                return matrix.ToArray();
            }
            return null;
#endif
            return default;
        }

        /// <inheritdoc/>
        public uint ReadSwitchField(IList<string> switches, out string fieldName)
        {
            fieldName = null;

            if (m_stack.Peek() is Dictionary<string, object> context)
            {
                long index = -1;

                if (context.ContainsKey("SwitchField"))
                {
                    index = ReadUInt32("SwitchField");
                }

                if (switches == null)
                {
                    return 0;
                }

                if (index >= switches.Count)
                {
                    return (uint)index;
                }

                if (index >= 0)
                {
                    if (!context.ContainsKey("Value"))
                    {
                        fieldName = switches[(int)(index - 1)];
                    }
                    else
                    {
                        fieldName = "Value";
                    }

                    return (uint)index;
                }

                foreach (KeyValuePair<string, object> ii in context)
                {
                    if (ii.Key == "UaTypeId")
                    {
                        continue;
                    }

                    index = switches.IndexOf(ii.Key);

                    if (index >= 0)
                    {
                        fieldName = ii.Key;
                        return (uint)(index + 1);
                    }
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public uint ReadEncodingMask(IList<string> masks)
        {
            if (m_stack.Peek() is Dictionary<string, object> context)
            {
                if (context.ContainsKey("EncodingMask"))
                {
                    return ReadUInt32("EncodingMask");
                }

                uint mask = 0;

                if (masks == null)
                {
                    return 0;
                }

                foreach (string fieldName in masks)
                {
                    if (context.ContainsKey(fieldName))
                    {
                        int index = masks.IndexOf(fieldName);

                        if (index >= 0)
                        {
                            mask |= (uint)(1 << index);
                        }
                    }
                }

                return mask;
            }

            return 0;
        }

        /// <summary>
        /// Push the specified structure on the Read Stack
        /// </summary>
        /// <param name="fieldName">The name of the object that shall be placed on the Read Stack</param>
        /// <returns>true if successful</returns>
        public bool PushStructure(string fieldName)
        {
            if (!ReadField(fieldName, out object token))
            {
                return false;
            }

            if (token != null)
            {
                m_stack.Push(token);
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool PushArray(string fieldName, int index)
        {
            if (!ReadArrayField(fieldName, out List<object> token))
            {
                return false;
            }

            if (index < token.Count)
            {
                m_stack.Push(token[index]);
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public void Pop()
        {
            m_stack.Pop();
        }

        private ushort ToNamespaceIndex(string uri)
        {
            int index = Context.NamespaceUris.GetIndex(uri);

            if (index < 0)
            {
                if (!UpdateNamespaceTable)
                {
                    return ushort.MaxValue;
                }

                index = Context.NamespaceUris.GetIndexOrAppend(uri);
            }

            return (ushort)index;
        }

        private ushort ToNamespaceIndex(long index)
        {
            if (m_namespaceMappings == null || index <= 0)
            {
                return (ushort)index;
            }

            if (index < 0 || index >= m_namespaceMappings.Length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"No mapping for NamespaceIndex={index}.");
            }

            return m_namespaceMappings[index];
        }

        private ushort ToServerIndex(string uri)
        {
            int index = Context.ServerUris.GetIndex(uri);

            if (index < 0)
            {
                if (!UpdateNamespaceTable)
                {
                    return ushort.MaxValue;
                }

                index = Context.ServerUris.GetIndexOrAppend(uri);
            }

            return (ushort)index;
        }

        private ushort ToServerIndex(long index)
        {
            if (m_serverMappings == null || index <= 0)
            {
                return (ushort)index;
            }

            if (index < 0 || index >= m_serverMappings.Length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"No mapping for ServerIndex(={index}.");
            }

            return m_serverMappings[index];
        }

        /// <summary>
        /// Helper to provide the TryParse method when reading an enumerated string.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private delegate bool TryParseHandler<T>(
            string s,
            NumberStyles numberStyles,
            CultureInfo cultureInfo,
            out T result);

        /// <summary>
        /// Helper to read an enumerated string in an extension object.
        /// </summary>
        /// <typeparam name="T">The number type which was encoded.</typeparam>
        /// <returns>The parsed number or 0.</returns>
        private static T ReadEnumeratedString<T>(object token, TryParseHandler<T> handler)
            where T : struct
        {
            T number = default;
            if (token is string text)
            {
                bool retry = false;
                do
                {
                    if (handler?.Invoke(
                        text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out number) == false)
                    {
                        int lastIndex = text.LastIndexOf('_');
                        if (lastIndex != -1)
                        {
                            text = text[(lastIndex + 1)..];
                            retry = true;
                        }
                    }
                } while (retry);
            }

            return number;
        }

        /// <summary>
        /// Reads a DiagnosticInfo from the stream.
        /// Limits the InnerDiagnosticInfos to the specified depth.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private DiagnosticInfo ReadDiagnosticInfo(string fieldName, int depth)
        {
            if (!ReadField(fieldName, out object token))
            {
                return null;
            }

            if (token is not Dictionary<string, object> value)
            {
                return null;
            }

            if (depth >= DiagnosticInfo.MaxInnerDepth)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of InnerDiagnosticInfo was exceeded");
            }

            CheckAndIncrementNestingLevel();

            try
            {
                m_stack.Push(value);

                var di = new DiagnosticInfo();

                bool hasDiagnosticInfo = false;
                if (value.ContainsKey("SymbolicId"))
                {
                    di.SymbolicId = ReadInt32("SymbolicId");
                    hasDiagnosticInfo = true;
                }

                if (value.ContainsKey("NamespaceUri"))
                {
                    di.NamespaceUri = ReadInt32("NamespaceUri");
                    hasDiagnosticInfo = true;
                }

                if (value.ContainsKey("Locale"))
                {
                    di.Locale = ReadInt32("Locale");
                    hasDiagnosticInfo = true;
                }

                if (value.ContainsKey("LocalizedText"))
                {
                    di.LocalizedText = ReadInt32("LocalizedText");
                    hasDiagnosticInfo = true;
                }

                if (value.ContainsKey("AdditionalInfo"))
                {
                    di.AdditionalInfo = ReadString("AdditionalInfo");
                    hasDiagnosticInfo = true;
                }

                if (value.ContainsKey("InnerStatusCode"))
                {
                    di.InnerStatusCode = ReadStatusCode("InnerStatusCode");
                    hasDiagnosticInfo = true;
                }

                if (value.ContainsKey("InnerDiagnosticInfo") &&
                    depth < DiagnosticInfo.MaxInnerDepth)
                {
                    di.InnerDiagnosticInfo = ReadDiagnosticInfo("InnerDiagnosticInfo", depth + 1);
                    hasDiagnosticInfo = true;
                }

                return hasDiagnosticInfo ? di : null;
            }
            finally
            {
                m_nestingLevel--;
                m_stack.Pop();
            }
        }

        /// <summary>
        /// Read the body of a Variant as a BuiltInType
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Variant ReadVariantScalarBody(string fieldName, BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    return Variant.From(ReadBoolean(fieldName));
                case BuiltInType.SByte:
                    return Variant.From(ReadSByte(fieldName));
                case BuiltInType.Byte:
                    return Variant.From(ReadByte(fieldName));
                case BuiltInType.Int16:
                    return Variant.From(ReadInt16(fieldName));
                case BuiltInType.UInt16:
                    return Variant.From(ReadUInt16(fieldName));
                case BuiltInType.Int32:
                    return Variant.From(ReadInt32(fieldName));
                case BuiltInType.UInt32:
                    return Variant.From(ReadUInt32(fieldName));
                case BuiltInType.Int64:
                    return Variant.From(ReadInt64(fieldName));
                case BuiltInType.UInt64:
                    return Variant.From(ReadUInt64(fieldName));
                case BuiltInType.Float:
                    return Variant.From(ReadFloat(fieldName));
                case BuiltInType.Double:
                    return Variant.From(ReadDouble(fieldName));
                case BuiltInType.String:
                    return Variant.From(ReadString(fieldName));
                case BuiltInType.ByteString:
                    return Variant.From(ReadByteString(fieldName));
                case BuiltInType.DateTime:
                    return Variant.From(ReadDateTime(fieldName));
                case BuiltInType.Guid:
                    return Variant.From(ReadGuid(fieldName));
                case BuiltInType.NodeId:
                    return Variant.From(ReadNodeId(fieldName));
                case BuiltInType.ExpandedNodeId:
                    return Variant.From(ReadExpandedNodeId(fieldName));
                case BuiltInType.QualifiedName:
                    return Variant.From(ReadQualifiedName(fieldName));
                case BuiltInType.LocalizedText:
                    return Variant.From(ReadLocalizedText(fieldName));
                case BuiltInType.StatusCode:
                    return Variant.From(ReadStatusCode(fieldName));
                case BuiltInType.XmlElement:
                    return Variant.From(ReadXmlElement(fieldName));
                case BuiltInType.ExtensionObject:
                    return Variant.From(ReadExtensionObject(fieldName));
                case BuiltInType.DataValue:
                    return Variant.From(ReadDataValue(fieldName));
                case BuiltInType.Variant:
                case BuiltInType.DiagnosticInfo:
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unsupported built in type for Variant content ({0}).",
                        type);
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unexpected scalar built in type ({0}).",
                        type);
            }
        }

        /// <summary>
        /// Read the Body of a Variant as an Array
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Variant ReadVariantArrayBody(string fieldName, BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    return Variant.From(ReadBooleanArray(fieldName));
                case BuiltInType.SByte:
                    return Variant.From(ReadSByteArray(fieldName));
                case BuiltInType.Byte:
                    return Variant.From(ReadByteArray(fieldName));
                case BuiltInType.Int16:
                    return Variant.From(ReadInt16Array(fieldName));
                case BuiltInType.UInt16:
                    return Variant.From(ReadUInt16Array(fieldName));
                case BuiltInType.Int32:
                    return Variant.From(ReadInt32Array(fieldName));
                case BuiltInType.UInt32:
                    return Variant.From(ReadUInt32Array(fieldName));
                case BuiltInType.Int64:
                    return Variant.From(ReadInt64Array(fieldName));
                case BuiltInType.UInt64:
                    return Variant.From(ReadUInt64Array(fieldName));
                case BuiltInType.Float:
                    return Variant.From(ReadFloatArray(fieldName));
                case BuiltInType.Double:
                    return Variant.From(ReadDoubleArray(fieldName));
                case BuiltInType.String:
                    return Variant.From(ReadStringArray(fieldName));
                case BuiltInType.ByteString:
                    return Variant.From(ReadByteStringArray(fieldName));
                case BuiltInType.DateTime:
                    return Variant.From(ReadDateTimeArray(fieldName));
                case BuiltInType.Guid:
                    return Variant.From(ReadGuidArray(fieldName));
                case BuiltInType.NodeId:
                    return Variant.From(ReadNodeIdArray(fieldName));
                case BuiltInType.ExpandedNodeId:
                    return Variant.From(ReadExpandedNodeIdArray(fieldName));
                case BuiltInType.QualifiedName:
                    return Variant.From(ReadQualifiedNameArray(fieldName));
                case BuiltInType.LocalizedText:
                    return Variant.From(ReadLocalizedTextArray(fieldName));
                case BuiltInType.StatusCode:
                    return Variant.From(ReadStatusCodeArray(fieldName));
                case BuiltInType.XmlElement:
                    return Variant.From(ReadXmlElementArray(fieldName));
                case BuiltInType.ExtensionObject:
                    return Variant.From(ReadExtensionObjectArray(fieldName));
                case BuiltInType.DataValue:
                    return Variant.From(ReadDataValueArray(fieldName));
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                case BuiltInType.Variant:
                    return Variant.From(ReadVariantArray(null));
                case BuiltInType.DiagnosticInfo:
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unsupported built in type for Variant array content ({0}).",
                        type);
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unexpected array built in type ({0}).",
                        type);
            }
        }

        /// <summary>
        /// Reads the content of an Array from json stream
        /// </summary>
        private List<object> ReadArray()
        {
            CheckAndIncrementNestingLevel();

            try
            {
                var elements = new List<object>();

                while (m_reader.Read() && m_reader.TokenType != JsonToken.EndArray)
                {
                    switch (m_reader.TokenType)
                    {
                        case JsonToken.Comment:
                            break;
                        case JsonToken.Null:
                            elements.Add(JTokenNullObject.Array);
                            break;
                        case JsonToken.Date:
                        case JsonToken.Boolean:
                        case JsonToken.Integer:
                        case JsonToken.Float:
                        case JsonToken.String:
                            elements.Add(m_reader.Value);
                            break;
                        case JsonToken.StartArray:
                            elements.Add(ReadArray());
                            break;
                        case JsonToken.StartObject:
                            elements.Add(ReadObject());
                            break;
                        case JsonToken.None:
                        case JsonToken.StartConstructor:
                        case JsonToken.PropertyName:
                        case JsonToken.Raw:
                        case JsonToken.Undefined:
                        case JsonToken.EndObject:
                        case JsonToken.EndArray:
                        case JsonToken.EndConstructor:
                        case JsonToken.Bytes:
                            break;
                        default:
                            Debug.Fail($"Unexpected token type in array: {m_reader.TokenType}");
                            break;
                    }
                }

                return elements;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Reads an object from the json stream
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Dictionary<string, object> ReadObject()
        {
            var fields = new Dictionary<string, object>();

            try
            {
                while (m_reader.Read() && m_reader.TokenType != JsonToken.EndObject)
                {
                    if (m_reader.TokenType == JsonToken.StartArray)
                    {
                        fields[RootArrayName] = ReadArray();
                    }
                    else if (m_reader.TokenType == JsonToken.PropertyName)
                    {
                        string name = (string)m_reader.Value;

                        if (m_reader.Read() && m_reader.TokenType != JsonToken.EndObject)
                        {
                            switch (m_reader.TokenType)
                            {
                                case JsonToken.Comment:
                                    break;
                                case JsonToken.Null:
                                    fields[name] = JTokenNullObject.Object;
                                    break;
                                case JsonToken.Date:
                                case JsonToken.Bytes:
                                case JsonToken.Boolean:
                                case JsonToken.Integer:
                                case JsonToken.Float:
                                case JsonToken.String:
                                    fields[name] = m_reader.Value;
                                    break;
                                case JsonToken.StartArray:
                                    fields[name] = ReadArray();
                                    break;
                                case JsonToken.StartObject:
                                    fields[name] = ReadObject();
                                    break;
                                case JsonToken.None:
                                case JsonToken.StartConstructor:
                                case JsonToken.PropertyName:
                                case JsonToken.Raw:
                                case JsonToken.Undefined:
                                case JsonToken.EndObject:
                                case JsonToken.EndArray:
                                case JsonToken.EndConstructor:
                                    break;
                                default:
                                    Debug.Fail($"Unexpected token type in array: {m_reader.TokenType}");
                                    break;
                            }
                        }
                    }
                }
            }
            catch (JsonReaderException jre)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Error reading JSON object: {0}",
                    jre.Message);
            }
            return fields;
        }

        /// <summary>
        /// Get Default value for NodeId for diferent IdTypes
        /// </summary>
        /// <returns>new NodeId</returns>
        /// <exception cref="ServiceResultException"></exception>
        private static NodeId DefaultNodeId(IdType idType, ushort namespaceIndex)
        {
            switch (idType)
            {
                case IdType.Numeric:
                    return new NodeId(0U, namespaceIndex);
                case IdType.Opaque:
                    return new NodeId(ByteString.Empty, namespaceIndex);
                case IdType.String:
                    return new NodeId(string.Empty, namespaceIndex);
                case IdType.Guid:
                    return new NodeId(Guid.Empty, namespaceIndex);
                default:
                    throw ServiceResultException.Unexpected(
                        "Unexpected IdType value: {0}", idType);
            }
        }

        private void EncodeAsJson(JsonTextWriter writer, object value)
        {
            try
            {
                if (value is Dictionary<string, object> map)
                {
                    EncodeAsJson(writer, map);
                    return;
                }

                if (value is List<object> list)
                {
                    writer.WriteStartArray();

                    foreach (object element in list)
                    {
                        EncodeAsJson(writer, element);
                    }

                    writer.WriteEndArray();
                    return;
                }

                writer.WriteValue(value);
            }
            catch (JsonWriterException jwe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Unable to encode ExtensionObject Body as Json: {0}",
                    jwe.Message);
            }
        }

        private void EncodeAsJson(JsonTextWriter writer, Dictionary<string, object> value)
        {
            writer.WriteStartObject();

            foreach (KeyValuePair<string, object> field in value)
            {
                writer.WritePropertyName(field.Key);
                EncodeAsJson(writer, field.Value);
            }

            writer.WriteEndObject();
        }

        private bool ReadArrayField(string fieldName, out List<object> array)
        {
            array = null;

            if (!ReadField(fieldName, out object token))
            {
                return false;
            }

            array = token as List<object>;

            if (array == null)
            {
                return false;
            }

            if (Context.MaxArrayLength > 0 && Context.MaxArrayLength < array.Count)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            return true;
        }

        /// <summary>
        /// Safe Convert function which throws a BadDecodingError if unsuccessful.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static byte[] SafeConvertFromBase64String(string s)
        {
            try
            {
                return Convert.FromBase64String(s);
            }
            catch (FormatException fe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Error decoding base64 string: {0}",
                    fe.Message);
            }
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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
    }
}
