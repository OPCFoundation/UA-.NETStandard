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
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class XmlParserTests
    {
        private const string Ns = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        [Test]
        public void ConstructorWithStringValidContextCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root></Root>";

            // Act
            using var decoder = new XmlParser(xml, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithStringNullContextThrowsArgumentNullException()
        {
            // Arrange
            const string xml = "<Root></Root>";

            // Act & Assert
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => new XmlParser(xml, null));
            Assert.That(ex.ParamName, Is.EqualTo("context"));
        }

        [Test]
        public void ConstructorWithStringParsesXml()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Child>Value</Child>
            </Root>
            """;

            // Act
            using var decoder = new XmlParser(xml, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.EncodingType, Is.EqualTo(EncodingType.Xml));
        }

        [Test]
        public void ConstructorWithOpcUaXmlElementValidContextCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xmlContent = "<Root>Content</Root>";
            var xmlElement = XmlElement.From(xmlContent);

            // Act
            using var decoder = new XmlParser(xmlElement, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithOpcUaXmlElementNullContextThrowsArgumentNullException()
        {
            // Arrange
            const string xmlContent = "<Root>Content</Root>";
            var xmlElement = XmlElement.From(xmlContent);

            // Act & Assert
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => new XmlParser(xmlElement, null));
            Assert.That(ex.ParamName, Is.EqualTo("context"));
        }

        [Test]
        public void ConstructorWithSystemXmlElementValidContextCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            var doc = new XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
            doc.LoadXml("<Root>Content</Root>");
#pragma warning restore CA3075 // Insecure DTD processing in XML
            System.Xml.XmlElement xmlElement = doc.DocumentElement;

            // Act
            using var decoder = new XmlParser(xmlElement, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithSystemXmlElementNullContextThrowsArgumentNullException()
        {
            // Arrange
            var doc = new XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
            doc.LoadXml("<Root>Content</Root>");
#pragma warning restore CA3075 // Insecure DTD processing in XML
            System.Xml.XmlElement xmlElement = doc.DocumentElement;

            // Act & Assert
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => new XmlParser(xmlElement, null));
            Assert.That(ex.ParamName, Is.EqualTo("context"));
        }

        [Test]
        public void ConstructorWithSystemXmlElementMinimalXmlCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            var doc = new XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
            doc.LoadXml("<Root/>");
#pragma warning restore CA3075 // Insecure DTD processing in XML
            System.Xml.XmlElement xmlElement = doc.DocumentElement;

            // Act
            using var decoder = new XmlParser(xmlElement, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithTypeAndStringValidParametersCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEncodeable xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Value>Test</Value>
            </TestEncodeable>
            """;

            // Act
            using var decoder = new XmlParser(typeof(TestEncodeable), xml, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithTypeAndStringNullTypeCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root><Value>Test</Value></Root>";

            // Act
            using var decoder = new XmlParser(null, xml, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithTypeAndStringWithNamespacePrefixStripsPrefix()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ns:Root xmlns:ns="http://test.namespace">
                <Value>Test</Value>
            </ns:Root>
            """;

            // Act
            using var decoder = new XmlParser(null, xml, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
        }

        [Test]
        public void DisposeCleansUpSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root></Root>";
            using var decoder = new XmlParser(xml, messageContext);

            // Act & Assert
            Assert.DoesNotThrow(decoder.Dispose);
        }

        [Test]
        public void LoadStringTableValidTableReturnsTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Uri>http://namespace1.com</Uri>
                <Uri>http://namespace2.com</Uri>
                <Uri>http://namespace3.com</Uri>
            </NamespaceUris>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(stringTable.Count, Is.EqualTo(3));
            Assert.That(stringTable.GetString(0), Is.EqualTo("http://namespace1.com"));
            Assert.That(stringTable.GetString(1), Is.EqualTo("http://namespace2.com"));
            Assert.That(stringTable.GetString(2), Is.EqualTo("http://namespace3.com"));
        }

        [Test]
        public void LoadStringTableEmptyTableReturnsTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
            </NamespaceUris>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(stringTable.Count, Is.Zero);
        }

        [Test]
        public void LoadStringTableTableNotFoundReturnsFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <OtherElement xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Data>Value</Data>
            </OtherElement>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(stringTable.Count, Is.Zero);
        }

        [Test]
        public void LoadStringTableSingleEntryReturnsTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Uri>http://single.namespace.com</Uri>
            </NamespaceUris>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(stringTable.Count, Is.EqualTo(1));
            Assert.That(stringTable.GetString(0), Is.EqualTo("http://single.namespace.com"));
        }

        [Test]
        public void LoadStringTableWithEmptyElementsThrows()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Uri></Uri>
                <Uri>http://namespace.com</Uri>
                <Uri></Uri>
            </NamespaceUris>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var stringTable = new StringTable();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => decoder.LoadStringTable("NamespaceUris", "Uri", stringTable));
        }

        [Test]
        public void LoadStringTableDifferentTableNameReturnsFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ServerUris xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Uri>http://server1.com</Uri>
            </ServerUris>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void LoadStringTableDifferentElementNameIgnoresElements()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Namespace>http://namespace1.com</Namespace>
                <Namespace>http://namespace2.com</Namespace>
            </NamespaceUris>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(stringTable.Count, Is.Zero);
        }

        [Test]
        public void LoadStringTableWithElementsWithWhiteSpaceAddsWhitespace()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Uri>   test   </Uri>
                <Uri>http://namespace.com</Uri>
            </NamespaceUris>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(stringTable.Count, Is.EqualTo(2));
            Assert.That(stringTable.GetString(0), Is.EqualTo("test"));
        }

        [Test]
        public void PeekStringWithMatchingFieldNameReturnsTrue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEncodeable xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Child>Value</Child>
            </TestEncodeable>
            """;
            using var decoder = new XmlParser(typeof(TestEncodeable), xml, messageContext);

            // Act
            bool result = decoder.Peek("Child");

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void PeekStringWithNonMatchingFieldNameReturnsFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEncodeable xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Child>Value</Child>
            </TestEncodeable>
            """;
            using var decoder = new XmlParser(typeof(TestEncodeable), xml, messageContext);

            // Act
            bool result = decoder.Peek("Other");

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void PeekStringWithDifferentNamespaceReturnsFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEncodeable xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Child xmlns="http://different.namespace/">Value</Child>
            </TestEncodeable>
            """;
            using var decoder = new XmlParser(typeof(TestEncodeable), xml, messageContext);

            // Act
            bool result = decoder.Peek("Child");

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void PeekStringMovesToContent()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """

            <TestEncodeable xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Child>Value</Child>
            </TestEncodeable>
            """;
            using var decoder = new XmlParser(typeof(TestEncodeable), xml, messageContext);

            // Act
            bool result = decoder.Peek("Child");

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void ReadVariantContentsNullReturnsNullVariant()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Null xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd"/>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ReadVariantContentsBooleanReturnsBoolean()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Boolean xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">true</Boolean>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetBoolean(), Is.True);
        }

        [Test]
        public void ReadVariantContentsSByteReturnsSByte()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <SByte xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-128</SByte>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetSByte(), Is.EqualTo((sbyte)-128));
        }

        [Test]
        public void ReadVariantContentsByteReturnsByte()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Byte xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">255</Byte>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetByte(), Is.EqualTo((byte)255));
        }

        [Test]
        public void ReadVariantContentsInt16ReturnsInt16()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Int16 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-32768</Int16>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetInt16(), Is.EqualTo((short)-32768));
        }

        [Test]
        public void ReadVariantContentsUInt16ReturnsUInt16()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <UInt16 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">65535</UInt16>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetUInt16(), Is.EqualTo((ushort)65535));
        }

        [Test]
        public void ReadVariantContentsInt32ReturnsInt32()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Int32 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-2147483648</Int32>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetInt32(), Is.EqualTo(-2147483648));
        }

        [Test]
        public void ReadVariantContentsUInt32ReturnsUInt32()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <UInt32 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">4294967295</UInt32>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetUInt32(), Is.EqualTo(4294967295u));
        }

        [Test]
        public void ReadVariantContentsInt64ReturnsInt64()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Int64 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-9223372036854775808</Int64>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetInt64(), Is.EqualTo(-9223372036854775808));
        }

        [Test]
        public void ReadVariantContentsUInt64ReturnsUInt64()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <UInt64 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">18446744073709551615</UInt64>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetUInt64(), Is.EqualTo(18446744073709551615ul));
        }

        [Test]
        public void ReadVariantContentsFloatReturnsFloat()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Float xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">3.14159</Float>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetFloat(), Is.EqualTo(3.14159f).Within(0.00001f));
        }

        [Test]
        public void ReadVariantContentsDoubleReturnsDouble()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Double xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">3.141592653589793</Double>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetDouble(), Is.EqualTo(3.141592653589793).Within(0.000000000000001));
        }

        [Test]
        public void ReadVariantContentsStringReturnsString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <String xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">Hello World</String>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetString(), Is.EqualTo("Hello World"));
        }

        [Test]
        public void ReadVariantContentsDateTimeReturnsDateTime()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <DateTime xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">2024-01-15T12:30:45Z</DateTime>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetDateTime(), Is.EqualTo(new DateTimeUtc(2024, 1, 15, 12, 30, 45)));
        }

        [Test]
        public void ReadVariantContentsGuidReturnsGuid()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Guid xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <String>12345678-1234-1234-1234-123456789012</String>
            </Guid>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetGuid(), Is.EqualTo(new Uuid("12345678-1234-1234-1234-123456789012")));
        }

        [Test]
        public void ReadVariantContentsByteStringReturnsByteString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ByteString xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">AQIDBA==</ByteString>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetByteString(), Is.EqualTo(s_expectedByteArray));
        }

        [Test]
        public void ReadVariantContentsXmlElementReturnsXmlElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <XmlElement xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <CustomElement>Content</CustomElement>
            </XmlElement>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetXmlElement().IsEmpty, Is.False);
        }

        [Test]
        public void ReadVariantContentsNodeIdReturnsNodeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <NodeId xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Identifier>i=123</Identifier>
            </NodeId>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetNodeId(), Is.EqualTo(new NodeId(123)));
        }

        [Test]
        public void ReadVariantContentsExpandedNodeIdReturnsExpandedNodeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ExpandedNodeId xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Identifier>i=456</Identifier>
            </ExpandedNodeId>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetExpandedNodeId(), Is.EqualTo(new ExpandedNodeId(456)));
        }

        [Test]
        public void ReadVariantContentsStatusCodeReturnsStatusCode()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <StatusCode xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Code>2147483648</Code>
            </StatusCode>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetStatusCode(), Is.EqualTo(new StatusCode(0x80000000)));
        }

        [Test]
        public void ReadVariantContentsQualifiedNameReturnsQualifiedName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <QualifiedName xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <NamespaceIndex>1</NamespaceIndex>
                <Name>TestName</Name>
            </QualifiedName>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetQualifiedName(), Is.EqualTo(new QualifiedName("TestName", 1)));
        }

        [Test]
        public void ReadVariantContentsLocalizedTextReturnsLocalizedText()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <LocalizedText xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Locale>en-US</Locale>
                <Text>Hello</Text>
            </LocalizedText>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetLocalizedText(), Is.EqualTo(new LocalizedText("en-US", "Hello")));
        }

        [Test]
        public void ReadVariantContentsListOfBooleanReturnsBooleanArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfBoolean xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Boolean>true</Boolean>
                <Boolean>false</Boolean>
                <Boolean>true</Boolean>
            </ListOfBoolean>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetBooleanArray(), Is.EqualTo(s_expectedBoolArray.ToArrayOf()));
        }

        [Test]
        public void ReadVariantContentsListOfInt32ReturnsInt32Array()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfInt32 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Int32>1</Int32>
                <Int32>2</Int32>
                <Int32>3</Int32>
            </ListOfInt32>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetInt32Array(), Is.EqualTo(s_expectedInt32Array.ToArrayOf()));
        }

        [Test]
        public void ReadVariantContentsListOfStringReturnsStringArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfString xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <String>First</String>
                <String>Second</String>
                <String>Third</String>
            </ListOfString>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.GetStringArray(), Is.EqualTo(s_expectedStringArray.ToArrayOf()));
        }

        [Test]
        public void ReadVariantContentsInvalidTypeThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <InvalidType xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">Value</InvalidType>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act & Assert
            ServiceResultException ex =
                Assert.Throws<ServiceResultException>(() => decoder.ReadVariantValue(null, default));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("not allowed in a Variant"));
        }

        [Test]
        public void ReadVariantContentsInvalidArrayTypeThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfInvalidType xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <InvalidType>Value</InvalidType>
            </ListOfInvalidType>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => decoder.ReadVariantValue(null, default));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("not allowed in a Variant"));
        }

        [Test]
        public void ReadExtensionObjectBodyBinaryEncodedReturnsByteString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ByteString xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">AQIDBA==</ByteString>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var typeId = new ExpandedNodeId(123);

            // Act
            ExtensionObject result = decoder.ReadExtensionObjectBody(typeId);

            // Assert
            Assert.That(result.TryGetAsBinary(out ByteString byteString), Is.True);
        }

        [Test]
        public void ReadExtensionObjectBodyKnownTypeReturnsEncodeable()
        {
            // Arrange
            var mockFactory = new Mock<IEncodeableFactory>();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, mockFactory.Object);

            var encodeableType = new Mock<IEncodeableType>();
            encodeableType.SetupGet(x => x.Type).Returns(typeof(TestEncodeable));
            encodeableType.Setup(x => x.CreateInstance()).Returns(new TestEncodeable());
            IEncodeableType type = encodeableType.Object;
            mockFactory.Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out type))
                .Returns(true);

            const string xml = """
            <TestEncodeable xmlns="http://test.namespace">
            </TestEncodeable>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var typeId = new ExpandedNodeId(123);

            // Act
            ExtensionObject result = decoder.ReadExtensionObjectBody(typeId);

            // Assert
            Assert.That(result.TryGetEncodeable(out IEncodeable encodeable), Is.True);
        }

        [Test]
        public void ReadExtensionObjectBodyUnknownTypeReturnsXmlElement()
        {
            // Arrange
            var mockFactory = new Mock<IEncodeableFactory>();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, mockFactory.Object);

            var encodeableType = new Mock<IEncodeableType>();
            encodeableType.SetupGet(x => x.Type).Returns((Type)null);
            IEncodeableType type = encodeableType.Object;
            mockFactory.Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out type))
                .Returns(false);

            const string xml = """
            <CustomElement xmlns="http://test.namespace"><Value>Test</Value></CustomElement>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var typeId = new ExpandedNodeId(999);

            // Act
            ExtensionObject result = decoder.ReadExtensionObjectBody(typeId);

            // Assert
            Assert.That(result.TryGetAsXml(out XmlElement xmlElement), Is.True);
            Assert.That(xmlElement.OuterXml, Is.EqualTo(xml));
        }

        [Test]
        public void ReadExtensionObjectWhenFieldMissingReturnsNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\"/>";
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ExtensionObject result = decoder.ReadExtensionObject("ExtensionObject");

            // Assert
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ReadExtensionObjectWithoutBodyReturnsTypeId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ExtensionObject xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TypeId>
                    <Identifier>i=1</Identifier>
                </TypeId>
            </ExtensionObject>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ExtensionObject result = decoder.ReadExtensionObject("ExtensionObject");

            // Assert
            Assert.That(result.TypeId, Is.EqualTo(new ExpandedNodeId(new NodeId(1u))));
        }

        [Test]
        public void ReadExtensionObjectSetsTypeIdFromEncodeableBody()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            messageContext.Factory.AddEncodeableType(typeof(TestEncodeableWithTypeId));
            const string xml = """
            <ExtensionObject xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TypeId>
                    <Identifier>i=12345</Identifier>
                </TypeId>
                <Body>
                    <TestEncodeableWithTypeId xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd" />
                </Body>
            </ExtensionObject>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ExtensionObject result = decoder.ReadExtensionObject("ExtensionObject");

            // Assert
            Assert.That(result.TypeId, Is.EqualTo(new ExpandedNodeId(12345, 0)));
        }

        [Test]
        public void ReadEncodeableReturnsDecodedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEncodeableWithData xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Value>42</Value>
            </TestEncodeableWithData>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            TestEncodeableWithData result = decoder.ReadEncodeable<TestEncodeableWithData>("TestEncodeableWithData");

            // Assert
            Assert.That(result.Value, Is.EqualTo(42));
        }

        [Test]
        public void ReadEncodeableWithTypeIdReturnsDecodedValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            messageContext.Factory.AddEncodeableType(typeof(TestEncodeableWithData));
            const string xml = """
            <TestEncodeableWithData xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Value>99</Value>
            </TestEncodeableWithData>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            TestEncodeableWithData result = decoder.ReadEncodeable<TestEncodeableWithData>(
                "TestEncodeableWithData",
                new ExpandedNodeId(99999, 0));

            // Assert
            Assert.That(result.Value, Is.EqualTo(99));
        }

        [Test]
        public void ReadEncodeableWithTypeIdThrowsWhenFactoryMissing()
        {
            // Arrange
            var mockFactory = new Mock<IEncodeableFactory>();
            IEncodeableType type = null;
            mockFactory.Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out type))
                .Returns(false);
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, mockFactory.Object);
            const string xml = """
            <TestEncodeableWithData xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Value>1</Value>
            </TestEncodeableWithData>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeable<TestEncodeableWithData>(
                    "TestEncodeableWithData",
                    new ExpandedNodeId(99999, 0)));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadEncodeableArrayReturnsDecodedValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfTestEncodeableWithData xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TestEncodeableWithData>
                    <Value>1</Value>
                </TestEncodeableWithData>
                <TestEncodeableWithData>
                    <Value>2</Value>
                </TestEncodeableWithData>
            </ListOfTestEncodeableWithData>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ArrayOf<TestEncodeableWithData> result = decoder.ReadEncodeableArray<TestEncodeableWithData>(
                "ListOfTestEncodeableWithData");

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Value, Is.EqualTo(1));
            Assert.That(result[1].Value, Is.EqualTo(2));
        }

        [Test]
        public void ReadEncodeableArrayWithTypeIdReturnsDecodedValues()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            messageContext.Factory.AddEncodeableType(typeof(TestEncodeableWithData));
            const string xml = """
            <ListOfTestEncodeableWithData xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TestEncodeableWithData>
                    <Value>3</Value>
                </TestEncodeableWithData>
                <TestEncodeableWithData>
                    <Value>4</Value>
                </TestEncodeableWithData>
            </ListOfTestEncodeableWithData>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ArrayOf<TestEncodeableWithData> result = decoder.ReadEncodeableArray<TestEncodeableWithData>(
                "ListOfTestEncodeableWithData",
                new ExpandedNodeId(99999, 0));

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Value, Is.EqualTo(3));
            Assert.That(result[1].Value, Is.EqualTo(4));
        }

        [Test]
        public void ReadEncodeableArrayThrowsWhenMaxArrayLengthExceeded()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            messageContext.MaxArrayLength = 1;
            const string xml = """
            <ListOfTestEncodeableWithData xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TestEncodeableWithData>
                    <Value>1</Value>
                </TestEncodeableWithData>
                <TestEncodeableWithData>
                    <Value>2</Value>
                </TestEncodeableWithData>
            </ListOfTestEncodeableWithData>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeableArray<TestEncodeableWithData>("ListOfTestEncodeableWithData"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadDiagnosticInfoReturnsSymbolicId()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <DiagnosticInfo xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <SymbolicId>42</SymbolicId>
            </DiagnosticInfo>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            DiagnosticInfo result = decoder.ReadDiagnosticInfo("DiagnosticInfo");

            // Assert
            Assert.That(result.SymbolicId, Is.EqualTo(42));
        }

        [Test]
        public void ReadDiagnosticInfoReturnsNullForEmptyElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <DiagnosticInfo xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd" />
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            DiagnosticInfo result = decoder.ReadDiagnosticInfo("DiagnosticInfo");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadDiagnosticInfoThrowsWhenInnerDepthExceeded()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            string xml = CreateDiagnosticInfoWithDepth(DiagnosticInfo.MaxInnerDepth + 1);
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadDiagnosticInfo("DiagnosticInfo"));

            // Assert
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadDiagnosticInfoArrayReturnsCount()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfDiagnosticInfo xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <DiagnosticInfo>
                    <SymbolicId>1</SymbolicId>
                </DiagnosticInfo>
                <DiagnosticInfo>
                    <SymbolicId>2</SymbolicId>
                </DiagnosticInfo>
            </ListOfDiagnosticInfo>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ArrayOf<DiagnosticInfo> result = decoder.ReadDiagnosticInfoArray("ListOfDiagnosticInfo");

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ReadDiagnosticInfoArrayThrowsWhenMaxArrayLengthExceeded()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            messageContext.MaxArrayLength = 1;
            const string xml = """
            <ListOfDiagnosticInfo xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <DiagnosticInfo>
                    <SymbolicId>1</SymbolicId>
                </DiagnosticInfo>
                <DiagnosticInfo>
                    <SymbolicId>2</SymbolicId>
                </DiagnosticInfo>
            </ListOfDiagnosticInfo>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadDiagnosticInfoArray("ListOfDiagnosticInfo"));

            // Assert
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadEncodingMaskReturnsValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <EncodingMask xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">5</EncodingMask>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            uint result = decoder.ReadEncodingMask(null);

            // Assert
            Assert.That(result, Is.EqualTo(5u));
        }

        [Test]
        public void ReadEncodingMaskReturnsZeroWhenMissing()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd" />
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            uint result = decoder.ReadEncodingMask(null);

            // Assert
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void ReadSwitchFieldReturnsValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <SwitchField xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">7</SwitchField>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            uint result = decoder.ReadSwitchField(null, out _);

            // Assert
            Assert.That(result, Is.EqualTo(7u));
        }

        [Test]
        public void ReadSwitchFieldSetsNullFieldName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <SwitchField xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">1</SwitchField>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            decoder.ReadSwitchField(null, out string fieldName);

            // Assert
            Assert.That(fieldName, Is.Null);
        }

        [Test]
        public void ReadEnumeratedReturnsEnumValue()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">Value1</TestEnum>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            TestEnum result = decoder.ReadEnumerated<TestEnum>("TestEnum");

            // Assert
            Assert.That(result, Is.EqualTo(TestEnum.Value1));
        }

        [Test]
        public void ReadEnumeratedReturnsNumericValueWhenSuffixed()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">Value_2</TestEnum>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            TestEnum result = decoder.ReadEnumerated<TestEnum>("TestEnum");

            // Assert
            Assert.That(result, Is.EqualTo(TestEnum.Value2));
        }

        [Test]
        public void ReadEnumeratedThrowsWhenInvalid()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">Invalid</TestEnum>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEnumerated<TestEnum>("TestEnum"));

            // Assert
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadEnumeratedArrayReturnsCount()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfTestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TestEnum>Value1</TestEnum>
                <TestEnum>Value2</TestEnum>
            </ListOfTestEnum>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ArrayOf<TestEnum> result = decoder.ReadEnumeratedArray<TestEnum>("ListOfTestEnum");

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ReadEnumeratedArrayThrowsWhenMaxArrayLengthExceeded()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            messageContext.MaxArrayLength = 1;
            const string xml = """
            <ListOfTestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TestEnum>Value1</TestEnum>
                <TestEnum>Value2</TestEnum>
            </ListOfTestEnum>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEnumeratedArray<TestEnum>("ListOfTestEnum"));

            // Assert
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadEnumeratedArrayThrowsBadDecodingErrorWhenNoElementsPresent()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfTestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <![CDATA[]]>
            </ListOfTestEnum>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEnumeratedArray("ListOfTestEnum"));

            // Assert
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadEnumeratedArrayThrowsWhenEmpty()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfTestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd"></ListOfTestEnum>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEnumeratedArray("ListOfTestEnum"));

            // Assert
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadEnumeratedArrayReturnsDefaultWhenNil()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfTestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:nil="true" />
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ArrayOf<EnumValue> result = decoder.ReadEnumeratedArray("ListOfTestEnum");

            // Assert
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ReadEnumeratedArrayReturnsDefaultWhenMissing()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Other xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd" />
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ArrayOf<EnumValue> result = decoder.ReadEnumeratedArray("ListOfTestEnum");

            // Assert
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ReadEnumeratedArrayParsesPopulatedArray()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ListOfTestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TestEnum>Value1</TestEnum>
                <TestEnum>Value2</TestEnum>
            </ListOfTestEnum>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ArrayOf<EnumValue> result = decoder.ReadEnumeratedArray("ListOfTestEnum");

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Symbol, Is.EqualTo("Value1"));
            Assert.That(result[1].Symbol, Is.EqualTo("Value2"));
        }

        [Test]
        public void DecodeMessageReturnsDecodedValue()
        {
            // Arrange
            var mockFactory = new Mock<IEncodeableFactory>();
            var encodeableType = new Mock<IEncodeableType>();
            encodeableType.SetupGet(x => x.Type).Returns(typeof(TestEncodeableWithData));
            encodeableType.SetupGet(x => x.XmlName)
                .Returns(new XmlQualifiedName("TestEncodeableWithData", Namespaces.OpcUaXsd));
            encodeableType.Setup(x => x.CreateInstance()).Returns(new TestEncodeableWithData());
            IType type = encodeableType.Object;
            mockFactory.Setup(f => f.TryGetType(It.IsAny<XmlQualifiedName>(), out type))
                .Returns(true);
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, mockFactory.Object);
            const string xml = """
            <TestEncodeableWithData xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Value>5</Value>
            </TestEncodeableWithData>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            TestEncodeableWithData result = decoder.DecodeMessage<TestEncodeableWithData>();

            // Assert
            Assert.That(result.Value, Is.EqualTo(5));
        }

        [Test]
        public void DecodeMessageThrowsWhenTypeUnknown()
        {
            // Arrange
            var mockFactory = new Mock<IEncodeableFactory>();
            IType type = null;
            mockFactory.Setup(f => f.TryGetType(It.IsAny<XmlQualifiedName>(), out type))
                .Returns(false);
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, mockFactory.Object);
            const string xml = """
            <TestEncodeableWithData xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Value>1</Value>
            </TestEncodeableWithData>
            """;
            using var decoder = new XmlParser(xml, messageContext);

            // Act
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.DecodeMessage<TestEncodeableWithData>());

            // Assert
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadQualifiedNameAppliesNamespaceMapping()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            var streamNamespaces = new NamespaceTable();
            streamNamespaces.Append("urn:stream0");
            streamNamespaces.Append("urn:stream1");
            var contextNamespaces = new NamespaceTable();
            contextNamespaces.Append("urn:context");
            contextNamespaces.Append("urn:stream0");
            contextNamespaces.Append("urn:stream1");
            messageContext.NamespaceUris = contextNamespaces;
            const string xml = """
            <QualifiedName xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <NamespaceIndex>1</NamespaceIndex>
                <Name>Test</Name>
            </QualifiedName>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.SetMappingTables(streamNamespaces, null);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            QualifiedName result = decoder.ReadQualifiedName("QualifiedName");

            // Assert
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void ReadNodeIdAppliesNamespaceMapping()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            var streamNamespaces = new NamespaceTable();
            streamNamespaces.Append("urn:stream0");
            streamNamespaces.Append("urn:stream1");
            var contextNamespaces = new NamespaceTable();
            contextNamespaces.Append("urn:context");
            contextNamespaces.Append("urn:stream0");
            contextNamespaces.Append("urn:stream1");
            messageContext.NamespaceUris = contextNamespaces;
            const string xml = """
            <NodeId xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Identifier>ns=1;i=1</Identifier>
            </NodeId>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.SetMappingTables(streamNamespaces, null);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            NodeId result = decoder.ReadNodeId("NodeId");

            // Assert
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void ReadExpandedNodeIdAppliesNamespaceMapping()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            var streamNamespaces = new NamespaceTable();
            streamNamespaces.Append("urn:stream0");
            streamNamespaces.Append("urn:stream1");
            var contextNamespaces = new NamespaceTable();
            contextNamespaces.Append("urn:context");
            contextNamespaces.Append("urn:stream0");
            contextNamespaces.Append("urn:stream1");
            messageContext.NamespaceUris = contextNamespaces;
            const string xml = """
            <ExpandedNodeId xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Identifier>ns=1;i=1</Identifier>
            </ExpandedNodeId>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.SetMappingTables(streamNamespaces, null);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ExpandedNodeId result = decoder.ReadExpandedNodeId("ExpandedNodeId");

            // Assert
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void ReadExpandedNodeIdAppliesServerMapping()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            var streamServers = new StringTable();
            streamServers.Append("urn:server1");
            var contextServers = new StringTable();
            contextServers.Append("urn:server0");
            contextServers.Append("urn:server1");
            messageContext.ServerUris = contextServers;
            const string xml = """
            <ExpandedNodeId xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Identifier>svr=0;ns=0;i=1</Identifier>
            </ExpandedNodeId>
            """;
            using var decoder = new XmlParser(xml, messageContext);
            decoder.SetMappingTables(null, streamServers);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act
            ExpandedNodeId result = decoder.ReadExpandedNodeId("ExpandedNodeId");

            // Assert
            Assert.That(result.ServerIndex, Is.EqualTo(1u));
        }

        [Test]
        public void ReadExtensionObjectBodyInvalidXmlThrowsException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <InvalidXml>
                <Unclosed>
            """;

            // Act & Assert - DOM parsing fails on malformed XML in the constructor
            Assert.Throws<XmlException>(() => new XmlParser(xml, messageContext));
        }

        [Test]
        public void ReadExtensionObjectBodyEmptyXmlReturnsXmlElement()
        {
            // Arrange
            var mockFactory = new Mock<IEncodeableFactory>();
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext, mockFactory.Object);

            var encodeableType = new Mock<IEncodeableType>();
            encodeableType.SetupGet(x => x.Type).Returns((Type)null);
            IEncodeableType type = encodeableType.Object;
            mockFactory.Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out type))
                .Returns(false);

            const string xml = """
            <Empty xmlns="http://test.namespace" />
            """;
            using var decoder = new XmlParser(xml, messageContext);
            var typeId = new ExpandedNodeId(999);

            // Act
            ExtensionObject result = decoder.ReadExtensionObjectBody(typeId);

            // Assert
            Assert.That(result.TryGetAsXml(out XmlElement xmlElement), Is.True);
            Assert.That(xmlElement.OuterXml, Is.EqualTo(xml));
        }

        [DataContract(Name = "TestEncodeable", Namespace = Namespaces.OpcUaXsd)]
        private sealed class TestEncodeable : IEncodeable
        {
            public ExpandedNodeId TypeId => ExpandedNodeId.Null;
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return false;
            }

            public object Clone()
            {
                return new TestEncodeable();
            }
        }

        [DataContract(Name = "TestEncodeableWithTypeId", Namespace = Namespaces.OpcUaXsd)]
        private sealed class TestEncodeableWithTypeId : IEncodeable
        {
            public ExpandedNodeId TypeId => new(12345, 0);
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeableWithTypeId;
            }

            public object Clone()
            {
                return new TestEncodeableWithTypeId();
            }
        }

        [DataContract(Name = "TestEncodeableWithData", Namespace = Namespaces.OpcUaXsd)]
        private sealed class TestEncodeableWithData : IEncodeable
        {
            public ExpandedNodeId TypeId => new(99999, 0);
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            public int Value { get; private set; }

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32("Value", Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = decoder.ReadInt32("Value");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeableWithData other && other.Value == Value;
            }

            public object Clone()
            {
                return new TestEncodeableWithData { Value = Value };
            }
        }

        private static ServiceMessageContext CreateMockContext()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            return ServiceMessageContext.CreateEmpty(telemetryContext);
        }

        private static string CreateDiagnosticInfoWithDepth(int depth)
        {
            string xml = "<DiagnosticInfo xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">";
            for (int i = 0; i < depth; i++)
            {
                xml += "<InnerDiagnosticInfo>";
            }

            xml += "<SymbolicId>1</SymbolicId>";

            for (int i = 0; i < depth; i++)
            {
                xml += "</InnerDiagnosticInfo>";
            }

            xml += "</DiagnosticInfo>";
            return xml;
        }

        [DataContract(Name = "TestEnum", Namespace = Namespaces.OpcUaXsd)]
        private enum TestEnum
        {
            Value0 = 0,
            Value1 = 1,
            Value2 = 2
        }

        private static readonly bool[] s_expectedBoolArray = [true, false, true];
        private static readonly int[] s_expectedInt32Array = [1, 2, 3];
        private static readonly string[] s_expectedStringArray = ["First", "Second", "Third"];
        private static readonly byte[] s_expectedByteArray = [1, 2, 3, 4];

        [Test]
        public void ConstructorWithStreamCreatesInstance()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = "<Root xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\"><Value>1</Value></Root>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

            using var decoder = new XmlParser(stream, ctx);

            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(ctx));
        }

        [Test]
        public void ConstructorWithStreamNullContextThrows()
        {
            const string xml = "<Root/>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

            Assert.Throws<ArgumentNullException>(() => new XmlParser(stream, null));
        }

        [Test]
        public void ConstructorWithStreamCanReadScalar()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"<Int32 xmlns=\"{Ns}\">42</Int32>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            using var decoder = new XmlParser(stream, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            int result = decoder.ReadInt32("Int32");

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void CloseDoesNotThrow()
        {
            ServiceMessageContext ctx = CreateContext();
            using var decoder = new XmlParser("<Root/>", ctx);

            Assert.DoesNotThrow(decoder.Close);
        }

        [Test]
        public void CloseWithCheckEofDoesNotThrow()
        {
            ServiceMessageContext ctx = CreateContext();
            using var decoder = new XmlParser("<Root/>", ctx);

            Assert.DoesNotThrow(decoder.Close);
            Assert.DoesNotThrow(decoder.Close);
        }

        [Test]
        public void PeekXmlNodeTypeElementReturnsQualifiedName()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <TestEncodeable xmlns="{Ns}">
                <Child>Value</Child>
            </TestEncodeable>
            """;
            using var decoder = new XmlParser(typeof(CoverageTestEncodeable), xml, ctx);

            XmlQualifiedName result = decoder.Peek(XmlNodeType.Element);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("Child"));
        }

        [Test]
        public void PeekXmlNodeTypeNoneReturnsQualifiedName()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <TestEncodeable xmlns="{Ns}">
                <Child>Value</Child>
            </TestEncodeable>
            """;
            using var decoder = new XmlParser(typeof(CoverageTestEncodeable), xml, ctx);

            XmlQualifiedName result = decoder.Peek(XmlNodeType.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("Child"));
        }

        [Test]
        public void PeekXmlNodeTypeTextReturnsNull()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <TestEncodeable xmlns="{Ns}">
                <Child>Value</Child>
            </TestEncodeable>
            """;
            using var decoder = new XmlParser(typeof(CoverageTestEncodeable), xml, ctx);

            // Text node type should return null for DOM-based parser
            XmlQualifiedName result = decoder.Peek(XmlNodeType.Text);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void PeekXmlNodeTypeOnEmptyElementReturnsNull()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);

            // No children and Text node type → null
            XmlQualifiedName result = decoder.Peek(XmlNodeType.Text);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadVariantReturnsVariantWithValue()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Variant xmlns="{Ns}">
                <Value>
                    <Int32 xmlns="{Ns}">42</Int32>
                </Value>
            </Variant>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Variant result = decoder.ReadVariant("Variant");

            Assert.That(result.GetInt32(), Is.EqualTo(42));
        }

        [Test]
        public void ReadVariantMissingFieldReturnsNull()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Variant result = decoder.ReadVariant("Variant");

            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithEmptyValueReturnsNull()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Variant xmlns="{Ns}">
            </Variant>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Variant result = decoder.ReadVariant("Variant");

            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadDataValueReturnsAllFields()
        {
            ServiceMessageContext ctx = CreateContext();
            // DataValue's Value field wraps a Variant, so the inner value
            // needs double <Value> nesting: one for the Variant field, one
            // for the Variant's internal Value element.
            const string xml = $"""
            <DataValue xmlns="{Ns}">
                <Value>
                    <Value>
                        <Int32 xmlns="{Ns}">100</Int32>
                    </Value>
                </Value>
                <StatusCode>
                    <Code>0</Code>
                </StatusCode>
                <SourceTimestamp>2024-06-15T10:30:00Z</SourceTimestamp>
                <SourcePicoseconds>500</SourcePicoseconds>
                <ServerTimestamp>2024-06-15T10:30:01Z</ServerTimestamp>
                <ServerPicoseconds>250</ServerPicoseconds>
            </DataValue>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            DataValue result = decoder.ReadDataValue("DataValue");

            Assert.That(result.WrappedValue.GetInt32(), Is.EqualTo(100));
            Assert.That(result.StatusCode, Is.EqualTo(new StatusCode(0)));
            Assert.That(result.SourceTimestamp, Is.EqualTo(new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc)));
            Assert.That(result.SourcePicoseconds, Is.EqualTo((ushort)500));
            Assert.That(result.ServerTimestamp, Is.EqualTo(new DateTime(2024, 6, 15, 10, 30, 1, DateTimeKind.Utc)));
            Assert.That(result.ServerPicoseconds, Is.EqualTo((ushort)250));
        }

        [Test]
        public void ReadDataValueMissingFieldReturnsDefault()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            DataValue result = decoder.ReadDataValue("DataValue");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadDataValueWithOnlyValueField()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <DataValue xmlns="{Ns}">
                <Value>
                    <Value>
                        <String xmlns="{Ns}">Hello</String>
                    </Value>
                </Value>
            </DataValue>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            DataValue result = decoder.ReadDataValue("DataValue");

            Assert.That(result.WrappedValue.GetString(), Is.EqualTo("Hello"));
            Assert.That(result.SourcePicoseconds, Is.Zero);
            Assert.That(result.ServerPicoseconds, Is.Zero);
        }

        [Test]
        public void ReadVariantValueContentsDataValueReturnsDataValue()
        {
            ServiceMessageContext ctx = CreateContext();
            // DataValue as a scalar variant value: the ReadDataValue call
            // inside ReadVariantValue wraps Value in a Variant (double Value).
            const string xml = $"""
            <DataValue xmlns="{Ns}">
                <Value>
                    <Value>
                        <Int32 xmlns="{Ns}">77</Int32>
                    </Value>
                </Value>
                <StatusCode>
                    <Code>0</Code>
                </StatusCode>
            </DataValue>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            DataValue dv = result.GetDataValue();
            Assert.That(dv, Is.Not.Null);
            Assert.That(dv.WrappedValue.GetInt32(), Is.EqualTo(77));
        }

        [Test]
        public void ReadSByteArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfSByte xmlns="{Ns}">
                <SByte>-128</SByte>
                <SByte>0</SByte>
                <SByte>127</SByte>
            </ListOfSByte>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<sbyte> result = decoder.ReadSByteArray("ListOfSByte");

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo((sbyte)-128));
            Assert.That(result[1], Is.Zero);
            Assert.That(result[2], Is.EqualTo((sbyte)127));
        }

        [Test]
        public void ReadByteArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfByte xmlns="{Ns}">
                <Byte>0</Byte>
                <Byte>128</Byte>
                <Byte>255</Byte>
            </ListOfByte>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<byte> result = decoder.ReadByteArray("ListOfByte");

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.Zero);
            Assert.That(result[2], Is.EqualTo((byte)255));
        }

        [Test]
        public void ReadInt16ArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfInt16 xmlns="{Ns}">
                <Int16>-32768</Int16>
                <Int16>0</Int16>
                <Int16>32767</Int16>
            </ListOfInt16>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<short> result = decoder.ReadInt16Array("ListOfInt16");

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo((short)-32768));
            Assert.That(result[2], Is.EqualTo((short)32767));
        }

        [Test]
        public void ReadUInt16ArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfUInt16 xmlns="{Ns}">
                <UInt16>0</UInt16>
                <UInt16>65535</UInt16>
            </ListOfUInt16>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<ushort> result = decoder.ReadUInt16Array("ListOfUInt16");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.Zero);
            Assert.That(result[1], Is.EqualTo((ushort)65535));
        }

        [Test]
        public void ReadInt32ArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfInt32 xmlns="{Ns}">
                <Int32>-100</Int32>
                <Int32>0</Int32>
                <Int32>100</Int32>
            </ListOfInt32>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<int> result = decoder.ReadInt32Array("ListOfInt32");

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(-100));
            Assert.That(result[2], Is.EqualTo(100));
        }

        [Test]
        public void ReadUInt32ArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfUInt32 xmlns="{Ns}">
                <UInt32>0</UInt32>
                <UInt32>4294967295</UInt32>
            </ListOfUInt32>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<uint> result = decoder.ReadUInt32Array("ListOfUInt32");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.Zero);
            Assert.That(result[1], Is.EqualTo(4294967295u));
        }

        [Test]
        public void ReadInt64ArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfInt64 xmlns="{Ns}">
                <Int64>-9223372036854775808</Int64>
                <Int64>9223372036854775807</Int64>
            </ListOfInt64>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<long> result = decoder.ReadInt64Array("ListOfInt64");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(long.MinValue));
            Assert.That(result[1], Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void ReadUInt64ArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfUInt64 xmlns="{Ns}">
                <UInt64>0</UInt64>
                <UInt64>18446744073709551615</UInt64>
            </ListOfUInt64>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<ulong> result = decoder.ReadUInt64Array("ListOfUInt64");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.Zero);
            Assert.That(result[1], Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void ReadFloatArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfFloat xmlns="{Ns}">
                <Float>1.5</Float>
                <Float>-2.5</Float>
            </ListOfFloat>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<float> result = decoder.ReadFloatArray("ListOfFloat");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(1.5f));
            Assert.That(result[1], Is.EqualTo(-2.5f));
        }

        [Test]
        public void ReadDoubleArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfDouble xmlns="{Ns}">
                <Double>3.141592653589793</Double>
                <Double>-1.0</Double>
            </ListOfDouble>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<double> result = decoder.ReadDoubleArray("ListOfDouble");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(3.141592653589793).Within(0.0000001));
            Assert.That(result[1], Is.EqualTo(-1.0));
        }

        [Test]
        public void ReadStringArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfString xmlns="{Ns}">
                <String>Hello</String>
                <String>World</String>
            </ListOfString>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<string> result = decoder.ReadStringArray("ListOfString");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo("Hello"));
            Assert.That(result[1], Is.EqualTo("World"));
        }

        [Test]
        public void ReadDateTimeArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfDateTime xmlns="{Ns}">
                <DateTime>2024-01-01T00:00:00Z</DateTime>
                <DateTime>2024-12-31T23:59:59Z</DateTime>
            </ListOfDateTime>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<DateTimeUtc> result = decoder.ReadDateTimeArray("ListOfDateTime");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That((DateTime)result[0], Is.EqualTo(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void ReadGuidArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfGuid xmlns="{Ns}">
                <Guid>
                    <String>12345678-1234-1234-1234-123456789012</String>
                </Guid>
                <Guid>
                    <String>aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee</String>
                </Guid>
            </ListOfGuid>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<Uuid> result = decoder.ReadGuidArray("ListOfGuid");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(new Uuid("12345678-1234-1234-1234-123456789012")));
        }

        [Test]
        public void ReadByteStringArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfByteString xmlns="{Ns}">
                <ByteString>AQID</ByteString>
                <ByteString>BAUG</ByteString>
            </ListOfByteString>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<ByteString> result = decoder.ReadByteStringArray("ListOfByteString");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(result[1].ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
        }

        [Test]
        public void ReadXmlElementArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfXmlElement xmlns="{Ns}">
                <XmlElement><Custom>A</Custom></XmlElement>
                <XmlElement><Custom>B</Custom></XmlElement>
            </ListOfXmlElement>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<XmlElement> result = decoder.ReadXmlElementArray("ListOfXmlElement");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].IsNull, Is.False);
            Assert.That(result[1].IsNull, Is.False);
        }

        [Test]
        public void ReadNodeIdArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfNodeId xmlns="{Ns}">
                <NodeId>
                    <Identifier>i=1</Identifier>
                </NodeId>
                <NodeId>
                    <Identifier>i=2</Identifier>
                </NodeId>
            </ListOfNodeId>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<NodeId> result = decoder.ReadNodeIdArray("ListOfNodeId");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(new NodeId(1)));
            Assert.That(result[1], Is.EqualTo(new NodeId(2)));
        }

        [Test]
        public void ReadExpandedNodeIdArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfExpandedNodeId xmlns="{Ns}">
                <ExpandedNodeId>
                    <Identifier>i=10</Identifier>
                </ExpandedNodeId>
                <ExpandedNodeId>
                    <Identifier>i=20</Identifier>
                </ExpandedNodeId>
            </ListOfExpandedNodeId>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<ExpandedNodeId> result = decoder.ReadExpandedNodeIdArray("ListOfExpandedNodeId");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(new ExpandedNodeId(10)));
            Assert.That(result[1], Is.EqualTo(new ExpandedNodeId(20)));
        }

        [Test]
        public void ReadStatusCodeArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfStatusCode xmlns="{Ns}">
                <StatusCode>
                    <Code>0</Code>
                </StatusCode>
                <StatusCode>
                    <Code>2147483648</Code>
                </StatusCode>
            </ListOfStatusCode>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<StatusCode> result = decoder.ReadStatusCodeArray("ListOfStatusCode");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(new StatusCode(0)));
            Assert.That(result[1], Is.EqualTo(new StatusCode(0x80000000)));
        }

        [Test]
        public void ReadQualifiedNameArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfQualifiedName xmlns="{Ns}">
                <QualifiedName>
                    <NamespaceIndex>0</NamespaceIndex>
                    <Name>First</Name>
                </QualifiedName>
                <QualifiedName>
                    <NamespaceIndex>1</NamespaceIndex>
                    <Name>Second</Name>
                </QualifiedName>
            </ListOfQualifiedName>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<QualifiedName> result = decoder.ReadQualifiedNameArray("ListOfQualifiedName");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Name, Is.EqualTo("First"));
            Assert.That(result[1].Name, Is.EqualTo("Second"));
        }

        [Test]
        public void ReadLocalizedTextArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfLocalizedText xmlns="{Ns}">
                <LocalizedText>
                    <Locale>en</Locale>
                    <Text>Hello</Text>
                </LocalizedText>
                <LocalizedText>
                    <Locale>de</Locale>
                    <Text>Hallo</Text>
                </LocalizedText>
            </ListOfLocalizedText>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<LocalizedText> result = decoder.ReadLocalizedTextArray("ListOfLocalizedText");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Text, Is.EqualTo("Hello"));
            Assert.That(result[1].Text, Is.EqualTo("Hallo"));
        }

        [Test]
        public void ReadVariantArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfVariant xmlns="{Ns}">
                <Variant>
                    <Value>
                        <Int32 xmlns="{Ns}">1</Int32>
                    </Value>
                </Variant>
                <Variant>
                    <Value>
                        <String xmlns="{Ns}">text</String>
                    </Value>
                </Variant>
            </ListOfVariant>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<Variant> result = decoder.ReadVariantArray("ListOfVariant");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].GetInt32(), Is.EqualTo(1));
            Assert.That(result[1].GetString(), Is.EqualTo("text"));
        }

        [Test]
        public void ReadDataValueArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfDataValue xmlns="{Ns}">
                <DataValue>
                    <Value>
                        <Value>
                            <Int32 xmlns="{Ns}">10</Int32>
                        </Value>
                    </Value>
                </DataValue>
                <DataValue>
                    <Value>
                        <Value>
                            <Int32 xmlns="{Ns}">20</Int32>
                        </Value>
                    </Value>
                </DataValue>
            </ListOfDataValue>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<DataValue> result = decoder.ReadDataValueArray("ListOfDataValue");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].WrappedValue.GetInt32(), Is.EqualTo(10));
            Assert.That(result[1].WrappedValue.GetInt32(), Is.EqualTo(20));
        }

        [Test]
        public void ReadVariantValueListOfSByteReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfSByte xmlns="{Ns}">
                <SByte>-1</SByte>
                <SByte>0</SByte>
                <SByte>1</SByte>
            </ListOfSByte>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.TryGet(out ArrayOf<sbyte> array), Is.True);
            Assert.That(array, Is.EqualTo(new sbyte[] { -1, 0, 1 }));
        }

        [Test]
        public void ReadVariantValueListOfByteReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfByte xmlns="{Ns}">
                <Byte>10</Byte>
                <Byte>20</Byte>
            </ListOfByte>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.TryGet(out ArrayOf<byte> array), Is.True);
            Assert.That(array, Is.EqualTo(new byte[] { 10, 20 }));
        }

        [Test]
        public void ReadVariantValueListOfInt16ReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfInt16 xmlns="{Ns}">
                <Int16>100</Int16>
                <Int16>200</Int16>
            </ListOfInt16>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.TryGet(out ArrayOf<short> array), Is.True);
            Assert.That(array, Is.EqualTo(new short[] { 100, 200 }));
        }

        [Test]
        public void ReadVariantValueListOfUInt16ReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfUInt16 xmlns="{Ns}">
                <UInt16>100</UInt16>
                <UInt16>200</UInt16>
            </ListOfUInt16>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.TryGet(out ArrayOf<ushort> array), Is.True);
            Assert.That(array, Is.EqualTo(new ushort[] { 100, 200 }));
        }

        [Test]
        public void ReadVariantValueListOfUInt32ReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfUInt32 xmlns="{Ns}">
                <UInt32>1</UInt32>
                <UInt32>2</UInt32>
            </ListOfUInt32>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.TryGet(out ArrayOf<uint> array), Is.True);
            Assert.That(array, Is.EqualTo(new uint[] { 1, 2 }));
        }

        [Test]
        public void ReadVariantValueListOfInt64ReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfInt64 xmlns="{Ns}">
                <Int64>1000</Int64>
                <Int64>2000</Int64>
            </ListOfInt64>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.TryGet(out ArrayOf<long> array), Is.True);
            Assert.That(array, Is.EqualTo(new long[] { 1000, 2000 }));
        }

        [Test]
        public void ReadVariantValueListOfUInt64ReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfUInt64 xmlns="{Ns}">
                <UInt64>1</UInt64>
                <UInt64>2</UInt64>
            </ListOfUInt64>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.TryGet(out ArrayOf<ulong> array), Is.True);
            Assert.That(array, Is.EqualTo(new ulong[] { 1, 2 }));
        }

        [Test]
        public void ReadVariantValueListOfFloatReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfFloat xmlns="{Ns}">
                <Float>1.5</Float>
                <Float>2.5</Float>
            </ListOfFloat>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.TryGet(out ArrayOf<float> array), Is.True);
            Assert.That(array, Is.EqualTo([1.5f, 2.5f]));
        }

        [Test]
        public void ReadVariantValueListOfDoubleReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfDouble xmlns="{Ns}">
                <Double>1.1</Double>
                <Double>2.2</Double>
            </ListOfDouble>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.TryGet(out ArrayOf<double> array), Is.True);
            Assert.That(array, Is.EqualTo([1.1, 2.2]));
        }

        [Test]
        public void ReadVariantValueListOfDateTimeReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfDateTime xmlns="{Ns}">
                <DateTime>2024-01-01T00:00:00Z</DateTime>
            </ListOfDateTime>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<DateTimeUtc> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfGuidReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfGuid xmlns="{Ns}">
                <Guid>
                    <String>12345678-1234-1234-1234-123456789012</String>
                </Guid>
            </ListOfGuid>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<Uuid> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfByteStringReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfByteString xmlns="{Ns}">
                <ByteString>AQID</ByteString>
            </ListOfByteString>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<ByteString> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfXmlElementReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfXmlElement xmlns="{Ns}">
                <XmlElement><Custom>A</Custom></XmlElement>
            </ListOfXmlElement>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<XmlElement> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfNodeIdReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfNodeId xmlns="{Ns}">
                <NodeId>
                    <Identifier>i=1</Identifier>
                </NodeId>
            </ListOfNodeId>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<NodeId> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfExpandedNodeIdReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfExpandedNodeId xmlns="{Ns}">
                <ExpandedNodeId>
                    <Identifier>i=1</Identifier>
                </ExpandedNodeId>
            </ListOfExpandedNodeId>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<ExpandedNodeId> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfStatusCodeReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfStatusCode xmlns="{Ns}">
                <StatusCode>
                    <Code>0</Code>
                </StatusCode>
            </ListOfStatusCode>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<StatusCode> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfQualifiedNameReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfQualifiedName xmlns="{Ns}">
                <QualifiedName>
                    <NamespaceIndex>0</NamespaceIndex>
                    <Name>Test</Name>
                </QualifiedName>
            </ListOfQualifiedName>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<QualifiedName> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfLocalizedTextReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfLocalizedText xmlns="{Ns}">
                <LocalizedText>
                    <Locale>en</Locale>
                    <Text>Test</Text>
                </LocalizedText>
            </ListOfLocalizedText>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<LocalizedText> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfExtensionObjectReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfExtensionObject xmlns="{Ns}">
                <ExtensionObject>
                    <TypeId>
                        <Identifier>i=1</Identifier>
                    </TypeId>
                </ExtensionObject>
            </ListOfExtensionObject>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<ExtensionObject> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfDataValueReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfDataValue xmlns="{Ns}">
                <DataValue>
                    <Value>
                        <Value>
                            <Int32 xmlns="{Ns}">5</Int32>
                        </Value>
                    </Value>
                </DataValue>
            </ListOfDataValue>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<DataValue> array), Is.True);
        }

        [Test]
        public void ReadVariantValueListOfVariantReturnsArray()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfVariant xmlns="{Ns}">
                <Variant>
                    <Value>
                        <Int32 xmlns="{Ns}">99</Int32>
                    </Value>
                </Variant>
            </ListOfVariant>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out ArrayOf<Variant> array), Is.True);
        }

        [Test]
        public void ReadInt32ArrayMissingFieldReturnsEmpty()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<int> result = decoder.ReadInt32Array("ListOfInt32");

            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public void ReadBooleanArrayMissingFieldReturnsEmpty()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<bool> result = decoder.ReadBooleanArray("ListOfBoolean");

            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public void ReadSByteArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfSByte xmlns="{Ns}">
                <SByte>1</SByte>
                <SByte>2</SByte>
            </ListOfSByte>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadSByteArray("ListOfSByte"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadByteArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfByte xmlns="{Ns}">
                <Byte>1</Byte>
                <Byte>2</Byte>
            </ListOfByte>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadByteArray("ListOfByte"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadInt16ArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfInt16 xmlns="{Ns}">
                <Int16>1</Int16>
                <Int16>2</Int16>
            </ListOfInt16>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadInt16Array("ListOfInt16"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadUInt16ArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfUInt16 xmlns="{Ns}">
                <UInt16>1</UInt16>
                <UInt16>2</UInt16>
            </ListOfUInt16>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadUInt16Array("ListOfUInt16"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadInt32ArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfInt32 xmlns="{Ns}">
                <Int32>1</Int32>
                <Int32>2</Int32>
            </ListOfInt32>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadInt32Array("ListOfInt32"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadUInt32ArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfUInt32 xmlns="{Ns}">
                <UInt32>1</UInt32>
                <UInt32>2</UInt32>
            </ListOfUInt32>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadUInt32Array("ListOfUInt32"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadInt64ArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfInt64 xmlns="{Ns}">
                <Int64>1</Int64>
                <Int64>2</Int64>
            </ListOfInt64>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadInt64Array("ListOfInt64"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadUInt64ArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfUInt64 xmlns="{Ns}">
                <UInt64>1</UInt64>
                <UInt64>2</UInt64>
            </ListOfUInt64>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadUInt64Array("ListOfUInt64"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadFloatArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfFloat xmlns="{Ns}">
                <Float>1.0</Float>
                <Float>2.0</Float>
            </ListOfFloat>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadFloatArray("ListOfFloat"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadDoubleArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfDouble xmlns="{Ns}">
                <Double>1.0</Double>
                <Double>2.0</Double>
            </ListOfDouble>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadDoubleArray("ListOfDouble"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadStringArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfString xmlns="{Ns}">
                <String>A</String>
                <String>B</String>
            </ListOfString>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadStringArray("ListOfString"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadDateTimeArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfDateTime xmlns="{Ns}">
                <DateTime>2024-01-01T00:00:00Z</DateTime>
                <DateTime>2024-01-02T00:00:00Z</DateTime>
            </ListOfDateTime>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadDateTimeArray("ListOfDateTime"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadGuidArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfGuid xmlns="{Ns}">
                <Guid><String>12345678-1234-1234-1234-123456789012</String></Guid>
                <Guid><String>aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee</String></Guid>
            </ListOfGuid>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadGuidArray("ListOfGuid"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadNodeIdArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfNodeId xmlns="{Ns}">
                <NodeId><Identifier>i=1</Identifier></NodeId>
                <NodeId><Identifier>i=2</Identifier></NodeId>
            </ListOfNodeId>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadNodeIdArray("ListOfNodeId"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadExpandedNodeIdArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfExpandedNodeId xmlns="{Ns}">
                <ExpandedNodeId><Identifier>i=1</Identifier></ExpandedNodeId>
                <ExpandedNodeId><Identifier>i=2</Identifier></ExpandedNodeId>
            </ListOfExpandedNodeId>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadExpandedNodeIdArray("ListOfExpandedNodeId"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadStatusCodeArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfStatusCode xmlns="{Ns}">
                <StatusCode><Code>0</Code></StatusCode>
                <StatusCode><Code>0</Code></StatusCode>
            </ListOfStatusCode>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadStatusCodeArray("ListOfStatusCode"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadXmlElementArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfXmlElement xmlns="{Ns}">
                <XmlElement><A>1</A></XmlElement>
                <XmlElement><B>2</B></XmlElement>
            </ListOfXmlElement>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadXmlElementArray("ListOfXmlElement"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadQualifiedNameArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfQualifiedName xmlns="{Ns}">
                <QualifiedName><NamespaceIndex>0</NamespaceIndex><Name>A</Name></QualifiedName>
                <QualifiedName><NamespaceIndex>0</NamespaceIndex><Name>B</Name></QualifiedName>
            </ListOfQualifiedName>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadQualifiedNameArray("ListOfQualifiedName"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadLocalizedTextArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfLocalizedText xmlns="{Ns}">
                <LocalizedText><Locale>en</Locale><Text>A</Text></LocalizedText>
                <LocalizedText><Locale>de</Locale><Text>B</Text></LocalizedText>
            </ListOfLocalizedText>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadLocalizedTextArray("ListOfLocalizedText"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadVariantArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfVariant xmlns="{Ns}">
                <Variant><Value><Int32 xmlns="{Ns}">1</Int32></Value></Variant>
                <Variant><Value><Int32 xmlns="{Ns}">2</Int32></Value></Variant>
            </ListOfVariant>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariantArray("ListOfVariant"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadDataValueArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfDataValue xmlns="{Ns}">
                <DataValue><Value><Value><Int32 xmlns="{Ns}">1</Int32></Value></Value></DataValue>
                <DataValue><Value><Value><Int32 xmlns="{Ns}">2</Int32></Value></Value></DataValue>
            </ListOfDataValue>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadDataValueArray("ListOfDataValue"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadExtensionObjectArrayReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ListOfExtensionObject xmlns="{Ns}">
                <ExtensionObject>
                    <TypeId>
                        <Identifier>i=1</Identifier>
                    </TypeId>
                </ExtensionObject>
                <ExtensionObject>
                    <TypeId>
                        <Identifier>i=2</Identifier>
                    </TypeId>
                </ExtensionObject>
            </ListOfExtensionObject>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<ExtensionObject> result = decoder.ReadExtensionObjectArray("ListOfExtensionObject");

            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ReadExtensionObjectArrayThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            const string xml = $"""
            <ListOfExtensionObject xmlns="{Ns}">
                <ExtensionObject><TypeId><Identifier>i=1</Identifier></TypeId></ExtensionObject>
                <ExtensionObject><TypeId><Identifier>i=2</Identifier></TypeId></ExtensionObject>
            </ListOfExtensionObject>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadExtensionObjectArray("ListOfExtensionObject"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadExtensionObjectArrayMissingFieldReturnsEmpty()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<ExtensionObject> result = decoder.ReadExtensionObjectArray("ListOfExtensionObject");

            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public void ReadVariantValueMatrixInt32ReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            // 2x3 matrix of Int32
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions>
                    <Int32>2</Int32>
                    <Int32>3</Int32>
                </Dimensions>
                <Elements>
                    <Int32>1</Int32>
                    <Int32>2</Int32>
                    <Int32>3</Int32>
                    <Int32>4</Int32>
                    <Int32>5</Int32>
                    <Int32>6</Int32>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<int> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixBooleanReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions>
                    <Int32>2</Int32>
                    <Int32>2</Int32>
                </Dimensions>
                <Elements>
                    <Boolean>true</Boolean>
                    <Boolean>false</Boolean>
                    <Boolean>false</Boolean>
                    <Boolean>true</Boolean>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<bool> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixStringReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions>
                    <Int32>1</Int32>
                    <Int32>2</Int32>
                </Dimensions>
                <Elements>
                    <String>A</String>
                    <String>B</String>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<string> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixDoubleReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions>
                    <Int32>2</Int32>
                    <Int32>2</Int32>
                </Dimensions>
                <Elements>
                    <Double>1.1</Double>
                    <Double>2.2</Double>
                    <Double>3.3</Double>
                    <Double>4.4</Double>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<double> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixFloatReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions>
                    <Int32>1</Int32>
                    <Int32>2</Int32>
                </Dimensions>
                <Elements>
                    <Float>1.5</Float>
                    <Float>2.5</Float>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<float> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixSByteReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <SByte>-1</SByte>
                    <SByte>1</SByte>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<sbyte> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixByteReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <Byte>10</Byte>
                    <Byte>20</Byte>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<byte> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixInt16ReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <Int16>100</Int16>
                    <Int16>200</Int16>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<short> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixUInt16ReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <UInt16>100</UInt16>
                    <UInt16>200</UInt16>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<ushort> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixUInt32ReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <UInt32>100</UInt32>
                    <UInt32>200</UInt32>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<uint> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixInt64ReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <Int64>100</Int64>
                    <Int64>200</Int64>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<long> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixUInt64ReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <UInt64>100</UInt64>
                    <UInt64>200</UInt64>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<ulong> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixDateTimeReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <DateTime>2024-01-01T00:00:00Z</DateTime>
                    <DateTime>2024-06-01T00:00:00Z</DateTime>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<DateTimeUtc> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixGuidReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <Guid><String>12345678-1234-1234-1234-123456789012</String></Guid>
                    <Guid><String>aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee</String></Guid>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<Uuid> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixByteStringReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <ByteString>AQID</ByteString>
                    <ByteString>BAUG</ByteString>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<ByteString> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixXmlElementReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <XmlElement><A>1</A></XmlElement>
                    <XmlElement><B>2</B></XmlElement>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<XmlElement> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixNodeIdReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <NodeId><Identifier>i=1</Identifier></NodeId>
                    <NodeId><Identifier>i=2</Identifier></NodeId>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<NodeId> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixExpandedNodeIdReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <ExpandedNodeId><Identifier>i=1</Identifier></ExpandedNodeId>
                    <ExpandedNodeId><Identifier>i=2</Identifier></ExpandedNodeId>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<ExpandedNodeId> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixStatusCodeReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <StatusCode><Code>0</Code></StatusCode>
                    <StatusCode><Code>0</Code></StatusCode>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<StatusCode> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixQualifiedNameReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <QualifiedName><NamespaceIndex>0</NamespaceIndex><Name>A</Name></QualifiedName>
                    <QualifiedName><NamespaceIndex>0</NamespaceIndex><Name>B</Name></QualifiedName>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<QualifiedName> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixLocalizedTextReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <LocalizedText><Locale>en</Locale><Text>A</Text></LocalizedText>
                    <LocalizedText><Locale>de</Locale><Text>B</Text></LocalizedText>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<LocalizedText> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixExtensionObjectReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <ExtensionObject><TypeId><Identifier>i=1</Identifier></TypeId></ExtensionObject>
                    <ExtensionObject><TypeId><Identifier>i=2</Identifier></TypeId></ExtensionObject>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<ExtensionObject> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixDataValueReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <DataValue><Value><Value><Int32 xmlns="{Ns}">1</Int32></Value></Value></DataValue>
                    <DataValue><Value><Value><Int32 xmlns="{Ns}">2</Int32></Value></Value></DataValue>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<DataValue> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixVariantReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <Variant><Value><Int32 xmlns="{Ns}">1</Int32></Value></Variant>
                    <Variant><Value><Int32 xmlns="{Ns}">2</Int32></Value></Variant>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.TryGet(out MatrixOf<Variant> array), Is.True);
        }

        [Test]
        public void ReadVariantValueMatrixUnknownTypeThrows()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions><Int32>1</Int32><Int32>2</Int32></Dimensions>
                <Elements>
                    <InvalidType>1</InvalidType>
                    <InvalidType>2</InvalidType>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariantValue(null, default));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadVariantThrowsWhenNestingLevelExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            // With MaxEncodingNestingLevels = 0, the first CheckAndIncrementNestingLevel
            // passes (0 > 0 = false), but ReadDataValue calls ReadVariant("Value")
            // internally which triggers a second check (1 > 0 = true → throws).
            ctx.MaxEncodingNestingLevels = 0;
            const string xml = $"""
            <Variant xmlns="{Ns}">
                <Value>
                    <DataValue xmlns="{Ns}">
                        <Value>
                            <Value>
                                <Int32 xmlns="{Ns}">1</Int32>
                            </Value>
                        </Value>
                    </DataValue>
                </Value>
            </Variant>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariant("Variant"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadVariantValueWithTypeInfoThrowsWhenNestingLevelExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            // ReadVariantValue(fieldName, typeInfo) calls CheckAndIncrementNestingLevel.
            // With MaxEncodingNestingLevels = 0, the first call passes (0 > 0 = false).
            // We need a second nesting check to trigger. ReadEncodeable inside the
            // variant would do it. Instead, we set max very low and use nested calls:
            // outer ReadVariantValue increments to 1, inner ReadVariant increments
            // check 1 > 0 → fails.
            ctx.MaxEncodingNestingLevels = 0;
            const string xml = $"""
            <Value xmlns="{Ns}">
                <DataValue xmlns="{Ns}">
                    <Value>
                        <Value>
                            <Int32 xmlns="{Ns}">1</Int32>
                        </Value>
                    </Value>
                </DataValue>
            </Value>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariantValue("Value", default));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadSByteOverflowThrowsBadDecodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <SByte xmlns="{Ns}">999</SByte>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadSByte("SByte"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadByteOverflowThrowsBadDecodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Byte xmlns="{Ns}">999</Byte>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadByte("Byte"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadInt32FormatExceptionThrowsBadDecodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Int32 xmlns="{Ns}">not_a_number</Int32>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadInt32("Int32"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadByteStringInvalidBase64ThrowsBadDecodingError()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <ByteString xmlns="{Ns}">!!!not-base64!!!</ByteString>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadByteString("ByteString"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectReturnsEncodeable()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.Factory.AddEncodeableType(typeof(CoverageTestEncodeableWithData));
            const string xml = $"""
            <ExtensionObject xmlns="{Ns}">
                <TypeId>
                    <Identifier>i=88888</Identifier>
                </TypeId>
                <Body>
                    <CoverageTestEncodeableWithData xmlns="{Ns}">
                        <Value>42</Value>
                    </CoverageTestEncodeableWithData>
                </Body>
            </ExtensionObject>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            CoverageTestEncodeableWithData result = decoder.ReadEncodeableAsExtensionObject<CoverageTestEncodeableWithData>("ExtensionObject");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.EqualTo(42));
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectReturnDefaultWhenTypeMismatch()
        {
            ServiceMessageContext ctx = CreateContext();
            // Do not register CoverageTestEncodeableWithData, so type won't match
            const string xml = $"""
            <ExtensionObject xmlns="{Ns}">
                <TypeId>
                    <Identifier>i=1</Identifier>
                </TypeId>
            </ExtensionObject>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            CoverageTestEncodeableWithData result = decoder.ReadEncodeableAsExtensionObject<CoverageTestEncodeableWithData>("ExtensionObject");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadEncodeableArrayAsExtensionObjectsReturnsValues()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.Factory.AddEncodeableType(typeof(CoverageTestEncodeableWithData));
            const string xml = $"""
            <ListOfExtensionObject xmlns="{Ns}">
                <ExtensionObject>
                    <TypeId>
                        <Identifier>i=88888</Identifier>
                    </TypeId>
                    <Body>
                        <CoverageTestEncodeableWithData xmlns="{Ns}">
                            <Value>10</Value>
                        </CoverageTestEncodeableWithData>
                    </Body>
                </ExtensionObject>
                <ExtensionObject>
                    <TypeId>
                        <Identifier>i=88888</Identifier>
                    </TypeId>
                    <Body>
                        <CoverageTestEncodeableWithData xmlns="{Ns}">
                            <Value>20</Value>
                        </CoverageTestEncodeableWithData>
                    </Body>
                </ExtensionObject>
            </ListOfExtensionObject>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<CoverageTestEncodeableWithData> result =
                decoder.ReadEncodeableArrayAsExtensionObjects<CoverageTestEncodeableWithData>("ListOfExtensionObject");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Value, Is.EqualTo(10));
            Assert.That(result[1].Value, Is.EqualTo(20));
        }

        [Test]
        public void ReadEncodeableArrayAsExtensionObjectsThrowsWhenMaxArrayLengthExceeded()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.MaxArrayLength = 1;
            ctx.Factory.AddEncodeableType(typeof(CoverageTestEncodeableWithData));
            const string xml = $"""
            <ListOfExtensionObject xmlns="{Ns}">
                <ExtensionObject>
                    <TypeId><Identifier>i=88888</Identifier></TypeId>
                    <Body>
                        <CoverageTestEncodeableWithData xmlns="{Ns}"><Value>1</Value></CoverageTestEncodeableWithData>
                    </Body>
                </ExtensionObject>
                <ExtensionObject>
                    <TypeId><Identifier>i=88888</Identifier></TypeId>
                    <Body>
                        <CoverageTestEncodeableWithData xmlns="{Ns}"><Value>2</Value></CoverageTestEncodeableWithData>
                    </Body>
                </ExtensionObject>
            </ListOfExtensionObject>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeableArrayAsExtensionObjects<CoverageTestEncodeableWithData>(
                    "ListOfExtensionObject"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadEncodeableMatrixReturnsMatrix()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.Factory.AddEncodeableType(typeof(CoverageTestEncodeableWithData));
            const string xml = $"""
            <Matrix xmlns="{Ns}">
                <Dimensions>
                    <Int32>1</Int32>
                    <Int32>2</Int32>
                </Dimensions>
                <Elements>
                    <CoverageTestEncodeableWithData xmlns="{Ns}">
                        <Value>1</Value>
                    </CoverageTestEncodeableWithData>
                    <CoverageTestEncodeableWithData xmlns="{Ns}">
                        <Value>2</Value>
                    </CoverageTestEncodeableWithData>
                </Elements>
            </Matrix>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            MatrixOf<CoverageTestEncodeableWithData> result =
                decoder.ReadEncodeableMatrix<CoverageTestEncodeableWithData>(
                    "Matrix", new ExpandedNodeId(88888, 0));

            Assert.That(result.IsEmpty, Is.False);
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ReadEncodeableMatrixMissingFieldReturnsDefault()
        {
            ServiceMessageContext ctx = CreateContext();
            ctx.Factory.AddEncodeableType(typeof(CoverageTestEncodeableWithData));
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            MatrixOf<CoverageTestEncodeableWithData> result =
                decoder.ReadEncodeableMatrix<CoverageTestEncodeableWithData>(
                    "Matrix", new ExpandedNodeId(88888, 0));

            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public void ReadVariantValueWithTypeMismatchThrows()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Value xmlns="{Ns}">
                <Int32 xmlns="{Ns}">42</Int32>
            </Value>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Expect String but got Int32
            var stringTypeInfo = new TypeInfo(BuiltInType.String, ValueRanks.Scalar);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariantValue("Value", stringTypeInfo));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("Type mismatch"));
        }

        [Test]
        public void ReadVariantValueWithMatchingTypeInfoReturnsValue()
        {
            ServiceMessageContext ctx = CreateContext();
            const string xml = $"""
            <Value xmlns="{Ns}">
                <Int32 xmlns="{Ns}">42</Int32>
            </Value>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            var int32TypeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);

            Variant result = decoder.ReadVariantValue("Value", int32TypeInfo);

            Assert.That(result.GetInt32(), Is.EqualTo(42));
        }

        [DataContract(Name = "TestEncodeable", Namespace = Namespaces.OpcUaXsd)]
        private sealed class CoverageTestEncodeable : IEncodeable
        {
            public ExpandedNodeId TypeId => ExpandedNodeId.Null;
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return false;
            }

            public object Clone()
            {
                return new CoverageTestEncodeable();
            }
        }

        [DataContract(Name = "CoverageTestEncodeableWithData", Namespace = Namespaces.OpcUaXsd)]
        public sealed class CoverageTestEncodeableWithData : IEncodeable
        {
            public ExpandedNodeId TypeId => new(88888, 0);
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            public int Value { get; private set; }

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32("Value", Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = decoder.ReadInt32("Value");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is CoverageTestEncodeableWithData other && other.Value == Value;
            }

            public object Clone()
            {
                return new CoverageTestEncodeableWithData();
            }
        }

        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            return ServiceMessageContext.CreateEmpty(telemetryContext);
        }
    }
}
