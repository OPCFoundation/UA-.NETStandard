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
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Reads objects from a XML stream.
    /// </summary>
    public sealed class XmlDecoder : IDecoder
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public XmlDecoder(Stream stream, IServiceMessageContext context)
            : this(XmlReader.Create(
                stream,
                CoreUtils.DefaultXmlReaderSettings()), context)
        {
        }

        /// <summary>
        /// Initializes the object with an XML element to parse.
        /// </summary>
        public XmlDecoder(XmlElement element, IServiceMessageContext context)
            : this(XmlReader.Create(
                new StringReader(element.OuterXml ?? string.Empty),
                CoreUtils.DefaultXmlReaderSettings()), context)
        {
        }

        /// <summary>
        /// Initializes the object with an XML element to parse.
        /// </summary>
        public XmlDecoder(System.Xml.XmlElement element, IServiceMessageContext context)
            : this(XmlReader.Create(
                new StringReader(element.OuterXml ?? string.Empty),
                CoreUtils.DefaultXmlReaderSettings()), context)
        {
        }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public XmlDecoder(XmlReader reader, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlDecoder>();
            m_nestingLevel = 0;
            m_reader = reader;
        }

        /// <summary>
        /// Initializes the object with a XML reader.
        /// </summary>
        public XmlDecoder(Type? systemType, XmlReader reader, IServiceMessageContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Telemetry.CreateLogger<XmlDecoder>();
            m_reader = reader;
            m_nestingLevel = 0;

            string? ns = null;
            string? name = null;

            if (systemType != null)
            {
                XmlQualifiedName? typeName = TypeInfo.GetXmlName(systemType);
                ns = typeName!.Namespace;
                name = typeName.Name;
            }

            if (ns == null)
            {
                m_reader.MoveToContent();
                ns = m_reader.NamespaceURI;
                name = m_reader.Name;
            }

            int index = name!.IndexOf(':', StringComparison.Ordinal);

            if (index != -1)
            {
                name = name![(index + 1)..];
            }

            PushNamespace(ns);
            BeginField(name, false);
        }

        /// <summary>
        /// Initializes a string table from an XML stream.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="elementName">Name of the element.</param>
        /// <param name="stringTable">The string table.</param>
        /// <returns>True if the table was found. False otherwise.</returns>
        public bool LoadStringTable(string tableName, string elementName, StringTable stringTable)
        {
            PushNamespace(Namespaces.OpcUaXsd);

            try
            {
                if (!Peek(tableName))
                {
                    return false;
                }

                ReadStartElement();

                while (Peek(elementName))
                {
                    // table entries are URIs (xs:anyURI collapses whitespace).
                    string namespaceUri = ReadString(elementName)?.Trim()!;
                    stringTable.Append(namespaceUri);
                }

                Skip(new XmlQualifiedName(tableName, Namespaces.OpcUaXsd));
                return true;
            }
            finally
            {
                PopNamespace();
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
            if (checkEof && m_reader.NodeType != XmlNodeType.None)
            {
                m_reader.ReadEndElement();
            }

            m_reader.Close();
        }

        /// <summary>
        /// Returns the qualified name for the next element in the stream.
        /// </summary>
        public XmlQualifiedName? Peek(XmlNodeType nodeType)
        {
            m_reader.MoveToContent();

            if (nodeType != XmlNodeType.None && nodeType != m_reader.NodeType)
            {
                return null;
            }

            return new XmlQualifiedName(m_reader.LocalName, m_reader.NamespaceURI);
        }

        /// <summary>
        /// Returns true if the specified field is the next element to be extracted.
        /// </summary>
        public bool Peek(string? fieldName)
        {
            m_reader.MoveToContent();

            if (XmlNodeType.Element != m_reader.NodeType)
            {
                return false;
            }

            if (fieldName != m_reader.LocalName)
            {
                return false;
            }

            return m_namespaces.Peek() == m_reader.NamespaceURI;
        }

        /// <summary>
        /// Returns the qualified name for the next element in the stream.
        /// </summary>
        public void ReadStartElement()
        {
            bool isEmpty = m_reader.IsEmptyElement;
            m_reader.ReadStartElement();

            if (!isEmpty)
            {
                m_reader.MoveToContent();
            }
        }

        /// <summary>
        /// Skips to the end of the specified element. Assumes we are already in
        /// the element to skip. Will skip all nested elements with the same name
        /// as well.
        /// </summary>
        /// <param name="qname">The qualified name of the element to skip.</param>
        /// <exception cref="ServiceResultException"></exception>
        public void Skip(XmlQualifiedName qname)
        {
            try
            {
                m_reader.MoveToContent();

                int depth = 1;

                // Skip to end passing all nested elements with the same name.
                while (depth > 0 && m_reader.NodeType != XmlNodeType.None)
                {
                    if (m_reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (m_reader.LocalName == qname.Name &&
                            m_reader.NamespaceURI == qname.Namespace)
                        {
                            depth--;
                        }
                    }
                    else if (m_reader.NodeType == XmlNodeType.Element)
                    {
                        if (m_reader.LocalName == qname.Name &&
                            m_reader.NamespaceURI == qname.Namespace)
                        {
                            // depth++;
                            // Handled by skip, skipping the entire tree
                        }
                    }

                    m_reader.Skip();
                    m_reader.MoveToContent();
                }
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Skip {0} failed: {1}",
                    qname.Name,
                    xe.Message);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_reader?.Dispose();
            m_reader = null!;
        }

        /// <inheritdoc/>
        public EncodingType EncodingType => EncodingType.Xml;

        /// <inheritdoc/>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// Reads a String element that holds only whitespace as an empty string.
        /// </summary>
        /// <remarks>
        /// xs:string preserves whitespace (Part 6 5.3.1.5), so by default such an
        /// element decodes to its whitespace. Hand-edited, pretty-printed documents
        /// such as NodeSets write empty values as an element with only layout
        /// whitespace (e.g. an empty Locale); importers of such documents enable
        /// this option to read them as empty strings.
        /// </remarks>
        public bool TreatWhitespaceOnlyStringsAsEmpty { get; set; }

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
        public void SetMappingTables(NamespaceTable? namespaceUris, StringTable? serverUris)
        {
            m_namespaceMappings = null;

            if (namespaceUris != null && Context.NamespaceUris != null)
            {
                m_namespaceMappings = Context.NamespaceUris.CreateMapping(namespaceUris, false);
            }

            m_serverMappings = null;

            if (serverUris != null && Context.ServerUris != null)
            {
                m_serverMappings = Context.ServerUris.CreateMapping(serverUris, false);
            }
        }

        /// <summary>
        /// Adopts already resolved mapping tables and the current nesting depth
        /// from an outer decoder. Used when an ExtensionObject with an XML body
        /// is decoded from inside another decoder - the nested body is part of
        /// the same message and must share both.
        /// </summary>
        internal void InheritDecodingState(
            ushort[]? namespaceMappings,
            ushort[]? serverMappings,
            uint nestingLevel)
        {
            m_namespaceMappings = namespaceMappings;
            m_serverMappings = serverMappings;
            m_nestingLevel = nestingLevel;
        }

        /// <inheritdoc/>
        public T DecodeMessage<T>() where T : IEncodeable
        {
            XmlQualifiedName? typeName = Peek(XmlNodeType.Element);
            IType? type = null;
            if (typeName != null &&
                !Context.Factory.TryGetType(typeName, out type) &&
                typeName.Namespace == Namespaces.OpcUaXsd)
            {
                // Standard message wrappers use the Types schema; generated types
                // may be registered under the corresponding UA model namespace.
                Context.Factory.TryGetType(new XmlQualifiedName(typeName.Name, Namespaces.OpcUa), out type);
            }
            if (typeName == null || type is not IEncodeableType activator)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Cannot decode message '{0}'.",
                    typeof(T));
            }

            string name = typeName.Name;
            int index = name.IndexOf(':', StringComparison.Ordinal);

            if (index != -1)
            {
                name = name[(index + 1)..];
            }

            PushNamespace(typeName.Namespace);

            // read the message.
            if (activator.CreateInstance() is not T instance)
            {
                // The type name comes from the document and need not name a T.
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Type '{0}' is not a {1}.",
                    typeName,
                    typeof(T).Name);
            }
            T encodeable = ReadEncodeable(name, instance);

            PopNamespace();

            return encodeable;
        }

        /// <inheritdoc/>
        public bool ReadBoolean(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    // xs:boolean is true, false, 1 or 0 - case sensitive.
                    bool value = SafeXmlConvert(fieldName, XmlConvert.ToBoolean, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public sbyte ReadSByte(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    sbyte value = SafeXmlConvert(fieldName, XmlConvert.ToSByte, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public byte ReadByte(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    byte value = SafeXmlConvert(fieldName, XmlConvert.ToByte, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public short ReadInt16(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    short value = SafeXmlConvert(fieldName, XmlConvert.ToInt16, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public ushort ReadUInt16(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    ushort value = SafeXmlConvert(fieldName, XmlConvert.ToUInt16, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public int ReadInt32(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    int value = SafeXmlConvert(fieldName, XmlConvert.ToInt32, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public uint ReadUInt32(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    uint value = SafeXmlConvert(fieldName, XmlConvert.ToUInt32, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public long ReadInt64(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    long value = SafeXmlConvert(fieldName, XmlConvert.ToInt64, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public ulong ReadUInt64(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    ulong value = SafeXmlConvert(fieldName, XmlConvert.ToUInt64, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public float ReadFloat(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    float value = SafeXmlConvert(fieldName, XmlConvert.ToSingle, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public double ReadDouble(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    double value = SafeXmlConvert(fieldName, XmlConvert.ToDouble, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public string? ReadString(string? fieldName)
        {
            if (BeginField(
                fieldName,
                true,
                out bool isNil,
                true,
                out string? whitespace))
            {
                // xs:string has whiteSpace=preserve (Part 6 5.3.1.5): do not trim,
                // and keep leading whitespace split off by a comment.
                string? value = SafeReadString();
                if (whitespace != null)
                {
                    value = whitespace + value;
                    EncodingLimits.CheckStringLength(Context.MaxStringLength, value);
                }
                EndField(fieldName);
                return value;
            }

            if (isNil)
            {
                return null;
            }

            // an element holding only whitespace keeps it (Part 6 5.3.1.5).
            if (whitespace != null && !TreatWhitespaceOnlyStringsAsEmpty)
            {
                EncodingLimits.CheckStringLength(Context.MaxStringLength, whitespace);
                return whitespace;
            }

            return string.Empty;
        }

        /// <inheritdoc/>
        public DateTimeUtc ReadDateTime(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                // check the length.
                EncodingLimits.CheckStringLength(Context.MaxStringLength, xml);

                if (!string.IsNullOrEmpty(xml))
                {
                    DateTimeUtc value = ParseDateTime(fieldName, xml!);
                    EndField(fieldName);
                    return value;
                }
            }

            return DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Parses an xs:dateTime value (Part 6 5.3.1.6). XmlConvert also takes
        /// the other XSD date and time types (time, gYear, gMonthDay...) and
        /// decodes them to a surprising instant (a time takes today's date, a
        /// gMonthDay the year 1904); those are rejected. Years beyond 9999
        /// decode as the latest and years before 0001 as the earliest
        /// date/time value.
        /// </summary>
        /// <remarks>
        /// Part 6 requires encoders to write the time and a time zone. A value
        /// without a zone is still read as UTC, and a date without a time
        /// (xs:date) as midnight UTC, because published model files (the
        /// standard type and DI designs) contain such values.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        internal static DateTimeUtc ParseDateTime(
            string? fieldName,
            string xml,
            [CallerMemberName] string? functionName = null)
        {
            // xs:dateTime collapses whitespace.
            string text = xml.Trim();

            // '-'? yyyy '-' mm '-' dd ('T' hh ':' mm ':' ss ('.' s+)?)? zone?
            int index = 0;
            bool negative = text.Length > 0 && text[0] == '-';
            if (negative)
            {
                index++;
            }

            int yearStart = index;
            while (index < text.Length && IsDigit(text[index]))
            {
                index++;
            }

            int yearDigits = index - yearStart;
            bool valid =
                (yearDigits == 4 || (yearDigits > 4 && text[yearStart] != '0')) &&
                MatchesPattern(text, index, "-dd-dd");
            index += 6;

            // the time may be left out (xs:date): published model files use
            // such values, which decode as midnight of that date.
            if (valid && index < text.Length && text[index] == 'T')
            {
                valid = MatchesPattern(text, index, "Tdd:dd:dd");
                index += 9;

                if (valid && index < text.Length && text[index] == '.')
                {
                    int fractionStart = ++index;
                    while (index < text.Length && IsDigit(text[index]))
                    {
                        index++;
                    }
                    valid = index > fractionStart;
                }
            }

            if (valid && index < text.Length && text[index] == 'Z')
            {
                index++;
            }
            else if (valid &&
                index < text.Length &&
                (text[index] is '+' or '-') &&
                MatchesPattern(text, index + 1, "dd:dd"))
            {
                index += 6;
            }

            if (!valid || index != text.Length)
            {
                throw CreateBadDecodingError(
                    fieldName,
                    new FormatException("The value is not an xs:dateTime."),
                    functionName,
                    xml);
            }

            if (negative ||
                (yearDigits == 4 && string.CompareOrdinal(text, yearStart, "0000", 0, 4) == 0))
            {
                return DateTimeUtc.MinValue;
            }

            if (yearDigits > 4)
            {
                return DateTimeUtc.MaxValue;
            }

            try
            {
                return XmlConvert.ToDateTime(text, XmlDateTimeSerializationMode.Utc);
            }
            catch (FormatException fe)
            {
                throw CreateBadDecodingError(fieldName, fe, functionName, xml);
            }

            static bool IsDigit(char c)
            {
                return c is >= '0' and <= '9';
            }

            static bool MatchesPattern(string value, int start, string pattern)
            {
                if (start + pattern.Length > value.Length)
                {
                    return false;
                }
                for (int ii = 0; ii < pattern.Length; ii++)
                {
                    char c = value[start + ii];
                    if (pattern[ii] == 'd' ? !IsDigit(c) : c != pattern[ii])
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <inheritdoc/>
        public Uuid ReadGuid(string? fieldName)
        {
            Uuid value = Uuid.Empty;

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string? guidString = ReadString("String");
                PopNamespace();

                value = ParseGuid(fieldName, guidString);

                EndField(fieldName);
            }

            return value;
        }

        /// <summary>
        /// Parses the String of an XML Guid, which has the form of Part 6 5.1.3
        /// (the "D" format: 8-4-4-4-12 hex digits, no braces or parentheses).
        /// Guid.Parse also takes the N, B, P and X formats.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static Uuid ParseGuid(
            string? fieldName,
            string? guidString,
            [CallerMemberName] string? functionName = null)
        {
            // the String element keeps layout whitespace (xs:string).
            if (!Guid.TryParseExact(
                (guidString ?? string.Empty).Trim(),
                "D",
                out Guid guid))
            {
                throw CreateBadDecodingError(
                    fieldName,
                    new FormatException("The value is not a Guid in the form of Part 6 5.1.3."),
                    functionName,
                    guidString);
            }

            return new Uuid(guid);
        }

        /// <inheritdoc/>
        public ByteString ReadByteString(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                ByteString value;
                try
                {
                    string xml = m_reader.ReadContentAsString();

                    if (!string.IsNullOrEmpty(xml))
                    {
                        // check the length before the bytes are allocated.
                        EncodingLimits.CheckBase64Length(Context.MaxByteStringLength, xml);
                        value = ByteString.From(SafeConvertFromBase64String(xml));
                    }
                    else
                    {
                        value = ByteString.Empty;
                    }
                }
                catch (XmlException xe)
                {
                    throw CreateBadDecodingError(fieldName, xe);
                }
                catch (InvalidOperationException ioe)
                {
                    throw CreateBadDecodingError(fieldName, ioe);
                }

                EndField(fieldName);
                return value;
            }

            return isNil ? default : ByteString.Empty;
        }

        /// <inheritdoc/>
        public XmlElement ReadXmlElement(string? fieldName)
        {
            if (BeginField(fieldName, true) && MoveToElement(null!))
            {
                XmlElement value = XmlElement.From(ReadXmlElementContent(fieldName, Context.MaxStringLength));
                EndField(fieldName);
                return value;
            }

            return default;
        }

        /// <inheritdoc/>
        public NodeId ReadNodeId(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string? identifierText = TrimNodeIdText(ReadString("Identifier"));
                PopNamespace();

                NodeId value;
                try
                {
                    value = NodeId.Parse(identifierText ?? string.Empty);
                }
                catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes
                    .BadNodeIdInvalid)
                {
                    throw CreateBadDecodingError(fieldName, sre, value: identifierText);
                }
                catch (ArgumentException ae)
                {
                    throw CreateBadDecodingError(fieldName, ae, value: identifierText);
                }

                EndField(fieldName);

                if (m_namespaceMappings != null &&
                    m_namespaceMappings.Length > value.NamespaceIndex)
                {
                    return value.WithNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
                }

                return value;
            }

            return NodeId.Null;
        }

        /// <inheritdoc/>
        public ExpandedNodeId ReadExpandedNodeId(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string? identifierText = TrimNodeIdText(ReadString("Identifier"));
                PopNamespace();

                ExpandedNodeId value;
                try
                {
                    value = ExpandedNodeId.Parse(identifierText ?? string.Empty);
                }
                catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes
                    .BadNodeIdInvalid)
                {
                    throw CreateBadDecodingError(fieldName, sre, value: identifierText);
                }
                catch (ArgumentException ae)
                {
                    throw CreateBadDecodingError(fieldName, ae, value: identifierText);
                }

                EndField(fieldName);

                // Part 6 5.2.2.10: an ExpandedNodeId with a NamespaceUri has no
                // NamespaceIndex to map, and WithNamespaceIndex would drop the uri.
                if (m_namespaceMappings != null &&
                    string.IsNullOrEmpty(value.NamespaceUri) &&
                    m_namespaceMappings.Length > value.NamespaceIndex &&
                    !value.IsNull)
                {
                    value = value.WithNamespaceIndex(m_namespaceMappings[value.NamespaceIndex]);
                }

                if (m_serverMappings != null &&
                    m_serverMappings.Length > value.ServerIndex &&
                    !value.IsNull)
                {
                    value = value.WithServerIndex(m_serverMappings[value.ServerIndex]);
                }

                return value;
            }

            return ExpandedNodeId.Null;
        }

        /// <inheritdoc/>
        public StatusCode ReadStatusCode(string? fieldName)
        {
            StatusCode value;

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                value = ReadUInt32("Code");
                PopNamespace();

                EndField(fieldName);
            }
            else
            {
                value = StatusCodes.Good;
            }
            return value;
        }

        /// <inheritdoc/>
        public DiagnosticInfo? ReadDiagnosticInfo(string? fieldName)
        {
            DiagnosticInfo? value = null;

            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                value = ReadDiagnosticInfo(0);
                PopNamespace();

                EndField(fieldName);
                return value;
            }

            return value;
        }

        /// <inheritdoc/>
        public QualifiedName ReadQualifiedName(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);

                ushort namespaceIndex = 0;

                if (BeginField("NamespaceIndex", true))
                {
                    namespaceIndex = ReadUInt16(null);
                    EndField("NamespaceIndex");
                }

                string? name = ReadString("Name");

                PopNamespace();
                EndField(fieldName);

                if (m_namespaceMappings != null && m_namespaceMappings.Length > namespaceIndex)
                {
                    namespaceIndex = m_namespaceMappings[namespaceIndex];
                }

                return new QualifiedName(name ?? string.Empty, namespaceIndex);
            }

            return default;
        }

        /// <inheritdoc/>
        public LocalizedText ReadLocalizedText(string? fieldName)
        {
            if (BeginField(fieldName, true))
            {
                PushNamespace(Namespaces.OpcUaXsd);
                string? locale = ReadString("Locale");
                string? text = ReadString("Text");

                var value = new LocalizedText(locale ?? string.Empty, text ?? string.Empty);

                PopNamespace();

                EndField(fieldName);
                return value;
            }

            return LocalizedText.Null;
        }

        /// <inheritdoc/>
        public Variant ReadVariant(string? fieldName)
        {
            CheckAndIncrementNestingLevel();

            try
            {
                Variant value = Variant.Null;

                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    if (BeginField("Value", true))
                    {
                        try
                        {
                            value = ReadVariantValue();
                        }
                        catch (Exception ex) when (ex is not ServiceResultException and not OutOfMemoryException)
                        {
                            // a malformed value fails the decode: returning a
                            // BadDecodingError value left the reader at an
                            // arbitrary position inside the Value element.
                            m_logger.ErrorReadingVariant(ex);
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                ex,
                                "Error reading variant value: {0}",
                                ex.Message);
                        }
                        EndField("Value");
                    }

                    PopNamespace();

                    EndField(fieldName);
                }

                return value;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <inheritdoc/>
        public DataValue ReadDataValue(string? fieldName)
        {
            if (!BeginField(fieldName, true))
            {
                // Field is absent — return the null sentinel so callers can
                // distinguish "missing" from "present but empty".
                return DataValue.Null;
            }

            PushNamespace(Namespaces.OpcUaXsd);

            Variant variant = ReadVariant("Value");
            StatusCode statusCode = ReadStatusCode("StatusCode");
            DateTimeUtc sourceTimestamp = ReadDateTime("SourceTimestamp");
            ushort sourcePicoseconds = ReadUInt16("SourcePicoseconds");
            DateTimeUtc serverTimestamp = ReadDateTime("ServerTimestamp");
            ushort serverPicoseconds = ReadUInt16("ServerPicoseconds");

            var value = new DataValue(
                variant,
                statusCode,
                sourceTimestamp,
                serverTimestamp,
                sourcePicoseconds,
                serverPicoseconds);

            PopNamespace();

            EndField(fieldName);
            return value;
        }

        /// <inheritdoc/>
        public ExtensionObject ReadExtensionObject(string? fieldName)
        {
            if (!BeginField(fieldName, true))
            {
                return ExtensionObject.Null;
            }

            PushNamespace(Namespaces.OpcUaXsd);

            // read local type id.
            NodeId typeId = ReadNodeId("TypeId");

            // convert to absolute type id.
            var absoluteId = NodeId.ToExpandedNodeId(typeId, Context.NamespaceUris);

            if (!typeId.IsNull && absoluteId.IsNull)
            {
                m_logger.CannotDeserializeExtensionObject(typeId);
            }

            // read body.
            if (!BeginField("Body", true))
            {
                // read end of extension object. The field element is in the
                // namespace of the enclosing structure, not in Types.xsd, so
                // the namespace pushed for TypeId/Body is popped first.
                PopNamespace();
                EndField(fieldName);

                return new ExtensionObject(absoluteId);
            }

            // read the body.
            ExtensionObject result = ReadExtensionObjectBody(absoluteId);

            // read end of body.
            EndField("Body");
            PopNamespace();

            // read end of extension object.
            EndField(fieldName);

            return result;
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string? fieldName) where T : IEncodeable, new()
        {
            return ReadEncodeable(fieldName, new T());
        }

        /// <inheritdoc/>
        public T ReadEncodeable<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
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

            if (activator.CreateInstance() is not T value)
            {
                // The type id comes from the wire and need not name a T at all.
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Type '{0}' is not a {1}.",
                    encodeableTypeId,
                    typeof(T).Name);
            }
            return ReadEncodeable(fieldName, value);
        }

        /// <inheritdoc/>
        public T ReadEncodeableAsExtensionObject<T>(string? fieldName)
            where T : IEncodeable
        {
            ExtensionObject extensionObject = ReadExtensionObject(fieldName);
#pragma warning disable CS8600 // out T may be null when false is returned
            if (extensionObject.TryGetValue(out T value))
            {
                return value!;
            }
#pragma warning restore CS8600
            return default!;
        }

        /// <inheritdoc/>
        public T ReadEnumerated<T>(string? fieldName) where T : struct, Enum
        {
            T value = default;

            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    int index = xml!.LastIndexOf('_');

                    try
                    {
                        if (index != -1)
                        {
                            int numericValue = Convert.ToInt32(
                                xml![(index + 1)..],
                                CultureInfo.InvariantCulture);
                            value = EnumHelper.Int32ToEnum<T>(numericValue);
                        }
                        else
                        {
#if NET8_0_OR_GREATER
                            value = Enum.Parse<T>(xml!, false);
#else
                            value = (T)Enum.Parse(typeof(T), xml!, false);
#endif
                        }
                    }
                    catch (Exception ex) when (ex is
                        ArgumentException or
                        FormatException or
                        OverflowException)
                    {
                        throw CreateBadDecodingError(fieldName, ex, value: xml);
                    }
                }

                EndField(fieldName);
            }

            return value;
        }

        /// <inheritdoc/>
        public EnumValue ReadEnumerated(string? fieldName)
        {
            EnumValue value = default;

            if (BeginField(fieldName, true))
            {
                string? xml = SafeReadString();

                if (!string.IsNullOrEmpty(xml))
                {
                    int index = xml!.LastIndexOf('_');

                    try
                    {
                        if (index != -1)
                        {
                            int numericValue = Convert.ToInt32(
                                xml![(index + 1)..],
                                CultureInfo.InvariantCulture);
                            value = new EnumValue(numericValue, xml[..index]);
                        }
                        else if (int.TryParse(xml, out int numeric))
                        {
                            value = (EnumValue)numeric;
                        }
                        else
                        {
                            value = new EnumValue(0, xml);
                        }
                    }
                    catch (Exception ex) when (ex is
                        ArgumentException or
                        FormatException or
                        OverflowException)
                    {
                        throw CreateBadDecodingError(fieldName, ex, value: xml);
                    }
                }

                EndField(fieldName);
            }

            return value;
        }

        /// <inheritdoc/>
        public ArrayOf<bool> ReadBooleanArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<bool>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Boolean"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadBoolean("Boolean"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<sbyte> ReadSByteArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<sbyte>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("SByte"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadSByte("SByte"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<byte> ReadByteArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<byte>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Byte"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadByte("Byte"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<short> ReadInt16Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<short>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Int16"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadInt16("Int16"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ushort> ReadUInt16Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ushort>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("UInt16"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadUInt16("UInt16"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<int> ReadInt32Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<int>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Int32"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadInt32("Int32"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<uint> ReadUInt32Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<uint>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("UInt32"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadUInt32("UInt32"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<long> ReadInt64Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<long>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Int64"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadInt64("Int64"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ulong> ReadUInt64Array(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ulong>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("UInt64"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadUInt64("UInt64"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<float> ReadFloatArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<float>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Float"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadFloat("Float"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<double> ReadDoubleArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<double>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Double"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadDouble("Double"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<string?> ReadStringArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<string?>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("String"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadString("String"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<DateTimeUtc> ReadDateTimeArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<DateTimeUtc>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("DateTime"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadDateTime("DateTime"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<Uuid> ReadGuidArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<Uuid>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Guid"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadGuid("Guid"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ByteString> ReadByteStringArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ByteString>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ByteString"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadByteString("ByteString"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<XmlElement> ReadXmlElementArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<XmlElement>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("XmlElement"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadXmlElement("XmlElement"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> ReadNodeIdArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<NodeId>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("NodeId"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadNodeId("NodeId"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ExpandedNodeId> ReadExpandedNodeIdArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ExpandedNodeId>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ExpandedNodeId"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadExpandedNodeId("ExpandedNodeId"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<StatusCode> ReadStatusCodeArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<StatusCode>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("StatusCode"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadStatusCode("StatusCode"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<DiagnosticInfo?> ReadDiagnosticInfoArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<DiagnosticInfo?>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("DiagnosticInfo"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadDiagnosticInfo("DiagnosticInfo"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ReadQualifiedNameArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<QualifiedName>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("QualifiedName"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadQualifiedName("QualifiedName"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<LocalizedText> ReadLocalizedTextArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<LocalizedText>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("LocalizedText"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadLocalizedText("LocalizedText"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<Variant> ReadVariantArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<Variant>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("Variant"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadVariant("Variant"));
                }

                PopNamespace();

                EndField(fieldName);
                return values;
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<DataValue> ReadDataValueArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<DataValue>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("DataValue"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadDataValue("DataValue"));
                }

                PopNamespace();

                EndField(fieldName);
                return values.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<ExtensionObject> ReadExtensionObjectArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<ExtensionObject>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ExtensionObject"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadExtensionObject("ExtensionObject"));
                }

                PopNamespace();

                EndField(fieldName);
                return values.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArrayAsExtensionObjects<T>(string? fieldName)
            where T : IEncodeable
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var values = new List<T>();
                PushNamespace(Namespaces.OpcUaXsd);

                while (MoveToElement("ExtensionObject"))
                {
                    CheckArrayLength(values.Count);
                    values.Add(ReadEncodeableAsExtensionObject<T>("ExtensionObject"));
                }

                PopNamespace();

                EndField(fieldName);
                return values.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var encodeables = new List<T>();
                XmlQualifiedName? xmlName = TypeInfo.GetXmlName(typeof(T));
                PushNamespace(xmlName!.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    CheckArrayLength(encodeables.Count);
                    encodeables.Add(ReadEncodeable<T>(xmlName.Name));
                }

                PopNamespace();

                EndField(fieldName);
                return encodeables.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEncodeableArray<T>(string? fieldName,
            ExpandedNodeId encodeableTypeId) where T : IEncodeable
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var encodeables = new List<T>();
                XmlQualifiedName? xmlName = Context.Factory.TryGetEncodeableType(
                    encodeableTypeId, out IEncodeableType? encodeableType)
                    ? encodeableType.XmlName
                    : TypeInfo.GetXmlName(typeof(T));
                PushNamespace(xmlName!.Namespace == Namespaces.OpcUa
                    ? Namespaces.OpcUaXsd
                    : xmlName.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    CheckArrayLength(encodeables.Count);
                    encodeables.Add(ReadEncodeable<T>(xmlName.Name, encodeableTypeId));
                }

                PopNamespace();
                EndField(fieldName);
                return encodeables.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public MatrixOf<T> ReadEncodeableMatrix<T>(string? fieldName, ExpandedNodeId encodeableTypeId)
            where T : IEncodeable
        {
            CheckAndIncrementNestingLevel();
            MatrixOf<T> value = default;
            try
            {
                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    int[] dimensions = ReadInt32Array("Dimensions").ToArray() ?? [];
                    if (BeginField("Elements", true))
                    {
                        value = ToMatrixOrThrow(ReadEncodeableArray<T>(null, encodeableTypeId), dimensions);
                        EndField("Elements");
                    }
                    else if (dimensions.Length > 0)
                    {
                        // An empty matrix has no Elements content; keep it
                        // empty (not null) when the dimensions allow it.
                        value = ToMatrixOrThrow(ArrayOf.Empty<T>(), dimensions);
                    }

                    PopNamespace();

                    EndField(fieldName);
                }
            }
            finally
            {
                m_nestingLevel--;
            }
            return value;
        }

        /// <inheritdoc/>
        public MatrixOf<T> ReadEncodeableMatrix<T>(string? fieldName)
            where T : IEncodeable, new()
        {
            CheckAndIncrementNestingLevel();
            MatrixOf<T> value = default;
            try
            {
                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    int[] dimensions = ReadInt32Array("Dimensions").ToArray() ?? [];
                    if (BeginField("Elements", true))
                    {
                        value = ToMatrixOrThrow(ReadEncodeableArray<T>(null), dimensions);
                        EndField("Elements");
                    }
                    else if (dimensions.Length > 0)
                    {
                        // An empty matrix has no Elements content; keep it
                        // empty (not null) when the dimensions allow it.
                        value = ToMatrixOrThrow(ArrayOf.Empty<T>(), dimensions);
                    }

                    PopNamespace();

                    EndField(fieldName);
                }
            }
            finally
            {
                m_nestingLevel--;
            }
            return value;
        }

        /// <summary>
        /// Builds a matrix from decoded elements and wire supplied dimensions.
        /// MatrixOf throws ArgumentException for invalid dimensions (negative,
        /// zero rank, overflowing product, or a length mismatch), which must be
        /// reported through the decoder rejection channel instead of escaping.
        /// </summary>
        /// <typeparam name="T">The element type of the matrix.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private MatrixOf<T> ToMatrixOrThrow<T>(ArrayOf<T> elements, int[] dimensions)
        {
            // The inline matrix of a structure field has at least two
            // dimensions (OPC 10000-6 5.2.5 Table 28, 5.3.4); a dimension
            // <= 0 means no values, like in the binary encoding. Earlier
            // versions wrote an empty matrix with the single dimension 0.
            dimensions = MatrixOf.NormalizeLegacyEmptyInlineMatrixDimensions(
                dimensions,
                elements.Count);
            MatrixOf.NormalizeInlineMatrixDimensions(dimensions);
            if (!MatrixOf.IsValidInlineMatrix(dimensions, elements.Count, Context.MaxArrayLength))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Encodeable matrix Dimensions [{0}] are inconsistent with {1} element(s).",
                    string.Join(",", dimensions),
                    elements.Count);
            }
            try
            {
                return elements.ToMatrix(dimensions);
            }
            catch (ArgumentException ex)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    ex,
                    "Encodeable matrix Dimensions [{0}] are inconsistent with {1} element(s).",
                    string.Join(",", dimensions),
                    elements.Count);
            }
        }

        /// <inheritdoc/>
        public ArrayOf<T> ReadEnumeratedArray<T>(string? fieldName) where T : struct, Enum
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var enums = new List<T>();
                XmlQualifiedName? xmlName = TypeInfo.GetXmlName(typeof(T));
                PushNamespace(xmlName!.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    CheckArrayLength(enums.Count);
                    enums.Add(ReadEnumerated<T>(xmlName.Name));
                }

                PopNamespace();
                EndField(fieldName);
                return enums.ToArrayOf();
            }

            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public ArrayOf<EnumValue> ReadEnumeratedArray(string? fieldName)
        {
            if (BeginField(fieldName, true, out bool isNil))
            {
                var enums = new List<EnumValue>();

                XmlQualifiedName xmlName = Peek(XmlNodeType.Element) ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unable to read field {0} in function {1}: The enumerated array does not contain any elements.",
                        fieldName ?? string.Empty,
                        nameof(ReadEnumeratedArray));
                PushNamespace(xmlName.Namespace);

                while (MoveToElement(xmlName.Name))
                {
                    CheckArrayLength(enums.Count);
                    enums.Add(ReadEnumerated(xmlName.Name));
                }

                PopNamespace();
                EndField(fieldName);
                return enums.ToArrayOf();
            }
            return isNil ? default : [];
        }

        /// <inheritdoc/>
        public uint ReadSwitchField(IList<string> switches, out string? fieldName)
        {
            fieldName = null;
            return ReadUInt32("SwitchField");
        }

        /// <inheritdoc/>
        public uint ReadEncodingMask(IList<string> masks)
        {
            return ReadUInt32("EncodingMask");
        }

        /// <inheritdoc/>
        public bool HasField(string fieldName)
        {
            return Peek(fieldName);
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue(string? fieldName, TypeInfo typeInfo)
        {
            typeInfo = XmlDecoder.AsVariantIfAbstractNumber(typeInfo);
            CheckAndIncrementNestingLevel();

            try
            {
                Variant value = Variant.Null;

                if (fieldName != null && IsTypedFieldValue(typeInfo))
                {
                    // OPC 10000-6 5.3.1, 5.3.4, 5.3.5: the field is written
                    // like the typed field of its type. The Variant body
                    // earlier versions wrapped it in is still accepted.
                    // A missing field is null, a nil array field a null array.
                    bool isPresent = HasField(fieldName);
                    if (!BeginField(fieldName, true, out bool isNil))
                    {
                        if (!isPresent)
                        {
                            return Variant.Null;
                        }
                        return isNil ? CreateNullFieldValue(typeInfo) : CreateEmptyFieldValue(typeInfo);
                    }
                    m_reader.MoveToContent();
                    if (m_reader.NodeType != XmlNodeType.Element ||
                        !IsVariantBodyElement(m_reader.LocalName, m_reader.NamespaceURI, typeInfo))
                    {
                        value = ReadTypedFieldValue(this, typeInfo);
                        EndField(fieldName);
                        return value;
                    }
                    PushNamespace(Namespaces.OpcUaXsd);
                    value = ReadVariantValue(true, typeInfo.BuiltInType);
                    CheckFieldValueType(value, typeInfo);
                    PopNamespace();
                    EndField(fieldName);
                    return value;
                }

                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    value = ReadVariantValue(true, typeInfo.BuiltInType);

                    // Allow reading with unknown type info
                    if (!typeInfo.IsUnknown && !value.IsNull)
                    {
                        if (typeInfo.BuiltInType == BuiltInType.Enumeration)
                        {
                            typeInfo = typeInfo.WithBuiltInType(BuiltInType.Int32);
                        }

                        if (value.TypeInfo != typeInfo &&
                            !IsInlineMatrixOf(value, typeInfo))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Error reading value as variant. Type mismatch: Expected {0} != Actual {1}",
                                typeInfo, value.TypeInfo);
                        }
                    }

                    PopNamespace();

                    EndField(fieldName);
                }

                return value;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Whether a decoded inline matrix matches the matrix type info of a
        /// structure field. The rank of the decoded value follows its
        /// dimensions, which for an empty matrix can be a single zero (or the
        /// 0 x 0 shape an empty matrix of any declared rank is written with)
        /// and which a null matrix does not have. A populated matrix must
        /// have the declared rank.
        /// </summary>
        internal static bool IsInlineMatrixOf(in Variant value, TypeInfo typeInfo)
        {
            if (!typeInfo.IsMatrix ||
                value.TypeInfo.BuiltInType != typeInfo.BuiltInType ||
                !value.IsInlineMatrix(out bool isNull))
            {
                return false;
            }
            return isNull ||
                value.TypeInfo.ValueRank == typeInfo.ValueRank ||
                value.Raw is IMatrixOf { Count: 0 };
        }

        /// <summary>
        /// OPC 10000-6 5.1.6: a Number, Integer or UInteger structure field is
        /// encoded as a Variant, so an array or matrix field of such a type is
        /// an array or matrix of Variant, which is what the encoder writes.
        /// </summary>
        internal static TypeInfo AsVariantIfAbstractNumber(TypeInfo typeInfo)
        {
            if (typeInfo.BuiltInType is BuiltInType.Number or
                    BuiltInType.Integer or
                    BuiltInType.UInteger &&
                !typeInfo.IsScalar)
            {
                return typeInfo.WithBuiltInType(BuiltInType.Variant);
            }
            return typeInfo;
        }

        /// <summary>
        /// Whether a structure field of the type is encoded with the typed
        /// field encoding of a built-in scalar or one dimensional array
        /// (OPC 10000-6 5.3.1, 5.3.4, 5.3.5), which the XmlEncoder writes for
        /// a named <see cref="IEncoder.WriteVariantValue(string?, in Variant)"/>.
        /// </summary>
        internal static bool IsTypedFieldValue(TypeInfo typeInfo)
        {
            if (typeInfo.IsUnknown)
            {
                return false;
            }
            if (typeInfo.ValueRank == ValueRanks.Scalar)
            {
                return typeInfo.BuiltInType is
                    (>= BuiltInType.Boolean and <= BuiltInType.DataValue) or
                    BuiltInType.Enumeration;
            }
            return typeInfo.ValueRank == ValueRanks.OneDimension &&
                typeInfo.BuiltInType is
                    (>= BuiltInType.Boolean and <= BuiltInType.Variant) or
                    BuiltInType.Enumeration;
        }

        /// <summary>
        /// Whether the element in a field is the Variant body earlier versions
        /// wrapped a structure field value in (e.g. <c>&lt;A&gt;&lt;Int32&gt;</c>,
        /// <c>&lt;A&gt;&lt;ListOfInt32&gt;</c>) rather than the content of the
        /// typed field.
        /// </summary>
        internal static bool IsVariantBodyElement(string localName, string namespaceUri, TypeInfo typeInfo)
        {
            if (namespaceUri != Namespaces.OpcUaXsd)
            {
                return false;
            }
            // The elements of an array are named by their type, a Variant
            // body of an array is a ListOf (or a Null) element.
            if (typeInfo.ValueRank != ValueRanks.Scalar)
            {
                return localName == "Null" ||
                    localName.StartsWith("ListOf", StringComparison.Ordinal);
            }
            // A typed scalar field contains text or the fields of its type,
            // which are not named like a built-in type - except the String
            // of a Guid, the StatusCode of a DataValue and the element of an
            // XmlElement. A Variant body of any other type is read as such,
            // so that a type mismatch is reported.
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.XmlElement:
                    return localName == nameof(BuiltInType.XmlElement);
                case BuiltInType.Guid when localName == nameof(BuiltInType.String):
                case BuiltInType.DataValue when localName == nameof(BuiltInType.StatusCode):
                    return false;
            }
            return localName is "Null" or "Matrix" ||
                (Enum.TryParse(localName, out BuiltInType builtInType) &&
                builtInType is >= BuiltInType.Boolean and <= BuiltInType.DiagnosticInfo &&
                localName == builtInType.ToString());
        }

        /// <summary>
        /// The value of a nil or missing typed field: a null array of the
        /// field type (what the typed array readers return), null otherwise.
        /// </summary>
        internal static Variant CreateNullFieldValue(TypeInfo typeInfo)
        {
            return typeInfo.ValueRank == ValueRanks.OneDimension
                ? Variant.CreateDefault(typeInfo)
                : Variant.Null;
        }

        /// <summary>
        /// The value of a present but empty typed field: an empty array or an
        /// empty string (what the typed readers return), null otherwise.
        /// </summary>
        internal static Variant CreateEmptyFieldValue(TypeInfo typeInfo)
        {
            if (typeInfo.ValueRank == ValueRanks.OneDimension)
            {
                return Variant.CreateDefault(typeInfo).ToEmptyArray();
            }
            return typeInfo.BuiltInType == BuiltInType.String
                ? Variant.From(string.Empty)
                : Variant.Null;
        }

        /// <summary>
        /// Checks that a value read from a Variant body has the field type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void CheckFieldValueType(in Variant value, TypeInfo typeInfo)
        {
            if (typeInfo.IsUnknown || value.IsNull)
            {
                return;
            }
            if (typeInfo.BuiltInType == BuiltInType.Enumeration)
            {
                typeInfo = typeInfo.WithBuiltInType(BuiltInType.Int32);
            }
            if (value.TypeInfo != typeInfo && !IsInlineMatrixOf(value, typeInfo))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Error reading value as variant. Type mismatch: Expected {0} != Actual {1}",
                    typeInfo, value.TypeInfo);
            }
        }

        /// <summary>
        /// Reads the content of a typed field (the field element is already
        /// entered) with the typed reader of its type.
        /// </summary>
        internal static Variant ReadTypedFieldValue(IDecoder decoder, TypeInfo typeInfo)
        {
            if (typeInfo.ValueRank == ValueRanks.Scalar)
            {
                return typeInfo.BuiltInType switch
                {
                    BuiltInType.Boolean => decoder.ReadBoolean(null),
                    BuiltInType.SByte => decoder.ReadSByte(null),
                    BuiltInType.Byte => decoder.ReadByte(null),
                    BuiltInType.Int16 => decoder.ReadInt16(null),
                    BuiltInType.UInt16 => decoder.ReadUInt16(null),
                    BuiltInType.Int32 => decoder.ReadInt32(null),
                    BuiltInType.UInt32 => decoder.ReadUInt32(null),
                    BuiltInType.Int64 => decoder.ReadInt64(null),
                    BuiltInType.UInt64 => decoder.ReadUInt64(null),
                    BuiltInType.Float => decoder.ReadFloat(null),
                    BuiltInType.Double => decoder.ReadDouble(null),
                    BuiltInType.String => Variant.From(decoder.ReadString(null) ?? string.Empty),
                    BuiltInType.DateTime => decoder.ReadDateTime(null),
                    BuiltInType.Guid => decoder.ReadGuid(null),
                    BuiltInType.ByteString => decoder.ReadByteString(null),
                    BuiltInType.XmlElement => decoder.ReadXmlElement(null),
                    BuiltInType.NodeId => decoder.ReadNodeId(null),
                    BuiltInType.ExpandedNodeId => decoder.ReadExpandedNodeId(null),
                    BuiltInType.StatusCode => decoder.ReadStatusCode(null),
                    BuiltInType.QualifiedName => decoder.ReadQualifiedName(null),
                    BuiltInType.LocalizedText => decoder.ReadLocalizedText(null),
                    BuiltInType.ExtensionObject => decoder.ReadExtensionObject(null),
                    BuiltInType.DataValue => decoder.ReadDataValue(null),
                    BuiltInType.Enumeration => decoder.ReadEnumerated(null),
                    _ => Variant.Null
                };
            }
            return typeInfo.BuiltInType switch
            {
                BuiltInType.Boolean => Variant.From(decoder.ReadBooleanArray(null)),
                BuiltInType.SByte => Variant.From(decoder.ReadSByteArray(null)),
                BuiltInType.Byte => Variant.From(decoder.ReadByteArray(null)),
                BuiltInType.Int16 => Variant.From(decoder.ReadInt16Array(null)),
                BuiltInType.UInt16 => Variant.From(decoder.ReadUInt16Array(null)),
                BuiltInType.Int32 => Variant.From(decoder.ReadInt32Array(null)),
                BuiltInType.UInt32 => Variant.From(decoder.ReadUInt32Array(null)),
                BuiltInType.Int64 => Variant.From(decoder.ReadInt64Array(null)),
                BuiltInType.UInt64 => Variant.From(decoder.ReadUInt64Array(null)),
                BuiltInType.Float => Variant.From(decoder.ReadFloatArray(null)),
                BuiltInType.Double => Variant.From(decoder.ReadDoubleArray(null)),
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                BuiltInType.String => Variant.From(decoder.ReadStringArray(null)),
#pragma warning restore CS8620
                BuiltInType.DateTime => Variant.From(decoder.ReadDateTimeArray(null)),
                BuiltInType.Guid => Variant.From(decoder.ReadGuidArray(null)),
                BuiltInType.ByteString => Variant.From(decoder.ReadByteStringArray(null)),
                BuiltInType.XmlElement => Variant.From(decoder.ReadXmlElementArray(null)),
                BuiltInType.NodeId => Variant.From(decoder.ReadNodeIdArray(null)),
                BuiltInType.ExpandedNodeId => Variant.From(decoder.ReadExpandedNodeIdArray(null)),
                BuiltInType.StatusCode => Variant.From(decoder.ReadStatusCodeArray(null)),
                BuiltInType.QualifiedName => Variant.From(decoder.ReadQualifiedNameArray(null)),
                BuiltInType.LocalizedText => Variant.From(decoder.ReadLocalizedTextArray(null)),
                BuiltInType.ExtensionObject => Variant.From(decoder.ReadExtensionObjectArray(null)),
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                BuiltInType.DataValue => Variant.From(decoder.ReadDataValueArray(null)),
#pragma warning restore CS8620
                BuiltInType.Variant => Variant.From(decoder.ReadVariantArray(null)),
                BuiltInType.Enumeration => Variant.From(decoder.ReadEnumeratedArray(null)),
                _ => Variant.Null
            };
        }

        /// <inheritdoc/>
        public Variant ReadVariantValue()
        {
            return ReadVariantValue(false, BuiltInType.Null);
        }

        /// <summary>
        /// Reads the content of a Variant. A raw value is the value of a
        /// structure field whose inline matrix may be empty.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Variant ReadVariantValue(bool readRawValue, BuiltInType rawBuiltInType)
        {
            // skip whitespace.
            while (m_reader.NodeType != XmlNodeType.Element)
            {
                if (!m_reader.Read())
                {
                    // Read() returns false at the end of the document, leaving
                    // NodeType at None - without this the loop never ends.
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Error reading variant value. No element found.");
                }
            }

            try
            {
                m_namespaces.Push(Namespaces.OpcUaXsd);

                string typeName = m_reader.LocalName;

                if (!typeName.StartsWith("ListOf", StringComparison.Ordinal))
                {
                    // process scalar types.
                    switch (typeName)
                    {
                        case "Null":
                            if (BeginField(typeName, true))
                            {
                                EndField(typeName);
                            }
                            return Variant.Null;
                        case "Boolean":
                            return ReadBoolean(typeName);
                        case "SByte":
                            return ReadSByte(typeName);
                        case "Byte":
                            return ReadByte(typeName);
                        case "Int16":
                            return ReadInt16(typeName);
                        case "UInt16":
                            return ReadUInt16(typeName);
                        case "Int32":
                            return ReadInt32(typeName);
                        case "UInt32":
                            return ReadUInt32(typeName);
                        case "Int64":
                            return ReadInt64(typeName);
                        case "UInt64":
                            return ReadUInt64(typeName);
                        case "Float":
                            return ReadFloat(typeName);
                        case "Double":
                            return ReadDouble(typeName);
                        case "String":
                            // a nil element is a null String Variant (5.3.1.17)
                            return new Variant(ReadString(typeName)!);
                        case "DateTime":
                            return ReadDateTime(typeName);
                        case "Guid":
                            return ReadGuid(typeName);
                        case "ByteString":
                            return ReadByteString(typeName);
                        case "XmlElement":
                            return ReadXmlElement(typeName);
                        case "NodeId":
                            return ReadNodeId(typeName);
                        case "ExpandedNodeId":
                            return ReadExpandedNodeId(typeName);
                        case "StatusCode":
                            return ReadStatusCode(typeName);
                        case "QualifiedName":
                            return ReadQualifiedName(typeName);
                        case "LocalizedText":
                            return ReadLocalizedText(typeName);
                        case "ExtensionObject":
                            return ReadExtensionObject(typeName);
                        case "DataValue":
                            return ReadDataValue(typeName);
                        case "Matrix":
                            // Earlier versions wrapped the inline matrix of a
                            // structure field in a Matrix element.
                            return ReadMatrix(typeName, readRawValue, rawBuiltInType);
                        case "Dimensions" when readRawValue:
                            // A matrix structure field is of the Matrix type
                            // itself: the field element directly contains
                            // Dimensions and Elements (OPC 10000-6 5.3.4).
                            return ReadMatrix(null, readRawValue, rawBuiltInType);
                        default:
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Element '{1}:{0}' is not allowed in a Variant.",
                                m_reader.LocalName,
                                m_reader.NamespaceURI);
                    }
                }
                else
                {
                    // process array types.
                    switch (typeName["ListOf".Length..])
                    {
                        case "Boolean":
                            return Variant.From(ReadBooleanArray(typeName));
                        case "SByte":
                            return Variant.From(ReadSByteArray(typeName));
                        case "Byte":
                            return Variant.From(ReadByteArray(typeName));
                        case "Int16":
                            return Variant.From(ReadInt16Array(typeName));
                        case "UInt16":
                            return Variant.From(ReadUInt16Array(typeName));
                        case "Int32":
                            return Variant.From(ReadInt32Array(typeName));
                        case "UInt32":
                            return Variant.From(ReadUInt32Array(typeName));
                        case "Int64":
                            return Variant.From(ReadInt64Array(typeName));
                        case "UInt64":
                            return Variant.From(ReadUInt64Array(typeName));
                        case "Float":
                            return Variant.From(ReadFloatArray(typeName));
                        case "Double":
                            return Variant.From(ReadDoubleArray(typeName));
                        case "String":
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                            return Variant.From(ReadStringArray(typeName));
#pragma warning restore CS8620
                        case "DateTime":
                            return Variant.From(ReadDateTimeArray(typeName));
                        case "Guid":
                            return Variant.From(ReadGuidArray(typeName));
                        case "ByteString":
                            return Variant.From(ReadByteStringArray(typeName));
                        case "XmlElement":
                            return Variant.From(ReadXmlElementArray(typeName));
                        case "NodeId":
                            return Variant.From(ReadNodeIdArray(typeName));
                        case "ExpandedNodeId":
                            return Variant.From(ReadExpandedNodeIdArray(typeName));
                        case "StatusCode":
                            return Variant.From(ReadStatusCodeArray(typeName));
                        case "QualifiedName":
                            return Variant.From(ReadQualifiedNameArray(typeName));
                        case "LocalizedText":
                            return Variant.From(ReadLocalizedTextArray(typeName));
                        case "ExtensionObject":
                            return Variant.From(ReadExtensionObjectArray(typeName));
                        case "DataValue":
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                            return Variant.From(ReadDataValueArray(typeName));
#pragma warning restore CS8620
                        case "Variant":
                            return Variant.From(ReadVariantArray(typeName));
                        default:
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Element '{1}:{0}' is not allowed in a Variant.",
                                m_reader.LocalName,
                                m_reader.NamespaceURI);
                    }
                }
            }
            finally
            {
                m_namespaces.Pop();
            }
        }

        /// <summary>
        /// Reads the body extension object from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ExtensionObject ReadExtensionObjectBody(ExpandedNodeId typeId)
        {
            m_reader.MoveToContent();

            // check for binary encoded body.
            if (m_reader.LocalName == "ByteString" && m_reader.NamespaceURI == Namespaces.OpcUaXsd)
            {
                PushNamespace(Namespaces.OpcUaXsd);
                ByteString bytes = ReadByteString("ByteString");
                PopNamespace();

                return new ExtensionObject(typeId, bytes);
            }

            // lookup type.
            if (Context.Factory.TryGetEncodeableType(typeId, out _))
            {
                // decode known type.
                PushNamespace(m_reader.NamespaceURI);
                IEncodeable encodeable = ReadEncodeable<IEncodeable>(m_reader.LocalName, typeId);
                PopNamespace();
                return new ExtensionObject(typeId, encodeable);
            }

            if (typeId.IsNull)
            {
                var xmlName = new XmlQualifiedName(
                    m_reader.LocalName,
                    m_reader.NamespaceURI);
                if (Context.Factory.TryGetType(xmlName, out IType? type) &&
                    type is IEncodeableType encodeableType)
                {
                    IEncodeable encodeable;
                    PushNamespace(m_reader.NamespaceURI);
                    try
                    {
                        encodeable = ReadEncodeable(
                            m_reader.LocalName,
                            encodeableType.CreateInstance());
                    }
                    finally
                    {
                        PopNamespace();
                    }
                    return new ExtensionObject(
                        GetXmlEncodingIdOrTypeId(encodeable),
                        encodeable);
                }
            }

            // an extension object body is structured XML, bounded by the message
            // and the depth limit, not by MaxStringLength like an XmlElement value.
            var xmlElement = XmlElement.From(ReadXmlElementContent(null, 0));
            if (!xmlElement.IsValid)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Invalid xml in extension object body: {0}",
                    xmlElement);
            }
            return new ExtensionObject(typeId, xmlElement);
        }

        private static ExpandedNodeId GetXmlEncodingIdOrTypeId(IEncodeable encodeable)
        {
            try
            {
                ExpandedNodeId xmlEncodingId = encodeable.XmlEncodingId;
                if (!xmlEncodingId.IsNull)
                {
                    return xmlEncodingId;
                }
            }
            catch (NotSupportedException)
            {
                return encodeable.TypeId;
            }
            return encodeable.TypeId;
        }

        /// <summary>
        /// Reads an DiagnosticInfo from the stream.
        /// Limits the InnerDiagnosticInfo nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private DiagnosticInfo? ReadDiagnosticInfo(int depth)
        {
            if (depth > DiagnosticInfo.MaxInnerDepth)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of InnerDiagnosticInfo was exceeded");
            }

            CheckAndIncrementNestingLevel();

            try
            {
                var value = new DiagnosticInfo();
                bool hasDiagnosticInfo = false;

                if (BeginField("SymbolicId", true))
                {
                    value.SymbolicId = ReadInt32(null);
                    EndField("SymbolicId");
                    hasDiagnosticInfo = true;
                }

                if (BeginField("NamespaceUri", true))
                {
                    value.NamespaceUri = ReadInt32(null);
                    EndField("NamespaceUri");
                    hasDiagnosticInfo = true;
                }

                if (BeginField("Locale", true))
                {
                    value.Locale = ReadInt32(null);
                    EndField("Locale");
                    hasDiagnosticInfo = true;
                }

                if (BeginField("LocalizedText", true))
                {
                    value.LocalizedText = ReadInt32(null);
                    EndField("LocalizedText");
                    hasDiagnosticInfo = true;
                }

                value.AdditionalInfo = ReadString("AdditionalInfo");
                value.InnerStatusCode = ReadStatusCode("InnerStatusCode");

                hasDiagnosticInfo =
                    hasDiagnosticInfo ||
                    value.AdditionalInfo != null ||
                    !value.InnerStatusCode.Equals(
                        StatusCodes.Good, StatusCodeComparison.AllBits);

                if (BeginField("InnerDiagnosticInfo", true))
                {
                    value.InnerDiagnosticInfo = ReadDiagnosticInfo(depth + 1);
                    EndField("InnerDiagnosticInfo");
                    hasDiagnosticInfo = true;
                }

                return hasDiagnosticInfo ? value : null;
            }
            finally
            {
                m_nestingLevel--;
            }
        }

        /// <summary>
        /// Reads a Matrix from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Variant ReadMatrix(
            string? fieldName,
            bool readRawValue,
            BuiltInType rawBuiltInType)
        {
            CheckAndIncrementNestingLevel();

            Variant value = default;
            try
            {
                if (BeginField(fieldName, true))
                {
                    PushNamespace(Namespaces.OpcUaXsd);

                    int[] dimensions = ReadInt32Array("Dimensions").ToArray() ?? [];
                    MatrixOf.ThrowIfRankNotSupported(dimensions.Length);

                    // A multi-dimensional Variant must carry Dimensions with at
                    // least two entries, each greater than zero (Part 6 5.2.2.16);
                    // the product-versus-length consistency is enforced by
                    // MatrixOf<T> below. Reject an absent, too-short, zero or
                    // negative dimension here so an empty matrix (which would
                    // otherwise satisfy the product check) is rejected. The
                    // inline matrix of a structure field may be empty (5.2.5):
                    // a dimension <= 0 means no values, like in binary.
                    if (readRawValue)
                    {
                        MatrixOf.NormalizeInlineMatrixDimensions(dimensions);
                    }
                    if (readRawValue
                        ? !MatrixOf.IsValidInlineMatrix(dimensions, -1, Context.MaxArrayLength)
                        : !MatrixOf.IsValidMatrix(dimensions))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Variant matrix Dimensions [{0}] are inconsistent.",
                            string.Join(",", dimensions));
                    }
                    if (BeginField("Elements", true))
                    {
                        value = ReadMatrix(dimensions);
                        EndField("Elements");
                    }
                    else if (readRawValue)
                    {
                        // An empty inline matrix has no element to take the type
                        // from; it must have a zero dimension.
                        if (!MatrixOf.IsValidInlineMatrix(dimensions, 0, Context.MaxArrayLength))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Variant matrix Dimensions [{0}] are inconsistent with 0 element(s).",
                                string.Join(",", dimensions));
                        }
                        value = Variant.CreateEmptyMatrix(
                            rawBuiltInType == BuiltInType.Enumeration ? BuiltInType.Int32 : rawBuiltInType,
                            dimensions);
                    }

                    PopNamespace();

                    EndField(fieldName);
                }
            }
            catch (ArgumentException ex)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    ex,
                    "Variant matrix Dimensions are inconsistent.");
            }
            finally
            {
                m_nestingLevel--;
            }
            return value;

            MatrixOf<T> ToMatrix<T>(ArrayOf<T> elements, int[] dimensions)
            {
                if (readRawValue
                    ? !MatrixOf.IsValidInlineMatrix(dimensions, elements.Count, Context.MaxArrayLength)
                    : !MatrixOf.IsValidMatrix(dimensions, elements.Count))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Variant matrix Dimensions [{0}] are inconsistent with {1} element(s).",
                        string.Join(",", dimensions),
                        elements.Count);
                }

                try
                {
                    return elements.ToMatrix(dimensions);
                }
                catch (ArgumentException)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Variant matrix Dimensions [{0}] are inconsistent with {1} element(s).",
                        string.Join(",", dimensions),
                        elements.Count);
                }
            }

            Variant ReadMatrix(int[] dimensions)
            {
                switch (m_reader.LocalName)
                {
                    case "Boolean":
                        return Variant.From(ToMatrix(ReadBooleanArray(null), dimensions));
                    case "SByte":
                        return Variant.From(ToMatrix(ReadSByteArray(null), dimensions));
                    case "Byte":
                        return Variant.From(ToMatrix(ReadByteArray(null), dimensions));
                    case "Int16":
                        return Variant.From(ToMatrix(ReadInt16Array(null), dimensions));
                    case "UInt16":
                        return Variant.From(ToMatrix(ReadUInt16Array(null), dimensions));
                    case "Int32":
                        return Variant.From(ToMatrix(ReadInt32Array(null), dimensions));
                    case "UInt32":
                        return Variant.From(ToMatrix(ReadUInt32Array(null), dimensions));
                    case "Int64":
                        return Variant.From(ToMatrix(ReadInt64Array(null), dimensions));
                    case "UInt64":
                        return Variant.From(ToMatrix(ReadUInt64Array(null), dimensions));
                    case "Float":
                        return Variant.From(ToMatrix(ReadFloatArray(null), dimensions));
                    case "Double":
                        return Variant.From(ToMatrix(ReadDoubleArray(null), dimensions));
                    case "String":
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                        return Variant.From(ToMatrix(ReadStringArray(null), dimensions));
#pragma warning restore CS8620
                    case "DateTime":
                        return Variant.From(ToMatrix(ReadDateTimeArray(null), dimensions));
                    case "Guid":
                        return Variant.From(ToMatrix(ReadGuidArray(null), dimensions));
                    case "ByteString":
                        return Variant.From(ToMatrix(ReadByteStringArray(null), dimensions));
                    case "XmlElement":
                        return Variant.From(ToMatrix(ReadXmlElementArray(null), dimensions));
                    case "NodeId":
                        return Variant.From(ToMatrix(ReadNodeIdArray(null), dimensions));
                    case "ExpandedNodeId":
                        return Variant.From(ToMatrix(ReadExpandedNodeIdArray(null), dimensions));
                    case "StatusCode":
                        return Variant.From(ToMatrix(ReadStatusCodeArray(null), dimensions));
                    case "QualifiedName":
                        return Variant.From(ToMatrix(ReadQualifiedNameArray(null), dimensions));
                    case "LocalizedText":
                        return Variant.From(ToMatrix(ReadLocalizedTextArray(null), dimensions));
                    case "ExtensionObject":
                        return Variant.From(ToMatrix(ReadExtensionObjectArray(null), dimensions));
                    case "DataValue":
#pragma warning disable CS8620 // Argument cannot be used due to differences in nullability
                        return Variant.From(ToMatrix(ReadDataValueArray(null), dimensions));
#pragma warning restore CS8620
                    case "Variant":
                        return Variant.From(ToMatrix(ReadVariantArray(null), dimensions));
                    default:
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Element '{1}:{0}' is not allowed in a Variant.",
                            m_reader.LocalName,
                            m_reader.NamespaceURI);
                }
            }
        }

        private T ReadEncodeable<T>(string? fieldName, T value) where T : IEncodeable
        {
            CheckAndIncrementNestingLevel();
            try
            {
                if (BeginField(fieldName, true))
                {
                    XmlQualifiedName? xmlName = TypeInfo.GetXmlName(value, Context);

                    PushNamespace(xmlName!.Namespace);
                    value.Decode(this);
                    PopNamespace();

                    // skip to end of encodeable object.
                    m_reader.MoveToContent();

                    while (
                        !(
                            m_reader.NodeType == XmlNodeType.EndElement &&
                            m_reader.LocalName == fieldName &&
                            m_reader.NamespaceURI == m_namespaces.Peek()))
                    {
                        if (m_reader.NodeType == XmlNodeType.None)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadDecodingError,
                                "Unexpected end of stream decoding field '{0}' for type '{1}'.",
                                fieldName!,
                                typeof(T).FullName ?? string.Empty);
                        }

                        m_reader.Skip();
                        m_reader.MoveToContent();
                    }

                    EndField(fieldName);
                }
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Error decoding field '{0}' for type '{1}': {2}",
                    fieldName!,
                    typeof(T).Name,
                    xe.Message);
            }
            finally
            {
                m_nestingLevel--;
            }
            return value;
        }

        /// <summary>
        /// Removes the layout whitespace around the Identifier text of an
        /// XML NodeId/ExpandedNodeId. ReadString keeps whitespace (xs:string),
        /// but the parsers accept none. Leading whitespace is never part of
        /// the id. Trailing spaces are kept for string ids ("s="), but String
        /// identifiers shall not contain Unicode control characters (Part 3
        /// 8.2.4), so trailing layout (newline, tab, other C0/C1) is removed.
        /// </summary>
        internal static string? TrimNodeIdText(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string trimmed = text!.TrimStart();

            // skip the svr=/svu=/nsu=/ns= prefixes to find the id type.
            int start = 0;
            while (HasPrefixAt(trimmed, start, "svr=") ||
                HasPrefixAt(trimmed, start, "svu=") ||
                HasPrefixAt(trimmed, start, "nsu=") ||
                HasPrefixAt(trimmed, start, "ns="))
            {
                int separator = trimmed.IndexOf(';', start);
                if (separator < 0)
                {
                    break;
                }
                start = separator + 1;
            }

            if (HasPrefixAt(trimmed, start, "s="))
            {
                // cut the trailing whitespace run at its first control character,
                // e.g. "s=Tag \n  " -> "s=Tag ".
                int end = trimmed.Length;
                int cut = end;
                while (end > start + 2 &&
                    (char.IsWhiteSpace(trimmed[end - 1]) || char.IsControl(trimmed[end - 1])))
                {
                    end--;
                    if (char.IsControl(trimmed[end]))
                    {
                        cut = end;
                    }
                }
                return cut == trimmed.Length ? trimmed : trimmed[..cut];
            }

            return trimmed.TrimEnd();

            static bool HasPrefixAt(string value, int index, string prefix)
            {
                return string.CompareOrdinal(value, index, prefix, 0, prefix.Length) == 0;
            }
        }

        /// <summary>
        /// Reads a string from the stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private string? SafeReadString([CallerMemberName] string? functionName = null)
        {
            string message;
            try
            {
                string value = m_reader.ReadContentAsString();

                // check the length.
                if (EncodingLimits.StringExceedsLimit(
                    Context.MaxStringLength,
                    value,
                    out int byteLength))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "ReadString in {0} exceeds MaxStringLength: {1} > {2}",
                        functionName ?? string.Empty,
                        byteLength,
                        Context.MaxStringLength);
                }

                return value;
            }
            catch (XmlException xe)
            {
                message = xe.Message;
            }
            catch (InvalidOperationException ioe)
            {
                message = ioe.Message;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadDecodingError,
                "Unable to read string of {0}: {1}",
                functionName ?? string.Empty,
                message);
        }

        /// <summary>
        /// Reads the element the reader is positioned on as raw XML, bounded by
        /// maxStringLength (0 for none) and by the XML element depth limit.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private string ReadXmlElementContent(
            string? fieldName,
            int maxStringLength,
            [CallerMemberName] string? functionName = null)
        {
            try
            {
                return EncodingLimits.ReadXmlElementContent(
                    m_reader,
                    EncodingLimits.GetMaxXmlElementDepth(Context, m_nestingLevel),
                    maxStringLength);
            }
            catch (XmlException xe)
            {
                throw CreateBadDecodingError(fieldName, xe, functionName);
            }
            catch (InvalidOperationException ioe)
            {
                throw CreateBadDecodingError(fieldName, ioe, functionName);
            }
            catch (ArgumentException ae)
            {
                throw CreateBadDecodingError(fieldName, ae, functionName);
            }
        }

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
        /// Reads the start of field where the presences of the xsi:nil attribute is not significant.
        /// </summary>
        private bool BeginField(string? fieldName, bool isOptional)
        {
            return BeginField(fieldName, isOptional, out _);
        }

        /// <summary>
        /// Reads the start of field.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private bool BeginField(string? fieldName, bool isOptional, out bool isNil)
        {
            return BeginField(fieldName, isOptional, out isNil, false, out _);
        }

        /// <summary>
        /// Reads the start of field. With <paramref name="captureWhitespace"/> the
        /// whitespace of an element that holds nothing else is returned in
        /// <paramref name="whitespace"/> (the method returns false for it). When
        /// the method returns true it holds the leading whitespace that was
        /// read before the content (split off by a comment or processing
        /// instruction), which the caller prepends.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private bool BeginField(
            string? fieldName,
            bool isOptional,
            out bool isNil,
            bool captureWhitespace,
            out string? whitespace)
        {
            whitespace = null;
            try
            {
                isNil = false;

                // move to the next node.
                m_reader.MoveToContent();

                // allow caller to skip reading element tag if field name is not specified.
                if (string.IsNullOrEmpty(fieldName))
                {
                    return true;
                }

                // check if requested element is present.
                if (!m_reader.IsStartElement(fieldName, m_namespaces.Peek()))
                {
                    if (!isOptional)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Encountered element: '{1}:{0}' when expecting element: '{2}:{3}'.",
                            m_reader.LocalName,
                            m_reader.NamespaceURI,
                            fieldName!,
                            m_namespaces.Peek());
                    }

                    isNil = true;

                    // nothing more to read.
                    return false;
                }

                // check for empty or nil element.
                if (m_reader.HasAttributes)
                {
                    string? nilValue = m_reader.GetAttribute("nil", Namespaces.XmlSchemaInstance);

                    if (!string.IsNullOrEmpty(nilValue) &&
                        SafeXmlConvert(fieldName, XmlConvert.ToBoolean, nilValue))
                    {
                        isNil = true;
                    }
                }

                bool isEmpty = m_reader.IsEmptyElement;

                m_reader.ReadStartElement();

                if (!isEmpty)
                {
                    string? content = null;
                    if (captureWhitespace)
                    {
                        // MoveToContent skips whitespace nodes, keep them for
                        // xs:string - also past comments and processing
                        // instructions, which are not part of the value.
                        while (m_reader.NodeType is
                            XmlNodeType.Whitespace or
                            XmlNodeType.SignificantWhitespace or
                            XmlNodeType.Comment or
                            XmlNodeType.ProcessingInstruction)
                        {
                            if (m_reader.NodeType is
                                XmlNodeType.Whitespace or
                                XmlNodeType.SignificantWhitespace)
                            {
                                content = content == null ? m_reader.Value : content + m_reader.Value;
                            }
                            m_reader.Read();
                        }
                    }

                    m_reader.MoveToContent();

                    // check for an element with no children but not empty (due to whitespace).
                    if (m_reader.NodeType == XmlNodeType.EndElement &&
                        m_reader.LocalName == fieldName &&
                        m_reader.NamespaceURI == m_namespaces.Peek())
                    {
                        m_reader.ReadEndElement();
                        whitespace = content;
                        return false;
                    }

                    // a nilled element shall have no content (XML Schema Part 1,
                    // Element Locally Valid); callers do not read it, which
                    // left the reader inside it and dropped the next fields.
                    if (isNil)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Element '{0}' is nil but has content.",
                            fieldName!);
                    }

                    // the leading whitespace of content that follows.
                    whitespace = content;
                }

                // caller must read contents of element.
                return !isNil && !isEmpty;
            }
            catch (XmlException xe)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Unable to read field {0}: {1}",
                    fieldName!,
                    xe.Message);
            }
        }

        /// <summary>
        /// Reads the end of a field.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void EndField(string? fieldName)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                try
                {
                    m_reader.MoveToContent();

                    if (m_reader.NodeType != XmlNodeType.EndElement ||
                        m_reader.LocalName != fieldName ||
                        m_reader.NamespaceURI != m_namespaces.Peek())
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Encountered end element: '{1}:{0}' when expecting element: '{3}:{2}'.",
                            m_reader.LocalName,
                            m_reader.NamespaceURI,
                            fieldName!,
                            m_namespaces.Peek());
                    }

                    m_reader.ReadEndElement();
                }
                catch (XmlException xe)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unable to read end field: {0}: {1}",
                        fieldName!,
                        xe.Message);
                }
            }
        }

        /// <summary>
        /// Moves to the next start element.
        /// </summary>
        private bool MoveToElement(string elementName)
        {
            while (!m_reader.IsStartElement())
            {
                if (m_reader.NodeType is XmlNodeType.None or XmlNodeType.EndElement)
                {
                    return false;
                }

                m_reader.Read();
            }

            if (string.IsNullOrEmpty(elementName))
            {
                return true;
            }

            return m_reader.LocalName == elementName &&
                m_reader.NamespaceURI == m_namespaces.Peek();
        }

        /// <summary>
        /// Checks MaxArrayLength before one more element is added to an array
        /// holding <paramref name="count"/> elements, so the list never grows
        /// beyond the limit.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckArrayLength(int count)
        {
            if (Context.MaxArrayLength > 0 && count >= Context.MaxArrayLength)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "MaxArrayLength {0} exceeded.",
                    Context.MaxArrayLength);
            }
        }

        /// <summary>
        /// Test and increment the nesting level.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckAndIncrementNestingLevel([CallerMemberName] string? functionName = null)
        {
            if (m_nestingLevel > Context.MaxEncodingNestingLevels)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Maximum nesting level of {0} in function {1} was exceeded",
                    Context.MaxEncodingNestingLevels,
                    functionName ?? string.Empty);
            }
            EncodingLimits.EnsureSufficientStack();
            m_nestingLevel++;
        }

        /// <summary>
        /// Helper to create a BadDecodingError exception.
        /// </summary>
        private static ServiceResultException CreateBadDecodingError(
            string? fieldName,
            Exception ex,
            [CallerMemberName] string? functionName = null,
            string? value = null)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Unable to read field {0} in function {1}: {2}. Value: '{3}'",
                    fieldName ?? string.Empty,
                    functionName ?? string.Empty,
                    ex.Message,
                    value!);
            }
            return ServiceResultException.Create(
                StatusCodes.BadDecodingError,
                "Unable to read field {0} in function {1}: {2}",
                fieldName ?? string.Empty,
                functionName ?? string.Empty,
                ex.Message);
        }

        /// <summary>
        /// Wrapper for XmlConvert calls which catches the
        /// <see cref="FormatException"/> or <see cref="OverflowException"/>"
        /// and throws instead a <see cref="ServiceResultException"/> with
        /// StatusCode <see cref="StatusCodes.BadDecodingError"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private static T SafeXmlConvert<T>(
            string? fieldName,
            Func<string, T> converter,
            string xml,
            [CallerMemberName] string? functionName = null)
        {
            try
            {
                return converter(xml);
            }
            catch (OverflowException ove)
            {
                throw CreateBadDecodingError(fieldName, ove, functionName: functionName, value: xml);
            }
            catch (FormatException fe)
            {
                throw CreateBadDecodingError(fieldName, fe, functionName: functionName, value: xml);
            }
        }

        private readonly ILogger m_logger;
        private XmlReader m_reader;
        private readonly Stack<string> m_namespaces = [];
        private ushort[]? m_namespaceMappings;
        private ushort[]? m_serverMappings;
        private uint m_nestingLevel;
    }
}
