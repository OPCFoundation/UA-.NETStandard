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
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Encoders.Tests
{
    /// <summary>
    /// <see cref="DataValue"/> stores its status code as a 32-bit code and packs
    /// both picosecond fields with the null flag. The encodings must stay byte for
    /// byte what the previous layout produced; the expected values were captured
    /// with that layout.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [Parallelizable]
    public class DataValueWireFormatTests
    {
        [Test]
        public void BinaryEncodingIsUnchanged()
        {
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteDataValueArray(null, CreateValues());
            Assert.That(CoreUtils.ToHexString(encoder.CloseAndReturnBuffer()!), Is.EqualTo(kExpectedBinary));
        }

        [Test]
        public void VerboseJsonEncodingIsUnchanged()
        {
            Assert.That(EncodeJson(JsonEncoderOptions.Verbose), Is.EqualTo(kExpectedVerboseJson));
        }

        [Test]
        public void CompactJsonEncodingIsUnchanged()
        {
            Assert.That(EncodeJson(JsonEncoderOptions.Compact), Is.EqualTo(kExpectedCompactJson));
        }

        [Test]
        public void XmlEncodingIsUnchanged()
        {
            Assert.That(
                EncodeXml().Replace("\r\n", "\n", StringComparison.Ordinal),
                Is.EqualTo(kExpectedXml.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        [Test]
        public void BinaryRoundTripKeepsEveryValue()
        {
            ServiceMessageContext context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            encoder.WriteDataValueArray(null, CreateValues());
            using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer()!, context);

            AssertSameValues(decoder.ReadDataValueArray(null), dropsOrphanPicoseconds: true);
        }

        [Test]
        public void JsonRoundTripKeepsEveryValue([Values] bool compact)
        {
            string json = EncodeJson(compact ? JsonEncoderOptions.Compact : JsonEncoderOptions.Verbose);
            using var decoder = new JsonDecoder(json, CreateContext());

            AssertSameValues(decoder.ReadDataValueArray("Values"), dropsOrphanPicoseconds: true);
        }

        [Test]
        public void XmlRoundTripKeepsEveryValue()
        {
            using var stringReader = new StringReader(EncodeXml());
            using var reader = XmlReader.Create(stringReader, CoreUtils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(null, reader, CreateContext());
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            AssertSameValues(decoder.ReadDataValueArray("Values"), dropsOrphanPicoseconds: false);
        }

        /// <summary>
        /// The binary encoding writes a default value and an empty one alike, as a
        /// single zero byte, and both decode to a null value.
        /// </summary>
        [Test]
        public void DefaultAndEmptyValuesDecodeAsNullFromBinary()
        {
            ServiceMessageContext context = CreateContext();
            using var encoder = new BinaryEncoder(context);
            encoder.WriteDataValue(null, default);
            encoder.WriteDataValue(null, new DataValue());
            byte[] buffer = encoder.CloseAndReturnBuffer()!;
            using var decoder = new BinaryDecoder(buffer, context);

            DataValue absent = decoder.ReadDataValue(null);
            DataValue empty = decoder.ReadDataValue(null);

            Assert.Multiple(() =>
            {
                Assert.That(CoreUtils.ToHexString(buffer), Is.EqualTo("0000"));
                Assert.That(absent.IsNull, Is.True);
                Assert.That(empty.IsNull, Is.True);
            });
        }

        private static void AssertSameValues(ArrayOf<DataValue> decoded, bool dropsOrphanPicoseconds)
        {
            // binary and JSON do not write picoseconds without their timestamp, XML does.
            // IsNull is not compared: binary decodes an empty value as null and the
            // text encodings may decode a null element as empty, as before.
            DataValue[] expected = Array.ConvertAll(
                CreateValues(),
                v => v.IsNull || !dropsOrphanPicoseconds ? v : new DataValue(
                    v.WrappedValue,
                    v.StatusCode,
                    v.SourceTimestamp,
                    v.ServerTimestamp,
                    v.SourceTimestamp == DateTimeUtc.MinValue ? (ushort)0 : v.SourcePicoseconds,
                    v.ServerTimestamp == DateTimeUtc.MinValue ? (ushort)0 : v.ServerPicoseconds));
            Assert.That(decoded.Count, Is.EqualTo(expected.Length));
            for (int ii = 0; ii < expected.Length; ii++)
            {
                DataValue actual = decoded[ii];
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.EqualTo(expected[ii]), $"value {ii}");
                    Assert.That(actual.StatusCodeValue, Is.EqualTo(expected[ii].StatusCodeValue), $"code {ii}");
                    Assert.That(
                        actual.SourcePicoseconds,
                        Is.EqualTo(expected[ii].SourcePicoseconds),
                        $"source ps {ii}");
                    Assert.That(
                        actual.ServerPicoseconds,
                        Is.EqualTo(expected[ii].ServerPicoseconds),
                        $"server ps {ii}");
                    Assert.That(
                        actual.StatusCode.SymbolicId,
                        Is.EqualTo(expected[ii].StatusCode.SymbolicId),
                        $"symbol {ii}");
                });
            }
        }

        private static string EncodeJson(JsonEncoderOptions options)
        {
            using var encoder = new JsonEncoder(CreateContext(), options);
            encoder.WriteDataValueArray("Values", CreateValues());
            return encoder.CloseAndReturnText();
        }

        private static string EncodeXml()
        {
            using var encoder = new XmlEncoder(
                new XmlQualifiedName("Root", Namespaces.OpcUaXsd),
                null!,
                CreateContext());
            encoder.PushNamespace(Namespaces.OpcUaXsd);
            encoder.WriteDataValueArray("Values", CreateValues());
            encoder.PopNamespace();
            return encoder.CloseAndReturnText()!;
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.Create(NUnitTelemetryContext.Create());
        }

        private static DataValue[] CreateValues()
        {
            var source = new DateTimeUtc(new DateTime(2026, 11, 10, 8, 30, 15, DateTimeKind.Utc));
            var server = new DateTimeUtc(new DateTime(2026, 11, 10, 8, 30, 16, DateTimeKind.Utc));
            return
            [
                // every field set, with picoseconds at both ends of the range.
                new DataValue(new Variant(42.5), StatusCodes.Good, source, server, 1, 9999),
                // uncertain with info bits: data value info type, overflow and limit bits.
                new DataValue(
                    new Variant(7),
                    new StatusCode(StatusCodes.UncertainLastUsableValue.Code | 0x0400 | 0x0080 | 0x0100),
                    source,
                    server,
                    1234,
                    0),
                new DataValue(new Variant("text"), StatusCodes.BadNodeIdUnknown),
                // picoseconds without their timestamp: binary and JSON do not write them.
                new DataValue(
                    Variant.Null,
                    StatusCodes.GoodClamped,
                    DateTimeUtc.MinValue,
                    DateTimeUtc.MinValue,
                    5,
                    6),
                new DataValue(),
                default
            ];
        }

        private const string kExpectedBinary =
            "060000003D0B00000000004045408005C3B5ED70DD010100009C5BB6ED70DD010F271F0607000000" +
            "800590408005C3B5ED70DD01D204009C5BB6ED70DD01030C04000000746578740000348002000030" +
            "000000";

        private const string kExpectedVerboseJson =
            """
            {"Values":[{"UaType":11,"Value":42.5,"SourceTimestamp":"2026-11-10T08:30:15Z","SourcePicoseconds":1,"ServerTimestamp":"2026-11-10T08:30:16Z","ServerPicoseconds":9999},{"UaType":6,"Value":7,"Status":{"Code":1083180416,"Symbol":"UncertainLastUsableValue"},"SourceTimestamp":"2026-11-10T08:30:15Z","SourcePicoseconds":1234,"ServerTimestamp":"2026-11-10T08:30:16Z"},{"UaType":12,"Value":"text","Status":{"Code":2150891520,"Symbol":"BadNodeIdUnknown"}},{"Status":{"Code":3145728,"Symbol":"GoodClamped"}},{},null]}
            """;

        private const string kExpectedCompactJson =
            """
            {"Values":[{"UaType":11,"Value":42.5,"SourceTimestamp":"2026-11-10T08:30:15Z","SourcePicoseconds":1,"ServerTimestamp":"2026-11-10T08:30:16Z","ServerPicoseconds":9999},{"UaType":6,"Value":7,"Status":{"Code":1083180416},"SourceTimestamp":"2026-11-10T08:30:15Z","SourcePicoseconds":1234,"ServerTimestamp":"2026-11-10T08:30:16Z"},{"UaType":12,"Value":"text","Status":{"Code":2150891520}},{"Status":{"Code":3145728}},{},null]}
            """;

        private const string kExpectedXml =
            """
            <?xml version="1.0" encoding="utf-16"?>
            <uax:Root xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
              <uax:Values>
                <uax:DataValue>
                  <uax:Value>
                    <uax:Value>
                      <uax:Double>42.5</uax:Double>
                    </uax:Value>
                  </uax:Value>
                  <uax:StatusCode>
                    <uax:Code>0</uax:Code>
                  </uax:StatusCode>
                  <uax:SourceTimestamp>2026-11-10T08:30:15Z</uax:SourceTimestamp>
                  <uax:SourcePicoseconds>1</uax:SourcePicoseconds>
                  <uax:ServerTimestamp>2026-11-10T08:30:16Z</uax:ServerTimestamp>
                  <uax:ServerPicoseconds>9999</uax:ServerPicoseconds>
                </uax:DataValue>
                <uax:DataValue>
                  <uax:Value>
                    <uax:Value>
                      <uax:Int32>7</uax:Int32>
                    </uax:Value>
                  </uax:Value>
                  <uax:StatusCode>
                    <uax:Code>1083180416</uax:Code>
                  </uax:StatusCode>
                  <uax:SourceTimestamp>2026-11-10T08:30:15Z</uax:SourceTimestamp>
                  <uax:SourcePicoseconds>1234</uax:SourcePicoseconds>
                  <uax:ServerTimestamp>2026-11-10T08:30:16Z</uax:ServerTimestamp>
                  <uax:ServerPicoseconds>0</uax:ServerPicoseconds>
                </uax:DataValue>
                <uax:DataValue>
                  <uax:Value>
                    <uax:Value>
                      <uax:String>text</uax:String>
                    </uax:Value>
                  </uax:Value>
                  <uax:StatusCode>
                    <uax:Code>2150891520</uax:Code>
                  </uax:StatusCode>
                  <uax:SourceTimestamp>0001-01-01T00:00:00Z</uax:SourceTimestamp>
                  <uax:SourcePicoseconds>0</uax:SourcePicoseconds>
                  <uax:ServerTimestamp>0001-01-01T00:00:00Z</uax:ServerTimestamp>
                  <uax:ServerPicoseconds>0</uax:ServerPicoseconds>
                </uax:DataValue>
                <uax:DataValue>
                  <uax:Value>
                    <uax:Value xsi:nil="true" />
                  </uax:Value>
                  <uax:StatusCode>
                    <uax:Code>3145728</uax:Code>
                  </uax:StatusCode>
                  <uax:SourceTimestamp>0001-01-01T00:00:00Z</uax:SourceTimestamp>
                  <uax:SourcePicoseconds>5</uax:SourcePicoseconds>
                  <uax:ServerTimestamp>0001-01-01T00:00:00Z</uax:ServerTimestamp>
                  <uax:ServerPicoseconds>6</uax:ServerPicoseconds>
                </uax:DataValue>
                <uax:DataValue>
                  <uax:Value>
                    <uax:Value xsi:nil="true" />
                  </uax:Value>
                  <uax:StatusCode>
                    <uax:Code>0</uax:Code>
                  </uax:StatusCode>
                  <uax:SourceTimestamp>0001-01-01T00:00:00Z</uax:SourceTimestamp>
                  <uax:SourcePicoseconds>0</uax:SourcePicoseconds>
                  <uax:ServerTimestamp>0001-01-01T00:00:00Z</uax:ServerTimestamp>
                  <uax:ServerPicoseconds>0</uax:ServerPicoseconds>
                </uax:DataValue>
                <uax:DataValue xsi:nil="true" />
              </uax:Values>
            </uax:Root>
            """;
    }
}
