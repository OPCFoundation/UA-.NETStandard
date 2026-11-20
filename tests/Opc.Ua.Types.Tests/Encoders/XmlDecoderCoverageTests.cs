// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Additional coverage tests for <see cref="XmlDecoder"/> targeting the
    /// enumerated, diagnostic-info, variant-value and numeric error paths that
    /// are not exercised by <c>XmlDecoderTests</c>.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class XmlDecoderCoverageTests
    {
        [Test]
        public void ReadEnumeratedWithSymbolAndValueSplitsOnUnderscore()
        {
            using XmlDecoder decoder = CreateDecoder(
                "<TestEnum xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">Running_1</TestEnum>");

            EnumValue result = decoder.ReadEnumerated("TestEnum");

            Assert.That(result.Value, Is.EqualTo(1));
            Assert.That(result.Symbol, Is.EqualTo("Running"));
        }

        [Test]
        public void ReadEnumeratedWithNumericOnlyReturnsValueWithoutSymbol()
        {
            using XmlDecoder decoder = CreateDecoder(
                "<TestEnum xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">5</TestEnum>");

            EnumValue result = decoder.ReadEnumerated("TestEnum");

            Assert.That(result.Value, Is.EqualTo(5));
            Assert.That(result.Symbol, Is.Null);
        }

        [Test]
        public void ReadEnumeratedWithPlainTextReturnsZeroWithSymbol()
        {
            using XmlDecoder decoder = CreateDecoder(
                "<TestEnum xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">JustText</TestEnum>");

            EnumValue result = decoder.ReadEnumerated("TestEnum");

            Assert.That(result.Value, Is.Zero);
            Assert.That(result.Symbol, Is.EqualTo("JustText"));
        }

        [Test]
        public void ReadEnumeratedWithOverflowingSuffixThrowsBadDecodingError()
        {
            using XmlDecoder decoder = CreateDecoder(
                "<TestEnum xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">Symbol_99999999999999999999</TestEnum>");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEnumerated("TestEnum"));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadEnumeratedReturnsDefaultForMissingField()
        {
            using XmlDecoder decoder = CreateDecoder(
                "<Other xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">1</Other>");

            EnumValue result = decoder.ReadEnumerated("TestEnum");

            Assert.That(result.Value, Is.Zero);
            Assert.That(result.Symbol, Is.Null);
        }

        [Test]
        public void ReadEnumeratedArrayReturnsAllValues()
        {
            const string xml = """
            <ListOfTestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TestEnum>Running_1</TestEnum>
                <TestEnum>Stopped_2</TestEnum>
            </ListOfTestEnum>
            """;
            using XmlDecoder decoder = CreateDecoder(xml);

            ArrayOf<EnumValue> result = decoder.ReadEnumeratedArray("ListOfTestEnum");

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Value, Is.EqualTo(1));
            Assert.That(result[0].Symbol, Is.EqualTo("Running"));
            Assert.That(result[1].Value, Is.EqualTo(2));
            Assert.That(result[1].Symbol, Is.EqualTo("Stopped"));
        }

        [Test]
        public void ReadEnumeratedArrayThrowsWhenMaxArrayLengthExceeded()
        {
            const string xml = """
            <ListOfTestEnum xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TestEnum>Running_1</TestEnum>
                <TestEnum>Stopped_2</TestEnum>
            </ListOfTestEnum>
            """;
            ServiceMessageContext messageContext = CreateMockContext();
            messageContext.MaxArrayLength = 1;
            using var reader = XmlReader.Create(new StringReader(xml));
            using var decoder = new XmlDecoder(reader, messageContext);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEnumeratedArray("ListOfTestEnum"));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadDiagnosticInfoReadsAllScalarFieldsAndInnerInfo()
        {
            const string xml = """
            <DiagnosticInfo xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <SymbolicId>1</SymbolicId>
                <NamespaceUri>2</NamespaceUri>
                <Locale>3</Locale>
                <LocalizedText>4</LocalizedText>
                <AdditionalInfo>extra</AdditionalInfo>
                <InnerDiagnosticInfo>
                    <SymbolicId>5</SymbolicId>
                </InnerDiagnosticInfo>
            </DiagnosticInfo>
            """;
            using XmlDecoder decoder = CreateDecoder(xml);

            DiagnosticInfo result = decoder.ReadDiagnosticInfo("DiagnosticInfo")!;

            Assert.That(result!.SymbolicId, Is.EqualTo(1));
            Assert.That(result.NamespaceUri, Is.EqualTo(2));
            Assert.That(result.Locale, Is.EqualTo(3));
            Assert.That(result.LocalizedText, Is.EqualTo(4));
            Assert.That(result.AdditionalInfo, Is.EqualTo("extra"));
            Assert.That(result.InnerDiagnosticInfo, Is.Not.Null);
            Assert.That(result.InnerDiagnosticInfo.SymbolicId, Is.EqualTo(5));
        }

        [Test]
        public void ReadVariantValueWithMatchingEnumerationTypeCoercesToInt32()
        {
            using XmlDecoder decoder = CreateDecoder(
                "<Int32 xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">5</Int32>",
                pushNamespace: false);

            Variant result = decoder.ReadVariantValue(null, TypeInfo.Scalars.Enumeration);

            Assert.That(result.GetInt32(), Is.EqualTo(5));
            Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public void ReadVariantValueWithTypeMismatchThrowsBadDecodingError()
        {
            using XmlDecoder decoder = CreateDecoder(
                "<Boolean xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">true</Boolean>",
                pushNamespace: false);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadVariantValue(null, TypeInfo.Scalars.Int32));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadInt32WithOverflowingValueThrowsBadDecodingError()
        {
            using XmlDecoder decoder = CreateDecoder(
                "<Int32 xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">99999999999999999999</Int32>");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadInt32("Int32"));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadInt32WithNonNumericValueThrowsBadDecodingError()
        {
            using XmlDecoder decoder = CreateDecoder(
                "<Int32 xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">notAnInt</Int32>");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadInt32("Int32"));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadExtensionObjectWithUnknownTypeKeepsXmlBody()
        {
            const string xml = """
            <ExtensionObject xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TypeId>
                    <Identifier>i=99999</Identifier>
                </TypeId>
                <Body>
                    <CustomPayload xmlns="urn:example:custom">
                        <Data>hello</Data>
                    </CustomPayload>
                </Body>
            </ExtensionObject>
            """;
            using XmlDecoder decoder = CreateDecoder(xml);

            ExtensionObject result = decoder.ReadExtensionObject("ExtensionObject");

            Assert.That(result.Encoding, Is.EqualTo(ExtensionObjectEncoding.Xml));
            Assert.That(result.TryGetAsXml(out XmlElement body), Is.True);
            Assert.That(body.OuterXml, Does.Contain("hello"));
        }

        private static XmlDecoder CreateDecoder(string xml, bool pushNamespace = true)
        {
            ServiceMessageContext messageContext = CreateMockContext();
            var reader = XmlReader.Create(new StringReader(xml));
            var decoder = new XmlDecoder(reader, messageContext);
            if (pushNamespace)
            {
                decoder.PushNamespace(Namespaces.OpcUaXsd);
            }

            return decoder;
        }

        private static ServiceMessageContext CreateMockContext()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            return ServiceMessageContext.CreateEmpty(telemetryContext);
        }
    }
}
