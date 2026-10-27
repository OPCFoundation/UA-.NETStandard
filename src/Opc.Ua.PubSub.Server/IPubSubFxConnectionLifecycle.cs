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
using Opc.Ua.Fx.Data;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Provides host-owned ConnectionEndpoint lifecycle transactions for an AutomationComponent.
    /// </summary>
    /// <remarks>
    /// Implementations authorize the actual ConnectionManager represented by the call context. EstablishConnections
    /// commands are processed in Part 81 order and return ordered endpoint results. CloseConnections processes the
    /// complete endpoint list as one transaction, preserves unrelated and shared resources, and prevents automatic
    /// readmission when removing an endpoint. This capability does not transfer ownership of host nodes or PubSub
    /// runtime components to the PubSub server.
    /// </remarks>
    public interface IPubSubFxConnectionLifecycle
    {
        /// <summary>
        /// Processes the host-owned endpoint commands in an EstablishConnections call.
        /// </summary>
        /// <remarks>
        /// The implementation processes selected commands in Part 81 order, starting with
        /// CreateConnectionEndpointCmd and ending with EnableCommunicationCmd. It preserves the supplied
        /// functional-entity verification results, returns ordered per-endpoint results, performs atomic operations
        /// where required by Part 81, and does not take ownership of pre-existing or shared host resources.
        /// </remarks>
        /// <param name="context">The actual Method call context, including its authenticated Session.</param>
        /// <param name="component">The host-owned AutomationComponent whose Method was called.</param>
        /// <param name="commandMask">The complete EstablishConnections command mask.</param>
        /// <param name="connectionEndpointConfigurations">The ordered endpoint configuration requests.</param>
        /// <param name="initialEndpointResults">
        /// Ordered results produced by earlier verification commands in the same Method call.
        /// </param>
        /// <param name="reservationResults">
        /// Ordered communication-ID reservations completed earlier in the same Method call.
        /// </param>
        /// <param name="communicationConfigurations">
        /// Communication-model configuration supplied for SetCommunicationConfigurationCmd.
        /// </param>
        /// <param name="cancellationToken">Cancels processing where the host can safely honor cancellation.</param>
        /// <returns>Overall status and the ordered endpoint and communication-configuration results.</returns>
        ValueTask<PubSubFxEstablishConnectionsResult> EstablishConnectionsAsync(
            ISystemContext context,
            AutomationComponentState component,
            uint commandMask,
            ArrayOf<ConnectionEndpointConfigurationDataType> connectionEndpointConfigurations,
            ArrayOf<ConnectionEndpointConfigurationResultDataType> initialEndpointResults,
            ArrayOf<ReserveCommunicationIdsResultDataType> reservationResults,
            ArrayOf<CommunicationConfigurationDataType> communicationConfigurations,
            CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Contains the result of host-owned Part 81 EstablishConnections endpoint commands.
    /// </summary>
    public sealed class PubSubFxEstablishConnectionsResult
    {
        /// <summary>
        /// Initializes a result containing the overall method status and ordered command result arrays.
        /// </summary>
        /// <param name="serviceResult">The overall EstablishConnections status.</param>
        /// <param name="connectionEndpointConfigurationResults">
        /// Per-endpoint results in the request order.
        /// </param>
        /// <param name="communicationConfigurationResults">
        /// Communication-configuration result array; one item when the command is processed.
        /// </param>
        public PubSubFxEstablishConnectionsResult(
            ServiceResult serviceResult,
            ArrayOf<ConnectionEndpointConfigurationResultDataType> connectionEndpointConfigurationResults,
            ArrayOf<CommunicationConfigurationResultDataType> communicationConfigurationResults)
        {
            ServiceResult = serviceResult;
            ConnectionEndpointConfigurationResults = connectionEndpointConfigurationResults;
            CommunicationConfigurationResults = communicationConfigurationResults;
        }

        /// <summary>
        /// Gets the overall EstablishConnections status.
        /// </summary>
        public ServiceResult ServiceResult { get; }

        /// <summary>
        /// Gets the per-endpoint results in request order.
        /// </summary>
        public ArrayOf<ConnectionEndpointConfigurationResultDataType> ConnectionEndpointConfigurationResults { get; }

        /// <summary>
        /// Gets the communication-configuration result array.
        /// </summary>
        public ArrayOf<CommunicationConfigurationResultDataType> CommunicationConfigurationResults { get; }
    }
}
