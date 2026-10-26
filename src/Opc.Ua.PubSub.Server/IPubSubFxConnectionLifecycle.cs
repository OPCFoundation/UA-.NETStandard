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
using Opc.Ua.Fx.Ac;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Provides host-owned ConnectionEndpoint lifecycle transactions for an AutomationComponent.
    /// </summary>
    /// <remarks>
    /// Implementations authorize the actual ConnectionManager represented by the call context and process the complete
    /// endpoint list as one transaction. They preserve unrelated and shared resources, prevent automatic readmission
    /// when removing an endpoint, and return one ordered result per requested endpoint. This capability does not
    /// transfer ownership of host nodes or PubSub runtime components to the PubSub server.
    /// </remarks>
    public interface IPubSubFxConnectionLifecycle
    {
        /// <summary>
        /// Closes or removes the specified host-owned ConnectionEndpoints in one lifecycle transaction.
        /// </summary>
        /// <param name="context">The actual Method call context, including its authenticated Session.</param>
        /// <param name="component">The host-owned AutomationComponent whose Method was called.</param>
        /// <param name="connectionEndpoints">Endpoint NodeIds in the exact order supplied by the caller.</param>
        /// <param name="remove">
        /// Whether the endpoint associations are removed after they have been closed.
        /// </param>
        /// <param name="cancellationToken">Cancels the transaction where the host can safely honor cancellation.</param>
        /// <returns>One per-endpoint status in the same order as <paramref name="connectionEndpoints"/>.</returns>
        ValueTask<ArrayOf<StatusCode>> CloseConnectionsAsync(
            ISystemContext context,
            AutomationComponentState component,
            ArrayOf<NodeId> connectionEndpoints,
            bool remove,
            CancellationToken cancellationToken = default);
    }
}
