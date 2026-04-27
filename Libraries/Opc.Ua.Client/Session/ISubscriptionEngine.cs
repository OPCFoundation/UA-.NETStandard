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
