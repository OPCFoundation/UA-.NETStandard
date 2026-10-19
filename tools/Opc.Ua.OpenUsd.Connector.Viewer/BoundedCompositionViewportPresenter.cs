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
using OpenUsd.Rendering;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    /// <summary>
    /// Enforces physical allocation bounds before handing a generation to a native presenter.
    /// Serializes frame preparation, rendering and disposal without owning the desktop dispatcher.
    /// </summary>
    internal sealed class BoundedCompositionViewportPresenter : ICompositionViewportPresenter
    {
        /// <summary>
        /// Wraps a typed native presenter and its retained-renderer callbacks.
        /// </summary>
        public BoundedCompositionViewportPresenter(
            ICompositionViewportPresenter inner,
            UsdViewportLimits limits,
            Func<ViewportDimensions, CancellationToken, ValueTask> prepare,
            Action disposeRenderer,
            Action<Exception> failed,
            Func<(int Generations, int Frames)>? nativeStatistics = null,
            UsdViewportCaptureQueue? captures = null)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            m_limits = limits ?? throw new ArgumentNullException(nameof(limits));
            m_prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
            m_disposeRenderer = disposeRenderer ?? throw new ArgumentNullException(nameof(disposeRenderer));
            m_failed = failed ?? throw new ArgumentNullException(nameof(failed));
            m_captures = captures ?? new UsdViewportCaptureQueue();
            m_nativeStatistics = nativeStatistics;
            limits.Validate();
        }

        /// <summary>
        /// Gets the current number of retained resize generations.
        /// </summary>
        public int ActiveGenerations
        {
            get
            {
                int tracked;
                lock (m_state)
                {
                    tracked = m_generations.Count;
                }
                return Math.Max(tracked, m_nativeStatistics?.Invoke().Generations ?? 0);
            }
        }

        /// <summary>
        /// Gets the current number of allocated ring frames.
        /// </summary>
        public int ActiveFrames
        {
            get
            {
                int tracked;
                lock (m_state)
                {
                    tracked = m_frames;
                }
                return Math.Max(tracked, m_nativeStatistics?.Invoke().Frames ?? 0);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CompositionPresenterProbeResult> ProbeAsync(
            CompositionPresentationTarget target,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(m_stopping.IsCancellationRequested, this);
            CompositionPresenterProbeResult probe;
            try
            {
                probe = await m_inner.ProbeAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                ReportFailure(error);
                throw;
            }
            if (!probe.IsAvailable)
            {
                ReportFailure(new PlatformNotSupportedException(probe.Status));
            }
            return probe;
        }

        /// <inheritdoc/>
        public async ValueTask<ICompositionPresentationGeneration> CreateGenerationAsync(
            ViewportDimensions size,
            int frameCount,
            CancellationToken cancellationToken)
        {
            int bytes = m_limits.RequireFrame(size);
            if (frameCount is < 2 or > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }
            long reserved = (long)bytes * 2 * frameCount;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                m_stopping.Token);
            await m_operations.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                lock (m_state)
                {
                    if (m_generations.Count >= 2 || m_allocatedBytes + reserved > m_limits.MaximumPresentationBytes)
                    {
                        throw new InvalidOperationException("The viewport resize-generation budget is exhausted.");
                    }
                }
                ICompositionPresentationGeneration generation = await m_inner.CreateGenerationAsync(
                    size,
                    frameCount,
                    linked.Token).ConfigureAwait(false);
                var owned = new Generation(this, generation, reserved);
                lock (m_state)
                {
                    m_generations.Add(owned);
                    m_allocatedBytes += reserved;
                    m_frames += generation.Frames.Count;
                }
                if (generation.Size != size || generation.Frames.Count != frameCount)
                {
                    await owned.DisposeAsync().ConfigureAwait(false);
                    throw new InvalidOperationException("The native presenter returned a different ring allocation.");
                }
                return owned;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                ReportFailure(error);
                throw;
            }
            finally
            {
                m_operations.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CompositionFrameRenderResult> RenderAsync(
            ICompositionPresentationFrame frame,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(frame);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                m_stopping.Token);
            await m_operations.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                _ = m_limits.RequireFrame(frame.Image.Size);
                await m_prepare(frame.Image.Size, linked.Token).ConfigureAwait(false);
                CompositionFrameRenderResult result = await m_inner.RenderAsync(frame, linked.Token)
                    .ConfigureAwait(false);
                if (result.Status == CompositionFrameRenderStatus.DeviceLost)
                {
                    ReportFailure(new InvalidOperationException("The embedded graphics device was lost."));
                }
                // A retained viewport must also observe later stage edits and queued captures.
                return result.Status == CompositionFrameRenderStatus.Presented
                    ? result with { ContinueRendering = true }
                    : result;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                ReportFailure(error);
                throw;
            }
            finally
            {
                m_operations.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_state)
            {
                return new ValueTask(m_disposal ??= DisposeCoreAsync());
            }
        }

        private async Task DisposeCoreAsync()
        {
            await m_stopping.CancelAsync().ConfigureAwait(false);
            await m_operations.WaitAsync().ConfigureAwait(false);
            try
            {
                Generation[] generations;
                lock (m_state)
                {
                    generations = [.. m_generations];
                }
                try
                {
                    foreach (Generation generation in generations)
                    {
                        await generation.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await m_inner.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_captures.Dispose();
                m_disposeRenderer();
                m_operations.Release();
                m_operations.Dispose();
                m_stopping.Dispose();
            }
        }

        private void ReportFailure(Exception error)
        {
            m_captures.Fail(error);
            m_failed(error);
        }

        private void Released(Generation generation, long bytes, int frames)
        {
            lock (m_state)
            {
                if (m_generations.Remove(generation))
                {
                    m_allocatedBytes -= bytes;
                    m_frames -= frames;
                }
            }
        }

        private readonly ICompositionViewportPresenter m_inner;
        private readonly UsdViewportLimits m_limits;
        private readonly Func<ViewportDimensions, CancellationToken, ValueTask> m_prepare;
        private readonly Action m_disposeRenderer;
        private readonly Action<Exception> m_failed;
        private readonly UsdViewportCaptureQueue m_captures;
        private readonly Func<(int Generations, int Frames)>? m_nativeStatistics;
        private readonly SemaphoreSlim m_operations = new(1, 1);
        private readonly CancellationTokenSource m_stopping = new();
        private readonly Lock m_state = new();
        private readonly HashSet<Generation> m_generations = [];
        private Task? m_disposal;
        private long m_allocatedBytes;
        private int m_frames;

        private sealed class Generation(
            BoundedCompositionViewportPresenter owner,
            ICompositionPresentationGeneration inner,
            long bytes) : ICompositionPresentationGeneration
        {
            public ViewportDimensions Size => inner.Size;

            public IReadOnlyList<ICompositionPresentationFrame> Frames => inner.Frames;

            public ValueTask DisposeAsync()
            {
                lock (m_state)
                {
                    return new ValueTask(m_disposal ??= DisposeCoreAsync());
                }
            }

            private async Task DisposeCoreAsync()
            {
                try
                {
                    await inner.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    owner.Released(this, bytes, m_frameCount);
                }
            }

            private readonly Lock m_state = new();
            private readonly int m_frameCount = inner.Frames.Count;
            private Task? m_disposal;
        }
    }
}
