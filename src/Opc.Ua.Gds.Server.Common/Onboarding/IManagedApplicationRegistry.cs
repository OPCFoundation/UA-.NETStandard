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

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Tracks the OPC UA applications that have been onboarded onto
    /// the server. Each managed application corresponds to a device
    /// or composite that the registrar has accepted; it is identified
    /// by its <c>ProductInstanceUri</c> and is materialised as a
    /// stable <see cref="NodeId"/> for client-side correlation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations must be safe for concurrent calls.
    /// </para>
    /// </remarks>
    public interface IManagedApplicationRegistry
    {
        /// <summary>
        /// Registers an application identified by
        /// <paramref name="productInstanceUri"/>. If an application
        /// with the same URI is already registered, the existing
        /// record is replaced and its NodeId is preserved.
        /// </summary>
        /// <param name="productInstanceUri">
        /// The stable product-instance URI from the device ticket.
        /// </param>
        /// <param name="certificate">
        /// The device's onboarding certificate as a DER-encoded
        /// byte array.
        /// </param>
        /// <param name="ticket">
        /// The ticket record this registration was matched against.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The NodeId assigned to the managed application. Stable
        /// across re-registrations of the same URI.
        /// </returns>
        ValueTask<NodeId> RegisterAsync(
            string productInstanceUri,
            byte[] certificate,
            TicketRecord ticket,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Unregisters the application with the supplied NodeId.
        /// Returns <see langword="true"/> when an application was
        /// removed.
        /// </summary>
        ValueTask<bool> UnregisterAsync(
            NodeId applicationNodeId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the NodeId of the application with the supplied
        /// <paramref name="productInstanceUri"/>, or
        /// <see langword="null"/> when no such application exists.
        /// </summary>
        ValueTask<NodeId?> FindAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams every managed application.
        /// </summary>
        IAsyncEnumerable<ManagedApplication> ListAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A managed-application record returned by
    /// <see cref="IManagedApplicationRegistry.ListAsync"/>.
    /// </summary>
    public sealed record ManagedApplication(
        NodeId NodeId,
        string ProductInstanceUri,
        byte[] Certificate,
        TicketRecord Ticket);
}
