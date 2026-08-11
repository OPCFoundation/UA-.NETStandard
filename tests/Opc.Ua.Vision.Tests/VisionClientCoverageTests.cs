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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Fills the gaps flagged by the vision-coverage baseline: exercises
    /// <see cref="VisionSensorClient.ReadExtrinsicCalibrationAsync"/> along the
    /// happy path (48 lines that no other test entered), the
    /// <see cref="VisionExtrinsicCalibrationSnapshot"/> record surface, and the
    /// three <see cref="VisionResultReader"/> Observe entry points that were only
    /// covered on their argument-guard path.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionClientCoverageTests
    {
        [Test]
        public async Task ReadExtrinsicCalibrationReturnsSnapshotWithMountFramesAndTransform()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            var pose = new VisionPose3DDataType
            {
                FrameId = "flange",
                Position = new double[] { 0.10, 0.20, 0.30 },
                Orientation = new double[] { 0.0, 0.0, 0.0, 1.0 }
            };
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.CalibrationId,
                new(2700u, 3), "ex-1");
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.PerformedAt,
                new(2701u, 3), new DateTimeUtc(new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc)));
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.Valid,
                new(2702u, 3), true);
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.ResidualError,
                new(2703u, 3), 0.08);
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.Method,
                new(2704u, 3), "HandEye-Tsai");
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.Mount,
                new(2705u, 3), (int)VisionCalibrationMountEnum.EyeInHand);
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.SourceFrame,
                new(2706u, 3), harness.SensorNodeId);
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.TargetFrame,
                new(2707u, 3), harness.FrameNodeId);
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.Transform,
                new(2708u, 3), Variant.FromStructure(pose));

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionExtrinsicCalibrationSnapshot snapshot = await sensor
                .ReadExtrinsicCalibrationAsync(harness.ExtrinsicCalibrationNodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.NodeId, Is.EqualTo(harness.ExtrinsicCalibrationNodeId));
                Assert.That(snapshot.CalibrationId, Is.EqualTo("ex-1"));
                Assert.That(snapshot.Valid, Is.True);
                Assert.That(snapshot.ResidualError, Is.EqualTo(0.08));
                Assert.That(snapshot.Method, Is.EqualTo("HandEye-Tsai"));
                Assert.That(snapshot.Mount, Is.EqualTo(VisionCalibrationMountEnum.EyeInHand));
                Assert.That(snapshot.SourceFrameId, Is.EqualTo(harness.SensorNodeId));
                Assert.That(snapshot.TargetFrameId, Is.EqualTo(harness.FrameNodeId));
                Assert.That(snapshot.Transform, Is.Not.Null);
                Assert.That(snapshot.Transform!.FrameId, Is.EqualTo("flange"));
            });
        }

        [Test]
        public async Task ReadExtrinsicCalibrationLeavesOptionalMembersDefaultWhenTheyResolveToNull()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            harness.AddValueChild(harness.ExtrinsicCalibrationNodeId, BrowseNames.CalibrationId,
                new(2710u, 3), "ex-min");
            // No PerformedAt/Valid/ResidualError/Method/Mount/Frames/Transform bindings.
            // BrowsePathResults for those names must come back BadNoMatch, so the ArrayOf<NodeId>
            // slots are Null and each TakeXxx helper skips the value read entirely.

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionExtrinsicCalibrationSnapshot snapshot = await sensor
                .ReadExtrinsicCalibrationAsync(harness.ExtrinsicCalibrationNodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.CalibrationId, Is.EqualTo("ex-min"),
                    "the one bound member must survive the missing-optional path");
                Assert.That(snapshot.Valid, Is.False,
                    "TakeBool must return the struct default when the member is absent, not throw");
                Assert.That(snapshot.ResidualError, Is.EqualTo(0.0));
                Assert.That(snapshot.Method, Is.Null);
                Assert.That(snapshot.Mount, Is.EqualTo(default(VisionCalibrationMountEnum)));
                Assert.That(snapshot.Transform, Is.Null);
                // SourceFrameId / TargetFrameId are INullable structs — assert .IsNull rather than
                // Is.Null (see the coverage instructions).
                Assert.That(snapshot.SourceFrameId.IsNull, Is.True);
                Assert.That(snapshot.TargetFrameId.IsNull, Is.True);
            });
        }

        [Test]
        public void VisionExtrinsicCalibrationSnapshotRecordEqualityIsStructural()
        {
            var nodeId = new NodeId(9001u, 3);
            var pose = new VisionPose3DDataType { FrameId = "tcp" };
            var left = new VisionExtrinsicCalibrationSnapshot
            {
                NodeId = nodeId,
                CalibrationId = "cal",
                PerformedAt = new DateTimeUtc(new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc)),
                Valid = true,
                ResidualError = 0.42,
                Method = "Tsai",
                Mount = VisionCalibrationMountEnum.EyeToHand,
                SourceFrameId = new NodeId(1u, 3),
                TargetFrameId = new NodeId(2u, 3),
                Transform = pose
            };
            var right = left with { };

            Assert.Multiple(() =>
            {
                Assert.That(right, Is.EqualTo(left),
                    "sealed record `with { }` must yield a structurally equal snapshot");
                Assert.That(right.GetHashCode(), Is.EqualTo(left.GetHashCode()));
                Assert.That(right, Is.Not.SameAs(left));
                Assert.That(right.NodeId, Is.EqualTo(nodeId));
                Assert.That(right.Transform, Is.SameAs(pose));
                Assert.That(right.SourceFrameId, Is.EqualTo(new NodeId(1u, 3)));
                Assert.That(right.TargetFrameId, Is.EqualTo(new NodeId(2u, 3)));
            });
        }

        [Test]
        public void ObserveDetectionsAsyncRejectsNullStreamingWithArgumentNullException()
        {
            var harness = new VisionSessionHarness();
            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);

            Assert.That(() => reader.ObserveDetectionsAsync(null!),
                Throws.InstanceOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("streaming"));
        }

        [Test]
        public void ObserveInspectionAsyncRejectsNullStreamingWithArgumentNullException()
        {
            var harness = new VisionSessionHarness();
            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);

            Assert.That(() => reader.ObserveInspectionAsync(null!),
                Throws.InstanceOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("streaming"));
        }

        [Test]
        public void ObserveDetectionsAsyncThrowsBadNotFoundWhenResultDoesNotExposeDetections()
        {
            var harness = new VisionSessionHarness();
            var streaming = new Mock<IStreamingSubscription>().Object;
            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);

            var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (var _ in reader.ObserveDetectionsAsync(streaming)
                    .ConfigureAwait(false))
                {
                }
            });

            Assert.That((uint)ex!.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void ObserveInspectionAsyncThrowsBadNotFoundWhenResultDoesNotExposeCharacteristics()
        {
            var harness = new VisionSessionHarness();
            var streaming = new Mock<IStreamingSubscription>().Object;
            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);

            var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (var _ in reader.ObserveInspectionAsync(streaming)
                    .ConfigureAwait(false))
                {
                }
            });

            Assert.That((uint)ex!.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void ObserveSegmentationAsyncThrowsBadNotFoundWhenResultDoesNotExposeMask()
        {
            var harness = new VisionSessionHarness();
            var streaming = new Mock<IStreamingSubscription>().Object;
            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);

            var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (var _ in reader.ObserveSegmentationAsync(streaming)
                    .ConfigureAwait(false))
                {
                }
            });

            Assert.That((uint)ex!.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task ObserveDetectionsAsyncCompletesGracefullyWhenSubscribeCompletesWithNoNotifications()
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.Detections,
                new(3210u, 3), new Variant(ArrayOf<ExtensionObject>.Empty));

            IReadOnlyList<NodeId>? monitored = null;
            var streaming = new Mock<IStreamingSubscription>();
            streaming
                .Setup(s => s.SubscribeDataChangesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IReadOnlyList<NodeId>,
                    Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions?,
                    CancellationToken>((ids, _, ct) =>
                {
                    monitored = ids;
                    return EmptyStreamAsync(ct);
                });

            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);
            int received = 0;
            await foreach (var _ in reader.ObserveDetectionsAsync(streaming.Object)
                .ConfigureAwait(false))
            {
                received++;
            }

            Assert.Multiple(() =>
            {
                Assert.That(received, Is.EqualTo(0),
                    "an empty upstream stream must yield zero snapshots — the reader " +
                    "may not fabricate data");
                Assert.That(monitored, Is.Not.Null);
                Assert.That(monitored!.Count, Is.EqualTo(1));
                Assert.That(monitored[0], Is.EqualTo(new NodeId(3210u, 3)),
                    "the observe iterator must have resolved the Detections child NodeId");
            });
        }

        [Test]
        public async Task ObserveInspectionAsyncCompletesGracefullyWhenSubscribeCompletesWithNoNotifications()
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.Characteristics,
                new(3220u, 3), new Variant(ArrayOf<ExtensionObject>.Empty));

            IReadOnlyList<NodeId>? monitored = null;
            var streaming = new Mock<IStreamingSubscription>();
            streaming
                .Setup(s => s.SubscribeDataChangesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IReadOnlyList<NodeId>,
                    Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions?,
                    CancellationToken>((ids, _, ct) =>
                {
                    monitored = ids;
                    return EmptyStreamAsync(ct);
                });

            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);
            int received = 0;
            await foreach (var _ in reader.ObserveInspectionAsync(streaming.Object)
                .ConfigureAwait(false))
            {
                received++;
            }

            Assert.That(received, Is.EqualTo(0));
            Assert.That(monitored, Is.Not.Null);
            Assert.That(monitored![0], Is.EqualTo(new NodeId(3220u, 3)));
        }

        [Test]
        public async Task ObserveSegmentationAsyncCompletesGracefullyWhenSubscribeCompletesWithNoNotifications()
        {
            var harness = new VisionSessionHarness();
            harness.AddValueChild(harness.ResultNodeId, BrowseNames.Mask,
                new(3230u, 3), new Variant(ByteString.Empty));

            IReadOnlyList<NodeId>? monitored = null;
            var streaming = new Mock<IStreamingSubscription>();
            streaming
                .Setup(s => s.SubscribeDataChangesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IReadOnlyList<NodeId>,
                    Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions?,
                    CancellationToken>((ids, _, ct) =>
                {
                    monitored = ids;
                    return EmptyStreamAsync(ct);
                });

            VisionResultReader reader = harness.Client.Result(harness.ResultNodeId);
            int received = 0;
            await foreach (var _ in reader.ObserveSegmentationAsync(streaming.Object)
                .ConfigureAwait(false))
            {
                received++;
            }

            Assert.That(received, Is.EqualTo(0));
            Assert.That(monitored, Is.Not.Null);
            Assert.That(monitored![0], Is.EqualTo(new NodeId(3230u, 3)));
        }

        [Test]
        public async Task GetStreamEndpointAsyncReturnsSessionFromServer()
        {
            var harness = new VisionSessionHarness();
            var session = new VisionStreamSessionDataType
            {
                SessionToken = new ByteString(new byte[] { 42, 43 }),
                Uri = "rtsp://cam.local/live",
                Protocol = VisionStreamProtocolEnum.Rtsp,
                ExpiresAt = new DateTimeUtc(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            };
            harness.ConfigureCall(StatusCodes.Good,
                Variant.FromStructure(session),
                new Variant(harness.StreamEndpointNodeId));

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            VisionStreamSessionDataType result = await media.GetStreamEndpointAsync(
                harness.StreamEndpointNodeId,
                "default",
                VisionStreamProtocolEnum.Rtsp).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Uri, Is.EqualTo("rtsp://cam.local/live"));
                Assert.That(result.Protocol, Is.EqualTo(VisionStreamProtocolEnum.Rtsp));
                Assert.That(result.SessionToken.ToArray(), Is.EqualTo(new byte[] { 42, 43 }),
                    "the token bytes must survive the round-trip through the proxy layer");
            });
        }

        [Test]
        public async Task ReadLatestClipMetadataAsyncReturnsMetadataWhenPresent()
        {
            var harness = new VisionSessionHarness();
            var metadata = new VisionImageReferenceDataType
            {
                Uri = "opc.ua://server/clips/latest.json",
                DigestAlgorithm = "SHA-256",
                PixelFormat = "Mono8",
                Width = 640,
                Height = 480
            };
            harness.AddValueChild(harness.ClipEndpointNodeId, BrowseNames.LatestClipMetadata,
                new(2401u, 3), Variant.FromStructure(metadata));

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            VisionImageReferenceDataType? result = await media
                .ReadLatestClipMetadataAsync(harness.ClipEndpointNodeId)
                .ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Uri, Is.EqualTo("opc.ua://server/clips/latest.json"));
            Assert.That(result.PixelFormat, Is.EqualTo("Mono8"));
            Assert.That(result.Width, Is.EqualTo(640u));
            Assert.That(result.Height, Is.EqualTo(480u));
        }

        [Test]
        public async Task ReadLatestClipMetadataAsyncReturnsNullWhenAbsent()
        {
            var harness = new VisionSessionHarness();
            // No metadata node bound — TryReadStructureAsync short-circuits on a null NodeId.

            VisionMediaClient media = harness.Client.Media(harness.MediaNodeId);
            VisionImageReferenceDataType? result = await media
                .ReadLatestClipMetadataAsync(harness.ClipEndpointNodeId)
                .ConfigureAwait(false);

            Assert.That(result, Is.Null,
                "when the LatestClipMetadata child is absent the reader must return null " +
                "rather than fabricate an empty descriptor");
        }

        private static async IAsyncEnumerable<DataValueChange> EmptyStreamAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }

        [Test]
        public void VisionClientFactoryCreateAsyncInvokesTheInjectedSessionFactoryAndPropagatesCancellation()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var telemetry = new Mock<ITelemetryContext>().Object;
            CancellationToken observedToken = default;
            int invocations = 0;
            Func<CancellationToken, Task<ManagedSession>> sessionFactory = ct =>
            {
                Interlocked.Increment(ref invocations);
                observedToken = ct;
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<ManagedSession>(null!);
            };
            var factory = new VisionClientFactory(sessionFactory, telemetry);

            Assert.That(
                async () => await factory.CreateAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.Multiple(() =>
            {
                Assert.That(invocations, Is.EqualTo(1),
                    "CreateAsync must forward straight to the session factory once");
                Assert.That(observedToken, Is.EqualTo(cts.Token),
                    "the exact caller token must be threaded through — not default");
            });
        }

        [Test]
        public void VisionClientFactoryConstructorRejectsNullSessionFactory()
        {
            var telemetry = new Mock<ITelemetryContext>().Object;

            Assert.That(() => new VisionClientFactory(null!, telemetry),
                Throws.InstanceOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("sessionFactory"));
        }

        [Test]
        public void VisionClientFactoryConstructorRejectsNullTelemetry()
        {
            Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                _ => Task.FromResult<ManagedSession>(null!);

            Assert.That(() => new VisionClientFactory(sessionFactory, null!),
                Throws.InstanceOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("telemetry"));
        }
    }
}
