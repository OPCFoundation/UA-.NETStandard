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

using System.Xml;
using Moq;
using Opc.Ua.Tests;
using System;
using System.IO;
using NUnit.Framework;
using System.Runtime.Serialization;

namespace Opc.Ua.Types.Tests.Encoders
{
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class XmlDecoderTests
    {
        [Test]
        public void ConstructorWithReaderValidContextCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));

            // Act
            var decoder = new XmlDecoder(reader, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithReaderNullContextThrowsArgumentNullException()
        {
            // Arrange
            const string xml = "<Root></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));

            // Act & Assert
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => new XmlDecoder(reader, null));
            Assert.That(ex.ParamName, Is.EqualTo("context"));
        }

        [Test]
        public void ConstructorWithReaderNullReaderCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();

            // Act
            var decoder = new XmlDecoder((XmlReader)null, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithOpcUaXmlElementValidContextCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xmlContent = "<Root>Content</Root>";
            var xmlElement = XmlElement.From(xmlContent);

            // Act
            var decoder = new XmlDecoder(xmlElement, messageContext);

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
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => new XmlDecoder(xmlElement, null));
            Assert.That(ex.ParamName, Is.EqualTo("context"));
        }

        [Test]
        public void ConstructorWithOpcUaXmlElementEmptyElementCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            XmlElement xmlElement = XmlElement.Empty;

            // Act
            var decoder = new XmlDecoder(xmlElement, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
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
            var decoder = new XmlDecoder(xmlElement, messageContext);

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
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => new XmlDecoder(xmlElement, null));
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
            var decoder = new XmlDecoder(xmlElement, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithTypeAndReaderValidParametersCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEncodeable xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Value>Test</Value>
            </TestEncodeable>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));

            // Act
            var decoder = new XmlDecoder(typeof(TestEncodeable), reader, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithTypeAndReaderNullTypeCreatesInstance()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root><Value>Test</Value></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));

            // Act
            var decoder = new XmlDecoder(null, reader, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
            Assert.That(decoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithTypeAndReaderWithNamespacePrefixStripsPrefix()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ns:Root xmlns:ns="http://test.namespace">
                <Value>Test</Value>
            </ns:Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));

            // Act
            var decoder = new XmlDecoder(null, reader, messageContext);

            // Assert
            Assert.That(decoder, Is.Not.Null);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(stringTable.Count, Is.EqualTo(0));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(stringTable.Count, Is.EqualTo(0));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(stringTable.Count, Is.EqualTo(0));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            var stringTable = new StringTable();

            // Act
            bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(stringTable.Count, Is.EqualTo(2));
            Assert.That(stringTable.GetString(0), Is.EqualTo("test"));
        }

        [Test]
        public void CloseClosesReaderSuccessfully()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.Close();

            // Assert - After close, reader state should be Closed
            Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
        }

        [Test]
        public void CloseWithCheckEofFalseClosesReader()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.Close(false);

            // Assert
            Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
        }

        [Test]
        public void CloseWithCheckEofTrueAndNodeTypeNoneClosesReader()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act - Reader at initial state (NodeType.None)
            decoder.Close(true);

            // Assert
            Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
        }

        [Test]
        public void CloseWithCheckEofTrueAndNodeTypeElementReadsEndElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root><Child>Value</Child></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            reader.Read(); // Move to Root element
            reader.Read(); // Move to Child element
            reader.Read(); // Move to text
            reader.Read(); // Move to end child

            // Act
            decoder.Close(true);

            // Assert
            Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
        }

        [Test]
        public void CloseWithCheckEofTrueAtEndElementReadsEndElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            reader.Read(); // Move to Root element
            reader.Read(); // Move to EndElement

            // Act
            decoder.Close(true);

            // Assert
            Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
        }

        [Test]
        public void PeekXmlNodeTypeWithNoneReturnsQualifiedName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://test.namespace">
                <Child>Value</Child>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            XmlQualifiedName result = decoder.Peek(XmlNodeType.None);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("Root"));
            Assert.That(result.Namespace, Is.EqualTo("http://test.namespace"));
        }

        [Test]
        public void PeekXmlNodeTypeWithMatchingTypeReturnsQualifiedName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://test.namespace">
                <Child>Value</Child>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            XmlQualifiedName result = decoder.Peek(XmlNodeType.Element);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("Root"));
            Assert.That(result.Namespace, Is.EqualTo("http://test.namespace"));
        }

        [Test]
        public void PeekXmlNodeTypeWithNonMatchingTypeReturnsNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root><Child>Value</Child></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            XmlQualifiedName result = decoder.Peek(XmlNodeType.EndElement);

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void PeekXmlNodeTypeWithTextNodeReturnsQualifiedName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root>TextContent</Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            reader.Read(); // Move to Root
            reader.Read(); // Move to Text

            // Act
            XmlQualifiedName result = decoder.Peek(XmlNodeType.Text);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo(string.Empty));
        }

        [Test]
        public void PeekXmlNodeTypeMovesToContent()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """

            <Root>
                <Child>Value</Child>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            XmlQualifiedName result = decoder.Peek(XmlNodeType.Element);

            // Assert - Should skip whitespace and reach Root element
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("Root"));
        }

        [Test]
        public void PeekXmlNodeTypeWithEmptyNamespaceReturnsQualifiedName()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root><Child>Value</Child></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            XmlQualifiedName result = decoder.Peek(XmlNodeType.Element);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("Root"));
            Assert.That(result.Namespace, Is.EqualTo(string.Empty));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(typeof(TestEncodeable), reader, messageContext);

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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(typeof(TestEncodeable), reader, messageContext);

            // Act
            bool result = decoder.Peek("Other");

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void PeekStringWithNonElementNodeTypeReturnsFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEncodeable xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">TextContent</TestEncodeable>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(typeof(TestEncodeable), reader, messageContext);
            reader.Read(); // Move to Root
            reader.Read(); // Move to Text

            // Act
            bool result = decoder.Peek("Root");

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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(typeof(TestEncodeable), reader, messageContext);

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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(typeof(TestEncodeable), reader, messageContext);

            // Act
            bool result = decoder.Peek("Child");

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void PeekStringAtEndElementReturnsFalse()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEncodeable xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd"></TestEncodeable>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(typeof(TestEncodeable), reader, messageContext);
            reader.Read(); // Move to Root
            reader.Read(); // Move to EndElement

            // Act
            bool result = decoder.Peek("Root");

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void PeekStringWithNestedElementReturnsCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <TestEncodeable xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Child xmlns="http://opcfoundation.org/UA/">
                    <Value>Test</Value>
                </Child>
            </TestEncodeable>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(typeof(TestEncodeable), reader, messageContext);
            decoder.PushNamespace("http://opcfoundation.org/UA/");
            reader.Read(); // Move to Child

            // Act
            bool result = decoder.Peek("Value");

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void ReadStartElementWithNonEmptyElementReadsAndMovesToContent()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root><Child>Value</Child></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.ReadStartElement();

            // Assert - Should be positioned at Child element
            Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
            Assert.That(reader.LocalName, Is.EqualTo("Child"));
        }

        [Test]
        public void ReadStartElementWithEmptyElementReadsElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root/>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.ReadStartElement();

            // Assert - Should be at EOF after reading empty element
            Assert.That(reader.EOF, Is.True);
        }

        [Test]
        public void ReadStartElementWithNestedElementsReadsCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root>
                <Level1>
                    <Level2>Value</Level2>
                </Level1>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.ReadStartElement(); // Read Root

            // Assert
            Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
            Assert.That(reader.LocalName, Is.EqualTo("Level1"));
        }

        [Test]
        public void ReadStartElementWithTextContentMovesToText()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root>TextContent</Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.ReadStartElement();

            // Assert
            Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Text));
            Assert.That(reader.Value, Is.EqualTo("TextContent"));
        }

        [Test]
        public void ReadStartElementWithWhitespaceMovesToContent()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root>
                <Child>Value</Child>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.ReadStartElement();

            // Assert - Should skip whitespace and be at Child element
            Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
            Assert.That(reader.LocalName, Is.EqualTo("Child"));
        }

        [Test]
        public void ReadStartElementWithAttributesReadsElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """<Root attr="value"><Child>Value</Child></Root>""";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.ReadStartElement();

            // Assert
            Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
            Assert.That(reader.LocalName, Is.EqualTo("Child"));
        }

        [Test]
        public void ReadStartElementWithEmptyElementAndAttributes()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """<Root attr="value"/>""";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.ReadStartElement();

            // Assert
            Assert.That(reader.EOF, Is.True);
        }

        [Test]
        [Explicit]
        public void SkipSimpleElementNavigatesToEndElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Target>Content</Target>
                <Next>Value</Next>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            reader.Read(); // Move to Root
            reader.Read(); // Move to Target
            var qname = new XmlQualifiedName("Target", "http://opcfoundation.org/UA/");

            // Act
            decoder.Skip(qname);

            // Assert - Should be positioned at Next element
            Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
            Assert.That(reader.LocalName, Is.EqualTo("Next"));
        }

        [Test]
        public void SkipNestedElementsDecreasesDepth()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://test.ns/">
                <Target xmlns="http://test.ns/">
                    <Child1>Value1</Child1>
                    <Target xmlns="http://test.ns/">
                        <Child2>Value2</Child2>
                    </Target>
                </Target>
                <Next>Value</Next>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            reader.Read(); // Move to Root
            reader.Read();
            reader.Read(); // Move to first Target
            reader.Read();
            var qname = new XmlQualifiedName("Target", "http://test.ns/");

            // Act
            decoder.Skip(qname);

            // Assert - Should skip both Target elements and be at Next
            Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
            Assert.That(reader.LocalName, Is.EqualTo("Next"));
        }

        [Test]
        public void SkipInvalidXmlThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://test.ns/">
                <Target xmlns="http://test.ns/">
                    <Unclosed>
                </Target>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            reader.Read(); // Move to Root
            reader.Read(); // Move to Target
            var qname = new XmlQualifiedName("Target", "http://test.ns/");

            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => decoder.Skip(qname));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(ex.Message, Does.Contain("Skip"));
            Assert.That(ex.Message, Does.Contain("Target"));
        }

        [Test]
        public void SkipElementWithDifferentNamespaceSkipsCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://test.ns/">
                <Target xmlns="http://other.ns/"><Next>Value</Next></Target>
                <Target xmlns="http://test.ns/"><Next>Value</Next></Target>
                <Another/>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            reader.Read(); // Move to Root
            reader.Read();
            reader.Read(); // Move to first Target (different namespace)
            reader.Read();
            reader.Read();
            reader.Read();
            reader.Read();
            reader.Read(); // Move to second Target (matching namespace)
            reader.Read();
            reader.Read();
            var qname = new XmlQualifiedName("Target", "http://test.ns/");

            // Act
            decoder.Skip(qname);

            // Assert - Should skip only matching Target
            Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
            Assert.That(reader.LocalName, Is.EqualTo("Another"));
        }

        [Test]
        public void SkipElementWithComplexContentSkipsCorrectly()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://test.ns/">
                <Target xmlns="http://test.ns/">
                    <Child1>
                        <GrandChild>Value</GrandChild>
                    </Child1>
                    <Child2>Text</Child2>
                </Target>
                <Next>Value</Next>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            reader.Read(); // Move to Root
            reader.Read(); // Move to Target
            reader.Read(); // White space
            reader.Read(); // Now in target
            var qname = new XmlQualifiedName("Target", "http://test.ns/");

            // Act
            decoder.Skip(qname);

            // Assert
            Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
            Assert.That(reader.LocalName, Is.EqualTo("Next"));
        }

        [Test]
        public void ReadVariantContentsNullReturnsNullVariant()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Null xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd"/>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.Null);
        }

        [Test]
        public void ReadVariantContentsBooleanReturnsBoolean()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Boolean xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">true</Boolean>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(true));
        }

        [Test]
        public void ReadVariantContentsSByteReturnsSByte()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <SByte xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-128</SByte>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo((sbyte)-128));
        }

        [Test]
        public void ReadVariantContentsByteReturnsByte()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Byte xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">255</Byte>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo((byte)255));
        }

        [Test]
        public void ReadVariantContentsInt16ReturnsInt16()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Int16 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-32768</Int16>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo((short)-32768));
        }

        [Test]
        public void ReadVariantContentsUInt16ReturnsUInt16()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <UInt16 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">65535</UInt16>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo((ushort)65535));
        }

        [Test]
        public void ReadVariantContentsInt32ReturnsInt32()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Int32 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-2147483648</Int32>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(-2147483648));
        }

        [Test]
        public void ReadVariantContentsUInt32ReturnsUInt32()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <UInt32 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">4294967295</UInt32>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(4294967295u));
        }

        [Test]
        public void ReadVariantContentsInt64ReturnsInt64()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Int64 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-9223372036854775808</Int64>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(-9223372036854775808));
        }

        [Test]
        public void ReadVariantContentsUInt64ReturnsUInt64()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <UInt64 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">18446744073709551615</UInt64>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(18446744073709551615ul));
        }

        [Test]
        public void ReadVariantContentsFloatReturnsFloat()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Float xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">3.14159</Float>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(3.14159f).Within(0.00001f));
        }

        [Test]
        public void ReadVariantContentsDoubleReturnsDouble()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Double xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">3.141592653589793</Double>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(3.141592653589793).Within(0.000000000000001));
        }

        [Test]
        public void ReadVariantContentsStringReturnsString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <String xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">Hello World</String>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo("Hello World"));
        }

        [Test]
        public void ReadVariantContentsDateTimeReturnsDateTime()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <DateTime xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">2024-01-15T12:30:45Z</DateTime>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(new DateTime(2024, 1, 15, 12, 30, 45, DateTimeKind.Utc)));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(new Uuid("12345678-1234-1234-1234-123456789012")));
        }

        [Test]
        public void ReadVariantContentsByteStringReturnsByteString()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <ByteString xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">AQIDBA==</ByteString>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(s_expectedByteArray));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.InstanceOf<XmlElement>());
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(new NodeId(123)));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(new ExpandedNodeId(456)));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(new StatusCode(0x80000000)));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(new QualifiedName("TestName", 1)));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(new LocalizedText("en-US", "Hello")));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(s_expectedBoolArray));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(s_expectedInt32Array));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            Variant result = decoder.ReadVariantValue(null, default);

            // Assert
            Assert.That(result.Value, Is.EqualTo(s_expectedStringArray));
        }

        [Test]
        public void ReadVariantContentsInvalidTypeThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <InvalidType xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">Value</InvalidType>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => decoder.ReadVariantValue(null, default));
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            var typeId = new ExpandedNodeId(123);

            // Act
            object result = decoder.ReadExtensionObjectBody(typeId);

            // Assert
            Assert.That(result, Is.InstanceOf<ByteString>());
            Assert.That((ByteString)result, Is.EqualTo(s_expectedByteArray));
        }

        [Test]
        public void ReadExtensionObjectBodyKnownTypeReturnsEncodeable()
        {
            // Arrange
            var mockFactory = new Mock<IEncodeableFactory>();
            ServiceMessageContext messageContext = CreateMockContext();
            messageContext.Factory = mockFactory.Object;

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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            var typeId = new ExpandedNodeId(123);

            // Act
            object result = decoder.ReadExtensionObjectBody(typeId);

            // Assert
            Assert.That(result, Is.InstanceOf<IEncodeable>());
        }

        [Test]
        public void ReadExtensionObjectBodyUnknownTypeReturnsXmlElement()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            var mockFactory = new Mock<IEncodeableFactory>();
            messageContext.Factory = mockFactory.Object;

            var encodeableType = new Mock<IEncodeableType>();
            encodeableType.SetupGet(x => x.Type).Returns((Type)null);
            IEncodeableType type = encodeableType.Object;
            mockFactory.Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out type))
                .Returns(false);

            const string xml = """
            <CustomElement xmlns="http://test.namespace">
                <Value>Test</Value>
            </CustomElement>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            var typeId = new ExpandedNodeId(999);

            // Act
            object result = decoder.ReadExtensionObjectBody(typeId);

            // Assert
            Assert.That(result, Is.InstanceOf<XmlElement>());
        }

        [Test]
        public void ReadExtensionObjectWhenFieldMissingReturnsNull()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\"/>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            ServiceMessageContext messageContext = CreateMockContext();
            var mockFactory = new Mock<IEncodeableFactory>();
            IEncodeableType type = null;
            mockFactory.Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out type))
                .Returns(false);
            messageContext.Factory = mockFactory.Object;
            const string xml = """
            <TestEncodeableWithData xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Value>1</Value>
            </TestEncodeableWithData>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
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
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeableArray<TestEncodeableWithData>("ListOfTestEncodeableWithData"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadExtensionObjectBodyInvalidXmlThrowsServiceResultException()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            var mockFactory = new Mock<IEncodeableFactory>();
            messageContext.Factory = mockFactory.Object;
            var encodeableType = new Mock<IEncodeableType>();
            encodeableType.SetupGet(x => x.Type).Returns((Type)null);
            IEncodeableType type = encodeableType.Object;
            mockFactory.Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out type))
                .Returns(false);
            const string xml = """
            <InvalidXml>
                <Unclosed>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            var typeId = new ExpandedNodeId(999);

            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => decoder.ReadExtensionObjectBody(typeId));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadExtensionObjectBodyEmptyXmlReturnsXmlElement()
        {
            // Arrange
            var mockFactory = new Mock<IEncodeableFactory>();
            ServiceMessageContext messageContext = CreateMockContext();
            messageContext.Factory = mockFactory.Object;

            var encodeableType = new Mock<IEncodeableType>();
            encodeableType.SetupGet(x => x.Type).Returns((Type)null);
            IEncodeableType type = encodeableType.Object;
            mockFactory.Setup(f => f.TryGetEncodeableType(It.IsAny<ExpandedNodeId>(), out type))
                .Returns(false);

            const string xml = """
            <Empty xmlns="http://test.namespace"/>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            var typeId = new ExpandedNodeId(999);

            // Act
            object result = decoder.ReadExtensionObjectBody(typeId);

            // Assert
            Assert.That(result, Is.InstanceOf<XmlElement>());
        }

        [Test]
        public void DisposeClosesReader()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.Dispose();

            // Assert
            Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
        }

        [Test]
        public void DisposeCanBeCalledMultipleTimes()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = "<Root></Root>";
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);

            // Act
            decoder.Dispose();
            decoder.Dispose();

            // Assert
            Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
        }

        [Test]
        public void DisposeNullReaderDoesNotThrow()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            var decoder = new XmlDecoder((XmlReader)null, messageContext);

            // Act & Assert
            Assert.DoesNotThrow(decoder.Dispose);
        }

        [Test]
        public void DisposeAfterReadingCompletes()
        {
            // Arrange
            ServiceMessageContext messageContext = CreateMockContext();
            const string xml = """
            <Root xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Value>Test</Value>
            </Root>
            """;
            using var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            reader.Read();

            // Act
            decoder.Dispose();

            // Assert
            Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
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
            public ExpandedNodeId TypeId => new ExpandedNodeId(12345, 0);
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
            public ExpandedNodeId TypeId => new ExpandedNodeId(99999, 0);
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
            return new ServiceMessageContext(telemetryContext);
        }

        private static readonly bool[] s_expectedBoolArray = [true, false, true];
        private static readonly int[] s_expectedInt32Array = [1, 2, 3];
        private static readonly string[] s_expectedStringArray = ["First", "Second", "Third"];
        private static readonly byte[] s_expectedByteArray = [1, 2, 3, 4];
    }
}
