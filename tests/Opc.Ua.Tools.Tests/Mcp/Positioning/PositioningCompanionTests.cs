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

#if NET10_0
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Gpos;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Positioning;
using Opc.Ua.Positioning.Client;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Focused checks of positioning catalog contracts, projections and transform options.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PositioningCompanionTests
    {
        /// <summary>
        /// Every public constructor rejects its required missing dependency.
        /// </summary>
        [Test]
        public void ConstructorsRejectNullDependencies()
        {
            Assert.That(() => new PositioningClientAccessor(null!), Throws.ArgumentNullException);
            Assert.That(() => new RelativePositioningTools(null!), Throws.ArgumentNullException);
            Assert.That(() => new GlobalPositioningTools(null!), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Both supported profile forms register the real, finite read-only catalog.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ProfileCatalogContainsSevenReadOnlyTools(bool composed)
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();
            if (composed)
            {
                builder.WithOpcUaPositioningTools(new McpToolProfileSet(McpToolProfile.Positioning));
            }
            else
            {
                builder.WithOpcUaPositioningTools(McpToolProfile.Positioning);
            }

            await using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool[] allTools = provider.GetServices<McpServerTool>().ToArray();
            McpServerTool[] tools = allTools
                .Where(tool => tool.ProtocolTool.Name.StartsWith("positioning_", StringComparison.Ordinal))
                .ToArray();
            Assert.That(tools.Select(tool => tool.ProtocolTool.Name), Is.EquivalentTo(s_toolNames));
            Assert.That(allTools.Count(tool => tool.ProtocolTool.Name == "Connect"), Is.EqualTo(1));
            foreach (McpServerTool tool in tools)
            {
                Assert.That(tool.ProtocolTool.Annotations?.ReadOnlyHint, Is.True, tool.ProtocolTool.Name);
                Assert.That(tool.ProtocolTool.Annotations?.DestructiveHint, Is.False, tool.ProtocolTool.Name);
                Assert.That(tool.ProtocolTool.InputSchema.GetProperty("properties")
                    .TryGetProperty("sessionName", out _), Is.True, tool.ProtocolTool.Name);
            }
        }

        /// <summary>
        /// Geographic input uses primitives and finite string enums rather than reflected UA structures.
        /// </summary>
        [Test]
        public async Task TransformSchemaExposesNumericCoordinatesAndExplicitOptions()
        {
            await using ServiceProvider provider = CreateProvider();
            JsonElement schema = provider.GetServices<McpServerTool>()
                .Single(tool => tool.ProtocolTool.Name == "positioning_gpos_transform")
                .ProtocolTool.InputSchema;
            JsonElement properties = schema.GetProperty("properties");
            Assert.That(properties.GetProperty("x").GetProperty("type").GetString(), Is.EqualTo("number"));
            Assert.That(properties.GetProperty("y").GetProperty("type").GetString(), Is.EqualTo("number"));
            Assert.That(properties.GetProperty("direction").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()),
                Is.EquivalentTo(s_directions));
            Assert.That(properties.GetProperty("angleUnit").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()),
                Is.EquivalentTo(s_angleUnits));
            Assert.That(schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()),
                Is.SupersetOf(s_transformFields));
            string text = schema.GetRawText();
            Assert.That(text, Does.Contain("controlPointAngleUnit"));
            Assert.That(text, Does.Contain("coordinateReferenceSystem"));
            Assert.That(text, Does.Contain("allowReflection"));
            Assert.That(text, Does.Not.Contain("S3DGeographicCoordinateDataType"));
        }

        /// <summary>
        /// A missing orientation unit is a tool error before any session is requested.
        /// </summary>
        [Test]
        public async Task WorldFrameRequiresActualAngleUnits()
        {
            await using ServiceProvider provider = CreateProvider();
            var tools = new RelativePositioningTools(provider.GetRequiredService<PositioningClientAccessor>());
            CallToolResult result = await tools.ReadAsync("ns=2;s=frame", RelativePositioningRead.WorldFrame)
                .ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content.OfType<TextContentBlock>().Single().Text, Does.Contain("angleUnit"));
        }

        /// <summary>
        /// The tools reject numeric enum values outside the documented operation set.
        /// </summary>
        [Test]
        public void UndefinedDiscriminatorsAreRejected()
        {
            Assert.That(() => PositioningJson.Validate((RelativePositioningDiscovery)999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => PositioningJson.Validate((RelativePositioningRead)999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => PositioningJson.Validate((RelativePositioningObservation)999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => PositioningJson.Validate((GlobalPositioningRead)999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => PositioningJson.Validate((GlobalPositioningObservation)999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => PositioningJson.Validate((PositioningTransformDirection)999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => PositioningFitOptions.ToAngleUnit((PositioningAngleUnit)999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PositioningFitOptions { Mode = (PositioningFitMode)999 }
                .ToOptions(Wgs84CoordinateReferenceSystemTransformer.Instance), Throws.ArgumentException);
        }

        /// <summary>
        /// Unknown targets cannot silently select the OPC UA null NodeId.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("i=0")]
        public void MissingTargetNodeIdsAreRejected(string? nodeId)
        {
            Assert.That(() => PositioningJson.ParseNodeId(nodeId), Throws.InstanceOf<ArgumentException>());
        }

        /// <summary>
        /// Non-finite input is rejected on each numeric axis, including optional elevation.
        /// </summary>
        [TestCase(double.NaN, 1.0, 2.0)]
        [TestCase(1.0, double.PositiveInfinity, 2.0)]
        [TestCase(1.0, 2.0, double.NegativeInfinity)]
        public void NonFiniteCoordinatesAreRejected(double x, double y, double z)
        {
            Assert.That(() => GlobalPositioningTools.ValidateCoordinates(x, y, z), Throws.ArgumentException);
        }

        /// <summary>
        /// Missing elevation is accepted without requiring a fabricated zero.
        /// </summary>
        [Test]
        public void GeographicElevationCanRemainAbsent()
        {
            Assert.That(() => GlobalPositioningTools.ValidateCoordinates(8.0, 47.0, null), Throws.Nothing);
        }

        /// <summary>
        /// CRS mismatch fails rather than silently applying the default WGS84 transform.
        /// </summary>
        [Test]
        public void FitOptionsRejectMismatchedCrs()
        {
            var options = new PositioningFitOptions { CoordinateReferenceSystem = "EPSG:3857" };
            Assert.That(() => options.ToOptions(Wgs84CoordinateReferenceSystemTransformer.Instance),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("EPSG:3857"));
        }

        /// <summary>
        /// All options reach the fitter without conflating control-point and query units.
        /// </summary>
        [Test]
        public void FitOptionsPreserveModeUnitsReflectionAndTransformer()
        {
            Wgs84CoordinateReferenceSystemTransformer transformer = Wgs84CoordinateReferenceSystemTransformer.Instance;
            var request = new PositioningFitOptions
            {
                Mode = PositioningFitMode.Affine,
                ControlPointAngleUnit = PositioningAngleUnit.Radians,
                AllowReflection = true,
                CoordinateReferenceSystem = "epsg:4326"
            };
            GroundControlPointFitOptions options = request.ToOptions(transformer);
            Assert.That(options.Mode, Is.EqualTo(GroundControlPointFitMode.Affine));
            Assert.That(options.AngleUnit, Is.EqualTo(AngleUnit.Radians));
            Assert.That(options.AllowReflection, Is.True);
            Assert.That(options.CoordinateReferenceSystem, Is.SameAs(transformer));
        }

        /// <summary>
        /// RSL projection preserves a nontrivial rotation, units, translation and chain order.
        /// </summary>
        [TestCase(PositioningAngleUnit.Degrees, 90.0)]
        [TestCase(PositioningAngleUnit.Radians, Math.PI / 2.0)]
        public async Task ResolvedFramePreservesNumericTransformAndProvenance(
            PositioningAngleUnit angleUnit,
            double yaw)
        {
            await using ServiceProvider provider = CreateProvider();
            IServiceMessageContext context = ServiceMessageContext.Create(
                provider.GetRequiredService<OpcUaSessionManager>().Telemetry);
            var translation = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 };
            RslFrameTransform transform = RslFrameTransform.FromComponents(
                new ThreeDOrientation { A = 0.0, B = 0.0, C = 90.0 }, translation, AngleUnit.Degrees);
            NodeId node = NodeId.Parse("ns=2;s=child");
            NodeId parent = NodeId.Parse("ns=2;s=parent");
            var value = new ResolvedRelativeSpatialFrame(node, transform, ArrayOf.Create<NodeId>([node, parent]));
            JsonObject json = PositioningJson.Resolved(value, angleUnit, context);
            double[] rotation = json["rotationMatrixRowMajor"]!.AsArray()
                .Select(item => item!.GetValue<double>()).ToArray();
            Assert.That(rotation, Is.EqualTo(new[] { 0.0, -1.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0 })
                .Within(1e-12));
            Assert.That(json["frameChain"]!.AsArray().Select(item => item!.GetValue<string>()),
                Is.EqualTo(new[] { node.ToString(), parent.ToString() }));
            Assert.That(json["angleUnit"]!.GetValue<string>(), Is.EqualTo(angleUnit.ToString()));
            var expected = new ThreeDFrame
            {
                CartesianCoordinates = translation,
                Orientation = new ThreeDOrientation { A = 0.0, B = 0.0, C = yaw }
            };
            Assert.That(JsonNode.DeepEquals(json["frameToWorld"], McpCompanionJson.Encode(expected, context)),
                Is.True);
        }

        /// <summary>
        /// Frame metadata retains a non-good status, source timestamp and base node.
        /// </summary>
        [Test]
        public async Task FrameProjectionPreservesBadStatusTimestampAndBaseNode()
        {
            await using ServiceProvider provider = CreateProvider();
            IServiceMessageContext context = ServiceMessageContext.Create(
                provider.GetRequiredService<OpcUaSessionManager>().Telemetry);
            NodeId node = NodeId.Parse("ns=2;s=frame");
            NodeId parent = NodeId.Parse("ns=2;s=base");
            var value = new RelativeSpatialFrameValue(node, parent,
                RslFrameTransform.Identity.ToFrame(AngleUnit.Radians),
                StatusCodes.BadOutOfService, new DateTimeUtc(2026, 1, 2, 3, 4, 5));
            JsonObject json = PositioningJson.Frame(value, context);
            Assert.That(json["statusCode"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadOutOfService));
            Assert.That(json["nodeId"]!.GetValue<string>(), Is.EqualTo(node.ToString()));
            Assert.That(json["baseNodeId"]!.GetValue<string>(), Is.EqualTo(parent.ToString()));
            Assert.That(json["metadata"]!["sourceTimestamp"]!.GetValue<string>(),
                Does.StartWith("2026-01-02T03:04:05"));
            Assert.That(json["valueType"]!.GetValue<string>(), Is.EqualTo("ThreeDFrame"));
        }

        /// <summary>
        /// Fitting options drive the existing numeric implementation and preserve diagnostics.
        /// </summary>
        [TestCase(PositioningFitMode.Rigid)]
        [TestCase(PositioningFitMode.Similarity)]
        [TestCase(PositioningFitMode.Affine)]
        public void ZoneFitOptionsPreserveNumericRoundTripAndDiagnostics(PositioningFitMode mode)
        {
            var origin = new S3DGeographicCoordinateDataType
            {
                EncodingMask = (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                Longitude = 8.0,
                Latitude = 47.0,
                Elevation = 400.0
            };
            var plane = new LocalTangentPlane(origin, AngleUnit.Degrees);
            GroundControlPointFitOptions options = new PositioningFitOptions { Mode = mode }
                .ToOptions(Wgs84CoordinateReferenceSystemTransformer.Instance);
            GroundControlPointFitResult fit = new GroundControlPointFitter(options.CoordinateReferenceSystem)
                .Fit(ArrayOf.Create<GroundControlPointDataType>(
                [
                    ControlPoint(plane, 0.0, 0.0, 0.0),
                    ControlPoint(plane, 10.0, 0.0, 0.0),
                    ControlPoint(plane, 0.0, 10.0, 0.0),
                    ControlPoint(plane, 0.0, 0.0, 10.0)
                ]), options);
            var input = new ThreeDCartesianCoordinates { X = 3.0, Y = 4.0, Z = 2.0 };
            S3DGeographicCoordinateDataType global = fit.LocalToGlobal(input, AngleUnit.Radians);
            S3DGeographicCoordinateDataType expected = plane.EnuToGeographic(input, AngleUnit.Degrees, true);
            Assert.That(global.Longitude * 180.0 / Math.PI, Is.EqualTo(expected.Longitude).Within(1e-8));
            Assert.That(global.Latitude * 180.0 / Math.PI, Is.EqualTo(expected.Latitude).Within(1e-8));
            Assert.That(global.Elevation, Is.EqualTo(expected.Elevation).Within(1e-5));
            ThreeDCartesianCoordinates local = fit.GlobalToLocal(global, AngleUnit.Radians);
            Assert.That(local.X, Is.EqualTo(input.X).Within(1e-5));
            Assert.That(local.Y, Is.EqualTo(input.Y).Within(1e-5));
            Assert.That(local.Z, Is.EqualTo(input.Z).Within(1e-5));
            JsonObject json = PositioningJson.Fit(NodeId.Parse("ns=2;s=zone"), fit, options);
            Assert.That(json["mode"]!.GetValue<string>(), Is.EqualTo(mode.ToString()));
            Assert.That(json["dimension"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(json["rank"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(json["isInvertible"]!.GetValue<bool>(), Is.True);
            Assert.That(json["rootMeanSquareError"]!.GetValue<double>(), Is.LessThan(1e-5));
            Assert.That(json["transformerCoordinateReferenceSystem"]!.GetValue<string>(),
                Is.EqualTo("EPSG:4326"));
        }

        /// <summary>
        /// Constructs a control point using the production tangent-plane implementation.
        /// </summary>
        private static GroundControlPointDataType ControlPoint(LocalTangentPlane plane, double x, double y, double z)
        {
            var local = new ThreeDCartesianCoordinates { X = x, Y = y, Z = z };
            return new GroundControlPointDataType
            {
                LocalPosition = local,
                GlobalPosition = plane.EnuToGeographic(local, AngleUnit.Degrees, true)
            };
        }

        /// <summary>
        /// Creates an unconnected host with the actual positioning registrations.
        /// </summary>
        private static ServiceProvider CreateProvider()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpPositioning();
            services.AddMcpServer().WithOpcUaPositioningTools(McpToolProfile.Positioning);
            return services.BuildServiceProvider();
        }

        private static readonly string[] s_toolNames =
        [
            "positioning_rsl_list", "positioning_rsl_read", "positioning_rsl_observe",
            "positioning_gpos_list", "positioning_gpos_read", "positioning_gpos_transform",
            "positioning_gpos_observe"
        ];
        private static readonly string[] s_directions = ["GlobalToLocal", "LocalToGlobal"];
        private static readonly string[] s_angleUnits = ["Radians", "Degrees"];
        private static readonly string[] s_transformFields = ["zoneNodeId", "direction", "x", "y", "angleUnit"];
    }
}
#endif
