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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AMB.Client;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Inspects actual local AMB aliases with bounded, quality-preserving client reads.
    /// </summary>
    internal sealed partial class AmbCompanionProvider : IPreparedCompanionProvider
    {
        /// <inheritdoc/>
        public CompanionDescriptor Descriptor { get; } = new(
            "amb", "Asset Management", Opc.Ua.AMB.Namespaces.AMB,
            "Published OPC 10000-110 Asset Management Basics 1.01.1. Current health and maintenance, not history.");

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            AmbClient client = CreateClient(context);
            var targets = new List<CompanionTarget>();
            var seen = new HashSet<NodeId>();
            await foreach (AssetAlias alias in client.EnumerateAssetsAsync(
                AssetAliasCategory.Assets, lifetime.Token).ConfigureAwait(false))
            {
                for (int index = 0; index < alias.ReferencedNodes.Count; index++)
                {
                    ExpandedNodeId reference = alias.ReferencedNodes[index];
                    if (reference.ServerIndex != 0)
                    {
                        continue;
                    }
                    NodeId node = ExpandedNodeId.ToNodeId(reference, context.Session.NamespaceUris);
                    if (node.IsNull)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid, "An asset alias has no resolvable local target.");
                    }
                    CellCompanionSupport.AddTarget(targets, seen,
                        new CompanionTarget(Descriptor.Id, node, alias.Name, AssetKind), context.MaxTargets);
                }
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return [.. targets];
        }

        /// <inheritdoc/>
        public async ValueTask<CompanionInspection> InspectAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            CompanionOperationResult result = await ReadSectionAsync(
                context, target, "identification", cancellationToken).ConfigureAwait(false);
            (ArrayOf<CompanionOperation> operations, string status) = await ReadTaskOperationsAsync(
                context, target, cancellationToken).ConfigureAwait(false);
            return new CompanionInspection(result.Values, operations, result.Summary + " " + status);
        }

        /// <inheritdoc/>
        public ValueTask<CompanionOperationResult> ExecuteAsync(
            CompanionContext context, CompanionTarget target, string operationId, string? input,
            CancellationToken cancellationToken)
        {
            CellCompanionSupport.RequireNoInput(input);
            cancellationToken.ThrowIfCancellationRequested();
            if (!s_readOperations.Contains(operation => operation.Id == operationId))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Select a supported asset task.");
            }
            return ReadSectionAsync(context, target, operationId, cancellationToken);
        }

        private async ValueTask<CompanionOperationResult> ReadSectionAsync(
            CompanionContext context, CompanionTarget target, string section, CancellationToken cancellationToken)
        {
            using CancellationTokenSource lifetime =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            AmbClient client = CreateClient(context);
            await RequireAssetAsync(context, client, target, lifetime.Token).ConfigureAwait(false);
            var fields = new List<CompanionValue>();
            switch (section)
            {
                case "identification":
                    AssetIdentificationRecord identification = await client.ReadIdentificationAsync(
                        target.NodeId, lifetime.Token).ConfigureAwait(false);
                    AddValues(context, fields, "Identification", identification.Values);
                    AddValue(context, fields, "Health", await client.ReadDeviceHealthValueAsync(
                        target.NodeId, lifetime.Token).ConfigureAwait(false));
                    break;
                case "health":
                    ArrayOf<AssetAlarmRecord> alarms = await client.ReadHealthAlarmsAsync(
                        target.NodeId, lifetime.Token).ConfigureAwait(false);
                    CellCompanionSupport.CheckCount(alarms.Count, context.MaxTargets, "Asset health conditions");
                    for (int index = 0; index < alarms.Count; index++)
                    {
                        AddValues(context, fields, "Health " +
                            (alarms[index].ConditionName ?? (index + 1).ToString(CultureInfo.InvariantCulture)),
                            alarms[index].Values);
                    }
                    break;
                case "maintenance":
                    ArrayOf<MaintenanceActivityRecord> activities = await client.ReadMaintenanceActivitiesAsync(
                        target.NodeId, lifetime.Token).ConfigureAwait(false);
                    CellCompanionSupport.CheckCount(activities.Count, context.MaxTargets, "Maintenance activities");
                    for (int index = 0; index < activities.Count; index++)
                    {
                        AddValues(context, fields,
                            "Maintenance " +
                            (activities[index].ConditionName ?? (index + 1).ToString(CultureInfo.InvariantCulture)),
                            activities[index].Values);
                    }
                    break;
                case "documentation":
                    ArrayOf<DocumentationLinkRecord> links = await client.ReadDocumentationLinksAsync(
                        target.NodeId, lifetime.Token).ConfigureAwait(false);
                    CellCompanionSupport.CheckCount(links.Count, context.MaxTargets, "Documentation links");
                    for (int index = 0; index < links.Count; index++)
                    {
                        AddValues(context, fields, "Link " +
                            (links[index].BrowseName.Name ?? (index + 1).ToString(CultureInfo.InvariantCulture)),
                            links[index].Values);
                    }
                    break;
                case "locations":
                    AssetContextRecord location = await client.ReadContextAsync(
                        target.NodeId, lifetime.Token).ConfigureAwait(false);
                    AddValues(context, fields, "Location", location.Values);
                    CellCompanionSupport.CheckCount(location.Classifications.Count, context.MaxTargets, "Classifications");
                    foreach (ExpandedNodeId entry in location.Classifications)
                    {
                        AddText(context, fields, "Classification", entry.ToString());
                    }
                    ArrayOf<AssetLocationRecord> locations = await client.ReadLocationsOfAssetAsync(
                        target.NodeId, lifetime.Token).ConfigureAwait(false);
                    CellCompanionSupport.CheckCount(locations.Count, context.MaxTargets, "Asset locations");
                    foreach (AssetLocationRecord item in locations)
                    {
                        AddText(context, fields, item.Kind + " location", item.Location.ToString());
                    }
                    break;
                case "sub-assets":
                    await foreach (ExpandedNodeId child in client.EnumerateSubAssetsAsync(target.NodeId, lifetime.Token)
                        .ConfigureAwait(false))
                    {
                        CellCompanionSupport.CheckCount(fields.Count + 1, context.MaxTargets, "Sub-assets");
                        AddText(context, fields, child.ServerIndex == 0 ? "Sub-asset" : "Remote sub-asset (not followed)",
                            child.ToString());
                    }
                    break;
                case "relations":
                    ArrayOf<AssetRelation> relations = await client.ReadRelationsAsync(target.NodeId, lifetime.Token)
                        .ConfigureAwait(false);
                    CellCompanionSupport.CheckCount(relations.Count, context.MaxTargets, "Asset relations");
                    foreach (AssetRelation relation in relations)
                    {
                        AddText(context, fields,
                            (relation.IsInverse ? "Inverse " : "Forward ") + relation.ReferenceTypeId, relation.Target.ToString());
                    }
                    break;
                case "requirements":
                case "capabilities":
                    ArrayOf<AssetEntryRecord> entries = await client.ReadEntriesAsync(
                        target.NodeId, section == "requirements" ? AssetEntryFolder.Requirements :
                            AssetEntryFolder.Capabilities, lifetime.Token).ConfigureAwait(false);
                    CellCompanionSupport.CheckCount(entries.Count, context.MaxTargets, "Asset entries");
                    for (int index = 0; index < entries.Count; index++)
                    {
                        if (entries[index].ReadValue is { } value)
                        {
                            AddValue(context, fields, section, value);
                        }
                    }
                    break;
                case "versions":
                    AddText(context, fields, "Product-instance alias version",
                        await client.ReadNodeVersionAsync(AssetAliasCategory.ByProductInstanceUri, lifetime.Token)
                            .ConfigureAwait(false));
                    AddText(context, fields, "Asset-ID alias version",
                        await client.ReadNodeVersionAsync(AssetAliasCategory.ByAssetId, lifetime.Token)
                            .ConfigureAwait(false));
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "The asset section is not supported.");
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                $"Read {fields.Count} {section} field(s). No link content, history or device operation was requested.",
                [.. fields]);
        }

        private async Task RequireAssetAsync(
            CompanionContext context, AmbClient client, CompanionTarget target, CancellationToken cancellationToken)
        {
            CellCompanionSupport.ValidateTarget(context, target, Descriptor.Id);
            if (target.TypeName != AssetKind)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Select a discovered manageable asset.");
            }
            bool found = false;
            await foreach (AssetAlias alias in client.EnumerateAssetsAsync(
                AssetAliasCategory.ByProductInstanceUri, cancellationToken).ConfigureAwait(false))
            {
                for (int index = 0; index < alias.ReferencedNodes.Count; index++)
                {
                    ExpandedNodeId node = alias.ReferencedNodes[index];
                    if (node.ServerIndex == 0 &&
                        ExpandedNodeId.ToNodeId(node, context.Session.NamespaceUris) == target.NodeId)
                    {
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    break;
                }
            }
            if (!found)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, "The selected asset is no longer in the local AMB alias category.");
            }
            ReadResponse response = await context.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = target.NodeId, AttributeId = Attributes.NodeClass }],
                cancellationToken).ConfigureAwait(false);
            if (response.ResponseHeader is null || response.Results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The asset class read is incomplete.");
            }
            if (!StatusCode.IsGood(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(response.ResponseHeader.ServiceResult);
            }
            DataValue value = response.Results[0];
            if (!StatusCode.IsGood(value.StatusCode))
            {
                throw new AmbValueReadException(value.StatusCode,
                    new AssetReadValue(target.NodeId, [], Attributes.NodeClass, in value));
            }
            if (value.WrappedValue.TypeInfo.BuiltInType != BuiltInType.Int32 ||
                !value.WrappedValue.TryGetValue(out int nodeClass) ||
                nodeClass != (int)NodeClass.Object)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The alias does not identify a readable local Object.");
            }
        }

        private static AmbClient CreateClient(CompanionContext context)
        {
            return new AmbClient(context.Session, context.Telemetry, new AmbClientOptions
            {
                MaximumBrowseReferences = 1024,
                MaximumBrowsePages = 128
            });
        }

        private static void AddValues(
            CompanionContext context, List<CompanionValue> fields, string section, ArrayOf<AssetReadValue> values)
        {
            foreach (AssetReadValue value in values)
            {
                AddValue(context, fields, section, value);
            }
        }

        private static void AddValue(
            CompanionContext context, List<CompanionValue> fields, string section, AssetReadValue value)
        {
            CellCompanionSupport.CheckCount(fields.Count + 1, context.MaxFields, "Asset fields");
            string path = string.Join(" / ", value.BrowsePath.ToList().Select(name => name.Name ?? name.ToString()));
            string name = string.IsNullOrEmpty(path) ? section : section + " / " + path;
            if (name.Length > 1024)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "An asset field name is too long.");
            }
            DataValue reading = value.Value;
            if (reading.WrappedValue.TryGetValue(out string? text) && text is { Length: > 4096 })
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "An asset field exceeds the display text limit.");
            }
            fields.Add(value.IsAbsent
                ? new CompanionValue(name, Variant.From("Not exposed"))
                : new CompanionValue(name, reading.WrappedValue)
                {
                    SourceNodeId = value.AttributeId == Attributes.Value ? value.SourceNodeId : default,
                    Quality = reading.StatusCode,
                    SourceTimestamp = reading.SourceTimestamp.IsNull ? null : reading.SourceTimestamp.ToDateTime()
                });
        }

        private static void AddText(
            CompanionContext context, List<CompanionValue> fields, string name, string? value)
        {
            CellCompanionSupport.CheckCount(fields.Count + 1, context.MaxFields, "Asset fields");
            if (value is { Length: > 4096 })
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "An asset version is too long.");
            }
            fields.Add(new CompanionValue(name, Variant.From(value ?? "Not exposed")));
        }

        private const string AssetKind = "Managed asset";

        private static readonly ArrayOf<CompanionOperation> s_readOperations =
        [
            new("identification", "Read identification", CompanionOperationSafety.ReadOnly),
            new("health", "Read current health conditions", CompanionOperationSafety.ReadOnly),
            new("maintenance", "Read current and future maintenance", CompanionOperationSafety.ReadOnly),
            new("documentation", "Read documentation links", CompanionOperationSafety.ReadOnly),
            new("locations", "Read location and classification", CompanionOperationSafety.ReadOnly),
            new("requirements", "Read requirements", CompanionOperationSafety.ReadOnly),
            new("capabilities", "Read capabilities", CompanionOperationSafety.ReadOnly),
            new("versions", "Read asset-set versions", CompanionOperationSafety.ReadOnly),
            new("sub-assets", "Read sub-assets", CompanionOperationSafety.ReadOnly),
            new("relations", "Read asset relationships", CompanionOperationSafety.ReadOnly)
        ];
    }
}
