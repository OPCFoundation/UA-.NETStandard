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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    [TestFixture]
    public sealed class VisionReadProtocolTests
    {
        [Test]
        [Combinatorial]
        public async Task PresentNonGoodOrWrongTypedMembersCannotBecomeSuccessfulDefaults(
            [Values("identity", "reality", "image", "depth", "optics", "illumination", "intrinsic", "extrinsic",
                "pipeline", "state", "detection", "inspection", "segmentation")] string method,
            [Values("bad", "uncertain", "wrong-type", "status-code")] string fault)
        {
            var harness = new VisionSessionHarness();
            (NodeId node, Variant value, Func<Task> read) = Configure(harness, method);
            StatusCode status = fault switch
            {
                "bad" => StatusCodes.BadUserAccessDenied,
                "uncertain" => StatusCodes.Uncertain,
                _ => StatusCodes.Good
            };
            DataValue data = new(fault switch
            {
                "wrong-type" => Variant.From(ByteString.From(1, 2)),
                "status-code" => Variant.From(new StatusCode(1u)),
                _ => value
            });
            data = data.WithStatus(status);
            SetRead(harness, node, new ReadResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [data]
            });
            await Assert.ThatAsync(read, Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(fault is "wrong-type" or "status-code" ? StatusCodes.BadTypeMismatch : status))
                .ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PipelineEnumerationPreservesSupportedNumericRepresentations(bool unsigned)
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.State, new NodeId(5200u, 3),
                unsigned ? Variant.From((uint)VisionEndpointStateEnum.Ready) :
                    Variant.From((int)VisionEndpointStateEnum.Ready));

            VisionEndpointStateEnum state = await harness.Client.Pipeline(harness.PipelineNodeId)
                .ReadStateAsync().ConfigureAwait(false);

            Assert.That(state, Is.EqualTo(VisionEndpointStateEnum.Ready));
        }

        [TestCase("bad-service")]
        [TestCase("uncertain-service")]
        [TestCase("short")]
        [TestCase("long")]
        public async Task ReadResponseProtocolFailuresAreNotSuccessfulSnapshots(string fault)
        {
            var harness = new VisionSessionHarness();
            (NodeId node, Variant value, Func<Task> read) = Configure(harness, "identity");
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = fault == "bad-service" ? StatusCodes.BadUserAccessDenied :
                        fault == "uncertain-service" ? StatusCodes.Uncertain : StatusCodes.Good
                },
                Results = fault == "short" ? [] :
                    fault == "long" ? [new DataValue(value), new DataValue(value)] : [new DataValue(value)]
            };
            SetRead(harness, node, response);

            await Assert.ThatAsync(read, Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitEmptyResultsAndAbsentOptionalFramesRemainDistinctFromMissingArrays(bool inspection)
        {
            var harness = new VisionSessionHarness();
            string member = inspection ? BrowseNames.Characteristics : BrowseNames.Detections;
            Variant value = inspection
                ? Variant.FromStructure(ArrayOf<VisionCharacteristicDataType>.Empty)
                : Variant.FromStructure(ArrayOf<VisionDetectionDataType>.Empty);
            harness.AddValueChild(harness.ResultNodeId, member, new NodeId(5200u, 3), value);
            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);
            if (inspection)
            {
                VisionInspectionResultSnapshot snapshot = await reader.ReadInspectionAsync().ConfigureAwait(false);
                Assert.That(snapshot.Characteristics.IsNull, Is.False);
                Assert.That(snapshot.Characteristics.IsEmpty, Is.True);
                Assert.That(snapshot.Frame, Is.Null);
            }
            else
            {
                VisionDetectionResultSnapshot snapshot = await reader.ReadDetectionAsync().ConfigureAwait(false);
                Assert.That(snapshot.Detections.IsNull, Is.False);
                Assert.That(snapshot.Detections.IsEmpty, Is.True);
                Assert.That(snapshot.Frame, Is.Null);
            }
            var absent = new VisionSessionHarness();
            Func<Task> read = inspection
                ? () => absent.Client.Result(absent.ResultNodeId).ReadInspectionAsync()
                : () => absent.Client.Result(absent.ResultNodeId).ReadDetectionAsync();
            await Assert.ThatAsync(read, Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                .EqualTo(StatusCodes.BadNotFound)).ConfigureAwait(false);
        }

        [Test]
        public async Task ExplicitNullNodeIdsRemainTypedAbsenceInsteadOfMalformedValues()
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.Sensor, new NodeId(5101u, 3),
                Variant.From(NodeId.Null));
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.Deployment, new NodeId(5102u, 3),
                Variant.From(NodeId.Null));
            harness.AddValueChild(harness.PipelineNodeId, BrowseNames.LearningJob, new NodeId(5103u, 3),
                Variant.From(NodeId.Null));
            VisionPipelineSnapshot pipeline = await harness.Client.Pipeline(harness.PipelineNodeId).ReadAsync()
                .ConfigureAwait(false);
            Assert.That(pipeline.SensorId.IsNull, Is.True);
            Assert.That(pipeline.DeploymentId.IsNull, Is.True);
            Assert.That(pipeline.LearningJobId.IsNull, Is.True);
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.SourceFrame, new NodeId(5104u, 3),
                Variant.From(NodeId.Null));
            VisionExtrinsicCalibrationSnapshot calibration = await harness.Client.Sensor(harness.SensorNodeId)
                .ReadExtrinsicCalibrationAsync(harness.ExtrinsicCalibrationNodeId).ConfigureAwait(false);
            Assert.That(calibration.SourceFrameId.IsNull, Is.True);
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.Sensor, new NodeId(5105u, 3),
                Variant.From(NodeId.Null));
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.Detections, new NodeId(5106u, 3),
                Variant.FromStructure(ArrayOf<VisionDetectionDataType>.Empty));
            VisionDetectionResultSnapshot result = await harness.Client.Result(harness.ResultNodeId)
                .ReadDetectionAsync()
                .ConfigureAwait(false);
            Assert.That(result.SensorId.IsNull, Is.True);
            Assert.That(result.Detections.IsNull, Is.False);
            Assert.That(result.Detections.IsEmpty, Is.True);
        }

        private static (NodeId Node, Variant Value, Func<Task> Read) Configure(
            VisionSessionHarness harness, string method)
        {
            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionPipelineClient pipeline = harness.Client.Pipeline(harness.PipelineNodeId);
            VisionResultReader result = harness.Client.Result(harness.ResultNodeId);
            harness.AddChild(harness.SensorNodeId, BrowseNames.Optics, harness.OpticsNodeId);
            harness.AddChild(harness.SensorNodeId, BrowseNames.Illumination, harness.IlluminationNodeId);
            (NodeId Owner, string Name, Variant Value, Func<Task> Read) selection = method switch
            {
                "identity" => (harness.SensorNodeId, BrowseNames.SensorId, Variant.From("sensor"),
                    () => sensor.ReadIdentityAsync()),
                "reality" => (harness.SensorNodeId, BrowseNames.RealityKind,
                    Variant.From((int)VisionRealityKindEnum.Simulated), () => sensor.ReadIdentityAsync()),
                "image" => (harness.SensorNodeId, BrowseNames.Width, Variant.From(640u),
                    () => sensor.ReadImageMembersAsync()),
                "depth" => (harness.SensorNodeId, BrowseNames.MinDepth, Variant.From(0.1),
                    () => sensor.ReadDepthMembersAsync()),
                "optics" => (harness.OpticsNodeId, BrowseNames.FocalLength, Variant.From(0.03),
                    () => sensor.ReadOpticsAsync()),
                "illumination" => (harness.IlluminationNodeId, BrowseNames.Wavelength, Variant.From(500d),
                    () => sensor.ReadIlluminationAsync()),
                "intrinsic" => (harness.IntrinsicCalibrationNodeId, BrowseNames.Valid, Variant.From(false),
                    () => sensor.ReadIntrinsicCalibrationAsync(harness.IntrinsicCalibrationNodeId)),
                "extrinsic" => (harness.ExtrinsicCalibrationNodeId, BrowseNames.Valid, Variant.From(false),
                    () => sensor.ReadExtrinsicCalibrationAsync(harness.ExtrinsicCalibrationNodeId)),
                "pipeline" => (harness.PipelineNodeId, BrowseNames.Continuous, Variant.From(false),
                    () => pipeline.ReadAsync()),
                "state" => (harness.PipelineNodeId, BrowseNames.State, Variant.From((int)VisionEndpointStateEnum.Ready),
                    () => pipeline.ReadStateAsync()),
                "detection" => (harness.ResultNodeId, BrowseNames.Detections,
                    Variant.FromStructure(ArrayOf<VisionDetectionDataType>.Empty), () => result.ReadDetectionAsync()),
                "inspection" => (harness.ResultNodeId, BrowseNames.Characteristics,
                    Variant.FromStructure(ArrayOf<VisionCharacteristicDataType>.Empty),
                    () => result.ReadInspectionAsync()),
                "segmentation" => (harness.ResultNodeId, BrowseNames.LabelClasses,
                    Variant.From(["background"]), () => result.ReadSegmentationAsync()),
                _ => throw new ArgumentOutOfRangeException(nameof(method))
            };
            var node = new NodeId(5200u, 3);
            harness.AddValueChild(selection.Owner, selection.Name, node, selection.Value);
            return (node, selection.Value, selection.Read);
        }

        private static void SetRead(VisionSessionHarness harness, NodeId node, ReadResponse response)
        {
            harness.Session.Setup(session => session.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> reads,
                    CancellationToken _) =>
                {
                    Assert.That(reads.Count, Is.EqualTo(1));
                    Assert.That(reads[0].NodeId, Is.EqualTo(node));
                    return new ValueTask<ReadResponse>(response);
                });
        }
    }
}
