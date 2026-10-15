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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Positioning.Client;

namespace Opc.Ua.Positioning.Tests
{
    public sealed partial class PositioningStrictReadTests
    {
        [Test]
        public async Task StrictFrameReadsFrameAndBaseTogetherPreservingValueTimestampAndWorldBaseAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupFrame(session);
            RelativeSpatialFrameValue result = await Relative(session).ReadFrameAsync(s_frame).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.NodeId, Is.EqualTo(s_frame));
                Assert.That(result.BaseNodeId.IsNull, Is.True);
                Assert.That(result.Frame.CartesianCoordinates.X, Is.EqualTo(1));
                Assert.That(result.SourceTimestamp, Is.EqualTo(s_timestamp));
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            });
            session.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader?>(), 0, TimestampsToReturn.Both,
                It.Is<ArrayOf<ReadValueId>>(nodes =>
                    nodes.Count == 2 && nodes[0].NodeId == s_frame && nodes[1].NodeId == s_base),
                It.IsAny<CancellationToken>()), Times.Once);
            VerifyBorrowed(session);
        }

        [Test]
        public async Task StrictPositionFrameUsesRslNamespaceAndMandatoryBasePathsAsync()
        {
            Mock<ISession> session = CreateSession();
            var names = new List<QualifiedName>();
            SetupTranslate(session, path =>
            {
                names.Add(path.RelativePath.Elements[0].TargetName);
                return Resolved(path.StartingNode == s_zone ? s_frame : s_base);
            });
            SetupRead(session, node => node == s_frame
                ? Structure(Frame(2)) : new DataValue(new Variant(NodeId.Null)));
            RelativeSpatialFrameValue result = await Relative(session).ReadPositionFrameAsync(s_zone)
                .ConfigureAwait(false);
            Assert.That(result.Frame.CartesianCoordinates.X, Is.EqualTo(2));
            Assert.That(names, Is.EqualTo(new[]
            {
                new QualifiedName("PositionFrame", 1), new QualifiedName("Base", 1)
            }));
        }

        [TestCaseSource(nameof(FrameQualityCases))]
        public async Task FrameAndBaseRequireGoodQualityWithoutReplacingOriginalStatusAsync(
            bool baseValue, StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            SetupFrame(session, baseValue ? StatusCodes.Good : status, baseValue ? status : StatusCodes.Good);
            await AssertStatusAsync(async () => await Relative(session).ReadFrameAsync(s_frame).ConfigureAwait(false),
                status).ConfigureAwait(false);
            await AssertStatusAsync(async () =>
                await Relative(session).ResolveFrameToWorldAsync(s_frame, AngleUnit.Degrees).ConfigureAwait(false),
                status).ConfigureAwait(false);
            VerifyBorrowed(session);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task FrameAndBaseWrongWireTypesAreNotReplacedWithDefaultValuesAsync(bool baseValue)
        {
            Mock<ISession> session = CreateSession();
            SetupFrame(session);
            SetupRead(session, node =>
            {
                if ((node == s_base) == baseValue)
                {
                    return new DataValue(Variant.From("not a frame or NodeId"));
                }
                return node == s_frame ? Structure(Frame()) : new DataValue(new Variant(NodeId.Null));
            });
            await AssertStatusAsync(async () => await Relative(session).ReadFrameAsync(s_frame).ConfigureAwait(false),
                StatusCodes.BadTypeMismatch).ConfigureAwait(false);
        }

        [Test]
        public async Task NonGoodFrameValueWinsOverInvalidPayloadTypeAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupFrame(session);
            SetupRead(session, node => node == s_frame
                ? new DataValue(Variant.From("invalid"), StatusCodes.Uncertain)
                : new DataValue(new Variant(NodeId.Null)));
            await AssertStatusAsync(async () => await Relative(session).ReadFrameAsync(s_frame).ConfigureAwait(false),
                StatusCodes.Uncertain).ConfigureAwait(false);
        }

        [TestCase("header")]
        [TestCase("missing")]
        [TestCase("extra")]
        [TestCase("diagnostics")]
        public async Task FrameBatchRejectsIncompleteOrMalformedReadResponsesAsync(string shape)
        {
            Mock<ISession> session = CreateSession();
            SetupFrame(session);
            session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(), 0, TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = shape == "header" ? StatusCodes.BadDecodingError : StatusCodes.Good
                    },
                    Results = shape switch
                    {
                        "missing" => [Structure(Frame())],
                        "extra" => [Structure(Frame()), new DataValue(new Variant(NodeId.Null)), DataValue.Null],
                        _ => [Structure(Frame()), new DataValue(new Variant(NodeId.Null))]
                    },
                    DiagnosticInfos = shape == "diagnostics" ? [new DiagnosticInfo()] : []
                });
            await AssertStatusAsync(async () => await Relative(session).ReadFrameAsync(s_frame).ConfigureAwait(false),
                StatusCodes.BadDecodingError).ConfigureAwait(false);
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task ReadHeaderMustBeGoodAndItsOriginalFailureIsPreservedAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(), 0, TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse { ResponseHeader = new ResponseHeader { ServiceResult = status } });
            await AssertStatusAsync(async () =>
                await Operations(session).ReadUInt32Async(s_crs, CancellationToken.None)
                .ConfigureAwait(false), status).ConfigureAwait(false);
        }

        [TestCase(AngleUnit.Degrees, 90)]
        [TestCase(AngleUnit.Radians, Math.PI / 2)]
        public async Task FrameChainAtExactDepthComposesExistingTransformsInBaseOrderAsync(
            AngleUnit angleUnit, double yaw)
        {
            Mock<ISession> session = CreateSession();
            NodeId parent = new("parent", 3);
            ThreeDFrame childFrame = Frame(3);
            var parentFrame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { Y = 10 },
                Orientation = new ThreeDOrientation { C = yaw }
            };
            var reads = new List<NodeId>();
            SetupChain(session, new Dictionary<NodeId, (ThreeDFrame, NodeId)>
            {
                [s_frame] = (childFrame, parent),
                [parent] = (parentFrame, NodeId.Null)
            }, reads);

            ResolvedRelativeSpatialFrame result = await Relative(session, new PositioningClientReadOptions
            {
                MaximumFrameChainLength = 2
            }).ResolveFrameToWorldAsync(s_frame, angleUnit).ConfigureAwait(false);

            ThreeDCartesianCoordinates origin = result.TransformToWorld
                .TransformPoint(new ThreeDCartesianCoordinates());
            Assert.Multiple(() =>
            {
                Assert.That(reads, Is.EqualTo(new[] { s_frame, parent }));
                Assert.That(result.FrameChain.ToArray(), Is.EqualTo(new[] { s_frame, parent }));
                Assert.That(origin.X, Is.EqualTo(0).Within(1e-12));
                Assert.That(origin.Y, Is.EqualTo(13).Within(1e-12));
                Assert.That(origin.Z, Is.Zero);
            });
        }

        [Test]
        public async Task FrameChainOneBeyondDepthDoesNotResolveOrReadTheNextFrameAsync()
        {
            Mock<ISession> session = CreateSession();
            NodeId parent = new("parent", 3);
            NodeId beyond = new("beyond", 3);
            var reads = new List<NodeId>();
            SetupChain(session, new Dictionary<NodeId, (ThreeDFrame, NodeId)>
            {
                [s_frame] = (Frame(), parent),
                [parent] = (Frame(), beyond)
            }, reads);
            await AssertStatusAsync(async () =>
                await Relative(session, new PositioningClientReadOptions { MaximumFrameChainLength = 2 })
                    .ResolveFrameToWorldAsync(s_frame, AngleUnit.Degrees).ConfigureAwait(false),
                StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            Assert.That(reads, Is.EqualTo(new[] { s_frame, parent }));
            session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.Is<ArrayOf<BrowsePath>>(paths => paths[0].StartingNode == beyond),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task FrameChainDetectsCyclesWithoutRereadingAFrameAsync(bool selfCycle)
        {
            Mock<ISession> session = CreateSession();
            NodeId parent = new("parent", 3);
            var reads = new List<NodeId>();
            SetupChain(session, new Dictionary<NodeId, (ThreeDFrame, NodeId)>
            {
                [s_frame] = (Frame(), selfCycle ? s_frame : parent),
                [parent] = (Frame(), s_frame)
            }, reads);
            await AssertStatusAsync(async () =>
                await Relative(session).ResolveFrameToWorldAsync(s_frame, AngleUnit.Degrees).ConfigureAwait(false),
                StatusCodes.BadInvalidState).ConfigureAwait(false);
            Assert.That(reads, Is.EqualTo(selfCycle ? new[] { s_frame } : new[] { s_frame, parent }));
        }

        [Test]
        public async Task MissingBaseFrameFailsInsteadOfReturningPartialWorldTransformAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, path => path.StartingNode == s_frame
                ? Resolved(s_base)
                : new BrowsePathResult { StatusCode = StatusCodes.BadNodeIdUnknown });
            SetupRead(session, node => node == s_frame ? Structure(Frame()) : new DataValue(new Variant(s_zone)));
            await AssertStatusAsync(async () =>
                await Relative(session).ResolveFrameToWorldAsync(s_frame, AngleUnit.Degrees).ConfigureAwait(false),
                StatusCodes.BadNodeIdUnknown).ConfigureAwait(false);
        }

        [TestCase(double.NaN, 0)]
        [TestCase(double.PositiveInfinity, 0)]
        [TestCase(1, double.NaN)]
        [TestCase(1, double.NegativeInfinity)]
        public async Task StrictFrameRejectsNonfiniteTranslationOrOrientationAsync(double x, double roll)
        {
            Mock<ISession> session = CreateSession();
            SetupFrame(session);
            SetupRead(session, node => node == s_frame
                ? Structure(Frame(x, roll)) : new DataValue(new Variant(NodeId.Null)));
            await AssertStatusAsync(async () => await Relative(session).ReadFrameAsync(s_frame).ConfigureAwait(false),
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
        }

        [Test]
        public async Task StrictFrameChainRejectsOverflowFromOtherwiseFiniteFramesAsync()
        {
            Mock<ISession> session = CreateSession();
            var reads = new List<NodeId>();
            SetupChain(session, new Dictionary<NodeId, (ThreeDFrame, NodeId)>
            {
                [s_frame] = (Frame(double.MaxValue), s_zone),
                [s_zone] = (Frame(double.MaxValue), NodeId.Null)
            }, reads);
            await AssertStatusAsync(async () =>
                await Relative(session).ResolveFrameToWorldAsync(s_frame, AngleUnit.Degrees).ConfigureAwait(false),
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
            Assert.That(reads, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task StrictFrameChainRejectsUnknownAngleUnitsAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupFrame(session);
            await AssertStatusAsync(async () =>
                await Relative(session).ResolveFrameToWorldAsync(s_frame, (AngleUnit)99).ConfigureAwait(false),
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
        }

        [Test]
        public async Task LegacyFrameRetainsSeparateReadsAndUncertainValueSemanticsAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupFrame(session, StatusCodes.Uncertain, StatusCodes.Uncertain);
            var widths = new List<int>();
            SetupRead(session, node => node == s_frame
                ? Structure(Frame(), StatusCodes.Uncertain)
                : new DataValue(new Variant(NodeId.Null), StatusCodes.Uncertain), reads => widths.Add(reads.Count));
            var client = new RelativeSpatialLocationClient(session.Object, Mock.Of<ITelemetryContext>());
            RelativeSpatialFrameValue frame = await client.ReadFrameAsync(s_frame).ConfigureAwait(false);
            ResolvedRelativeSpatialFrame resolved = await client.ResolveFrameToWorldAsync(s_frame, AngleUnit.Degrees)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(frame.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
                Assert.That(frame.BaseNodeId.IsNull, Is.True);
                Assert.That(widths, Has.Count.EqualTo(4).And.All.EqualTo(1));
                Assert.That(resolved.FrameChain.Count, Is.EqualTo(1));
                Assert.That(resolved.TransformToWorld.Translation.X, Is.EqualTo(1));
            });
        }

        private static IEnumerable<TestCaseData> FrameQualityCases()
        {
            foreach (bool baseValue in new[] { false, true })
            {
                yield return new TestCaseData(baseValue, StatusCodes.BadUserAccessDenied);
                yield return new TestCaseData(baseValue, StatusCodes.Uncertain);
                yield return new TestCaseData(baseValue, StatusCodes.BadNodeIdUnknown);
            }
        }

        private static void SetupChain(
            Mock<ISession> session,
            Dictionary<NodeId, (ThreeDFrame Frame, NodeId Base)> frames,
            List<NodeId> reads)
        {
            var bases = new Dictionary<NodeId, NodeId>();
            var baseValues = new Dictionary<NodeId, NodeId>();
            foreach (KeyValuePair<NodeId, (ThreeDFrame Frame, NodeId Base)> pair in frames)
            {
                var baseNode = new NodeId($"base-{bases.Count}", 3);
                bases.Add(pair.Key, baseNode);
                baseValues.Add(baseNode, pair.Value.Base);
            }
            SetupTranslate(session, path => Resolved(bases[path.StartingNode]));
            SetupRead(session, node =>
            {
                if (frames.TryGetValue(node, out (ThreeDFrame Frame, NodeId Base) frame))
                {
                    reads.Add(node);
                    return Structure(frame.Frame);
                }
                return new DataValue(new Variant(baseValues[node]));
            });
        }
    }
}
