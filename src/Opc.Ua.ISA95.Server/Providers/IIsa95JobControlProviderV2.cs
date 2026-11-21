// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Receives Job Control V2 job order operations.
    /// </summary>
    public interface IIsa95JobOrderReceiverV2
    {
        /// <summary>
        /// Applies a Job Control V2 operation to a job order.
        /// </summary>
        /// <param name="operation">
        /// The operation to apply.
        /// </param>
        /// <param name="jobOrder">
        /// The job order the operation applies to.
        /// </param>
        /// <param name="comment">
        /// The localized audit comment describing why the operation was invoked,
        /// as defined by OPC-10031-4 V2. May be empty when no comment is provided.
        /// The latest non-empty comment is retained with the stored job order.
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The receipt describing the outcome.
        /// </returns>
        ValueTask<Isa95JobOrderReceiptV2> ReceiveJobOrderAsync(
            Isa95JobOrderOperationV2 operation,
            V2.ISA95JobOrderDataType jobOrder,
            ArrayOf<LocalizedText> comment = default,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Provides Job Control V2 job responses.
    /// </summary>
    public interface IIsa95JobResponseProviderV2
    {
        /// <summary>
        /// Requests the latest job response for a job-order ID.
        /// </summary>
        /// <param name="jobOrderId">
        /// The job order identifier to request responses for.
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The query result carrying the matching response.
        /// </returns>
        ValueTask<Isa95JobResponseByIdResultV2> RequestJobResponseByJobOrderIdAsync(
            string jobOrderId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests all job responses matching a V2 state path.
        /// </summary>
        /// <param name="state">
        /// The state path. The first entry identifies the top-level state.
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The query result carrying the matching responses.
        /// </returns>
        ValueTask<Isa95JobResponsesByStateResultV2> RequestJobResponsesByStateAsync(
            ArrayOf<V2.ISA95StateDataType> state,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Receives Job Control V2 job responses.
    /// </summary>
    public interface IIsa95JobResponseReceiverV2
    {
        /// <summary>
        /// Receives and stores a Job Control V2 job response.
        /// </summary>
        /// <param name="response">
        /// The response to store.
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The receipt describing the outcome.
        /// </returns>
        ValueTask<Isa95JobResponseReceiptV2> ReceiveJobResponseAsync(
            V2.ISA95JobResponseDataType response,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Publishes Job Control V2 job order status changes for server event
    /// projection. Each subscriber receives exactly one notification per
    /// committed state change that occurs after it subscribes; subscribers are
    /// independent and cancellation or disposal of one does not affect others.
    /// </summary>
    public interface IIsa95JobStatusSourceV2
    {
        /// <summary>
        /// Subscribes to committed job order status changes.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token that ends the subscription when cancelled.
        /// </param>
        /// <returns>
        /// An asynchronous stream of status notifications.
        /// </returns>
        IAsyncEnumerable<Isa95JobStatusNotificationV2> SubscribeAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Applies execution-system state changes that are automatic in the
    /// OPC-10031-4 V2 state machine and are not represented by client Methods.
    /// </summary>
    public interface IIsa95JobExecutionController
    {
        /// <summary>
        /// Applies an execution-system transition to a job order.
        /// </summary>
        ValueTask<Isa95JobOrderReceiptV2> TransitionAsync(
            string jobOrderId,
            Isa95JobExecutionTransition transition,
            CancellationToken cancellationToken = default);
    }
}
