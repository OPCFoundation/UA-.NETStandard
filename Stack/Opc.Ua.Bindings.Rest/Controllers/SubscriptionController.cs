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

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Opc.Ua.Bindings.Rest.Controllers
{
    /// <summary>
    /// Routes the OPC UA Subscription service set (Part 4 §5.14) —
    /// CreateSubscription, ModifySubscription, SetPublishingMode,
    /// Publish, Republish, TransferSubscriptions, DeleteSubscriptions —
    /// as REST endpoints per the OpenAPI mapping (Part 6 §G.3).
    /// </summary>
    /// <remarks>
    /// <see cref="PublishAsync(CancellationToken)"/> is a server-side
    /// long-poll: the dispatcher awaits a pending NotificationMessage
    /// (or the request's <see cref="RequestHeader.TimeoutHint"/>)
    /// without holding any thread synchronously. Kestrel's
    /// <c>KeepAliveTimeout</c> and <c>RequestBodyTimeout</c> must be
    /// configured to exceed the largest expected
    /// <see cref="RequestHeader.TimeoutHint"/>; see
    /// <c>Docs/RestApi.md</c> for the recommended values.
    /// </remarks>
    [ApiController]
    public sealed class SubscriptionController : RestApiControllerBase
    {
        /// <inheritdoc/>
        public SubscriptionController(IRestApiServer server, ILoggerFactory loggerFactory)
            : base(server, loggerFactory)
        {
        }

        /// <summary>
        /// Creates a new subscription
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.2"/>).
        /// </summary>
        [HttpPost("/createsubscription")]
        public Task CreateSubscriptionAsync(CancellationToken ct)
            => ExecuteAsync<CreateSubscriptionRequest, CreateSubscriptionResponse>(ct);

        /// <summary>
        /// Modifies an existing subscription
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.3"/>).
        /// </summary>
        [HttpPost("/modifysubscription")]
        public Task ModifySubscriptionAsync(CancellationToken ct)
            => ExecuteAsync<ModifySubscriptionRequest, ModifySubscriptionResponse>(ct);

        /// <summary>
        /// Enables or disables publishing on one or more subscriptions
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.4"/>).
        /// </summary>
        [HttpPost("/setpublishingmode")]
        public Task SetPublishingModeAsync(CancellationToken ct)
            => ExecuteAsync<SetPublishingModeRequest, SetPublishingModeResponse>(ct);

        /// <summary>
        /// Server-side long-poll for the next NotificationMessage on any
        /// of the caller's subscriptions
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.5"/>).
        /// </summary>
        [HttpPost("/publish")]
        public Task PublishAsync(CancellationToken ct)
            => ExecuteAsync<PublishRequest, PublishResponse>(ct);

        /// <summary>
        /// Requests retransmission of a previously sent NotificationMessage
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.6"/>).
        /// </summary>
        [HttpPost("/republish")]
        public Task RepublishAsync(CancellationToken ct)
            => ExecuteAsync<RepublishRequest, RepublishResponse>(ct);

        /// <summary>
        /// Transfers a subscription to a different session
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.7"/>).
        /// </summary>
        [HttpPost("/transfersubscriptions")]
        public Task TransferSubscriptionsAsync(CancellationToken ct)
            => ExecuteAsync<TransferSubscriptionsRequest, TransferSubscriptionsResponse>(ct);

        /// <summary>
        /// Deletes one or more subscriptions
        /// (<see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.8"/>).
        /// </summary>
        [HttpPost("/deletesubscriptions")]
        public Task DeleteSubscriptionsAsync(CancellationToken ct)
            => ExecuteAsync<DeleteSubscriptionsRequest, DeleteSubscriptionsResponse>(ct);
    }
}
