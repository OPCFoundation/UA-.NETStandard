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
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// A failed service request does not raise this notification. Acknowledgements the
    /// client discards without sending, because the server no longer holds their message,
    /// are reported with <c>Bad_SequenceNumberUnknown</c>.
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
    /// <remarks>
    /// Every subscription identifier the server assigns gets a new, higher
    /// <see cref="Generation"/>; see
    /// <see cref="PublishSequenceNumbersToAcknowledgeEventArgs.GetSubscriptionGeneration"/>.
    /// A notification retires all acknowledgement state for
    /// <see cref="SubscriptionId"/> (or for every subscription when it is zero) whose
    /// generation is less than or equal to <see cref="Generation"/>. State of a later
    /// generation is unaffected, so the notification stays correct even when it is
    /// observed after the server has reused the identifier.
    /// </remarks>
    public sealed class SubscriptionAcknowledgementsDiscardedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a retirement notification for every generation.
        /// </summary>
        /// <param name="subscriptionId">The retired server id, or zero for all subscription state.</param>
        public SubscriptionAcknowledgementsDiscardedEventArgs(uint subscriptionId)
            : this(subscriptionId, long.MaxValue)
        {
        }

        /// <summary>
        /// Creates a retirement notification up to and including a generation.
        /// </summary>
        /// <param name="subscriptionId">The retired server id, or zero for all subscription state.</param>
        /// <param name="generation">The highest retired generation.</param>
        public SubscriptionAcknowledgementsDiscardedEventArgs(uint subscriptionId, long generation)
        {
            SubscriptionId = subscriptionId;
            Generation = generation;
        }

        /// <summary>
        /// Gets the retired server id, or zero when all subscription state is retired.
        /// </summary>
        public uint SubscriptionId { get; }

        /// <summary>
        /// Gets the highest retired generation. State of a higher generation is still valid.
        /// </summary>
        public long Generation { get; }
    }

    /// <summary>
    /// Optional bridge from an engine's retirement path to its owning session.
    /// </summary>
    internal interface ISubscriptionAcknowledgementLifecycle
    {
        /// <summary>
        /// Gets the generation the session assigned to a subscription identifier, or
        /// zero if the identifier was not assigned by a creation in this session.
        /// </summary>
        long GetSubscriptionGeneration(uint subscriptionId);

        /// <summary>
        /// Reports retired acknowledgement state up to and including a generation.
        /// </summary>
        void OnAcknowledgementsDiscarded(uint subscriptionId, long generation);

        /// <summary>
        /// Reports acknowledgements the client discarded because the server no longer holds
        /// their message, as if the server had answered them with <c>Bad_SequenceNumberUnknown</c>.
        /// </summary>
        void OnAcknowledgementsAbandoned(ArrayOf<SubscriptionAcknowledgement> acknowledgements);
    }

    /// <summary>
    /// Optional engine-context capability that applies the session's acknowledgement
    /// deferral policy with subscription generations. Kept internal so the public
    /// <see cref="ISubscriptionEngineContext"/> contract does not change.
    /// </summary>
    internal interface ISubscriptionAcknowledgementPolicy
    {
        /// <summary>
        /// Applies the deferral policy, exposing <paramref name="generations"/> to the handler.
        /// </summary>
        /// <param name="currentAcknowledgements">The acknowledgements eligible for the next request.</param>
        /// <param name="generations">Resolves the subscription generation of a presented
        /// acknowledgement.</param>
        /// <returns>The acknowledgements to send and those to retain.</returns>
        (List<SubscriptionAcknowledgement> toSend, List<SubscriptionAcknowledgement> updatedPending)
            PrepareAcknowledgementsToSend(
                List<SubscriptionAcknowledgement> currentAcknowledgements,
                Func<SubscriptionAcknowledgement, long> generations);
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
                // A server can reuse an expired subscription id before the old client object
                // is retired, so every assignment starts a new generation and retires the older ones.
                long generation = AssignSubscriptionGeneration(created.SubscriptionId);
                OnAcknowledgementsDiscarded(created.SubscriptionId, generation - 1);
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
        /// Reports acknowledgements the client discarded because the server no longer holds
        /// their message, with the result the server would have returned for them.
        /// </summary>
        internal void OnAcknowledgementsAbandoned(ArrayOf<SubscriptionAcknowledgement> acknowledgements)
        {
            if (PublishAcknowledgementsCompleted == null || acknowledgements.Count == 0)
            {
                return;
            }
            var results = new StatusCode[acknowledgements.Count];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = StatusCodes.BadSequenceNumberUnknown;
            }
            RaiseAcknowledgementEvent(PublishAcknowledgementsCompleted,
                new PublishAcknowledgementsCompletedEventArgs(acknowledgements, results),
                this, m_logger);
        }

        /// <summary>
        /// Gets the generation assigned to a subscription identifier by the latest successful
        /// creation in this session, or zero if there was none or it has been retired. For
        /// identifier zero, gets the latest generation assigned to any identifier.
        /// </summary>
        internal long GetSubscriptionGeneration(uint subscriptionId)
        {
            lock (m_subscriptionGenerationLock)
            {
                if (subscriptionId == 0)
                {
                    return m_subscriptionGenerationEpoch;
                }
                return m_subscriptionGenerations.TryGetValue(subscriptionId, out long generation)
                    ? generation
                    : 0;
            }
        }

        /// <summary>
        /// Reports retirement of the current generation of an identifier (or of all state
        /// when <paramref name="subscriptionId"/> is zero) after the owner stopped using it.
        /// </summary>
        internal void OnAcknowledgementsDiscarded(uint subscriptionId)
        {
            OnAcknowledgementsDiscarded(subscriptionId, GetSubscriptionGeneration(subscriptionId));
        }

        /// <summary>
        /// Reports retirement up to and including <paramref name="generation"/>. When the
        /// session lock is held by the caller, the notification is raised once the lock is
        /// released, so observers never run inside it.
        /// </summary>
        internal void OnAcknowledgementsDiscarded(uint subscriptionId, long generation)
        {
            lock (m_subscriptionGenerationLock)
            {
                if (subscriptionId == 0)
                {
                    m_subscriptionGenerations.Clear();
                }
                else if (m_subscriptionGenerations.TryGetValue(subscriptionId, out long current) &&
                    current <= generation)
                {
                    m_subscriptionGenerations.Remove(subscriptionId);
                }
            }
            m_pendingAcknowledgementDiscards.Enqueue(
                new SubscriptionAcknowledgementsDiscardedEventArgs(subscriptionId, generation));
            RaisePendingLifecycleEvents();
        }

        /// <summary>
        /// Raises acknowledgement retirement and session generation notifications queued while
        /// the session lock was held, in order. Does nothing while the current thread holds the
        /// session lock.
        /// </summary>
        private void RaisePendingLifecycleEvents()
        {
            if (m_lock.IsHeldByCurrentThread)
            {
                return;
            }
            while (m_pendingAcknowledgementDiscards.TryDequeue(
                out SubscriptionAcknowledgementsDiscardedEventArgs? args))
            {
                RaiseAcknowledgementEvent(SubscriptionAcknowledgementsDiscarded, args, this, m_logger);
            }
            RaisePendingSessionGenerationChanges();
        }

        /// <summary>
        /// Assigns a new generation to an identifier returned by CreateSubscription.
        /// </summary>
        private long AssignSubscriptionGeneration(uint subscriptionId)
        {
            lock (m_subscriptionGenerationLock)
            {
                long generation = ++m_subscriptionGenerationEpoch;
                m_subscriptionGenerations[subscriptionId] = generation;
                return generation;
            }
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
        private readonly Lock m_subscriptionGenerationLock = new();
        private readonly Dictionary<uint, long> m_subscriptionGenerations = [];
        private long m_subscriptionGenerationEpoch;
        private readonly ConcurrentQueue<SubscriptionAcknowledgementsDiscardedEventArgs>
            m_pendingAcknowledgementDiscards = new();
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
