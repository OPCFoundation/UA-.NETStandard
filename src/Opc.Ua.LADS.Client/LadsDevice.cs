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

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A handle to a LADS device that offers the device operations of
    /// <see cref="LadsClient"/> fluently.
    /// </summary>
    public sealed class LadsDevice
    {
        /// <summary>
        /// Creates a handle.
        /// </summary>
        internal LadsDevice(LadsClient client, NodeId nodeId, string name, string displayName)
        {
            Client = client;
            NodeId = nodeId;
            Name = name;
            DisplayName = displayName;
        }

        /// <summary>
        /// The client the handle belongs to.
        /// </summary>
        public LadsClient Client { get; }

        /// <summary>
        /// The NodeId of the device.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// The browse name of the device.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The display name of the device.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Reads a snapshot of the device.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The snapshot.</returns>
        public ValueTask<LadsDeviceSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Client.ReadDeviceAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Gets the functional units of the device.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The functional units.</returns>
        public ValueTask<ArrayOf<LadsFunctionalUnit>> GetFunctionalUnitsAsync(
            CancellationToken cancellationToken = default)
        {
            return Client.GetFunctionalUnitsAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Gets a functional unit by browse name or display name.
        /// </summary>
        /// <param name="name">The browse name or display name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The functional unit.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotFound"/> when the device has no such unit.
        /// </exception>
        public async ValueTask<LadsFunctionalUnit> GetFunctionalUnitAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A functional unit name is required.", nameof(name));
            }
            foreach (LadsFunctionalUnit unit in await GetFunctionalUnitsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (LadsClient.Matches(unit.Name, unit.DisplayName, name))
                {
                    return unit;
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNotFound, "The LADS device '{0}' has no functional unit '{1}'.", Name, name);
        }

        /// <summary>
        /// Gets the first functional unit of the device.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The functional unit.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotFound"/> when the device has no units.
        /// </exception>
        public async ValueTask<LadsFunctionalUnit> GetFirstFunctionalUnitAsync(
            CancellationToken cancellationToken = default)
        {
            ArrayOf<LadsFunctionalUnit> units = await GetFunctionalUnitsAsync(cancellationToken).ConfigureAwait(false);
            return units.Count > 0
                ? units[0]
                : throw ServiceResultException.Create(
                    StatusCodes.BadNotFound, "The LADS device '{0}' has no functional units.", Name);
        }

        /// <summary>
        /// Gets the maintenance tasks of the device.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The maintenance tasks.</returns>
        public ValueTask<ArrayOf<LadsMaintenanceTask>> GetMaintenanceTasksAsync(
            CancellationToken cancellationToken = default)
        {
            return Client.GetMaintenanceTasksAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Reads the operation counters of the device.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The counters, or <c>null</c> when the device has none.</returns>
        public ValueTask<LadsOperationCounters?> ReadOperationCountersAsync(
            CancellationToken cancellationToken = default)
        {
            return Client.ReadOperationCountersAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Moves the device to the <c>Operate</c> state.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask GotoOperateAsync(CancellationToken cancellationToken = default)
        {
            return Client.GotoOperateAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Moves the device to the <c>Shutdown</c> state.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask GotoShutdownAsync(CancellationToken cancellationToken = default)
        {
            return Client.GotoShutdownAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Moves the device to the <c>Sleep</c> state.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask GotoSleepAsync(CancellationToken cancellationToken = default)
        {
            return Client.GotoSleepAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Switches the device to the <c>Processing</c> operation mode.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask GotoProcessingAsync(CancellationToken cancellationToken = default)
        {
            return Client.GotoProcessingAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Switches the device to the <c>Maintenance</c> operation mode.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask GotoMaintenanceAsync(CancellationToken cancellationToken = default)
        {
            return Client.GotoMaintenanceAsync(NodeId, cancellationToken);
        }

        /// <summary>
        /// Switches the device to the <c>Setup</c> operation mode.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask GotoSetupAsync(CancellationToken cancellationToken = default)
        {
            return Client.GotoSetupAsync(NodeId, cancellationToken);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return DisplayName;
        }
    }
}
