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
using System.Linq;
using System.Text;
using System.Xml;
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
            Initialize();

            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_nestingLevel = 0;
            m_reader = new JsonTextReader(new StringReader(json));
            m_root = ReadObject();
            m_stack = new Stack<object>();
            m_stack.Push(m_root);
        }

        /// <summary>
        /// Create a JSON decoder to decode a <see cref="Type"/>from a <see cref="JsonTextReader"/>.
        /// </summary>
        /// <param name="systemType">The system type of the encoded JSON stream.</param>
        /// <param name="reader">The text reader.</param>
        /// <param name="context">The service message context to use.</param>
        public JsonDecoder(Type systemType, JsonTextReader reader, IServiceMessageContext context)
        {
            Initialize();

            Context = context;
            m_nestingLevel = 0;
            m_reader = reader;
            m_root = ReadObject();
            m_stack = new Stack<object>();
            m_stack.Push(m_root);
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_reader = null;
        }

        /// <summary>
        /// Decodes a session-less message from a buffer.
        /// </summary>
        public static IEncodeable DecodeSessionLessMessage(byte[] buffer, IServiceMessageContext context)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using var decoder = new JsonDecoder(Encoding.UTF8.GetString(buffer), context);
            // decode the actual message.
            var message = new SessionLessServiceMessage();
            message.Decode(decoder);
            return message.Message;
        }

        /// <summary>
        /// Decodes a message from a buffer.
        /// </summary>
        public static IEncodeable DecodeMessage(byte[] buffer, Type expectedType, IServiceMessageContext context)
        {
            return DecodeMessage(new ArraySegment<byte>(buffer), expectedType, context);
        }

        /// <summary>
        /// Decodes a message from a buffer.
        /// </summary>
        public static IEncodeable DecodeMessage(ArraySegment<byte> buffer, Type expectedType, IServiceMessageContext context)
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

            using var decoder = new JsonDecoder(Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count), context);
            return decoder.DecodeMessage(expectedType);
        }

        /// <summary>
        /// Decodes an object from a buffer.
        /// </summary>
        public IEncodeable DecodeMessage(Type expectedType)
        {
            StringCollection namespaceUris = ReadStringArray("NamespaceUris");
            StringCollection serverUris = ReadStringArray("ServerUris");

            if ((namespaceUris != null && namespaceUris.Count > 0) || (serverUris != null && serverUris.Count > 0))
            {
                NamespaceTable namespaces = (namespaceUris == null || namespaceUris.Count == 0) ? Context.NamespaceUris : new NamespaceTable(namespaceUris);
                StringTable servers = (serverUris == null || serverUris.Count == 0) ? Context.ServerUris : new StringTable(serverUris);

                SetMappingTables(namespaces, servers);
            }

            // read the node id.
            NodeId typeId = ReadNodeId("TypeId");

            // convert to absolute node id.
            var absoluteId = NodeId.ToExpandedNodeId(typeId, Context.NamespaceUris);

            // lookup message type.
            Type actualType = Context.Factory.GetSystemType(absoluteId);

            if (actualType == null)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, Utils.Format("Cannot decode message with type id: {0}.", absoluteId));
            }

            // read the message.

            // return the message.
            return ReadEncodeable("Body", actualType, absoluteId);
        }

        /// <summary>
        /// Initializes the tables used to map namespace and server uris during decoding.
        /// </summary>
        /// <param name="namespaceUris">The namespaces URIs referenced by the data being decoded.</param>
        /// <param name="serverUris">The server URIs referenced by the data being decoded.</param>
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
                        namespaceMappings[ii] = (index >= 0) ? (ushort)index : ushort.MaxValue;
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
                        serverMappings[ii] = (index >= 0) ? (ushort)index : ushort.MaxValue;
                    }
                }

                m_serverMappings = serverMappings;
            }
        }

        /// <summary>
        /// Closes the stream used for reading.
        /// </summary>
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

        /// <summary>
        /// Reads the body extension object from the stream.
        /// </summary>
        public object ReadExtensionObjectBody(ExpandedNodeId typeId)
        {
            return null;
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
                Utils.SilentDispose(m_reader);
                m_reader = null;
            }
        }

        /// <summary>
        /// The type of encoding being used.
        /// </summary>
        public EncodingType EncodingType => EncodingType.Json;

        /// <summary>
        /// The message context associated with the decoder.
        /// </summary>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// Pushes a namespace onto the namespace stack.
        /// </summary>
        public void PushNamespace(string namespaceUri)
        {
        }

        /// <summary>
        /// Pops a namespace from the namespace stack.
        /// </summary>
        public void PopNamespace()
        {
        }

        /// <summary>
        /// Read a decoded JSON field.
        /// </summary>
        /// <param name="fieldName">The name of the field.</param>
        /// <param name="token">The returned object token of the field.</param>
        public bool ReadField(string fieldName, out object token)
        {
            token = null;

            if (string.IsNullOrEmpty(fieldName))
            {
                token = m_stack.Peek();
                return true;
            }

            return (m_stack.Peek() is Dictionary<string, object> context) && context.TryGetValue(fieldName, out token);
        }

        /// <summary>
        /// Reads a boolean from the stream.
        /// </summary>
        public bool ReadBoolean(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

        /// <summary>
        /// Reads a sbyte from the stream.
        /// </summary>
        public sbyte ReadSByte(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

        /// <summary>
        /// Reads a byte from the stream.
        /// </summary>
        public byte ReadByte(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

        /// <summary>
        /// Reads a short from the stream.
        /// </summary>
        public short ReadInt16(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

        /// <summary>
        /// Reads a ushort from the stream.
        /// </summary>
        public ushort ReadUInt16(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

        /// <summary>
        /// Reads an int from the stream.
        /// </summary>
        public int ReadInt32(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

        /// <summary>
        /// Reads a uint from the stream.
        /// </summary>
        public uint ReadUInt32(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

        /// <summary>
        /// Reads a long from the stream.
        /// </summary>
        public long ReadInt64(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                long number = 0;

                if (token is not string text || !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    return 0;
                }

                return number;
            }

            return (long)value;
        }

        /// <summary>
        /// Reads a ulong from the stream.
        /// </summary>
        public ulong ReadUInt64(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
            {
                return 0;
            }

            long? value = token as long?;

            if (value == null)
            {
                ulong number = 0;

                if (token is not string text || !ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
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

        /// <summary>
        /// Reads a float from the stream.
        /// </summary>
        public float ReadFloat(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
            {
                return 0;
            }

            double? value = token as double?;

            if (value == null)
            {
                string text = token as string;
                float number = 0;
                if (text == null || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    if (text != null)
                    {
                        if (string.Equals(text, "Infinity", StringComparison.OrdinalIgnoreCase))
                        {
                            return float.PositiveInfinity;
                        }
                        else if (string.Equals(text, "-Infinity", StringComparison.OrdinalIgnoreCase))
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

        /// <summary>
        /// Reads a double from the stream.
        /// </summary>
        public double ReadDouble(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
            {
                return 0;
            }

            double? value = token as double?;

            if (value == null)
            {
                string text = token as string;
                double number = 0;

                if (text == null || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    if (text != null)
                    {
                        if (string.Equals(text, "Infinity", StringComparison.OrdinalIgnoreCase))
                        {
                            return double.PositiveInfinity;
                        }
                        else if (string.Equals(text, "-Infinity", StringComparison.OrdinalIgnoreCase))
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

        /// <summary>
        /// Reads a string from the stream.
        /// </summary>
        public string ReadString(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

        /// <summary>
        /// Reads a UTC date/time from the stream.
        /// </summary>
        public DateTime ReadDateTime(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Failed to decode DateTime: {0}", fe.Message);
                }
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// Reads a GUID from the stream.
        /// </summary>
        public Uuid ReadGuid(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
            {
                return Uuid.Empty;
            }

            if (token is not string value)
            {
                return Uuid.Empty;
            }

            try
            {
                return new Uuid(value);
            }
            catch (FormatException fe)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Failed to create Guid: {0}", fe.Message);
            }
        }

        /// <summary>
        /// Reads a byte string from the stream.
        /// </summary>
        public byte[] ReadByteString(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
            {
                return null;
            }

            if (token is JTokenNullObject)
            {
                return null;
            }

            if (token is not string value)
            {
                return [];
            }

            byte[] bytes = SafeConvertFromBase64String(value);

            if (Context.MaxByteStringLength > 0 && Context.MaxByteStringLength < bytes.Length)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }

            return bytes;
        }

        /// <summary>
        /// Reads an XmlElement from the stream.
        /// </summary>
        public XmlElement ReadXmlElement(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
            {
                return null;
            }

            if (token is not string value)
            {
                return null;
            }

            try
            {
                var document = new XmlDocument();

                using (var reader = XmlReader.Create(new StringReader(value), Utils.DefaultXmlReaderSettings()))
                {
                    document.Load(reader);
                }

                return document.DocumentElement;
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Unable to decode Xml: {0}", xe.Message);
            }
        }

        /// <summary>
        /// Reads an NodeId from the stream.
        /// </summary>
        public NodeId ReadNodeId(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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
                        new NodeIdParsingOptions() {
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

                object namespaceToken = null;

                if (ReadField("Namespace", out namespaceToken))
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
                        default:
                            return new NodeId(ReadUInt32("Id"), namespaceIndex);

                        case IdType.Opaque:
                            return new NodeId(ReadByteString("Id"), namespaceIndex);

                        case IdType.String:
                            return new NodeId(ReadString("Id"), namespaceIndex);

                        case IdType.Guid:
                            return new NodeId(ReadGuid("Id"), namespaceIndex);
                    }
                }
                return DefaultNodeId(idType, namespaceIndex);
            }
            finally
            {
                m_stack.Pop();
            }
        }

        /// <summary>
        /// Reads an ExpandedNodeId from the stream.
        /// </summary>
        public ExpandedNodeId ReadExpandedNodeId(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
            {
                return ExpandedNodeId.Null;
            }

            if (token is string text)
            {
                ExpandedNodeId nodeId;

                try
                {
                    return ExpandedNodeId.Parse(
                        Context,
                        text,
                        new NodeIdParsingOptions() {
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

                object namespaceToken = null;

                if (ReadField("Namespace", out namespaceToken))
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

                object serverUriToken = null;

                if (ReadField("ServerUri", out serverUriToken))
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
                        default:
                            return new ExpandedNodeId(ReadUInt32("Id"), namespaceIndex, namespaceUri, serverIndex);

                        case IdType.Opaque:
                            return new ExpandedNodeId(ReadByteString("Id"), namespaceIndex, namespaceUri, serverIndex);

                        case IdType.String:
                            return new ExpandedNodeId(ReadString("Id"), namespaceIndex, namespaceUri, serverIndex);

                        case IdType.Guid:
                            return new ExpandedNodeId(ReadGuid("Id"), namespaceIndex, namespaceUri, serverIndex);
                    }
                }

                return new ExpandedNodeId(DefaultNodeId(idType, namespaceIndex), namespaceUri, serverIndex);
            }
            finally
            {
                m_stack.Pop();
            }
        }

        /// <summary>
        /// Reads an StatusCode from the stream.
        /// </summary>
        public StatusCode ReadStatusCode(string fieldName)
        {
            object token;

            if (!ReadField(fieldName, out token))
            {
                // the status code was not found
                return StatusCodes.Good;
            }

            if (token is long code)
            {
                return (StatusCode)code;
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

        /// <summary>
        /// Reads a DiagnosticInfo from the stream.
        /// </summary>
        public DiagnosticInfo ReadDiagnosticInfo(string fieldName)
        {
            return ReadDiagnosticInfo(fieldName, 0);
        }

        /// <summary>
        /// Reads an QualifiedName from the stream.
        /// </summary>
        public QualifiedName ReadQualifiedName(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

                object namespaceToken = null;

                if (ReadField("Uri", out namespaceToken))
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

        /// <summary>
        /// Reads an LocalizedText from the stream.
        /// </summary>
        public LocalizedText ReadLocalizedText(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

        private Variant ReadVariantFromObject(string valueName, BuiltInType builtInType, Dictionary<string, object> value)
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
                            return new Variant(new Matrix(array.Value as Array, builtInType, [.. dimensions]));
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

                    return array;
                }

                return ReadVariantBody(valueName, builtInType);
            }

            return Variant.Null;
        }

        /// <summary>
        /// Reads an Variant from the stream.
        /// </summary>
        public Variant ReadVariant(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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
                BuiltInType builtInType = value.ContainsKey("UaType") ? (BuiltInType)ReadByte("UaType") : (BuiltInType)ReadByte("Type");

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

        /// <summary>
        /// Reads an DataValue from the stream.
        /// </summary>
        public DataValue ReadDataValue(string fieldName)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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
                dv.SourcePicoseconds = (dv.SourceTimestamp != DateTime.MinValue) ? ReadUInt16("SourcePicoseconds") : (ushort)0;
                dv.ServerTimestamp = ReadDateTime("ServerTimestamp");
                dv.ServerPicoseconds = (dv.ServerTimestamp != DateTime.MinValue) ? ReadUInt16("ServerPicoseconds") : (ushort)0;
            }
            finally
            {
                m_stack.Pop();
            }

            return dv;
        }

        /// <summary>
        /// Reads an extension object from the stream.
        /// </summary>
        public ExtensionObject ReadExtensionObject(string fieldName)
        {
            ExtensionObject extension = ExtensionObject.Null;
            object token = null;

            if (!ReadField(fieldName, out token))
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

                if (NodeId.IsNull(typeId))
                {
                    typeId = ReadExpandedNodeId("TypeId");
                    inlineValues = false;
                }

                ExpandedNodeId absoluteId =
                    typeId.IsAbsolute ?
                    typeId :
                    NodeId.ToExpandedNodeId(typeId.InnerNodeId, Context.NamespaceUris);

                if (!NodeId.IsNull(typeId) && NodeId.IsNull(absoluteId))
                {
                    Utils.LogWarning("Cannot de-serialized extension objects if the NamespaceUri is not in the NamespaceTable: Type = {0}", typeId);
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
                    byte[] bytes = ReadByteString(inlineValues ? "UaBody" : "Body");
                    return new ExtensionObject(typeId, bytes ?? []);
                }

                if (encoding == ExtensionObjectEncoding.Xml)
                {
                    XmlElement xml = ReadXmlElement(inlineValues ? "UaBody" : "Body");
                    if (xml == null)
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

                Type systemType = Context.Factory.GetSystemType(typeId);

                if (systemType != null)
                {
                    IEncodeable encodeable = null;

                    if (inlineValues)
                    {
                        encodeable = Activator.CreateInstance(systemType) as IEncodeable;

                        if (encodeable == null)
                        {
                            throw new ServiceResultException(StatusCodes.BadDecodingError, Utils.Format("Type does not support IEncodeable interface: '{0}'", systemType.FullName));
                        }

                        encodeable.Decode(this);
                    }
                    else
                    {
                        encodeable = ReadEncodeable("Body", systemType, typeId);

                        if (encodeable == null)
                        {
                            return extension;
                        }
                    }

                    return new ExtensionObject(typeId, encodeable);
                }

                using var ostrm = new MemoryStream();
                using (var stream = new StreamWriter(ostrm))
                using (var writer = new JsonTextWriter(stream))
                {
                    EncodeAsJson(writer, token);
                }
                // Close the writer before retrieving the data
                return new ExtensionObject(typeId, ostrm.ToArray());
            }
            finally
            {
                m_stack.Pop();
            }
        }

        /// <summary>
        /// Reads an encodeable object from the stream.
        /// </summary>
        /// <param name="fieldName">The encodeable object field name</param>
        /// <param name="systemType">The system type of the encodeable object to be read</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instance that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> object that was read from the stream.</returns>
        public IEncodeable ReadEncodeable(string fieldName, Type systemType, ExpandedNodeId encodeableTypeId = null)
        {
            if (systemType == null)
            {
                throw new ArgumentNullException(nameof(systemType));
            }

            object token = null;

            if (!ReadField(fieldName, out token))
            {
                return null;
            }

            if (Activator.CreateInstance(systemType) is not IEncodeable value)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, Utils.Format("Type does not support IEncodeable interface: '{0}'", systemType.FullName));
            }

            if (encodeableTypeId != null)
            {
                // set type identifier for custom complex data types before decode.

                if (value is IComplexTypeInstance complexTypeInstance)
                {
                    complexTypeInstance.TypeId = encodeableTypeId;
                }
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

        /// <summary>
        ///  Reads an enumerated value from the stream.
        /// </summary>
        public Enum ReadEnumerated(string fieldName, Type enumType)
        {
            if (enumType == null)
            {
                throw new ArgumentNullException(nameof(enumType));
            }

            object token;

            if (!ReadField(fieldName, out token))
            {
                return (Enum)Enum.ToObject(enumType, 0);
            }

            if (token is long code)
            {
                return (Enum)Enum.ToObject(enumType, code);
            }

            if (token is string text)
            {
                int index = text.LastIndexOf('_');

                if (index > 0 && long.TryParse(text[(index + 1)..], out code))
                {
                    return (Enum)Enum.ToObject(enumType, code);
                }
            }

            return (Enum)Enum.ToObject(enumType, 0);
        }

        /// <summary>
        /// Reads a boolean array from the stream.
        /// </summary>
        public BooleanCollection ReadBooleanArray(string fieldName)
        {
            var values = new BooleanCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadBoolean(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a sbyte array from the stream.
        /// </summary>
        public SByteCollection ReadSByteArray(string fieldName)
        {
            var values = new SByteCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadSByte(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a byte array from the stream.
        /// </summary>
        public ByteCollection ReadByteArray(string fieldName)
        {
            var values = new ByteCollection();

            List<object> token = null;

            string value = ReadString(fieldName);
            if (value != null)
            {
                return SafeConvertFromBase64String(value);
            }

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadByte(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a short array from the stream.
        /// </summary>
        public Int16Collection ReadInt16Array(string fieldName)
        {
            var values = new Int16Collection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadInt16(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a ushort array from the stream.
        /// </summary>
        public UInt16Collection ReadUInt16Array(string fieldName)
        {
            var values = new UInt16Collection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadUInt16(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a int array from the stream.
        /// </summary>
        public Int32Collection ReadInt32Array(string fieldName)
        {
            var values = new Int32Collection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadInt32(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a uint array from the stream.
        /// </summary>
        public UInt32Collection ReadUInt32Array(string fieldName)
        {
            var values = new UInt32Collection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadUInt32(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a long array from the stream.
        /// </summary>
        public Int64Collection ReadInt64Array(string fieldName)
        {
            var values = new Int64Collection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadInt64(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a ulong array from the stream.
        /// </summary>
        public UInt64Collection ReadUInt64Array(string fieldName)
        {
            var values = new UInt64Collection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadUInt64(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a float array from the stream.
        /// </summary>
        public FloatCollection ReadFloatArray(string fieldName)
        {
            var values = new FloatCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadFloat(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a double array from the stream.
        /// </summary>
        public DoubleCollection ReadDoubleArray(string fieldName)
        {
            var values = new DoubleCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadDouble(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a string array from the stream.
        /// </summary>
        public StringCollection ReadStringArray(string fieldName)
        {
            var values = new StringCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadString(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a UTC date/time array from the stream.
        /// </summary>
        public DateTimeCollection ReadDateTimeArray(string fieldName)
        {
            var values = new DateTimeCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    values.Add(ReadDateTime(null));
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a GUID array from the stream.
        /// </summary>
        public UuidCollection ReadGuidArray(string fieldName)
        {
            var values = new UuidCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    Uuid element = ReadGuid(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads a byte string array from the stream.
        /// </summary>
        public ByteStringCollection ReadByteStringArray(string fieldName)
        {
            var values = new ByteStringCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    byte[] element = ReadByteString(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an XmlElement array from the stream.
        /// </summary>
        public XmlElementCollection ReadXmlElementArray(string fieldName)
        {
            var values = new XmlElementCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    XmlElement element = ReadXmlElement(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an NodeId array from the stream.
        /// </summary>
        public NodeIdCollection ReadNodeIdArray(string fieldName)
        {
            var values = new NodeIdCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    NodeId element = ReadNodeId(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an ExpandedNodeId array from the stream.
        /// </summary>
        public ExpandedNodeIdCollection ReadExpandedNodeIdArray(string fieldName)
        {
            var values = new ExpandedNodeIdCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    ExpandedNodeId element = ReadExpandedNodeId(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an StatusCode array from the stream.
        /// </summary>
        public StatusCodeCollection ReadStatusCodeArray(string fieldName)
        {
            var values = new StatusCodeCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    StatusCode element = ReadStatusCode(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an DiagnosticInfo array from the stream.
        /// </summary>
        public DiagnosticInfoCollection ReadDiagnosticInfoArray(string fieldName)
        {
            var values = new DiagnosticInfoCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    DiagnosticInfo element = ReadDiagnosticInfo(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an QualifiedName array from the stream.
        /// </summary>
        public QualifiedNameCollection ReadQualifiedNameArray(string fieldName)
        {
            var values = new QualifiedNameCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    QualifiedName element = ReadQualifiedName(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an LocalizedText array from the stream.
        /// </summary>
        public LocalizedTextCollection ReadLocalizedTextArray(string fieldName)
        {
            var values = new LocalizedTextCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    LocalizedText element = ReadLocalizedText(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an Variant array from the stream.
        /// </summary>
        public VariantCollection ReadVariantArray(string fieldName)
        {
            var values = new VariantCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    Variant element = ReadVariant(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an DataValue array from the stream.
        /// </summary>
        public DataValueCollection ReadDataValueArray(string fieldName)
        {
            var values = new DataValueCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    DataValue element = ReadDataValue(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an array of extension objects from the stream.
        /// </summary>
        public ExtensionObjectCollection ReadExtensionObjectArray(string fieldName)
        {
            var values = new ExtensionObjectCollection();

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return values;
            }

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    ExtensionObject element = ReadExtensionObject(null);
                    values.Add(element);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an encodeable array from the stream.
        /// </summary>
        /// <param name="fieldName">The encodeable array field name</param>
        /// <param name="systemType">The system type of the encodeable objects to be read object</param>
        /// <param name="encodeableTypeId">The TypeId for the <see cref="IEncodeable"/> instances that will be read.</param>
        /// <returns>An <see cref="IEncodeable"/> array that was read from the stream.</returns>
        public Array ReadEncodeableArray(string fieldName, Type systemType, ExpandedNodeId encodeableTypeId = null)
        {
            if (systemType == null)
            {
                throw new ArgumentNullException(nameof(systemType));
            }

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return Array.CreateInstance(systemType, 0);
            }

            var values = Array.CreateInstance(systemType, token.Count);

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    IEncodeable element = ReadEncodeable(null, systemType, encodeableTypeId);
                    values.SetValue(element, ii);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <summary>
        /// Reads an enumerated value array from the stream.
        /// </summary>
        public Array ReadEnumeratedArray(string fieldName, Type enumType)
        {
            if (enumType == null)
            {
                throw new ArgumentNullException(nameof(enumType));
            }

            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
            {
                return Array.CreateInstance(enumType, 0);
            }

            var values = Array.CreateInstance(enumType, token.Count);

            for (int ii = 0; ii < token.Count; ii++)
            {
                try
                {
                    m_stack.Push(token[ii]);
                    Enum element = ReadEnumerated(null, enumType);
                    values.SetValue(element, ii);
                }
                finally
                {
                    m_stack.Pop();
                }
            }

            return values;
        }

        /// <inheritdoc/>
        public Array ReadArray(
            string fieldName,
            int valueRank,
            BuiltInType builtInType,
            Type systemType = null,
            ExpandedNodeId encodeableTypeId = null)
        {
            if (valueRank == ValueRanks.OneDimension)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                        return ReadBooleanArray(fieldName).ToArray();
                    case BuiltInType.SByte:
                        return ReadSByteArray(fieldName).ToArray();
                    case BuiltInType.Byte:
                        return ReadByteArray(fieldName).ToArray();
                    case BuiltInType.Int16:
                        return ReadInt16Array(fieldName).ToArray();
                    case BuiltInType.UInt16:
                        return ReadUInt16Array(fieldName).ToArray();
                    case BuiltInType.Enumeration:
                        DetermineIEncodeableSystemType(ref systemType, encodeableTypeId);
                        if (systemType?.IsEnum == true)
                        {
                            return ReadEnumeratedArray(fieldName, systemType);
                        }
                        goto case BuiltInType.Int32;

                    case BuiltInType.Int32:
                        return ReadInt32Array(fieldName).ToArray();
                    case BuiltInType.UInt32:
                        return ReadUInt32Array(fieldName).ToArray();
                    case BuiltInType.Int64:
                        return ReadInt64Array(fieldName).ToArray();
                    case BuiltInType.UInt64:
                        return ReadUInt64Array(fieldName).ToArray();
                    case BuiltInType.Float:
                        return ReadFloatArray(fieldName).ToArray();
                    case BuiltInType.Double:
                        return ReadDoubleArray(fieldName).ToArray();
                    case BuiltInType.String:
                        return ReadStringArray(fieldName).ToArray();
                    case BuiltInType.DateTime:
                        return ReadDateTimeArray(fieldName).ToArray();
                    case BuiltInType.Guid:
                        return ReadGuidArray(fieldName).ToArray();
                    case BuiltInType.ByteString:
                        return ReadByteStringArray(fieldName).ToArray();
                    case BuiltInType.XmlElement:
                        return ReadXmlElementArray(fieldName).ToArray();
                    case BuiltInType.NodeId:
                        return ReadNodeIdArray(fieldName).ToArray();
                    case BuiltInType.ExpandedNodeId:
                        return ReadExpandedNodeIdArray(fieldName).ToArray();
                    case BuiltInType.StatusCode:
                        return ReadStatusCodeArray(fieldName).ToArray();
                    case BuiltInType.QualifiedName:
                        return ReadQualifiedNameArray(fieldName).ToArray();
                    case BuiltInType.LocalizedText:
                        return ReadLocalizedTextArray(fieldName).ToArray();
                    case BuiltInType.DataValue:
                        return ReadDataValueArray(fieldName).ToArray();
                    case BuiltInType.Variant:
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }
                        return ReadVariantArray(fieldName).ToArray();

                    case BuiltInType.ExtensionObject:
                        return ReadExtensionObjectArray(fieldName).ToArray();
                    case BuiltInType.DiagnosticInfo:
                        return ReadDiagnosticInfoArray(fieldName).ToArray();
                    default:
                        if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                        {
                            return ReadEncodeableArray(fieldName, systemType, encodeableTypeId);
                        }

                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            Utils.Format("Cannot decode unknown type in Array object with BuiltInType: {0}.", builtInType));
                }
            }
            else if (valueRank >= ValueRanks.TwoDimensions)
            {
                object token = null;

                if (!ReadField(fieldName, out token))
                {
                    return null;
                }

                if (token is Dictionary<string, object> value)
                {
                    m_stack.Push(value);
                    Int32Collection dimensions2 = null;

                    if (value.ContainsKey("Dimensions"))
                    {
                        dimensions2 = ReadInt32Array("Dimensions");
                    }
                    else
                    {
                        dimensions2 = new Int32Collection(valueRank);
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
                    return null;
                }

                var elements = new List<object>();
                var dimensions = new List<int>();
                if (builtInType is BuiltInType.Enumeration or BuiltInType.Variant or BuiltInType.Null)
                {
                    DetermineIEncodeableSystemType(ref systemType, encodeableTypeId);
                }
                ReadMatrixPart(fieldName, array, builtInType, ref elements, ref dimensions, 0, systemType, encodeableTypeId);

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
                        dimensions.Count, valueRank);
                }

                Matrix matrix = null;

                try
                {
                    switch (builtInType)
                    {
                        case BuiltInType.Boolean:
                            matrix = new Matrix(elements.Cast<bool>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.SByte:
                            matrix = new Matrix(elements.Cast<sbyte>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.Byte:
                            matrix = new Matrix(elements.Cast<byte>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.Int16:
                            matrix = new Matrix(elements.Cast<short>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.UInt16:
                            matrix = new Matrix(elements.Cast<ushort>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.Int32:
                            matrix = new Matrix(elements.Cast<int>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.UInt32:
                            matrix = new Matrix(elements.Cast<uint>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.Int64:
                            matrix = new Matrix(elements.Cast<long>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.UInt64:
                            matrix = new Matrix(elements.Cast<ulong>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.Float:
                            matrix = new Matrix(elements.Cast<float>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.Double:
                            matrix = new Matrix(elements.Cast<double>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.String:
                            matrix = new Matrix(elements.Cast<string>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.DateTime:
                            matrix = new Matrix(elements.Cast<DateTime>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.Guid:
                            matrix = new Matrix(elements.Cast<Uuid>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.ByteString:
                            matrix = new Matrix(elements.Cast<byte[]>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.XmlElement:
                            matrix = new Matrix(elements.Cast<XmlElement>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.NodeId:
                            matrix = new Matrix(elements.Cast<NodeId>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.ExpandedNodeId:
                            matrix = new Matrix(elements.Cast<ExpandedNodeId>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.StatusCode:
                            matrix = new Matrix(elements.Cast<StatusCode>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.QualifiedName:
                            matrix = new Matrix(elements.Cast<QualifiedName>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.LocalizedText:
                            matrix = new Matrix(elements.Cast<LocalizedText>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.DataValue:
                            matrix = new Matrix(elements.Cast<DataValue>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.Enumeration:
                        {
                            if (systemType?.IsEnum == true)
                            {
                                var newElements = Array.CreateInstance(systemType, elements.Count);
                                int ii = 0;
                                foreach (object element in elements)
                                {
                                    newElements.SetValue(Convert.ChangeType(element, systemType, CultureInfo.InvariantCulture), ii++);
                                }
                                matrix = new Matrix(newElements, builtInType, [.. dimensions]);
                            }
                            else
                            {
                                matrix = new Matrix(elements.Cast<int>().ToArray(), builtInType, [.. dimensions]);
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
                                    newElements.SetValue(Convert.ChangeType(elements[i], systemType, CultureInfo.InvariantCulture), i);
                                }
                                matrix = new Matrix(newElements, builtInType, [.. dimensions]);
                                break;
                            }
                            matrix = new Matrix(elements.Cast<Variant>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        }
                        case BuiltInType.ExtensionObject:
                            matrix = new Matrix(elements.Cast<ExtensionObject>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        case BuiltInType.DiagnosticInfo:
                            matrix = new Matrix(elements.Cast<DiagnosticInfo>().ToArray(), builtInType, [.. dimensions]);
                            break;
                        default:
                        {
                            if (DetermineIEncodeableSystemType(ref systemType, encodeableTypeId))
                            {
                                var newElements = Array.CreateInstance(systemType, elements.Count);
                                for (int i = 0; i < elements.Count; i++)
                                {
                                    newElements.SetValue(Convert.ChangeType(elements[i], systemType, CultureInfo.InvariantCulture), i);
                                }
                                matrix = new Matrix(newElements, builtInType, [.. dimensions]);
                                break;
                            }

                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Cannot decode unknown type in Array object with BuiltInType: {0}.",
                                builtInType);
                        }
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
                        fieldName = switches[(int)index];
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
                        return (uint)index;
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
            object token = null;

            if (!ReadField(fieldName, out token))
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

        /// <summary>
        /// Push an Array item on the Read Stack
        /// </summary>
        /// <param name="fieldName">The array name</param>
        /// <param name="index">The index of the item that shall be placed on the Read Stack</param>
        /// <returns>true if successful</returns>
        public bool PushArray(string fieldName, int index)
        {
            List<object> token = null;

            if (!ReadArrayField(fieldName, out token))
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

        /// <summary>
        /// Pop the current object from the Read Stack
        /// </summary>
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
                else
                {
                    index = Context.NamespaceUris.GetIndexOrAppend(uri);
                }
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
                throw new ServiceResultException(StatusCodes.BadDecodingError, $"No mapping for NamespaceIndex={index}.");
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
                else
                {
                    index = Context.ServerUris.GetIndexOrAppend(uri);
                }
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
                throw new ServiceResultException(StatusCodes.BadDecodingError, $"No mapping for ServerIndex(={index}.");
            }

            return m_serverMappings[index];
        }

        /// <summary>
        /// Helper to provide the TryParse method when reading an enumerated string.
        /// </summary>
        private delegate bool TryParseHandler<T>(string s, NumberStyles numberStyles, CultureInfo cultureInfo, out T result);

        /// <summary>
        /// Helper to read an enumerated string in an extension object.
        /// </summary>
        /// <typeparam name="T">The number type which was encoded.</typeparam>
        /// <param name="token"></param>
        /// <param name="handler"></param>
        /// <returns>The parsed number or 0.</returns>
        private static T ReadEnumeratedString<T>(object token, TryParseHandler<T> handler) where T : struct
        {
            T number = default;
            if (token is string text)
            {
                bool retry = false;
                do
                {
                    if (handler?.Invoke(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) == false)
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
        private DiagnosticInfo ReadDiagnosticInfo(string fieldName, int depth)
        {
            object token = null;

            if (!ReadField(fieldName, out token))
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

                if (value.ContainsKey("InnerDiagnosticInfo") && depth < DiagnosticInfo.MaxInnerDepth)
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
        /// Get the system type from the type factory if not specified by caller.
        /// </summary>
        /// <param name="systemType">The reference to the system type, or null</param>
        /// <param name="encodeableTypeId">The encodeable type id of the system type.</param>
        /// <returns>If the system type is assignable to <see cref="IEncodeable"/> </returns>
        private bool DetermineIEncodeableSystemType(ref Type systemType, ExpandedNodeId encodeableTypeId)
        {
            if (encodeableTypeId != null && systemType == null)
            {
                systemType = Context.Factory.GetSystemType(encodeableTypeId);
            }
            return typeof(IEncodeable).IsAssignableFrom(systemType);
        }

        /// <summary>
        /// Read the body of a Variant as a BuiltInType
        /// </summary>
        private Variant ReadVariantBody(string fieldName, BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    return new Variant(ReadBoolean(fieldName), TypeInfo.Scalars.Boolean);

                case BuiltInType.SByte:
                    return new Variant(ReadSByte(fieldName), TypeInfo.Scalars.SByte);

                case BuiltInType.Byte:
                    return new Variant(ReadByte(fieldName), TypeInfo.Scalars.Byte);

                case BuiltInType.Int16:
                    return new Variant(ReadInt16(fieldName), TypeInfo.Scalars.Int16);

                case BuiltInType.UInt16:
                    return new Variant(ReadUInt16(fieldName), TypeInfo.Scalars.UInt16);

                case BuiltInType.Int32:
                    return new Variant(ReadInt32(fieldName), TypeInfo.Scalars.Int32);

                case BuiltInType.UInt32:
                    return new Variant(ReadUInt32(fieldName), TypeInfo.Scalars.UInt32);

                case BuiltInType.Int64:
                    return new Variant(ReadInt64(fieldName), TypeInfo.Scalars.Int64);

                case BuiltInType.UInt64:
                    return new Variant(ReadUInt64(fieldName), TypeInfo.Scalars.UInt64);

                case BuiltInType.Float:
                    return new Variant(ReadFloat(fieldName), TypeInfo.Scalars.Float);

                case BuiltInType.Double:
                    return new Variant(ReadDouble(fieldName), TypeInfo.Scalars.Double);

                case BuiltInType.String:
                    return new Variant(ReadString(fieldName), TypeInfo.Scalars.String);

                case BuiltInType.ByteString:
                    return new Variant(ReadByteString(fieldName), TypeInfo.Scalars.ByteString);

                case BuiltInType.DateTime:
                    return new Variant(ReadDateTime(fieldName), TypeInfo.Scalars.DateTime);

                case BuiltInType.Guid:
                    return new Variant(ReadGuid(fieldName), TypeInfo.Scalars.Guid);

                case BuiltInType.NodeId:
                    return new Variant(ReadNodeId(fieldName), TypeInfo.Scalars.NodeId);

                case BuiltInType.ExpandedNodeId:
                    return new Variant(ReadExpandedNodeId(fieldName), TypeInfo.Scalars.ExpandedNodeId);

                case BuiltInType.QualifiedName:
                    return new Variant(ReadQualifiedName(fieldName), TypeInfo.Scalars.QualifiedName);

                case BuiltInType.LocalizedText:
                    return new Variant(ReadLocalizedText(fieldName), TypeInfo.Scalars.LocalizedText);

                case BuiltInType.StatusCode:
                    return new Variant(ReadStatusCode(fieldName), TypeInfo.Scalars.StatusCode);

                case BuiltInType.XmlElement:
                    return new Variant(ReadXmlElement(fieldName), TypeInfo.Scalars.XmlElement);

                case BuiltInType.ExtensionObject:
                    return new Variant(ReadExtensionObject(fieldName), TypeInfo.Scalars.ExtensionObject);

                case BuiltInType.Variant:
                    return new Variant(ReadVariant(fieldName), TypeInfo.Scalars.Variant);

                case BuiltInType.DiagnosticInfo:
                    return new Variant(ReadDiagnosticInfo(fieldName), TypeInfo.Scalars.DiagnosticInfo);

                case BuiltInType.DataValue:
                    return new Variant(ReadDataValue(fieldName), TypeInfo.Scalars.DataValue);
            }

            return Variant.Null;
        }

        /// <summary>
        /// Read the Body of a Variant as an Array
        /// </summary>
        private Variant ReadVariantArrayBody(string fieldName, BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    return new Variant(ReadBooleanArray(fieldName), TypeInfo.Arrays.Boolean);

                case BuiltInType.SByte:
                    return new Variant(ReadSByteArray(fieldName), TypeInfo.Arrays.SByte);

                case BuiltInType.Byte:
                    return new Variant(ReadByteArray(fieldName), TypeInfo.Arrays.Byte);

                case BuiltInType.Int16:
                    return new Variant(ReadInt16Array(fieldName), TypeInfo.Arrays.Int16);

                case BuiltInType.UInt16:
                    return new Variant(ReadUInt16Array(fieldName), TypeInfo.Arrays.UInt16);

                case BuiltInType.Int32:
                    return new Variant(ReadInt32Array(fieldName), TypeInfo.Arrays.Int32);

                case BuiltInType.UInt32:
                    return new Variant(ReadUInt32Array(fieldName), TypeInfo.Arrays.UInt32);

                case BuiltInType.Int64:
                    return new Variant(ReadInt64Array(fieldName), TypeInfo.Arrays.Int64);

                case BuiltInType.UInt64:
                    return new Variant(ReadUInt64Array(fieldName), TypeInfo.Arrays.UInt64);

                case BuiltInType.Float:
                    return new Variant(ReadFloatArray(fieldName), TypeInfo.Arrays.Float);

                case BuiltInType.Double:
                    return new Variant(ReadDoubleArray(fieldName), TypeInfo.Arrays.Double);

                case BuiltInType.String:
                    return new Variant(ReadStringArray(fieldName), TypeInfo.Arrays.String);

                case BuiltInType.ByteString:
                    return new Variant(ReadByteStringArray(fieldName), TypeInfo.Arrays.ByteString);

                case BuiltInType.DateTime:
                    return new Variant(ReadDateTimeArray(fieldName), TypeInfo.Arrays.DateTime);

                case BuiltInType.Guid:
                    return new Variant(ReadGuidArray(fieldName), TypeInfo.Arrays.Guid);

                case BuiltInType.NodeId:
                    return new Variant(ReadNodeIdArray(fieldName), TypeInfo.Arrays.NodeId);

                case BuiltInType.ExpandedNodeId:
                    return new Variant(ReadExpandedNodeIdArray(fieldName), TypeInfo.Arrays.ExpandedNodeId);

                case BuiltInType.QualifiedName:
                    return new Variant(ReadQualifiedNameArray(fieldName), TypeInfo.Arrays.QualifiedName);

                case BuiltInType.LocalizedText:
                    return new Variant(ReadLocalizedTextArray(fieldName), TypeInfo.Arrays.LocalizedText);

                case BuiltInType.StatusCode:
                    return new Variant(ReadStatusCodeArray(fieldName), TypeInfo.Arrays.StatusCode);

                case BuiltInType.XmlElement:
                    return new Variant(ReadXmlElementArray(fieldName), TypeInfo.Arrays.XmlElement);

                case BuiltInType.ExtensionObject:
                    return new Variant(ReadExtensionObjectArray(fieldName), TypeInfo.Arrays.ExtensionObject);

                case BuiltInType.Variant:
                    return new Variant(ReadVariantArray(fieldName), TypeInfo.Arrays.Variant);

                case BuiltInType.DiagnosticInfo:
                    return new Variant(ReadDiagnosticInfoArray(fieldName), TypeInfo.Arrays.DiagnosticInfo);

                case BuiltInType.DataValue:
                    return new Variant(ReadDataValueArray(fieldName), TypeInfo.Arrays.DataValue);
            }

            return Variant.Null;
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

                        default:
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
        /// <returns></returns>
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

                                default:
                                    break;
                            }
                        }
                    }
                }
            }
            catch (JsonReaderException jre)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Error reading JSON object: {0}", jre.Message);
            }
            return fields;
        }

        /// <summary>
        /// Read the Matrix part (simple array or array of arrays)
        /// </summary>
        private void ReadMatrixPart(
            string fieldName,
            List<object> currentArray,
            BuiltInType builtInType,
            ref List<object> elements,
            ref List<int> dimensions,
            int level,
            Type systemType,
            ExpandedNodeId encodeableTypeId)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                if (currentArray?.Count > 0)
                {
                    bool hasInnerArray = false;
                    for (int ii = 0; ii < currentArray.Count; ii++)
                    {
                        if (ii == 0 && dimensions.Count <= level)
                        {
                            // remember dimension length
                            dimensions.Add(currentArray.Count);
                        }
                        if (currentArray[ii] is List<object>)
                        {
                            hasInnerArray = true;

                            PushArray(fieldName, ii);

                            ReadMatrixPart(null, currentArray[ii] as List<object>, builtInType, ref elements, ref dimensions, level + 1, systemType, encodeableTypeId);

                            Pop();
                        }
                        else
                        {
                            break; // do not continue reading array of array
                        }
                    }
                    if (!hasInnerArray)
                    {
                        // read array from one dimension
                        Array part = ReadArray(null, ValueRanks.OneDimension, builtInType, systemType, encodeableTypeId);
                        if (part != null && part.Length > 0)
                        {
                            // add part elements to final list
                            foreach (object item in part)
                            {
                                elements.Add(item);
                            }
                        }
                    }
                }
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Get Default value for NodeId for diferent IdTypes
        /// </summary>
        /// <returns>new NodeId</returns>
        private static NodeId DefaultNodeId(IdType idType, ushort namespaceIndex)
        {
            switch (idType)
            {
                case IdType.Numeric:
                default:
                    return new NodeId(0U, namespaceIndex);

                case IdType.Opaque:
                    return new NodeId([], namespaceIndex);

                case IdType.String:
                    return new NodeId("", namespaceIndex);

                case IdType.Guid:
                    return new NodeId(Guid.Empty, namespaceIndex);
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
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Unable to encode ExtensionObject Body as Json: {0}", jwe.Message);
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
            object token;

            if (!ReadField(fieldName, out token))
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
        private static byte[] SafeConvertFromBase64String(string s)
        {
            try
            {
                return Convert.FromBase64String(s);
            }
            catch (FormatException fe)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Error decoding base64 string: {0}", fe.Message);
            }
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
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
