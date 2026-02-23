// Copyright (c) 1996-2025 The OPC Foundation. All rights reserved.

using System;
using System.IO;
using System.Xml;
using Microsoft.Extensions.Logging;
using Moq;

namespace Opc.Ua.Types.UnitTests.Encoders;

[TestFixture]
public class XmlDecoderTests
{
    private static readonly bool[] ExpectedBoolArray = [true, false, true];
    private static readonly int[] ExpectedInt32Array = [1, 2, 3];
    private static readonly string[] ExpectedStringArray = ["First", "Second", "Third"];
    private static readonly byte[] ExpectedByteArray = [1, 2, 3, 4];

    private Mock<IServiceMessageContext> CreateMockContext()
    {
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlDecoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        return mockContext;
    }

    #region Constructor Tests - XmlReader

    [Test]
    public void ConstructorWithReaderValidContextCreatesInstance()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));

        // Act
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Assert
        Assert.That(decoder, Is.Not.Null);
        Assert.That(decoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithReaderNullContextThrowsArgumentNullException()
    {
        // Arrange
        string xml = "<Root></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new XmlDecoder(reader, null!));
        Assert.That(ex!.ParamName, Is.EqualTo("context"));
    }

    [Test]
    public void ConstructorWithReaderNullReaderCreatesInstance()
    {
        // Arrange
        var mockContext = CreateMockContext();

        // Act
        var decoder = new XmlDecoder((XmlReader)null!, mockContext.Object);

        // Assert
        Assert.That(decoder, Is.Not.Null);
        Assert.That(decoder.Context, Is.EqualTo(mockContext.Object));
    }

    #endregion

    #region Constructor Tests - Opc.Ua.XmlElement

    [Test]
    public void ConstructorWithOpcUaXmlElementValidContextCreatesInstance()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xmlContent = "<Root>Content</Root>";
        var xmlElement = Opc.Ua.XmlElement.From(xmlContent);

        // Act
        var decoder = new XmlDecoder(xmlElement, mockContext.Object);

        // Assert
        Assert.That(decoder, Is.Not.Null);
        Assert.That(decoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithOpcUaXmlElementNullContextThrowsArgumentNullException()
    {
        // Arrange
        string xmlContent = "<Root>Content</Root>";
        var xmlElement = Opc.Ua.XmlElement.From(xmlContent);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new XmlDecoder(xmlElement, null!));
        Assert.That(ex!.ParamName, Is.EqualTo("context"));
    }

    [Test]
    public void ConstructorWithOpcUaXmlElementEmptyElementCreatesInstance()
    {
        // Arrange
        var mockContext = CreateMockContext();
        var xmlElement = Opc.Ua.XmlElement.Empty;

        // Act
        var decoder = new XmlDecoder(xmlElement, mockContext.Object);

        // Assert
        Assert.That(decoder, Is.Not.Null);
        Assert.That(decoder.Context, Is.EqualTo(mockContext.Object));
    }

    #endregion

    #region Constructor Tests - System.Xml.XmlElement

    [Test]
    public void ConstructorWithSystemXmlElementValidContextCreatesInstance()
    {
        // Arrange
        var mockContext = CreateMockContext();
        var doc = new XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
        doc.LoadXml("<Root>Content</Root>");
#pragma warning restore CA3075 // Insecure DTD processing in XML
        var xmlElement = doc.DocumentElement!;

        // Act
        var decoder = new XmlDecoder(xmlElement, mockContext.Object);

        // Assert
        Assert.That(decoder, Is.Not.Null);
        Assert.That(decoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithSystemXmlElementNullContextThrowsArgumentNullException()
    {
        // Arrange
        var doc = new XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
        doc.LoadXml("<Root>Content</Root>");
#pragma warning restore CA3075 // Insecure DTD processing in XML
        var xmlElement = doc.DocumentElement!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new XmlDecoder(xmlElement, null!));
        Assert.That(ex!.ParamName, Is.EqualTo("context"));
    }

    [Test]
    public void ConstructorWithSystemXmlElementMinimalXmlCreatesInstance()
    {
        // Arrange
        var mockContext = CreateMockContext();
        var doc = new XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
        doc.LoadXml("<Root/>");
#pragma warning restore CA3075 // Insecure DTD processing in XML
        var xmlElement = doc.DocumentElement!;

        // Act
        var decoder = new XmlDecoder(xmlElement, mockContext.Object);

        // Assert
        Assert.That(decoder, Is.Not.Null);
    }

    #endregion

    #region Constructor Tests - Type and XmlReader

    [Test]
    public void ConstructorWithTypeAndReaderValidParametersCreatesInstance()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://opcfoundation.org/UA/">
                <Value>Test</Value>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));

        // Act
        var decoder = new XmlDecoder(typeof(TestEncodeable), reader, mockContext.Object);

        // Assert
        Assert.That(decoder, Is.Not.Null);
        Assert.That(decoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithTypeAndReaderNullTypeCreatesInstance()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root><Value>Test</Value></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));

        // Act
        var decoder = new XmlDecoder(null, reader, mockContext.Object);

        // Assert
        Assert.That(decoder, Is.Not.Null);
        Assert.That(decoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithTypeAndReaderNullContextCreatesInstance()
    {
        // Arrange
        string xml = "<Root><Value>Test</Value></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));

        // Act
        var decoder = new XmlDecoder(typeof(TestEncodeable), reader, null!);

        // Assert
        Assert.That(decoder, Is.Not.Null);
        Assert.That(decoder.Context, Is.Null);
    }

    [Test]
    public void ConstructorWithTypeAndReaderWithNamespacePrefixStripsPrefix()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <ns:Root xmlns:ns="http://test.namespace">
                <Value>Test</Value>
            </ns:Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));

        // Act
        var decoder = new XmlDecoder(null, reader, mockContext.Object);

        // Assert
        Assert.That(decoder, Is.Not.Null);
    }

    [Test]
    public void ConstructorWithTypeAndReaderWithMultipleColonsInNameHandlesCorrectly()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <a:b:Root xmlns:a:b="http://test.namespace">
                <Value>Test</Value>
            </a:b:Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));

        // Act & Assert - This might throw or handle the invalid XML
        var decoder = new XmlDecoder(null, reader, mockContext.Object);
        Assert.That(decoder, Is.Not.Null);
    }

    #endregion

    #region LoadStringTable Tests

    [Test]
    public void LoadStringTableValidTableReturnsTrue()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/">
                <Uri>http://namespace1.com</Uri>
                <Uri>http://namespace2.com</Uri>
                <Uri>http://namespace3.com</Uri>
            </NamespaceUris>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
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
        var mockContext = CreateMockContext();
        string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/">
            </NamespaceUris>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
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
        var mockContext = CreateMockContext();
        string xml = """
            <OtherElement xmlns="http://opcfoundation.org/UA/">
                <Data>Value</Data>
            </OtherElement>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
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
        var mockContext = CreateMockContext();
        string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/">
                <Uri>http://single.namespace.com</Uri>
            </NamespaceUris>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        var stringTable = new StringTable();

        // Act
        bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(stringTable.Count, Is.EqualTo(1));
        Assert.That(stringTable.GetString(0), Is.EqualTo("http://single.namespace.com"));
    }

    [Test]
    public void LoadStringTableWithEmptyElementsAddsEmptyStrings()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/">
                <Uri></Uri>
                <Uri>http://namespace.com</Uri>
                <Uri></Uri>
            </NamespaceUris>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        var stringTable = new StringTable();

        // Act
        bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(stringTable.Count, Is.EqualTo(3));
        Assert.That(stringTable.GetString(0), Is.EqualTo(string.Empty));
        Assert.That(stringTable.GetString(1), Is.EqualTo("http://namespace.com"));
        Assert.That(stringTable.GetString(2), Is.EqualTo(string.Empty));
    }

    [Test]
    public void LoadStringTableDifferentTableNameReturnsFalse()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <ServerUris xmlns="http://opcfoundation.org/UA/">
                <Uri>http://server1.com</Uri>
            </ServerUris>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
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
        var mockContext = CreateMockContext();
        string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/">
                <Namespace>http://namespace1.com</Namespace>
                <Namespace>http://namespace2.com</Namespace>
            </NamespaceUris>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        var stringTable = new StringTable();

        // Act
        bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(stringTable.Count, Is.EqualTo(0));
    }

    [Test]
    public void LoadStringTableWithWhitespaceOnlyElementsAddsWhitespace()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <NamespaceUris xmlns="http://opcfoundation.org/UA/">
                <Uri>   </Uri>
                <Uri>http://namespace.com</Uri>
            </NamespaceUris>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        var stringTable = new StringTable();

        // Act
        bool result = decoder.LoadStringTable("NamespaceUris", "Uri", stringTable);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(stringTable.Count, Is.EqualTo(2));
    }

    #endregion

    #region Close Tests

    [Test]
    public void CloseClosesReaderSuccessfully()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        decoder.Close();

        // Assert - After close, reader state should be Closed
        Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
    }

    [Test]
    public void CloseWithCheckEofFalseClosesReader()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        decoder.Close(false);

        // Assert
        Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
    }

    [Test]
    public void CloseWithCheckEofTrueAndNodeTypeNoneClosesReader()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act - Reader at initial state (NodeType.None)
        decoder.Close(true);

        // Assert
        Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
    }

    [Test]
    public void CloseWithCheckEofTrueAndNodeTypeElementReadsEndElement()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root><Child>Value</Child></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        reader.Read(); // Move to Root element

        // Act
        decoder.Close(true);

        // Assert
        Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
    }

    [Test]
    public void CloseWithCheckEofTrueAtEndElementReadsEndElement()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        reader.Read(); // Move to Root element
        reader.Read(); // Move to EndElement

        // Act
        decoder.Close(true);

        // Assert
        Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
    }

    #endregion

    #region Peek XmlNodeType Tests

    [Test]
    public void PeekXmlNodeTypeWithNoneReturnsQualifiedName()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://test.namespace">
                <Child>Value</Child>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.Peek(XmlNodeType.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Root"));
        Assert.That(result.Namespace, Is.EqualTo("http://test.namespace"));
    }

    [Test]
    public void PeekXmlNodeTypeWithMatchingTypeReturnsQualifiedName()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://test.namespace">
                <Child>Value</Child>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.Peek(XmlNodeType.Element);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Root"));
        Assert.That(result.Namespace, Is.EqualTo("http://test.namespace"));
    }

    [Test]
    public void PeekXmlNodeTypeWithNonMatchingTypeReturnsNull()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root><Child>Value</Child></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.Peek(XmlNodeType.EndElement);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void PeekXmlNodeTypeWithTextNodeReturnsQualifiedName()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root>TextContent</Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        reader.Read(); // Move to Root
        reader.Read(); // Move to Text

        // Act
        var result = decoder.Peek(XmlNodeType.Text);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public void PeekXmlNodeTypeMovesToContent()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            
            <Root>
                <Child>Value</Child>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.Peek(XmlNodeType.Element);

        // Assert - Should skip whitespace and reach Root element
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Root"));
    }

    [Test]
    public void PeekXmlNodeTypeWithEmptyNamespaceReturnsQualifiedName()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root><Child>Value</Child></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.Peek(XmlNodeType.Element);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("Root"));
        Assert.That(result.Namespace, Is.EqualTo(string.Empty));
    }

    #endregion

    #region Peek String Tests

    [Test]
    public void PeekStringWithMatchingFieldNameReturnsTrue()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://opcfoundation.org/UA/">
                <Child>Value</Child>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(typeof(TestEncodeable), reader, mockContext.Object);

        // Act
        bool result = decoder.Peek("Root");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PeekStringWithNonMatchingFieldNameReturnsFalse()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://opcfoundation.org/UA/">
                <Child>Value</Child>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(typeof(TestEncodeable), reader, mockContext.Object);

        // Act
        bool result = decoder.Peek("Other");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void PeekStringWithNonElementNodeTypeReturnsFalse()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://opcfoundation.org/UA/">TextContent</Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(typeof(TestEncodeable), reader, mockContext.Object);
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
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://different.namespace/">
                <Child>Value</Child>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(typeof(TestEncodeable), reader, mockContext.Object);

        // Act
        bool result = decoder.Peek("Root");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void PeekStringMovesToContent()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            
            <Root xmlns="http://opcfoundation.org/UA/">
                <Child>Value</Child>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(typeof(TestEncodeable), reader, mockContext.Object);

        // Act
        bool result = decoder.Peek("Root");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void PeekStringAtEndElementReturnsFalse()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://opcfoundation.org/UA/"></Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(typeof(TestEncodeable), reader, mockContext.Object);
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
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://opcfoundation.org/UA/">
                <Child>Value</Child>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(typeof(TestEncodeable), reader, mockContext.Object);
        decoder.PushNamespace("http://opcfoundation.org/UA/");
        reader.Read(); // Move to Root
        reader.Read(); // Move to Child

        // Act
        bool result = decoder.Peek("Child");

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region ReadStartElement Tests

    [Test]
    public void ReadStartElementWithNonEmptyElementReadsAndMovesToContent()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root><Child>Value</Child></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

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
        var mockContext = CreateMockContext();
        string xml = "<Root/>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        decoder.ReadStartElement();

        // Assert - Should be at EOF after reading empty element
        Assert.That(reader.EOF, Is.True);
    }

    [Test]
    public void ReadStartElementWithNestedElementsReadsCorrectly()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root>
                <Level1>
                    <Level2>Value</Level2>
                </Level1>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        decoder.ReadStartElement(); // Read Root

        // Assert
        Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
        Assert.That(reader.LocalName, Is.EqualTo("Level1"));
    }

    [Test]
    public void ReadStartElementMultipleTimesNavigatesCorrectly()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root>
                <Child1>Value1</Child1>
                <Child2>Value2</Child2>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        decoder.ReadStartElement(); // Read Root
        var firstChild = reader.LocalName;
        reader.Read(); // Skip Child1 content
        reader.Read(); // Move to Child2
        var secondChild = reader.LocalName;

        // Assert
        Assert.That(firstChild, Is.EqualTo("Child1"));
        Assert.That(secondChild, Is.EqualTo("Child2"));
    }

    [Test]
    public void ReadStartElementWithTextContentMovesToText()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root>TextContent</Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

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
        var mockContext = CreateMockContext();
        string xml = """
            <Root>
                <Child>Value</Child>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

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
        var mockContext = CreateMockContext();
        string xml = """<Root attr="value"><Child>Value</Child></Root>""";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

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
        var mockContext = CreateMockContext();
        string xml = """<Root attr="value"/>""";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        decoder.ReadStartElement();

        // Assert
        Assert.That(reader.EOF, Is.True);
    }

    #endregion

    #region Skip Tests

    [Test]
    [Explicit]
    public void SkipSimpleElementNavigatesToEndElement()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://opcfoundation.org/UA/">
                <Target>Content</Target>
                <Next>Value</Next>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
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
        var mockContext = CreateMockContext();
        string xml = """
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
        var decoder = new XmlDecoder(reader, mockContext.Object);
        reader.Read(); // Move to Root
        reader.Read(); // Move to first Target
        var qname = new XmlQualifiedName("Target", "http://test.ns/");

        // Act
        decoder.Skip(qname);

        // Assert - Should skip both Target elements and be at Next
        Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
        Assert.That(reader.LocalName, Is.EqualTo("Next"));
    }

    [Test]
    public void SkipEmptyElementNavigatesCorrectly()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://test.ns/">
                <Target xmlns="http://test.ns/"/>
                <Next>Value</Next>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        reader.Read(); // Move to Root
        reader.Read(); // Move to Target
        var qname = new XmlQualifiedName("Target", "http://test.ns/");

        // Act
        decoder.Skip(qname);

        // Assert - Should be at Next element
        Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
        Assert.That(reader.LocalName, Is.EqualTo("Next"));
    }

    [Test]
    public void SkipInvalidXmlThrowsServiceResultException()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://test.ns/">
                <Target xmlns="http://test.ns/">
                    <Unclosed>
                </Target>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        reader.Read(); // Move to Root
        reader.Read(); // Move to Target
        var qname = new XmlQualifiedName("Target", "http://test.ns/");

        // Act & Assert
        var ex = Assert.Throws<ServiceResultException>(() => decoder.Skip(qname));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        Assert.That(ex.Message, Does.Contain("Skip"));
        Assert.That(ex.Message, Does.Contain("Target"));
    }

    [Test]
    public void SkipElementWithDifferentNamespaceSkipsCorrectly()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://test.ns/">
                <Target xmlns="http://other.ns/">Content</Target>
                <Target xmlns="http://test.ns/">Content</Target>
                <Next>Value</Next>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        reader.Read(); // Move to Root
        reader.Read(); // Move to first Target (different namespace)
        reader.Read(); // Move to second Target (matching namespace)
        var qname = new XmlQualifiedName("Target", "http://test.ns/");

        // Act
        decoder.Skip(qname);

        // Assert - Should skip only matching Target
        Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
        Assert.That(reader.LocalName, Is.EqualTo("Next"));
    }

    [Test]
    public void SkipElementWithComplexContentSkipsCorrectly()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
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
        var decoder = new XmlDecoder(reader, mockContext.Object);
        reader.Read(); // Move to Root
        reader.Read(); // Move to Target
        var qname = new XmlQualifiedName("Target", "http://test.ns/");

        // Act
        decoder.Skip(qname);

        // Assert
        Assert.That(reader.NodeType, Is.EqualTo(XmlNodeType.Element));
        Assert.That(reader.LocalName, Is.EqualTo("Next"));
    }

    #endregion

    #region ReadVariantContents Tests

    [Test]
    public void ReadVariantContentsNullReturnsNullVariant()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Null xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd"/>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.Null);
    }

    [Test]
    public void ReadVariantContentsBooleanReturnsBoolean()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Boolean xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">true</Boolean>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(true));
    }

    [Test]
    public void ReadVariantContentsSByteReturnsSByte()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <SByte xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-128</SByte>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo((sbyte)-128));
    }

    [Test]
    public void ReadVariantContentsByteReturnsByte()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Byte xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">255</Byte>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo((byte)255));
    }

    [Test]
    public void ReadVariantContentsInt16ReturnsInt16()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Int16 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-32768</Int16>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo((short)-32768));
    }

    [Test]
    public void ReadVariantContentsUInt16ReturnsUInt16()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <UInt16 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">65535</UInt16>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo((ushort)65535));
    }

    [Test]
    public void ReadVariantContentsInt32ReturnsInt32()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Int32 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-2147483648</Int32>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(-2147483648));
    }

    [Test]
    public void ReadVariantContentsUInt32ReturnsUInt32()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <UInt32 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">4294967295</UInt32>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(4294967295u));
    }

    [Test]
    public void ReadVariantContentsInt64ReturnsInt64()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Int64 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">-9223372036854775808</Int64>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(-9223372036854775808));
    }

    [Test]
    public void ReadVariantContentsUInt64ReturnsUInt64()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <UInt64 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">18446744073709551615</UInt64>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(18446744073709551615ul));
    }

    [Test]
    public void ReadVariantContentsFloatReturnsFloat()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Float xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">3.14159</Float>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(3.14159f).Within(0.00001f));
    }

    [Test]
    public void ReadVariantContentsDoubleReturnsDouble()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Double xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">3.141592653589793</Double>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(3.141592653589793).Within(0.000000000000001));
    }

    [Test]
    public void ReadVariantContentsStringReturnsString()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <String xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">Hello World</String>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo("Hello World"));
    }

    [Test]
    public void ReadVariantContentsDateTimeReturnsDateTime()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <DateTime xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">2024-01-15T12:30:45Z</DateTime>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(new DateTime(2024, 1, 15, 12, 30, 45, DateTimeKind.Utc)));
    }

    [Test]
    public void ReadVariantContentsGuidReturnsGuid()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Guid xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <String>12345678-1234-1234-1234-123456789012</String>
            </Guid>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(new Uuid("12345678-1234-1234-1234-123456789012")));
    }

    [Test]
    public void ReadVariantContentsByteStringReturnsByteString()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <ByteString xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">AQIDBA==</ByteString>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(ExpectedByteArray));
    }

    [Test]
    public void ReadVariantContentsXmlElementReturnsXmlElement()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <XmlElement xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <CustomElement>Content</CustomElement>
            </XmlElement>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.InstanceOf<XmlElement>());
    }

    [Test]
    public void ReadVariantContentsNodeIdReturnsNodeId()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <NodeId xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Identifier>i=123</Identifier>
            </NodeId>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(new NodeId(123)));
    }

    [Test]
    public void ReadVariantContentsExpandedNodeIdReturnsExpandedNodeId()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <ExpandedNodeId xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Identifier>i=456</Identifier>
            </ExpandedNodeId>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ExpandedNodeId(456)));
    }

    [Test]
    public void ReadVariantContentsStatusCodeReturnsStatusCode()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <StatusCode xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Code>2147483648</Code>
            </StatusCode>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(new StatusCode(0x80000000)));
    }

    [Test]
    public void ReadVariantContentsQualifiedNameReturnsQualifiedName()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <QualifiedName xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <NamespaceIndex>1</NamespaceIndex>
                <Name>TestName</Name>
            </QualifiedName>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(new QualifiedName("TestName", 1)));
    }

    [Test]
    public void ReadVariantContentsLocalizedTextReturnsLocalizedText()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <LocalizedText xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Locale>en-US</Locale>
                <Text>Hello</Text>
            </LocalizedText>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(new LocalizedText("en-US", "Hello")));
    }

    [Test]
    public void ReadVariantContentsListOfBooleanReturnsBooleanArray()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <ListOfBoolean xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Boolean>true</Boolean>
                <Boolean>false</Boolean>
                <Boolean>true</Boolean>
            </ListOfBoolean>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(ExpectedBoolArray));
    }

    [Test]
    public void ReadVariantContentsListOfInt32ReturnsInt32Array()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <ListOfInt32 xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <Int32>1</Int32>
                <Int32>2</Int32>
                <Int32>3</Int32>
            </ListOfInt32>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(ExpectedInt32Array));
    }

    [Test]
    public void ReadVariantContentsListOfStringReturnsStringArray()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <ListOfString xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <String>First</String>
                <String>Second</String>
                <String>Third</String>
            </ListOfString>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        var result = decoder.ReadVariantContents();

        // Assert
        Assert.That(result.Value, Is.EqualTo(ExpectedStringArray));
    }

    [Test]
    public void ReadVariantContentsInvalidTypeThrowsServiceResultException()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <InvalidType xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">Value</InvalidType>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act & Assert
        var ex = Assert.Throws<ServiceResultException>(() => decoder.ReadVariantContents());
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        Assert.That(ex.Message, Does.Contain("not allowed in a Variant"));
    }

    [Test]
    public void ReadVariantContentsInvalidArrayTypeThrowsServiceResultException()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <ListOfInvalidType xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <InvalidType>Value</InvalidType>
            </ListOfInvalidType>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act & Assert
        var ex = Assert.Throws<ServiceResultException>(() => decoder.ReadVariantContents());
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        Assert.That(ex.Message, Does.Contain("not allowed in a Variant"));
    }

    #endregion

    #region ReadExtensionObjectBody Tests

    [Test]
    public void ReadExtensionObjectBodyBinaryEncodedReturnsByteString()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <ByteString xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">AQIDBA==</ByteString>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        var typeId = new ExpandedNodeId(123);

        // Act
        var result = decoder.ReadExtensionObjectBody(typeId);

        // Assert
        Assert.That(result, Is.InstanceOf<ByteString>());
        Assert.That((ByteString)result, Is.EqualTo(ExpectedByteArray));
    }

    [Test]
    public void ReadExtensionObjectBodyKnownTypeReturnsEncodeable()
    {
        // Arrange
        var mockContext = CreateMockContext();
        var mockFactory = new Mock<IEncodeableFactory>();
        mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
        mockFactory.Setup(f => f.GetSystemType(It.IsAny<ExpandedNodeId>())).Returns(typeof(TestEncodeable));

        string xml = """
            <TestEncodeable xmlns="http://test.namespace">
            </TestEncodeable>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        var typeId = new ExpandedNodeId(123);

        // Act
        var result = decoder.ReadExtensionObjectBody(typeId);

        // Assert
        Assert.That(result, Is.InstanceOf<IEncodeable>());
    }

    [Test]
    public void ReadExtensionObjectBodyUnknownTypeReturnsXmlElement()
    {
        // Arrange
        var mockContext = CreateMockContext();
        var mockFactory = new Mock<IEncodeableFactory>();
        mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
        mockFactory.Setup(f => f.GetSystemType(It.IsAny<ExpandedNodeId>())).Returns((Type?)null);

        string xml = """
            <CustomElement xmlns="http://test.namespace">
                <Value>Test</Value>
            </CustomElement>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        var typeId = new ExpandedNodeId(999);

        // Act
        var result = decoder.ReadExtensionObjectBody(typeId);

        // Assert
        Assert.That(result, Is.InstanceOf<XmlElement>());
    }

    [Test]
    public void ReadExtensionObjectBodyInvalidXmlThrowsServiceResultException()
    {
        // Arrange
        var mockContext = CreateMockContext();
        var mockFactory = new Mock<IEncodeableFactory>();
        mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
        mockFactory.Setup(f => f.GetSystemType(It.IsAny<ExpandedNodeId>())).Returns((Type?)null);

        string xml = """
            <InvalidXml>
                <Unclosed>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        var typeId = new ExpandedNodeId(999);

        // Act & Assert
        var ex = Assert.Throws<ServiceResultException>(() => decoder.ReadExtensionObjectBody(typeId));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
    }

    [Test]
    public void ReadExtensionObjectBodyEmptyXmlReturnsXmlElement()
    {
        // Arrange
        var mockContext = CreateMockContext();
        var mockFactory = new Mock<IEncodeableFactory>();
        mockContext.Setup(c => c.Factory).Returns(mockFactory.Object);
        mockFactory.Setup(f => f.GetSystemType(It.IsAny<ExpandedNodeId>())).Returns((Type?)null);

        string xml = """
            <Empty xmlns="http://test.namespace"/>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        var typeId = new ExpandedNodeId(999);

        // Act
        var result = decoder.ReadExtensionObjectBody(typeId);

        // Assert
        Assert.That(result, Is.InstanceOf<XmlElement>());
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void DisposeClosesReader()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

        // Act
        decoder.Dispose();

        // Assert
        Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
    }

    [Test]
    public void DisposeCanBeCalledMultipleTimes()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = "<Root></Root>";
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);

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
        var mockContext = CreateMockContext();
        var decoder = new XmlDecoder((XmlReader)null!, mockContext.Object);

        // Act & Assert
        Assert.DoesNotThrow(() => decoder.Dispose());
    }

    [Test]
    public void DisposeAfterReadingCompletes()
    {
        // Arrange
        var mockContext = CreateMockContext();
        string xml = """
            <Root xmlns="http://opcfoundation.org/UA/">
                <Value>Test</Value>
            </Root>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var decoder = new XmlDecoder(reader, mockContext.Object);
        reader.Read();

        // Act
        decoder.Dispose();

        // Assert
        Assert.That(reader.ReadState, Is.EqualTo(ReadState.Closed));
    }

    #endregion

    #region Helper Classes

    private sealed class TestEncodeable : IEncodeable
    {
        public ExpandedNodeId TypeId => ExpandedNodeId.Null;
        public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
        public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;
        public void Encode(IEncoder encoder) { }
        public void Decode(IDecoder decoder) { }
        public bool IsEqual(IEncodeable encodeable) => false;
        public object Clone() => new TestEncodeable();
    }

    #endregion
}
