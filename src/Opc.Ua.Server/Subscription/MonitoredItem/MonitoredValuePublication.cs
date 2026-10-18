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
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Tracks one final notification from its monitored queue into a real Publish response.
    /// </summary>
    internal sealed class MonitoredValuePublication
    {
        /// <summary>
        /// Creates a registration owned by a subscription.
        /// </summary>
        internal MonitoredValuePublication(Subscription subscription)
        {
            m_subscription = subscription;
        }

        /// <summary>
        /// Gets the asynchronous completion, which distinguishes publication from a released obligation.
        /// </summary>
        internal Task<bool> Completion => m_completion.Task;

        /// <summary>
        /// Gets the exact notification instance retained by the subscription.
        /// </summary>
        internal MonitoredItemNotification? Notification { get; private set; }

        /// <summary>
        /// Releases the subscription's references on every exit from the bounded wait.
        /// </summary>
        internal void Unregister()
        {
            m_subscription.UnregisterValuePublication(this);
        }

        /// <summary>
        /// Rejects unbounded or unsupported timer durations.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is not positive and finite.</exception>
        internal static void ValidateTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "A positive, finite timeout is required.");
            }
        }

        /// <summary>
        /// Associates the exact notification without treating item dequeue as publication.
        /// </summary>
        internal void TrackNotification(MonitoredItemNotification notification)
        {
            if (Notification == null && !m_completion.Task.IsCompleted)
            {
                Notification = notification;
                m_subscription.TrackValuePublication(this);
            }
        }

        /// <summary>
        /// Completes the observation without running continuations on the Publish or teardown thread.
        /// </summary>
        internal void Complete(bool published)
        {
            m_completion.TrySetResult(published);
        }

        /// <summary>
        /// Fails an observation whose source, monitoring configuration, or session ownership changed.
        /// </summary>
        internal void Fail(StatusCode statusCode)
        {
            m_completion.TrySetException(new ServiceResultException(
                statusCode, "The final observation's source, monitoring configuration, or session ownership changed."));
        }

        /// <summary>The subscription whose Publish responses may complete this observation.</summary>
        private readonly Subscription m_subscription;

        /// <summary>The asynchronous signal for the observation.</summary>
        private readonly TaskCompletionSource<bool> m_completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
