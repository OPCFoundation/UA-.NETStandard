// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
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
        /// The position of the notification in the subscription's publish queue
        /// (see <see cref="Subscription.TrackValuePublication"/>), or -1 if not yet tracked.
        /// </summary>
        internal long NotificationOrdinal { get; set; } = -1;

        /// <summary>
        /// The message that holds the notification, once the subscription built it.
        /// </summary>
        internal NotificationMessage? Message
        {
            get => m_message;
            set => m_message = value;
        }

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
        internal void TrackNotification(Queue<MonitoredItemNotification> notifications)
        {
            if (NotificationOrdinal < 0 && !m_completion.Task.IsCompleted)
            {
                m_subscription.TrackValuePublication(this, notifications);
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

        /// <summary>Set under the subscription lock, read by the Publish response path.</summary>
        private volatile NotificationMessage? m_message;
    }
}
