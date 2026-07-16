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

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Allocates PubSub server-side transient identifiers.
    /// </summary>
    /// <remarks>
    /// Implements the externalized state contract required for Part 14
    /// §9.1.6 HA deployments. Implementations must allocate monotonically
    /// increasing values when shared by multiple server instances.
    /// </remarks>
    public interface IPubSubIdAllocator
    {
        /// <summary>
        /// Reserves a sequence of PubSub configuration identifiers.
        /// </summary>
        /// <param name="count">Number of identifiers to reserve.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<ArrayOf<uint>> ReserveIdsAsync(
            ushort count,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Allocates a PubSubConfiguration FileType handle.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<uint> AllocateFileHandleAsync(CancellationToken cancellationToken = default);
    }
}
