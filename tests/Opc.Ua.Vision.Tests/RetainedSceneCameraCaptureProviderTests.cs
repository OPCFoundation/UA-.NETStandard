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
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.Vision.OpenUsd;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Verifies retained-frame admission, encoding and lifetime contracts using only managed frame sources.
    /// </summary>
    [TestFixture]
    public sealed class RetainedSceneCameraCaptureProviderTests
    {
        /// <summary>
        /// Invalid allocation limits are rejected at construction.
        /// </summary>
        [TestCase(0, 16, "maximumDimension")]
        [TestCase(8193, 16, "maximumDimension")]
        [TestCase(4, 3, "maximumFrameBytes")]
        [TestCase(4, 67108865, "maximumFrameBytes")]
        public void ConstructorRejectsInvalidLimits(int dimension, int bytes, string parameter)
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            Assert.That(
                () => new RetainedSceneCameraCaptureProvider(
                    source.Object,
                    NUnitTelemetryContext.Create(),
                    dimension,
                    bytes),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo(parameter));
        }

        /// <summary>
        /// Missing collaborators and requests are rejected without a frame call.
        /// </summary>
        [Test]
        public async Task NullInputsAreRejectedBeforeCaptureAsync()
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            Assert.Multiple(
                () =>
                {
                    Assert.That(
                        () => new RetainedSceneCameraCaptureProvider(null!, NUnitTelemetryContext.Create()),
                        Throws.ArgumentNullException);
                    Assert.That(
                        () => new RetainedSceneCameraCaptureProvider(source.Object, null!),
                        Throws.ArgumentNullException);
                });
            RetainedSceneCameraCaptureProvider provider = CreateProvider(source);
            await Assert.ThatAsync(
                async () => await provider.CaptureAsync(null!, CancellationToken.None).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
            source.Verify(
                item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Invalid requests fail admission before the borrowed source can allocate a frame.
        /// </summary>
        [TestCase("NullStage")]
        [TestCase("EmptyStage")]
        [TestCase("ForeignStage")]
        [TestCase("ZeroWidth")]
        [TestCase("NegativeHeight")]
        [TestCase("WidthLimit")]
        [TestCase("HeightLimit")]
        [TestCase("ByteLimit")]
        [TestCase("Format")]
        [TestCase("NaN")]
        [TestCase("PositiveInfinity")]
        [TestCase("NegativeInfinity")]
        [TestCase("PrimPathLimit")]
        [TestCase("LocalTimestamp")]
        [TestCase("UnspecifiedTimestamp")]
        public async Task InvalidRequestsNeverReachTheFrameSourceAsync(string fault)
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            RetainedSceneCameraCaptureProvider provider = CreateProvider(source);
            SceneCameraCaptureRequest valid = CreateRequest();
            SceneCameraCaptureRequest request = fault switch
            {
                "NullStage" => valid with { StageIdentifier = null! },
                "EmptyStage" => valid with { StageIdentifier = string.Empty },
                "ForeignStage" => valid with { StageIdentifier = "another-stage" },
                "ZeroWidth" => valid with { Width = 0 },
                "NegativeHeight" => valid with { Height = -1 },
                "WidthLimit" => valid with { Width = 5 },
                "HeightLimit" => valid with { Height = 5 },
                "ByteLimit" => valid with { Width = 3 },
                "Format" => valid with { Format = (SceneCameraImageFormat)999 },
                "NaN" => valid with { TimeCode = double.NaN },
                "PositiveInfinity" => valid with { TimeCode = double.PositiveInfinity },
                "NegativeInfinity" => valid with { TimeCode = double.NegativeInfinity },
                "PrimPathLimit" => valid with { PrimPath = new string('a', 513) },
                "LocalTimestamp" => valid with { TimestampUtc = DateTime.SpecifyKind(s_timestamp, DateTimeKind.Local) },
                "UnspecifiedTimestamp" => valid with
                {
                    TimestampUtc = DateTime.SpecifyKind(s_timestamp, DateTimeKind.Unspecified)
                },
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };

            SceneCameraCaptureResult result = await provider.CaptureAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
                    Assert.That(result.Reason, Is.Not.Empty);
                    Assert.That(result.Image.IsNull, Is.True);
                });
            source.Verify(
                item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Exact dimension, byte and prim-path limits are inclusive and preserve the requested timestamp.
        /// </summary>
        [Test]
        public async Task ExactLimitsProduceAPngWithTheRequestedEvidenceAsync()
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            var provider = new RetainedSceneCameraCaptureProvider(
                source.Object,
                NUnitTelemetryContext.Create(),
                maximumDimension: 2,
                maximumFrameBytes: 16);
            SceneCameraCaptureRequest request = CreateRequest() with { PrimPath = new string('a', 512) };

            SceneCameraCaptureResult result = await provider.CaptureAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.Succeeded));
                    Assert.That(result.Width, Is.EqualTo(2));
                    Assert.That(result.Height, Is.EqualTo(2));
                    Assert.That(result.Format, Is.EqualTo(SceneCameraImageFormat.Png));
                    Assert.That(result.TimestampUtc, Is.EqualTo(s_timestamp));
                    Assert.That(result.Backend, Is.SameAs(provider.Backend));
                    Assert.That(result.Image.Span[..8].ToArray(), Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
                    Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.Image.Span.Slice(16, 4)), Is.EqualTo(2));
                    Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.Image.Span.Slice(20, 4)), Is.EqualTo(2));
                });
            source.Verify(item => item.CaptureFrameAsync(request, CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// A caller that omits a timestamp receives time from the injected provider.
        /// </summary>
        [Test]
        public async Task MissingTimestampUsesTheInjectedClockAsync()
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            var clock = new FakeTimeProvider(new DateTimeOffset(s_timestamp));
            var provider = new RetainedSceneCameraCaptureProvider(
                source.Object,
                NUnitTelemetryContext.Create(),
                timeProvider: clock);

            SceneCameraCaptureResult result = await provider.CaptureAsync(
                CreateRequest() with { TimestampUtc = null },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.TimestampUtc, Is.EqualTo(s_timestamp));
            Assert.That(result.TimestampUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        /// <summary>
        /// Backend unavailability is reported without attempting a frame or probing a device.
        /// </summary>
        [Test]
        public async Task UnavailableBackendDoesNotCallTheFrameSourceAsync()
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            source.SetupGet(item => item.Backend).Returns(SceneCameraCaptureBackend.None);
            RetainedSceneCameraCaptureProvider provider = CreateProvider(source);

            SceneCameraCaptureResult result = await provider.CaptureAsync(CreateRequest(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.NoRenderingBackend));
            Assert.That(result.Reason, Is.EqualTo(SceneCameraCaptureBackend.None.UnavailableReason));
            Assert.That(result.Image.IsNull, Is.True);
            source.Verify(
                item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Returned metadata and storage must match the admitted request before PNG encoding.
        /// </summary>
        [TestCase("Null")]
        [TestCase("Width")]
        [TestCase("Height")]
        [TestCase("ShortPixels")]
        [TestCase("LongPixels")]
        [TestCase("NullPixels")]
        public async Task InconsistentFramesAreNotSuccessfulImagesAsync(string fault)
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            RetainedSceneFrame valid = CreateFrame();
            RetainedSceneFrame frame = fault switch
            {
                "Null" => null!,
                "Width" => valid with { Width = 3 },
                "Height" => valid with { Height = 1 },
                "ShortPixels" => valid with { Rgba = ByteString.From(new byte[15]) },
                "LongPixels" => valid with { Rgba = ByteString.From(new byte[17]) },
                "NullPixels" => valid with { Rgba = default },
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
            source.Setup(item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<RetainedSceneFrame>(frame));

            SceneCameraCaptureResult result = await CreateProvider(source).CaptureAsync(
                CreateRequest(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.RenderFailed));
            Assert.That(result.Image.IsNull, Is.True);
        }

        /// <summary>
        /// Empty geometry and uniform targets remain explicit failures rather than blank successful PNGs.
        /// </summary>
        [TestCase(0, 1, false)]
        [TestCase(-1, 1, false)]
        [TestCase(1, 0, false)]
        [TestCase(1, -1, false)]
        [TestCase(1, 1, true)]
        public async Task BlankFramesAreRejectedAsync(int draws, int meshes, bool uniform)
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            RetainedSceneFrame frame = CreateFrame() with { DrawCount = draws, MeshCount = meshes };
            if (uniform)
            {
                frame = frame with { Rgba = ByteString.From(new byte[16]) };
            }
            source.Setup(item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<RetainedSceneFrame>(frame));

            SceneCameraCaptureResult result = await CreateProvider(source).CaptureAsync(
                CreateRequest(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.BlankFrame));
            Assert.That(result.Reason, Is.Not.Empty);
            Assert.That(result.Image.IsNull, Is.True);
        }

        /// <summary>
        /// A source failure is surfaced and releases admission for a later capture.
        /// </summary>
        [Test]
        public async Task SourceFailureDoesNotLeaveTheAdapterBusyAsync()
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            source.Setup(item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()))
                .Throws(new IOException("The retained source failed."));
            RetainedSceneCameraCaptureProvider provider = CreateProvider(source);

            SceneCameraCaptureResult failed = await provider.CaptureAsync(CreateRequest(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(failed.Status, Is.EqualTo(SceneCameraCaptureStatus.RenderFailed));
            Assert.That(failed.Image.IsNull, Is.True);
            source.Setup(item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<RetainedSceneFrame>(CreateFrame()));
            SceneCameraCaptureResult recovered = await provider.CaptureAsync(CreateRequest(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(recovered.Status, Is.EqualTo(SceneCameraCaptureStatus.Succeeded));
        }

        /// <summary>
        /// A canceled caller cannot release admission while its source still owns an in-progress readback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InFlightCaptureDrainsBeforeAnotherRequestIsAdmittedAsync(bool cancel)
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            var drained = new TaskCompletionSource<RetainedSceneFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Setup(item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<RetainedSceneFrame>(drained.Task));
            RetainedSceneCameraCaptureProvider provider = CreateProvider(source);
            using var cancellation = new CancellationTokenSource();
            Task<SceneCameraCaptureResult> first = provider.CaptureAsync(CreateRequest(), cancellation.Token).AsTask();
            if (cancel)
            {
                cancellation.Cancel();
            }
            Assert.That(first.IsCompleted, Is.False);
            SceneCameraCaptureResult competing = await provider.CaptureAsync(CreateRequest(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(competing.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
            source.Verify(
                item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);

            drained.SetResult(CreateFrame());
            if (cancel)
            {
                await Assert.ThatAsync(
                    async () => await first.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            else
            {
                Assert.That((await first.ConfigureAwait(false)).Status, Is.EqualTo(SceneCameraCaptureStatus.Succeeded));
            }
            SceneCameraCaptureResult next = await provider.CaptureAsync(CreateRequest(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(next.Status, Is.EqualTo(SceneCameraCaptureStatus.Succeeded));
            source.Verify(
                item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// A pre-canceled operation never acquires the borrowed frame source.
        /// </summary>
        [Test]
        public async Task CancellationBeforeAdmissionDoesNotCallTheSourceAsync()
        {
            Mock<IRetainedSceneFrameSource> source = CreateSource();
            RetainedSceneCameraCaptureProvider provider = CreateProvider(source);

            await Assert.ThatAsync(
                async () => await provider.CaptureAsync(CreateRequest(), new CancellationToken(canceled: true))
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            source.Verify(
                item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static Mock<IRetainedSceneFrameSource> CreateSource()
        {
            var source = new Mock<IRetainedSceneFrameSource>(MockBehavior.Strict);
            source.SetupGet(item => item.StageIdentifier).Returns("retained-stage");
            source.SetupGet(item => item.Backend).Returns(
                new SceneCameraCaptureBackend
                {
                    Name = "Managed fake",
                    DeviceName = "No native device",
                    IsAvailable = true
                });
            source.Setup(item => item.CaptureFrameAsync(It.IsAny<SceneCameraCaptureRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<RetainedSceneFrame>(CreateFrame()));
            return source;
        }

        private static RetainedSceneCameraCaptureProvider CreateProvider(Mock<IRetainedSceneFrameSource> source)
        {
            return new RetainedSceneCameraCaptureProvider(
                source.Object,
                NUnitTelemetryContext.Create(),
                maximumDimension: 4,
                maximumFrameBytes: 16);
        }

        private static SceneCameraCaptureRequest CreateRequest()
        {
            return new SceneCameraCaptureRequest
            {
                StageIdentifier = "retained-stage",
                Width = 2,
                Height = 2,
                TimestampUtc = s_timestamp
            };
        }

        private static RetainedSceneFrame CreateFrame()
        {
            return new RetainedSceneFrame(
                2,
                2,
                ByteString.From([255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255]),
                1,
                1,
                7);
        }

        private static readonly DateTime s_timestamp = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    }
}
