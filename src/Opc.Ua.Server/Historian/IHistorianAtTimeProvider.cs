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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that can answer read-at-time
    /// (Part 11 §5.2.6.6) requests natively. Providers that do not
    /// implement this interface get a streaming framework fallback that
    /// uses <see cref="IHistorianDataProvider.ReadRawAsync"/> plus
    /// stepped/sloped interpolation.
    /// </summary>
    public interface IHistorianAtTimeProvider
    {
        /// <summary>
        /// Returns one value per requested timestamp, in the same order
        /// the client supplied. Values without an exact match should be
        /// interpolated (or marked uncertain/bounded per the historization
        /// configuration).
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <param name="request">Normalised at-time read request.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<IList<DataValue>> ReadAtTimeAsync(
            HistorianOperationContext context,
            HistorianAtTimeReadRequest request,
            CancellationToken ct);
    }
}
