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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System.Threading;
using System.Threading.Tasks;
using V1 = Opc.Ua.ISA95.JobControl.V1;
#pragma warning restore IDE0005

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
