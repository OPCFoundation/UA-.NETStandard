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

namespace Opc.Ua.GMS.Contracts
{
    /// <summary>
    /// Per-GMS storage for immutable results. Implementations must honor cancellation and atomically commit writes.
    /// </summary>
    public interface IGmsResultStore
    {
        /// <summary>
        /// Returns the newest result, or null only when no result is available.
        /// </summary>
        ValueTask<GmsResult?> ReadLatestAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the identified result, or null only when it is absent.
        /// </summary>
        ValueTask<GmsResult?> ReadAsync(string resultId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns newest-first identifiers, never exceeding the positive caller-supplied limit.
        /// </summary>
        ValueTask<ArrayOf<string>> ListIdsAsync(int maximumResults, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically adds or replaces a result, failing without mutation when storage limits are exceeded.
        /// </summary>
        ValueTask WriteAsync(GmsResult result, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns one domain status for each requested identifier: zero on success, nonzero when absent.
        /// Acknowledgment does not delete the result.
        /// </summary>
        ValueTask<ArrayOf<int>> AcknowledgeAsync(
            ArrayOf<string> resultIds,
            CancellationToken cancellationToken = default);
    }
}
