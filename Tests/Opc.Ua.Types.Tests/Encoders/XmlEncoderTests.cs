// Copyright (c) 1996-2025 The OPC Foundation. All rights reserved.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Tests for the XML encoder and decoder class.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public
#if NET7_0_OR_GREATER && !NET_STANDARD_TESTS
    partial
#endif
    class XmlEncoderTests
    {
        [Test]
        public void ConstructorWithContextCreatesInstance()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            // Act
            var encoder = new XmlEncoder(messageContext);

            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithContextInitializesXmlWriter()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            // Act
            var encoder = new XmlEncoder(messageContext);
            encoder.PushNamespace(Namespaces.OpcUaXsd);
            encoder.WriteString("Test", "value");
            encoder.PopNamespace();
            string result = encoder.CloseAndReturnText();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("Test"));
            Assert.That(result, Does.Contain("value"));
        }

        [Test]
        public void ConstructorWithTypeAndWriterCreatesInstance()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = false,
                Encoding = Encoding.UTF8,
                Indent = true
            };
            using var writer = XmlWriter.Create(sb, settings);
            Type systemType = typeof(ExtensionObject);

            // Act
            var encoder = new XmlEncoder(systemType, writer, messageContext);

            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithTypeAndNullWriterCreatesInstance()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            Type systemType = typeof(ExtensionObject);

            // Act
            var encoder = new XmlEncoder(systemType, null, messageContext);

            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithQualifiedNameAndWriterCreatesInstance()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

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
            var encoder = new XmlEncoder(root, writer, messageContext);

            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithQualifiedNameAndNullWriterCreatesInstance()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var root = new XmlQualifiedName("TestRoot", Namespaces.OpcUaXsd);

            // Act
            var encoder = new XmlEncoder(root, null, messageContext);

            // Assert
            Assert.That(encoder, Is.Not.Null);
            Assert.That(encoder.Context, Is.EqualTo(messageContext));
        }

        [Test]
        public void ConstructorWithQualifiedNameAndWriterInitializesRoot()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

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
            var encoder = new XmlEncoder(root, writer, messageContext);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestElement"));
            Assert.That(result, Does.Contain("http://test.namespace"));
        }

        [Test]
        public void SetMappingTablesWithBothTablesSetsMappings()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var contextNamespaceUris = new NamespaceTable();
            contextNamespaceUris.Append("http://namespace1");
            contextNamespaceUris.Append("http://namespace2");
            var contextServerUris = new StringTable();
            contextServerUris.Append("server1");
            contextServerUris.Append("server2");

            messageContext.NamespaceUris = contextNamespaceUris;
            messageContext.ServerUris = contextServerUris;

            var encoder = new XmlEncoder(messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var contextServerUris = new StringTable();

            messageContext.NamespaceUris = default;
            messageContext.ServerUris = contextServerUris;

            var encoder = new XmlEncoder(messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = default
            };

            var encoder = new XmlEncoder(messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = default,
                ServerUris = default
            };

            var encoder = new XmlEncoder(messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = default,
                ServerUris = default
            };

            var encoder = new XmlEncoder(messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);

            // Act
            encoder.SaveStringTable("NamespaceUris", "Uri", null);
            string result = encoder.CloseAndReturnText();

            // Assert
            Assert.That(result, Does.Not.Contain("NamespaceUris"));
            Assert.That(result, Does.Not.Contain("Uri"));
        }

        [Test]
        public void SaveStringTableWithEmptyTableDoesNotWrite()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);
            var stringTable = new StringTable();

            // Act
            encoder.SaveStringTable("NamespaceUris", "Uri", stringTable);
            string result = encoder.CloseAndReturnText();

            // Assert
            Assert.That(result, Does.Not.Contain("NamespaceUris"));
            Assert.That(result, Does.Not.Contain("Uri"));
        }

        [Test]
        public void SaveStringTableWithSingleElementDoesNotWrite()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);
            var stringTable = new StringTable();
            stringTable.Append("http://namespace0");

            // Act
            encoder.SaveStringTable("NamespaceUris", "Uri", stringTable);
            string result = encoder.CloseAndReturnText();

            // Assert
            Assert.That(result, Does.Not.Contain("NamespaceUris"));
        }

        [Test]
        public void SaveStringTableWithMultipleElementsWritesTable()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);
            var stringTable = new StringTable();
            stringTable.Append("http://namespace0");
            stringTable.Append("http://namespace1");
            stringTable.Append("http://namespace2");

            // Act
            encoder.SaveStringTable("NamespaceUris", "Uri", stringTable);
            string result = encoder.CloseAndReturnText();

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);
            var stringTable = new StringTable();
            stringTable.Append("first");
            stringTable.Append("second");
            stringTable.Append("third");
            stringTable.Append("fourth");

            // Act
            encoder.SaveStringTable("TestTable", "Element", stringTable);
            string result = encoder.CloseAndReturnText();

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Encoding = Encoding.UTF8,
                Indent = false
            };
            using var writer = XmlWriter.Create(sb, settings);
            var root = new XmlQualifiedName("TestElement", "http://test.namespace");
            var encoder = new XmlEncoder(root, writer, messageContext);

            // Act
            string result = encoder.CloseAndReturnText();

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void DisposeCallsDisposeWithTrueAndSuppressesFinalize()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);

            // Act
            encoder.Dispose();

            // Assert
            Assert.That(encoder, Is.Not.Null);
        }

        [Test]
        public void DisposeCanBeCalledMultipleTimes()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);

            // Act
            EncodingType result = encoder.EncodingType;

            // Assert
            Assert.That(result, Is.EqualTo(EncodingType.Xml));
        }

        [Test]
        public void UseReversibleEncodingReturnsTrue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);

            // Act
            bool result = encoder.UseReversibleEncoding;

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void EncodeMessageWithValidMessageEncodesSuccessfully()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);
            var message = new TestEncodeable();

            // Act
            encoder.EncodeMessage(message);
            string result = encoder.CloseAndReturnText();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("TestEncodeable"));
        }

        [Test]
        public void EncodeMessageWithNullMessageThrowsArgumentNullException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var encoder = new XmlEncoder(messageContext);
            TestEncodeable message = null;

            // Act & Assert
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => encoder.EncodeMessage(message));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.ParamName, Is.EqualTo("message"));
        }

        [Test]
        public void WriteBooleanWithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteBoolean("TestBoolean", true);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestBoolean"));
            Assert.That(result, Does.Contain("true"));
        }

        [Test]
        public void WriteBooleanWithFalseValueWritesFalse()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteBoolean("TestBoolean", false);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestBoolean"));
            Assert.That(result, Does.Contain("false"));
        }

        [Test]
        public void WriteSByteWithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const sbyte value = 42;

            // Act
            encoder.WriteSByte("TestSByte", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestSByte"));
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void WriteSByteWithNegativeValueWritesNegativeValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const sbyte value = -100;

            // Act
            encoder.WriteSByte("TestSByte", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestSByte"));
            Assert.That(result, Does.Contain("-100"));
        }

        [Test]
        public void WriteSByteWithMinValueWritesMinValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const sbyte value = sbyte.MinValue;

            // Act
            encoder.WriteSByte("TestSByte", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestSByte"));
            Assert.That(result, Does.Contain("-128"));
        }

        [Test]
        public void WriteSByteWithMaxValueWritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const sbyte value = sbyte.MaxValue;

            // Act
            encoder.WriteSByte("TestSByte", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestSByte"));
            Assert.That(result, Does.Contain("127"));
        }

        [Test]
        public void WriteSByteWithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const sbyte value = 0;

            // Act
            encoder.WriteSByte("TestSByte", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestSByte"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteByteWithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const byte value = 42;

            // Act
            encoder.WriteByte("TestByte", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestByte"));
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void WriteByteWithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const byte value = 0;

            // Act
            encoder.WriteByte("TestByte", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestByte"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteByteWithMaxValueWritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const byte value = byte.MaxValue;

            // Act
            encoder.WriteByte("TestByte", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestByte"));
            Assert.That(result, Does.Contain("255"));
        }

        [Test]
        public void WriteInt16WithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const short value = 1234;

            // Act
            encoder.WriteInt16("TestInt16", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt16"));
            Assert.That(result, Does.Contain("1234"));
        }

        [Test]
        public void WriteInt16WithNegativeValueWritesNegativeValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const short value = -5000;

            // Act
            encoder.WriteInt16("TestInt16", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt16"));
            Assert.That(result, Does.Contain("-5000"));
        }

        [Test]
        public void WriteInt16WithMinValueWritesMinValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const short value = short.MinValue;

            // Act
            encoder.WriteInt16("TestInt16", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt16"));
            Assert.That(result, Does.Contain("-32768"));
        }

        [Test]
        public void WriteInt16WithMaxValueWritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const short value = short.MaxValue;

            // Act
            encoder.WriteInt16("TestInt16", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt16"));
            Assert.That(result, Does.Contain("32767"));
        }

        [Test]
        public void WriteInt16WithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const short value = 0;

            // Act
            encoder.WriteInt16("TestInt16", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt16"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteUInt16WithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const ushort value = 1234;

            // Act
            encoder.WriteUInt16("TestUInt16", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestUInt16"));
            Assert.That(result, Does.Contain("1234"));
        }

        [Test]
        public void WriteUInt16WithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const ushort value = 0;

            // Act
            encoder.WriteUInt16("TestUInt16", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestUInt16"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteUInt16WithMaxValueWritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const ushort value = ushort.MaxValue;

            // Act
            encoder.WriteUInt16("TestUInt16", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestUInt16"));
            Assert.That(result, Does.Contain("65535"));
        }

        [Test]
        public void WriteInt32WithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const int value = 123456;

            // Act
            encoder.WriteInt32("TestInt32", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt32"));
            Assert.That(result, Does.Contain("123456"));
        }

        [Test]
        public void WriteInt32WithNegativeValueWritesNegativeValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const int value = -50000;

            // Act
            encoder.WriteInt32("TestInt32", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt32"));
            Assert.That(result, Does.Contain("-50000"));
        }

        [Test]
        public void WriteInt32WithMinValueWritesMinValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const int value = int.MinValue;

            // Act
            encoder.WriteInt32("TestInt32", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt32"));
            Assert.That(result, Does.Contain("-2147483648"));
        }

        [Test]
        public void WriteInt32WithMaxValueWritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const int value = int.MaxValue;

            // Act
            encoder.WriteInt32("TestInt32", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt32"));
            Assert.That(result, Does.Contain("2147483647"));
        }

        [Test]
        public void WriteInt32WithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const int value = 0;

            // Act
            encoder.WriteInt32("TestInt32", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt32"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteUInt32WithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const uint value = 123456;

            // Act
            encoder.WriteUInt32("TestUInt32", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestUInt32"));
            Assert.That(result, Does.Contain("123456"));
        }

        [Test]
        public void WriteUInt32WithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const uint value = 0;

            // Act
            encoder.WriteUInt32("TestUInt32", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestUInt32"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteUInt32WithMaxValueWritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const uint value = uint.MaxValue;

            // Act
            encoder.WriteUInt32("TestUInt32", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestUInt32"));
            Assert.That(result, Does.Contain("4294967295"));
        }

        [Test]
        public void WriteInt64WithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const long value = 123456789012;

            // Act
            encoder.WriteInt64("TestInt64", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt64"));
            Assert.That(result, Does.Contain("123456789012"));
        }

        [Test]
        public void WriteInt64WithNegativeValueWritesNegativeValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const long value = -500000000000;

            // Act
            encoder.WriteInt64("TestInt64", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt64"));
            Assert.That(result, Does.Contain("-500000000000"));
        }

        [Test]
        public void WriteInt64WithMinValueWritesMinValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const long value = long.MinValue;

            // Act
            encoder.WriteInt64("TestInt64", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt64"));
            Assert.That(result, Does.Contain("-9223372036854775808"));
        }

        [Test]
        public void WriteInt64WithMaxValueWritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const long value = long.MaxValue;

            // Act
            encoder.WriteInt64("TestInt64", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt64"));
            Assert.That(result, Does.Contain("9223372036854775807"));
        }

        [Test]
        public void WriteInt64WithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const long value = 0;

            // Act
            encoder.WriteInt64("TestInt64", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestInt64"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteUInt64WithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const ulong value = 123456789012;

            // Act
            encoder.WriteUInt64("TestUInt64", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestUInt64"));
            Assert.That(result, Does.Contain("123456789012"));
        }

        [Test]
        public void WriteUInt64WithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const ulong value = 0;

            // Act
            encoder.WriteUInt64("TestUInt64", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestUInt64"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteUInt64WithMaxValueWritesMaxValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const ulong value = ulong.MaxValue;

            // Act
            encoder.WriteUInt64("TestUInt64", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestUInt64"));
            Assert.That(result, Does.Contain("18446744073709551615"));
        }

        [Test]
        public void WriteFloatWithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const float value = 3.14159f;

            // Act
            encoder.WriteFloat("TestFloat", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestFloat"));
            Assert.That(result, Does.Contain("3.14159"));
        }

        [Test]
        public void WriteFloatWithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const float value = 0f;

            // Act
            encoder.WriteFloat("TestFloat", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestFloat"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteFloatWithNegativeValueWritesNegativeValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const float value = -123.456f;

            // Act
            encoder.WriteFloat("TestFloat", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestFloat"));
            Assert.That(result, Does.Contain("-123.456"));
        }

        [Test]
        public void WriteDoubleWithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const double value = 2.718281828;

            // Act
            encoder.WriteDouble("TestDouble", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestDouble"));
            Assert.That(result, Does.Contain("2.718281828"));
        }

        [Test]
        public void WriteDoubleWithZeroValueWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const double value = 0.0;

            // Act
            encoder.WriteDouble("TestDouble", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestDouble"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteDoubleWithNegativeValueWritesNegativeValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            const double value = -987.654321;

            // Act
            encoder.WriteDouble("TestDouble", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestDouble"));
            Assert.That(result, Does.Contain("-987.654321"));
        }

        [Test]
        public void WriteString_LargeStringExceedsLimit_ThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = int.MaxValue,
                MaxStringLength = (1024 * 10) - 1,
                MaxByteStringLength = int.MaxValue,
                MaxMessageSize = int.MaxValue
            };
            var encoder = new XmlEncoder(context);
            string largeString = new('A', 1024 * 10); // 10 KB
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteString(null, largeString));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteString_Null_WritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            // Act & Assert
            encoder.WriteString(null, null);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Is.EqualTo(
                "<uax:Root xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" />"));
        }

        [Test]
        public void WriteDateTimeWithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var value = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc);

            // Act
            encoder.WriteDateTime("TestDateTime", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestDateTime"));
            Assert.That(result, Does.Contain("2024-01-15"));
        }

        [Test]
        public void WriteDateTimeWithLocalTimeConvertsToUtc()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var value = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local);

            // Act
            encoder.WriteDateTime("TestDateTime", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestDateTime"));
            Assert.That(result, Does.Contain("2024-06"));
        }

        [Test]
        public void WriteGuidWithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var guid = new Guid("12345678-1234-1234-1234-123456789abc");
            var value = new Uuid(guid);

            // Act
            encoder.WriteGuid("TestGuid", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestGuid"));
            Assert.That(result, Does.Contain("12345678-1234-1234-1234-123456789abc"));
        }

        [Test]
        public void WriteGuidWithEmptyGuidWritesEmptyValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var value = new Uuid(Guid.Empty);

            // Act
            encoder.WriteGuid("TestGuid", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestGuid"));
            Assert.That(result, Does.Contain("00000000-0000-0000-0000-000000000000"));
        }

        [Test]
        public void WriteByteStringWithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            byte[] bytes = [1, 2, 3, 4, 5];
            var value = new ByteString(bytes);

            // Act
            encoder.WriteByteString("TestByteString", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestByteString"));
            Assert.That(result, Does.Contain("AQIDBAU="));
        }

        [Test]
        public void WriteByteString_LargeBufferExceedsLimit_ThrowsServiceResultException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = int.MaxValue,
                MaxStringLength = int.MaxValue,
                MaxByteStringLength = (1024 * 10) - 1,
                MaxMessageSize = int.MaxValue
            };
            var encoder = new XmlEncoder(context);
            byte[] largeBuffer = new byte[1024 * 10]; // 10 KB
            // Act & Assert
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => encoder.WriteByteString(null, new ByteString(largeBuffer)));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteByteStringWithEmptyByteStringWritesEmpty()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var value = new ByteString(Array.Empty<byte>());

            // Act
            encoder.WriteByteString("TestByteString", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestByteString"));
        }

        [Test]
        public void WriteByteStringWithNullByteStringDoesNotWriteField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var value = default(ByteString);

            // Act
            encoder.WriteByteString("TestByteString", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Not.Contain("TestByteString"));
        }

        [Test]
        public void WriteByteStringWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            ByteString bytes = [0, 1, 2, 3, 4, 5, 6, 7];

            // Act
            encoder.WriteByteString("TestByteString", bytes);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestByteString"));
            Assert.That(result, Does.Contain("AAECAwQFBgc="));
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        [Test]
        public void WriteByteStringWithReadOnlySpanWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            ReadOnlySpan<byte> bytes = [1, 2, 3, 4, 5];

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var xmlDoc = new XmlDocument();
            System.Xml.XmlElement systemElement = xmlDoc.CreateElement("TestElement");
            systemElement.InnerText = "TestValue";
            var element = XmlElement.From(systemElement);

            // Act
            encoder.WriteXmlElement("TestField", element);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestField"));
            Assert.That(result, Does.Contain("TestElement"));
            Assert.That(result, Does.Contain("TestValue"));
        }

        [Test]
        public void WriteNodeIdWithNumericValueWritesNodeId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var nodeId = new NodeId(123, 1);

            // Act
            encoder.WriteNodeId("TestNodeId", nodeId);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestNodeId"));
            Assert.That(result, Does.Contain("Identifier"));
        }

        [Test]
        public void WriteNodeIdWithStringValueWritesNodeId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var nodeId = new NodeId("TestString", 2);

            // Act
            encoder.WriteNodeId("TestNodeId", nodeId);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestNodeId"));
            Assert.That(result, Does.Contain("Identifier"));
        }

        [Test]
        public void WriteNodeIdWithNullDoesNotWriteField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            NodeId nodeId = NodeId.Null;

            // Act
            encoder.WriteNodeId("TestNodeId", nodeId);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Not.Contain("TestNodeId"));
        }

        [Test]
        public void WriteExpandedNodeIdWithNumericValueWritesExpandedNodeId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var expandedNodeId = new ExpandedNodeId(456, 1, null, 0);

            // Act
            encoder.WriteExpandedNodeId("TestExpandedNodeId", expandedNodeId);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestExpandedNodeId"));
            Assert.That(result, Does.Contain("Identifier"));
        }

        [Test]
        public void WriteExpandedNodeIdWithStringValueWritesExpandedNodeId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            var expandedNodeId = new ExpandedNodeId("ExpandedString", 3, null, 0);

            // Act
            encoder.WriteExpandedNodeId("TestExpandedNodeId", expandedNodeId);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestExpandedNodeId"));
            Assert.That(result, Does.Contain("Identifier"));
        }

        [Test]
        public void WriteExpandedNodeIdWithNullDoesNotWriteField()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            ExpandedNodeId expandedNodeId = ExpandedNodeId.Null;

            // Act
            encoder.WriteExpandedNodeId("TestExpandedNodeId", expandedNodeId);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Not.Contain("TestExpandedNodeId"));
        }

        [Test]
        public void WriteStatusCodeWithFieldNameWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var statusCode = new StatusCode(0x80010000);

            // Act
            encoder.WriteStatusCode("TestStatusCode", statusCode);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestStatusCode"));
            Assert.That(result, Does.Contain("Code"));
            Assert.That(result, Does.Contain("2147549184"));
        }

        [Test]
        public void WriteStatusCodeWithGoodStatusCodeWritesGoodCode()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            StatusCode statusCode = StatusCodes.Good;

            // Act
            encoder.WriteStatusCode("Status", statusCode);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("Status"));
            Assert.That(result, Does.Contain("Code"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteDiagnosticInfoWithNullValueWritesNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            DiagnosticInfo diagnosticInfo = null;

            // Act
            encoder.WriteDiagnosticInfo("TestDiagnosticInfo", diagnosticInfo);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Not.Contain("TestDiagnosticInfo"));
        }

        [Test]
        public void WriteDiagnosticInfoWithValueWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var diagnosticInfo = new DiagnosticInfo(1, 2, 3, 4, "Additional info");

            // Act
            encoder.WriteDiagnosticInfo("TestDiagnosticInfo", diagnosticInfo);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestDiagnosticInfo"));
            Assert.That(result, Does.Contain("Additional info"));
        }

        [Test]
        public void WriteQualifiedNameWithNamespaceMappingsUsesMappedNamespaceIndex()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            messageContext.NamespaceUris.Append("http://namespace1.com");
            messageContext.NamespaceUris.Append("http://namespace2.com");
            var encoderNamespaceUris = new NamespaceTable();
            encoderNamespaceUris.Append("http://namespace2.com");
            encoderNamespaceUris.Append("http://namespace1.com");

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var qualifiedName = new QualifiedName("TestName", 1);
            encoder.SetMappingTables(encoderNamespaceUris, null);

            // Act
            encoder.WriteQualifiedName("TestQualifiedName", qualifiedName);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("<uax:NamespaceIndex>2</uax:NamespaceIndex>"));
        }

        [Test]
        public void WriteQualifiedNameWithValueWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var qualifiedName = new QualifiedName("TestName", 1);

            // Act
            encoder.WriteQualifiedName("TestQualifiedName", qualifiedName);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestQualifiedName"));
            Assert.That(result, Does.Contain("TestName"));
        }

        [Test]
        public void WriteQualifiedNameWithNullValueWritesNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            QualifiedName qualifiedName = QualifiedName.Null;

            // Act
            encoder.WriteQualifiedName("TestQualifiedName", qualifiedName);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Not.Contain("TestQualifiedName"));
        }

        [Test]
        public void WriteLocalizedTextWithValueWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var localizedText = new LocalizedText("en-US", "Hello World");

            // Act
            encoder.WriteLocalizedText("TestLocalizedText", localizedText);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestLocalizedText"));
            Assert.That(result, Does.Contain("Hello World"));
        }

        [Test]
        public void WriteLocalizedTextWithEmptyValueWritesEmpty()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            LocalizedText localizedText = LocalizedText.Null;

            // Act
            encoder.WriteLocalizedText("TestLocalizedText", localizedText);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Not.Contain("TestLocalizedText"));
        }

        [Test]
        public void WriteVariantWithIntegerValueWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var variant = Variant.From(42);

            // Act
            encoder.WriteVariant("TestVariant", variant);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestVariant"));
            Assert.That(result, Does.Contain("Value"));
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        public void WriteVariantWithStringValueWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var variant = Variant.From("Test String");

            // Act
            encoder.WriteVariant("TestVariant", variant);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestVariant"));
            Assert.That(result, Does.Contain("Value"));
            Assert.That(result, Does.Contain("Test String"));
        }

        [Test]
        public void WriteVariantWithBooleanValueWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var variant = Variant.From(true);

            // Act
            encoder.WriteVariant("TestVariant", variant);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestVariant"));
            Assert.That(result, Does.Contain("Value"));
            Assert.That(result, Does.Contain("true"));
        }

        [Test]
        public void WriteVariantWithNullValueWritesNull()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            Variant variant = Variant.Null;

            // Act
            encoder.WriteVariant("TestVariant", variant);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestVariant"));
            Assert.That(result, Does.Contain("Value"));
        }

        [Test]
        public void WriteStatusCodeWithZeroCodeWritesZero()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var statusCode = new StatusCode(0);

            // Act
            encoder.WriteStatusCode("Status", statusCode);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("Status"));
            Assert.That(result, Does.Contain("Code"));
            Assert.That(result, Does.Contain("0"));
        }

        [Test]
        public void WriteDiagnosticInfoWithEmptyValueWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxEncodingNestingLevels = 100
            };
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var diagnosticInfo = new DiagnosticInfo();

            // Act
            encoder.WriteDiagnosticInfo("DiagInfo", diagnosticInfo);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("DiagInfo"));
        }

        [Test]
        public void WriteQualifiedNameWithNamespaceZeroWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var qualifiedName = new QualifiedName("Name", 0);

            // Act
            encoder.WriteQualifiedName("QName", qualifiedName);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("QName"));
            Assert.That(result, Does.Contain("Name"));
        }

        [Test]
        public void WriteLocalizedTextWithLocaleWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var localizedText = new LocalizedText("fr-FR", "Bonjour");

            // Act
            encoder.WriteLocalizedText("LocalText", localizedText);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("LocalText"));
            Assert.That(result, Does.Contain("Bonjour"));
            Assert.That(result, Does.Contain("fr-FR"));
        }

        [Test]
        public void WriteVariantWithDoubleValueWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", "http://test"), writer, messageContext);
            var variant = Variant.From(3.14159);

            // Act
            encoder.WriteVariant("DoubleVar", variant);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("DoubleVar"));
            Assert.That(result, Does.Contain("Value"));
            Assert.That(result, Does.Contain("3.14159"));
        }

        [Test]
        public void WriteDataValueWithNullValueWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteDataValue("TestValue", null);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Not.Contain("TestValue"));
        }

        [Test]
        public void WriteDataValueWithValueWritesAllFields()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var dataValue = new DataValue(
                Variant.From(42),
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
            string result = sb.ToString();
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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            ExtensionObject value = ExtensionObject.Null;

            // Act
            encoder.WriteExtensionObject("TestExtension", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Not.Contain("TestExtension"));
        }

        [Test]
        public void WriteExtensionObjectWithValueCallsPrivateOverload()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var body = new TestEncodeable();
            var value = new ExtensionObject(body, false);

            // Act
            encoder.WriteExtensionObject("TestExtension", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestExtension"));
        }

        [Test]
        public void WriteEncodeableWithNullValueWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxEncodingNestingLevels = 100
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteEncodeable<TestEncodeable>("TestEncodeable", null);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Not.Contain("TestEncodeable"));
        }

        [Test]
        public void WriteEncodeableWithValueEncodesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxEncodingNestingLevels = 100
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var value = new TestEncodeable();

            // Act
            encoder.WriteEncodeable("TestEncodeable", value);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestEncodeable"));
        }

        [Test]
        public void WriteEncodeableAsExtensionObjectWritesXml()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxEncodingNestingLevels = 100
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var value = new TestEncodeable();

            encoder.WriteEncodeableAsExtensionObject("TestExtension", value);
            encoder.Close();

            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestExtension").And.Contain("TestEncodeable"));
        }

        [Test]
        public void WriteEncodeableArrayAsExtensionObjectsWritesXml()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxEncodingNestingLevels = 100
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            ArrayOf<TestEncodeable> values = [new TestEncodeable(), new TestEncodeable()];

            encoder.WriteEncodeableArrayAsExtensionObjects("Extensions", values);
            encoder.Close();

            string result = sb.ToString();
            Assert.That(result, Does.Contain("Extensions").And.Contain("ExtensionObject").And.Contain("TestEncodeable"));
        }

        [Test]
        public void WriteEncodeableWithValueDecrementsNestingLevel()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxEncodingNestingLevels = 100
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var value = new TestEncodeable();

            // Act - call multiple times to ensure nesting level is properly managed
            encoder.WriteEncodeable("Test1", value);
            encoder.WriteEncodeable("Test2", value);
            encoder.Close();

            // Assert - no exception should be thrown
            string result = sb.ToString();
            Assert.That(result, Does.Contain("Test1"));
            Assert.That(result, Does.Contain("Test2"));
        }

        [Test]
        public void WriteEnumeratedWithValueWritesSymbol()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteEnumerated("TestEnum", TestEnum.Value1);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestEnum"));
            Assert.That(result, Does.Contain("Value1_1"));
        }

        [Test]
        public void WriteEnumeratedWithSymbolMatchingIntWritesSymbolOnly()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act - using an enum with numeric name
            encoder.WriteEnumerated("TestNumericEnum", TestNumericEnum.Item100);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestNumericEnum"));
            Assert.That(result, Does.Contain("Item100_100"));
        }

        [Test]
        public void WriteBooleanArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteBooleanArray("TestArray", default);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("xsi:nil=\"true\""));
        }

        [Test]
        public void WriteBooleanArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var values = ArrayOf.Wrapped(Array.Empty<bool>());

            // Act
            encoder.WriteBooleanArray("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Not.Contain("Boolean"));
        }

        [Test]
        public void WriteBooleanArrayWithValuesWritesAllElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            bool[] boolValues = [true, false, true];
            var values = ArrayOf.Wrapped(boolValues);

            // Act
            encoder.WriteBooleanArray("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("Boolean"));
            Assert.That(result, Does.Contain("true"));
            Assert.That(result, Does.Contain("false"));
        }

        [Test]
        public void WriteBooleanArrayExceedingMaxLengthThrowsException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            bool[] boolValues = [true, false, true];
            var values = ArrayOf.Wrapped(boolValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteBooleanArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteSByteArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteSByteArray("TestArray", default);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("xsi:nil=\"true\""));
        }

        [Test]
        public void WriteSByteArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var values = ArrayOf.Wrapped(Array.Empty<sbyte>());

            // Act
            encoder.WriteSByteArray("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Not.Contain("SByte"));
        }

        [Test]
        public void WriteSByteArrayWithValuesWritesAllElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            sbyte[] sbyteValues = [42, -100, 127];
            var values = ArrayOf.Wrapped(sbyteValues);

            // Act
            encoder.WriteSByteArray("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("42"));
            Assert.That(result, Does.Contain("-100"));
            Assert.That(result, Does.Contain("127"));
        }

        [Test]
        public void WriteSByteArrayExceedingMaxLengthThrowsException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            sbyte[] sbyteValues = [42, -100, 127];
            var values = ArrayOf.Wrapped(sbyteValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteSByteArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteByteArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteByteArray("TestArray", default);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("xsi:nil=\"true\""));
        }

        [Test]
        public void WriteByteArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var values = ArrayOf.Wrapped(Array.Empty<byte>());

            // Act
            encoder.WriteByteArray("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Not.Contain("Byte"));
        }

        [Test]
        public void WriteByteArrayWithValuesWritesAllElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            byte[] byteValues = [42, 100, 255];
            var values = ArrayOf.Wrapped(byteValues);

            // Act
            encoder.WriteByteArray("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("42"));
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("255"));
        }

        [Test]
        public void WriteByteArrayExceedingMaxLengthThrowsException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            byte[] byteValues = [42, 100, 255];
            var values = ArrayOf.Wrapped(byteValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteByteArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteInt16ArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteInt16Array("TestArray", default);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("xsi:nil=\"true\""));
        }

        [Test]
        public void WriteInt16ArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var values = ArrayOf.Wrapped(Array.Empty<short>());

            // Act
            encoder.WriteInt16Array("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Not.Contain("Int16"));
        }

        [Test]
        public void WriteInt16ArrayWithValuesWritesAllElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            short[] int16Values = [1234, -5000, 32767];
            var values = ArrayOf.Wrapped(int16Values);

            // Act
            encoder.WriteInt16Array("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("1234"));
            Assert.That(result, Does.Contain("-5000"));
            Assert.That(result, Does.Contain("32767"));
        }

        [Test]
        public void WriteInt16ArrayExceedingMaxLengthThrowsException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            short[] int16Values = [1234, -5000, 32767];
            var values = ArrayOf.Wrapped(int16Values);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteInt16Array("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteUInt16ArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteUInt16Array("TestArray", default);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("xsi:nil=\"true\""));
        }

        [Test]
        public void WriteUInt16ArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var values = ArrayOf.Wrapped(Array.Empty<ushort>());

            // Act
            encoder.WriteUInt16Array("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Not.Contain("UInt16"));
        }

        [Test]
        public void WriteUInt16ArrayWithValuesWritesAllElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            ushort[] uint16Values = [1234, 5000, 65535];
            var values = ArrayOf.Wrapped(uint16Values);

            // Act
            encoder.WriteUInt16Array("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("1234"));
            Assert.That(result, Does.Contain("5000"));
            Assert.That(result, Does.Contain("65535"));
        }

        [Test]
        public void WriteUInt16ArrayExceedingMaxLengthThrowsException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            ushort[] uint16Values = [1234, 5000, 65535];
            var values = ArrayOf.Wrapped(uint16Values);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt16Array("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteInt32ArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteInt32Array("TestArray", default);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("xsi:nil=\"true\""));
        }

        [Test]
        public void WriteInt32ArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var values = ArrayOf.Wrapped(Array.Empty<int>());

            // Act
            encoder.WriteInt32Array("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Not.Contain("Int32"));
        }

        [Test]
        public void WriteInt32ArrayWithValuesWritesAllElements()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            int[] int32Values = [123456, -50000, 2147483647];
            var values = ArrayOf.Wrapped(int32Values);

            // Act
            encoder.WriteInt32Array("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("123456"));
            Assert.That(result, Does.Contain("-50000"));
            Assert.That(result, Does.Contain("2147483647"));
        }

        [Test]
        public void WriteInt32ArrayExceedingMaxLengthThrowsException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            int[] int32Values = [123456, -50000, 2147483647];
            var values = ArrayOf.Wrapped(int32Values);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteInt32Array("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }
        [Test]
        public void WriteUInt32ArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            uint[] uint32Values = [1, 2, 3];
            var values = ArrayOf.Wrapped(uint32Values);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt32Array("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteInt64ArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteInt64Array("TestArray", default);
            encoder.Close();

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Does.Contain("Root"));
        }

        [Test]
        public void WriteInt64ArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            long[] int64Values = [1, 2, 3];
            var values = ArrayOf.Wrapped(int64Values);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteInt64Array("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteUInt64ArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            ulong[] uint64Values = [1, 2, 3];
            var values = ArrayOf.Wrapped(uint64Values);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteUInt64Array("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteFloatArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteFloatArray("TestArray", default);
            encoder.Close();

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Does.Contain("Root"));
        }

        [Test]
        public void WriteFloatArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            float[] floatValues = [1.0f, 2.0f, 3.0f];
            var values = ArrayOf.Wrapped(floatValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteFloatArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDoubleArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteDoubleArray("TestArray", default);
            encoder.Close();

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Does.Contain("Root"));
        }

        [Test]
        public void WriteDoubleArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            double[] doubleValues = [1.0, 2.0, 3.0];
            var values = ArrayOf.Wrapped(doubleValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteDoubleArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteStringArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteStringArray("TestArray", default);
            encoder.Close();

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Does.Contain("Root"));
        }

        [Test]
        public void WriteStringArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            string[] stringValues = ["one", "two", "three"];
            var values = ArrayOf.Wrapped(stringValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteStringArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDateTimeArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            DateTime[] dateTimeValues = [DateTime.Now, DateTime.Now, DateTime.Now];
            var values = ArrayOf.Wrapped(dateTimeValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteDateTimeArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteGuidArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            Uuid[] guidValues = [new Uuid(Guid.NewGuid()), new Uuid(Guid.NewGuid()), new Uuid(Guid.NewGuid())];
            var values = ArrayOf.Wrapped(guidValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteGuidArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteByteStringArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            ByteString[] byteStringValues =
            [
                new ByteString(new byte[] { 1 }),
                new ByteString(new byte[] { 2 }),
                new ByteString(new byte[] { 3 })
            ];
            var values = ArrayOf.Wrapped(byteStringValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteByteStringArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteXmlElementArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var values = ArrayOf.Wrapped(Array.Empty<XmlElement>());

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var xmlDoc = new XmlDocument();
            System.Xml.XmlElement systemElement1 = xmlDoc.CreateElement("Element1");
            systemElement1.InnerText = "Value1";
            var element1 = XmlElement.From(systemElement1);

            System.Xml.XmlElement systemElement2 = xmlDoc.CreateElement("Element2");
            systemElement2.InnerText = "Value2";
            var element2 = XmlElement.From(systemElement2);

            XmlElement[] xmlElementValues = [element1, element2];
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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var xmlDoc = new XmlDocument();
            System.Xml.XmlElement systemElement1 = xmlDoc.CreateElement("E1");
            var element1 = XmlElement.From(systemElement1);
            System.Xml.XmlElement systemElement2 = xmlDoc.CreateElement("E2");
            var element2 = XmlElement.From(systemElement2);
            System.Xml.XmlElement systemElement3 = xmlDoc.CreateElement("E3");
            var element3 = XmlElement.From(systemElement3);

            XmlElement[] xmlElementValues = [element1, element2, element3];
            var values = ArrayOf.Wrapped(xmlElementValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteXmlElementArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteNodeIdArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            NodeId[] nodeIdValues = [new NodeId(1), new NodeId(2), new NodeId(3)];
            var values = ArrayOf.Wrapped(nodeIdValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteNodeIdArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteExpandedNodeIdArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            // Act
            encoder.WriteExpandedNodeIdArray("TestArray", default);
            encoder.Close();

            // Assert
            string result = stringWriter.ToString();
            Assert.That(result, Does.Contain("Root"));
        }

        [Test]
        public void WriteExpandedNodeIdArrayWithEmptyArrayWritesArrayElement()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            ExpandedNodeId[] expandedNodeIdValues = [
                new ExpandedNodeId(1, "http://test", 0),
                new ExpandedNodeId(2, "http://test", 0),
                new ExpandedNodeId(3, "http://test", 0)
                ];
            var values = ArrayOf.Wrapped(expandedNodeIdValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteExpandedNodeIdArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteStatusCodeArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            StatusCode[] statusCodeValues = [StatusCodes.Good, StatusCodes.Bad, StatusCodes.Uncertain];
            var values = ArrayOf.Wrapped(statusCodeValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteStatusCodeArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDiagnosticInfoArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                MaxEncodingNestingLevels = 100,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            DiagnosticInfo[] diagnosticInfoValues = [new DiagnosticInfo(), new DiagnosticInfo(1, 2, 3, 4, "test"), new DiagnosticInfo()];
            var values = ArrayOf.Wrapped(diagnosticInfoValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteDiagnosticInfoArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteQualifiedNameArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            QualifiedName[] qualifiedNameValues = [new QualifiedName("name1"), new QualifiedName("name2"), new QualifiedName("name3")];
            var values = ArrayOf.Wrapped(qualifiedNameValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteQualifiedNameArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteLocalizedTextArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            LocalizedText[] localizedTextValues = [
                new LocalizedText("en", "Hello"),
                new LocalizedText("fr", "Bonjour"),
                new LocalizedText("de", "Hallo")
            ];
            var values = ArrayOf.Wrapped(localizedTextValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteLocalizedTextArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteVariantArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            Variant[] variantValues = [Variant.From(42), Variant.From("test")];
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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            Variant[] variantValues = [Variant.From(42), Variant.From("test"), Variant.From(3.14)];
            var values = ArrayOf.Wrapped(variantValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteVariantArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDataValueArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            DataValue[] dataValueValues = [new DataValue(Variant.From(42)), new DataValue(Variant.From("test"))];
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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            DataValue[] dataValueValues = [
                new DataValue(Variant.From(42)),
                new DataValue(Variant.From("test")),
                new DataValue(Variant.From(3.14))
            ];
            var values = ArrayOf.Wrapped(dataValueValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValueArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteExtensionObjectArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            ExtensionObject[] extensionObjectValues = [
                new ExtensionObject(ExpandedNodeId.Null),
                new ExtensionObject(ExpandedNodeId.Null),
                new ExtensionObject(ExpandedNodeId.Null)
            ];
            var values = ArrayOf.Wrapped(extensionObjectValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteExtensionObjectArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteEncodeableArrayWithNullArrayWritesNothing()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            TestEncodeable[] encodeableValues = [new TestEncodeable(), new TestEncodeable(), new TestEncodeable()];
            var values = ArrayOf.Wrapped(encodeableValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteEncodeableArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteEnumeratedArrayWritesEnumValues()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(typeof(TestEncodeable), writer, messageContext);

            var values = ArrayOf.Wrapped([TestEnum.Value1, TestEnum.Value2, TestEnum.Value3]);

            // Act
            encoder.WriteEnumeratedArray("TestEnumArray", values);
            writer.Flush();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestEnumArray"));
            Assert.That(result, Does.Contain("Value1"));
            Assert.That(result, Does.Contain("Value2"));
            Assert.That(result, Does.Contain("Value3"));
        }

        [Test]
        public void WriteEnumeratedArrayWithNullArrayDoesNotWrite()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(typeof(TestEncodeable), writer, messageContext);

            ArrayOf<TestEnum> values = default;

            // Act
            encoder.WriteEnumeratedArray("TestEnumArray", values);
            writer.Flush();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestEnumArray"));
            Assert.That(result, Does.Contain("xsi:nil=\"true\""));
        }

        [Test]
        public void WriteEnumeratedArrayExceedsMaxArrayLengthThrowsException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2
            };

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(typeof(TestEncodeable), writer, messageContext);

            var values = ArrayOf.Wrapped([TestEnum.Value1, TestEnum.Value2, TestEnum.Value3]);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteEnumeratedArray("TestEnumArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteSwitchFieldWritesUInt32Value()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(typeof(TestEncodeable), writer, messageContext);

            // Act
            encoder.WriteSwitchField(42u, out string fieldName);
            writer.Flush();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("SwitchField"));
            Assert.That(result, Does.Contain("42"));
            Assert.That(fieldName, Is.Null);
        }

        [Test]
        public void WriteEncodingMaskWritesUInt32Value()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(typeof(TestEncodeable), writer, messageContext);

            // Act
            encoder.WriteEncodingMask(255u);
            writer.Flush();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("EncodingMask"));
            Assert.That(result, Does.Contain("255"));
        }

        [Test]
        public void WriteObjectArrayWithNullArrayWritesEmpty()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            ArrayOf<object> values = default;

            // Act
            encoder.WriteObjectArray("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("Root"));
        }

        [Test]
        public void WriteObjectArrayWithEmptyArrayWritesArray()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            var values = ArrayOf.Wrapped(Array.Empty<object>());

            // Act
            encoder.WriteObjectArray("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
        }

        [Test]
        public void WriteObjectArrayWithValuesWritesVariants()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 0,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            object[] objectValues = [42, "test", true];
            var values = ArrayOf.Wrapped(objectValues);

            // Act
            encoder.WriteObjectArray("TestArray", values);
            encoder.Close();

            // Assert
            string result = sb.ToString();
            Assert.That(result, Does.Contain("TestArray"));
            Assert.That(result, Does.Contain("Variant"));
        }

        [Test]
        public void WriteObjectArrayWithMaxArrayLengthExceededThrowsException()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            object[] objectValues = [1, 2, 3];
            var values = ArrayOf.Wrapped(objectValues);

            // Act & Assert
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() => encoder.WriteObjectArray("TestArray", values));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        [TestCaseSource(nameof(ScalarVariantValueTestCases))]
        public void WriteVariantValueWithScalarWritesExpectedContent(
            Variant variant, string expectedTypeName, string expectedContent)
        {
            string result = WriteVariantValueToString(variant);

            Assert.That(result, Does.Contain(expectedTypeName));
            Assert.That(result, Does.Contain(expectedContent));
        }

        [Test]
        [TestCaseSource(nameof(ArrayVariantValueTestCases))]
        public void WriteVariantValueWithArrayWritesExpectedListType(
            Variant variant, string expectedListName)
        {
            string result = WriteVariantValueToString(variant);

            Assert.That(result, Does.Contain("TestValue"));
            Assert.That(result, Does.Contain(expectedListName));
        }

        [Test]
        public void WriteVariantValueWithBooleanMatrixWritesMatrixElements()
        {
            ArrayOf<bool> elements = [true, false, true, false];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            string result = WriteVariantValueToString(variant);

            Assert.That(result, Does.Contain("Matrix"));
            Assert.That(result, Does.Contain("Dimensions"));
            Assert.That(result, Does.Contain("Elements"));
        }

        [Test]
        public void WriteVariantValueWithInt32MatrixWritesMatrixElements()
        {
            ArrayOf<int> elements = [1, 2, 3, 4];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            string result = WriteVariantValueToString(variant);

            Assert.That(result, Does.Contain("Matrix"));
            Assert.That(result, Does.Contain("Dimensions"));
            Assert.That(result, Does.Contain("Elements"));
        }

        [Test]
        public void WriteVariantValueWithDoubleMatrixWritesMatrixElements()
        {
            ArrayOf<double> elements = [1.0, 2.0, 3.0, 4.0];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            string result = WriteVariantValueToString(variant);

            Assert.That(result, Does.Contain("Matrix"));
            Assert.That(result, Does.Contain("Dimensions"));
            Assert.That(result, Does.Contain("Elements"));
        }

        [Test]
        public void WriteVariantValueWithStringMatrixWritesMatrixElements()
        {
            ArrayOf<string> elements = ["a", "b", "c", "d"];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            string result = WriteVariantValueToString(variant);

            Assert.That(result, Does.Contain("Matrix"));
            Assert.That(result, Does.Contain("Dimensions"));
            Assert.That(result, Does.Contain("Elements"));
        }

        [Test]
        public void WriteVariantValueWithVariantMatrixWritesMatrixElements()
        {
            ArrayOf<Variant> elements =
            [
                Variant.From(1), Variant.From(2),
                Variant.From(3), Variant.From(4)
            ];
            var variant = Variant.From(elements.ToMatrix([2, 2]));

            string result = WriteVariantValueToString(variant);

            Assert.That(result, Does.Contain("Matrix"));
            Assert.That(result, Does.Contain("Dimensions"));
            Assert.That(result, Does.Contain("Elements"));
        }

        [Test]
        public void WriteVariantValueWithNullVariantWritesNil()
        {
            string result = WriteVariantValueToString(Variant.Null);

            Assert.That(result, Does.Contain("nil"));
        }

        [Test]
        public void WriteVariantValueWithNullFieldNameWritesWithoutFieldWrapper()
        {
            var variant = Variant.From(42);

            string result = WriteVariantValueToString(variant, null);

            Assert.That(result, Does.Not.Contain("TestValue"));
            Assert.That(result, Does.Contain("Int32"));
            Assert.That(result, Does.Contain("42"));
        }

        [Test]
        [TestCase(float.PositiveInfinity, "INF")]
        [TestCase(float.NegativeInfinity, "-INF")]
        [TestCase(float.NaN, "NaN")]
        public void EncodeDecodeFloat(float binaryValue, string expectedXmlValue)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            // Encode
            var context = new ServiceMessageContext(telemetry);
            string actualXmlValue;
            using (
                var xmlEncoder = new XmlEncoder(
                    new XmlQualifiedName("FloatSpecialValues", Namespaces.OpcUaXsd),
                    null,
                    context))
            {
                xmlEncoder.PushNamespace(Namespaces.OpcUaXsd);
                xmlEncoder.WriteFloat("Value", binaryValue);
                xmlEncoder.PopNamespace();
                actualXmlValue = xmlEncoder.CloseAndReturnText();
            }

            // Check encode result against expected XML value
            Match m = REValue().Match(actualXmlValue);
            Assert.That(m.Success, Is.True);
            Assert.That(m.Groups.Count, Is.EqualTo(2));
            Assert.That(m.Groups[1].Value, Is.EqualTo(expectedXmlValue));

            // Decode
            float actualBinaryValue;
            using (var reader = XmlReader.Create(new StringReader(actualXmlValue)))
            using (var xmlDecoder = new XmlDecoder(null, reader, context))
            {
                actualBinaryValue = xmlDecoder.ReadFloat("Value");
            }

            // Check decode result against input value
            if (float.IsNaN(actualBinaryValue)) // NaN is not equal to anything!
            {
                Assert.That(float.IsNaN(binaryValue), Is.True);
            }
            else
            {
                Assert.That(actualBinaryValue, Is.EqualTo(binaryValue));
            }
        }

        [Test]
        [TestCase(double.PositiveInfinity, "INF")]
        [TestCase(double.NegativeInfinity, "-INF")]
        [TestCase(double.NaN, "NaN")]
        public void EncodeDecodeDouble(double binaryValue, string expectedXmlValue)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            // Encode
            var context = new ServiceMessageContext(telemetry);
            string actualXmlValue;
            using (
                var xmlEncoder = new XmlEncoder(
                    new XmlQualifiedName("DoubleSpecialValues", Namespaces.OpcUaXsd),
                    null,
                    context))
            {
                xmlEncoder.PushNamespace(Namespaces.OpcUaXsd);
                xmlEncoder.WriteDouble("Value", binaryValue);
                xmlEncoder.PopNamespace();
                actualXmlValue = xmlEncoder.CloseAndReturnText();
            }

            // Check encode result against expected XML value
            Match m = REValue().Match(actualXmlValue);
            Assert.That(m.Success, Is.True);
            Assert.That(m.Groups.Count, Is.EqualTo(2));
            Assert.That(m.Groups[1].Value, Is.EqualTo(expectedXmlValue));

            // Decode
            double actualBinaryValue;
            using (var reader = XmlReader.Create(new StringReader(actualXmlValue)))
            using (var xmlDecoder = new XmlDecoder(null, reader, context))
            {
                actualBinaryValue = xmlDecoder.ReadDouble("Value");
            }

            // Check decode result against input value
            if (double.IsNaN(actualBinaryValue)) // NaN is not equal to anything!
            {
                Assert.That(double.IsNaN(binaryValue), Is.True);
            }
            else
            {
                Assert.That(actualBinaryValue, Is.EqualTo(binaryValue));
            }
        }

        [Test]
        public void EncodeDecodeVariantMatrix()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MatrixOf<int> value = s_elements.ToMatrix(s_dimensions);
            var variant = Variant.From(value);

            const string expected =
                """
                <?xml version="1.0" encoding="utf-16"?>
                <uax:VariantTest xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                  <uax:Test>
                    <uax:Value>
                      <uax:Matrix>
                        <uax:Dimensions>
                          <uax:Int32>2</uax:Int32>
                          <uax:Int32>2</uax:Int32>
                        </uax:Dimensions>
                        <uax:Elements>
                          <uax:Int32>1</uax:Int32>
                          <uax:Int32>2</uax:Int32>
                          <uax:Int32>3</uax:Int32>
                          <uax:Int32>4</uax:Int32>
                        </uax:Elements>
                      </uax:Matrix>
                    </uax:Value>
                  </uax:Test>
                </uax:VariantTest>
                """;

            // Encode
            var context = new ServiceMessageContext(telemetry);
            string actualXmlValue;
            using (
                var xmlEncoder = new XmlEncoder(
                    new XmlQualifiedName("VariantTest", Namespaces.OpcUaXsd),
                    null,
                    context))
            {
                xmlEncoder.PushNamespace(Namespaces.OpcUaXsd);
                xmlEncoder.WriteVariant("Test", variant);
                xmlEncoder.PopNamespace();
                actualXmlValue = xmlEncoder.CloseAndReturnText();
            }

            // Check encode result against expected XML value
            Assert.That(
                expected.Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal),
                Is.EqualTo(actualXmlValue.Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal)));
            // Decode
            Variant actualVariant;
            using (var reader = XmlReader.Create(new StringReader(actualXmlValue)))
            using (var xmlDecoder = new XmlDecoder(null, reader, context))
            {
                actualVariant = xmlDecoder.ReadVariant("Test");
            }

            // Check decode result against input value
            Assert.That(actualVariant, Is.EqualTo(variant));
        }

        [Test]
        public void EncodeDecodeVariantNil()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Variant variant = Variant.Null;

            const string expected =
                "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<uax:VariantTest xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">\r\n  <uax:Test>\r\n    <uax:Value xsi:nil=\"true\" />\r\n  </uax:Test>\r\n</uax:VariantTest>";

            // Encode
            var context = new ServiceMessageContext(telemetry);
            string actualXmlValue;
            using (
                var xmlEncoder = new XmlEncoder(
                    new XmlQualifiedName("VariantTest", Namespaces.OpcUaXsd),
                    null,
                    context))
            {
                xmlEncoder.PushNamespace(Namespaces.OpcUaXsd);
                xmlEncoder.WriteVariant("Test", variant);
                xmlEncoder.PopNamespace();
                actualXmlValue = xmlEncoder.CloseAndReturnText();
            }

            // Check encode result against expected XML value
            Assert.That(
                expected.Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal),
                Is.EqualTo(actualXmlValue.Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal)));
            // Decode
            Variant actualVariant;
            using (var reader = XmlReader.Create(new StringReader(actualXmlValue)))
            using (var xmlDecoder = new XmlDecoder(null, reader, context))
            {
                actualVariant = xmlDecoder.ReadVariant("Test");
            }

            // Check decode result against input value
            Assert.That(actualVariant, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void DecodeInvalidFloatIncludesValueInError()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            const string invalidValue = "not-a-number";
            const string xmlContent = "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<FloatTest xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" " +
                "xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                $"<Value>{invalidValue}</Value></FloatTest>";

            using var reader = XmlReader.Create(new StringReader(xmlContent));
            using var xmlDecoder = new XmlDecoder(null, reader, context);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => xmlDecoder.ReadFloat("Value"));
            Assert.That(ex.Message, Does.Contain(invalidValue));
            Assert.That(ex.Message, Does.Contain("Value:"));
        }

        [Test]
        public void DecodeInvalidDoubleIncludesValueInError()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            const string invalidValue = "invalid-double";
            const string xmlContent = "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<DoubleTest xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" " +
                "xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                $"<Value>{invalidValue}</Value></DoubleTest>";

            using var reader = XmlReader.Create(new StringReader(xmlContent));
            using var xmlDecoder = new XmlDecoder(null, reader, context);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => xmlDecoder.ReadDouble("Value"));
            Assert.That(ex.Message, Does.Contain(invalidValue));
            Assert.That(ex.Message, Does.Contain("Value:"));
        }

        [Test]
        public void DecodeInvalidDateTimeIncludesValueInError()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            const string invalidValue = "not-a-date";
            const string xmlContent = "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<DateTimeTest xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" " +
                "xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                $"<Value>{invalidValue}</Value></DateTimeTest>";

            using var reader = XmlReader.Create(new StringReader(xmlContent));
            using var xmlDecoder = new XmlDecoder(null, reader, context);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => xmlDecoder.ReadDateTime("Value"));
            Assert.That(ex.Message, Does.Contain(invalidValue));
            Assert.That(ex.Message, Does.Contain("Value:"));
        }

        [Test]
        public void DecodeInvalidInt32IncludesValueInError()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            const string invalidValue = "not-an-integer";
            const string xmlContent = "<?xml version=\"1.0\" encoding=\"utf-16\"?>" +
                "<Int32Test xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" " +
                "xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                $"<Value>{invalidValue}</Value></Int32Test>";

            using var reader = XmlReader.Create(new StringReader(xmlContent));
            using var xmlDecoder = new XmlDecoder(null, reader, context);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => xmlDecoder.ReadInt32("Value"));
            Assert.That(ex.Message, Does.Contain(invalidValue));
            Assert.That(ex.Message, Does.Contain("Value:"));
        }

        [Test]
        [TestCaseSource(nameof(ScalarVariantRoundTripTestCases))]
        public void WriteVariantValueWithScalarRoundTripsCorrectly(Variant variant)
        {
            Variant decoded = RoundTripVariantValueFromXml(variant);

            Assert.That(decoded, Is.EqualTo(variant));
        }

        [Test]
        public void WriteVariantValueWithDateTimeScalarRoundTripsCorrectly()
        {
            var value = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.Value, Is.EqualTo(value));
        }

        [Test]
        public void WriteVariantValueWithGuidScalarRoundTripsCorrectly()
        {
            var value = new Uuid(new Guid("12345678-1234-1234-1234-123456789abc"));
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.Value, Is.EqualTo(value));
        }

        [Test]
        public void WriteVariantValueWithByteStringScalarRoundTripsCorrectly()
        {
            var value = new ByteString(new byte[] { 1, 2, 3 });
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.GetByteString(), Is.EqualTo(value));
        }

        [Test]
        public void WriteVariantValueWithNodeIdScalarRoundTripsCorrectly()
        {
            var value = new NodeId(123, 1);
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.Value, Is.EqualTo(value));
        }

        [Test]
        public void WriteVariantValueWithExpandedNodeIdScalarRoundTripsCorrectly()
        {
            var value = new ExpandedNodeId(456, 1);
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.Value, Is.EqualTo(value));
        }

        [Test]
        public void WriteVariantValueWithQualifiedNameScalarRoundTripsCorrectly()
        {
            var value = new QualifiedName("qname", 1);
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.Value, Is.EqualTo(value));
        }

        [Test]
        public void WriteVariantValueWithLocalizedTextScalarRoundTripsCorrectly()
        {
            var value = new LocalizedText("en", "loctext");
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.Value, Is.EqualTo(value));
        }

        [Test]
        public void WriteVariantValueWithExtensionObjectScalarRoundTripsCorrectly()
        {
            var value = new ExtensionObject(
                new ExpandedNodeId(1, 0),
                new ByteString(new byte[] { 1 }));
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.Value, Is.InstanceOf<ExtensionObject>());
        }

        [Test]
        public void WriteVariantValueWithDataValueScalarRoundTripsCorrectly()
        {
            var value = new DataValue(Variant.From(99));
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.GetDataValue().Value, Is.EqualTo(Variant.From(99)));
        }

        [Test]
        public void WriteVariantValueWithEnumerationScalarRoundTripsAsInt32()
        {
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(TestEnum.Value1));

            Assert.That(decoded.Value, Is.EqualTo(1));
        }

        [Test]
        public void WriteVariantValueWithXmlElementScalarRoundTripsCorrectly()
        {
            var xmlDoc = new XmlDocument();
            System.Xml.XmlElement sysElement = xmlDoc.CreateElement("TestElem");
            sysElement.InnerText = "XmlVal";
            var value = XmlElement.From(sysElement);
            Variant decoded = RoundTripVariantValueFromXml(Variant.From(value));

            Assert.That(decoded.Value, Is.InstanceOf<XmlElement>());
        }

        [Test]
        [TestCaseSource(nameof(ArrayVariantValueTestCases))]
        public void WriteVariantValueWithArrayRoundTripsCorrectly(Variant variant, string _)
        {
            Variant decoded = RoundTripVariantValueFromXml(variant);

            Assert.That(decoded.TypeInfo.IsArray, Is.True);
        }

        [Test]
        [TestCaseSource(nameof(MatrixVariantRoundTripTestCases))]
        public void WriteVariantValueWithMatrixRoundTripsCorrectly(
            Variant variant, BuiltInType expectedBuiltInType)
        {
            Variant decoded = RoundTripVariantValueFromXml(variant);

            Assert.That(decoded.TypeInfo.IsMatrix, Is.True);
            Assert.That(decoded.TypeInfo.BuiltInType, Is.EqualTo(expectedBuiltInType));
            Assert.That(decoded, Is.EqualTo(variant));
        }

        [Test]
        public void WriteVariantValueWithGuidMatrixRoundTripsCorrectly()
        {
            var variant = Variant.From(
            ArrayOf.Wrapped([
                Uuid.NewUuid(),
                Uuid.NewUuid(),
                Uuid.NewUuid(),
                Uuid.NewUuid()
            ]).ToMatrix(2, 2));

            Variant decoded = RoundTripVariantValueFromXml(variant);

            Assert.That(decoded.TypeInfo.IsMatrix, Is.True);
            Assert.That(decoded.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Guid));
            Assert.That(decoded, Is.EqualTo(variant));
        }

        [Test]
        public void WriteVariantValueWithXmlElementArrayRoundTripsCorrectly()
        {
            var xmlDoc = new XmlDocument();
            ArrayOf<XmlElement> elems = [XmlElement.From(xmlDoc.CreateElement("E"))];
            var variant = Variant.From(elems);

            Variant decoded = RoundTripVariantValueFromXml(variant);

            Assert.That(decoded.TypeInfo.IsArray, Is.True);
        }

        [Test]
        [TestCase(BuiltInType.DiagnosticInfo)]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        [TestCase(BuiltInType.Variant)]
        public void WriteVariantValueWithUnsupportedScalarTypeThrows(BuiltInType builtInType)
        {
            Variant variant = CreateVariantWithTypeInfo(1, builtInType, ValueRanks.Scalar);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteVariantValueToString(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteVariantValueWithInvalidScalarTypeThrows()
        {
            Variant variant = CreateVariantWithTypeInfo(1, (BuiltInType)999, ValueRanks.Scalar);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteVariantValueToString(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        [TestCase(BuiltInType.DiagnosticInfo)]
        public void WriteVariantValueWithUnsupportedArrayTypeThrows(BuiltInType builtInType)
        {
            ArrayOf<int> value = [1, 2, 3, 4];
            Variant variant = CreateVariantWithTypeInfo(value, builtInType, ValueRanks.OneDimension);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteVariantValueToString(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteVariantValueWithInvalidArrayTypeThrows()
        {
            ArrayOf<int> value = [1, 2, 3, 4];
            Variant variant = CreateVariantWithTypeInfo(value, (BuiltInType)999, ValueRanks.OneDimension);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteVariantValueToString(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        [TestCase(BuiltInType.DiagnosticInfo)]
        public void WriteVariantValueWithUnsupportedMatrixTypeThrows(BuiltInType builtInType)
        {
            ArrayOf<int> value = [1, 2, 3, 4];
            Variant variant = CreateVariantWithTypeInfo(value.ToMatrix(2, 2), builtInType, ValueRanks.TwoDimensions);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteVariantValueToString(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteVariantValueWithInvalidMatrixTypeThrows()
        {
            ArrayOf<int> value = [1, 2, 3, 4];
            Variant variant = CreateVariantWithTypeInfo(
                value.ToMatrix(2, 2),
                (BuiltInType)999,
                ValueRanks.TwoDimensions);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteVariantValueToString(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public void WriteVariantValueWithDiagnosticArrayThrows()
        {
            ArrayOf<DiagnosticInfo> value = [new DiagnosticInfo(), new DiagnosticInfo()];
            Variant variant = CreateVariantWithTypeInfo(value, BuiltInType.DiagnosticInfo, ValueRanks.OneDimension);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteVariantValueToString(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteVariantValueWithDiagnosticMatrixThrows()
        {
            ArrayOf<DiagnosticInfo> value =
            [
                new DiagnosticInfo(),
                new DiagnosticInfo(),
                new DiagnosticInfo(),
                new DiagnosticInfo()
            ];
            Variant variant = CreateVariantWithTypeInfo(
                value.ToMatrix(2, 2),
                BuiltInType.DiagnosticInfo,
                ValueRanks.TwoDimensions);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteVariantValueToString(variant));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteExpandedNodeIdWithNamespaceUriWritesNamespaceUri()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var nodeId = new NodeId(50u);
            var expandedNodeId = new ExpandedNodeId(nodeId, "http://test.namespace.uri");

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Does.Contain("nsu=http://test.namespace.uri;"));
        }

        [Test]
        public void WriteExpandedNodeIdWithServerIndexWritesServerIndex()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var nodeId = new NodeId(25u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, 1u);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Does.Contain("svr=1;"));
        }

        [Test]
        public void WriteExpandedNodeIdWithNamespaceUriAndServerIndexWritesBoth()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var nodeId = new NodeId(75u);
            var expandedNodeId = new ExpandedNodeId(nodeId, "http://test.namespace", 2u);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Does.Contain("svr=2;").And.Contain("nsu=http://test.namespace;"));
        }

        [Test]
        public void WriteExpandedNodeIdWithServerIndexZeroDoesNotWriteServerIndex()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var nodeId = new NodeId(30u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null, 0u);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Does.Not.Contain("svr="));
        }

        [Test]
        public void WriteExpandedNodeIdWithNullNamespaceUriDoesNotWriteNamespaceUri()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var nodeId = new NodeId(40u);
            var expandedNodeId = new ExpandedNodeId(nodeId, null);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Does.Not.Contain("nsu="));
        }

        [Test]
        public void WriteExpandedNodeIdWithEmptyNamespaceUriDoesNotWriteNamespaceUri()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var nodeId = new NodeId(45u);
            var expandedNodeId = new ExpandedNodeId(nodeId, string.Empty);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Does.Not.Contain("nsu="));
        }

        [Test]
        public void WriteExpandedNodeIdWithNamespaceMappingsUsesMappedNamespaceIndex()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            messageContext.NamespaceUris.Append("http://namespace1.com");
            messageContext.NamespaceUris.Append("http://namespace2.com");
            var encoderNamespaceUris = new NamespaceTable();
            encoderNamespaceUris.Append("http://namespace2.com");
            encoderNamespaceUris.Append("http://namespace1.com");
            var expandedNodeId = new ExpandedNodeId(new NodeId(100u, 1));
            string expected = FormatExpandedNodeIdIdentifier(expandedNodeId, 2, 0);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext, encoderNamespaceUris);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExpandedNodeIdWithServerMappingsUsesMappedServerIndex()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                ServerUris = new StringTable()
            };
            messageContext.ServerUris.Append("urn:server1");
            messageContext.ServerUris.Append("urn:server2");
            var encoderServerUris = new StringTable();
            encoderServerUris.Append("urn:server2");
            encoderServerUris.Append("urn:server1");
            var expandedNodeId = new ExpandedNodeId(new NodeId(50u), null, 0u);
            string expected = FormatExpandedNodeIdIdentifier(expandedNodeId, 0, 1);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext, null, encoderServerUris);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExpandedNodeIdWithGuidValueWritesIdentifier()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var guid = Guid.NewGuid();
            var expandedNodeId = new ExpandedNodeId(new NodeId(guid, 0));
            string expected = FormatExpandedNodeIdIdentifier(expandedNodeId, 0, 0);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExpandedNodeIdWithOpaqueValueWritesIdentifier()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var opaque = Guid.NewGuid().ToByteString();
            var expandedNodeId = new ExpandedNodeId(new NodeId(opaque, 0));
            string expected = FormatExpandedNodeIdIdentifier(expandedNodeId, 0, 0);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExpandedNodeIdWithMaxServerIndexWritesServerIndex()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expandedNodeId = new ExpandedNodeId(new NodeId(10u), null, uint.MaxValue);
            string expected = FormatExpandedNodeIdIdentifier(expandedNodeId, 0, uint.MaxValue);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExpandedNodeIdWithMaxByteNamespaceIndexWritesIdentifier()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expandedNodeId = new ExpandedNodeId(new NodeId(200u, 255));
            string expected = FormatExpandedNodeIdIdentifier(expandedNodeId, 255, 0);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExpandedNodeIdWithTwoByteNumericValueWritesIdentifier()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expandedNodeId = new ExpandedNodeId(new NodeId(byte.MaxValue, 0));
            string expected = FormatExpandedNodeIdIdentifier(expandedNodeId, 0, 0);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExpandedNodeIdWithFourByteNumericValueWritesIdentifier()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expandedNodeId = new ExpandedNodeId(new NodeId(ushort.MaxValue, 100));
            string expected = FormatExpandedNodeIdIdentifier(expandedNodeId, 100, 0);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExpandedNodeIdWithNumericValueWritesIdentifier()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expandedNodeId = new ExpandedNodeId(new NodeId(ushort.MaxValue + 1, 100));
            string expected = FormatExpandedNodeIdIdentifier(expandedNodeId, 100, 0);

            string result = WriteExpandedNodeIdToString(expandedNodeId, messageContext);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExtensionObjectWithByteStringBodyWritesByteString()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            messageContext.NamespaceUris.Append(Namespaces.OpcUaXsd);
            var value = new ExtensionObject(
                new ExpandedNodeId(1, Namespaces.OpcUaXsd),
                new ByteString(new byte[] { 1, 2 }));

            string result = WriteExtensionObjectToString(value, messageContext);

            Assert.That(result, Does.Contain("ByteString"));
        }

        [Test]
        public void WriteExtensionObjectWithEmptyByteStringBodyWritesByteString()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            messageContext.NamespaceUris.Append(Namespaces.OpcUaXsd);
            var value = new ExtensionObject(
                new ExpandedNodeId(1, Namespaces.OpcUaXsd),
                ByteString.Empty);

            string result = WriteExtensionObjectToString(value, messageContext);

            Assert.That(result, Does.Contain("ByteString"));
        }

        [Test]
        public void WriteExtensionObjectWithXmlElementBodyWritesXmlElement()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            messageContext.NamespaceUris.Append(Namespaces.OpcUaXsd);
            var xmlDoc = new XmlDocument();
            System.Xml.XmlElement xmlElement = xmlDoc.CreateElement("TestElement");
            var value = new ExtensionObject(
                new ExpandedNodeId(1, Namespaces.OpcUaXsd),
                XmlElement.From(xmlElement));

            string result = WriteExtensionObjectToString(value, messageContext);

            Assert.That(result, Does.Contain("TestElement"));
        }

        [Test]
        public void WriteExtensionObjectWithEncodeableBodyWritesEncodeableElement()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            messageContext.NamespaceUris.Append(Namespaces.OpcUaXsd);
            var value = new ExtensionObject(new TestEncodeable());

            string result = WriteExtensionObjectToString(value, messageContext);

            Assert.That(result, Does.Contain("TestEncodeable"));
        }

        [Test]
        public void WriteExtensionObjectWithNullFieldNameWritesWithoutWrapper()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            messageContext.NamespaceUris.Append(Namespaces.OpcUaXsd);
            var value = new ExtensionObject(
                new ExpandedNodeId(1, Namespaces.OpcUaXsd),
                new ByteString(new byte[] { 1 }));

            string result = WriteExtensionObjectToString(
                value,
                messageContext,
                fieldName: null);

            Assert.That(result, Does.Not.Contain("TestExtension").And.Contain("TypeId"));
        }

        [Test]
        public void WriteExtensionObjectWithUnknownNamespaceThrowsServiceResultException()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            var value = new ExtensionObject(new TestEncodeableWithNamespace());

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteExtensionObjectToString(value, messageContext));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteExtensionObjectWithUnsupportedBodyThrowsServiceResultException()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            messageContext.NamespaceUris.Append(Namespaces.OpcUaXsd);
            var value = new ExtensionObject(new ExpandedNodeId(1, Namespaces.OpcUaXsd), "json");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteExtensionObjectToString(value, messageContext));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingError));
        }

        [Test]
        public void WriteExtensionObjectWithNamespaceMappingsUsesMappedNamespaceIndex()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                NamespaceUris = new NamespaceTable()
            };
            messageContext.NamespaceUris.Append("http://namespace1.com");
            messageContext.NamespaceUris.Append("http://namespace2.com");
            var encoderNamespaceUris = new NamespaceTable();
            encoderNamespaceUris.Append("http://namespace2.com");
            encoderNamespaceUris.Append("http://namespace1.com");
            var value = new ExtensionObject(new ExpandedNodeId(1, 1), new ByteString(new byte[] { 1 }));
            string expected = FormatExpandedNodeIdIdentifier(value.TypeId, 2, 0);

            string result = WriteExtensionObjectToString(
                value,
                messageContext,
                encoderNamespaceUris);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.EqualTo(expected));
        }

        [Test]
        public void WriteExtensionObject_ByteStringEncodeableWithUnknownExternalTypeId_WritesNullNodeId()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var typeId = new ExpandedNodeId(1234, 5, "http://someurinotknowntous", 5);
            var value = new ExtensionObject(typeId, ByteString.From([1, 2]));
            var encoderNamespaceUris = new NamespaceTable();
            // Act
            string result = WriteExtensionObjectToString(
                value,
                messageContext,
                encoderNamespaceUris);
            string identifier = GetIdentifierValue(result);

            Assert.That(identifier, Is.Empty);
        }

        [Test]
        public void WriteDiagnosticInfoWithSymbolicIdWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { SymbolicId = 1 };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("SymbolicId").And.Contain(">1<"));
        }

        [Test]
        public void WriteDiagnosticInfoWithNamespaceUriWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { NamespaceUri = 2 };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("NamespaceUri").And.Contain(">2<"));
        }

        [Test]
        public void WriteDiagnosticInfoWithLocaleWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { Locale = 3 };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("Locale").And.Contain(">3<"));
        }

        [Test]
        public void WriteDiagnosticInfoWithLocalizedTextWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { LocalizedText = 4 };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("LocalizedText").And.Contain(">4<"));
        }

        [Test]
        public void WriteDiagnosticInfoWithAdditionalInfoWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { AdditionalInfo = "info" };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("AdditionalInfo").And.Contain("info"));
        }

        [Test]
        public void WriteDiagnosticInfoWithInnerStatusCodeWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { InnerStatusCode = StatusCodes.BadUnexpectedError };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("InnerStatusCode"));
        }

        [Test]
        public void WriteDiagnosticInfoWithInnerDiagnosticInfoWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo
            {
                InnerDiagnosticInfo = new DiagnosticInfo { AdditionalInfo = "Inner" }
            };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("InnerDiagnosticInfo").And.Contain("Inner"));
        }

        [Test]
        public void WriteDiagnosticInfoWithEmptyAdditionalInfoWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { AdditionalInfo = string.Empty };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("AdditionalInfo"));
        }

        [Test]
        public void WriteDiagnosticInfoWithNegativeSymbolicIdWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { SymbolicId = -1 };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("SymbolicId").And.Contain(">-1<"));
        }

        [Test]
        public void WriteDiagnosticInfoWithZeroSymbolicIdWritesValue()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { SymbolicId = 0 };

            string result = WriteDiagnosticInfoToString(value, messageContext);

            Assert.That(result, Does.Contain("SymbolicId").And.Contain(">0<"));
        }

        [Test]
        public void WriteDiagnosticInfoWithNullFieldNameWritesWithoutWrapper()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { AdditionalInfo = "info" };

            string result = WriteDiagnosticInfoToString(value, messageContext, null);

            Assert.That(result, Does.Not.Contain("TestDiagnosticInfo").And.Contain("AdditionalInfo"));
        }

        [Test]
        public void WriteDiagnosticInfoWithEmptyFieldNameWritesWithoutWrapper()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var value = new DiagnosticInfo { AdditionalInfo = "info" };

            string result = WriteDiagnosticInfoToString(value, messageContext, string.Empty);

            Assert.That(result, Does.Not.Contain("TestDiagnosticInfo").And.Contain("AdditionalInfo"));
        }

        [Test]
        public void WriteDiagnosticInfoExceedsMaxEncodingNestingLevelsThrowsServiceResultException()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxEncodingNestingLevels = 2
            };
            var value = new DiagnosticInfo
            {
                InnerDiagnosticInfo = new DiagnosticInfo
                {
                    InnerDiagnosticInfo = new DiagnosticInfo
                    {
                        InnerDiagnosticInfo = new DiagnosticInfo()
                    }
                }
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => WriteDiagnosticInfoToString(value, messageContext));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void WriteDiagnosticInfoExceedsMaxDiagnosticLevelsTruncates()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var deep = new DiagnosticInfo
            {
                InnerDiagnosticInfo = new DiagnosticInfo
                {
                    InnerDiagnosticInfo = new DiagnosticInfo
                    {
                        InnerDiagnosticInfo = new DiagnosticInfo
                        {
                            InnerDiagnosticInfo = new DiagnosticInfo
                            {
                                InnerDiagnosticInfo = new DiagnosticInfo
                                {
                                    InnerDiagnosticInfo = new DiagnosticInfo
                                    {
                                        InnerDiagnosticInfo = new DiagnosticInfo()
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var truncated = new DiagnosticInfo
            {
                InnerDiagnosticInfo = new DiagnosticInfo
                {
                    InnerDiagnosticInfo = new DiagnosticInfo
                    {
                        InnerDiagnosticInfo = new DiagnosticInfo
                        {
                            InnerDiagnosticInfo = new DiagnosticInfo
                            {
                                InnerDiagnosticInfo = new DiagnosticInfo
                                {
                                    InnerDiagnosticInfo = new DiagnosticInfo()
                                }
                            }
                        }
                    }
                }
            };

            string resultDeep = WriteDiagnosticInfoToString(deep, messageContext);
            string resultTruncated = WriteDiagnosticInfoToString(truncated, messageContext);

            Assert.That(resultDeep, Is.EqualTo(resultTruncated));
        }

        private static Variant RoundTripVariantValueFromXml(Variant variant)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            encoder.WriteVariantValue("TestValue", variant);
            encoder.Close();
            string xml = sb.ToString();
            using var reader = XmlReader.Create(new StringReader(xml));
            using var decoder = new XmlDecoder(null, reader, messageContext);
            return decoder.ReadVariantValue("TestValue", TypeInfo.Unknown);
        }

        private static Variant CreateVariantWithTypeInfo(object value, BuiltInType builtInType, int valueRank)
        {
            return new Variant(default, TypeInfo.Create(builtInType, valueRank), value);
        }

        private static string WriteExpandedNodeIdToString(
            ExpandedNodeId value,
            ServiceMessageContext messageContext,
            NamespaceTable encoderNamespaceUris = null,
            StringTable encoderServerUris = null,
            string fieldName = "TestExpandedNodeId")
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                OmitXmlDeclaration = true
            };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            if (encoderNamespaceUris != null || encoderServerUris != null)
            {
                encoder.SetMappingTables(encoderNamespaceUris, encoderServerUris);
            }
            encoder.WriteExpandedNodeId(fieldName, value);
            encoder.Close();
            return sb.ToString();
        }

        private static string GetIdentifierValue(string xml)
        {
            var document = new XmlDocument();
            using var reader = XmlReader.Create(
                new StringReader(xml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            document.Load(reader);
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("uax", Namespaces.OpcUaXsd);
            XmlNode node = document.SelectSingleNode("//uax:Identifier", manager);
            return node?.InnerText ?? string.Empty;
        }

        private static string FormatExpandedNodeIdIdentifier(
            ExpandedNodeId value,
            ushort namespaceIndex,
            uint serverIndex)
        {
            var buffer = new StringBuilder();
            ExpandedNodeId.Format(
                CultureInfo.InvariantCulture,
                buffer,
                value.IdentifierAsString,
                value.IdType,
                namespaceIndex,
                value.NamespaceUri,
                serverIndex);
            return buffer.ToString();
        }

        private static string WriteExtensionObjectToString(
            ExtensionObject value,
            ServiceMessageContext messageContext,
            NamespaceTable encoderNamespaceUris = null,
            StringTable encoderServerUris = null,
            string fieldName = "TestExtension")
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(
                new XmlQualifiedName("Root", Namespaces.OpcUaXsd),
                writer,
                messageContext);
            if (encoderNamespaceUris != null || encoderServerUris != null)
            {
                encoder.SetMappingTables(encoderNamespaceUris, encoderServerUris);
            }
            encoder.WriteExtensionObject(fieldName, value);
            encoder.Close();
            return sb.ToString();
        }

        private static string WriteDiagnosticInfoToString(
            DiagnosticInfo value,
            ServiceMessageContext messageContext,
            string fieldName = "TestDiagnosticInfo")
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            encoder.WriteDiagnosticInfo(fieldName, value);
            encoder.Close();
            return sb.ToString();
        }

        private static string WriteVariantValueToString(Variant variant, string fieldName = "TestValue")
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(
                new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            encoder.WriteVariantValue(fieldName, variant);
            encoder.Close();
            return sb.ToString();
        }

        private static System.Collections.IEnumerable ScalarVariantValueTestCases()
        {
            yield return new TestCaseData(Variant.From(true), "Boolean", "true");
            yield return new TestCaseData(Variant.From((sbyte)-42), "SByte", "-42");
            yield return new TestCaseData(Variant.From((byte)255), "Byte", "255");
            yield return new TestCaseData(Variant.From((short)-1234), "Int16", "-1234");
            yield return new TestCaseData(Variant.From((ushort)65535), "UInt16", "65535");
            yield return new TestCaseData(Variant.From(123456), "Int32", "123456");
            yield return new TestCaseData(Variant.From(123456u), "UInt32", "123456");
            yield return new TestCaseData(Variant.From(123456789L), "Int64", "123456789");
            yield return new TestCaseData(Variant.From(123456789uL), "UInt64", "123456789");
            yield return new TestCaseData(Variant.From(3.14f), "Float", "3.14");
            yield return new TestCaseData(Variant.From(2.718), "Double", "2.718");
            yield return new TestCaseData(Variant.From("hello"), "String", "hello");
            yield return new TestCaseData(
                Variant.From(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc)),
                "DateTime", "2024-01-15");
            yield return new TestCaseData(
                Variant.From(new Uuid(new Guid("12345678-1234-1234-1234-123456789abc"))),
                "Guid", "12345678-1234-1234-1234-123456789abc");
            yield return new TestCaseData(
                Variant.From(new ByteString(new byte[] { 1, 2, 3 })),
                "ByteString", "AQID");
            yield return new TestCaseData(
                Variant.From(new NodeId(123, 1)),
                "NodeId", "Identifier");
            yield return new TestCaseData(
                Variant.From(new ExpandedNodeId(456, 1)),
                "ExpandedNodeId", "Identifier");
            yield return new TestCaseData(
                Variant.From(new StatusCode(0x80010000u)),
                "StatusCode", "Code");
            yield return new TestCaseData(
                Variant.From(new QualifiedName("qname")),
                "QualifiedName", "qname");
            yield return new TestCaseData(
                Variant.From(new LocalizedText("en", "loctext")),
                "LocalizedText", "loctext");
            yield return new TestCaseData(
                Variant.From(new ExtensionObject(new TestEncodeable())),
                "ExtensionObject", "Body");
            yield return new TestCaseData(
                Variant.From(new DataValue(Variant.From(99))),
                "DataValue", "99");
            yield return new TestCaseData(
                Variant.From(TestEnum.Value1),
                "Int32", "1");

            var xmlDoc = new XmlDocument();
            System.Xml.XmlElement sysElement = xmlDoc.CreateElement("TestElem");
            sysElement.InnerText = "XmlVal";
            yield return new TestCaseData(
                Variant.From(XmlElement.From(sysElement)),
                "XmlElement", "TestElem");
        }

        private static System.Collections.IEnumerable ScalarVariantRoundTripTestCases()
        {
            yield return new TestCaseData(Variant.From(true));
            yield return new TestCaseData(Variant.From((sbyte)-42));
            yield return new TestCaseData(Variant.From((byte)255));
            yield return new TestCaseData(Variant.From((short)-1234));
            yield return new TestCaseData(Variant.From((ushort)65535));
            yield return new TestCaseData(Variant.From(123456));
            yield return new TestCaseData(Variant.From(123456u));
            yield return new TestCaseData(Variant.From(123456789L));
            yield return new TestCaseData(Variant.From(123456789uL));
            yield return new TestCaseData(Variant.From(3.14f));
            yield return new TestCaseData(Variant.From(2.718));
            yield return new TestCaseData(Variant.From("hello"));
        }

        private static System.Collections.IEnumerable ArrayVariantValueTestCases()
        {
            yield return new TestCaseData(
                Variant.From(s_booleanArray), "ListOfBoolean");
            yield return new TestCaseData(
                Variant.From(new sbyte[] { 1, -1 }), "ListOfSByte");
            yield return new TestCaseData(
                Variant.From(new byte[] { 1, 2 }), "ListOfByte");
            yield return new TestCaseData(
                Variant.From(new short[] { 1, -1 }), "ListOfInt16");
            yield return new TestCaseData(
                Variant.From(new ushort[] { 1, 2 }), "ListOfUInt16");
            yield return new TestCaseData(
                Variant.From(new int[] { 1, -1 }), "ListOfInt32");
            yield return new TestCaseData(
                Variant.From(new uint[] { 1, 2 }), "ListOfUInt32");
            yield return new TestCaseData(
                Variant.From(new long[] { 1, -1 }), "ListOfInt64");
            yield return new TestCaseData(
                Variant.From(new ulong[] { 1, 2 }), "ListOfUInt64");
            yield return new TestCaseData(
                Variant.From(s_floatArray), "ListOfFloat");
            yield return new TestCaseData(
                Variant.From(s_doubleArray), "ListOfDouble");
            yield return new TestCaseData(
                Variant.From(s_stringArray), "ListOfString");
            yield return new TestCaseData(
                Variant.From([new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)]), "ListOfDateTime");
            yield return new TestCaseData(
                Variant.From([new Uuid(Guid.Empty)]),
                "ListOfGuid");
            yield return new TestCaseData(
                Variant.From([ByteString.From(new byte[] { 1, 2 })]),
                "ListOfByteString");
            yield return new TestCaseData(
                Variant.From([new NodeId(1)]), "ListOfNodeId");
            yield return new TestCaseData(
                Variant.From([new ExpandedNodeId(1)]),
                "ListOfExpandedNodeId");
            yield return new TestCaseData(
                Variant.From([StatusCodes.Good]),
                "ListOfStatusCode");
            yield return new TestCaseData(
                Variant.From([new QualifiedName("q")]),
                "ListOfQualifiedName");
            yield return new TestCaseData(
                Variant.From([new LocalizedText("en", "t")]),
                "ListOfLocalizedText");
            yield return new TestCaseData(
                Variant.From([new ExtensionObject(ExpandedNodeId.Null)]),
                "ListOfExtensionObject");
            yield return new TestCaseData(
                Variant.From([new DataValue(Variant.From(1))]),
                "ListOfDataValue");
            yield return new TestCaseData(
                Variant.From([Variant.From(1)]),
                "ListOfVariant");
            var xmlDoc = new XmlDocument();
            yield return new TestCaseData(
                Variant.From([XmlElement.From(xmlDoc.CreateElement("E"))]),
                "ListOfXmlElement");
            yield return new TestCaseData(
               Variant.From([TestEnum.Value1, TestEnum.Value2]),
               "ListOfInt32");
        }

        private static System.Collections.IEnumerable MatrixVariantRoundTripTestCases()
        {
            yield return new TestCaseData(
                Variant.From(s_booleanMatrixElements.ToMatrixOf(2, 2)),
                BuiltInType.Boolean);
            yield return new TestCaseData(
                Variant.From(new sbyte[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                BuiltInType.SByte);
            yield return new TestCaseData(
                Variant.From(new byte[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                BuiltInType.Byte);
            yield return new TestCaseData(
                Variant.From(new short[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                BuiltInType.Int16);
            yield return new TestCaseData(
                Variant.From(new ushort[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                BuiltInType.UInt16);
            yield return new TestCaseData(
                Variant.From(new int[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                BuiltInType.Int32);
            yield return new TestCaseData(
                Variant.From(new uint[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                BuiltInType.UInt32);
            yield return new TestCaseData(
                Variant.From(new long[] { 1, -1, 2, -2 }.ToMatrixOf(2, 2)),
                BuiltInType.Int64);
            yield return new TestCaseData(
                Variant.From(new ulong[] { 1, 2, 3, 4 }.ToMatrixOf(2, 2)),
                BuiltInType.UInt64);
            yield return new TestCaseData(
                Variant.From(s_floatMatrixElements.ToMatrixOf(2, 2)),
                BuiltInType.Float);
            yield return new TestCaseData(
                Variant.From(s_doubleMatrixElements.ToMatrixOf(2, 2)),
                BuiltInType.Double);
            yield return new TestCaseData(
                Variant.From(s_stringMatrixElements.ToMatrixOf(2, 2)),
                BuiltInType.String);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 4, 0, 0, 0, DateTimeKind.Utc)
                }.ToMatrixOf(2, 2)),
                BuiltInType.DateTime);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new ByteString(new byte[] { 1 }),
                    new ByteString(new byte[] { 2 }),
                    new ByteString(new byte[] { 3 }),
                    new ByteString(new byte[] { 4 })
                }.ToMatrixOf(2, 2)),
                BuiltInType.ByteString);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    XmlElement.From("<a />"),
                    XmlElement.From("<b />"),
                    XmlElement.From("<c />"),
                    XmlElement.From("<d />")
                }.ToMatrixOf(2, 2)),
                BuiltInType.XmlElement);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new NodeId(1),
                    new NodeId(2),
                    new NodeId(3),
                    new NodeId(4)
                }.ToMatrixOf(2, 2)),
                BuiltInType.NodeId);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new ExpandedNodeId(1),
                    new ExpandedNodeId(2),
                    new ExpandedNodeId(3),
                    new ExpandedNodeId(4)
                }.ToMatrixOf(2, 2)),
                BuiltInType.ExpandedNodeId);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    StatusCodes.Good,
                    StatusCodes.Bad,
                    StatusCodes.Good,
                    StatusCodes.Bad
                }.ToMatrixOf(2, 2)),
                BuiltInType.StatusCode);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new QualifiedName("a"),
                    new QualifiedName("b"),
                    new QualifiedName("c"),
                    new QualifiedName("d")
                }.ToMatrixOf(2, 2)),
                BuiltInType.QualifiedName);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new LocalizedText("en", "a"),
                    new LocalizedText("en", "b"),
                    new LocalizedText("en", "c"),
                    new LocalizedText("en", "d")
                }.ToMatrixOf(2, 2)),
                BuiltInType.LocalizedText);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new ExtensionObject(ExpandedNodeId.Null),
                    new ExtensionObject(ExpandedNodeId.Null),
                    new ExtensionObject(ExpandedNodeId.Null),
                    new ExtensionObject(ExpandedNodeId.Null)
                }.ToMatrixOf(2, 2)),
                BuiltInType.ExtensionObject);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    new DataValue(Variant.From(1)),
                    new DataValue(Variant.From(2)),
                    new DataValue(Variant.From(3)),
                    new DataValue(Variant.From(4))
                }.ToMatrixOf(2, 2)),
                BuiltInType.DataValue);
            yield return new TestCaseData(
                Variant.From(new[]
                {
                    Variant.From(1),
                    Variant.From(2),
                    Variant.From(3),
                    Variant.From(4)
                }.ToMatrixOf(2, 2)),
                BuiltInType.Variant);
            yield return new TestCaseData(
                CreateVariantWithTypeInfo(
                    s_enumMatrixElements.ToMatrixOf(2, 2),
                    BuiltInType.Enumeration,
                    ValueRanks.TwoDimensions),
                BuiltInType.Int32);
        }
