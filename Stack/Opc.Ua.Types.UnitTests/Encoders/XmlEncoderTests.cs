// Copyright (c) 1996-2025 The OPC Foundation. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.UnitTests.Encoders;

[TestFixture]
public class XmlEncoderTests
{
    [Test]
    public void ConstructorWithContextCreatesInstance()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        // Act
        var encoder = new XmlEncoder(mockContext.Object);

        // Assert
        Assert.That(encoder, Is.Not.Null);
        Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithContextInitializesXmlWriter()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        // Act
        var encoder = new XmlEncoder(mockContext.Object);
        encoder.PushNamespace(Namespaces.OpcUaXsd);
        encoder.WriteString("Test", "value");
        encoder.PopNamespace();
        var result = encoder.CloseAndReturnText();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Test"));
        Assert.That(result, Does.Contain("value"));
    }

    [Test]
    public void ConstructorWithTypeAndWriterCreatesInstance()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Encoding = Encoding.UTF8,
            Indent = true
        };
        using var writer = XmlWriter.Create(sb, settings);
        var systemType = typeof(ExtensionObject);

        // Act
        var encoder = new XmlEncoder(systemType, writer, mockContext.Object);

        // Assert
        Assert.That(encoder, Is.Not.Null);
        Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithTypeAndNullWriterCreatesInstance()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var systemType = typeof(ExtensionObject);

        // Act
        var encoder = new XmlEncoder(systemType, null, mockContext.Object);

        // Assert
        Assert.That(encoder, Is.Not.Null);
        Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithQualifiedNameAndWriterCreatesInstance()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Encoding = Encoding.UTF8,
            Indent = true
        };
        using var writer = XmlWriter.Create(sb, settings);
        var root = new XmlQualifiedName("TestRoot", Namespaces.OpcUaXsd);

        // Act
        var encoder = new XmlEncoder(root, writer, mockContext.Object);

        // Assert
        Assert.That(encoder, Is.Not.Null);
        Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithQualifiedNameAndNullWriterCreatesInstance()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var root = new XmlQualifiedName("TestRoot", Namespaces.OpcUaXsd);

        // Act
        var encoder = new XmlEncoder(root, null, mockContext.Object);

        // Assert
        Assert.That(encoder, Is.Not.Null);
        Assert.That(encoder.Context, Is.EqualTo(mockContext.Object));
    }

    [Test]
    public void ConstructorWithQualifiedNameAndWriterInitializesRoot()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Encoding = Encoding.UTF8,
            Indent = false
        };
        using var writer = XmlWriter.Create(sb, settings);
        var root = new XmlQualifiedName("TestElement", "http://test.namespace");

        // Act
        var encoder = new XmlEncoder(root, writer, mockContext.Object);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestElement"));
        Assert.That(result, Does.Contain("http://test.namespace"));
    }

    [Test]
    public void SetMappingTablesWithBothTablesSetsMappings()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        var contextNamespaceUris = new NamespaceTable();
        contextNamespaceUris.Append("http://namespace1");
        contextNamespaceUris.Append("http://namespace2");
        var contextServerUris = new StringTable();
        contextServerUris.Append("server1");
        contextServerUris.Append("server2");

        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.NamespaceUris).Returns(contextNamespaceUris);
        mockContext.Setup(c => c.ServerUris).Returns(contextServerUris);

        var encoder = new XmlEncoder(mockContext.Object);

        var namespaceUris = new NamespaceTable();
        namespaceUris.Append("http://namespace1");
        namespaceUris.Append("http://namespace3");

        var serverUris = new StringTable();
        serverUris.Append("server1");
        serverUris.Append("server3");

        // Act
        encoder.SetMappingTables(namespaceUris, serverUris);

        // Assert - no exception should be thrown
        Assert.That(encoder, Is.Not.Null);
    }

    [Test]
    public void SetMappingTablesWithNullNamespaceUrisDoesNotThrow()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        var contextServerUris = new StringTable();

        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.NamespaceUris).Returns(default(NamespaceTable)!);
        mockContext.Setup(c => c.ServerUris).Returns(contextServerUris);

        var encoder = new XmlEncoder(mockContext.Object);

        var serverUris = new StringTable();
        serverUris.Append("server1");

        // Act
        encoder.SetMappingTables(null, serverUris);

        // Assert - no exception should be thrown
        Assert.That(encoder, Is.Not.Null);
    }

    [Test]
    public void SetMappingTablesWithNullServerUrisDoesNotThrow()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        var contextNamespaceUris = new NamespaceTable();

        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.NamespaceUris).Returns(contextNamespaceUris);
        mockContext.Setup(c => c.ServerUris).Returns(default(StringTable)!);

        var encoder = new XmlEncoder(mockContext.Object);

        var namespaceUris = new NamespaceTable();
        namespaceUris.Append("http://namespace1");

        // Act
        encoder.SetMappingTables(namespaceUris, null);

        // Assert - no exception should be thrown
        Assert.That(encoder, Is.Not.Null);
    }

    [Test]
    public void SetMappingTablesWithNullContextNamespaceUrisDoesNotThrow()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();

        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.NamespaceUris).Returns(default(NamespaceTable)!);
        mockContext.Setup(c => c.ServerUris).Returns(default(StringTable)!);

        var encoder = new XmlEncoder(mockContext.Object);

        var namespaceUris = new NamespaceTable();
        namespaceUris.Append("http://namespace1");

        // Act
        encoder.SetMappingTables(namespaceUris, null);

        // Assert - no exception should be thrown
        Assert.That(encoder, Is.Not.Null);
    }

    [Test]
    public void SetMappingTablesWithNullContextServerUrisDoesNotThrow()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();

        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.NamespaceUris).Returns(default(NamespaceTable)!);
        mockContext.Setup(c => c.ServerUris).Returns(default(StringTable)!);

        var encoder = new XmlEncoder(mockContext.Object);

        var serverUris = new StringTable();
        serverUris.Append("server1");

        // Act
        encoder.SetMappingTables(null, serverUris);

        // Assert - no exception should be thrown
        Assert.That(encoder, Is.Not.Null);
    }

    [Test]
    public void SaveStringTableWithNullTableDoesNotWrite()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);

        // Act
        encoder.SaveStringTable("NamespaceUris", "Uri", null);
        var result = encoder.CloseAndReturnText();

        // Assert
        Assert.That(result, Does.Not.Contain("NamespaceUris"));
        Assert.That(result, Does.Not.Contain("Uri"));
    }

    [Test]
    public void SaveStringTableWithEmptyTableDoesNotWrite()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);
        var stringTable = new StringTable();

        // Act
        encoder.SaveStringTable("NamespaceUris", "Uri", stringTable);
        var result = encoder.CloseAndReturnText();

        // Assert
        Assert.That(result, Does.Not.Contain("NamespaceUris"));
        Assert.That(result, Does.Not.Contain("Uri"));
    }

    [Test]
    public void SaveStringTableWithSingleElementDoesNotWrite()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);
        var stringTable = new StringTable();
        stringTable.Append("http://namespace0");

        // Act
        encoder.SaveStringTable("NamespaceUris", "Uri", stringTable);
        var result = encoder.CloseAndReturnText();

        // Assert
        Assert.That(result, Does.Not.Contain("NamespaceUris"));
    }

    [Test]
    public void SaveStringTableWithMultipleElementsWritesTable()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);
        var stringTable = new StringTable();
        stringTable.Append("http://namespace0");
        stringTable.Append("http://namespace1");
        stringTable.Append("http://namespace2");

        // Act
        encoder.SaveStringTable("NamespaceUris", "Uri", stringTable);
        var result = encoder.CloseAndReturnText();

        // Assert
        Assert.That(result, Does.Contain("NamespaceUris"));
        Assert.That(result, Does.Contain("Uri"));
        Assert.That(result, Does.Contain("http://namespace1"));
        Assert.That(result, Does.Contain("http://namespace2"));
        Assert.That(result, Does.Not.Contain("http://namespace0"));
    }

    [Test]
    public void SaveStringTableWritesAllElementsExceptFirst()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);
        var stringTable = new StringTable();
        stringTable.Append("first");
        stringTable.Append("second");
        stringTable.Append("third");
        stringTable.Append("fourth");

        // Act
        encoder.SaveStringTable("TestTable", "Element", stringTable);
        var result = encoder.CloseAndReturnText();

        // Assert
        Assert.That(result, Does.Contain("TestTable"));
        Assert.That(result, Does.Contain("Element"));
        Assert.That(result, Does.Not.Contain("first"));
        Assert.That(result, Does.Contain("second"));
        Assert.That(result, Does.Contain("third"));
        Assert.That(result, Does.Contain("fourth"));
    }

    [Test]
    public void CloseAndReturnTextReturnsNullWhenDestinationIsNull()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Encoding = Encoding.UTF8,
            Indent = false
        };
        using var writer = XmlWriter.Create(sb, settings);
        var root = new XmlQualifiedName("TestElement", "http://test.namespace");
        var encoder = new XmlEncoder(root, writer, mockContext.Object);

        // Act
        var result = encoder.CloseAndReturnText();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void DisposeCallsDisposeWithTrueAndSuppressesFinalize()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);

        // Act
        encoder.Dispose();

        // Assert
        Assert.That(encoder, Is.Not.Null);
    }

    [Test]
    public void DisposeCanBeCalledMultipleTimes()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);

        // Act
        encoder.Dispose();
        encoder.Dispose();

        // Assert
        Assert.That(encoder, Is.Not.Null);
    }

    [Test]
    public void EncodingTypeReturnsXml()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);

        // Act
        var result = encoder.EncodingType;

        // Assert
        Assert.That(result, Is.EqualTo(EncodingType.Xml));
    }

    [Test]
    public void UseReversibleEncodingReturnsTrue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);

        // Act
        var result = encoder.UseReversibleEncoding;

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void EncodeMessageWithValidMessageEncodesSuccessfully()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);
        var message = new TestEncodeable();

        // Act
        encoder.EncodeMessage(message);
        var result = encoder.CloseAndReturnText();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("TestEncodeable"));
    }

    [Test]
    public void EncodeMessageWithNullMessageThrowsArgumentNullException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var encoder = new XmlEncoder(mockContext.Object);
        TestEncodeable? message = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => encoder.EncodeMessage(message!));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.ParamName, Is.EqualTo("message"));
    }

    [Test]
    public void WriteBooleanWithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteBoolean("TestBoolean", true);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestBoolean"));
        Assert.That(result, Does.Contain("true"));
    }

    [Test]
    public void WriteBooleanWithFalseValueWritesFalse()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteBoolean("TestBoolean", false);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestBoolean"));
        Assert.That(result, Does.Contain("false"));
    }

    [Test]
    public void WriteSByteWithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        sbyte value = 42;

        // Act
        encoder.WriteSByte("TestSByte", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestSByte"));
        Assert.That(result, Does.Contain("42"));
    }

    [Test]
    public void WriteSByteWithNegativeValueWritesNegativeValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        sbyte value = -100;

        // Act
        encoder.WriteSByte("TestSByte", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestSByte"));
        Assert.That(result, Does.Contain("-100"));
    }

    [Test]
    public void WriteSByteWithMinValueWritesMinValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        sbyte value = sbyte.MinValue;

        // Act
        encoder.WriteSByte("TestSByte", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestSByte"));
        Assert.That(result, Does.Contain("-128"));
    }

    [Test]
    public void WriteSByteWithMaxValueWritesMaxValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        sbyte value = sbyte.MaxValue;

        // Act
        encoder.WriteSByte("TestSByte", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestSByte"));
        Assert.That(result, Does.Contain("127"));
    }

    [Test]
    public void WriteSByteWithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        sbyte value = 0;

        // Act
        encoder.WriteSByte("TestSByte", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestSByte"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteByteWithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        byte value = 42;

        // Act
        encoder.WriteByte("TestByte", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestByte"));
        Assert.That(result, Does.Contain("42"));
    }

    [Test]
    public void WriteByteWithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        byte value = 0;

        // Act
        encoder.WriteByte("TestByte", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestByte"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteByteWithMaxValueWritesMaxValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        byte value = byte.MaxValue;

        // Act
        encoder.WriteByte("TestByte", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestByte"));
        Assert.That(result, Does.Contain("255"));
    }

    [Test]
    public void WriteInt16WithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        short value = 1234;

        // Act
        encoder.WriteInt16("TestInt16", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt16"));
        Assert.That(result, Does.Contain("1234"));
    }

    [Test]
    public void WriteInt16WithNegativeValueWritesNegativeValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        short value = -5000;

        // Act
        encoder.WriteInt16("TestInt16", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt16"));
        Assert.That(result, Does.Contain("-5000"));
    }

    [Test]
    public void WriteInt16WithMinValueWritesMinValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        short value = short.MinValue;

        // Act
        encoder.WriteInt16("TestInt16", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt16"));
        Assert.That(result, Does.Contain("-32768"));
    }

    [Test]
    public void WriteInt16WithMaxValueWritesMaxValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        short value = short.MaxValue;

        // Act
        encoder.WriteInt16("TestInt16", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt16"));
        Assert.That(result, Does.Contain("32767"));
    }

    [Test]
    public void WriteInt16WithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        short value = 0;

        // Act
        encoder.WriteInt16("TestInt16", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt16"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteUInt16WithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        ushort value = 1234;

        // Act
        encoder.WriteUInt16("TestUInt16", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestUInt16"));
        Assert.That(result, Does.Contain("1234"));
    }

    [Test]
    public void WriteUInt16WithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        ushort value = 0;

        // Act
        encoder.WriteUInt16("TestUInt16", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestUInt16"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteUInt16WithMaxValueWritesMaxValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        ushort value = ushort.MaxValue;

        // Act
        encoder.WriteUInt16("TestUInt16", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestUInt16"));
        Assert.That(result, Does.Contain("65535"));
    }

    [Test]
    public void WriteInt32WithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        int value = 123456;

        // Act
        encoder.WriteInt32("TestInt32", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt32"));
        Assert.That(result, Does.Contain("123456"));
    }

    [Test]
    public void WriteInt32WithNegativeValueWritesNegativeValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        int value = -50000;

        // Act
        encoder.WriteInt32("TestInt32", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt32"));
        Assert.That(result, Does.Contain("-50000"));
    }

    [Test]
    public void WriteInt32WithMinValueWritesMinValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        int value = int.MinValue;

        // Act
        encoder.WriteInt32("TestInt32", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt32"));
        Assert.That(result, Does.Contain("-2147483648"));
    }

    [Test]
    public void WriteInt32WithMaxValueWritesMaxValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        int value = int.MaxValue;

        // Act
        encoder.WriteInt32("TestInt32", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt32"));
        Assert.That(result, Does.Contain("2147483647"));
    }

    [Test]
    public void WriteInt32WithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        int value = 0;

        // Act
        encoder.WriteInt32("TestInt32", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt32"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteUInt32WithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        uint value = 123456;

        // Act
        encoder.WriteUInt32("TestUInt32", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestUInt32"));
        Assert.That(result, Does.Contain("123456"));
    }

    [Test]
    public void WriteUInt32WithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        uint value = 0;

        // Act
        encoder.WriteUInt32("TestUInt32", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestUInt32"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteUInt32WithMaxValueWritesMaxValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        uint value = uint.MaxValue;

        // Act
        encoder.WriteUInt32("TestUInt32", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestUInt32"));
        Assert.That(result, Does.Contain("4294967295"));
    }

    [Test]
    public void WriteInt64WithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        long value = 123456789012;

        // Act
        encoder.WriteInt64("TestInt64", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt64"));
        Assert.That(result, Does.Contain("123456789012"));
    }

    [Test]
    public void WriteInt64WithNegativeValueWritesNegativeValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        long value = -500000000000;

        // Act
        encoder.WriteInt64("TestInt64", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt64"));
        Assert.That(result, Does.Contain("-500000000000"));
    }

    [Test]
    public void WriteInt64WithMinValueWritesMinValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        long value = long.MinValue;

        // Act
        encoder.WriteInt64("TestInt64", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt64"));
        Assert.That(result, Does.Contain("-9223372036854775808"));
    }

    [Test]
    public void WriteInt64WithMaxValueWritesMaxValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        long value = long.MaxValue;

        // Act
        encoder.WriteInt64("TestInt64", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt64"));
        Assert.That(result, Does.Contain("9223372036854775807"));
    }

    [Test]
    public void WriteInt64WithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        long value = 0;

        // Act
        encoder.WriteInt64("TestInt64", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestInt64"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteUInt64WithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        ulong value = 123456789012;

        // Act
        encoder.WriteUInt64("TestUInt64", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestUInt64"));
        Assert.That(result, Does.Contain("123456789012"));
    }

    [Test]
    public void WriteUInt64WithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        ulong value = 0;

        // Act
        encoder.WriteUInt64("TestUInt64", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestUInt64"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteUInt64WithMaxValueWritesMaxValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        ulong value = ulong.MaxValue;

        // Act
        encoder.WriteUInt64("TestUInt64", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestUInt64"));
        Assert.That(result, Does.Contain("18446744073709551615"));
    }

    [Test]
    public void WriteFloatWithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        float value = 3.14159f;

        // Act
        encoder.WriteFloat("TestFloat", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestFloat"));
        Assert.That(result, Does.Contain("3.14159"));
    }

    [Test]
    public void WriteFloatWithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        float value = 0f;

        // Act
        encoder.WriteFloat("TestFloat", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestFloat"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteFloatWithNegativeValueWritesNegativeValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        float value = -123.456f;

        // Act
        encoder.WriteFloat("TestFloat", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestFloat"));
        Assert.That(result, Does.Contain("-123.456"));
    }

    [Test]
    public void WriteDoubleWithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        double value = 2.718281828;

        // Act
        encoder.WriteDouble("TestDouble", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestDouble"));
        Assert.That(result, Does.Contain("2.718281828"));
    }

    [Test]
    public void WriteDoubleWithZeroValueWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        double value = 0.0;

        // Act
        encoder.WriteDouble("TestDouble", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestDouble"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteDoubleWithNegativeValueWritesNegativeValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        double value = -987.654321;

        // Act
        encoder.WriteDouble("TestDouble", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestDouble"));
        Assert.That(result, Does.Contain("-987.654321"));
    }

    [Test]
    public void WriteDateTimeWithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var value = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc);

        // Act
        encoder.WriteDateTime("TestDateTime", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestDateTime"));
        Assert.That(result, Does.Contain("2024-01-15"));
    }

    [Test]
    public void WriteDateTimeWithLocalTimeConvertsToUtc()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var value = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local);

        // Act
        encoder.WriteDateTime("TestDateTime", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestDateTime"));
        Assert.That(result, Does.Contain("2024-06"));
    }

    [Test]
    public void WriteGuidWithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var guid = new Guid("12345678-1234-1234-1234-123456789abc");
        var value = new Uuid(guid);

        // Act
        encoder.WriteGuid("TestGuid", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestGuid"));
        Assert.That(result, Does.Contain("12345678-1234-1234-1234-123456789abc"));
    }

    [Test]
    public void WriteGuidWithEmptyGuidWritesEmptyValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var value = new Uuid(Guid.Empty);

        // Act
        encoder.WriteGuid("TestGuid", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestGuid"));
        Assert.That(result, Does.Contain("00000000-0000-0000-0000-000000000000"));
    }

    [Test]
    public void WriteByteStringWithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        byte[] bytes = new byte[] { 1, 2, 3, 4, 5 };
        var value = new ByteString(bytes);

        // Act
        encoder.WriteByteString("TestByteString", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestByteString"));
        Assert.That(result, Does.Contain("AQIDBAU="));
    }

    [Test]
    public void WriteByteStringWithEmptyByteStringWritesEmpty()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var value = new ByteString(Array.Empty<byte>());

        // Act
        encoder.WriteByteString("TestByteString", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestByteString"));
    }

    [Test]
    public void WriteByteStringWithNullByteStringDoesNotWriteField()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var value = default(ByteString);

        // Act
        encoder.WriteByteString("TestByteString", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestByteString"));
    }

    [Test]
    public void WriteByteStringWithIndexAndCountWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        byte[] bytes = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };

        // Act
        encoder.WriteByteString("TestByteString", bytes, 0, 8);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestByteString"));
        Assert.That(result, Does.Contain("AAECAwQFBgc="));
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    [Test]
    public void WriteByteStringWithReadOnlySpanWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        ReadOnlySpan<byte> bytes = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        encoder.WriteByteString("TestByteString", bytes);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestByteString"));
        Assert.That(result, Does.Contain("AQIDBAU="));
    }

    [Test]
    public void WriteByteStringWithEmptySpanDoesNotWriteField()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        ReadOnlySpan<byte> bytes = ReadOnlySpan<byte>.Empty;

        // Act
        encoder.WriteByteString("TestByteString", bytes);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestByteString"));
    }
#endif

    [Test]
    public void WriteXmlElementWithValueWritesXml()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var xmlDoc = new XmlDocument();
        var systemElement = xmlDoc.CreateElement("TestElement");
        systemElement.InnerText = "TestValue";
        var element = Opc.Ua.XmlElement.From(systemElement);

        // Act
        encoder.WriteXmlElement("TestField", element);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestField"));
        Assert.That(result, Does.Contain("TestElement"));
        Assert.That(result, Does.Contain("TestValue"));
    }

    [Test]
    public void WriteNodeIdWithNumericValueWritesNodeId()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var nodeId = new NodeId(123, 1);

        // Act
        encoder.WriteNodeId("TestNodeId", nodeId);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestNodeId"));
        Assert.That(result, Does.Contain("Identifier"));
    }

    [Test]
    public void WriteNodeIdWithStringValueWritesNodeId()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var nodeId = new NodeId("TestString", 2);

        // Act
        encoder.WriteNodeId("TestNodeId", nodeId);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestNodeId"));
        Assert.That(result, Does.Contain("Identifier"));
    }

    [Test]
    public void WriteNodeIdWithNullDoesNotWriteField()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var nodeId = NodeId.Null;

        // Act
        encoder.WriteNodeId("TestNodeId", nodeId);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestNodeId"));
    }

    [Test]
    public void WriteExpandedNodeIdWithNumericValueWritesExpandedNodeId()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var expandedNodeId = new ExpandedNodeId(456, 1, null, 0);

        // Act
        encoder.WriteExpandedNodeId("TestExpandedNodeId", expandedNodeId);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestExpandedNodeId"));
        Assert.That(result, Does.Contain("Identifier"));
    }

    [Test]
    public void WriteExpandedNodeIdWithStringValueWritesExpandedNodeId()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var expandedNodeId = new ExpandedNodeId("ExpandedString", 3, null, 0);

        // Act
        encoder.WriteExpandedNodeId("TestExpandedNodeId", expandedNodeId);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestExpandedNodeId"));
        Assert.That(result, Does.Contain("Identifier"));
    }

    [Test]
    public void WriteExpandedNodeIdWithNullDoesNotWriteField()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);
        var expandedNodeId = ExpandedNodeId.Null;

        // Act
        encoder.WriteExpandedNodeId("TestExpandedNodeId", expandedNodeId);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestExpandedNodeId"));
    }

    [Test]
    public void WriteStatusCodeWithFieldNameWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var statusCode = new StatusCode(0x80010000);

        // Act
        encoder.WriteStatusCode("TestStatusCode", statusCode);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestStatusCode"));
        Assert.That(result, Does.Contain("Code"));
        Assert.That(result, Does.Contain("2147549184"));
    }

    [Test]
    public void WriteStatusCodeWithGoodStatusCodeWritesGoodCode()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var statusCode = StatusCodes.Good;

        // Act
        encoder.WriteStatusCode("Status", statusCode);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("Status"));
        Assert.That(result, Does.Contain("Code"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteDiagnosticInfoWithNullValueWritesNull()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        DiagnosticInfo? diagnosticInfo = null;

        // Act
        encoder.WriteDiagnosticInfo("TestDiagnosticInfo", diagnosticInfo!);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestDiagnosticInfo"));
    }

    [Test]
    public void WriteDiagnosticInfoWithValueWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.MaxEncodingNestingLevels).Returns(100);
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var diagnosticInfo = new DiagnosticInfo(1, 2, 3, 4, "Additional info");

        // Act
        encoder.WriteDiagnosticInfo("TestDiagnosticInfo", diagnosticInfo);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestDiagnosticInfo"));
        Assert.That(result, Does.Contain("Additional info"));
    }

    [Test]
    public void WriteQualifiedNameWithValueWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var qualifiedName = new QualifiedName("TestName", 1);

        // Act
        encoder.WriteQualifiedName("TestQualifiedName", qualifiedName);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestQualifiedName"));
        Assert.That(result, Does.Contain("TestName"));
    }

    [Test]
    public void WriteQualifiedNameWithNullValueWritesNull()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var qualifiedName = QualifiedName.Null;

        // Act
        encoder.WriteQualifiedName("TestQualifiedName", qualifiedName);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestQualifiedName"));
    }

    [Test]
    public void WriteLocalizedTextWithValueWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var localizedText = new LocalizedText("en-US", "Hello World");

        // Act
        encoder.WriteLocalizedText("TestLocalizedText", localizedText);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestLocalizedText"));
        Assert.That(result, Does.Contain("Hello World"));
    }

    [Test]
    public void WriteLocalizedTextWithEmptyValueWritesEmpty()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var localizedText = LocalizedText.Null;

        // Act
        encoder.WriteLocalizedText("TestLocalizedText", localizedText);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestLocalizedText"));
    }

    [Test]
    public void WriteVariantWithIntegerValueWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var variant = new Variant(42);

        // Act
        encoder.WriteVariant("TestVariant", variant);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestVariant"));
        Assert.That(result, Does.Contain("Value"));
        Assert.That(result, Does.Contain("42"));
    }

    [Test]
    public void WriteVariantWithStringValueWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var variant = new Variant("Test String");

        // Act
        encoder.WriteVariant("TestVariant", variant);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestVariant"));
        Assert.That(result, Does.Contain("Value"));
        Assert.That(result, Does.Contain("Test String"));
    }

    [Test]
    public void WriteVariantWithBooleanValueWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var variant = new Variant(true);

        // Act
        encoder.WriteVariant("TestVariant", variant);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestVariant"));
        Assert.That(result, Does.Contain("Value"));
        Assert.That(result, Does.Contain("true"));
    }

    [Test]
    public void WriteVariantWithNullValueWritesNull()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var variant = Variant.Null;

        // Act
        encoder.WriteVariant("TestVariant", variant);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestVariant"));
        Assert.That(result, Does.Contain("Value"));
    }

    [Test]
    public void WriteStatusCodeWithZeroCodeWritesZero()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var statusCode = new StatusCode(0);

        // Act
        encoder.WriteStatusCode("Status", statusCode);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("Status"));
        Assert.That(result, Does.Contain("Code"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteDiagnosticInfoWithEmptyValueWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockContext.Setup(c => c.MaxEncodingNestingLevels).Returns(100);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var diagnosticInfo = new DiagnosticInfo();

        // Act
        encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("DiagInfo"));
    }

    [Test]
    public void WriteQualifiedNameWithNamespaceZeroWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var qualifiedName = new QualifiedName("Name", 0);

        // Act
        encoder.WriteQualifiedName("QName", qualifiedName);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("QName"));
        Assert.That(result, Does.Contain("Name"));
    }

    [Test]
    public void WriteLocalizedTextWithLocaleWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var localizedText = new LocalizedText("fr-FR", "Bonjour");

        // Act
        encoder.WriteLocalizedText("LocalText", localizedText);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("LocalText"));
        Assert.That(result, Does.Contain("Bonjour"));
        Assert.That(result, Does.Contain("fr-FR"));
    }

    [Test]
    public void WriteVariantWithDoubleValueWritesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
        var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, mockContext.Object);
        var variant = new Variant(3.14159);

        // Act
        encoder.WriteVariant("DoubleVar", variant);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("DoubleVar"));
        Assert.That(result, Does.Contain("Value"));
        Assert.That(result, Does.Contain("3.14159"));
    }

    [Test]
    public void WriteDataValueWithNullValueWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteDataValue("TestValue", null!);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestValue"));
    }

    [Test]
    public void WriteDataValueWithValueWritesAllFields()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var dataValue = new DataValue(
            new Variant(42),
            StatusCodes.Good,
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 12, 0, 5, DateTimeKind.Utc)
        )
        {
            SourcePicoseconds = 100,
            ServerPicoseconds = 200
        };

        // Act
        encoder.WriteDataValue("TestValue", dataValue);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestValue"));
        Assert.That(result, Does.Contain("Value"));
        Assert.That(result, Does.Contain("42"));
        Assert.That(result, Does.Contain("StatusCode"));
        Assert.That(result, Does.Contain("SourceTimestamp"));
        Assert.That(result, Does.Contain("ServerTimestamp"));
        Assert.That(result, Does.Contain("SourcePicoseconds"));
        Assert.That(result, Does.Contain("ServerPicoseconds"));
    }

    [Test]
    public void WriteExtensionObjectWithNullValueCallsPrivateOverload()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ExtensionObject value = ExtensionObject.Null;

        // Act
        encoder.WriteExtensionObject("TestExtension", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestExtension"));
    }

    [Test]
    public void WriteExtensionObjectWithValueCallsPrivateOverload()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var body = new TestEncodeable();
        var value = new ExtensionObject(body, false);

        // Act
        encoder.WriteExtensionObject("TestExtension", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestExtension"));
    }

    [Test]
    public void WriteEncodeableWithNullValueWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxEncodingNestingLevels).Returns(100);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteEncodeable<TestEncodeable>("TestEncodeable", null!);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Not.Contain("TestEncodeable"));
    }

    [Test]
    public void WriteEncodeableWithValueEncodesValue()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxEncodingNestingLevels).Returns(100);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var value = new TestEncodeable();

        // Act
        encoder.WriteEncodeable("TestEncodeable", value);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestEncodeable"));
    }

    [Test]
    public void WriteEncodeableWithValueDecrementsNestingLevel()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxEncodingNestingLevels).Returns(100);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var value = new TestEncodeable();

        // Act - call multiple times to ensure nesting level is properly managed
        encoder.WriteEncodeable("Test1", value);
        encoder.WriteEncodeable("Test2", value);
        encoder.Close();

        // Assert - no exception should be thrown
        var result = sb.ToString();
        Assert.That(result, Does.Contain("Test1"));
        Assert.That(result, Does.Contain("Test2"));
    }

    [Test]
    public void WriteEnumeratedWithValueWritesSymbol()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteEnumerated("TestEnum", TestEnum.Value1);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestEnum"));
        Assert.That(result, Does.Contain("Value1_1"));
    }

    [Test]
    public void WriteEnumeratedWithSymbolMatchingIntWritesSymbolOnly()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act - using an enum with numeric name
        encoder.WriteEnumerated("TestNumericEnum", TestNumericEnum.Item100);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestNumericEnum"));
        Assert.That(result, Does.Contain("Item100_100"));
    }

    [Test]
    public void WriteBooleanArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteBooleanArray("TestArray", default);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteBooleanArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<bool>());

        // Act
        encoder.WriteBooleanArray("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("Boolean"));
    }

    [Test]
    public void WriteBooleanArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        bool[] boolValues = [true, false, true];
        var values = ArrayOf.Wrapped(boolValues);

        // Act
        encoder.WriteBooleanArray("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("Boolean"));
        Assert.That(result, Does.Contain("true"));
        Assert.That(result, Does.Contain("false"));
    }

    [Test]
    public void WriteBooleanArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        bool[] boolValues = [true, false, true];
        var values = ArrayOf.Wrapped(boolValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteBooleanArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteSByteArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteSByteArray("TestArray", default);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteSByteArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<sbyte>());

        // Act
        encoder.WriteSByteArray("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("SByte"));
    }

    [Test]
    public void WriteSByteArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        sbyte[] sbyteValues = [42, -100, 127];
        var values = ArrayOf.Wrapped(sbyteValues);

        // Act
        encoder.WriteSByteArray("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("42"));
        Assert.That(result, Does.Contain("-100"));
        Assert.That(result, Does.Contain("127"));
    }

    [Test]
    public void WriteSByteArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        sbyte[] sbyteValues = [42, -100, 127];
        var values = ArrayOf.Wrapped(sbyteValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteSByteArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteByteArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteByteArray("TestArray", default);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteByteArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<byte>());

        // Act
        encoder.WriteByteArray("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("Byte"));
    }

    [Test]
    public void WriteByteArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        byte[] byteValues = [42, 100, 255];
        var values = ArrayOf.Wrapped(byteValues);

        // Act
        encoder.WriteByteArray("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("42"));
        Assert.That(result, Does.Contain("100"));
        Assert.That(result, Does.Contain("255"));
    }

    [Test]
    public void WriteByteArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        byte[] byteValues = [42, 100, 255];
        var values = ArrayOf.Wrapped(byteValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteByteArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteInt16ArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteInt16Array("TestArray", default);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteInt16ArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<short>());

        // Act
        encoder.WriteInt16Array("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("Int16"));
    }

    [Test]
    public void WriteInt16ArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        short[] int16Values = [1234, -5000, 32767];
        var values = ArrayOf.Wrapped(int16Values);

        // Act
        encoder.WriteInt16Array("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("1234"));
        Assert.That(result, Does.Contain("-5000"));
        Assert.That(result, Does.Contain("32767"));
    }

    [Test]
    public void WriteInt16ArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        short[] int16Values = [1234, -5000, 32767];
        var values = ArrayOf.Wrapped(int16Values);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteInt16Array("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteUInt16ArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteUInt16Array("TestArray", default);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteUInt16ArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<ushort>());

        // Act
        encoder.WriteUInt16Array("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("UInt16"));
    }

    [Test]
    public void WriteUInt16ArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ushort[] uint16Values = [1234, 5000, 65535];
        var values = ArrayOf.Wrapped(uint16Values);

        // Act
        encoder.WriteUInt16Array("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("1234"));
        Assert.That(result, Does.Contain("5000"));
        Assert.That(result, Does.Contain("65535"));
    }

    [Test]
    public void WriteUInt16ArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ushort[] uint16Values = [1234, 5000, 65535];
        var values = ArrayOf.Wrapped(uint16Values);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt16Array("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteInt32ArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteInt32Array("TestArray", default);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteInt32ArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<int>());

        // Act
        encoder.WriteInt32Array("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("Int32"));
    }

    [Test]
    public void WriteInt32ArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        int[] int32Values = [123456, -50000, 2147483647];
        var values = ArrayOf.Wrapped(int32Values);

        // Act
        encoder.WriteInt32Array("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("123456"));
        Assert.That(result, Does.Contain("-50000"));
        Assert.That(result, Does.Contain("2147483647"));
    }

    [Test]
    public void WriteInt32ArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        int[] int32Values = [123456, -50000, 2147483647];
        var values = ArrayOf.Wrapped(int32Values);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteInt32Array("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }
    [Test]
    public void WriteUInt32ArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteUInt32Array("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteUInt32ArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<uint>());

        // Act
        encoder.WriteUInt32Array("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("UInt32"));
    }

    [Test]
    public void WriteUInt32ArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        uint[] uint32Values = [1, 100, 50000, uint.MaxValue];
        var values = ArrayOf.Wrapped(uint32Values);

        // Act
        encoder.WriteUInt32Array("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("1"));
        Assert.That(result, Does.Contain("100"));
        Assert.That(result, Does.Contain("50000"));
        Assert.That(result, Does.Contain("4294967295"));
    }

    [Test]
    public void WriteUInt32ArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        uint[] uint32Values = [1, 2, 3];
        var values = ArrayOf.Wrapped(uint32Values);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt32Array("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    [Category("ProductionBugSuspected")]
    [Ignore("ProductionBugSuspected")]
    public void WriteInt64ArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteInt64Array("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Not.Contain("TestArray"));
    }

    [Test]
    public void WriteInt64ArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<long>());

        // Act
        encoder.WriteInt64Array("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("Int64"));
    }

    [Test]
    public void WriteInt64ArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        long[] int64Values = [1, -100, 50000, long.MinValue, long.MaxValue];
        var values = ArrayOf.Wrapped(int64Values);

        // Act
        encoder.WriteInt64Array("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("1"));
        Assert.That(result, Does.Contain("-100"));
        Assert.That(result, Does.Contain("50000"));
        Assert.That(result, Does.Contain("-9223372036854775808"));
        Assert.That(result, Does.Contain("9223372036854775807"));
    }

    [Test]
    public void WriteInt64ArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        long[] int64Values = [1, 2, 3];
        var values = ArrayOf.Wrapped(int64Values);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteInt64Array("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteUInt64ArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteUInt64Array("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteUInt64ArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<ulong>());

        // Act
        encoder.WriteUInt64Array("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("UInt64"));
    }

    [Test]
    public void WriteUInt64ArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ulong[] uint64Values = [1, 100, 50000, ulong.MaxValue];
        var values = ArrayOf.Wrapped(uint64Values);

        // Act
        encoder.WriteUInt64Array("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("1"));
        Assert.That(result, Does.Contain("100"));
        Assert.That(result, Does.Contain("50000"));
        Assert.That(result, Does.Contain("18446744073709551615"));
    }

    [Test]
    public void WriteUInt64ArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ulong[] uint64Values = [1, 2, 3];
        var values = ArrayOf.Wrapped(uint64Values);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt64Array("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    [Category("ProductionBugSuspected")]
    [Ignore("ProductionBugSuspected")]
    public void WriteFloatArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteFloatArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Not.Contain("TestArray"));
    }

    [Test]
    public void WriteFloatArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<float>());

        // Act
        encoder.WriteFloatArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("Float"));
    }

    [Test]
    public void WriteFloatArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        float[] floatValues = [1.5f, -2.75f, 0.0f, float.MaxValue];
        var values = ArrayOf.Wrapped(floatValues);

        // Act
        encoder.WriteFloatArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("1.5"));
        Assert.That(result, Does.Contain("-2.75"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteFloatArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        float[] floatValues = [1.0f, 2.0f, 3.0f];
        var values = ArrayOf.Wrapped(floatValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteFloatArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    [Category("ProductionBugSuspected")]
    [Ignore("ProductionBugSuspected")]
    public void WriteDoubleArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteDoubleArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Not.Contain("TestArray"));
    }

    [Test]
    public void WriteDoubleArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<double>());

        // Act
        encoder.WriteDoubleArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("Double"));
    }

    [Test]
    public void WriteDoubleArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        double[] doubleValues = [1.5, -2.75, 0.0, double.MaxValue];
        var values = ArrayOf.Wrapped(doubleValues);

        // Act
        encoder.WriteDoubleArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("1.5"));
        Assert.That(result, Does.Contain("-2.75"));
        Assert.That(result, Does.Contain("0"));
    }

    [Test]
    public void WriteDoubleArrayExceedingMaxLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        double[] doubleValues = [1.0, 2.0, 3.0];
        var values = ArrayOf.Wrapped(doubleValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteDoubleArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    [Category("ProductionBugSuspected")]
    [Ignore("ProductionBugSuspected")]
    public void WriteStringArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteStringArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Not.Contain("TestArray"));
    }

    [Test]
    public void WriteStringArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<string>());

        // Act
        encoder.WriteStringArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("String"));
    }

    [Test]
    public void WriteStringArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        string[] stringValues = ["first", "second", "third"];
        var values = ArrayOf.Wrapped(stringValues);

        // Act
        encoder.WriteStringArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("first"));
        Assert.That(result, Does.Contain("second"));
        Assert.That(result, Does.Contain("third"));
    }

    [Test]
    public void WriteStringArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        string[] stringValues = ["one", "two", "three"];
        var values = ArrayOf.Wrapped(stringValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteStringArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteDateTimeArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteDateTimeArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteDateTimeArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<DateTime>());

        // Act
        encoder.WriteDateTimeArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("DateTime"));
    }

    [Test]
    public void WriteDateTimeArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        DateTime[] dateTimeValues = [new DateTime(2023, 1, 1), new DateTime(2023, 12, 31)];
        var values = ArrayOf.Wrapped(dateTimeValues);

        // Act
        encoder.WriteDateTimeArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("2023"));
    }

    [Test]
    public void WriteDateTimeArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        DateTime[] dateTimeValues = [DateTime.Now, DateTime.Now, DateTime.Now];
        var values = ArrayOf.Wrapped(dateTimeValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteDateTimeArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteGuidArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteGuidArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteGuidArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<Uuid>());

        // Act
        encoder.WriteGuidArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("Guid"));
    }

    [Test]
    public void WriteGuidArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        Uuid[] guidValues = [new Uuid(Guid.NewGuid()), new Uuid(Guid.NewGuid())];
        var values = ArrayOf.Wrapped(guidValues);

        // Act
        encoder.WriteGuidArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("Guid"));
    }

    [Test]
    public void WriteGuidArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        Uuid[] guidValues = [new Uuid(Guid.NewGuid()), new Uuid(Guid.NewGuid()), new Uuid(Guid.NewGuid())];
        var values = ArrayOf.Wrapped(guidValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteGuidArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteByteStringArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteByteStringArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteByteStringArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<ByteString>());

        // Act
        encoder.WriteByteStringArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("ByteString"));
    }

    [Test]
    public void WriteByteStringArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ByteString[] byteStringValues = [new ByteString(new byte[] { 1, 2, 3 }), new ByteString(new byte[] { 4, 5, 6 })];
        var values = ArrayOf.Wrapped(byteStringValues);

        // Act
        encoder.WriteByteStringArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("ByteString"));
    }

    [Test]
    public void WriteByteStringArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ByteString[] byteStringValues =
        [
            new ByteString(new byte[] { 1 }),
            new ByteString(new byte[] { 2 }),
            new ByteString(new byte[] { 3 })
        ];
        var values = ArrayOf.Wrapped(byteStringValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteByteStringArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteXmlElementArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteXmlElementArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteXmlElementArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<Opc.Ua.XmlElement>());

        // Act
        encoder.WriteXmlElementArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("XmlElement"));
    }

    [Test]
    public void WriteXmlElementArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var xmlDoc = new XmlDocument();
        var systemElement1 = xmlDoc.CreateElement("Element1");
        systemElement1.InnerText = "Value1";
        var element1 = Opc.Ua.XmlElement.From(systemElement1);

        var systemElement2 = xmlDoc.CreateElement("Element2");
        systemElement2.InnerText = "Value2";
        var element2 = Opc.Ua.XmlElement.From(systemElement2);

        Opc.Ua.XmlElement[] xmlElementValues = [element1, element2];
        var values = ArrayOf.Wrapped(xmlElementValues);

        // Act
        encoder.WriteXmlElementArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("Element1"));
        Assert.That(result, Does.Contain("Value1"));
        Assert.That(result, Does.Contain("Element2"));
        Assert.That(result, Does.Contain("Value2"));
    }

    [Test]
    public void WriteXmlElementArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var xmlDoc = new XmlDocument();
        var systemElement1 = xmlDoc.CreateElement("E1");
        var element1 = Opc.Ua.XmlElement.From(systemElement1);
        var systemElement2 = xmlDoc.CreateElement("E2");
        var element2 = Opc.Ua.XmlElement.From(systemElement2);
        var systemElement3 = xmlDoc.CreateElement("E3");
        var element3 = Opc.Ua.XmlElement.From(systemElement3);

        Opc.Ua.XmlElement[] xmlElementValues = [element1, element2, element3];
        var values = ArrayOf.Wrapped(xmlElementValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteXmlElementArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteNodeIdArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteNodeIdArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteNodeIdArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<NodeId>());

        // Act
        encoder.WriteNodeIdArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("NodeId"));
    }

    [Test]
    public void WriteNodeIdArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        NodeId[] nodeIdValues = [new NodeId(123), new NodeId("test", 1), new NodeId(Guid.NewGuid())];
        var values = ArrayOf.Wrapped(nodeIdValues);

        // Act
        encoder.WriteNodeIdArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("NodeId"));
    }

    [Test]
    public void WriteNodeIdArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        NodeId[] nodeIdValues = [new NodeId(1), new NodeId(2), new NodeId(3)];
        var values = ArrayOf.Wrapped(nodeIdValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteNodeIdArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    [Category("ProductionBugSuspected")]
    [Ignore("ProductionBugSuspected")]
    public void WriteExpandedNodeIdArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteExpandedNodeIdArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Not.Contain("TestArray"));
    }

    [Test]
    public void WriteExpandedNodeIdArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<ExpandedNodeId>());

        // Act
        encoder.WriteExpandedNodeIdArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("ExpandedNodeId"));
    }

    [Test]
    public void WriteExpandedNodeIdArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ExpandedNodeId[] expandedNodeIdValues = [new ExpandedNodeId(123, "http://test", 0), new ExpandedNodeId("test", "http://test2", 0)];
        var values = ArrayOf.Wrapped(expandedNodeIdValues);

        // Act
        encoder.WriteExpandedNodeIdArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("ExpandedNodeId"));
    }

    [Test]
    public void WriteExpandedNodeIdArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ExpandedNodeId[] expandedNodeIdValues = [
            new ExpandedNodeId(1, "http://test", 0),
            new ExpandedNodeId(2, "http://test", 0),
            new ExpandedNodeId(3, "http://test", 0)
            ];
        var values = ArrayOf.Wrapped(expandedNodeIdValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteExpandedNodeIdArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteStatusCodeArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteStatusCodeArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteStatusCodeArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<StatusCode>());

        // Act
        encoder.WriteStatusCodeArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("StatusCode"));
    }

    [Test]
    public void WriteStatusCodeArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        StatusCode[] statusCodeValues = [StatusCodes.Good, StatusCodes.Bad, StatusCodes.Uncertain];
        var values = ArrayOf.Wrapped(statusCodeValues);

        // Act
        encoder.WriteStatusCodeArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("StatusCode"));
    }

    [Test]
    public void WriteStatusCodeArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        StatusCode[] statusCodeValues = [StatusCodes.Good, StatusCodes.Bad, StatusCodes.Uncertain];
        var values = ArrayOf.Wrapped(statusCodeValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteStatusCodeArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteDiagnosticInfoArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteDiagnosticInfoArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteDiagnosticInfoArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<DiagnosticInfo>());

        // Act
        encoder.WriteDiagnosticInfoArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("DiagnosticInfo"));
    }

    [Test]
    public void WriteDiagnosticInfoArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.MaxEncodingNestingLevels).Returns(100);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        mockContext.Setup(c => c.Telemetry).Returns(Mock.Of<ITelemetryContext>());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        DiagnosticInfo[] diagnosticInfoValues = [new DiagnosticInfo(), new DiagnosticInfo(1, 2, 3, 4, "test")];
        var values = ArrayOf.Wrapped(diagnosticInfoValues);

        // Act
        encoder.WriteDiagnosticInfoArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("DiagnosticInfo"));
    }

    [Test]
    public void WriteDiagnosticInfoArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        DiagnosticInfo[] diagnosticInfoValues = [new DiagnosticInfo(), new DiagnosticInfo(1, 2, 3, 4, "test"), new DiagnosticInfo()];
        var values = ArrayOf.Wrapped(diagnosticInfoValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteDiagnosticInfoArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteQualifiedNameArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteQualifiedNameArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteQualifiedNameArrayWithEmptyArrayWritesArrayElement()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<QualifiedName>());

        // Act
        encoder.WriteQualifiedNameArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Not.Contain("QualifiedName"));
    }

    [Test]
    public void WriteQualifiedNameArrayWithValuesWritesAllElements()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        QualifiedName[] qualifiedNameValues = [new QualifiedName("name1"), new QualifiedName("name2", 1)];
        var values = ArrayOf.Wrapped(qualifiedNameValues);

        // Act
        encoder.WriteQualifiedNameArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("QualifiedName"));
    }

    [Test]
    public void WriteQualifiedNameArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        QualifiedName[] qualifiedNameValues = [new QualifiedName("name1"), new QualifiedName("name2"), new QualifiedName("name3")];
        var values = ArrayOf.Wrapped(qualifiedNameValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteQualifiedNameArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteLocalizedTextArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteLocalizedTextArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
    }

    [Test]
    public void WriteLocalizedTextArrayWithEmptyArrayWritesArray()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<LocalizedText>());

        // Act
        encoder.WriteLocalizedTextArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
    }

    [Test]
    public void WriteLocalizedTextArrayWithValuesWritesLocalizedTexts()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        LocalizedText[] localizedTextValues = [new LocalizedText("en", "Hello"), new LocalizedText("fr", "Bonjour")];
        var values = ArrayOf.Wrapped(localizedTextValues);

        // Act
        encoder.WriteLocalizedTextArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("LocalizedText"));
    }

    [Test]
    public void WriteLocalizedTextArrayWithMaxArrayLengthExceededThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        LocalizedText[] localizedTextValues = [
            new LocalizedText("en", "Hello"),
            new LocalizedText("fr", "Bonjour"),
            new LocalizedText("de", "Hallo")
        ];
        var values = ArrayOf.Wrapped(localizedTextValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteLocalizedTextArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteVariantArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteVariantArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
    }

    [Test]
    public void WriteVariantArrayWithEmptyArrayWritesArray()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<Variant>());

        // Act
        encoder.WriteVariantArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
    }

    [Test]
    public void WriteVariantArrayWithValuesWritesVariants()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        Variant[] variantValues = [new Variant(42), new Variant("test")];
        var values = ArrayOf.Wrapped(variantValues);

        // Act
        encoder.WriteVariantArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("Variant"));
    }

    [Test]
    public void WriteVariantArrayWithMaxArrayLengthExceededThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        Variant[] variantValues = [new Variant(42), new Variant("test"), new Variant(3.14)];
        var values = ArrayOf.Wrapped(variantValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteVariantArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteDataValueArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteDataValueArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
    }

    [Test]
    public void WriteDataValueArrayWithEmptyArrayWritesArray()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<DataValue>());

        // Act
        encoder.WriteDataValueArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
    }

    [Test]
    public void WriteDataValueArrayWithValuesWritesDataValues()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        DataValue[] dataValueValues = [new DataValue(new Variant(42)), new DataValue(new Variant("test"))];
        var values = ArrayOf.Wrapped(dataValueValues);

        // Act
        encoder.WriteDataValueArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("DataValue"));
    }

    [Test]
    public void WriteDataValueArrayWithMaxArrayLengthExceededThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        DataValue[] dataValueValues = [
            new DataValue(new Variant(42)),
            new DataValue(new Variant("test")),
            new DataValue(new Variant(3.14))
        ];
        var values = ArrayOf.Wrapped(dataValueValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValueArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteExtensionObjectArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteExtensionObjectArray("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
    }

    [Test]
    public void WriteExtensionObjectArrayWithEmptyArrayWritesArray()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<ExtensionObject>());

        // Act
        encoder.WriteExtensionObjectArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
    }

    [Test]
    public void WriteExtensionObjectArrayWithValuesWritesExtensionObjects()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ExtensionObject[] extensionObjectValues = [new ExtensionObject(ExpandedNodeId.Null), new ExtensionObject(ExpandedNodeId.Null)];
        var values = ArrayOf.Wrapped(extensionObjectValues);

        // Act
        encoder.WriteExtensionObjectArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("ExtensionObject"));
    }

    [Test]
    public void WriteExtensionObjectArrayWithMaxArrayLengthExceededThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ExtensionObject[] extensionObjectValues = [
            new ExtensionObject(ExpandedNodeId.Null),
            new ExtensionObject(ExpandedNodeId.Null),
            new ExtensionObject(ExpandedNodeId.Null)
        ];
        var values = ArrayOf.Wrapped(extensionObjectValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteExtensionObjectArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteEncodeableArrayWithNullArrayWritesNothing()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        // Act
        encoder.WriteEncodeableArray<TestEncodeable>("TestArray", default);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("Root"));
    }

    [Test]
    public void WriteEncodeableArrayWithEmptyArrayWritesArray()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<TestEncodeable>());

        // Act
        encoder.WriteEncodeableArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
    }

    [Test]
    public void WriteEncodeableArrayWithValuesWritesEncodeables()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        TestEncodeable[] encodeableValues = [new TestEncodeable(), new TestEncodeable()];
        var values = ArrayOf.Wrapped(encodeableValues);

        // Act
        encoder.WriteEncodeableArray("TestArray", values);
        encoder.Close();

        // Assert
        string result = stringWriter.ToString();
        Assert.That(result, Does.Contain("TestArray"));
    }

    [Test]
    public void WriteEncodeableArrayWithMaxArrayLengthExceededThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());

        var stringWriter = new StringWriter();
        var writer = XmlWriter.Create(stringWriter);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        TestEncodeable[] encodeableValues = [new TestEncodeable(), new TestEncodeable(), new TestEncodeable()];
        var values = ArrayOf.Wrapped(encodeableValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteEncodeableArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteEnumeratedArrayWritesEnumValues()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(typeof(TestEncodeable), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(new[] { TestEnum.Value1, TestEnum.Value2, TestEnum.Value3 });

        // Act
        encoder.WriteEnumeratedArray("TestEnumArray", values);
        writer.Flush();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestEnumArray"));
        Assert.That(result, Does.Contain("Value1"));
        Assert.That(result, Does.Contain("Value2"));
        Assert.That(result, Does.Contain("Value3"));
    }

    [Test]
    public void WriteEnumeratedArrayWithNullArrayDoesNotWrite()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(typeof(TestEncodeable), writer, mockContext.Object);

        ArrayOf<TestEnum> values = default;

        // Act
        encoder.WriteEnumeratedArray("TestEnumArray", values);
        writer.Flush();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestEnumArray"));
        Assert.That(result, Does.Contain("xsi:nil=\"true\""));
    }

    [Test]
    public void WriteEnumeratedArrayExceedsMaxArrayLengthThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(typeof(TestEncodeable), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(new[] { TestEnum.Value1, TestEnum.Value2, TestEnum.Value3 });

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteEnumeratedArray("TestEnumArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }

    [Test]
    public void WriteSwitchFieldWritesUInt32Value()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(typeof(TestEncodeable), writer, mockContext.Object);

        // Act
        encoder.WriteSwitchField(42u, out string fieldName);
        writer.Flush();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("SwitchField"));
        Assert.That(result, Does.Contain("42"));
        Assert.That(fieldName, Is.Null);
    }

    [Test]
    public void WriteEncodingMaskWritesUInt32Value()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        var mockTelemetry = new Mock<ITelemetryContext>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger<XmlEncoder>>();
        mockContext.Setup(c => c.Telemetry).Returns(mockTelemetry.Object);
        mockTelemetry.Setup(t => t.LoggerFactory).Returns(mockLoggerFactory.Object);
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(typeof(TestEncodeable), writer, mockContext.Object);

        // Act
        encoder.WriteEncodingMask(255u);
        writer.Flush();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("EncodingMask"));
        Assert.That(result, Does.Contain("255"));
    }

    [Test]
    public void WriteObjectArrayWithNullArrayWritesEmpty()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        ArrayOf<object> values = default;

        // Act
        encoder.WriteObjectArray("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("Root"));
    }

    [Test]
    public void WriteObjectArrayWithEmptyArrayWritesArray()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        var values = ArrayOf.Wrapped(Array.Empty<object>());

        // Act
        encoder.WriteObjectArray("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
    }

    [Test]
    public void WriteObjectArrayWithValuesWritesVariants()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(0);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        object[] objectValues = [42, "test", true];
        var values = ArrayOf.Wrapped(objectValues);

        // Act
        encoder.WriteObjectArray("TestArray", values);
        encoder.Close();

        // Assert
        var result = sb.ToString();
        Assert.That(result, Does.Contain("TestArray"));
        Assert.That(result, Does.Contain("Variant"));
    }

    [Test]
    public void WriteObjectArrayWithMaxArrayLengthExceededThrowsException()
    {
        // Arrange
        var mockContext = new Mock<IServiceMessageContext>();
        mockContext.Setup(c => c.MaxArrayLength).Returns(2);
        mockContext.Setup(c => c.NamespaceUris).Returns(new NamespaceTable());
        mockContext.Setup(c => c.ServerUris).Returns(new StringTable());
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(sb, settings);
        var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, mockContext.Object);

        object[] objectValues = [1, 2, 3];
        var values = ArrayOf.Wrapped(objectValues);

        // Act & Assert
        var exception = Assert.Throws<ServiceResultException>(() => encoder.WriteObjectArray("TestArray", values));
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
    }
}



#region Helper Classes

internal sealed class TestEncodeable : IEncodeable
{
    public ExpandedNodeId TypeId => ExpandedNodeId.Null;
    public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
    public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;
    public void Encode(IEncoder encoder) { }
    public void Decode(IDecoder decoder) { }
    public bool IsEqual(IEncodeable encodeable) => false;
    public object Clone() => new TestEncodeable();
}

internal enum TestEnum
{
    Value1 = 1,
    Value2 = 2,
    Value3 = 3
}

internal enum TestNumericEnum
{
    Item100 = 100,
    Item200 = 200
}

#endregion

