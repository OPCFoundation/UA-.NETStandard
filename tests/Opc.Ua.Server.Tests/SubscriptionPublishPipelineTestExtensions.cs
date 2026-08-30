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

#nullable enable

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Forwards the publish-pipeline protocol members to <see cref="ISubscriptionPublishPipeline"/>
    /// so tests can keep driving them with the original member syntax on a subscription reference.
    /// The pipeline members are explicit interface implementations on <see cref="Subscription"/>
    /// and no longer part of <see cref="ISubscription"/>.
    /// </summary>
    /// <remarks>
    /// These extensions reuse the original member names. If any of these names is ever re-added
    /// to <see cref="ISubscription"/>, the instance member silently wins over the extension -
    /// which is the correct binding, but worth knowing when reading test failures.
    /// </remarks>
    internal static class SubscriptionPublishPipelineTestExtensions
    {
        public static ISubscriptionPublishPipeline AsPipeline(this ISubscription subscription)
        {
            return (ISubscriptionPublishPipeline)subscription;
        }

        public static PublishingState PublishTimerExpired(this ISubscription subscription)
        {
            return subscription.AsPipeline().PublishTimerExpired();
        }

        public static ServiceResult? Acknowledge(
            this ISubscription subscription,
            OperationContext context,
            uint sequenceNumber)
        {
            return subscription.AsPipeline().Acknowledge(context, sequenceNumber);
        }

        public static NotificationMessage PublishTimeout(this ISubscription subscription)
        {
            return subscription.AsPipeline().PublishTimeout();
        }

        public static NotificationMessage SubscriptionTransferred(this ISubscription subscription)
        {
            return subscription.AsPipeline().SubscriptionTransferred();
        }

        public static ArrayOf<uint> AvailableSequenceNumbersForRetransmission(
            this ISubscription subscription)
        {
            return subscription.AsPipeline().AvailableSequenceNumbersForRetransmission();
        }

        public static void QueueOverflowHandler(this ISubscription subscription)
        {
            subscription.AsPipeline().QueueOverflowHandler();
        }

        public static bool SessionClosed(this ISubscription subscription, ISession closingSession)
        {
            return subscription.AsPipeline().SessionClosed(closingSession);
        }

        public static NotificationMessage? Publish(
            this ISubscription subscription,
            OperationContext context,
            out ArrayOf<uint> availableSequenceNumbers,
            out bool moreNotifications)
        {
            return subscription.AsPipeline().Publish(
                context,
                out availableSequenceNumbers,
                out moreNotifications);
        }
    }
}
