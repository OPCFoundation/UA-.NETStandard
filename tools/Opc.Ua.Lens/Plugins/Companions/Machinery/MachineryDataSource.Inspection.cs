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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Machinery.Client;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Plugins.Companions.Machinery
{
    internal sealed partial class MachineryDataSource
    {
        /// <inheritdoc/>
        public async Task<ArrayOf<CompanionValue>> InspectAsync(
            CompanionContext context, MachineryMachine machine, MachinerySection section,
            MachineryItem item, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(machine);
            ArgumentNullException.ThrowIfNull(item);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            var binding = new CompanionOperationDraft(
                new CompanionTarget("machinery", machine.NodeId, machine.Name, machine.Kind),
                s_readSection, null, context.Session, DateTimeOffset.MaxValue);
            await RequireMachineAsync(context, machine, lifetime.Token).ConfigureAwait(false);
            if (machine.Kind == ManagementKind &&
                (section is MachinerySection.ProcessValues or MachinerySection.Energy ||
                    (section != MachinerySection.Overview &&
                        !await HasDirectManagementAsync(context, section, lifetime.Token).ConfigureAwait(false))))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The selected section is not exposed by this management-only source.");
            }
            MachineryClient client = CreateClient(context);
            var values = new List<CompanionValue>();
            switch (section)
            {
                case MachinerySection.Overview:
                    if (item.NodeId != machine.NodeId)
                    {
                        throw new ArgumentException("Select the current machine's details.", nameof(item));
                    }
                    if (machine.Kind == ManagementKind)
                    {
                        values.Add(new CompanionValue("Owner", Variant.From(machine.NodeId)));
                        values.Add(new CompanionValue(
                            "Source", Variant.From("Direct JobManagement / ResultManagement")));
                        break;
                    }
                    MachineIdentification? identity = await client.ReadIdentificationAsync(
                        machine.NodeId, lifetime.Token)
                        .ConfigureAwait(false);
                    AddEvidence(values, identity?.Values ?? []);
                    MachineryOperationCounters? counters = await client.ReadOperationCountersAsync(
                        machine.NodeId, lifetime.Token).ConfigureAwait(false);
                    AddEvidence(values, counters?.Values ?? []);
                    await foreach (MachineEntry component in client.EnumerateComponentsAsync(
                        machine.NodeId, lifetime.Token).ConfigureAwait(false))
                    {
                        values.Add(new CompanionValue(
                            "Component: " + EntryName(component), Variant.From(component.NodeId)));
                        CellCompanionSupport.CheckCount(values.Count, context.MaxFields, "Machine details");
                    }
                    break;
                case MachinerySection.ProcessValues:
                    await RequireEntryAsync(
                        client.EnumerateProcessValuesAsync(machine.NodeId, lifetime.Token),
                        item.NodeId, lifetime.Token)
                        .ConfigureAwait(false);
                    MachineryProcessValue process = await client.ReadProcessValueAsync(item.NodeId, lifetime.Token)
                        .ConfigureAwait(false) ??
                        throw new ServiceResultException(StatusCodes.BadNotFound);
                    AddEvidence(values, process.Values);
                    break;
                case MachinerySection.Energy:
                    await RequireEntryAsync(EnumerateMetersAsync(client, machine.NodeId, lifetime.Token),
                        item.NodeId, lifetime.Token).ConfigureAwait(false);
                    MachineryMeteringPoint meter = await client.ReadMeteringPointAsync(item.NodeId, lifetime.Token)
                        .ConfigureAwait(false) ??
                        throw new ServiceResultException(StatusCodes.BadNotFound);
                    AddEvidence(values, meter.Values);
                    await foreach (MachineEntry child in client.EnumerateSubMetersAsync(
                        item.NodeId, lifetime.Token).ConfigureAwait(false))
                    {
                        values.Add(new CompanionValue("Sub-meter: " + EntryName(child), Variant.From(child.NodeId)));
                        CellCompanionSupport.CheckCount(values.Count, context.MaxFields, "Meter details");
                    }
                    break;
                case MachinerySection.Jobs:
                    MachineryJobOrdersSnapshot jobs = await client.ReadJobOrdersSnapshotAsync(
                        machine.NodeId, lifetime.Token).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(item.ItemId) || jobs.Values[0].SourceNodeId != item.NodeId)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState, "The job-list source changed.");
                    }
                    var selected = jobs.JobOrders.ToList()
                        .Where(order => order.JobOrder.JobOrderID == item.ItemId).ToList();
                    if (selected.Count != 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "The selected job is absent or its identifier is ambiguous.");
                    }
                    CompanionValue list = Present(
                        jobs.Values[0], Variant.FromStructure(selected[0].JobOrder), "Job order");
                    values.Add(list);
                    values.Add(list with
                    {
                        Name = "Execution state",
                        Value = Variant.FromStructure(selected[0].State)
                    });
                    break;
                case MachinerySection.Results:
                    await RequireEntryAsync(
                        client.EnumeratePublishedResultsAsync(machine.NodeId, lifetime.Token),
                        item.NodeId, lifetime.Token).ConfigureAwait(false);
                    MachineryResultSnapshot result = await client.ReadPublishedResultAsync(item.NodeId, lifetime.Token)
                        .ConfigureAwait(false);
                    foreach (MachineryReadValue evidence in result.Values)
                    {
                        values.Add(Present(evidence, "Published result"));
                    }
                    if (result.Result is not null)
                    {
                        string? id = result.Result.ResultMetaData?.ResultId;
                        if (string.IsNullOrWhiteSpace(id) || id.Length > 256)
                        {
                            throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                                "A transferable result requires a bounded, nonempty ResultId.");
                        }
                        values.Add(new CompanionValue("Result ID", Variant.From(id)));
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section));
            }
            CellCompanionSupport.CheckCount(values.Count, context.MaxFields, "Machinery details");
            if (values.Any(value => value.Name.Length > 256 || value.Text.Length > 4096))
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "A Machinery detail exceeds its display limit.");
            }
            lifetime.Token.ThrowIfCancellationRequested();
            if (!binding.MatchesSession(context.Session))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The Machinery connection changed.");
            }
            return [.. values];
        }

        private static void AddEvidence(List<CompanionValue> values, ArrayOf<MachineryReadValue> evidence)
        {
            foreach (MachineryReadValue value in evidence)
            {
                values.Add(Present(value));
            }
        }

        private static async Task RequireEntryAsync(
            IAsyncEnumerable<MachineEntry> entries, NodeId selected, CancellationToken cancellationToken)
        {
            int visited = 0;
            await foreach (MachineEntry entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                CellCompanionSupport.CheckCount(++visited, 1024, "Section entries");
                if (entry.NodeId == selected)
                {
                    return;
                }
            }
            throw new ServiceResultException(StatusCodes.BadInvalidState,
                "The selected item no longer belongs to this machine section.");
        }
    }
}
