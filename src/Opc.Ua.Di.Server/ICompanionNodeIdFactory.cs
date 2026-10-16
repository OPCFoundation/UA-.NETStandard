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

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// Marks a thread-safe instance allocator shared by companion builders on one DI owner.
    /// </summary>
    /// <remarks>
    /// Allocations must remain unique while instances are staged and use the configured
    /// application namespace. All builders sharing an owner must select that same namespace.
    /// Existing family-specific allocator markers can derive from this marker.
    /// </remarks>
    public interface ICompanionNodeIdFactory : INodeIdFactory
    {
    }

    /// <summary>
    /// Releases temporary allocations after a companion build succeeds or is rolled back.
    /// </summary>
    /// <remarks>
    /// This optional capability does not add members to existing allocator marker contracts.
    /// The allocator must only release reservations still owned by the supplied node.
    /// </remarks>
    public interface ICompanionNodeIdReservationOwner
    {
        /// <summary>
        /// Releases the node's uncommitted reservations without removing registered address-space nodes.
        /// </summary>
        /// <param name="node">The node whose own reservations may be released.</param>
        void ReleaseNodeIdReservation(NodeState node);
    }
}
