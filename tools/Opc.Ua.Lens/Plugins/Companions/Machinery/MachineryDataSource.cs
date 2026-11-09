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
    /// <summary>
    /// Reads only the active Machinery section through the bounded, evidenced client.
    /// The caller retains the connection and controls observation and operation lifetimes.
    /// </summary>
    internal sealed partial class MachineryDataSource : IMachineryDataSource
    {
        /// <inheritdoc/>
        public async Task<ArrayOf<MachineryMachine>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            MachineryClient client = CreateClient(context);
            var machines = new List<MachineryMachine>();
            var seen = new HashSet<NodeId>();
            await foreach (MachineEntry entry in client.EnumerateMachinesAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (!seen.Add(entry.NodeId))
                {
                    continue;
                }
                CellCompanionSupport.CheckCount(machines.Count + 1, context.MaxTargets, "Machines");
                machines.Add(new MachineryMachine(entry.NodeId, EntryName(entry), "Machine"));
            }
            if (await HasDirectManagementAsync(context, null, lifetime.Token).ConfigureAwait(false) &&
                seen.Add(ObjectIds.ObjectsFolder))
            {
                CellCompanionSupport.CheckCount(machines.Count + 1, context.MaxTargets, "Management sources");
                machines.Add(new MachineryMachine(
                    ObjectIds.ObjectsFolder, "Direct management objects", ManagementKind));
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return [.. machines];
        }

        /// <inheritdoc/>
        public async Task<MachineryPage> ReadAsync(
            CompanionContext context, MachineryMachine machine, MachinerySection section,
            int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(machine);
            ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(pageIndex, 1023);
            ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 32);
            using CancellationTokenSource lifetime = Begin(context, cancellationToken);
            var target = new CompanionTarget("machinery", machine.NodeId, machine.Name, machine.Kind);
            var binding = new CompanionOperationDraft(
                target, s_readSection, null, context.Session, DateTimeOffset.MaxValue);
            await RequireMachineAsync(context, machine, lifetime.Token).ConfigureAwait(false);
            MachineryClient client = CreateClient(context);
            MachineryPage page;
            if (machine.Kind == ManagementKind &&
                section is MachinerySection.ProcessValues or MachinerySection.Energy)
            {
                page = new MachineryPage([], [], false,
                    "Not exposed by this management-only source. Select a machine for process values or energy.");
            }
            else if (machine.Kind == ManagementKind &&
                section is MachinerySection.Jobs or MachinerySection.Results &&
                !await HasDirectManagementAsync(context, section, lifetime.Token).ConfigureAwait(false))
            {
                page = new MachineryPage([], [], false, "This direct management part is not exposed.");
            }
            else
            {
                page = section switch
                {
                    MachinerySection.Overview => await ReadOverviewAsync(context, client, machine, lifetime.Token)
                        .ConfigureAwait(false),
                    MachinerySection.ProcessValues => await ReadProcessValuesAsync(
                        context, client, machine, pageIndex, pageSize, lifetime.Token).ConfigureAwait(false),
                    MachinerySection.Jobs => await ReadJobsAsync(
                        context, client, machine, pageIndex, pageSize, lifetime.Token).ConfigureAwait(false),
                    MachinerySection.Energy => await ReadEnergyAsync(
                        context, client, machine, pageIndex, pageSize, lifetime.Token).ConfigureAwait(false),
                    MachinerySection.Results => await ReadResultsAsync(
                        context, client, machine, pageIndex, pageSize, lifetime.Token).ConfigureAwait(false),
                    _ => throw new ArgumentOutOfRangeException(nameof(section))
                };
            }
            lifetime.Token.ThrowIfCancellationRequested();
            if (!binding.MatchesSession(context.Session))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The Machinery connection changed.");
            }
            return page;
        }

        private static async Task<MachineryPage> ReadOverviewAsync(
            CompanionContext context, MachineryClient client, MachineryMachine machine,
            CancellationToken cancellationToken)
        {
            var fields = new List<CompanionValue> { new("Machine", Variant.From(machine.Name)) };
            if (machine.Kind == ManagementKind)
            {
                fields[0] = new CompanionValue("Source", Variant.From(machine.Name));
                fields.Add(new CompanionValue("Owner", Variant.From(machine.NodeId)));
                fields.Add(new CompanionValue("Available views", Variant.From("Jobs / Results when exposed")));
                return new MachineryPage([.. fields], [], false,
                    "Direct standard JobManagement/ResultManagement objects; this source is not a physical machine.");
            }
            MachineIdentification? identification = await client.ReadIdentificationAsync(
                machine.NodeId, cancellationToken).ConfigureAwait(false);
            AddSelected(fields, identification?.Values ?? [],
                ["Manufacturer", "Model", "SerialNumber", "ProductInstanceUri", "Location"]);
            MachineryReadValue health = await client.ReadDeviceHealthValueAsync(
                machine.NodeId, cancellationToken).ConfigureAwait(false);
            fields.Add(Present(health, "Device health"));
            MachineryStateSnapshot? state = await client.ReadItemStateAsync(
                machine.NodeId, cancellationToken).ConfigureAwait(false);
            AddSelected(fields, state?.Values ?? [], ["CurrentState"], "Item state");
            MachineryStateSnapshot? mode = await client.ReadOperationModeAsync(
                machine.NodeId, cancellationToken).ConfigureAwait(false);
            AddSelected(fields, mode?.Values ?? [], ["CurrentState"], "Operation mode");
            MachineryOperationCounters? counters = await client.ReadOperationCountersAsync(
                machine.NodeId, cancellationToken).ConfigureAwait(false);
            AddSelected(fields, counters?.Values ?? [],
                ["PowerOnDuration", "OperationDuration", "OperationCycleCounter"]);
            CellCompanionSupport.CheckCount(fields.Count, Math.Min(16, context.MaxFields), "Overview fields");
            return new MachineryPage([.. fields], [], false,
                "Overview read. Jobs, process-value lists, energy topology and results remain unloaded.");
        }

        private static async Task RequireMachineAsync(
            CompanionContext context, MachineryMachine machine, CancellationToken cancellationToken)
        {
            if (machine.Kind == ManagementKind && machine.NodeId == ObjectIds.ObjectsFolder)
            {
                if (!await HasDirectManagementAsync(context, null, cancellationToken).ConfigureAwait(false))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The direct management objects are no longer exposed.");
                }
                return;
            }
            if (machine.Kind != "Machine" || machine.NodeId.IsNull)
            {
                throw new ArgumentException("Select a discovered machine.", nameof(machine));
            }
            DataValue value = await CompanionMethodMetadata.ReadOneAsync(
                context, machine.NodeId, Attributes.NodeClass, cancellationToken).ConfigureAwait(false);
            if (!value.WrappedValue.TryGetValue(out int nodeClass) || nodeClass != (int)NodeClass.Object)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A machine must be an Object.");
            }
            NodeId folder = CreateClient(context).MachinesFolderId;
            var budget = new IndustrialBrowseBudget(context);
            await foreach (ReferenceDescription parent in IndustrialCompanionAccess.BrowseAsync(
                context, machine.NodeId, BrowseDirection.Inverse, ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
            {
                if (IndustrialCompanionAccess.LocalId(context, parent.NodeId) == folder)
                {
                    return;
                }
            }
            throw new ServiceResultException(StatusCodes.BadInvalidState,
                "The selected object is no longer organized by Machines.");
        }

        private static async Task<bool> HasDirectManagementAsync(
            CompanionContext context, MachinerySection? section, CancellationToken cancellationToken)
        {
            return (section != MachinerySection.Results &&
                await HasManagementAsync(Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs,
                    "JobManagement", Opc.Ua.Machinery.Jobs.ObjectTypeIds.JobManagementType).ConfigureAwait(false)) ||
                (section != MachinerySection.Jobs &&
                    await HasManagementAsync(Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                        "ResultManagement", Opc.Ua.Machinery.Result.ObjectTypeIds.ResultManagementType)
                        .ConfigureAwait(false));

            async Task<bool> HasManagementAsync(string model, string name, ExpandedNodeId typeId)
            {
                if (context.Session.NamespaceUris.GetIndex(model) < 0)
                {
                    return false;
                }
                NodeId node = await IndustrialCompanionAccess.ResolveChildAsync(
                    context, ObjectIds.ObjectsFolder, model, name, true, cancellationToken).ConfigureAwait(false);
                if (node.IsNull)
                {
                    return false;
                }
                await IndustrialCompanionAccess.RequireTargetAsync(context,
                    new CompanionTarget("machinery", node, name, name), "machinery",
                    [new(typeId, name)], cancellationToken).ConfigureAwait(false);
                return true;
            }
        }

        private static void AddSelected(
            List<CompanionValue> destination, ArrayOf<MachineryReadValue> values,
            ArrayOf<string> names, string? label = null)
        {
            foreach (MachineryReadValue value in values)
            {
                if (value.BrowsePath.Count == 0)
                {
                    continue;
                }
                string? name = value.BrowsePath[^1].Name;
                if (name is not null && names.Contains(name))
                {
                    destination.Add(Present(value, label ?? name));
                }
            }
        }

        private static CompanionValue Present(MachineryReadValue value, string? name = null)
        {
            return Present(value, value.Value.WrappedValue, name);
        }

        private static CompanionValue Present(MachineryReadValue value, Variant displayed, string? name)
        {
            string fieldName = name ?? string.Join(" / ", value.BrowsePath.ToList().Select(part => part.Name));
            var field = new CompanionValue(fieldName, value.IsAbsent
                ? Variant.From("Not exposed") : displayed)
            {
                SourceNodeId = value.SourceNodeId,
                Quality = value.Value.StatusCode,
                SourceTimestamp = value.Value.SourceTimestamp.IsNull ? null : value.Value.SourceTimestamp.ToDateTime()
            };
            if (field.Name.Length > 256 || field.Text.Length > 4096)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "A Machinery value exceeds the bounded display size.");
            }
            return field;
        }

        private static string EntryName(MachineEntry entry)
        {
            string name = entry.DisplayName.Text ?? entry.BrowseName.Name ?? entry.NodeId.ToString();
            if (entry.NodeId.IsNull || name.Length > 256)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "A Machinery entry has an invalid identity or oversized name.");
            }
            return name;
        }

        private static string Details(ArrayOf<MachineryReadValue> values)
        {
            string text = string.Join(Environment.NewLine, values.ToList().Select(value =>
            {
                CompanionValue field = Present(value);
                return $"{field.Name}: {field.Text}\n{field.Evidence}";
            }));
            if (text.Length > 8192)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "Selected Machinery details exceed 8192 characters.");
            }
            return text;
        }

        private static MachineryClient CreateClient(CompanionContext context)
        {
            return new(context.Session, context.Telemetry, s_limits);
        }

        private static CancellationTokenSource Begin(CompanionContext context, CancellationToken cancellationToken)
        {
            return CellCompanionSupport.BeginOperation(context, Opc.Ua.Machinery.Namespaces.Machinery, cancellationToken);
        }

        private static readonly MachineryClientOptions s_limits = new()
        {
            MaximumBrowseReferences = 1024,
            MaximumBrowsePages = 128,
            MaximumReadArrayItems = 1024
        };

        private static readonly CompanionOperation s_readSection =
            new("read-section", "Read Machinery section", CompanionOperationSafety.ReadOnly);

        private const string ManagementKind = "Management owner";
    }
}
