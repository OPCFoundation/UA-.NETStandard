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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Pumps;
using Opc.Ua.Pumps.Client;
using PumpBrowseNames = Opc.Ua.Pumps.BrowseNames;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Bounded OPC 40223 inspection using the Pumps client. No pump actuation is exposed.
    /// </summary>
    internal sealed class PumpsCompanionProvider : ICompanionProvider
    {
        /// <summary>
        /// Creates the typed adapter with an optional client factory.
        /// </summary>
        public PumpsCompanionProvider(Func<CompanionContext, PumpsClient>? createClient = null)
        {
            m_createClient = createClient ??
                (static context =>
                    new PumpsClient(context.Session, context.Telemetry, context.MaxFields));
        }

        /// <inheritdoc/>
        public CompanionDescriptor Descriptor { get; } = new(
            "pumps", "Pumps", Opc.Ua.Pumps.Namespaces.Pumps,
            "Published OPC 40223 Pumps and Vacuum Pumps. Controls and actuation are observational.");

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            PumpsClient client = m_createClient(context);
            var targets = new List<CompanionTarget>();
            var visited = new HashSet<NodeId>();
            await foreach (PumpEntry entry in client.EnumeratePumpsAsync(lifetime.Token)
                .WithCancellation(lifetime.Token).ConfigureAwait(false))
            {
                CellCompanionSupport.AddTarget(targets, visited, new CompanionTarget(
                    Descriptor.Id, entry.NodeId,
                    entry.DisplayName.Text ?? entry.BrowseName.Name ?? "Pump", "Pump"), context.MaxTargets);
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return [.. targets];
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionInspection> InspectAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            CompanionOperationResult overview = await ReadSectionAsync(
                context, target, s_sections[0], cancellationToken).ConfigureAwait(false);
            return new CompanionInspection(
                overview.Values,
                s_sections.ConvertAll(static section =>
                    new CompanionOperation(section.Id, "Read " + section.Name, CompanionOperationSafety.ReadOnly)),
                overview.Summary);
        }

        /// <inheritdoc/>
        public ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, string? input,
            CancellationToken cancellationToken)
        {
            CellCompanionSupport.RequireNoInput(input);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (PumpSection section in s_sections)
            {
                if (section.Id == operationId)
                {
                    return ReadSectionAsync(context, target, section, cancellationToken);
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "The Pumps tool exposes only the listed read-only sections.");
        }

        private async ValueTask<CompanionOperationResult> ReadSectionAsync(
            CompanionContext context, CompanionTarget target, PumpSection section, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, target, Descriptor.Id, s_types, lifetime.Token).ConfigureAwait(false);
            PumpsClient client = m_createClient(context);
            NodeId group = section.DiGroup
                ? await client.ResolveDiGroupAsync(target.NodeId, section.Group, lifetime.Token).ConfigureAwait(false)
                : await client.ResolvePumpsChildAsync(target.NodeId, section.Group, lifetime.Token)
                    .ConfigureAwait(false);
            if (!group.IsNull && section.Child is not null)
            {
                group = await client.ResolvePumpsChildAsync(group, section.Child, lifetime.Token).ConfigureAwait(false);
            }
            if (group.IsNull)
            {
                return new CompanionOperationResult(
                    section.Name + " is not exposed by this pump.",
                    [new CompanionValue(section.Name, Variant.From("Not exposed"))]);
            }
            var fields = new List<CompanionValue>();
            if (section.Id == "ports")
            {
                var visited = new HashSet<NodeId>();
                await foreach (PumpEntry port in client.EnumeratePortEntriesAsync(target.NodeId, lifetime.Token)
                    .ConfigureAwait(false))
                {
                    if (!visited.Add(port.NodeId))
                    {
                        continue;
                    }
                    CellCompanionSupport.CheckCount(visited.Count, context.MaxTargets, "Pump ports");
                    PumpValueSet values = await client.ReadValueSetAsync(
                        port.NodeId, s_readOptions, lifetime.Token).ConfigureAwait(false);
                    AddValues(context, fields, values,
                        "Port " + (port.DisplayName.Text ?? port.BrowseName.Name ?? port.NodeId.ToString()),
                        lifetime.Token);
                }
            }
            else
            {
                PumpValueSet values = await client.ReadValueSetAsync(
                    group, s_readOptions, lifetime.Token).ConfigureAwait(false);
                AddValues(context, fields, values, section.Name, lifetime.Token);
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                $"Read {fields.Count} {section.Name.ToLowerInvariant()} value(s). " +
                "Only this section was read; no pump command or documentation download was started.",
                [.. fields]);
        }

        private static void AddValues(
            CompanionContext context, List<CompanionValue> fields, PumpValueSet values,
            string prefix, CancellationToken cancellationToken)
        {
            foreach (PumpValue value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CellCompanionSupport.CheckCount(fields.Count + 1, context.MaxFields, "Pump inspection");
                if (value.NodeId.IsNull ||
                    string.IsNullOrEmpty(value.Name) ||
                    value.Name.Length > 256 ||
                    (value.Value.TryGetValue(out string? text) && text is { Length: > 4096 }))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError, "The pump returned an invalid inspection value.");
                }
                string units = value.EngineeringUnits is { } unit
                    ? $"{unit.DisplayName.Text ?? unit.Description.Text ?? "Unit"} " +
                        $"({unit.NamespaceUri}, {unit.UnitId})"
                    : "units not exposed";
                string range = value.EuRange is { } limits
                    ? string.Create(CultureInfo.InvariantCulture, $"; range {limits.Low} .. {limits.High}")
                    : string.Empty;
                fields.Add(new CompanionValue(prefix + " / " + value.Name, value.Value)
                {
                    SourceNodeId = value.NodeId,
                    Quality = value.StatusCode,
                    SourceTimestamp = value.SourceTimestamp,
                    EngineeringMetadata = units + range
                });
            }
            foreach (KeyValuePair<string, PumpValueSet> child in values.Groups)
            {
                AddValues(context, fields, child.Value, prefix + " / " + child.Key, cancellationToken);
            }
        }

        private readonly Func<CompanionContext, PumpsClient> m_createClient;
        private static readonly PumpReadOptions s_readOptions = new() { IncludeTimestamps = true };

        private static readonly ArrayOf<IndustrialCompanionType> s_types =
        [
            new(Opc.Ua.Pumps.ObjectTypeIds.PumpType, "Pump")
        ];

        private static readonly ArrayOf<PumpSection> s_sections =
        [
            new("identification", "Identification", true, PumpsModel.DiGroups.Identification),
            new("configuration", "Configuration", true, PumpsModel.DiGroups.Configuration),
            new("measurements", "Measurements", true, PumpsModel.DiGroups.Operational, PumpBrowseNames.Measurements),
            new("signals", "Signals", true, PumpsModel.DiGroups.Operational, PumpBrowseNames.Signals),
            new("control", "Control state", true, PumpsModel.DiGroups.Operational, PumpBrowseNames.Control),
            new("actuation", "Actuation state", true, PumpsModel.DiGroups.Operational, PumpBrowseNames.PumpActuation),
            new("bypass", "Bypass state", true, PumpsModel.DiGroups.Operational, PumpBrowseNames.BypassActuation),
            new("throttle", "Throttle state", true, PumpsModel.DiGroups.Operational,
                PumpBrowseNames.ThrottleValveActuation),
            new("supervision", "Supervision", false, PumpBrowseNames.Events),
            new("maintenance", "Maintenance", true, PumpsModel.DiGroups.Maintenance),
            new("ports", "Ports", false, PumpBrowseNames.Ports),
            new("documentation", "Documentation links", false, PumpBrowseNames.Documentation)
        ];

        /// <summary>
        /// One explicitly requested, namespace-qualified section.
        /// </summary>
        private sealed record PumpSection(string Id, string Name, bool DiGroup, string Group, string? Child = null);
    }
}
