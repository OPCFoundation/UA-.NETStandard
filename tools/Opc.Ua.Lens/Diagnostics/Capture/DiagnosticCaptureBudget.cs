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

namespace UaLens.Diagnostics.Capture
{
    /// <summary>
    /// Enforces exact byte and frame admission before a shared capture source copies any payload.
    /// </summary>
    internal sealed class DiagnosticCaptureBudget
    {
        /// <summary>
        /// Validates the fixed budget and starts its monotonic admission deadline.
        /// </summary>
        public DiagnosticCaptureBudget(DiagnosticCaptureLimits limits, TimeProvider? clock = null)
        {
            ArgumentNullException.ThrowIfNull(limits);
            limits.RequireValid();
            m_limits = limits;
            m_clock = clock ?? TimeProvider.System;
            m_startedAt = m_clock.GetTimestamp();
        }

        /// <summary>
        /// Completes when the recording reaches its hard frame or payload bound.
        /// </summary>
        public Task Reached => m_reached.Task;

        /// <summary>
        /// Returns consistent accepted-frame and payload counts.
        /// </summary>
        public (long Frames, long PayloadBytes) Snapshot()
        {
            lock (m_gate)
            {
                return (m_frames, m_bytes);
            }
        }

        /// <summary>
        /// Admits a complete frame without ever exceeding either bound or truncating its payload.
        /// </summary>
        public bool TryAccept(int bytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            lock (m_gate)
            {
                if (m_stopped)
                {
                    return false;
                }
                if (m_frames >= m_limits.MaxFrames ||
                    bytes > m_limits.MaxPayloadBytes - m_bytes ||
                    m_clock.GetElapsedTime(m_startedAt, m_clock.GetTimestamp()).TotalSeconds >=
                        m_limits.DurationSeconds)
                {
                    m_stopped = true;
                    m_reached.TrySetResult();
                    return false;
                }
                m_frames++;
                m_bytes += bytes;
                if (m_frames == m_limits.MaxFrames || m_bytes == m_limits.MaxPayloadBytes)
                {
                    m_stopped = true;
                    m_reached.TrySetResult();
                }
                return true;
            }
        }

        /// <summary>
        /// Stops admission before the underlying source is drained.
        /// </summary>
        public void Stop()
        {
            lock (m_gate)
            {
                m_stopped = true;
            }
        }

        private readonly Lock m_gate = new();
        private readonly DiagnosticCaptureLimits m_limits;
        private readonly TimeProvider m_clock;
        private readonly long m_startedAt;
        private readonly TaskCompletionSource m_reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long m_frames;
        private long m_bytes;
        private bool m_stopped;
    }
}
