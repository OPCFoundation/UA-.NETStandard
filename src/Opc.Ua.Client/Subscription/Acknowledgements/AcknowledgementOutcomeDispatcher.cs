/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Delivers acknowledgement outcomes to an observer in order, from a single background
    /// loop, so that the observer never runs under an SDK lock or blocks a Publish request.
    /// </summary>
    internal sealed class AcknowledgementOutcomeDispatcher
    {
        /// <summary>
        /// Creates a dispatcher.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public AcknowledgementOutcomeDispatcher(ILogger logger)
        {
            m_logger = logger;
        }

        /// <summary>
        /// Queues outcomes for delivery to the observer.
        /// </summary>
        /// <param name="observer">The observer, or <c>null</c> to discard the outcomes.</param>
        /// <param name="outcomes">The outcomes, or <c>null</c>.</param>
        public void Report(
            ISubscriptionAcknowledgementObserver? observer,
            List<AcknowledgementOutcome>? outcomes)
        {
            if (observer == null || outcomes == null || outcomes.Count == 0)
            {
                return;
            }
            lock (m_lock)
            {
                m_loop ??= DeliverAsync(observer);
            }
            if (!m_outcomes.Writer.TryWrite([.. outcomes]))
            {
                m_logger.AcknowledgementOutcomesDropped(outcomes.Count);
            }
        }

        /// <summary>
        /// Delivers the outcomes that are still queued and stops the delivery loop.
        /// </summary>
        public async ValueTask CompleteAsync()
        {
            m_outcomes.Writer.TryComplete();
            Task? loop;
            lock (m_lock)
            {
                loop = m_loop;
            }
            if (loop == null)
            {
                return;
            }
            try
            {
                await loop.WaitAsync(kCompletionTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                m_logger.AcknowledgementOutcomeDeliveryTimedOut();
            }
        }

        /// <summary>
        /// Delivers outcomes until the dispatcher is completed.
        /// </summary>
        private async Task DeliverAsync(ISubscriptionAcknowledgementObserver observer)
        {
            await Task.Yield();
            while (await m_outcomes.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (m_outcomes.Reader.TryRead(out AcknowledgementOutcome[]? outcomes))
                {
                    try
                    {
                        await observer.OnCompletedAsync(outcomes).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.AcknowledgementObserverFailed(ex, outcomes.Length);
                    }
                }
            }
        }

        private static readonly TimeSpan kCompletionTimeout = TimeSpan.FromSeconds(5);
        private readonly ILogger m_logger;
        private readonly System.Threading.Lock m_lock = new();

        private readonly Channel<AcknowledgementOutcome[]> m_outcomes =
            Channel.CreateUnbounded<AcknowledgementOutcome[]>(
                new UnboundedChannelOptions { SingleReader = true });

        private Task? m_loop;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="AcknowledgementOutcomeDispatcher"/>.
    /// </summary>
    internal static partial class AcknowledgementOutcomeDispatcherLog
    {
        [LoggerMessage(EventId = ClientEventIds.SubscriptionManagerContinued + 2, Level = LogLevel.Warning,
            Message = "The acknowledgement observer failed for {Count} outcomes.")]
        public static partial void AcknowledgementObserverFailed(
            this ILogger logger,
            Exception exception,
            int count);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManagerContinued + 3, Level = LogLevel.Warning,
            Message = "{Count} acknowledgement outcomes were not delivered because the session or " +
                "subscription manager is disposed.")]
        public static partial void AcknowledgementOutcomesDropped(
            this ILogger logger,
            int count);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManagerContinued + 4, Level = LogLevel.Warning,
            Message = "Delivering the remaining acknowledgement outcomes timed out during disposal.")]
        public static partial void AcknowledgementOutcomeDeliveryTimedOut(
            this ILogger logger);
    }
}
