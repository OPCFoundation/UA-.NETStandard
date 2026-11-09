/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises the released WMTP structure through real codecs and generated factory registrations.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    public sealed class ModelEncodingTests
    {
        /// <summary>
        /// Non-default values in all fifteen fields survive each supported wire encoding.
        /// </summary>
        [TestCase("Binary")]
        [TestCase("Json")]
        [TestCase("Xml")]
        public void CompleteWmtpOutputRoundTripsThroughRegisteredFactory(string encoding)
        {
            WMTPOutputDataType expected = CreateOutput();
            WMTPOutputDataType actual = RoundTrip(expected, encoding);
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.Not.SameAs(expected));
                Assert.That(actual.IsEqual(expected), Is.True);
                Assert.That(actual.TypeId, Is.EqualTo(new ExpandedNodeId(3003, WMTP.Namespaces.WMTP)));
                Assert.That(actual.EngineeringUnits.UnitId, Is.EqualTo(5066068));
                Assert.That(actual.ActualValue, Is.EqualTo(-12.75));
                Assert.That(actual.TypeOfMeasurement, Is.EqualTo(6));
                Assert.That(actual.TypeOfSample, Is.EqualTo(3));
                Assert.That(actual.Timestamp, Is.EqualTo(new DateTimeUtc(2025, 11, 1, 12, 34, 56, 789)));
                Assert.That(actual.Index, Is.EqualTo(1234));
                Assert.That(actual.MeasurementPeriod, Is.EqualTo(250.5));
                Assert.That(actual.InternalUpdateInterval, Is.EqualTo(62.5));
                Assert.That(actual.InstrumentRange, Is.Not.SameAs(expected.InstrumentRange));
                Assert.That(actual.EURange, Is.Not.SameAs(expected.EURange));
            });
        }

        /// <summary>
        /// Ancillary fields may retain the standard defaults without losing the supplied measurement identity.
        /// </summary>
        [TestCase("Binary")]
        [TestCase("Json")]
        [TestCase("Xml")]
        public void WmtpOutputAllowsAncillaryDefaults(string encoding)
        {
            var expected = new WMTPOutputDataType
            {
                EngineeringUnits = new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 4408652,
                    DisplayName = new LocalizedText("en", "°C")
                },
                ActualValue = 21.25,
                TypeOfMeasurement = 0,
                TypeOfSample = 1
            };
            WMTPOutputDataType actual = RoundTrip(expected, encoding);
            Assert.Multiple(() =>
            {
                Assert.That(actual.IsEqual(expected), Is.True);
                Assert.That(actual.ActualValue, Is.EqualTo(21.25));
                Assert.That(actual.TypeOfSample, Is.EqualTo(1));
                Assert.That(actual.Timestamp, Is.EqualTo(DateTimeUtc.MinValue));
                Assert.That(actual.Index, Is.Zero);
                Assert.That(actual.MeasurementPeriod, Is.Zero);
                Assert.That(actual.InternalUpdateInterval, Is.Zero);
                Assert.That(actual.RelativeUncertainty, Is.Zero);
                Assert.That(actual.AbsoluteUncertainty, Is.Zero);
                Assert.That(actual.InstrumentRange.Low, Is.Zero);
                Assert.That(actual.EURange.High, Is.Zero);
                Assert.That(actual.Definition, Is.Null);
                Assert.That(actual.SignalTag, Is.Null);
            });
        }

        /// <summary>
        /// Binary encoding follows the published field order, not merely a symmetric encoder/decoder mistake.
        /// </summary>
        [Test]
        public void WmtpOutputBinaryFieldOrderMatchesReleasedDefinition()
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(MachineToolParts.Wmtp);
            WMTPOutputDataType expected = CreateOutput();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEncodeable("Output", expected);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadEncodeable<EUInformation>(null).UnitId, Is.EqualTo(5066068));
                Assert.That(decoder.ReadDouble(null), Is.EqualTo(-12.75));
                Assert.That(decoder.ReadUInt32(null), Is.EqualTo(6));
                Assert.That(decoder.ReadUInt32(null), Is.EqualTo(3));
                Assert.That(decoder.ReadEncodeable<Range>(null).Low, Is.EqualTo(-100));
                Assert.That(decoder.ReadEncodeable<Range>(null).High, Is.EqualTo(80));
                Assert.That(decoder.ReadDouble(null), Is.EqualTo(0.125));
                Assert.That(decoder.ReadString(null), Is.EqualTo("Acquired displacement"));
                Assert.That(decoder.ReadString(null), Is.EqualTo("head-2/displacement"));
                Assert.That(decoder.ReadDouble(null), Is.EqualTo(0.02));
                Assert.That(decoder.ReadDouble(null), Is.EqualTo(0.005));
                Assert.That(decoder.ReadDateTime(null), Is.EqualTo(new DateTimeUtc(2025, 11, 1, 12, 34, 56, 789)));
                Assert.That(decoder.ReadUInt32(null), Is.EqualTo(1234));
                Assert.That(decoder.ReadDouble(null), Is.EqualTo(250.5));
                Assert.That(decoder.ReadDouble(null), Is.EqualTo(62.5));
                Assert.That(stream.Position, Is.EqualTo(stream.Length));
            });
        }

        /// <summary>
        /// Report payload arrays preserve empty, singleton and multi-record cardinality, order and independent copies.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void WmtpOutputArraysRoundTripWithoutReorderingOrAliasing(int count)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(MachineToolParts.All);
            ArrayOf<WMTPOutputDataType> expected = Enumerable.Range(0, count)
                .Select(index =>
                {
                    WMTPOutputDataType record = CreateOutput();
                    record.Index = (uint)index + 17;
                    record.ActualValue = 20 + index;
                    return record;
                }).ToArrayOf();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEncodeableArray("Records", expected);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            ArrayOf<WMTPOutputDataType> actual = decoder.ReadEncodeableArray<WMTPOutputDataType>("Records");
            Assert.That(actual.Count, Is.EqualTo(count));
            for (int i = 0; i < count; i++)
            {
                Assert.That(actual[i].Index, Is.EqualTo(i + 17));
                Assert.That(actual[i].ActualValue, Is.EqualTo(i + 20));
                Assert.That(actual[i].IsEqual(expected[i]), Is.True);
                Assert.That(actual[i], Is.Not.SameAs(expected[i]));
            }
        }

        /// <summary>
        /// A MachineTool-only decoder cannot resolve WMTP types from the assembly's unrelated registrations.
        /// </summary>
        [Test]
        public void MachineToolOnlyFactoryLeavesWmtpBinaryBodyOpaque()
        {
            ServiceMessageContext writerContext = ModelTestContext.CreateMessageContext(MachineToolParts.Wmtp);
            ServiceMessageContext readerContext = ModelTestContext.CreateMessageContext(MachineToolParts.MachineTool);
            readerContext.NamespaceUris = writerContext.NamespaceUris;
            WMTPOutputDataType expected = CreateOutput();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, writerContext, true))
            {
                encoder.WriteExtensionObject("Output", new ExtensionObject(expected));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, readerContext, true);
            ExtensionObject decoded = decoder.ReadExtensionObject("Output");
            Assert.Multiple(() =>
            {
                Assert.That(decoded.TypeId, Is.EqualTo(expected.BinaryEncodingId));
                Assert.That(decoded.TryGetValue(out WMTPOutputDataType? _), Is.False);
                Assert.That(decoded.Encoding, Is.EqualTo(ExtensionObjectEncoding.Binary));
            });
        }

        private static WMTPOutputDataType RoundTrip(WMTPOutputDataType value, string encoding)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(MachineToolParts.Wmtp);
            ExtensionObject decoded;
            switch (encoding)
            {
                case "Binary":
                    using (var stream = new MemoryStream())
                    {
                        using (var encoder = new BinaryEncoder(stream, context, true))
                        {
                            encoder.WriteExtensionObject("Output", new ExtensionObject(value));
                        }
                        stream.Position = 0;
                        using var decoder = new BinaryDecoder(stream, context, true);
                        decoded = decoder.ReadExtensionObject("Output");
                    }
                    break;
                case "Json":
                    string json;
                    using (var encoder = new JsonEncoder(context))
                    {
                        encoder.WriteExtensionObject("Output", new ExtensionObject(value));
                        json = encoder.CloseAndReturnText();
                    }
                    using (var decoder = new JsonDecoder(json, context))
                    {
                        decoded = decoder.ReadExtensionObject("Output");
                    }
                    break;
                case "Xml":
                    string xml;
                    using (var encoder = new XmlEncoder(context))
                    {
                        encoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);
                        encoder.WriteExtensionObject("Output", new ExtensionObject(value));
                        encoder.PopNamespace();
                        xml = encoder.CloseAndReturnText()!;
                    }
                    using (var text = new StringReader(xml))
                    using (var reader = XmlReader.Create(text, CoreUtils.DefaultXmlReaderSettings()))
                    using (var decoder = new XmlDecoder(reader, context))
                    {
                        decoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);
                        decoded = decoder.ReadExtensionObject("Output");
                        decoder.PopNamespace();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding));
            }
            Assert.That(decoded.TryGetValue(out WMTPOutputDataType? result), Is.True);
            return result!;
        }

        private static WMTPOutputDataType CreateOutput()
        {
            return new WMTPOutputDataType
            {
                EngineeringUnits = new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 5066068,
                    DisplayName = new LocalizedText("en", "mm"),
                    Description = new LocalizedText("en", "millimetre")
                },
                ActualValue = -12.75,
                TypeOfMeasurement = 6,
                TypeOfSample = 3,
                InstrumentRange = new Range { Low = -100, High = 100 },
                EURange = new Range { Low = -80, High = 80 },
                ValuePrecision = 0.125,
                Definition = "Acquired displacement",
                SignalTag = "head-2/displacement",
                RelativeUncertainty = 0.02,
                AbsoluteUncertainty = 0.005,
                Timestamp = new DateTimeUtc(2025, 11, 1, 12, 34, 56, 789),
                Index = 1234,
                MeasurementPeriod = 250.5,
                InternalUpdateInterval = 62.5
            };
        }
    }
}
