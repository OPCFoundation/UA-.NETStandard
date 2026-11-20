// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using V1 = Opc.Ua.ISA95.JobControl.V1;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Receives Job Control V1 job orders (the <c>ReceiveJobOrder</c> operation).
    /// </summary>
    public interface IIsa95JobOrderReceiverV1
    {
        /// <summary>
        /// Applies a Job Control V1 command to a job order.
        /// </summary>
        /// <param name="command">
        /// The command to apply.
        /// </param>
        /// <param name="jobOrder">
        /// The job order the command applies to.
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The receipt describing the outcome.
        /// </returns>
        ValueTask<Isa95JobOrderReceiptV1> ReceiveJobOrderAsync(
            V1.ISA95JobOrderCommandEnum command,
            V1.ISA95JobOrderDataType jobOrder,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Provides Job Control V1 job responses (the <c>RequestJobResponse</c>
    /// operation).
    /// </summary>
    public interface IIsa95JobResponseProviderV1
    {
        /// <summary>
        /// Requests the job responses for a job order filtered by state.
        /// </summary>
        /// <param name="jobOrderId">
        /// The job order identifier to request responses for.
        /// </param>
        /// <param name="state">
        /// The state filter. <c>Undefined</c> returns responses in any state.
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The query result carrying the matching responses.
        /// </returns>
        ValueTask<Isa95JobResponseQueryV1> RequestJobResponseAsync(
            string? jobOrderId,
            V1.ISA95JobOrderStateEnum state,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Receives Job Control V1 job responses (the <c>ReceiveJobResponse</c>
    /// operation).
    /// </summary>
    public interface IIsa95JobResponseReceiverV1
    {
        /// <summary>
        /// Receives and stores a Job Control V1 job response.
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
        ValueTask<Isa95JobResponseReceiptV1> ReceiveJobResponseAsync(
            V1.ISA95JobResponseDataType response,
            CancellationToken cancellationToken = default);
    }
}
