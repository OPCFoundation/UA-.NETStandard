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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.LADS.Client
{
    public sealed partial class LadsClient
    {
        /// <summary>
        /// Discovers the LADS devices below the Device Integration <c>DeviceSet</c>
        /// and the Machinery <c>Machines</c> folder, including vendor subtypes of
        /// <c>LADSDeviceType</c>. A device both folders reference is returned once.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The devices in discovery order.</returns>
        public async ValueTask<ArrayOf<LadsDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<LadsDevice>();
            var seen = new HashSet<NodeId>();
            ExpandedNodeId[] roots = [Opc.Ua.Di.ObjectIds.DeviceSet, Opc.Ua.Machinery.ObjectIds.Machines];
            foreach (ExpandedNodeId root in roots)
            {
                List<ReferenceDescription> references = await BrowseAsync(
                    ToNodeId(root), NodeClass.Object, cancellationToken).ConfigureAwait(false);
                foreach (ReferenceDescription reference in references)
                {
                    NodeId nodeId = ToNodeId(reference.NodeId);
                    if (nodeId.IsNull || seen.Contains(nodeId) ||
                        !await IsTypeOfAsync(reference.TypeDefinition, ObjectTypeIds.LADSDeviceType, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        continue;
                    }
                    seen.Add(nodeId);
                    devices.Add(new LadsDevice(this, nodeId, reference.BrowseName.Name ?? string.Empty,
                        DisplayNameOf(reference)));
                }
            }
            return devices.ToArrayOf();
        }

        /// <summary>
        /// Finds a LADS device by its browse name or display name.
        /// </summary>
        /// <param name="name">The browse name or display name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The device, or <c>null</c> when none matches.</returns>
        /// <exception cref="ArgumentException">The name is empty.</exception>
        public async ValueTask<LadsDevice?> FindDeviceAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A device name is required.", nameof(name));
            }
            ArrayOf<LadsDevice> devices = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
            foreach (LadsDevice device in devices)
            {
                if (Matches(device.Name, device.DisplayName, name))
                {
                    return device;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets a LADS device by its browse name or display name.
        /// </summary>
        /// <param name="name">The browse name or display name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The device.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotFound"/> when no device matches.
        /// </exception>
        public async ValueTask<LadsDevice> GetDeviceAsync(string name, CancellationToken cancellationToken = default)
        {
            return await FindDeviceAsync(name, cancellationToken).ConfigureAwait(false) ??
                throw ServiceResultException.Create(StatusCodes.BadNotFound, "No LADS device '{0}' was found.", name);
        }

        /// <summary>
        /// Gets the first LADS device the server publishes.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The device.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotFound"/> when the server publishes none.
        /// </exception>
        public async ValueTask<LadsDevice> GetFirstDeviceAsync(CancellationToken cancellationToken = default)
        {
            ArrayOf<LadsDevice> devices = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
            return devices.Count > 0
                ? devices[0]
                : throw new ServiceResultException(StatusCodes.BadNotFound, "The server publishes no LADS device.");
        }

        /// <summary>
        /// Gets a handle for a device by its NodeId without browsing for it.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <returns>The device handle.</returns>
        /// <exception cref="ArgumentException">The NodeId is null.</exception>
        public LadsDevice Device(NodeId deviceId)
        {
            ThrowIfNull(deviceId, nameof(deviceId));
            return new LadsDevice(this, deviceId, deviceId.ToString(), deviceId.ToString());
        }

        /// <summary>
        /// Gets the functional units of a device's <c>FunctionalUnitSet</c>.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The functional units.</returns>
        public async ValueTask<ArrayOf<LadsFunctionalUnit>> GetFunctionalUnitsAsync(
            NodeId deviceId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(deviceId, nameof(deviceId));
            NodeId set = await ResolveAsync(deviceId, cancellationToken, Lads(BrowseNames.FunctionalUnitSet))
                .ConfigureAwait(false);
            List<ReferenceDescription> references = await BrowseAsync(set, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false);
            var units = new List<LadsFunctionalUnit>();
            foreach (ReferenceDescription reference in references)
            {
                NodeId nodeId = ToNodeId(reference.NodeId);
                if (!nodeId.IsNull &&
                    await IsTypeOfAsync(reference.TypeDefinition, ObjectTypeIds.FunctionalUnitType, cancellationToken)
                        .ConfigureAwait(false))
                {
                    units.Add(new LadsFunctionalUnit(this, nodeId, reference.BrowseName.Name ?? string.Empty,
                        DisplayNameOf(reference)));
                }
            }
            return units.ToArrayOf();
        }

        /// <summary>
        /// Gets a handle for a functional unit by its NodeId without browsing for it.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <returns>The functional unit handle.</returns>
        /// <exception cref="ArgumentException">The NodeId is null.</exception>
        public LadsFunctionalUnit FunctionalUnit(NodeId functionalUnitId)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            return new LadsFunctionalUnit(this, functionalUnitId, functionalUnitId.ToString(),
                functionalUnitId.ToString());
        }

        /// <summary>
        /// Gets the functions of the <c>FunctionSet</c> of a functional unit or a function.
        /// </summary>
        /// <param name="parentId">The NodeId of the functional unit or function.</param>
        /// <param name="recursive">Whether to include the functions of nested function sets.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The functions, parents before their nested functions.</returns>
        public async ValueTask<ArrayOf<LadsFunction>> GetFunctionsAsync(
            NodeId parentId,
            bool recursive = true,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(parentId, nameof(parentId));
            var functions = new List<LadsFunction>();
            var seen = new HashSet<NodeId>();
            await CollectFunctionsAsync(parentId, string.Empty, recursive ? Options.MaximumFunctionDepth : 1,
                functions, seen, cancellationToken).ConfigureAwait(false);
            return functions.ToArrayOf();
        }

        /// <summary>
        /// Gets a handle for a function by its NodeId without browsing for it.
        /// </summary>
        /// <param name="functionId">The NodeId of the function.</param>
        /// <param name="kind">The kind of the function, when known.</param>
        /// <returns>The function handle.</returns>
        /// <exception cref="ArgumentException">The NodeId is null.</exception>
        public LadsFunction Function(NodeId functionId, LadsFunctionKind kind = LadsFunctionKind.Unknown)
        {
            ThrowIfNull(functionId, nameof(functionId));
            return new LadsFunction(this, functionId, functionId.ToString(), functionId.ToString(), kind,
                functionId.ToString());
        }

        /// <summary>
        /// Tests whether a browse name or display name matches a name.
        /// </summary>
        internal static bool Matches(string name, string displayName, string expected)
        {
            return string.Equals(name, expected, StringComparison.Ordinal) ||
                string.Equals(displayName, expected, StringComparison.Ordinal);
        }

        /// <summary>
        /// Throws when a NodeId is null.
        /// </summary>
        /// <exception cref="ArgumentException">The NodeId is null.</exception>
        internal static void ThrowIfNull(NodeId nodeId, string parameterName)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A NodeId is required.", parameterName);
            }
        }

        private async ValueTask CollectFunctionsAsync(
            NodeId parentId,
            string parentPath,
            int depth,
            List<LadsFunction> functions,
            HashSet<NodeId> seen,
            CancellationToken cancellationToken)
        {
            if (depth <= 0)
            {
                return;
            }
            NodeId set = await ResolveAsync(parentId, cancellationToken, Lads(BrowseNames.FunctionSet))
                .ConfigureAwait(false);
            List<ReferenceDescription> references = await BrowseAsync(set, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false);
            foreach (ReferenceDescription reference in references)
            {
                NodeId nodeId = ToNodeId(reference.NodeId);
                if (nodeId.IsNull || !seen.Add(nodeId) ||
                    !await IsTypeOfAsync(reference.TypeDefinition, ObjectTypeIds.FunctionType, cancellationToken)
                        .ConfigureAwait(false))
                {
                    continue;
                }
                LadsFunctionKind kind = await ClassifyFunctionAsync(reference.TypeDefinition, cancellationToken)
                    .ConfigureAwait(false);
                string name = reference.BrowseName.Name ?? string.Empty;
                string path = parentPath.Length == 0 ? name : parentPath + "/" + name;
                functions.Add(new LadsFunction(this, nodeId, name, DisplayNameOf(reference), kind, path));
                await CollectFunctionsAsync(nodeId, path, depth - 1, functions, seen, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
