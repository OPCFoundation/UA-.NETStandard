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

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Optional reservation-provider capability for transaction-owned automatic identifiers.
    /// </summary>
    public interface IPubSubCommunicationIdAllocation
    {
        /// <summary>
        /// Resolves the explicit transport default without inferring it from existing connections.
        /// </summary>
        ValueTask<Variant> ResolvePublisherIdAsync(
            string transportProfileUri, CancellationToken cancellationToken);

        /// <summary>
        /// Allocates one identifier in the exact typed publisher domain. Disposing the lease releases
        /// uncommitted allocation; successful persistence consumes it.
        /// </summary>
        ValueTask<IPubSubCommunicationIdAllocationLease> AllocateAsync(
            string transportProfileUri,
            Variant publisherId,
            bool dataSetWriter,
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken);

        /// <summary>
        /// Checks pending reservation ownership without committing or consuming reservations.
        /// </summary>
        ValueTask ValidateOwnershipAsync(
            PubSubConfigurationDataType configuration, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A transaction-owned provisional identifier.
    /// </summary>
    public interface IPubSubCommunicationIdAllocationLease : IDisposable
    {
        /// <summary>
        /// Gets the allocated communication identifier.
        /// </summary>
        ushort Id { get; }
    }
}
