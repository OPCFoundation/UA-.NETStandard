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
using NUnit.Framework;
using Opc.Ua.OpenUsd.Connector.Viewer;
using OpenUsd.Rendering;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdViewportBoundsTests
    {
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(9, 1)]
        [TestCase(1, 9)]
        [TestCase(8, 5)]
        public void FrameBoundsRejectBeforeAnyAllocation(int width, int height)
        {
            var limits = new UsdViewportLimits { MaximumDimension = 8, MaximumFrameBytes = 128 };
            Assert.That(() => limits.RequireFrame(new ViewportDimensions(width, height)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(1, 1, 4)]
        [TestCase(8, 4, 128)]
        [TestCase(4, 8, 128)]
        public void FrameBoundsAcceptExactDimensionAndByteBoundary(int width, int height, int bytes)
        {
            var limits = new UsdViewportLimits { MaximumDimension = 8, MaximumFrameBytes = 128 };
            Assert.That(limits.RequireFrame(new ViewportDimensions(width, height)), Is.EqualTo(bytes));
        }

        [Test]
        public void GeometryAdmissionReplacesCostsAndRejectsBeforeChangingTheBudget()
        {
            var budget = new UsdViewportGeometryBudget(
                new UsdViewportLimits { MaximumMeshes = 1, MaximumGeometryBytes = 4624 });
            budget.Upsert(1, 0, 1, 1, 0, false);
            Assert.That(budget.Bytes, Is.EqualTo(4624));
            budget.Upsert(1, 0, 0, 1, 0, false);
            Assert.That(budget.Bytes, Is.EqualTo(4112));
            Assert.That(() => budget.Upsert(1, 0, 2, 1, 0, false), Throws.InvalidOperationException);
            Assert.That(budget.Bytes, Is.EqualTo(4112), "A rejected page cannot consume admitted memory.");
            Assert.That(() => budget.Upsert(1, 1, 0, 0, 0, false), Throws.InvalidOperationException);
            budget.Remove(1, 4);
            Assert.That(budget.Bytes, Is.EqualTo(4112), "Instance identity is part of the retained allocation key.");
            budget.Remove(1, 0);
            Assert.That(budget.Bytes, Is.Zero);
            budget.Upsert(1, 1, 1, 1, 0, false);
            Assert.That(budget.Bytes, Is.EqualTo(4624));
        }

        [TestCase(-1, 1, 0, false)]
        [TestCase(1, -1, 0, false)]
        [TestCase(1, 1, -1, false)]
        [TestCase(1, 1, 33, false)]
        [TestCase(1, 1, 0, true)]
        public void UnsupportedMeshDeclarationsDoNotReserveGeometry(
            int points, int indices, int attributes, bool deform)
        {
            var budget = new UsdViewportGeometryBudget(new UsdViewportLimits());
            Assert.That(() => budget.Upsert(1, 0, points, indices, attributes, deform),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(budget.Bytes, Is.Zero);
        }

        [Test]
        public async Task ResizeRetainsAtMostTwoBoundedGenerationsAndReleasesAllFramesAsync()
        {
            var inner = new TestPresenter();
            var errors = new List<Exception>();
            int rendererDisposals = 0;
            var presenter = new BoundedCompositionViewportPresenter(
                inner, new UsdViewportLimits { MaximumDimension = 8, MaximumPresentationBytes = 1024 },
                static (_, _) => ValueTask.CompletedTask, () => rendererDisposals++, errors.Add);
            await using (presenter.ConfigureAwait(false))
            {
                ICompositionPresentationGeneration first = await presenter.CreateGenerationAsync(
                    new ViewportDimensions(4, 4), 3, CancellationToken.None).ConfigureAwait(false);
                ICompositionPresentationGeneration second = await presenter.CreateGenerationAsync(
                    new ViewportDimensions(4, 5), 3, CancellationToken.None).ConfigureAwait(false);
                Assert.That(presenter.ActiveGenerations, Is.EqualTo(2));
                Assert.That(presenter.ActiveFrames, Is.EqualTo(6));
                Assert.That(async () => await presenter.CreateGenerationAsync(
                    new ViewportDimensions(2, 2), 2, CancellationToken.None).ConfigureAwait(false),
                    Throws.InvalidOperationException);
                Assert.That(inner.Allocations, Is.EqualTo(2), "Reject before calling the native allocation API.");
                Assert.That(errors, Has.Count.EqualTo(1));
                await first.DisposeAsync().ConfigureAwait(false);
                _ = await presenter.CreateGenerationAsync(
                    new ViewportDimensions(2, 2), 2, CancellationToken.None).ConfigureAwait(false);
                Assert.That(presenter.ActiveFrames, Is.EqualTo(5));
                await second.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(presenter.ActiveFrames, Is.Zero);
            Assert.That(presenter.ActiveGenerations, Is.Zero);
            Assert.That(inner.Disposals, Is.EqualTo(1));
            Assert.That(rendererDisposals, Is.EqualTo(1));
            await presenter.DisposeAsync().ConfigureAwait(false);
            Assert.That(rendererDisposals, Is.EqualTo(1), "Repeated host and control disposal is harmless.");
        }

        [TestCase(1)]
        [TestCase(4)]
        public async Task InvalidRingCountsNeverReachTheBackendAsync(int count)
        {
            var inner = new TestPresenter();
            var presenter = new BoundedCompositionViewportPresenter(inner, new UsdViewportLimits(),
                static (_, _) => ValueTask.CompletedTask, static () => { }, static _ => { });
            await using (presenter.ConfigureAwait(false))
            {
                Assert.That(async () => await presenter.CreateGenerationAsync(
                    new ViewportDimensions(4, 4), count, CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(inner.Allocations, Is.Zero);
            }
        }

        [Test]
        public async Task PresentationByteBudgetIsCheckedBeforeNativeAllocationAsync()
        {
            var inner = new TestPresenter();
            var presenter = new BoundedCompositionViewportPresenter(
                inner, new UsdViewportLimits { MaximumPresentationBytes = 255 },
                static (_, _) => ValueTask.CompletedTask, static () => { }, static _ => { });
            await using (presenter.ConfigureAwait(false))
            {
                Assert.That(async () => await presenter.CreateGenerationAsync(
                    new ViewportDimensions(4, 4), 2, CancellationToken.None).ConfigureAwait(false),
                    Throws.InvalidOperationException);
                Assert.That(inner.Allocations, Is.Zero);
                Assert.That(presenter.ActiveFrames, Is.Zero);
            }
        }

        [Test]
        public async Task DisposalWaitsForActualFrameCompletionBeforeReleasingRendererAsync()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var inner = new TestPresenter
            {
                Render = async _ =>
                {
                    entered.TrySetResult();
                    await release.Task.ConfigureAwait(false);
                    return new CompositionFrameRenderResult(
                        CompositionFrameRenderStatus.Presented, true, CompositionFrameSynchronization.Automatic);
                }
            };
            bool rendererDisposed = false;
            var presenter = new BoundedCompositionViewportPresenter(inner, new UsdViewportLimits(),
                static (_, _) => ValueTask.CompletedTask, () => rendererDisposed = true, static _ => { });
            ICompositionPresentationGeneration generation = await presenter.CreateGenerationAsync(
                new ViewportDimensions(4, 4), 2, CancellationToken.None).ConfigureAwait(false);
            Task<CompositionFrameRenderResult> rendering = presenter.RenderAsync(
                generation.Frames[0], CancellationToken.None).AsTask();
            await entered.Task.ConfigureAwait(false);
            Task disposing = presenter.DisposeAsync().AsTask();
            Assert.That(disposing.IsCompleted, Is.False);
            Assert.That(rendererDisposed, Is.False);
            Assert.That(inner.Disposals, Is.Zero);
            release.TrySetResult();
            await rendering.ConfigureAwait(false);
            await disposing.ConfigureAwait(false);
            Assert.That(rendererDisposed, Is.True);
            Assert.That(presenter.ActiveFrames, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PresentedFramesKeepTheRetainedStagePumpAliveAsync(bool backendContinues)
        {
            CompositionFrameSynchronization synchronization =
                CompositionFrameSynchronization.TimelineSemaphores(11, 12, 13, 14);
            var inner = new TestPresenter
            {
                Render = _ => ValueTask.FromResult(new CompositionFrameRenderResult(
                    CompositionFrameRenderStatus.Presented, backendContinues, synchronization))
            };
            var errors = new List<Exception>();
            var presenter = new BoundedCompositionViewportPresenter(inner, new UsdViewportLimits(),
                static (_, _) => ValueTask.CompletedTask, static () => { }, errors.Add);
            await using (presenter.ConfigureAwait(false))
            {
                ICompositionPresentationGeneration generation = await presenter.CreateGenerationAsync(
                    new ViewportDimensions(2, 2), 2, default).ConfigureAwait(false);
                CompositionFrameRenderResult result = await presenter.RenderAsync(generation.Frames[0], default)
                    .ConfigureAwait(false);

                Assert.That(result, Is.EqualTo(new CompositionFrameRenderResult(
                    CompositionFrameRenderStatus.Presented, true, synchronization)),
                    "A capture or stage edit queued after the first frame must reach a later render callback.");
                Assert.That(errors, Is.Empty);
                Assert.That(presenter.ActiveFrames, Is.EqualTo(2));
            }
            Assert.That(presenter.ActiveFrames, Is.Zero);
        }

        [Test]
        public async Task NoFrameDoesNotBecomeAPresentedOrContinuingFrameAsync()
        {
            var expected = new CompositionFrameRenderResult(
                CompositionFrameRenderStatus.NoFrame, false, CompositionFrameSynchronization.Automatic);
            var inner = new TestPresenter { Render = _ => ValueTask.FromResult(expected) };
            var errors = new List<Exception>();
            var presenter = new BoundedCompositionViewportPresenter(inner, new UsdViewportLimits(),
                static (_, _) => ValueTask.CompletedTask, static () => { }, errors.Add);
            await using (presenter.ConfigureAwait(false))
            {
                ICompositionPresentationGeneration generation = await presenter.CreateGenerationAsync(
                    new ViewportDimensions(2, 2), 2, default).ConfigureAwait(false);
                CompositionFrameRenderResult result = await presenter.RenderAsync(generation.Frames[0], default)
                    .ConfigureAwait(false);
                Assert.That(result, Is.EqualTo(expected));
                Assert.That(errors, Is.Empty);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ProbeAvailabilityIsExplicitAndNeverAllocatesARingAsync(bool available)
        {
            var inner = new TestPresenter { Available = available };
            var errors = new List<Exception>();
            var presenter = new BoundedCompositionViewportPresenter(inner, new UsdViewportLimits(),
                static (_, _) => ValueTask.CompletedTask, static () => { }, errors.Add);
            await using (presenter.ConfigureAwait(false))
            {
                CompositionPresenterProbeResult result = await presenter.ProbeAsync(
                    new CompositionPresentationTarget([], [], [], []), default).ConfigureAwait(false);
                Assert.That(result.IsAvailable, Is.EqualTo(available));
                Assert.That(inner.Allocations, Is.Zero);
                if (available)
                {
                    Assert.That(errors, Is.Empty);
                }
                else
                {
                    Assert.That(errors, Has.Count.EqualTo(1));
                    Assert.That(errors[0], Is.TypeOf<PlatformNotSupportedException>());
                    Assert.That(errors[0].Message, Is.EqualTo("No matching interop device."));
                }
            }
        }

        [Test]
        public async Task RequiredProbeFailureReachesTheViewportOwnerWithoutAllocatingAsync()
        {
            var failure = new PlatformNotSupportedException("The compositor has no compatible external image handles.");
            var inner = new TestPresenter { ProbeFailure = failure };
            var errors = new List<Exception>();
            var presenter = new BoundedCompositionViewportPresenter(inner, new UsdViewportLimits(),
                static (_, _) => ValueTask.CompletedTask, static () => { }, errors.Add);
            await using (presenter.ConfigureAwait(false))
            {
                await Assert.ThatAsync(
                    () => presenter.ProbeAsync(
                        new CompositionPresentationTarget([], [], [], []), default).AsTask(),
                    Throws.TypeOf<PlatformNotSupportedException>().With.Message.EqualTo(failure.Message))
                    .ConfigureAwait(false);
                Assert.That(errors, Is.EqualTo(new[] { failure }));
                Assert.That(inner.Allocations, Is.Zero);
                Assert.That(presenter.ActiveFrames, Is.Zero);
            }
        }

        [Test]
        public async Task ProbeCancellationDoesNotReportADeviceFailureAsync()
        {
            var inner = new TestPresenter { ProbeFailure = new OperationCanceledException() };
            var errors = new List<Exception>();
            var presenter = new BoundedCompositionViewportPresenter(inner, new UsdViewportLimits(),
                static (_, _) => ValueTask.CompletedTask, static () => { }, errors.Add);
            await using (presenter.ConfigureAwait(false))
            {
                await Assert.ThatAsync(
                    () => presenter.ProbeAsync(
                        new CompositionPresentationTarget([], [], [], []), default).AsTask(),
                    Throws.TypeOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(errors, Is.Empty);
                Assert.That(inner.Allocations, Is.Zero);
            }
        }

        [Test]
        public async Task DeviceLossIsReportedRatherThanPresentedAsASuccessfulFrameAsync()
        {
            var inner = new TestPresenter
            {
                Render = static _ => ValueTask.FromResult(new CompositionFrameRenderResult(
                    CompositionFrameRenderStatus.DeviceLost, false, CompositionFrameSynchronization.Automatic))
            };
            var errors = new List<Exception>();
            var presenter = new BoundedCompositionViewportPresenter(inner, new UsdViewportLimits(),
                static (_, _) => ValueTask.CompletedTask, static () => { }, errors.Add);
            await using (presenter.ConfigureAwait(false))
            {
                ICompositionPresentationGeneration generation = await presenter.CreateGenerationAsync(
                    new ViewportDimensions(2, 2), 2, default).ConfigureAwait(false);
                CompositionFrameRenderResult result = await presenter.RenderAsync(generation.Frames[0], default)
                    .ConfigureAwait(false);
                Assert.That(result.Status, Is.EqualTo(CompositionFrameRenderStatus.DeviceLost));
                Assert.That(result.ContinueRendering, Is.False);
                Assert.That(errors, Has.Count.EqualTo(1));
                Assert.That(errors[0], Is.TypeOf<InvalidOperationException>());
            }
        }

        [Test]
        public async Task NativeAllocationsCannotBeHiddenByDisposedWrapperCountersAsync()
        {
            var presenter = new BoundedCompositionViewportPresenter(new TestPresenter(), new UsdViewportLimits(),
                static (_, _) => ValueTask.CompletedTask, static () => { }, static _ => { }, static () => (2, 6));
            await presenter.DisposeAsync().ConfigureAwait(false);
            Assert.That(presenter.ActiveGenerations, Is.EqualTo(2));
            Assert.That(presenter.ActiveFrames, Is.EqualTo(6),
                "A backend that reports surviving allocations must not be reported as drained.");
        }

        private sealed class TestPresenter : ICompositionViewportPresenter
        {
            public int Allocations { get; private set; }
            public int Disposals { get; private set; }
            public bool Available { get; init; } = true;
            public Exception? ProbeFailure { get; init; }
            public Func<CancellationToken, ValueTask<CompositionFrameRenderResult>> Render { get; init; } =
                static _ => ValueTask.FromResult(new CompositionFrameRenderResult(
                    CompositionFrameRenderStatus.Presented, true, CompositionFrameSynchronization.Automatic));

            public ValueTask<CompositionPresenterProbeResult> ProbeAsync(
                CompositionPresentationTarget target, CancellationToken cancellationToken)
            {
                if (ProbeFailure is not null)
                {
                    throw ProbeFailure;
                }
                return ValueTask.FromResult(Available
                    ? CompositionPresenterProbeResult.Available("Test only; no GPU.")
                    : CompositionPresenterProbeResult.Unavailable("No matching interop device."));
            }

            public ValueTask<ICompositionPresentationGeneration> CreateGenerationAsync(
                ViewportDimensions size, int frameCount, CancellationToken cancellationToken)
            {
                Allocations++;
                return ValueTask.FromResult<ICompositionPresentationGeneration>(new TestGeneration(size, frameCount));
            }

            public ValueTask<CompositionFrameRenderResult> RenderAsync(
                ICompositionPresentationFrame frame, CancellationToken cancellationToken) => Render(cancellationToken);

            public ValueTask DisposeAsync()
            {
                Disposals++;
                return ValueTask.CompletedTask;
            }
        }

        private sealed class TestGeneration : ICompositionPresentationGeneration
        {
            public TestGeneration(ViewportDimensions size, int count)
            {
                Size = size;
                for (int index = 0; index < count; index++)
                {
                    m_frames.Add(new TestFrame(size, index));
                }
            }

            public ViewportDimensions Size { get; }
            public IReadOnlyList<ICompositionPresentationFrame> Frames => m_frames;

            public ValueTask DisposeAsync()
            {
                m_frames.Clear();
                return ValueTask.CompletedTask;
            }

            private readonly List<ICompositionPresentationFrame> m_frames = [];
        }

        private sealed class TestFrame(ViewportDimensions size, long id) : ICompositionPresentationFrame
        {
            public long AllocationId => id;
            public CompositionExternalImage Image { get; } =
                new("test", size, CompositionExternalImageFormat.R8G8B8A8UNorm, 0, 0, true);
            public IReadOnlyList<CompositionExternalSemaphore> Semaphores => [];

            public ValueTask<ICompositionExternalHandleLease> LeaseImageHandleAsync(
                CancellationToken cancellationToken) =>
                throw new NotSupportedException("This allocation-free test never imports native handles.");

            public ValueTask<ICompositionExternalHandleLease> LeaseSemaphoreHandleAsync(
                long resourceId, CancellationToken cancellationToken) =>
                throw new NotSupportedException("This allocation-free test never imports native handles.");

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
