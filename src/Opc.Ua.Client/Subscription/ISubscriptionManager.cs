/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Subscription manager manages all subscription in a session
    /// </summary>
    public interface ISubscriptionManager
    {
        /// <summary>
        /// Return diagnostics mask to use when sending publish requests.
        /// </summary>
        DiagnosticsMasks ReturnDiagnostics { get; set; }

        /// <summary>
        /// Gets and sets the maximum number of publish requests to
        /// be used in the session.
        /// </summary>
        int MaxPublishWorkerCount { get; set; }

        /// <summary>
        /// Gets and sets the minimum number of publish requests to be
        /// used in the session.
        /// </summary>
        int MinPublishWorkerCount { get; set; }

        /// <summary>
        /// Get the number of current publishing workers
        /// </summary>
        int PublishWorkerCount { get; }

        /// <summary>
        /// Good publish requests
        /// </summary>
        int GoodPublishRequestCount { get; }

        /// <summary>
        /// Bad publish requests
        /// </summary>
        int BadPublishRequestCount { get; }

        /// <summary>
        /// Total number of missing notification messages detected across
        /// all subscriptions managed by this manager (sum of
        /// <see cref="ISubscription.MissingMessageCount"/>).
        /// </summary>
        long MissingMessageCount { get; }

        /// <summary>
        /// Total number of republish requests issued across all
        /// subscriptions managed by this manager (sum of
        /// <see cref="ISubscription.RepublishMessageCount"/>).
        /// </summary>
        long RepublishMessageCount { get; }

        /// <summary>
        /// Number of subscriptions
        /// </summary>
        int Count { get; }

        /// <summary>
        /// <para>
        /// When <c>true</c>, the V2 subscription dispatcher calls
        /// <see cref="IPooledEncodeable.Reuse"/> on notification payload
        /// containers (such as <c>DataChangeNotification</c> and
        /// <c>EventNotificationList</c>) after the handler returns, releasing them
        /// back to their activator-level pools for reuse. The
        /// <see cref="System.ReadOnlyMemory{T}"/> of <see cref="DataValueChange"/>
        /// or <see cref="EventNotification"/> values passed to
        /// <see cref="ISubscriptionNotificationHandler"/> is rented as well
        /// and returned at the same point: copy the values a handler keeps
        /// (each struct by value, or the memory with <c>ToArray()</c>)
        /// instead of keeping the memory itself.
        /// </para>
        /// <para>
        /// When <c>false</c> (default), instances are simply released
        /// to the GC as before.
        /// </para>
        /// <para>
        /// Setting this property takes effect on the next publish
        /// dispatch. Handlers that retain notification values past the
        /// dispatch call must not enable this option, or must copy the
        /// retained values before returning from the handler.
        /// </para>
        /// </summary>
        bool PoolNotifications { get; set; }

        /// <summary>
        /// Subscriptions
        /// </summary>
        IEnumerable<ISubscription> Items { get; }

        /// <summary>
        /// Create new subscriptions inside the session.
        /// The subscription will not be created but will
        /// be created asynchronously. Disposing the session
        /// will remove it from the session but not from
        /// the server. The subscription must also explicitly
        /// be deleted using DeleteAsync to remove it from
        /// the server.
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        ISubscription Add(ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options);

        /// <summary>
        /// Snapshot all subscriptions managed by this instance and write
        /// them to <paramref name="stream"/> in OPC UA binary encoding.
        /// The format starts with the session's namespace and server URI
        /// tables so the snapshot is portable across sessions whose
        /// tables index the same URIs in different positions.
        /// </summary>
        /// <param name="stream">The destination stream. Must be writable.</param>
        /// <param name="messageContext">The message context that supplies
        /// the namespace + server URI tables used by the on-wire encoding.
        /// Pass <c>session.MessageContext</c>.</param>
        /// <param name="subscriptions">Optional subset of subscriptions to
        /// snapshot. When <c>null</c> all managed subscriptions are saved.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SaveAsync(
            Stream stream,
            IServiceMessageContext messageContext,
            IEnumerable<ISubscription>? subscriptions = null,
            CancellationToken ct = default);

        /// <summary>
        /// Restore subscriptions from a stream previously produced by
        /// <see cref="SaveAsync"/>. Each restored subscription is added to the
        /// manager via the same path used by <see cref="Add"/>.
        /// </summary>
        /// <param name="stream">The source stream. Must be readable.</param>
        /// <param name="messageContext">Message context that supplies the
        /// active session's namespace + server URI tables. Indexes in the
        /// saved snapshot are remapped onto these tables.</param>
        /// <param name="handlerFactory">Factory invoked once per restored
        /// subscription to construct the application's
        /// <see cref="ISubscriptionNotificationHandler"/>. The factory
        /// receives the per-subscription <c>Name</c> from the snapshot.</param>
        /// <param name="transferSubscriptions">When <c>true</c> the
        /// server-side subscription and monitored item ids are preserved
        /// so the caller can issue a TransferSubscriptions call to take
        /// over the existing server-side state. When <c>false</c> the ids
        /// are cleared so the V2 manager recreates the subscriptions from
        /// scratch on the new session — matching classic
        /// <see cref="Session.Load(Stream, bool, IEnumerable{System.Type})"/>.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<IReadOnlyList<ISubscription>> LoadAsync(Stream stream,
            IServiceMessageContext messageContext,
            System.Func<string, ISubscriptionNotificationHandler> handlerFactory,
            bool transferSubscriptions = false,
            CancellationToken ct = default);
    }
}
