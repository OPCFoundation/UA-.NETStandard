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
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Round-trips the AutoID DataTypes through the registered binary, JSON and XML codecs.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdEncodingTests
    {
        /// <summary>
        /// Every arm of the ScanData union survives every encoding inside an extension object.
        /// </summary>
        [Test]
        public void ScanDataUnionArmsRoundTrip(
            [Values("Binary", "Json", "Xml")] string encoding,
            [Values] ScanDataFields arm)
        {
            ScanData expected = CreateScanData(arm);
            ScanData actual = RoundTrip(expected, encoding);
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.Not.SameAs(expected));
                Assert.That(actual.SwitchField, Is.EqualTo(arm));
                Assert.That(actual.IsEqual(expected), Is.True);
            });
            switch (arm)
            {
                case ScanDataFields.ByteString:
                    Assert.That(actual.ByteString, Is.EqualTo(ByteString.From(0xE2, 0x00, 0x34, 0x12)));
                    break;
                case ScanDataFields.String:
                    Assert.That(actual.String, Is.EqualTo("4012345678901"));
                    break;
                case ScanDataFields.Epc:
                    Assert.That(actual.Epc.PC, Is.EqualTo(0x3000));
                    Assert.That(actual.Epc.UId, Is.EqualTo(ByteString.From(0x30, 0x14, 0x15, 0x9E)));
                    Assert.That(actual.Epc.XPC_W2, Is.EqualTo(7));
                    break;
                case ScanDataFields.Custom:
                    Assert.That(actual.Custom.TryGetValue(out int custom), Is.True);
                    Assert.That(custom, Is.EqualTo(42));
                    break;
            }
        }

        /// <summary>
        /// The Location union selects one coordinate form and round-trips it.
        /// </summary>
        [Test]
        public void LocationUnionArmsRoundTrip(
            [Values("Binary", "Json", "Xml")] string encoding,
            [Values(LocationFields.NMEA, LocationFields.Local, LocationFields.WGS84, LocationFields.Name)]
            LocationFields arm)
        {
            Location expected = CreateLocation(arm);
            Location actual = RoundTrip(expected, encoding);
            Assert.Multiple(() =>
            {
                Assert.That(actual.SwitchField, Is.EqualTo(arm));
                Assert.That(actual.IsEqual(expected), Is.True);
            });
        }

        /// <summary>
        /// Each concrete scan result round-trips polymorphically as an extension object, with and
        /// without its optional fields.
        /// </summary>
        [Test]
        public void ScanResultSubtypesRoundTripPolymorphically(
            [Values("Binary", "Json", "Xml")] string encoding,
            [Values("Rfid", "Ocr", "Optical", "OpticalVerifier", "Rtls")] string kind,
            [Values] bool withOptionalFields)
        {
            ScanResult expected = CreateScanResult(kind, withOptionalFields);
            ScanResult actual = RoundTrip(expected, encoding);
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.TypeOf(expected.GetType()));
                Assert.That(actual.TypeId, Is.EqualTo(expected.TypeId));
                Assert.That(actual.EncodingMask, Is.EqualTo(expected.EncodingMask));
                Assert.That(actual.IsEqual(expected), Is.True);
                Assert.That(actual.ScanData.String, Is.EqualTo("4012345678901"));
            });
            if (withOptionalFields)
            {
                Assert.That(actual.Location.SwitchField, Is.EqualTo(LocationFields.Local));
                Assert.That(actual.Location.Local.X, Is.EqualTo(1.5));
            }
        }

        /// <summary>
        /// The RFID access result inherits the optional fields of AccessResult and adds its own
        /// under one encoding mask.
        /// </summary>
        [Test]
        public void RfidAccessResultOptionalFieldsRoundTrip(
            [Values("Binary", "Json", "Xml")] string encoding,
            [Values] bool withOptionalFields)
        {
            RfidAccessResult expected = CreateRfidAccessResult(withOptionalFields);
            RfidAccessResult actual = RoundTrip(expected, encoding);
            Assert.Multiple(() =>
            {
                Assert.That(actual.EncodingMask, Is.EqualTo(expected.EncodingMask));
                Assert.That(actual.IsEqual(expected), Is.True);
            });
            if (withOptionalFields)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(actual.Identifier.Epc.UId, Is.EqualTo(ByteString.From(0x30, 0x14, 0x15, 0x9E)));
                    Assert.That(actual.RWData.ByteString, Is.EqualTo(ByteString.From(0xCA, 0xFE)));
                    Assert.That(actual.Antenna, Is.EqualTo(3));
                    Assert.That(actual.Polarization, Is.EqualTo("circular"));
                });
            }
        }

        /// <summary>
        /// The structures without optional fields round-trip in every encoding.
        /// </summary>
        [Test]
        public void PlainStructuresRoundTrip([Values("Binary", "Json", "Xml")] string encoding)
        {
            var settings = new ScanSettings
            {
                Duration = 250,
                Cycles = 3,
                DataAvailable = true,
                LocationType = LocationTypeEnumeration.WGS84,
                EncodingMask = (uint)ScanSettingsFields.LocationType
            };
            var antenna = new AntennaNameIdPair { AntennaId = 4, AntennaName = "Gate 4" };
            var geo = new DhcpGeoConfCoordinate
            {
                LaRes = 34,
                LatitudeInteger = 48,
                LatitudeFraction = 1234,
                LoRes = 34,
                LongitudeInteger = 11,
                LongitudeFraction = 5678,
                AT = 1,
                AltRes = 30,
                AltitudeInteger = 520,
                AltitudeFraction = 12,
                Datum = 1
            };
            Assert.Multiple(() =>
            {
                Assert.That(RoundTrip(settings, encoding).IsEqual(settings), Is.True);
                Assert.That(RoundTrip(antenna, encoding).IsEqual(antenna), Is.True);
                Assert.That(RoundTrip(geo, encoding).IsEqual(geo), Is.True);
            });
        }

        /// <summary>
        /// The binary layout follows OPC 10000-6: the encoding mask first, then the fields in the
        /// order the DataTypeDefinition lists them, inherited fields before the subtype's.
        /// </summary>
        [Test]
        public void RfidAccessResultBinaryLayoutFollowsTheDefinition()
        {
            ServiceMessageContext context = AutoIdTestContext.CreateMessageContext();
            var value = new RfidAccessResult
            {
                CodeType = "EPC",
                Antenna = 3,
                Strength = -40,
                EncodingMask = (uint)(RfidAccessResultFields.CodeType |
                    RfidAccessResultFields.Antenna |
                    RfidAccessResultFields.Strength)
            };
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEncodeable("Value", value);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadUInt32(null), Is.EqualTo(0x1u | 0x20u | 0x200u));
                Assert.That(decoder.ReadString(null), Is.EqualTo("EPC"));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(3));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(-40));
                Assert.That(stream.Position, Is.EqualTo(stream.Length));
            });
        }

        /// <summary>
        /// The ScanData union writes its switch field and then only the selected arm.
        /// </summary>
        [Test]
        public void ScanDataBinaryLayoutIsSwitchFieldThenArm()
        {
            ServiceMessageContext context = AutoIdTestContext.CreateMessageContext();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEncodeable("Value", CreateScanData(ScanDataFields.String));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadUInt32(null), Is.EqualTo(2u));
                Assert.That(decoder.ReadString(null), Is.EqualTo("4012345678901"));
                Assert.That(stream.Position, Is.EqualTo(stream.Length));
            });
        }

        /// <summary>
        /// Enumerations encode their published Int32 values.
        /// </summary>
        [Test]
        public void EnumerationsEncodeTheirPublishedNumericValues()
        {
            ServiceMessageContext context = AutoIdTestContext.CreateMessageContext();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEnumerated("Status", AutoIdOperationStatusEnumeration.TAG_HAS_LOW_BATTERY);
                encoder.WriteEnumerated("Device", DeviceStatusEnumeration.Scanning);
                encoder.WriteEnumerated("Location", LocationTypeEnumeration.NAME);
                encoder.WriteEnumerated("Lock", RfidLockOperationEnumeration.PermanentUnlock);
                encoder.WriteEnumerated("Region", RfidLockRegionEnumeration.User);
                encoder.WriteEnumerated("Password", RfidPasswordTypeEnumeration.Write);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            Assert.Multiple(() =>
            {
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(21));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(2));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(3));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(3));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(4));
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(3));
            });
        }

        /// <summary>
        /// The encodeable factory resolves every structure by DataType and binary encoding id after
        /// registration, and does not know them without it.
        /// </summary>
        [TestCase(3017u, 5022u, typeof(AccessResult))]
        [TestCase(3018u, 5024u, typeof(RfidAccessResult))]
        [TestCase(3020u, 0u, typeof(ScanData))]
        [TestCase(3008u, 0u, typeof(Location))]
        [TestCase(3007u, 0u, typeof(RfidScanResult))]
        [TestCase(3002u, 0u, typeof(OcrScanResult))]
        [TestCase(3026u, 0u, typeof(OpticalScanResult))]
        [TestCase(3030u, 0u, typeof(OpticalVerifierScanResult))]
        [TestCase(3028u, 0u, typeof(RtlsLocationResult))]
        [TestCase(3006u, 0u, typeof(RfidSighting))]
        [TestCase(3024u, 0u, typeof(ScanDataEpc))]
        [TestCase(3010u, 0u, typeof(ScanSettings))]
        [TestCase(3027u, 0u, typeof(WGS84Coordinate))]
        public void EncodeableFactoryKnowsTheTypes(uint dataTypeId, uint binaryEncodingId, Type expected)
        {
            ServiceMessageContext registered = AutoIdTestContext.CreateMessageContext(reverseNamespaces: true);
            ServiceMessageContext unregistered = AutoIdTestContext.CreateMessageContext(registerModel: false);
            var typeId = new ExpandedNodeId(dataTypeId, AutoIdModel.NamespaceUri);
            Assert.That(registered.Factory.TryGetEncodeableType(typeId, out IEncodeableType? type), Is.True);
            IEncodeable instance = type!.CreateInstance();
            Assert.Multiple(() =>
            {
                Assert.That(type.Type, Is.EqualTo(expected));
                Assert.That(instance, Is.TypeOf(expected));
                Assert.That(instance.TypeId, Is.EqualTo(typeId));
                Assert.That(
                    registered.Factory.TryGetEncodeableType(instance.BinaryEncodingId, out IEncodeableType? byEncoding),
                    Is.True);
                Assert.That(byEncoding!.Type, Is.EqualTo(expected));
                Assert.That(unregistered.Factory.TryGetEncodeableType(typeId, out _), Is.False);
            });
            if (binaryEncodingId != 0)
            {
                Assert.That(instance.BinaryEncodingId,
                    Is.EqualTo(new ExpandedNodeId(binaryEncodingId, AutoIdModel.NamespaceUri)));
            }
        }

        /// <summary>
        /// The enumerations are registered as enumerated types.
        /// </summary>
        [Test]
        public void EncodeableFactoryKnowsTheEnumerations()
        {
            ServiceMessageContext context = AutoIdTestContext.CreateMessageContext();
            Assert.Multiple(() =>
            {
                foreach (uint id in new uint[] { 3013, 3003, 3009, 3016, 3015, 3014 })
                {
                    Assert.That(context.Factory.TryGetEnumeratedType(
                        new ExpandedNodeId(id, AutoIdModel.NamespaceUri), out _), Is.True, $"i={id}");
                }
            });
        }

        /// <summary>
        /// Without registration an AutoID body stays an opaque extension object.
        /// </summary>
        [Test]
        public void CoreOnlyFactoryLeavesScanResultOpaque()
        {
            ServiceMessageContext writer = AutoIdTestContext.CreateMessageContext();
            ServiceMessageContext reader = AutoIdTestContext.CreateMessageContext(registerModel: false);
            reader.NamespaceUris = writer.NamespaceUris;
            ScanResult expected = CreateScanResult("Rfid", true);
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, writer, true))
            {
                encoder.WriteExtensionObject("Value", new ExtensionObject(expected));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, reader, true);
            ExtensionObject actual = decoder.ReadExtensionObject("Value");
            Assert.Multiple(() =>
            {
                Assert.That(actual.TypeId, Is.EqualTo(expected.BinaryEncodingId));
                Assert.That(actual.Encoding, Is.EqualTo(ExtensionObjectEncoding.Binary));
                Assert.That(actual.TryGetValue(out RfidScanResult? _), Is.False);
            });
        }

        private static ScanData CreateScanData(ScanDataFields arm)
        {
            return arm switch
            {
                ScanDataFields.ByteString => new ScanData
                {
                    SwitchField = arm,
                    ByteString = ByteString.From(0xE2, 0x00, 0x34, 0x12)
                },
                ScanDataFields.String => new ScanData { SwitchField = arm, String = "4012345678901" },
                ScanDataFields.Epc => new ScanData
                {
                    SwitchField = arm,
                    Epc = new ScanDataEpc
                    {
                        PC = 0x3000,
                        UId = ByteString.From(0x30, 0x14, 0x15, 0x9E),
                        XPC_W1 = 0,
                        XPC_W2 = 7
                    }
                },
                ScanDataFields.Custom => new ScanData { SwitchField = arm, Custom = new Variant(42) },
                _ => new ScanData()
            };
        }

        private static Location CreateLocation(LocationFields arm)
        {
            return arm switch
            {
                LocationFields.NMEA => new Location
                {
                    SwitchField = arm,
                    NMEA = "$GPGGA,092750.000,5321.6802,N,00630.3372,W,1,8,1.03,61.7,M,55.2,M,,*76"
                },
                LocationFields.Local => new Location
                {
                    SwitchField = arm,
                    Local = new LocalCoordinate
                    {
                        X = 1.5,
                        Y = -2.25,
                        Z = 0.75,
                        Timestamp = new DateTimeUtc(2020, 6, 18, 13, 52, 3),
                        DilutionOfPrecision = 0.5,
                        UsefulPrecision = 2
                    }
                },
                LocationFields.WGS84 => new Location
                {
                    SwitchField = arm,
                    WGS84 = new WGS84Coordinate
                    {
                        N_S_Hemisphere = "N",
                        Latitude = 48.137,
                        E_W_Hemisphere = "E",
                        Longitude = 11.575,
                        Altitude = 519.0,
                        Timestamp = new DateTimeUtc(2020, 6, 18, 13, 52, 3),
                        DilutionOfPrecision = 1.2,
                        UsefulPrecisionLatLon = 5,
                        UsefulPrecisionAlt = 10
                    }
                },
                _ => new Location { SwitchField = arm, Name = "Dock 7" }
            };
        }

        private static ScanResult CreateScanResult(string kind, bool withOptionalFields)
        {
            ScanResult result;
            uint optionalMask = 0;
            switch (kind)
            {
                case "Rfid":
                    result = new RfidScanResult
                    {
                        Sighting =
                        [
                            new RfidSighting
                            {
                                Antenna = 1,
                                Strength = -52,
                                Timestamp = new DateTimeUtc(2020, 6, 18, 13, 52, 3),
                                CurrentPowerLevel = 27
                            },
                            new RfidSighting { Antenna = 2, Strength = -61 }
                        ]
                    };
                    break;
                case "Ocr":
                    result = new OcrScanResult
                    {
                        ImageId = new NodeId(42u, 2),
                        Quality = 87,
                        Position = new Position { PositionX = 10, PositionY = 20, SizeX = 300, SizeY = 40 },
                        Font = "OCR-B",
                        DecodingTime = new DateTimeUtc(2020, 6, 18, 13, 52, 4)
                    };
                    optionalMask = (uint)(OcrScanResultFields.Font | OcrScanResultFields.DecodingTime);
                    break;
                case "Optical":
                    result = new OpticalScanResult
                    {
                        Grade = 3.5f,
                        Symbology = "GS1 DataMatrix",
                        ImageId = new NodeId("image-7", 2)
                    };
                    optionalMask = (uint)(OpticalScanResultFields.Grade |
                        OpticalScanResultFields.Symbology |
                        OpticalScanResultFields.ImageId);
                    break;
                case "OpticalVerifier":
                    result = new OpticalVerifierScanResult
                    {
                        Grade = 4.0f,
                        Symbology = "GS1-128",
                        IsoGrade = "A",
                        RMin = 4,
                        SymbolContrast = 4,
                        ECMin = 4,
                        Modulation = 3,
                        Defects = 4,
                        Decodability = 4,
                        Decode_ = 4,
                        PrintGain = 12
                    };
                    optionalMask = (uint)(OpticalScanResultFields.Grade | OpticalScanResultFields.Symbology);
                    break;
                default:
                    result = new RtlsLocationResult
                    {
                        Speed = 1.25,
                        Heading = 90.0,
                        Rotation = new Rotation { Yaw = 0.1, Pitch = 0.2, Roll = 0.3 },
                        ReceiveTime = new DateTimeUtc(2020, 6, 18, 13, 52, 5)
                    };
                    break;
            }
            result.CodeType = "GTIN";
            result.ScanData = CreateScanData(ScanDataFields.String);
            result.Timestamp = new DateTimeUtc(2020, 6, 18, 13, 52, 3);
            result.Location = CreateLocation(LocationFields.Local);
            result.EncodingMask = withOptionalFields ? (uint)ScanResultFields.Location | optionalMask : 0u;
            return result;
        }

        private static RfidAccessResult CreateRfidAccessResult(bool withOptionalFields)
        {
            return new RfidAccessResult
            {
                CodeType = "EPC",
                Identifier = CreateScanData(ScanDataFields.Epc),
                Timestamp = new DateTimeUtc(2020, 6, 18, 13, 52, 3),
                CodeTypeRWData = "UTF-8",
                RWData = new ScanData
                {
                    SwitchField = ScanDataFields.ByteString,
                    ByteString = ByteString.From(0xCA, 0xFE)
                },
                Antenna = 3,
                CurrentPowerLevel = 30,
                PC = 0x3000,
                Polarization = "circular",
                Strength = -40,
                EncodingMask = withOptionalFields ? 0x3FFu : 0u
            };
        }

        private static T RoundTrip<T>(T value, string encoding)
            where T : class, IEncodeable
        {
            ServiceMessageContext context = AutoIdTestContext.CreateMessageContext(reverseNamespaces: true);
            ExtensionObject decoded;
            switch (encoding)
            {
                case "Binary":
                    using (var stream = new MemoryStream())
                    {
                        using (var encoder = new BinaryEncoder(stream, context, true))
                        {
                            encoder.WriteExtensionObject("Value", new ExtensionObject(value));
                        }
                        stream.Position = 0;
                        using var decoder = new BinaryDecoder(stream, context, true);
                        decoded = decoder.ReadExtensionObject("Value");
                    }
                    break;
                case "Json":
                    string json;
                    using (var encoder = new JsonEncoder(context))
                    {
                        encoder.WriteExtensionObject("Value", new ExtensionObject(value));
                        json = encoder.CloseAndReturnText();
                    }
                    using (var decoder = new JsonDecoder(json, context))
                    {
                        decoded = decoder.ReadExtensionObject("Value");
                    }
                    break;
                case "Xml":
                    string xml;
                    using (var encoder = new XmlEncoder(context))
                    {
                        encoder.PushNamespace(Ua.Namespaces.OpcUaXsd);
                        encoder.WriteExtensionObject("Value", new ExtensionObject(value));
                        encoder.PopNamespace();
                        xml = encoder.CloseAndReturnText()!;
                    }
                    using (var text = new StringReader(xml))
                    using (var reader = XmlReader.Create(text, CoreUtils.DefaultXmlReaderSettings()))
                    using (var decoder = new XmlDecoder(reader, context))
                    {
                        decoder.PushNamespace(Ua.Namespaces.OpcUaXsd);
                        decoded = decoder.ReadExtensionObject("Value");
                        decoder.PopNamespace();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding));
            }
            Assert.That(decoded.TryGetValue(out T? result), Is.True, $"{typeof(T).Name} via {encoding}");
            return result!;
        }
    }
}
