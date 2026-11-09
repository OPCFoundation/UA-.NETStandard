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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Decides which acknowledgements a session sends with its next Publish request and which
    /// it defers. Register it with the session options, the session builder or dependency
    /// injection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Publish workers call the provider concurrently and without holding SDK locks, so an
    /// implementation must be thread-safe. Each acknowledgement is validated again after the
    /// decision; one whose subscription was retired in the meantime is not sent.
    /// </para>
    /// <para>
    /// Deferred acknowledgements are presented again with later Publish requests until they
    /// are sent or end, for example because their subscription is deleted. If the provider
    /// throws, the whole batch is deferred (fail-closed).
    /// </para>
    /// </remarks>
    public interface ISubscriptionAcknowledgementProvider
    {
        /// <summary>
        /// Decides about a batch of acknowledgements.
        /// </summary>
        /// <param name="acknowledgements">The acknowledgements eligible for the next request.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>Which acknowledgements to defer.</returns>
        ValueTask<AcknowledgementDecision> DecideAsync(
            ArrayOf<AcknowledgementHandle> acknowledgements,
            CancellationToken ct = default);
    }
}
