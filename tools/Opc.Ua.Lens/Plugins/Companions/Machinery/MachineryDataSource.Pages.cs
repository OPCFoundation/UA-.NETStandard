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
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Machinery.Client;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Plugins.Companions.Machinery
{
    internal sealed partial class MachineryDataSource
    {
        private static Task<MachineryPage> ReadProcessValuesAsync(
            CompanionContext context, MachineryClient client, MachineryMachine machine,
            int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            return ReadEntryPageAsync(
                client.EnumerateProcessValuesAsync(machine.NodeId, cancellationToken), pageIndex, pageSize,
                async entry =>
                {
                    MachineryProcessValue value = await client.ReadProcessValueAsync(entry.NodeId, cancellationToken)
                        .ConfigureAwait(false) ??
                        throw new ServiceResultException(
                            StatusCodes.BadNotFound, "The selected process value has no AnalogSignal.");
                    CellCompanionSupport.CheckCount(value.Values.Count, context.MaxFields, "Process-value fields");
                    MachineryReadValue reading = value.Values.ToList().First(field =>
                        field.SourceNodeId == value.SignalNodeId && !field.IsAbsent);
                    CompanionValue signal = Present(reading, EntryName(entry)) with
                    {
                        EngineeringMetadata = value.EngineeringUnits?.DisplayName.Text
                    };
                    string units = signal.EngineeringMetadata ?? "units not exposed";
                    return new MachineryItem(entry.NodeId, signal.Name,
                        $"{signal.Text} {units}; {signal.Evidence}", Details(value.Values))
                    {
                        SourceNodeId = value.SignalNodeId,
                        Values = [signal]
                    };
                }, "Process values", cancellationToken);
        }

        private static async Task<MachineryPage> ReadJobsAsync(
            CompanionContext context, MachineryClient client, MachineryMachine machine,
            int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            MachineryJobOrdersSnapshot snapshot = await client.ReadJobOrdersSnapshotAsync(
                machine.NodeId, cancellationToken).ConfigureAwait(false);
            MachineryReadValue evidence = snapshot.Values[0];
            if (evidence.IsAbsent)
            {
                return new MachineryPage([], [], false, "Job management is not exposed.");
            }
            int start = checked(pageIndex * pageSize);
            var items = new List<MachineryItem>();
            for (int index = start; index < snapshot.JobOrders.Count && items.Count < pageSize; index++)
            {
                Opc.Ua.ISA95.JobControl.V2.ISA95JobOrderAndStateDataType order = snapshot.JobOrders[index];
                string? id = order.JobOrder.JobOrderID;
                if (string.IsNullOrWhiteSpace(id) || id.Length > 256)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "A job identifier is empty or exceeds 256 characters.");
                }
                CellCompanionSupport.CheckCount(order.State.Count, context.MaxFields, "Job states");
                string state = string.Join(", ", order.State.ToList()
                    .Select(value => value.StateNumber.ToString(CultureInfo.InvariantCulture)));
                CompanionValue value = Present(evidence, Variant.FromStructure(order), "Job order and state");
                items.Add(new MachineryItem(evidence.SourceNodeId, id,
                    state.Length == 0 ? "No state is published." : "State numbers: " + state,
                    value.Evidence)
                {
                    ItemId = id,
                    SourceNodeId = evidence.SourceNodeId,
                    Values = [value]
                });
            }
            return new MachineryPage([], [.. items], start + items.Count < snapshot.JobOrders.Count,
                evidence.Value.WrappedValue.IsNull
                    ? "JobOrderList is Good-null." : "Published ISA-95 V2 job orders. No job command was sent.");
        }

        private static Task<MachineryPage> ReadEnergyAsync(
            CompanionContext context, MachineryClient client, MachineryMachine machine,
            int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            return ReadEntryPageAsync(
                EnumerateMetersAsync(client, machine.NodeId, cancellationToken), pageIndex, pageSize,
                entry => Task.FromResult(new MachineryItem(entry.NodeId, EntryName(entry),
                    "Energy metering point", $"Type: {entry.TypeDefinitionId}")),
                "Energy meters", cancellationToken);
        }

        private static async IAsyncEnumerable<MachineEntry> EnumerateMetersAsync(
            MachineryClient client, NodeId machine, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            int resources = 0;
            await foreach (MachineEntry resource in client.EnumerateEnergyResourcesAsync(machine, cancellationToken)
                .ConfigureAwait(false))
            {
                CellCompanionSupport.CheckCount(++resources, 128, "Energy carriers");
                string carrier = EntryName(resource);
                await foreach (MachineEntry meter in client.EnumerateMeteringPointsAsync(
                    resource.NodeId, cancellationToken).ConfigureAwait(false))
                {
                    yield return meter with { DisplayName = new LocalizedText(carrier + " / " + EntryName(meter)) };
                }
            }
        }

        private static Task<MachineryPage> ReadResultsAsync(
            CompanionContext context, MachineryClient client, MachineryMachine machine,
            int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            return ReadEntryPageAsync(
                client.EnumeratePublishedResultsAsync(machine.NodeId, cancellationToken), pageIndex, pageSize,
                entry => Task.FromResult(new MachineryItem(entry.NodeId, EntryName(entry),
                    "Published result Variable", $"Type: {entry.TypeDefinitionId}")
                {
                    SourceNodeId = entry.NodeId
                }), "Published results", cancellationToken);
        }

        private static async Task<MachineryPage> ReadEntryPageAsync(
            IAsyncEnumerable<MachineEntry> entries, int pageIndex, int pageSize,
            Func<MachineEntry, Task<MachineryItem>> read, string section, CancellationToken cancellationToken)
        {
            int start = checked(pageIndex * pageSize);
            int visited = 0;
            int offset = 0;
            var seen = new HashSet<NodeId>();
            var items = new List<MachineryItem>();
            await foreach (MachineEntry entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                CellCompanionSupport.CheckCount(++visited, 1024, "Section entries");
                if (!seen.Add(entry.NodeId))
                {
                    continue;
                }
                if (offset++ < start)
                {
                    continue;
                }
                if (items.Count == pageSize)
                {
                    return new MachineryPage([], [.. items], true, section + " page read.");
                }
                MachineryItem item = await read(entry).ConfigureAwait(false);
                if (item.Name.Length > 256 || item.Summary.Length > 4096 || item.Details.Length > 8192)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The Machinery row exceeds its bounded display size.");
                }
                items.Add(item);
            }
            return new MachineryPage([], [.. items], false,
                items.Count == 0
                    ? "No " + section.ToLowerInvariant() + " are exposed on this page."
                    : section + " page read.");
        }
    }
}
