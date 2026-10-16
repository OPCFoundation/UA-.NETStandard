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
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning.Client;
using GlobalLocationValue = Opc.Ua.Positioning.Client.GlobalLocationValue;
using GlobalPositionValue = Opc.Ua.Positioning.Client.GlobalPositionValue;

namespace Opc.Ua.Positioning.Tests
{
    public sealed partial class PositioningStrictReadTests
    {
        [Test]
        public async Task StrictGlobalPositionPreservesCoordinatesMetadataQualityAndTimestampAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            SetupRead(session, GlobalValue);
            GlobalPositionValue result = await Global(session).ReadGlobalPositionAsync(s_position)
                .ConfigureAwait(false);
            Assert.Multiple(
                () =>
                {
                    Assert.That(result.NodeId, Is.EqualTo(s_position));
                    Assert.That(result.Position.Longitude, Is.EqualTo(8));
                    Assert.That(result.Position.Latitude, Is.EqualTo(47));
                    Assert.That(result.SourceNodeId, Is.EqualTo(s_zone));
                    Assert.That(result.CoordinateReferenceSystem, Is.EqualTo(4326u));
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(result.SourceTimestamp, Is.EqualTo(s_timestamp));
                });
            VerifyBorrowed(session);
        }

        [TestCaseSource(nameof(GlobalQualityCases))]
        public async Task GlobalStructuresAndMetadataRequireGoodQualityBeforeDecodingAsync(
            string component,
            StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            NodeId badNode = component switch
            {
                "source" => s_source,
                "crs" => s_crs,
                "location" => s_location,
                "gcp" => s_gcps,
                _ => s_position
            };
            SetupRead(
                session,
                node =>
                {
                    DataValue value = node == s_gcps ? PointsValue(ControlPoints()) : GlobalValue(node);
                    if (node == badNode)
                    {
                        value = value.WithStatus(status);
                    }
                    return value;
                });
            GlobalPositioningClient client = Global(session);
            await AssertStatusAsync(
                async () =>
                {
                    switch (component)
                    {
                        case "location":
                        case "position-component":
                            await client.ReadGlobalLocationAsync(s_location).ConfigureAwait(false);
                            break;
                        case "gcp":
                            await client.ReadZoneTransformAsync(s_zone).ConfigureAwait(false);
                            break;
                        default:
                            await client.ReadGlobalPositionAsync(s_position).ConfigureAwait(false);
                            break;
                    }
                },
                status).ConfigureAwait(false);
            VerifyBorrowed(session);
        }

