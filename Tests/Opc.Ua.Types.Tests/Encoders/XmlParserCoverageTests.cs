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
    /// <summary>
    /// Additional coverage tests for XmlParser targeting uncovered code paths:
    /// array reading (all types), DataValue reading, ReadVariant wrapper,
    /// matrix reading, stream constructor, error paths (nesting limits,
    /// encoding limits, invalid data), Peek(XmlNodeType), Close methods, and
    /// extension object arrays.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class XmlParserCoverageTests
    {
        private const string Ns = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        #region Stream Constructor

        [Test]
        public void ConstructorWithStreamCreatesInstance()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
            const string xml = $"<Int32 xmlns=\"{Ns}\">42</Int32>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            using var decoder = new XmlParser(stream, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            int result = decoder.ReadInt32("Int32");

            Assert.That(result, Is.EqualTo(42));
        }

        #endregion

        #region Close Methods

        [Test]
        public void CloseDoesNotThrow()
        {
            var ctx = CreateContext();
            using var decoder = new XmlParser("<Root/>", ctx);

            Assert.DoesNotThrow(() => decoder.Close());
        }

        [Test]
        public void CloseWithCheckEofDoesNotThrow()
        {
            var ctx = CreateContext();
            using var decoder = new XmlParser("<Root/>", ctx);

            Assert.DoesNotThrow(() => decoder.Close(true));
            Assert.DoesNotThrow(() => decoder.Close(false));
        }

        #endregion

        #region Peek(XmlNodeType)

        [Test]
        public void PeekXmlNodeTypeElementReturnsQualifiedName()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);

            // No children and Text node type → null
            XmlQualifiedName result = decoder.Peek(XmlNodeType.Text);

            Assert.That(result, Is.Null);
        }

        #endregion

        #region ReadVariant (wrapper method, lines 941-978)

        [Test]
        public void ReadVariantReturnsVariantWithValue()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.EqualTo(42));
        }

        [Test]
        public void ReadVariantMissingFieldReturnsNull()
        {
            var ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Variant result = decoder.ReadVariant("Variant");

            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithEmptyValueReturnsNull()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <Variant xmlns="{Ns}">
            </Variant>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Variant result = decoder.ReadVariant("Variant");

            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        #endregion

        #region ReadDataValue (lines 981-1002)

        [Test]
        public void ReadDataValueReturnsAllFields()
        {
            var ctx = CreateContext();
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

            Assert.That(result.WrappedValue.Value, Is.EqualTo(100));
            Assert.That(result.StatusCode, Is.EqualTo(new StatusCode(0)));
            Assert.That(result.SourceTimestamp, Is.EqualTo(new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc)));
            Assert.That(result.SourcePicoseconds, Is.EqualTo((ushort)500));
            Assert.That(result.ServerTimestamp, Is.EqualTo(new DateTime(2024, 6, 15, 10, 30, 1, DateTimeKind.Utc)));
            Assert.That(result.ServerPicoseconds, Is.EqualTo((ushort)250));
        }

        [Test]
        public void ReadDataValueMissingFieldReturnsDefault()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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

            Assert.That(result.WrappedValue.Value, Is.EqualTo("Hello"));
            Assert.That(result.SourcePicoseconds, Is.EqualTo((ushort)0));
            Assert.That(result.ServerPicoseconds, Is.EqualTo((ushort)0));
        }

        #endregion

        #region ReadVariant as scalar DataValue in variant (line 2131)

        [Test]
        public void ReadVariantValueContentsDataValueReturnsDataValue()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.InstanceOf<DataValue>());
            var dv = (DataValue)result.Value;
            Assert.That(dv.WrappedValue.Value, Is.EqualTo(77));
        }

        #endregion

        #region Array Reading — All Primitive Types

        [Test]
        public void ReadSByteArrayReturnsValues()
        {
            var ctx = CreateContext();
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
            Assert.That(result[1], Is.EqualTo((sbyte)0));
            Assert.That(result[2], Is.EqualTo((sbyte)127));
        }

        [Test]
        public void ReadByteArrayReturnsValues()
        {
            var ctx = CreateContext();
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
            Assert.That(result[0], Is.EqualTo((byte)0));
            Assert.That(result[2], Is.EqualTo((byte)255));
        }

        [Test]
        public void ReadInt16ArrayReturnsValues()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            Assert.That(result[0], Is.EqualTo((ushort)0));
            Assert.That(result[1], Is.EqualTo((ushort)65535));
        }

        [Test]
        public void ReadInt32ArrayReturnsValues()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            Assert.That(result[0], Is.EqualTo(0u));
            Assert.That(result[1], Is.EqualTo(4294967295u));
        }

        [Test]
        public void ReadInt64ArrayReturnsValues()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            Assert.That(result[0], Is.EqualTo(0ul));
            Assert.That(result[1], Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void ReadFloatArrayReturnsValues()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            Assert.That(result[0], Is.Not.Null);
            Assert.That(result[1], Is.Not.Null);
        }

        [Test]
        public void ReadNodeIdArrayReturnsValues()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            Assert.That(result[0].Value, Is.EqualTo(1));
            Assert.That(result[1].Value, Is.EqualTo("text"));
        }

        [Test]
        public void ReadDataValueArrayReturnsValues()
        {
            var ctx = CreateContext();
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
            Assert.That(result[0].WrappedValue.Value, Is.EqualTo(10));
            Assert.That(result[1].WrappedValue.Value, Is.EqualTo(20));
        }

        #endregion

        #region Array Reading — Variant-wrapped arrays (ListOf* via ReadVariantValue)

        [Test]
        public void ReadVariantValueListOfSByteReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfSByte xmlns="{Ns}">
                <SByte>-1</SByte>
                <SByte>0</SByte>
                <SByte>1</SByte>
            </ListOfSByte>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.InstanceOf<sbyte[]>());
            Assert.That((sbyte[])result.Value, Is.EqualTo(new sbyte[] { -1, 0, 1 }));
        }

        [Test]
        public void ReadVariantValueListOfByteReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfByte xmlns="{Ns}">
                <Byte>10</Byte>
                <Byte>20</Byte>
            </ListOfByte>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.InstanceOf<byte[]>());
            Assert.That((byte[])result.Value, Is.EqualTo(new byte[] { 10, 20 }));
        }

        [Test]
        public void ReadVariantValueListOfInt16ReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfInt16 xmlns="{Ns}">
                <Int16>100</Int16>
                <Int16>200</Int16>
            </ListOfInt16>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.InstanceOf<short[]>());
        }

        [Test]
        public void ReadVariantValueListOfUInt16ReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfUInt16 xmlns="{Ns}">
                <UInt16>100</UInt16>
                <UInt16>200</UInt16>
            </ListOfUInt16>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.InstanceOf<ushort[]>());
        }

        [Test]
        public void ReadVariantValueListOfUInt32ReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfUInt32 xmlns="{Ns}">
                <UInt32>1</UInt32>
                <UInt32>2</UInt32>
            </ListOfUInt32>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.InstanceOf<uint[]>());
        }

        [Test]
        public void ReadVariantValueListOfInt64ReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfInt64 xmlns="{Ns}">
                <Int64>1000</Int64>
                <Int64>2000</Int64>
            </ListOfInt64>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.InstanceOf<long[]>());
        }

        [Test]
        public void ReadVariantValueListOfUInt64ReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfUInt64 xmlns="{Ns}">
                <UInt64>1</UInt64>
                <UInt64>2</UInt64>
            </ListOfUInt64>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.InstanceOf<ulong[]>());
        }

        [Test]
        public void ReadVariantValueListOfFloatReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfFloat xmlns="{Ns}">
                <Float>1.5</Float>
                <Float>2.5</Float>
            </ListOfFloat>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.InstanceOf<float[]>());
        }

        [Test]
        public void ReadVariantValueListOfDoubleReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfDouble xmlns="{Ns}">
                <Double>1.1</Double>
                <Double>2.2</Double>
            </ListOfDouble>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.InstanceOf<double[]>());
        }

        [Test]
        public void ReadVariantValueListOfDateTimeReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfDateTime xmlns="{Ns}">
                <DateTime>2024-01-01T00:00:00Z</DateTime>
            </ListOfDateTime>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfGuidReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfGuid xmlns="{Ns}">
                <Guid>
                    <String>12345678-1234-1234-1234-123456789012</String>
                </Guid>
            </ListOfGuid>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfByteStringReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfByteString xmlns="{Ns}">
                <ByteString>AQID</ByteString>
            </ListOfByteString>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfXmlElementReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfXmlElement xmlns="{Ns}">
                <XmlElement><Custom>A</Custom></XmlElement>
            </ListOfXmlElement>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfNodeIdReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfNodeId xmlns="{Ns}">
                <NodeId>
                    <Identifier>i=1</Identifier>
                </NodeId>
            </ListOfNodeId>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfExpandedNodeIdReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfExpandedNodeId xmlns="{Ns}">
                <ExpandedNodeId>
                    <Identifier>i=1</Identifier>
                </ExpandedNodeId>
            </ListOfExpandedNodeId>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfStatusCodeReturnsArray()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ListOfStatusCode xmlns="{Ns}">
                <StatusCode>
                    <Code>0</Code>
                </StatusCode>
            </ListOfStatusCode>
            """;
            using var decoder = new XmlParser(xml, ctx);

            Variant result = decoder.ReadVariantValue(null, default);

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfQualifiedNameReturnsArray()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfLocalizedTextReturnsArray()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfExtensionObjectReturnsArray()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfDataValueReturnsArray()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueListOfVariantReturnsArray()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        #endregion

        #region Array — Nil and Empty Field Handling

        [Test]
        public void ReadInt32ArrayMissingFieldReturnsEmpty()
        {
            var ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<int> result = decoder.ReadInt32Array("ListOfInt32");

            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void ReadBooleanArrayMissingFieldReturnsEmpty()
        {
            var ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<bool> result = decoder.ReadBooleanArray("ListOfBoolean");

            Assert.That(result.Count, Is.EqualTo(0));
        }

        #endregion

        #region Array Length Exceeded

        [Test]
        public void ReadSByteArrayThrowsWhenMaxArrayLengthExceeded()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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

        #endregion

        #region ExtensionObject Array

        [Test]
        public void ReadExtensionObjectArrayReturnsValues()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ArrayOf<ExtensionObject> result = decoder.ReadExtensionObjectArray("ListOfExtensionObject");

            Assert.That(result.Count, Is.EqualTo(0));
        }

        #endregion

        #region Matrix Reading (ReadMatrix in variant)

        [Test]
        public void ReadVariantValueMatrixInt32ReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixBooleanReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixStringReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixDoubleReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixFloatReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixSByteReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixByteReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixInt16ReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixUInt16ReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixUInt32ReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixInt64ReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixUInt64ReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixDateTimeReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixGuidReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixByteStringReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixXmlElementReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixNodeIdReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixExpandedNodeIdReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixStatusCodeReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixQualifiedNameReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixLocalizedTextReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixExtensionObjectReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixDataValueReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixVariantReturnsMatrix()
        {
            var ctx = CreateContext();
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

            Assert.That(result.Value, Is.Not.Null);
        }

        [Test]
        public void ReadVariantValueMatrixUnknownTypeThrows()
        {
            var ctx = CreateContext();
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

        #endregion

        #region Error Paths — Nesting Level

        [Test]
        public void ReadVariantThrowsWhenNestingLevelExceeded()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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

        #endregion

        #region Error Paths — SafeXmlConvert (overflow and format exceptions)

        [Test]
        public void ReadSByteOverflowThrowsBadDecodingError()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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
            var ctx = CreateContext();
            const string xml = $"""
            <Int32 xmlns="{Ns}">not_a_number</Int32>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadInt32("Int32"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        #endregion

        #region Error Paths — SafeConvertFromBase64String (invalid base64)

        [Test]
        public void ReadByteStringInvalidBase64ThrowsBadDecodingError()
        {
            var ctx = CreateContext();
            const string xml = $"""
            <ByteString xmlns="{Ns}">!!!not-base64!!!</ByteString>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadByteString("ByteString"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        #endregion

        #region ReadEncodeableAsExtensionObject

        [Test]
        public void ReadEncodeableAsExtensionObjectReturnsEncodeable()
        {
            var ctx = CreateContext();
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

            var result = decoder.ReadEncodeableAsExtensionObject<CoverageTestEncodeableWithData>("ExtensionObject");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.EqualTo(42));
        }

        [Test]
        public void ReadEncodeableAsExtensionObjectReturnDefaultWhenTypeMismatch()
        {
            var ctx = CreateContext();
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

            var result = decoder.ReadEncodeableAsExtensionObject<CoverageTestEncodeableWithData>("ExtensionObject");

            Assert.That(result, Is.Null);
        }

        #endregion

        #region ReadEncodeableArrayAsExtensionObjects

        [Test]
        public void ReadEncodeableArrayAsExtensionObjectsReturnsValues()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
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

        #endregion

        #region ReadEncodeableMatrix

        [Test]
        public void ReadEncodeableMatrixReturnsMatrix()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
            ctx.Factory.AddEncodeableType(typeof(CoverageTestEncodeableWithData));
            const string xml = $"<Root xmlns=\"{Ns}\"/>";
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            MatrixOf<CoverageTestEncodeableWithData> result =
                decoder.ReadEncodeableMatrix<CoverageTestEncodeableWithData>(
                    "Matrix", new ExpandedNodeId(88888, 0));

            Assert.That(result.IsEmpty, Is.True);
        }

        #endregion

        #region ReadVariantValue with TypeInfo validation

        [Test]
        public void ReadVariantValueWithTypeMismatchThrows()
        {
            var ctx = CreateContext();
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
            var ctx = CreateContext();
            const string xml = $"""
            <Value xmlns="{Ns}">
                <Int32 xmlns="{Ns}">42</Int32>
            </Value>
            """;
            using var decoder = new XmlParser(xml, ctx);
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            var int32TypeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);

            Variant result = decoder.ReadVariantValue("Value", int32TypeInfo);

            Assert.That(result.Value, Is.EqualTo(42));
        }

        #endregion

        #region Test helper types

        [DataContract(Name = "TestEncodeable", Namespace = Namespaces.OpcUaXsd)]
        private sealed class CoverageTestEncodeable : IEncodeable
        {
            public ExpandedNodeId TypeId => ExpandedNodeId.Null;
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            public void Encode(IEncoder encoder) { }
            public void Decode(IDecoder decoder) { }

            public bool IsEqual(IEncodeable encodeable) => false;
            public object Clone() => new CoverageTestEncodeable();
        }

        [DataContract(Name = "CoverageTestEncodeableWithData", Namespace = Namespaces.OpcUaXsd)]
        public sealed class CoverageTestEncodeableWithData : IEncodeable
        {
            public ExpandedNodeId TypeId => new ExpandedNodeId(88888, 0);
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

        #endregion

        #region Helpers

        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            return new ServiceMessageContext(telemetryContext);
        }

        #endregion
    }
}
