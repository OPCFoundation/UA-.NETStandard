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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gpos;
using UaLens.Plugins.Companions;
using Gpos = Opc.Ua.Gpos;
using ObjectTypeIds = Opc.Ua.ObjectTypeIds;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace UaLens.Tests.Companions
{
    public sealed partial class PositioningCompanionProviderTests
    {
        [Test]
        public async Task TwoDimensionalZoneRejectsNonzeroPreviewZAsync()
        {
            var test = new PositioningContext();
            (CompanionTarget zone, _) = AddZone(test, threeDimensional: false);
            await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                test.Context, zone, "preview", PreviewCoordinates(0, 0, 1), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadOutOfRange))
                .ConfigureAwait(false);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase("cancellation")]
        [TestCase("expiry")]
        [TestCase("session")]
        public async Task PreviewCannotPublishAfterFinalReadInvalidatesItsContextAsync(string invalidation)
        {
            var clock = new PreviewClock();
            var test = new PositioningContext(clock);
            (CompanionTarget zone, NodeId points) = AddZone(test);
            CompanionTaskInput input = await test.Provider.PrepareInputAsync(
                test.Context, zone, "preview", PreviewCoordinates(), CancellationToken.None).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            int reads = 0;
            test.Services.ReadHandler = (read, _) =>
            {
                DataValue value = test.Services.Read(read);
                if (read.NodeId == points && ++reads == 3)
                {
                    switch (invalidation)
                    {
                        case "cancellation":
                            cancellation.Cancel();
                            break;
                        case "expiry":
                            clock.Now += TimeSpan.FromMinutes(5);
                            break;
                        case "session":
                            test.Services.Session.SetupGet(session => session.SessionId).Returns(new NodeId(9876u));
                            break;
                        default:
                            throw new AssertionException("Unexpected invalidation case.");
                    }
                }
                return value;
            };
            if (invalidation == "cancellation")
            {
                await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                    test.Context, zone, "preview", input, null, cancellation.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                    test.Context, zone, "preview", input, null, cancellation.Token).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadInvalidState))
                    .ConfigureAwait(false);
            }
            Assert.That(reads, Is.EqualTo(3));
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreviewPreservesTheCalibratedDimensionAndIsSingleUseAsync(bool threeDimensional)
        {
            var test = new PositioningContext();
            (CompanionTarget zone, _) = AddZone(test, threeDimensional);
            CompanionTaskInput input = await test.Provider.PrepareInputAsync(
                test.Context, zone, "preview", PreviewCoordinates(), CancellationToken.None).ConfigureAwait(false);
            CompanionOperationResult result = await test.Provider.ExecutePreparedAsync(
                test.Context, zone, "preview", input, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(IndustrialCompanionTestSession.Field(result.Values, "Longitude")
                .TryGetValue(out double longitude), Is.True);
            Assert.That(IndustrialCompanionTestSession.Field(result.Values, "Latitude")
                .TryGetValue(out double latitude), Is.True);
            Assert.That(longitude, Is.Zero.Within(1e-7));
            Assert.That(latitude, Is.Zero.Within(1e-7));
            CompanionValue? height = result.Values.ToList()
                .SingleOrDefault(value => value.Name == "Ellipsoidal elevation (metres)");
            if (threeDimensional)
            {
                Assert.That(height, Is.Not.Null);
                Assert.That(height!.Value.TryGetValue(out double elevation), Is.True);
                Assert.That(elevation, Is.Zero.Within(1e-5));
            }
            else
            {
                Assert.That(height, Is.Null);
            }
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, zone, "preview", input, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
            Assert.That(test.Context.Session.Connected, Is.True);
        }

        [TestCase(-10000000.0)]
        [TestCase(0.0)]
        [TestCase(10000000.0)]
        public async Task PreviewAcceptsItsExactCoordinateBoundsAsync(double coordinate)
        {
            var test = new PositioningContext();
            (CompanionTarget zone, _) = AddZone(test);
            CompanionTaskInput input = await test.Provider.PrepareInputAsync(
                test.Context, zone, "preview", PreviewCoordinates(coordinate, coordinate, coordinate),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(input.Review, Does.Contain("No device write"));
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase(-10000000.000001)]
        [TestCase(10000000.000001)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public async Task PreviewRejectsInvalidCoordinatesBeforeReadingAsync(double coordinate)
        {
            var test = new PositioningContext();
            (CompanionTarget zone, _) = AddZone(test);
            test.Services.ReadHandler = (_, _) => throw new AssertionException("An invalid preview must not read.");
            await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                test.Context, zone, "preview", PreviewCoordinates(coordinate), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(128, true)]
        [TestCase(129, false)]
        public async Task PreviewEnforcesBothGroundControlPointBoundsAsync(int count, bool allowed)
        {
            var test = new PositioningContext();
            (CompanionTarget zone, NodeId points) = AddZone(test, threeDimensional: false);
            ArrayOf<Gpos.GroundControlPointDataType> selected =
            [
                .. Enumerable.Range(0, count).Select(index => new Gpos.GroundControlPointDataType
                {
                    LocalPosition = new ThreeDCartesianCoordinates { X = index / 2, Y = index % 2 },
                    GlobalPosition = new Gpos.S3DGeographicCoordinateDataType
                    {
                        Longitude = (index / 2) * 0.000008983152841195214,
                        Latitude = (index % 2) * 0.000009043694770503822
                    }
                })
            ];
            test.Services.SetValue(points, Variant.FromStructure(selected));
            if (allowed)
            {
                CompanionTaskInput input = await test.Provider.PrepareInputAsync(
                    test.Context, zone, "preview", PreviewCoordinates(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(input.Review, Does.Contain("pinned zone calibration"));
            }
            else
            {
                await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                    test.Context, zone, "preview", PreviewCoordinates(), CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            }
        }

        [TestCase(299, true)]
        [TestCase(300, false)]
        [TestCase(301, false)]
        public async Task PreviewPreparationHasAFixedExpiryAsync(int elapsedSeconds, bool allowed)
        {
            var clock = new PreviewClock();
            var test = new PositioningContext(clock);
            (CompanionTarget zone, _) = AddZone(test);
            CompanionTaskInput input = await test.Provider.PrepareInputAsync(
                test.Context, zone, "preview", PreviewCoordinates(), CancellationToken.None).ConfigureAwait(false);
            clock.Now += TimeSpan.FromSeconds(elapsedSeconds);
            if (allowed)
            {
                CompanionOperationResult result = await test.Provider.ExecutePreparedAsync(
                    test.Context, zone, "preview", input, null, CancellationToken.None).ConfigureAwait(false);
                Assert.That(result.Values, Has.Count.EqualTo(3));
            }
            else
            {
                await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                    test.Context, zone, "preview", input, null, CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadInvalidState))
                    .ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ChangedCalibrationCannotPublishOrReplayAPreviewAsync(bool duringCalculation)
        {
            var test = new PositioningContext();
            (CompanionTarget zone, NodeId points) = AddZone(test);
            CompanionTaskInput input = await test.Provider.PrepareInputAsync(
                test.Context, zone, "preview", PreviewCoordinates(), CancellationToken.None).ConfigureAwait(false);
            ArrayOf<Gpos.GroundControlPointDataType> changed = ControlPoints(threeDimensional: true);
            changed[0].GlobalPosition.Longitude = 0.001;
            int reads = 0;
            if (duringCalculation)
            {
                test.Services.ReadHandler = (read, _) =>
                {
                    DataValue value = test.Services.Read(read);
                    if (read.NodeId == points && ++reads == 2)
                    {
                        test.Services.SetValue(points, Variant.FromStructure(changed));
                    }
                    return value;
                };
            }
            else
            {
                test.Services.SetValue(points, Variant.FromStructure(changed));
            }
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, zone, "preview", input, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, zone, "preview", input, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            if (duringCalculation)
            {
                Assert.That(reads, Is.EqualTo(3));
            }
            Assert.That(test.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task ZoneInspectionReportsFitDimensionRankAndResidualsAsync()
        {
            var test = new PositioningContext();
            (CompanionTarget zone, _) = AddZone(test);
            ArrayOf<CompanionTarget> targets = await test.Provider.DiscoverAsync(
                test.Context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(targets.ToList().Single(target => target.TypeName == "Global zone").NodeId,
                Is.EqualTo(zone.NodeId));
            CompanionInspection snapshot = await test.Provider.InspectAsync(
                test.Context, zone, CancellationToken.None).ConfigureAwait(false);
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "Fit mode"),
                Is.EqualTo(Variant.From("Rigid")));
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "Dimension"),
                Is.EqualTo(Variant.From(3)));
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "Rank"), Is.EqualTo(Variant.From(3)));
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "Invertible"),
                Is.EqualTo(Variant.From(true)));
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "RMS residual (metres)")
                .TryGetValue(out double rms), Is.True);
            Assert.That(rms, Is.InRange(0, 1e-5));
            Assert.That(IndustrialCompanionTestSession.Field(snapshot.Values, "Determinant")
                .TryGetValue(out double determinant), Is.True);
            Assert.That(determinant, Is.EqualTo(1).Within(1e-8));
            Assert.That(snapshot.Operations.ToList().Single(operation => operation.Id == "preview").Safety,
                Is.EqualTo(CompanionOperationSafety.ReadOnly));
        }

        private static ArrayOf<CompanionValue> PreviewCoordinates(double x = 0, double y = 0, double z = 0) =>
            [new("x", Variant.From(x)), new("y", Variant.From(y)), new("z", Variant.From(z))];

        private static (CompanionTarget Zone, NodeId Points) AddZone(
            PositioningContext test, bool threeDimensional = true)
        {
            test.Services.NamespaceUris.GetIndexOrAppend(Gpos.Namespaces.GPOS);
            test.Services.MessageContext.Factory.Builder.AddOpcUaGpos().Commit();
            NodeId root = test.Services.Resolve(Gpos.ObjectIds.GlobalLocations);
            test.Services.AddObject(root, ObjectTypeIds.FolderType, "GlobalLocations");
            NodeId zone = test.Services.Id("zone");
            test.Services.AddObject(zone, Gpos.ObjectTypeIds.ZoneType, "Surveyed zone");
            test.Services.SetReferences(root, BrowseDirection.Forward, ReferenceTypeIds.Organizes,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(zone), NodeClass = NodeClass.Object,
                    TypeDefinition = Gpos.ObjectTypeIds.ZoneType, BrowseName = new QualifiedName("Zone")
                }
            ]);
            NodeId points = test.Services.AddProperty(zone, Gpos.Namespaces.GPOS, "GroundControlPoints",
                Variant.FromStructure(ControlPoints(threeDimensional)));
            return (new CompanionTarget("positioning", zone, "Surveyed zone", "Global zone"), points);
        }

        private static ArrayOf<Gpos.GroundControlPointDataType> ControlPoints(bool threeDimensional)
        {
            uint mask = threeDimensional ? (uint)Gpos.S3DGeographicCoordinateDataTypeFields.Elevation : 0;
            var points = new List<Gpos.GroundControlPointDataType>
            {
                new()
                {
                    LocalPosition = new ThreeDCartesianCoordinates(),
                    GlobalPosition = new Gpos.S3DGeographicCoordinateDataType { EncodingMask = mask }
                },
                new()
                {
                    LocalPosition = new ThreeDCartesianCoordinates { X = 1 },
                    GlobalPosition = new Gpos.S3DGeographicCoordinateDataType
                    {
                        EncodingMask = mask, Longitude = 0.000008983152841195214
                    }
                },
                new()
                {
                    LocalPosition = new ThreeDCartesianCoordinates { Y = 1 },
                    GlobalPosition = new Gpos.S3DGeographicCoordinateDataType
                    {
                        EncodingMask = mask, Latitude = 0.000009043694770503822
                    }
                }
            };
            if (threeDimensional)
            {
                points.Add(new Gpos.GroundControlPointDataType
                {
                    LocalPosition = new ThreeDCartesianCoordinates { Z = 1 },
                    GlobalPosition = new Gpos.S3DGeographicCoordinateDataType { EncodingMask = mask, Elevation = 1 }
                });
            }
            return [.. points];
        }

        private sealed class PreviewClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