        [Test]
        public async Task StatusCodeCannotMasqueradeAsUInt32CrsButLegacyOverloadStillAcceptsItAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            SetupRead(
                session,
                node => node == s_crs
                    ? new DataValue(new Variant(new StatusCode(4326u)))
                    : GlobalValue(node));
            await AssertStatusAsync(
                async () => await Global(session).ReadGlobalPositionAsync(s_position)
                    .ConfigureAwait(false),
                StatusCodes.BadTypeMismatch).ConfigureAwait(false);
            var legacy = new GlobalPositioningClient(session.Object, Mock.Of<ITelemetryContext>());
            GlobalPositionValue result = await legacy.ReadGlobalPositionAsync(s_position).ConfigureAwait(false);
            Assert.That(result.CoordinateReferenceSystem, Is.EqualTo(4326u));
        }

        [Test]
        public async Task NonWgs84CrsIsPreservedWithoutApplyingWgs84DegreeRangesAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            SetupRead(
                session,
                node => node == s_crs
                    ? new DataValue(new Variant(3857u))
                    : node == s_position ? Structure(Position(1000, 2000)) : GlobalValue(node));
            GlobalPositionValue result = await Global(session).ReadGlobalPositionAsync(s_position)
                .ConfigureAwait(false);
            Assert.Multiple(
                () =>
                {
                    Assert.That(result.CoordinateReferenceSystem, Is.EqualTo(3857u));
                    Assert.That(result.Position.Longitude, Is.EqualTo(1000));
                    Assert.That(result.Position.Latitude, Is.EqualTo(2000));
                });
        }

        [TestCase(double.NaN, 47)]
        [TestCase(double.PositiveInfinity, 47)]
        [TestCase(8, double.NegativeInfinity)]
        [TestCase(180.001, 47)]
        [TestCase(-180.001, 47)]
        [TestCase(8, 90.001)]
        [TestCase(8, -90.001)]
        public async Task StrictWgs84ReadRejectsInvalidGeometryAsync(double longitude, double latitude)
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            SetupRead(
                session,
                node => node == s_position
                    ? Structure(Position(longitude, latitude)) : GlobalValue(node));
            await AssertStatusAsync(
                async () => await Global(session).ReadGlobalPositionAsync(s_position)
                    .ConfigureAwait(false),
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
        }

        [TestCase(-180, -90)]
        [TestCase(180, 90)]
        [TestCase(0, 0)]
        public async Task StrictWgs84ReadAcceptsBoundaryAndOriginCoordinatesAsync(double longitude, double latitude)
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            SetupRead(
                session,
                node => node == s_position
                    ? Structure(Position(longitude, latitude)) : GlobalValue(node));
            GlobalPositionValue result = await Global(session).ReadGlobalPositionAsync(s_position)
                .ConfigureAwait(false);
            Assert.That(result.Position.Longitude, Is.EqualTo(longitude));
            Assert.That(result.Position.Latitude, Is.EqualTo(latitude));
        }

        [TestCase("elevation")]
        [TestCase("accuracy")]
        [TestCase("negative-accuracy")]
        [TestCase("floor")]
        public async Task StrictGlobalPositionValidatesEncodedOptionalFieldsAsync(string field)
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            GlobalPositionDataType position = Position();
            position.EncodingMask = field switch
            {
                "elevation" => (uint)GlobalPositionDataTypeFields.Elevation,
                "floor" => (uint)GlobalPositionDataTypeFields.Floor,
                _ => (uint)GlobalPositionDataTypeFields.Accuracy
            };
            position.Elevation = double.NaN;
            position.Accuracy = field == "negative-accuracy" ? -1 : double.NaN;
            position.Floor = float.NaN;
            SetupRead(session, node => node == s_position ? Structure(position) : GlobalValue(node));
            await AssertStatusAsync(
                async () => await Global(session).ReadGlobalPositionAsync(s_position)
                    .ConfigureAwait(false),
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
        }

        [Test]
        public async Task UnencodedOptionalFieldsAreNotInventedOrValidatedAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            GlobalPositionDataType position = Position();
            position.Elevation = double.NaN;
            position.Accuracy = double.NaN;
            position.Floor = float.NaN;
            SetupRead(session, node => node == s_position ? Structure(position) : GlobalValue(node));
            GlobalPositionValue result = await Global(session).ReadGlobalPositionAsync(s_position)
                .ConfigureAwait(false);
            Assert.That(result.Position.EncodingMask, Is.Zero);
            Assert.That(result.Position.Elevation, Is.NaN);
            Assert.That(result.Position.Accuracy, Is.NaN);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task GlobalLocationPreservesThreeAttemptCoherenceAndReturnsTheMatchingAggregateAsync(int attempts)
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            int aggregateReads = 0;
            int componentReads = 0;
            SetupRead(
                session,
                node =>
                {
                    if (node == s_location)
                    {
                        aggregateReads++;
                        return Structure(
                            new GlobalLocationDataType
                            {
                                Position = Position(aggregateReads < attempts ? 7 : 8)
                            });
                    }
                    if (node == s_position)
                    {
                        componentReads++;
                    }
                    return GlobalValue(node);
                });
            GlobalLocationValue result = await Global(session).ReadGlobalLocationAsync(s_location)
                .ConfigureAwait(false);
            Assert.Multiple(
                () =>
                {
                    Assert.That(aggregateReads, Is.EqualTo(attempts));
                    Assert.That(componentReads, Is.EqualTo(attempts));
                    Assert.That(result.Location.Position.Longitude, Is.EqualTo(8));
                    Assert.That(result.CoordinateReferenceSystem, Is.EqualTo(4326u));
                    Assert.That(result.SourceNodeId, Is.EqualTo(s_zone));
                });
        }

        [Test]
        public async Task PersistentGlobalLocationMismatchStopsAfterExactlyThreeAttemptsBeforeMetadataAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            int aggregateReads = 0;
            int componentReads = 0;
            SetupRead(
                session,
                node =>
                {
                    if (node == s_location)
                    {
                        aggregateReads++;
                        return Structure(new GlobalLocationDataType { Position = Position(7) });
                    }
                    Assert.That(node, Is.EqualTo(s_position));
                    componentReads++;
                    return Structure(Position());
                });
            await AssertStatusAsync(
                async () => await Global(session).ReadGlobalLocationAsync(s_location)
                    .ConfigureAwait(false),
                StatusCodes.BadDataEncodingInvalid).ConfigureAwait(false);
            Assert.That(aggregateReads, Is.EqualTo(3));
            Assert.That(componentReads, Is.EqualTo(3));
        }

        [Test]
        public async Task StrictGlobalLocationRejectsNonfiniteEncodedOrientationAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            SetupRead(
                session,
                node => node == s_location
                    ? Structure(
                        new GlobalLocationDataType
                        {
                            Position = Position(),
                            EncodingMask = (uint)GlobalLocationDataTypeFields.Orientation,
                            Orientation = new ThreeDOrientation { B = double.NaN }
                        })
                    : GlobalValue(node));
            await AssertStatusAsync(
                async () => await Global(session).ReadGlobalLocationAsync(s_location)
                    .ConfigureAwait(false),
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
        }

        [Test]
        public async Task GcpArrayAtExactLimitReusesExistingFitterAndCoordinateConversionsAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            ArrayOf<GroundControlPointDataType> points = ControlPoints();
            SetupRead(session, _ => PointsValue(points));
            GlobalPositioningClient client = Global(
                session,
                new PositioningClientReadOptions
                {
                    MaximumReadArrayItems = 2
                });
            GroundControlPointFitResult actual = await client.ReadZoneTransformAsync(s_zone).ConfigureAwait(false);
            GroundControlPointFitResult expected = new GroundControlPointFitter().Fit(points);
            ThreeDCartesianCoordinates local = await client.GlobalToLocalAsync(
                s_zone,
                points[0].GlobalPosition,
                AngleUnit.Degrees).ConfigureAwait(false);
            S3DGeographicCoordinateDataType global = await client.LocalToGlobalAsync(
                s_zone,
                points[0].LocalPosition,
                AngleUnit.Degrees).ConfigureAwait(false);
            ThreeDCartesianCoordinates expectedLocal = expected.GlobalToLocal(
                points[0].GlobalPosition,
                AngleUnit.Degrees);
            S3DGeographicCoordinateDataType expectedGlobal = expected.LocalToGlobal(
                points[0].LocalPosition,
                AngleUnit.Degrees);
            Assert.Multiple(
                () =>
                {
                    Assert.That(actual.IsInvertible, Is.True);
                    Assert.That(actual.Dimension, Is.EqualTo(2));
                    Assert.That(actual.Determinant, Is.EqualTo(expected.Determinant).Within(1e-12));
                    Assert.That(actual.RootMeanSquareError, Is.EqualTo(expected.RootMeanSquareError).Within(1e-12));
                    Assert.That(local.X, Is.EqualTo(expectedLocal.X).Within(1e-8));
                    Assert.That(local.Y, Is.EqualTo(expectedLocal.Y).Within(1e-8));
                    Assert.That(global.Longitude, Is.EqualTo(expectedGlobal.Longitude).Within(1e-10));
                    Assert.That(global.Latitude, Is.EqualTo(expectedGlobal.Latitude).Within(1e-10));
                });
        }

        [Test]
        public async Task OneOverGcpLimitFailsBeforeTypedDecodingOrInvokingFitterAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            ArrayOf<ExtensionObject> raw =
            [
                new ExtensionObject(Frame()),
                new ExtensionObject(Frame()),
                new ExtensionObject(Frame())
            ];
            SetupRead(session, _ => new DataValue(new Variant(raw)));
            var transformer = new Mock<ICoordinateReferenceSystemTransformer>(MockBehavior.Strict);
            await AssertStatusAsync(
                async () =>
                    await Global(session, new PositioningClientReadOptions { MaximumReadArrayItems = 2 })
                        .ReadZoneTransformAsync(
                            s_zone,
                            new GroundControlPointFitOptions
                            {
                                CoordinateReferenceSystem = transformer.Object
                            }).ConfigureAwait(false),
                StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            transformer.VerifyNoOtherCalls();
        }

        [TestCase("null")]
        [TestCase("empty")]
        [TestCase("singleton")]
        [TestCase("duplicate")]
        [TestCase("nonfinite-local")]
        [TestCase("nonfinite-global")]
        [TestCase("out-of-range")]
        public async Task InvalidGcpConfigurationsFailInsteadOfProducingATransformAsync(string shape)
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            ArrayOf<GroundControlPointDataType> points = ControlPoints();
            points = shape switch
            {
                "null" => default,
                "empty" => [],
                "singleton" => [points[0]],
                "duplicate" => [points[0], points[0]],
                _ => points
            };
            if (shape == "nonfinite-local")
            {
                points[0].LocalPosition.X = double.NaN;
            }
            if (shape == "nonfinite-global")
            {
                points[0].GlobalPosition.Latitude = double.NaN;
            }
            if (shape == "out-of-range")
            {
                points[0].GlobalPosition.Latitude = 91;
            }
            SetupRead(session, _ => PointsValue(points));
            await AssertStatusAsync(
                async () => await Global(session).ReadZoneTransformAsync(s_zone)
                    .ConfigureAwait(false),
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
        }

        [Test]
        public async Task LegacyGlobalReadStillReturnsUncertainGeometryAndMetadataAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            SetupRead(
                session,
                node =>
                {
                    DataValue value = node == s_position ? Structure(Position(double.NaN)) : GlobalValue(node);
                    return value.WithStatus(StatusCodes.Uncertain);
                });
            var client = new GlobalPositioningClient(session.Object, Mock.Of<ITelemetryContext>());
            GlobalPositionValue result = await client.ReadGlobalPositionAsync(s_position).ConfigureAwait(false);
            Assert.Multiple(
                () =>
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
                    Assert.That(result.Position.Longitude, Is.NaN);
                    Assert.That(result.SourceNodeId, Is.EqualTo(s_zone));
                    Assert.That(result.CoordinateReferenceSystem, Is.EqualTo(4326u));
                });
        }

        [TestCase("scalar")]
        [TestCase("wrong-element")]
        [TestCase("null-element")]
        public async Task GcpArrayRejectsWrongWireShapeOrElementTypeWithinBudgetAsync(string kind)
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            Variant value = kind switch
            {
                "scalar" => Variant.From(new ExtensionObject(ControlPoints()[0])),
                "wrong-element" => new Variant(ArrayOf.Create<ExtensionObject>([new ExtensionObject(Frame())])),
                _ => new Variant(ArrayOf.Create<ExtensionObject>([ExtensionObject.Null]))
            };
            SetupRead(session, _ => new DataValue(value));
            await AssertStatusAsync(
                async () => await Global(session).ReadZoneTransformAsync(s_zone)
                    .ConfigureAwait(false),
                StatusCodes.BadTypeMismatch).ConfigureAwait(false);
        }

        [Test]
        public async Task GcpFitRejectsUnknownFitModeBeforeCallingTransformerAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupGlobalMetadata(session);
            SetupRead(session, _ => PointsValue(ControlPoints()));
            var transformer = new Mock<ICoordinateReferenceSystemTransformer>(MockBehavior.Strict);
            await AssertStatusAsync(
                async () => await Global(session).ReadZoneTransformAsync(
                    s_zone,
                    new GroundControlPointFitOptions
                    {
                        Mode = (GroundControlPointFitMode)99,
                        CoordinateReferenceSystem = transformer.Object
                    }).ConfigureAwait(false),
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
            transformer.VerifyNoOtherCalls();
        }

        [Test]
        public async Task CancelledReadResponseIsNotReturnedAsSuccessfulPositionAsync()
        {
            Mock<ISession> session = CreateSession();
            using var cancellation = new System.Threading.CancellationTokenSource();
            SetupRead(
                session,
                node =>
                {
                    cancellation.Cancel();
                    return GlobalValue(node);
                });
            await Assert.ThatAsync(
                async () => await Global(session).ReadGlobalPositionAsync(
                    s_position,
                    cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            session.Verify(
                s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<System.Threading.CancellationToken>()),
                Times.Never);
            VerifyBorrowed(session);
        }

        private static IEnumerable<TestCaseData> GlobalQualityCases()
        {
            foreach (string component in new[]
            {
                "position", "location", "position-component", "source", "crs", "gcp"
            })
            {
                yield return new TestCaseData(component, StatusCodes.BadUserAccessDenied);
                yield return new TestCaseData(component, StatusCodes.Uncertain);
            }
        }

        private static ArrayOf<GroundControlPointDataType> ControlPoints()
        {
            return
            [
                new GroundControlPointDataType
                {
                    LocalPosition = new ThreeDCartesianCoordinates(),
                    GlobalPosition = new S3DGeographicCoordinateDataType { Longitude = 8, Latitude = 47 }
                },
                new GroundControlPointDataType
                {
                    LocalPosition = new ThreeDCartesianCoordinates { X = 100, Y = 30 },
                    GlobalPosition = new S3DGeographicCoordinateDataType { Longitude = 8.001, Latitude = 47.0001 }
                }
            ];
        }

        private static DataValue PointsValue(ArrayOf<GroundControlPointDataType> points)
        {
            ArrayOf<ExtensionObject> extensions = points.IsNull
                ? default
                : points.ConvertAll(point => new ExtensionObject(point));
            return new DataValue(new Variant(extensions));
        }
    }
}
