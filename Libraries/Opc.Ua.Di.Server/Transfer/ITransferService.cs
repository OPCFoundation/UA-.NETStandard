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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.Transfer
{
    /// <summary>
    /// Application-facing facade for OPC 10000-100 §10.4
    /// <c>TransferServicesType</c>. Implementations provide
    /// import/export of the parameter set of a topology element and
    /// hold the chunked transfer state between
    /// <c>TransferToDevice</c> / <c>TransferFromDevice</c> and
    /// <c>FetchTransferResultData</c> calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two transfer-direction methods kick off an asynchronous
    /// transfer and return an integer transfer ID.
    /// Clients then poll <see cref="FetchAsync"/> with that ID and a
    /// monotonically-increasing sequence number to drain the chunks
    /// of the result.
    /// </para>
    /// <para>
    /// Default implementation:
    /// <see cref="DefaultTransferService"/> — in-memory.
    /// </para>
    /// </remarks>
    public interface ITransferService
    {
        /// <summary>
        /// Starts a transfer of parameters TO the device. The server
        /// records the <paramref name="parameters"/> internally and
        /// returns a transfer ID the client can poll with
        /// <see cref="FetchAsync"/> to retrieve the per-parameter
        /// status codes (mostly <see cref="StatusCodes.Good"/>
        /// on success).
        /// </summary>
        ValueTask<int> TransferToDeviceAsync(
            ISystemContext context,
            NodeId elementId,
            ParameterSet parameters,
            CancellationToken ct = default);

        /// <summary>
        /// Starts a transfer of parameters FROM the device — the
        /// server snapshots the current parameter set and queues it
        /// for chunked fetch by <see cref="FetchAsync"/>.
        /// </summary>
        ValueTask<int> TransferFromDeviceAsync(
            ISystemContext context,
            NodeId elementId,
            CancellationToken ct = default);

        /// <summary>
        /// Fetches the next chunk of a previously-started transfer.
        /// </summary>
        ValueTask<FetchResult> FetchAsync(
            ISystemContext context,
            int transferId,
            int sequenceNumber,
            int maxResults,
            bool omitGoodResults,
            CancellationToken ct = default);
    }

    /// <summary>
    /// One chunk of a transfer-result stream returned by
    /// <see cref="ITransferService.FetchAsync"/>.
    /// </summary>
    public sealed record FetchResult(
        int SequenceNumber,
        bool EndOfResults,
        ParameterEntry[] Entries,
        StatusCode TransferError);
}
