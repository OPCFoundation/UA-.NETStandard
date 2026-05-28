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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that support modified-history reads
    /// (Part 11 §5.2.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modified history returns the prior versions of values that were
    /// replaced and the values that were deleted, plus the
    /// <see cref="ModificationInfo"/> describing each modification. The
    /// "live" current value at each timestamp is <em>not</em> returned by
    /// this call (it belongs to <see cref="IHistorianDataProvider.ReadRawAsync"/>).
    /// </para>
    /// </remarks>
    public interface IHistorianModifiedProvider
    {
        /// <summary>
        /// Reads one page of modified-history values from the archive.
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <param name="request">Normalised modified read request.</param>
        /// <param name="resumeToken">Page resume token; empty on first page.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<HistorianPage<ModifiedDataValue>> ReadModifiedAsync(
            HistorianOperationContext context,
            HistorianModifiedReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct);
    }
}
