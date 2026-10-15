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

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DiBrowseNames = Opc.Ua.Di.BrowseNames;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// Reads a machinery item's OPC 40001-1 operation counters, or
        /// <see langword="null"/> when it publishes none.
        /// </summary>
        /// <param name="machineryItem">The machine or component to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MachineryOperationCounters?> ReadOperationCountersAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            if (m_options is not null &&
                !TryGetNamespaceIndex(Di.Namespaces.OpcUaDi, out _) &&
                !TryGetNamespaceIndex(Namespaces.Machinery, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
            ushort diNamespaceIndex = NamespaceIndexOf(Di.Namespaces.OpcUaDi);
            NodeId counters = await ResolveChildAsync(
                machineryItem,
                new QualifiedName(DiBrowseNames.OperationCounters, diNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            if (counters.IsNull)
            {
                return null;
            }

            var powerOnName = new QualifiedName(
                DiBrowseNames.PowerOnDuration,
                diNamespaceIndex);
            var operationName = new QualifiedName(
                DiBrowseNames.OperationDuration,
                diNamespaceIndex);
            // OperationCycleCounter is a Device Integration member the
            // Machinery type re-declares, so its browse name stays in the DI
            // namespace.
            var cycleName = new QualifiedName(
                DiBrowseNames.OperationCycleCounter,
                diNamespaceIndex);

            ReadValueSet values = await ReadChildValuesAsync(
                counters,
                [powerOnName, operationName, cycleName],
                ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);

            values.TryGetValue(powerOnName, out Variant powerOn);
            values.RequireType(powerOnName, HasType(powerOn, BuiltInType.Double));
            values.TryGetValue(operationName, out Variant operation);
            values.RequireType(operationName, HasType(operation, BuiltInType.Double));
            values.TryGetValue(cycleName, out Variant cycles);
            values.RequireType(cycleName,
                HasType(cycles, BuiltInType.Byte) ||
                HasType(cycles, BuiltInType.UInt16) ||
                HasType(cycles, BuiltInType.UInt32) ||
                HasType(cycles, BuiltInType.UInt64));
            return new MachineryOperationCounters
            {
                Values = values.Values,
                NodeId = counters,
                PowerOnDuration = AsDouble(values, powerOnName),
                OperationDuration = AsDouble(values, operationName),
                OperationCycleCounter = cycles
            };
        }

        /// <summary>
        /// Enumerates a machinery item's OPC 40001-1 lifetime counters.
        /// Returns nothing when it publishes none.
        /// </summary>
        /// <param name="machineryItem">The machine or component to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<MachineryLifetimeVariable> ReadLifetimeCountersAsync(
            NodeId machineryItem,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId counters = await ResolveMachineryChildAsync(
                machineryItem,
                MachineryBrowseNames.LifetimeCounters,
                cancellationToken).ConfigureAwait(false);
            if (counters.IsNull)
            {
                yield break;
            }

            ushort diNamespaceIndex = NamespaceIndexOf(Di.Namespaces.OpcUaDi);
            var startName = new QualifiedName(DiBrowseNames.StartValue, diNamespaceIndex);
            var limitName = new QualifiedName(DiBrowseNames.LimitValue, diNamespaceIndex);
            var warningName = new QualifiedName(
                DiBrowseNames.WarningValues,
                diNamespaceIndex);

            var variables = new List<MachineEntry>();
            await foreach (MachineEntry entry in EnumerateChildVariablesAsync(
                    counters,
                    cancellationToken).ConfigureAwait(false))
            {
                variables.Add(entry);
            }

            foreach (MachineEntry entry in variables)
            {
                ReadValueSet children = await ReadChildValuesAsync(
                    entry.NodeId,
                    [startName, limitName, warningName],
                    ReferenceTypeIds.HierarchicalReferences,
                    cancellationToken).ConfigureAwait(false);
                ReadValueSet self = await ReadChildValuesAsync(
                    counters,
                    [entry.BrowseName],
                    ReferenceTypeIds.HierarchicalReferences,
                    cancellationToken).ConfigureAwait(false);

                children.TryGetValue(warningName, out Variant warnings);
                bool hasWarnings = warnings.TryGetValue(out ArrayOf<double> warningValues);
                children.RequireType(warningName, hasWarnings);
                ValidateArrayCount(children, warningName, warningValues.Count);
                yield return new MachineryLifetimeVariable
                {
                    Values = CombineEvidence(self.Values, children.Values),
                    NodeId = entry.NodeId,
                    BrowseName = entry.BrowseName,
                    Remaining = AsDouble(self, entry.BrowseName),
                    StartValue = AsDouble(children, startName),
                    LimitValue = AsDouble(children, limitName),
                    WarningValues = hasWarnings ? warningValues : default
                };
            }
        }

        /// <summary>
        /// Enumerates the OPC 40001-1 machinery equipment of a machine.
        /// Returns nothing when the machine publishes no equipment folder.
        /// </summary>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<MachineryEquipmentItem> EnumerateEquipmentAsync(
            NodeId machine,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId folder = await ResolveMachineryChildAsync(
                machine,
                MachineryBrowseNames.MachineryEquipment,
                cancellationToken).ConfigureAwait(false);
            if (folder.IsNull)
            {
                yield break;
            }

            ushort machineryNamespaceIndex = NamespaceIndexOf(
                Namespaces.Machinery);
            ushort diNamespaceIndex = NamespaceIndexOf(Di.Namespaces.OpcUaDi);
            var typeIdName = new QualifiedName(
                MachineryBrowseNames.MachineryEquipmentTypeId,
                machineryNamespaceIndex);
            var descriptionName = new QualifiedName(
                MachineryBrowseNames.Description,
                machineryNamespaceIndex);
            var serialName = new QualifiedName(
                DiBrowseNames.SerialNumber,
                diNamespaceIndex);
            var lifeName = new QualifiedName(
                MachineryBrowseNames.EquipmentLife,
                machineryNamespaceIndex);
            var startName = new QualifiedName(DiBrowseNames.StartValue, diNamespaceIndex);
            var limitName = new QualifiedName(DiBrowseNames.LimitValue, diNamespaceIndex);

            var items = new List<MachineEntry>();
            await foreach (MachineEntry entry in EnumerateChildObjectsAsync(
                    folder,
                    cancellationToken).ConfigureAwait(false))
            {
                items.Add(entry);
            }

            foreach (MachineEntry entry in items)
            {
                ReadValueSet values = await ReadChildValuesAsync(
                    entry.NodeId,
                    [typeIdName, descriptionName, serialName, lifeName],
                    ReferenceTypeIds.HierarchicalReferences,
                    cancellationToken).ConfigureAwait(false);
                values.RequirePresent(typeIdName);

                MachineryLifetimeVariable? life = null;
                NodeId lifeNodeId = await ResolveChildAsync(
                    entry.NodeId,
                    lifeName,
                    cancellationToken).ConfigureAwait(false);
                if (!lifeNodeId.IsNull)
                {
                    ReadValueSet lifeValues = await ReadChildValuesAsync(
                        lifeNodeId,
                        [startName, limitName],
                        ReferenceTypeIds.HierarchicalReferences,
                        cancellationToken).ConfigureAwait(false);
                    life = new MachineryLifetimeVariable
                    {
                        Values = CombineEvidence(values.EvidenceOf(lifeName), lifeValues.Values),
                        NodeId = lifeNodeId,
                        BrowseName = lifeName,
                        Remaining = AsDouble(values, lifeName),
                        StartValue = AsDouble(lifeValues, startName),
                        LimitValue = AsDouble(lifeValues, limitName)
                    };
                }

                yield return new MachineryEquipmentItem
                {
                    Values = CombineEvidence(values.Values, life is { Values: var lifeFields } ? lifeFields : []),
                    NodeId = entry.NodeId,
                    BrowseName = entry.BrowseName,
                    MachineryEquipmentTypeId = AsString(values, typeIdName),
                    Description = AsLocalizedText(values, descriptionName),
                    SerialNumber = AsString(values, serialName),
                    EquipmentLife = life
                };
            }
        }

        /// <summary>
        /// Reads the Device Integration <c>DeviceHealth</c> a machinery item
        /// publishes below <c>Monitoring/Health</c>, or
        /// <see langword="null"/> when it publishes none.
        /// </summary>
        /// <param name="machineryItem">The machine or component to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<Di.DeviceHealthEnumeration?> ReadDeviceHealthAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            if (m_options is not null)
            {
                MachineryReadValue field = await ReadDeviceHealthValueAsync(machineryItem, cancellationToken)
                    .ConfigureAwait(false);
                return field.Value.WrappedValue.TryGetValue(out Di.DeviceHealthEnumeration healthValue)
                    ? healthValue
                    : null;
            }
            ushort machineryNamespaceIndex = NamespaceIndexOf(
                Namespaces.Machinery);
            ushort diNamespaceIndex = NamespaceIndexOf(Di.Namespaces.OpcUaDi);
            NodeId health = await ResolvePathAsync(
                machineryItem,
                cancellationToken,
                new QualifiedName(MachineryBrowseNames.Monitoring, machineryNamespaceIndex),
                new QualifiedName(MachineryBrowseNames.Health, machineryNamespaceIndex))
                .ConfigureAwait(false);
            if (health.IsNull)
            {
                return null;
            }

            var deviceHealthName = new QualifiedName(
                DiBrowseNames.DeviceHealth,
                diNamespaceIndex);
            ReadValueSet values = await ReadChildValuesAsync(
                health,
                [deviceHealthName],
                ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);
            if (!values.TryGetValue(deviceHealthName, out Variant value))
            {
                return null;
            }
            return value.TryGetValue(out Di.DeviceHealthEnumeration deviceHealth)
                ? deviceHealth
                : null;
        }

        /// <summary>
        /// Strictly reads DeviceHealth with source, quality, time and explicit
        /// optional absence. Does not synthesize a healthy value for a failed read.
        /// </summary>
        /// <param name="machineryItem">The selected machine or component.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <exception cref="MachineryReadException"></exception>
        public async ValueTask<MachineryReadValue> ReadDeviceHealthValueAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetNamespaceIndex(Namespaces.Machinery, out ushort machineryNamespaceIndex))
            {
                return MissingNamespaceField(machineryItem, Namespaces.Machinery);
            }
            if (!TryGetNamespaceIndex(Di.Namespaces.OpcUaDi, out ushort diNamespaceIndex))
            {
                throw ReadFailure(
                    StatusCodes.BadNotSupported,
                    UnresolvedField(machineryItem, [], StatusCodes.BadNotSupported),
                    "The Machinery namespace requires the Device Integration namespace.");
            }
            ArrayOf<MachineryReadValue> fields = await ReadStrictFieldsAsync(
                machineryItem,
                [
                    [
                        new QualifiedName(MachineryBrowseNames.Monitoring, machineryNamespaceIndex),
                        new QualifiedName(MachineryBrowseNames.Health, machineryNamespaceIndex),
                        new QualifiedName(DiBrowseNames.DeviceHealth, diNamespaceIndex)
                    ]
                ],
                ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);
            Variant value = fields[0].Value.WrappedValue;
            RequireFieldType(fields[0],
                (HasType(value, BuiltInType.Int32) || HasType(value, BuiltInType.Enumeration)) &&
                value.TryGetValue(out Di.DeviceHealthEnumeration _));
            return fields[0];
        }
    }
}
