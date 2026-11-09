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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Authenticated connection borrowed by bounded membership operations.
    /// The connection owner, not membership retry logic, controls its lifetime.
    /// </summary>
    public interface IRecyclingMembershipClient : IAsyncDisposable
    {
        /// <summary>
        /// Actual server-side session identity, preserved while retrying revision contention.
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        /// Whether the managed connection is currently usable.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Actual authenticated user-certificate fingerprint, not a caller-supplied actor claim.
        /// </summary>
        string CertificateSha256 { get; }

        /// <summary>
        /// Reads a committed image for optimistic membership commands.
        /// </summary>
        ValueTask<CommittedSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies a typed command through the authenticated connection.
        /// </summary>
        ValueTask<RecyclingCommandOutcome> ExecuteAsync(
            RecyclingCommand command,
            CancellationToken cancellationToken = default);
    }
}
