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
 *
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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Optional acknowledgement lifecycle notifications implemented by SDK sessions.
    /// </summary>
    /// <remarks>
    /// Handlers can run concurrently. They must be short, must not modify the
    /// acknowledgement objects and must not block on session or subscription work.
    /// Existing implementations of <see cref="ISession"/> need not implement this capability.
    /// </remarks>
    public interface ISubscriptionAcknowledgementEvents
    {
        /// <summary>
        /// Raised for the acknowledgement results of a successfully decoded Publish response.
        /// </summary>
        event EventHandler<PublishAcknowledgementsCompletedEventArgs>? PublishAcknowledgementsCompleted;

        /// <summary>
        /// Raised when a subscription identifier or the session acknowledgement state is retired.
        /// </summary>
        event EventHandler<SubscriptionAcknowledgementsDiscardedEventArgs>? SubscriptionAcknowledgementsDiscarded;
    }

    /// <summary>
    /// Acknowledgements and their corresponding server results.
    /// </summary>
    /// <remarks>
    /// Entries preserve the acknowledgement object identities presented by the deferral
    /// callback. Consumers can correlate an approval with its result even if the same
    /// numeric subscription and sequence identifiers have since been reused.
    /// A failed service request does not raise this notification.
    /// </remarks>
    public sealed class PublishAcknowledgementsCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates an acknowledgement result notification.
        /// </summary>
        public PublishAcknowledgementsCompletedEventArgs(
            ArrayOf<SubscriptionAcknowledgement> acknowledgements,
            ArrayOf<StatusCode> results)
        {
            if (acknowledgements.Count != results.Count)
            {
                throw new ArgumentException("Each acknowledgement requires one result.", nameof(results));
            }
            Acknowledgements = acknowledgements;
            Results = results;
        }

        /// <summary>
        /// Gets the acknowledgement objects from the completed request.
        /// </summary>
        public ArrayOf<SubscriptionAcknowledgement> Acknowledgements { get; }

        /// <summary>
        /// Gets the server result for each acknowledgement, in request order.
        /// </summary>
        public ArrayOf<StatusCode> Results { get; }
    }

    /// <summary>
    /// Identifies acknowledgement state no longer owned by the session.
    /// </summary>
    public sealed class SubscriptionAcknowledgementsDiscardedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a retirement notification.
        /// </summary>
        /// <param name="subscriptionId">The retired server id, or zero for all subscription state.</param>
        public SubscriptionAcknowledgementsDiscardedEventArgs(uint subscriptionId)
        {
            SubscriptionId = subscriptionId;
        }

        /// <summary>
        /// Gets the retired server id, or zero when all subscription state is retired.
        /// </summary>
        public uint SubscriptionId { get; }
    }

    /// <summary>
    /// Optional bridge from an engine's retirement path to its owning session.
    /// </summary>
    internal interface ISubscriptionAcknowledgementLifecycle
    {
        /// <summary>
        /// Reports retired acknowledgement state.
        /// </summary>
        void OnAcknowledgementsDiscarded(uint subscriptionId);
    }

    public partial class Session : ISubscriptionAcknowledgementEvents
    {
        /// <inheritdoc/>
        public event EventHandler<PublishAcknowledgementsCompletedEventArgs>? PublishAcknowledgementsCompleted;

        /// <inheritdoc/>
        public event EventHandler<SubscriptionAcknowledgementsDiscardedEventArgs>? SubscriptionAcknowledgementsDiscarded;

        /// <summary>
        /// Reports a validated acknowledgement response without affecting Publish completion.
        /// </summary>
        private void OnAcknowledgementServiceCompleted(IServiceRequest request, IServiceResponse? response)
        {
            if (request is CreateSubscriptionRequest &&
                response is CreateSubscriptionResponse created &&
                created.SubscriptionId != 0 &&
                created.ResponseHeader != null &&
                StatusCode.IsGood(created.ResponseHeader.ServiceResult))
            {
                // A server can reuse an expired subscription id before the old client object is retired.
                OnAcknowledgementsDiscarded(created.SubscriptionId);
                return;
            }
            if (PublishAcknowledgementsCompleted == null ||
                request is not PublishRequest publish ||
                publish.SubscriptionAcknowledgements.Count == 0 ||
                response is not PublishResponse result ||
                result.ResponseHeader == null ||
                !StatusCode.IsGood(result.ResponseHeader.ServiceResult))
            {
                return;
            }
            if (result.Results.Count != publish.SubscriptionAcknowledgements.Count)
            {
                m_logger.AcknowledgementResultCountMismatch();
                return;
            }
            RaiseAcknowledgementEvent(PublishAcknowledgementsCompleted,
                new PublishAcknowledgementsCompletedEventArgs(
                    publish.SubscriptionAcknowledgements, result.Results),
                this, m_logger);
        }

        /// <summary>
        /// Reports retirement after the owning lifecycle has stopped using the old identifier.
        /// </summary>
        internal void OnAcknowledgementsDiscarded(uint subscriptionId)
        {
            RaiseAcknowledgementEvent(SubscriptionAcknowledgementsDiscarded,
                new SubscriptionAcknowledgementsDiscardedEventArgs(subscriptionId),
                this, m_logger);
        }

        /// <summary>
        /// Isolates subscriber failures so observers cannot change protocol outcomes.
        /// </summary>
        /// <typeparam name="T">The acknowledgement notification arguments.</typeparam>
        internal static void RaiseAcknowledgementEvent<T>(
            EventHandler<T>? handlers, T args, object sender, ILogger logger)
            where T : EventArgs
        {
            if (handlers == null)
            {
                return;
            }
            foreach (EventHandler<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(sender, args);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    logger.AcknowledgementObserverFailed(exception);
                }
            }
        }

        /// <summary>
        /// Starts a session identity replacement during which existing acknowledgement
        /// state stays valid because the subscriptions will be transferred. Every call
        /// must be paired with <see cref="EndRetainingAcknowledgements"/>.
        /// </summary>
        internal void BeginRetainingAcknowledgements()
        {
            Interlocked.Increment(ref m_retainAcknowledgementsAcrossSessionChange);
        }

        /// <summary>
        /// Ends a replacement started with <see cref="BeginRetainingAcknowledgements"/>.
        /// </summary>
        internal void EndRetainingAcknowledgements()
        {
            Interlocked.Decrement(ref m_retainAcknowledgementsAcrossSessionChange);
        }

        /// <summary>
        /// Whether a session recreate will try to transfer the existing subscriptions,
        /// which keeps their acknowledgement state valid across the session change.
        /// </summary>
        internal bool WillTransferSubscriptionsOnRecreate()
        {
            return m_engine is DefaultSubscriptionEngine
            {
                SubscriptionManager: Subscriptions.SubscriptionManager manager
            }
                ? manager.TransferSubscriptionsOnRecreate
                : TransferSubscriptionsOnReconnect;
        }

        /// <summary>
        /// Non-zero while a recreate that transfers subscriptions replaces the session id.
        /// </summary>
        private int m_retainAcknowledgementsAcrossSessionChange;
    }

    /// <summary>
    /// Acknowledgement observation diagnostics.
    /// </summary>
    internal static partial class SessionLog
    {
        [LoggerMessage(EventId = ClientEventIds.Session + 72, Level = LogLevel.Error,
            Message = "Session: Acknowledgement observer failed.")]
        public static partial void AcknowledgementObserverFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = ClientEventIds.Session + 73, Level = LogLevel.Warning,
            Message = "Session: Publish acknowledgement result count does not match its request.")]
        public static partial void AcknowledgementResultCountMismatch(this ILogger logger);
    }
}
