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
using EcmBrowseNames = Opc.Ua.ECM.BrowseNames;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// Resolves a machinery item's <c>Monitoring/Consumption</c> folder,
        /// the entry point OPC 40001-4 puts energy information under, or
        /// <see cref="NodeId.Null"/> when the item publishes none.
        /// </summary>
        /// <param name="machineryItem">The machine or component to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolveConsumptionAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            ushort machineryNamespaceIndex = NamespaceIndexOf(
                Opc.Ua.Machinery.Namespaces.Machinery);
            return ResolvePathAsync(
                machineryItem,
                cancellationToken,
                new QualifiedName(MachineryBrowseNames.Monitoring, machineryNamespaceIndex),
                new QualifiedName(MachineryBrowseNames.Consumption, machineryNamespaceIndex));
        }

        /// <summary>
        /// Enumerates the OPC 40001-4 resource folders a machinery item
        /// publishes — one per energy resource it meters.
        /// </summary>
        /// <param name="machineryItem">The machine or component to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<MachineEntry> EnumerateEnergyResourcesAsync(
            NodeId machineryItem,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId consumption = await ResolveConsumptionAsync(machineryItem, cancellationToken)
                .ConfigureAwait(false);
            await foreach (MachineEntry entry in EnumerateChildObjectsAsync(
                    consumption,
                    cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        /// <summary>
        /// Enumerates every metering point of an OPC 40001-4 resource folder:
        /// <c>Main</c> and the further points the resource publishes next to
        /// it.
        /// </summary>
        /// <param name="resource">The resource folder to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public IAsyncEnumerable<MachineEntry> EnumerateMeteringPointsAsync(
            NodeId resource,
            CancellationToken cancellationToken = default)
        {
            return EnumerateChildObjectsAsync(resource, cancellationToken);
        }

        /// <summary>
        /// Enumerates the sub-meters of a metering point — the points it
        /// <c>Contains</c>.
        /// </summary>
        /// <remarks>
        /// OPC 40001-4 §8.1 runs <c>Contains</c> from a metering point to a
        /// point whose measurements are part of it, typically from <c>Main</c>
        /// to the points below the same resource. The reference is followed
        /// forward and with its subtypes, so a vendor refinement of it is
        /// found too; a point that contains nothing yields nothing.
        /// </remarks>
        /// <param name="meteringPoint">The containing metering point.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public IAsyncEnumerable<MachineEntry> EnumerateSubMetersAsync(
            NodeId meteringPoint,
            CancellationToken cancellationToken = default)
        {
            var contains = new NodeId(
                Opc.Ua.Machinery.Energy.ReferenceTypes.Contains,
                NamespaceIndexOf(Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy));
            return BrowseChildrenAsync(
                meteringPoint,
                NodeClass.Object,
                contains,
                cancellationToken);
        }

        /// <summary>
        /// Reads every sub-meter a metering point <c>Contains</c>, the same
        /// way <see cref="ReadMeteringPointAsync"/> reads one point.
        /// </summary>
        /// <param name="meteringPoint">The containing metering point.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<MachineryMeteringPoint> ReadSubMetersAsync(
            NodeId meteringPoint,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (MachineEntry subMeter in EnumerateSubMetersAsync(
                    meteringPoint,
                    cancellationToken).ConfigureAwait(false))
            {
                MachineryMeteringPoint? point = await ReadMeteringPointAsync(
                    subMeter.NodeId,
                    cancellationToken).ConfigureAwait(false);
                if (point != null)
                {
                    yield return point;
                }
            }
        }

        /// <summary>
        /// Reads the <c>Main</c> metering point of a resource, or
        /// <see langword="null"/> when the resource publishes none.
        /// </summary>
        /// <remarks>
        /// OPC 40001-4 requires a <c>Main</c> point per resource folder, so a
        /// null here means the server is not conformant rather than that the
        /// feature is absent.
        /// </remarks>
        /// <param name="resource">The resource folder to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MachineryMeteringPoint?> ReadMainMeteringPointAsync(
            NodeId resource,
            CancellationToken cancellationToken = default)
        {
            ushort energyNamespaceIndex = NamespaceIndexOf(
                Opc.Ua.Machinery.Energy.Namespaces.MachineryEnergy);
            NodeId main = await ResolveChildAsync(
                resource,
                new QualifiedName(
                    Opc.Ua.Machinery.Energy.BrowseNames.Main,
                    energyNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            return main.IsNull
                ? null
                : await ReadMeteringPointAsync(main, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads one OPC 34100 metering point: its application tag and every
        /// measurement value below it.
        /// </summary>
        /// <param name="meteringPoint">The metering point to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MachineryMeteringPoint?> ReadMeteringPointAsync(
            NodeId meteringPoint,
            CancellationToken cancellationToken = default)
        {
            if (meteringPoint.IsNull)
            {
                return null;
            }
            ushort ecmNamespaceIndex = NamespaceIndexOf(Opc.Ua.ECM.Namespaces.ECM);
            var applicationTagName = new QualifiedName(
                EcmBrowseNames.ApplicationTag,
                ecmNamespaceIndex);

            Dictionary<QualifiedName, Variant> values = await ReadChildValuesAsync(
                meteringPoint,
                [applicationTagName],
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);

            var names = new List<QualifiedName>();
            var measurements = new List<MachineryMeasurementValue>();
            await foreach (MachineEntry child in EnumerateChildVariablesAsync(
                    meteringPoint,
                    cancellationToken).ConfigureAwait(false))
            {
                if (child.BrowseName != applicationTagName)
                {
                    names.Add(child.BrowseName);
                }
            }
            if (names.Count > 0)
            {
                Dictionary<QualifiedName, Variant> readings = await ReadChildValuesAsync(
                    meteringPoint,
                    names,
                    Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    cancellationToken).ConfigureAwait(false);
                foreach (QualifiedName name in names)
                {
                    if (readings.TryGetValue(name, out Variant reading))
                    {
                        measurements.Add(new MachineryMeasurementValue(name, reading));
                    }
                }
            }

            return new MachineryMeteringPoint(
                meteringPoint,
                AsString(values, applicationTagName),
                measurements.ToArrayOf());
        }
    }
}
