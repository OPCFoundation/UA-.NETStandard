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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AMB;
using Opc.Ua.AMB.Client;
using UaLens.Connection;
using UaLens.Plugins.Alarms;
using AmbNames = Opc.Ua.AMB.BrowseNames;
using UaNames = Opc.Ua.BrowseNames;
using UaTypes = Opc.Ua.DataTypeIds;
using static UaLens.Plugins.Companions.CompanionMethodMetadata;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class AmbCompanionProvider
    {
        private async Task<TaskEvidence> ReadTaskEvidenceAsync(
            CompanionContext context, CompanionTarget target, string operationId, MutationInput payload,
            CancellationToken cancellationToken)
        {
            RequireSecureTask(context);
            AmbClient client = CreateClient(context);
            await RequireAssetAsync(context, client, target, cancellationToken).ConfigureAwait(false);
            string productVersion = await RequireVersionAsync(
                client, AssetAliasCategory.ByProductInstanceUri, cancellationToken).ConfigureAwait(false);
            string assetVersion = await RequireVersionAsync(
                client, AssetAliasCategory.ByAssetId, cancellationToken).ConfigureAwait(false);
            AssetIdentificationRecord identity = await client.ReadIdentificationAsync(target.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(identity.ProductInstanceUri) || identity.ProductInstanceUri.Length > 4096)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The asset has no stable product identity.");
            }
            NodeId subject = target.NodeId;
            NodeId owner = target.NodeId;
            NodeId method = default;
            Variant oldValue = default;
            string metadata = string.Empty;
            AlarmCondition? condition = null;
            if (operationId == "set-asset-id")
            {
                AssetReadValue field = identity.Values.ToList().Single(value =>
                    value.BrowsePath.Count != 0 && value.BrowsePath[^1].Name == Opc.Ua.Di.BrowseNames.AssetId);
                if (field.IsAbsent)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "The asset has no configurable AssetId.");
                }
                subject = field.SourceNodeId;
                oldValue = DataValueCodec.Snapshot(field.Value.WrappedValue, context.Session.MessageContext);
                await RequireWritableStringAsync(context, subject, cancellationToken).ConfigureAwait(false);
            }
            else if (operationId is "add-link" or "write-link" or "remove-link")
            {
                owner = await client.ResolveDocumentationLinksAsync(target.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                if (owner.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "The asset has no DocumentationLinks.");
                }
                ArrayOf<DocumentationLinkRecord> links = await client.ReadDocumentationLinksAsync(
                    target.NodeId, cancellationToken).ConfigureAwait(false);
                if (operationId == "add-link")
                {
                    if (links.Contains(link => link.BrowseName == payload.Name))
                    {
                        throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated,
                            "A documentation link already uses the requested browse name.");
                    }
                    subject = owner;
                    method = await RequireMethodAsync(
                        context, owner, Opc.Ua.AMB.Namespaces.AMB, AmbNames.AddLink,
                        [UaTypes.String, UaTypes.QualifiedName, UaTypes.LocalizedText, UaTypes.LocalizedText],
                        [UaTypes.NodeId], cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    DocumentationLinkRecord[] selected = links.ToList()
                        .Where(link => link.NodeId == payload.Node("linkId")).ToArray();
                    if (selected.Length != 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                            "The selected link is not uniquely owned by this asset.");
                    }
                    DocumentationLinkRecord link = selected[0];
                    subject = link.NodeId;
                    ValidateText(link.Uri, 4096, allowEmpty: true);
                    oldValue = Variant.From(link.Uri);
                    metadata = $"{link.BrowseName}; user link={link.IsUserLink}; writable={link.IsWritable}";
                    if (operationId == "write-link")
                    {
                        if (!link.IsWritable)
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The link is read-only.");
                        }
                        await RequireWritableStringAsync(context, subject, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        if (!link.IsUserLink)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNotSupported, "Only a verified user-created link can be removed.");
                        }
                        method = await RequireMethodAsync(
                            context, owner, Opc.Ua.AMB.Namespaces.AMB, AmbNames.RemoveLink,
                            [UaTypes.NodeId], [], cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                condition = await ReadAcknowledgementConditionAsync(
                    context, client, target, operationId, payload.Node("conditionId"), cancellationToken)
                    .ConfigureAwait(false);
                if (condition.EventId != payload.EventId)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEventIdUnknown, "The condition has a different current EventId. Prepare again.");
                }
                subject = condition.Key.ConditionId;
                owner = subject;
                method = await RequireMethodAsync(
                    context, owner, Opc.Ua.Namespaces.OpcUa, UaNames.Acknowledge,
                    [UaTypes.ByteString, UaTypes.LocalizedText], [], cancellationToken).ConfigureAwait(false);
            }
            return new TaskEvidence(identity.ProductInstanceUri, productVersion, assetVersion,
                subject, owner, method, oldValue, metadata, condition);
        }

        private static async ValueTask<bool> IsTaskSupportedAsync(
            CompanionContext context, CompanionTarget target, string operationId, CancellationToken cancellationToken)
        {
            RequireSecureTask(context);
            AmbClient client = CreateClient(context);
            _ = await RequireVersionAsync(client, AssetAliasCategory.ByProductInstanceUri, cancellationToken)
                .ConfigureAwait(false);
            _ = await RequireVersionAsync(client, AssetAliasCategory.ByAssetId, cancellationToken).ConfigureAwait(false);
            if (operationId == "set-asset-id")
            {
                AssetIdentificationRecord identity = await client.ReadIdentificationAsync(target.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                AssetReadValue field = identity.Values.ToList().Single(value =>
                    value.BrowsePath.Count != 0 && value.BrowsePath[^1].Name == Opc.Ua.Di.BrowseNames.AssetId);
                if (field.IsAbsent)
                {
                    return false;
                }
                await RequireWritableStringAsync(context, field.SourceNodeId, cancellationToken).ConfigureAwait(false);
                return true;
            }
            if (operationId is "add-link" or "write-link" or "remove-link")
            {
                NodeId owner = await client.ResolveDocumentationLinksAsync(target.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                if (owner.IsNull)
                {
                    return false;
                }
                if (operationId == "add-link")
                {
                    _ = await RequireMethodAsync(context, owner, Opc.Ua.AMB.Namespaces.AMB, AmbNames.AddLink,
                        [UaTypes.String, UaTypes.QualifiedName, UaTypes.LocalizedText, UaTypes.LocalizedText],
                        [UaTypes.NodeId], cancellationToken).ConfigureAwait(false);
                    return true;
                }
                ArrayOf<DocumentationLinkRecord> links = await client.ReadDocumentationLinksAsync(
                    target.NodeId, cancellationToken).ConfigureAwait(false);
                if (operationId == "write-link")
                {
                    return links.Contains(link => link.IsWritable);
                }
                if (!links.Contains(link => link.IsUserLink))
                {
                    return false;
                }
                _ = await RequireMethodAsync(context, owner, Opc.Ua.AMB.Namespaces.AMB, AmbNames.RemoveLink,
                    [UaTypes.NodeId], [], cancellationToken).ConfigureAwait(false);
                return true;
            }
            if (operationId == "acknowledge-health")
            {
                ArrayOf<AssetAlarmRecord> alarms = await client.ReadHealthAlarmsAsync(target.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                return alarms.Contains(alarm => alarm.SourceNode == target.NodeId &&
                    alarm.Retain &&
                    !alarm.IsAcknowledged &&
                    !alarm.EventId.IsEmpty);
            }
            ArrayOf<MaintenanceActivityRecord> activities = await client.ReadMaintenanceActivitiesAsync(
                target.NodeId, cancellationToken).ConfigureAwait(false);
            return activities.Contains(activity => activity.SourceNode == target.NodeId &&
                activity.State == MaintenanceStateKind.Finished &&
                activity.Retain &&
                !activity.IsAcknowledged &&
                !activity.EventId.IsEmpty);
        }

        private static async Task<AlarmCondition> ReadAcknowledgementConditionAsync(
            CompanionContext context, AmbClient client, CompanionTarget target, string operationId,
            NodeId conditionId, CancellationToken cancellationToken)
        {
            AlarmCondition condition;
            if (operationId == "acknowledge-health")
            {
                AssetAlarmRecord[] records = (await client.ReadHealthAlarmsAsync(target.NodeId, cancellationToken)
                    .ConfigureAwait(false)).ToList().Where(record => record.ConditionId == conditionId).ToArray();
                if (records.Length != 1)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, "The health condition is not owned here.");
                }
                AssetAlarmRecord alarm = records[0];
                RequireConditionFields(alarm.Values);
                condition = new AlarmCondition(new AlarmKey(conditionId, default),
                    alarm.EventId.Copy(), alarm.EventType, alarm.SourceNode, alarm.SourceName ?? string.Empty,
                    alarm.ConditionName ?? string.Empty, alarm.Message.Text ?? string.Empty, alarm.Severity,
                    alarm.Time.IsNull ? null : alarm.Time.ToDateTime(), null, true, alarm.IsActive, alarm.IsAcknowledged,
                    null, alarm.Retain, StatusCodes.Good, AlarmConditionKind.Acknowledgeable);
            }
            else
            {
                MaintenanceActivityRecord[] records = (await client.ReadMaintenanceActivitiesAsync(
                    target.NodeId, cancellationToken).ConfigureAwait(false)).ToList()
                    .Where(record => record.ConditionId == conditionId).ToArray();
                if (records.Length != 1 || records[0].State != MaintenanceStateKind.Finished)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Select a finished maintenance activity owned by this asset.");
                }
                MaintenanceActivityRecord activity = records[0];
                RequireConditionFields(activity.Values);
                condition = new AlarmCondition(new AlarmKey(conditionId, default),
                    activity.EventId.Copy(), activity.EventType, activity.SourceNode, string.Empty,
                    activity.ConditionName ?? string.Empty, activity.Message.Text ?? string.Empty, null,
                    activity.Time.IsNull ? null : activity.Time.ToDateTime(), null, true, activity.IsActive,
                    activity.IsAcknowledged, null, activity.Retain, StatusCodes.Good, AlarmConditionKind.Acknowledgeable);
            }
            if (condition.SourceNode != target.NodeId || !condition.Retain || condition.Acknowledged != false)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The condition is no longer retained, unacknowledged and owned here.");
            }
            NodeId branchNode = await IndustrialCompanionAccess.ResolveChildAsync(
                context, conditionId, Opc.Ua.Namespaces.OpcUa, UaNames.BranchId, false, cancellationToken)
                .ConfigureAwait(false);
            DataValue branch = await ReadOneAsync(context, branchNode, Attributes.Value, cancellationToken)
                .ConfigureAwait(false);
            if (!branch.WrappedValue.TryGetValue(out NodeId branchId) || !branchId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "Only the current condition branch is supported.");
            }
            return condition;
        }

        private static void RequireConditionFields(ArrayOf<AssetReadValue> fields)
        {
            foreach (string name in (ArrayOf<string>)[UaNames.EventId, UaNames.SourceNode, UaNames.Retain, UaNames.AckedState])
            {
                if (!fields.Contains(field => !field.IsAbsent &&
                    field.AttributeId == Attributes.Value &&
                    !field.Value.WrappedValue.IsNull &&
                    field.BrowsePath.Count > 0 &&
                    field.BrowsePath[0].Name == name))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNoData, "Acknowledgement requires the condition's complete current event evidence.");
                }
            }
        }

        private async Task AcknowledgeTaskAsync(
            CompanionContext context, PreparedMutation prepared, CancellationToken cancellationToken)
        {
            AlarmCondition condition = prepared.Evidence.Condition ??
                throw new InvalidOperationException("The acknowledgement evidence is missing.");
            AlarmCommandAdapter.Validate(new AlarmCommand(
                condition, AlarmOperationKind.Acknowledge, prepared.Payload.Text("comment")));
            using var session = new PreparedCompanionMethodClient(
                context, condition.Key.ConditionId, Opc.Ua.MethodIds.AcknowledgeableConditionType_Acknowledge,
                prepared.Evidence.Method, false, () => prepared.RequireCurrent(context, m_timeProvider), static () => { });
            await new AcknowledgeableConditionTypeClient(session, condition.Key.ConditionId, context.Telemetry)
                .AcknowledgeAsync(condition.EventId, new LocalizedText(prepared.Payload.Text("comment")), cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<string> RequireVersionAsync(
            AmbClient client, AssetAliasCategory category, CancellationToken cancellationToken)
        {
            string? version = await client.ReadNodeVersionAsync(category, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(version) || version.Length > 512)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Both alias-category NodeVersions must be available for metadata tasks.");
            }
            return version;
        }

        private static void RequireSecureTask(CompanionContext context)
        {
            if (!context.Session.Connected ||
                context.Session.Endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                string.IsNullOrEmpty(context.Session.Endpoint.SecurityPolicyUri) ||
                context.Session.Endpoint.SecurityPolicyUri == SecurityPolicies.None)
            {
                throw new UnauthorizedAccessException("Asset tasks require a connected, signed and encrypted session.");
            }
        }

        /// <summary>
        /// Only stable identity, old metadata and the exact current event are pinned, not read timestamps.
        /// </summary>
        private sealed record TaskEvidence(
            string ProductInstanceUri, string ProductVersion, string AssetVersion,
            NodeId Subject, NodeId Owner, NodeId Method, Variant OldValue, string Metadata, AlarmCondition? Condition)
        {
            /// <summary>
            /// Tests equality of the immutable preflight observations.
            /// </summary>
            public bool Matches(TaskEvidence current) => this == current;
        }
    }
}
