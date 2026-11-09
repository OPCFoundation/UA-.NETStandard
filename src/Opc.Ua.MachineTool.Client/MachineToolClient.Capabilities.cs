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

using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.Client
{
    public sealed partial class MachineToolClient
    {
        /// <summary>
        /// Reads this library's optional per-instance declaration, never inferring supported CUs from namespace names.
        /// Third-party machines without the extension return an explicitly absent declaration.
        /// Malformed declarations, access failures and cancelled services remain errors.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<MachineToolCapabilityReadSnapshot> GetCapabilitiesAsync(
            NodeId machineId, CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource operation = BeginObservation(cancellationToken);
            try
            {
                CancellationToken ct = operation.Token;
                ct.ThrowIfCancellationRequested();
                MachineToolTypeClient machine = GetMachineProxy(machineId);
                _ = await machine.GetMonitoringAsync(Telemetry, ct).ConfigureAwait(false) ??
                    throw MissingMandatory(BrowseNames.Monitoring);
                int index = Session.NamespaceUris.GetIndex(MachineToolCapabilityNames.NamespaceUri);
                if (index < 0)
                {
                    return new MachineToolCapabilityReadSnapshot(false, null, []);
                }
                ArrayOf<NodeId> declaration = await ResolveChildNodesAsync(machineId,
                    [new QualifiedName(MachineToolCapabilityNames.Capabilities, (ushort)index)], ct)
                    .ConfigureAwait(false);
                if (declaration[0].IsNull)
                {
                    return new MachineToolCapabilityReadSnapshot(false, null, []);
                }
                ArrayOf<string> fields = MachineToolCapabilityNames.Fields;
                var names = new QualifiedName[fields.Count];
                for (int field = 0; field < fields.Count; field++)
                {
                    names[field] = new QualifiedName(fields[field], (ushort)index);
                }
                ArrayOf<NodeId> nodes = await ResolveChildNodesAsync(declaration[0], names.ToArrayOf(), ct)
                    .ConfigureAwait(false);
                foreach (NodeId node in nodes)
                {
                    if (node.IsNull)
                    {
                        throw InvalidCapabilities("The per-instance capability declaration is incomplete.");
                    }
                }
                ArrayOf<DataValue> values = await ReadInventorySamplesAsync(nodes, Attributes.Value, ct)
                    .ConfigureAwait(false);
                int bytes = 0;
                foreach (DataValue value in values)
                {
                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        throw new ServiceResultException(value.StatusCode);
                    }
                    using var encoder = new JsonEncoder(Session.MessageContext);
                    encoder.WriteDataValue("Capability", value);
                    bytes += Encoding.UTF8.GetByteCount(encoder.CloseAndReturnText());
                    if (bytes > 65536)
                    {
                        throw InventoryLimit("The capability declaration exceeds 64 kibibytes.");
                    }
                }
                if (!values[0].WrappedValue.TryGetValue(out ArrayOf<QualifiedName> units) ||
                    units.Count > 37 ||
                    !values[1].WrappedValue.TryGetValue(out ArrayOf<string> profiles) ||
                    profiles.Count > 12)
                {
                    throw InvalidCapabilities("The capability unit or profile array is invalid or unbounded.");
                }
                uint[] numbers = new uint[8];
                for (int field = 2; field < 10; field++)
                {
                    if (!values[field].WrappedValue.TryGetValue(out numbers[field - 2]))
                    {
                        throw InvalidCapabilities("A monitoring-capacity field is not UInt32.");
                    }
                }
                if (!values[10].WrappedValue.TryGetValue(out bool minimum) ||
                    !values[11].WrappedValue.TryGetValue(out bool all))
                {
                    throw InvalidCapabilities("Monitoring-capacity results must be Boolean.");
                }
                var capacity = new MachineToolMonitoringCapacity(
                    numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5],
                    numbers[6], numbers[7], minimum, all);
                return new MachineToolCapabilityReadSnapshot(true,
                    new MachineToolCapabilities(machineId, units, profiles, capacity), values);
            }
            finally
            {
                EndObservation();
            }
        }

        /// <summary>
        /// Reports malformed declarations without substituting a successful empty claim.
        /// </summary>
        private static ServiceResultException InvalidCapabilities(string message)
        {
            return new(StatusCodes.BadDecodingError, message);
        }
    }
}
