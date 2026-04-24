// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using Opc.Ua;
    using Microsoft.Extensions.Options;
    using System.Collections.Generic;

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
        /// Number of subscriptions
        /// </summary>
        int Count { get; }

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
    }
}
