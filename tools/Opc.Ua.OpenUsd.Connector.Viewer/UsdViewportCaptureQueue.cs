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
using Opc.Ua.Vision.OpenUsd;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    /// <summary>
    /// Owns one pending capture while the renderer retains ownership of any readback already started.
    /// </summary>
    internal sealed class UsdViewportCaptureQueue : IDisposable
    {
        /// <summary>
        /// Queues an admitted request and releases the slot only when its completion has drained.
        /// </summary>
        public async ValueTask<RetainedSceneFrame> CaptureAsync(
            SceneCameraCaptureRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            var capture = new Readback(request, cancellationToken);
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_capture is not null)
                {
                    throw new InvalidOperationException("A viewport readback is already queued.");
                }
                m_capture = capture;
            }
            using CancellationTokenRegistration registration = cancellationToken.Register(capture.CancelIfQueued);
            try
            {
                return await capture.Completion.ConfigureAwait(false);
            }
            finally
            {
                lock (m_state)
                {
                    if (ReferenceEquals(m_capture, capture))
                    {
                        m_capture = null;
                    }
                }
            }
        }

        /// <summary>
        /// Transfers a queued request to the serialized renderer callback.
        /// </summary>
        public Readback? TryStart()
        {
            lock (m_state)
            {
                return m_capture?.TryStart() == true ? m_capture : null;
            }
        }

        /// <summary>
        /// Fails queued work immediately or defers its failure until an active readback finishes.
        /// </summary>
        public void Fail(Exception error)
        {
            ArgumentNullException.ThrowIfNull(error);
            lock (m_state)
            {
                m_capture?.Fail(error);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_state)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_capture?.Fail(new ObjectDisposedException(nameof(UsdViewportCaptureQueue)));
            }
        }

        /// <summary>
        /// Carries one capture through queueing, readback and terminal completion.
        /// </summary>
        internal sealed class Readback(SceneCameraCaptureRequest request, CancellationToken token)
        {
            /// <summary>
            /// Gets the request admitted by the renderer.
            /// </summary>
            public SceneCameraCaptureRequest Request { get; } = request;

            /// <summary>
            /// Gets the caller's cancellation token without abandoning an active readback.
            /// </summary>
            public CancellationToken Token { get; } = token;

            /// <summary>
            /// Gets the terminal capture outcome.
            /// </summary>
            public Task<RetainedSceneFrame> Completion => m_completion.Task;

            /// <summary>
            /// Claims the request exactly once while it is still queued.
            /// </summary>
            public bool TryStart()
            {
                lock (m_state)
                {
                    if (m_started || m_completion.Task.IsCompleted)
                    {
                        return false;
                    }
                    m_started = true;
                    return true;
                }
            }

            /// <summary>
            /// Cancels a queued capture, leaving an active readback to drain normally.
            /// </summary>
            public void CancelIfQueued()
            {
                lock (m_state)
                {
                    if (!m_started)
                    {
                        m_completion.TrySetCanceled(Token);
                    }
                }
            }

            /// <summary>
            /// Records the first failure without completing a readback that still owns native resources.
            /// </summary>
            public void Fail(Exception error)
            {
                lock (m_state)
                {
                    m_failure ??= error;
                    if (!m_started)
                    {
                        m_completion.TrySetException(m_failure);
                    }
                }
            }

            /// <summary>
            /// Completes a drained readback, giving pending failure or cancellation precedence over pixels.
            /// </summary>
            public void Complete(RetainedSceneFrame frame)
            {
                lock (m_state)
                {
                    if (m_failure is not null)
                    {
                        m_completion.TrySetException(m_failure);
                    }
                    else if (Token.IsCancellationRequested)
                    {
                        m_completion.TrySetCanceled(Token);
                    }
                    else
                    {
                        m_completion.TrySetResult(frame);
                    }
                }
            }

            /// <summary>
            /// Completes a drained readback with its error or a previously reported presenter failure.
            /// </summary>
            public void Complete(Exception error)
            {
                lock (m_state)
                {
                    if (m_failure is not null)
                    {
                        m_completion.TrySetException(m_failure);
                    }
                    else if (error is OperationCanceledException)
                    {
                        m_completion.TrySetCanceled(Token);
                    }
                    else
                    {
                        m_completion.TrySetException(error);
                    }
                }
            }

            private readonly Lock m_state = new();

            private readonly TaskCompletionSource<RetainedSceneFrame> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private Exception? m_failure;
            private bool m_started;
        }

        private readonly Lock m_state = new();
        private Readback? m_capture;
        private bool m_disposed;
    }
}
