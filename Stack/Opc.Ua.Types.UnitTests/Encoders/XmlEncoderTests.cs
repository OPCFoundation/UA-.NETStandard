// Copyright (c) 1996-2025 The OPC Foundation. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
        public void WriteByteStringWithIndexAndCountWritesValue()
        {
            // Arrange
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment };
            using var writer = XmlWriter.Create(sb, settings);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);
            byte[] bytes = [0, 1, 2, 3, 4, 5, 6, 7];

            // Act
            encoder.WriteByteString("TestByteString", bytes, 0, 8);
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
            var variant = new Variant(42);

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
            var variant = new Variant("Test String");

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
            var variant = new Variant(true);

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
            var variant = new Variant(3.14159);

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
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
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
            Assert.That(result, Does.Not.Contain("TestArray"));
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
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
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
            Assert.That(result, Does.Not.Contain("TestArray"));
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
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
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
            Assert.That(result, Does.Not.Contain("TestArray"));
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
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
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
            Assert.That(result, Does.Not.Contain("TestArray"));
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
        [Category("ProductionBugSuspected")]
        [Ignore("ProductionBugSuspected")]
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
            Assert.That(result, Does.Not.Contain("TestArray"));
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
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext)
            {
                MaxArrayLength = 2,
                NamespaceUris = new NamespaceTable()
            };

            var stringWriter = new StringWriter();
            var writer = XmlWriter.Create(stringWriter);
            var encoder = new XmlEncoder(new XmlQualifiedName("Root", Namespaces.OpcUaXsd), writer, messageContext);

            Variant[] variantValues = [new Variant(42), new Variant("test"), new Variant(3.14)];
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
                new DataValue(new Variant(42)),
                new DataValue(new Variant("test")),
                new DataValue(new Variant(3.14))
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

        /// <summary>
        /// Validate the encoding and decoding of the float special values.
        /// </summary>
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
            Assert.True(m.Success);
            Assert.True(m.Groups.Count == 2);
            Assert.AreEqual(m.Groups[1].Value, expectedXmlValue);

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
                Assert.True(float.IsNaN(binaryValue));
            }
            else
            {
                Assert.AreEqual(actualBinaryValue, binaryValue);
            }
        }

        /// <summary>
        /// Validate the encoding and decoding of the double special values.
        /// </summary>
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
            Assert.True(m.Success);
            Assert.True(m.Groups.Count == 2);
            Assert.AreEqual(m.Groups[1].Value, expectedXmlValue);

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
                Assert.True(double.IsNaN(binaryValue));
            }
            else
            {
                Assert.AreEqual(actualBinaryValue, binaryValue);
            }
        }

        /// <summary>
        /// Validate the encoding and decoding of the a variant that consists of a matrix.
        /// </summary>
        [Test]
        public void EncodeDecodeVariantMatrix()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            MatrixOf<int> value = s_elements.ToMatrix(s_dimensions);
            var variant = new Variant(value);

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
            Assert.AreEqual(
                expected.Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal),
                actualXmlValue.Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal));

            // Decode
            Variant actualVariant;
            using (var reader = XmlReader.Create(new StringReader(actualXmlValue)))
            using (var xmlDecoder = new XmlDecoder(null, reader, context))
            {
                actualVariant = xmlDecoder.ReadVariant("Test");
            }

            // Check decode result against input value
            Assert.AreEqual(actualVariant, variant);
        }

        /// <summary>
        /// Validate the encoding and decoding of the a variant that contains a null value
        /// </summary>
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
            Assert.AreEqual(
                expected.Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal),
                actualXmlValue.Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal));

            // Decode
            Variant actualVariant;
            using (var reader = XmlReader.Create(new StringReader(actualXmlValue)))
            using (var xmlDecoder = new XmlDecoder(null, reader, context))
            {
                actualVariant = xmlDecoder.ReadVariant("Test");
            }

            // Check decode result against input value
            Assert.AreEqual(actualVariant, Variant.Null);
        }

        /// <summary>
        /// Validate that decoding errors include the failed value in the error message for Float.
        /// </summary>
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

        /// <summary>
        /// Validate that decoding errors include the failed value in the error message for Double.
        /// </summary>
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

        /// <summary>
        /// Validate that decoding errors include the failed value in the error message for DateTime.
        /// </summary>
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

        /// <summary>
        /// Validate that decoding errors include the failed value in the error message for Int32.
        /// </summary>
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
