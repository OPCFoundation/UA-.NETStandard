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

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// Enlists companion bindings in another builder's single subtree registration.
    /// </summary>
    /// <remarks>
    /// The owner prepares every participant, registers the root once, binds every
    /// participant, then commits them. Preparation and binding may fail; commitment
    /// must be non-throwing and must not perform I/O. Disposal aborts an uncommitted
    /// participant, releasing only its bindings and reservations, never the owner's
    /// tree. After commitment the node manager owns the binding lifetime.
    /// Participants are single-use and their callbacks are serialized by the owner.
    /// </remarks>
    public interface ICompanionRegistrationParticipant : IAsyncDisposable
    {
        /// <summary>
        /// Gets the manager which must perform the registration.
        /// </summary>
        DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the exact, unregistered root instance being bound.
        /// </summary>
        BaseObjectState Root { get; }

        /// <summary>
        /// Configures and validates bindings before any node is published.
        /// </summary>
        ValueTask PrepareAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Attaches runtime bindings after publication, without registering nodes again.
        /// </summary>
        ValueTask BindAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits prepared bindings after all fallible work has succeeded.
        /// </summary>
        void Commit();
    }
}
