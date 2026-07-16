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

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// An acquired leadership lease used to elect the active instance of a
    /// redundant PubSub component.
    /// </summary>
    /// <remarks>
    /// The <see cref="FencingToken"/> is a monotonically increasing value the
    /// store assigns each time the lease changes owner; downstream consumers
    /// can use it to reject stale writes from a superseded active instance
    /// (fencing), per the externalized-state requirements of OPC UA Part 14
    /// §9.1.6.
    /// </remarks>
    /// <param name="LeaseKey">
    /// Stable key identifying the contended resource (for example a writer
    /// group component id).
    /// </param>
    /// <param name="OwnerId">
    /// Identifier of the instance currently holding the lease.
    /// </param>
    /// <param name="FencingToken">
    /// Monotonic token incremented on every ownership change.
    /// </param>
    /// <param name="ExpiresAt">
    /// Absolute UTC time at which the lease expires unless renewed.
    /// </param>
    public readonly record struct PubSubLease(
        string LeaseKey,
        string OwnerId,
        long FencingToken,
        DateTimeOffset ExpiresAt);
}
