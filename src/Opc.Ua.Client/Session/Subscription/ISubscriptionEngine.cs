// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Defines the subscription engine that manages the publish loop,
    /// subscription lifecycle, and notification dispatch for a session.
    /// Implementations can be swapped at session creation time to use
    /// different publish strategies (e.g., classic fire-and-forget vs.
    /// Channel-based worker pool).
    /// </summary>
    public interface ISubscriptionEngine : IDisposable
    {
        /// <summary>
        /// Start the publish loop. Called after the session is activated
        /// and subscriptions may begin receiving notifications.
        /// </summary>
        /// <param name="timeout">The timeout for publish requests in
        /// milliseconds.</param>
        /// <param name="fullQueue">Whether to fill the publish queue
        /// completely on start.</param>
        void StartPublishing(int timeout, bool fullQueue);

        /// <summary>
        /// Stop the publish loop and wait for outstanding publish
        /// requests to complete or be cancelled. Called before session
        /// close or dispose.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask StopPublishingAsync(CancellationToken ct = default);

        /// <summary>
        /// Pause publishing. Called when the session enters a reconnect
        /// state and publish responses should not be processed.
        /// </summary>
        void PausePublishing();

        /// <summary>
        /// Resume publishing after a pause. Called when the session
        /// reconnect completes and publish responses can be processed
        /// again.
        /// </summary>
        void ResumePublishing();

        /// <summary>
        /// Notify the engine that subscriptions have been added, removed,
        /// or modified, so the publish pipeline depth should be
        /// re-evaluated.
        /// </summary>
        void NotifySubscriptionsChanged();

        /// <summary>
        /// Drive a recreate / transfer pass for all subscriptions managed
        /// by the engine after the underlying session has been
        /// (re-)established. Implementations that maintain their own
        /// subscription bookkeeping (e.g. the V2
        /// <see cref="DefaultSubscriptionEngine"/>) drive
        /// transfer-on-recreate or per-subscription recreate here.
        /// Implementations that do not (e.g. the classic engine, where
        /// the host <see cref="Session"/> drives recreate via subscription
        /// templates) MUST treat this as a no-op.
        /// </summary>
        /// <param name="previousSessionId">
        /// The previous server-assigned <see cref="NodeId"/> of the
        /// session before it was re-created. May be <c>null</c> when the
        /// caller has no prior session id (initial connect). When
        /// non-null, V2 subscriptions configured for transfer-on-recreate
        /// will attempt to transfer their server-side state from the
        /// previous session into the current one.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask RecreateSubscriptionsAsync(
            NodeId? previousSessionId,
            CancellationToken ct = default);

        /// <summary>
        /// The number of currently outstanding publish requests that
        /// have received a successful response.
        /// </summary>
        int GoodPublishRequestCount { get; }

        /// <summary>
        /// The number of publish requests that resulted in an error.
        /// </summary>
        int BadPublishRequestCount { get; }

        /// <summary>
        /// The current number of active publish workers or in-flight
        /// publish requests.
        /// </summary>
        int PublishWorkerCount { get; }

        /// <summary>
        /// Gets or sets the minimum number of publish requests to
        /// keep in the pipeline.
        /// </summary>
        int MinPublishRequestCount { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of publish requests to
        /// keep in the pipeline.
        /// </summary>
        int MaxPublishRequestCount { get; set; }
    }
}
