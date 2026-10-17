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
using Opc.Ua.OpenUsd.Connector.Viewer;
using Opc.Ua.Vision.OpenUsd;
using OpenUsd.Rendering;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Exercises presenter failure and capture ownership without constructing a native renderer or graphics device.
    /// </summary>
    [TestFixture]
    public sealed class ViewportCaptureFailureTests
    {
        /// <summary>
        /// Selecting B cannot make a frame prepared with A a successful capture for camera B.
        /// </summary>
        [Test]
        public async Task CameraChangeAfterPreparationIsRejectedBeforeReadbackAsync()
        {
            using var captures = new UsdViewportCaptureQueue();
            var limits = new UsdViewportLimits();
            var size = new ViewportDimensions(2, 2);
            SceneCameraCaptureRequest request = CreateRequest() with { PrimPath = "/World/B" };
            Task<RetainedSceneFrame> pending = captures.CaptureAsync(request, CancellationToken.None).AsTask();
            UsdViewportCaptureQueue.Readback? readback = captures.TryStart();
            Assert.That(readback, Is.Not.Null);
            try
            {
                UsdViewportRenderer.RequireCapture(request, limits, size, "/World/B", "/World/A");
                readback!.Complete(CreateFrame());
            }
            catch (InvalidOperationException error)
            {
                readback!.Complete(error);
            }
            await Assert.ThatAsync(() => pending, Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(
                () => UsdViewportRenderer.RequireCapture(request, limits, size, "/World/B", "/World/B"),
                Throws.Nothing);
            Assert.That(
                () => UsdViewportRenderer.RequireCapture(request, limits, size, "/World/A", "/World/B"),
                Throws.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// Existing managed presenter callers can omit capture wiring and keep their native-statistics argument.
        /// </summary>
        [Test]
        public async Task ExistingPresenterConstructorShapeRemainsUsableAsync()
        {
            var inner = new Mock<ICompositionViewportPresenter>(MockBehavior.Strict);
            inner.Setup(item => item.DisposeAsync()).Returns(default(ValueTask));
            bool rendererDisposed = false;
            var presenter = new BoundedCompositionViewportPresenter(
                inner.Object,
                new UsdViewportLimits(),
                static (_, _) => default,
                () => rendererDisposed = true,
                static _ => Assert.Fail("Disposal must not report a renderer failure."),
                static () => (1, 2));

            Assert.That(presenter.ActiveGenerations, Is.EqualTo(1));
            Assert.That(presenter.ActiveFrames, Is.EqualTo(2));
            await presenter.DisposeAsync().ConfigureAwait(false);
            Assert.That(rendererDisposed, Is.True);
            inner.Verify(item => item.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Preparation, rendering and device-loss failures settle pending captures and permit a subsequent request.
        /// </summary>
        [TestCase("prepare")]
        [TestCase("render")]
        [TestCase("device")]
        public async Task PresenterFailureCompletesTheQueuedCaptureAsync(string fault)
        {
            using var captures = new UsdViewportCaptureQueue();
            var error = new InvalidOperationException("The presenter failed.");
            var inner = new Mock<ICompositionViewportPresenter>(MockBehavior.Strict);
            inner.Setup(item => item.DisposeAsync()).Returns(default(ValueTask));
            if (fault == "render")
            {
                inner.Setup(item => item.RenderAsync(It.IsAny<ICompositionPresentationFrame>(), It.IsAny<CancellationToken>()))
                    .Throws(error);
            }
            else if (fault == "device")
            {
                inner.Setup(item => item.RenderAsync(It.IsAny<ICompositionPresentationFrame>(), It.IsAny<CancellationToken>()))
                    .Returns(
                        new ValueTask<CompositionFrameRenderResult>(
                            new CompositionFrameRenderResult(
                                CompositionFrameRenderStatus.DeviceLost,
                                false,
                                CompositionFrameSynchronization.Automatic)));
            }
            Exception? reported = null;
            await using var presenter = new BoundedCompositionViewportPresenter(
                inner.Object,
                new UsdViewportLimits(),
                (_, _) => fault == "prepare" ? throw error : default,
                static () => { },
                failure => reported = failure,
                captures: captures);
            var frame = new Mock<ICompositionPresentationFrame>(MockBehavior.Strict);
            frame.SetupGet(item => item.Image).Returns(
                new CompositionExternalImage(
                    "managed-test",
                    new ViewportDimensions(2, 2),
                    CompositionExternalImageFormat.R8G8B8A8UNorm,
                    0,
                    16,
                    true));
            Task<RetainedSceneFrame> pending = captures.CaptureAsync(CreateRequest(), CancellationToken.None).AsTask();

            if (fault == "device")
            {
                CompositionFrameRenderResult result = await presenter.RenderAsync(frame.Object, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(result.Status, Is.EqualTo(CompositionFrameRenderStatus.DeviceLost));
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await presenter.RenderAsync(frame.Object, CancellationToken.None).ConfigureAwait(false),
                    Throws.Exception.SameAs(error)).ConfigureAwait(false);
            }
            Assert.That(reported, Is.TypeOf<InvalidOperationException>());
            await Assert.ThatAsync(
                () => pending.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.Exception.SameAs(reported)).ConfigureAwait(false);

            Task<RetainedSceneFrame> next = captures.CaptureAsync(CreateRequest(), CancellationToken.None).AsTask();
            UsdViewportCaptureQueue.Readback? readback = captures.TryStart();
            Assert.That(readback, Is.Not.Null);
            RetainedSceneFrame pixels = CreateFrame();
            readback!.Complete(pixels);
            Assert.That(await next.ConfigureAwait(false), Is.SameAs(pixels));
        }

        /// <summary>
        /// Failure, cancellation and disposal cannot complete an active readback until its owner drains it.
        /// </summary>
        [TestCase("failure")]
        [TestCase("cancellation")]
        [TestCase("disposal")]
        public async Task StartedReadbackDrainsBeforeFailureOrCancellationAsync(string signal)
        {
            using var captures = new UsdViewportCaptureQueue();
            using var cancellation = new CancellationTokenSource();
            Task<RetainedSceneFrame> pending = captures.CaptureAsync(CreateRequest(), cancellation.Token).AsTask();
            UsdViewportCaptureQueue.Readback? readback = captures.TryStart();
            Assert.That(readback, Is.Not.Null);
            Assert.That(captures.TryStart(), Is.Null, "A readback cannot be claimed twice.");
            var error = new InvalidOperationException("Device lost during readback.");
            if (signal == "failure")
            {
                captures.Fail(error);
                captures.Fail(new InvalidOperationException("A later failure must not hide the first."));
            }
            else if (signal == "cancellation")
            {
                cancellation.Cancel();
            }
            else
            {
                captures.Dispose();
            }
            Assert.That(pending.IsCompleted, Is.False, "The borrowed readback still owns its native lifetime.");
            if (signal != "disposal")
            {
                await Assert.ThatAsync(
                    async () => await captures.CaptureAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            }

            readback!.Complete(CreateFrame());
            if (signal == "failure")
            {
                await Assert.ThatAsync(
                    () => pending,
                    Throws.Exception.SameAs(error)).ConfigureAwait(false);
            }
            else if (signal == "cancellation")
            {
                await Assert.ThatAsync(
                    () => pending,
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(
                    () => pending,
                    Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
            }
            if (signal != "disposal")
            {
                Task<RetainedSceneFrame> next = captures.CaptureAsync(CreateRequest(), CancellationToken.None).AsTask();
                UsdViewportCaptureQueue.Readback? nextReadback = captures.TryStart();
                Assert.That(nextReadback, Is.Not.Null);
                RetainedSceneFrame frame = CreateFrame();
                nextReadback!.Complete(frame);
                Assert.That(await next.ConfigureAwait(false), Is.SameAs(frame));
            }
        }

        /// <summary>
        /// Canceling queued work releases admission without running a renderer callback.
        /// </summary>
        [Test]
        public async Task QueuedCancellationReleasesAdmissionAsync()
        {
            using var captures = new UsdViewportCaptureQueue();
            using var cancellation = new CancellationTokenSource();
            Task<RetainedSceneFrame> pending = captures.CaptureAsync(CreateRequest(), cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.ThatAsync(
                () => pending,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(captures.TryStart(), Is.Null);

            Task<RetainedSceneFrame> next = captures.CaptureAsync(CreateRequest(), CancellationToken.None).AsTask();
            UsdViewportCaptureQueue.Readback? readback = captures.TryStart();
            Assert.That(readback, Is.Not.Null);
            var error = new InvalidOperationException("Readback failed.");
            readback!.Complete(error);
            await Assert.ThatAsync(
                () => next,
                Throws.Exception.SameAs(error)).ConfigureAwait(false);
        }

        /// <summary>
        /// Disposing a queue faults waiting work and rejects new captures.
        /// </summary>
        [Test]
        public async Task DisposalCompletesQueuedWorkAndRejectsNewRequestsAsync()
        {
            using var captures = new UsdViewportCaptureQueue();
            Task<RetainedSceneFrame> pending = captures.CaptureAsync(CreateRequest(), CancellationToken.None).AsTask();
            captures.Dispose();
            await Assert.ThatAsync(
                () => pending,
                Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await captures.CaptureAsync(CreateRequest(), CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
            Assert.That(captures.TryStart(), Is.Null);
        }

        private static SceneCameraCaptureRequest CreateRequest()
        {
            return new SceneCameraCaptureRequest { StageIdentifier = "retained-stage", Width = 2, Height = 2 };
        }

        private static RetainedSceneFrame CreateFrame()
        {
            return new RetainedSceneFrame(2, 2, ByteString.From(new byte[16]), 1, 1, 1);
        }
    }
}
