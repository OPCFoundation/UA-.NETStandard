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
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision.OpenUsd;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class RenderedVisionCaptureTests
    {
        /// <summary>
        /// Rejects budgets outside either supported interval before invoking the frame source.
        /// </summary>
        [TestCase(0, 4, "maximumDimension")]
        [TestCase(8193, 4, "maximumDimension")]
        [TestCase(1, 3, "maximumFrameBytes")]
        [TestCase(1, (64 * 1024 * 1024) + 1, "maximumFrameBytes")]
        public void ConstructorRejectsInvalidBudgetsWithoutStartingCapture(
            int maximumDimension,
            int maximumFrameBytes,
            string parameter)
        {
            var source = new Source();

            Assert.That(
                () => new RetainedSceneCameraCaptureProvider(
                    source, DefaultTelemetry.Create(static _ => { }), maximumDimension, maximumFrameBytes),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo(parameter));
            Assert.That(source.Captures, Is.Zero);
        }

        /// <summary>
        /// Accepts both budget endpoints without allocating or acquiring a frame.
        /// </summary>
        [TestCase(1, 4)]
        [TestCase(8192, 64 * 1024 * 1024)]
        public void ConstructorAcceptsBudgetEndpointsWithoutStartingCapture(
            int maximumDimension,
            int maximumFrameBytes)
        {
            var source = new Source();
            var provider = new RetainedSceneCameraCaptureProvider(
                source, DefaultTelemetry.Create(static _ => { }), maximumDimension, maximumFrameBytes);

            Assert.That(provider.Backend, Is.SameAs(source.Backend));
            Assert.That(source.Captures, Is.Zero);
        }

        /// <summary>
        /// Reports missing construction dependencies without starting a frame.
        /// </summary>
        [Test]
        public void ConstructorRequiresSourceAndTelemetry()
        {
            var source = new Source();
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new RetainedSceneCameraCaptureProvider(null!, telemetry),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("source"));
                Assert.That(
                    () => new RetainedSceneCameraCaptureProvider(source, null!),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("telemetry"));
                Assert.That(source.Captures, Is.Zero);
            });
        }

        [Test]
        public async Task CaptureUsesTheRetainedSourceAndEncodesItsExactPixelsAsync()
        {
            var source = new Source();
            DateTime timestamp = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
            var provider = new RetainedSceneCameraCaptureProvider(source, DefaultTelemetry.Create(static _ => { }));
            Assert.That(source.Captures, Is.Zero, "Construction cannot start a device or frame.");

            SceneCameraCaptureResult first = await provider.CaptureAsync(
                Request() with { TimestampUtc = timestamp }, CancellationToken.None).ConfigureAwait(false);
            SceneCameraCaptureResult second = await provider.CaptureAsync(
                Request() with { TimestampUtc = timestamp }, CancellationToken.None).ConfigureAwait(false);

            Assert.That(source.Captures, Is.EqualTo(2));
            Assert.That(first.Status, Is.EqualTo(SceneCameraCaptureStatus.Succeeded));
            Assert.That(second.Status, Is.EqualTo(SceneCameraCaptureStatus.Succeeded));
            Assert.That(first.TimestampUtc, Is.EqualTo(timestamp));
            Assert.That(first.Backend, Is.SameAs(source.Backend));
            Assert.That(first.Width, Is.EqualTo(2));
            Assert.That(first.Height, Is.EqualTo(1));
            Assert.That(first.Image.Span[..8].ToArray(), Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            Assert.That(DecodeStoredPixels(first.Image.Span), Is.EqualTo(Source.Pixels.ToArray()));
            Assert.That(DecodeStoredPixels(second.Image.Span), Is.EqualTo(Source.Pixels.ToArray()));
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(5, 1)]
        [TestCase(1, 5)]
        [TestCase(4, 3)]
        public async Task CaptureBoundsRejectBeforeCallingTheRetainedRendererAsync(int width, int height)
        {
            var source = new Source();
            var provider = new RetainedSceneCameraCaptureProvider(
                source, DefaultTelemetry.Create(static _ => { }), maximumDimension: 4, maximumFrameBytes: 32);

            SceneCameraCaptureResult result = await provider.CaptureAsync(
                Request() with { Width = width, Height = height }, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
            Assert.That(result.Image.IsNull, Is.True);
            Assert.That(source.Captures, Is.Zero);
        }

        [TestCase("")]
        [TestCase("other.usda")]
        [TestCase("https://example.invalid/scene.usda")]
        public async Task CaptureNeverOpensAnAlternativeStageOrUriAsync(string stage)
        {
            var source = new Source();
            var provider = new RetainedSceneCameraCaptureProvider(source, DefaultTelemetry.Create(static _ => { }));
            SceneCameraCaptureResult result = await provider.CaptureAsync(
                Request() with { StageIdentifier = stage }, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
            Assert.That(source.Captures, Is.Zero);
        }

        [TestCase(0, 1, false)]
        [TestCase(1, 0, false)]
        [TestCase(-1, 1, false)]
        [TestCase(1, 1, true)]
        public async Task NoDrawsAndUniformFramesAreExplicitFailuresAsync(int draws, int meshes, bool uniform)
        {
            var source = new Source
            {
                Capture = _ => ValueTask.FromResult(new RetainedSceneFrame(
                    2, 1, uniform ? ByteString.From(new byte[8]) : Source.Pixels, draws, meshes, 7))
            };
            var provider = new RetainedSceneCameraCaptureProvider(source, DefaultTelemetry.Create(static _ => { }));
            SceneCameraCaptureResult result = await provider.CaptureAsync(Request(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.BlankFrame));
            Assert.That(result.Reason, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Image.IsNull, Is.True);
        }

        [Test]
        public async Task MalformedReadbacksAreRejectedBeforeEncodingAsync()
        {
            var source = new Source
            {
                Capture = _ => ValueTask.FromResult(new RetainedSceneFrame(2, 1, ByteString.From(new byte[7]), 1, 1, 7))
            };
            var provider = new RetainedSceneCameraCaptureProvider(source, DefaultTelemetry.Create(static _ => { }));
            SceneCameraCaptureResult result = await provider.CaptureAsync(Request(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(SceneCameraCaptureStatus.RenderFailed));
            Assert.That(result.Image.IsNull, Is.True);
        }

        [Test]
        public async Task UnavailableAndFailedBackendsNeverReturnSuccessfulImagesAsync()
        {
            var source = new Source { Available = false };
            var provider = new RetainedSceneCameraCaptureProvider(source, DefaultTelemetry.Create(static _ => { }));
            SceneCameraCaptureResult unavailable = await provider.CaptureAsync(Request(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(unavailable.Status, Is.EqualTo(SceneCameraCaptureStatus.NoRenderingBackend));
            Assert.That(source.Captures, Is.Zero);
            source.Available = true;
            source.Capture = _ => throw new InvalidOperationException("device lost");
            SceneCameraCaptureResult failed = await provider.CaptureAsync(Request(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(failed.Status, Is.EqualTo(SceneCameraCaptureStatus.RenderFailed));
            Assert.That(failed.Image.IsNull, Is.True);
        }

        [Test]
        public async Task ConcurrentCaptureIsBoundedAndCancellationWaitsForTheOwnedReadbackAsync()
        {
            var release = new TaskCompletionSource<RetainedSceneFrame>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new Source { Capture = _ => new ValueTask<RetainedSceneFrame>(release.Task) };
            var provider = new RetainedSceneCameraCaptureProvider(source, DefaultTelemetry.Create(static _ => { }));
            using var cancellation = new CancellationTokenSource();
            Task<SceneCameraCaptureResult> pending = provider.CaptureAsync(Request(), cancellation.Token).AsTask();
            SceneCameraCaptureResult rejected = await provider.CaptureAsync(Request(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(rejected.Status, Is.EqualTo(SceneCameraCaptureStatus.InvalidRequest));
            Assert.That(source.Captures, Is.EqualTo(1));
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(pending.IsCompleted, Is.False,
                "Cancellation cannot detach an already-running native readback.");
            release.TrySetResult(new RetainedSceneFrame(2, 1, Source.Pixels, 1, 1, 9));
            Assert.That(() => pending, Throws.InstanceOf<OperationCanceledException>());
            source.Capture = _ => ValueTask.FromResult(new RetainedSceneFrame(2, 1, Source.Pixels, 1, 1, 10));
            SceneCameraCaptureResult next = await provider.CaptureAsync(Request(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(next.Status, Is.EqualTo(SceneCameraCaptureStatus.Succeeded));
        }

        private static SceneCameraCaptureRequest Request() => new()
        {
            StageIdentifier = "owned.usda",
            PrimPath = "/World/Camera",
            Width = 2,
            Height = 1
        };

        private static byte[] DecodeStoredPixels(ReadOnlySpan<byte> png)
        {
            int offset = 8;
            using var compressed = new MemoryStream();
            while (offset < png.Length)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[offset..]));
                if (png.Slice(offset + 4, 4).SequenceEqual("IDAT"u8))
                {
                    compressed.Write(png.Slice(offset + 8, length));
                }
                offset += length + 12;
            }
            compressed.Position = 0;
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var pixels = new MemoryStream();
            zlib.CopyTo(pixels);
            byte[] row = pixels.ToArray();
            Assert.That(row[0], Is.Zero, "The PNG encoder uses the identity scanline filter.");
            return row[1..];
        }

        private sealed class Source : IRetainedSceneFrameSource
        {
            public string StageIdentifier => "owned.usda";
            public bool Available { get; set; } = true;
            public SceneCameraCaptureBackend Backend => Available ? s_backend : SceneCameraCaptureBackend.None;
            public int Captures { get; private set; }
            public Func<CancellationToken, ValueTask<RetainedSceneFrame>> Capture { get; set; } =
                static _ => ValueTask.FromResult(new RetainedSceneFrame(2, 1, Pixels, 1, 1, 4));

            public ValueTask<RetainedSceneFrame> CaptureFrameAsync(
                SceneCameraCaptureRequest request, CancellationToken cancellationToken)
            {
                Captures++;
                Assert.That(request.PrimPath, Is.EqualTo("/World/Camera"));
                return Capture(cancellationToken);
            }

            public static readonly ByteString Pixels = ByteString.From([1, 2, 3, 255, 4, 5, 6, 255]);
            private static readonly SceneCameraCaptureBackend s_backend =
                new() { Name = "Retained test", IsAvailable = true };
        }
    }
}
