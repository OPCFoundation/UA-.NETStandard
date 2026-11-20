/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.IO;
using NUnit.Framework;
using Opc.Ua.Gpos;

namespace Opc.Ua.Positioning.Tests
{
    [TestFixture]
    [Category("Positioning")]
    public sealed class ModelEncodingTests
    {
        [Test]
        public void GeographicCoordinateRoundTripsBinaryAndJson()
        {
            var value = new S3DGeographicCoordinateDataType
            {
                EncodingMask =
                    (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                Longitude = 8.55,
                Latitude = 47.37,
                Elevation = 408.2
            };

            AssertRoundTrips(value);
        }

        [Test]
        public void GlobalPositionRoundTripsBinaryAndJson()
        {
            var value = new GlobalPositionDataType
            {
                EncodingMask =
                    (uint)S3DGeographicCoordinateDataTypeFields.Elevation |
                    (uint)GlobalPositionDataTypeFields.Accuracy |
                    (uint)GlobalPositionDataTypeFields.Floor,
                Longitude = 8.55,
                Latitude = 47.37,
                Elevation = 408.2,
                Accuracy = 0.04,
                Floor = 2.0f
            };

            AssertRoundTrips(value);
        }

        [Test]
        public void GlobalPositionBinaryEncodingUsesSingleOptionalFieldMask()
        {
            var value = new GlobalPositionDataType
            {
                EncodingMask =
                    (uint)GlobalPositionDataTypeFields.Elevation |
                    (uint)GlobalPositionDataTypeFields.Accuracy |
                    (uint)GlobalPositionDataTypeFields.Floor,
                Longitude = 8.55,
                Latitude = 47.37,
                Elevation = 408.2,
                Accuracy = 0.04,
                Floor = 2.0f
            };
            ServiceMessageContext context = CreateContext();

            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEncodeable("Value", value);
            }

            Assert.That(stream.Length, Is.EqualTo(40));
        }

        [Test]
        public void GlobalLocationRoundTripsBinaryAndJson()
        {
            var value = new GlobalLocationDataType
            {
                EncodingMask =
                    (uint)GlobalLocationDataTypeFields.Orientation,
                Position = new GlobalPositionDataType
                {
                    EncodingMask =
                        (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                    Longitude = 8.55,
                    Latitude = 47.37,
                    Elevation = 408.2
                },
                Orientation = new ThreeDOrientation
                {
                    A = 1.0,
                    B = 2.0,
                    C = 3.0
                }
            };

            AssertRoundTrips(value);
        }

        [Test]
        public void GroundControlPointRoundTripsBinaryAndJson()
        {
            var value = new GroundControlPointDataType
            {
                GlobalPosition = new S3DGeographicCoordinateDataType
                {
                    EncodingMask =
                        (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                    Longitude = 8.55,
                    Latitude = 47.37,
                    Elevation = 408.2
                },
                LocalPosition = new ThreeDCartesianCoordinates
                {
                    X = 1.0,
                    Y = 2.0,
                    Z = 3.0
                }
            };

            AssertRoundTrips(value);
        }

        private static void AssertRoundTrips<T>(T value)
            where T : IEncodeable, new()
        {
            ServiceMessageContext context = CreateContext();

            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEncodeable("Value", value);
            }
            stream.Position = 0;
            using (var decoder = new BinaryDecoder(stream, context, true))
            {
                T decoded = decoder.ReadEncodeable<T>("Value");
                Assert.That(value.IsEqual(decoded), Is.True);
            }

            string json;
            using (var encoder = new JsonEncoder(context))
            {
                encoder.EncodeMessage(value, value.TypeId);
                json = encoder.CloseAndReturnText();
            }
            using (var decoder = new JsonDecoder(json, context))
            {
                T decoded = decoder.DecodeMessage<T>();
                Assert.That(value.IsEqual(decoded), Is.True);
            }
        }

        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(Rsl.Namespaces.RSL);
            context.NamespaceUris.GetIndexOrAppend(Gpos.Namespaces.GPOS);
            context.Factory.Builder.AddOpcUaGpos().Commit();
            return context;
        }
    }
}