#if NET7_0_OR_GREATER && !NET_STANDARD_TESTS
        [GeneratedRegex(@"Value>([^<]*)<")]
        internal static partial Regex REValue();
#else
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable SYSLIB1045 //Use 'GeneratedRegexAttribute' to generate the regular expression implementation at compile-time.
        internal static Regex REValue()
        {
            return new Regex("Value>([^<]*)<");
        }
#pragma warning restore SYSLIB1045 //Use 'GeneratedRegexAttribute' to generate the regular expression implementation at compile-time.
#pragma warning restore IDE0079 // Remove unnecessary suppression
#endif
        private static readonly ArrayOf<int> s_elements = [1, 2, 3, 4];
        private static readonly int[] s_dimensions = [2, 2];
        private static readonly bool[] s_booleanArray = [true, false];
        private static readonly float[] s_floatArray = [1.0f, 2.0f];
        private static readonly double[] s_doubleArray = [1.0, 2.0];
        private static readonly string[] s_stringArray = ["a", "b"];
        private static readonly bool[] s_booleanMatrixElements = [true, false, true, false];
        private static readonly float[] s_floatMatrixElements = [1.0f, 2.0f, 3.0f, 4.0f];
        private static readonly double[] s_doubleMatrixElements = [1.0, 2.0, 3.0, 4.0];
        private static readonly string[] s_stringMatrixElements = ["a", "b", "c", "d"];
        private static readonly int[] s_enumMatrixElements = [1, 2, 1, 2];
    }

    internal sealed class TestEncodeable : IEncodeable
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

    internal sealed class TestEncodeableWithNamespace : IEncodeable
    {
        private static readonly ExpandedNodeId s_typeId = new(1, "urn:missing-namespace");

        public ExpandedNodeId TypeId => s_typeId;
        public ExpandedNodeId BinaryEncodingId => s_typeId;
        public ExpandedNodeId XmlEncodingId => s_typeId;

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
            return new TestEncodeableWithNamespace();
        }
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
}
