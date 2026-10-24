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
using Opc.Ua;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;
using RI = Opc.Ua.RobotIntent;
using static UaLens.Plugins.Companions.CompanionMethodMetadata;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Separately labeled draft Robot Intent workflows. Admission is limited to finite waits and wait-only missions;
/// channel operations only broker leases and never connect to, renew or publish on the returned transport.
/// </summary>
internal sealed partial class RobotIntentCompanionProvider : IPreparedCompanionProvider
{
    /// <summary>
    /// Composes existing typed clients over the borrowed primary session with default-deny simulation authorization.
    /// </summary>
    public RobotIntentCompanionProvider(
        ICompanionDeploymentPolicy? policy = null, TimeProvider? timeProvider = null,
        Func<CompanionContext, NodeId, IRobotIntentTransport>? createTransport = null)
    {
        m_policy = policy as IRoboticsSimulationPolicy;
        m_time = timeProvider ?? TimeProvider.System;
        m_createTransport = createTransport ?? (static (context, controller) =>
            new UaRobotIntentTransport(context.Session, controller, context.Telemetry, observeReconnect: false));
    }

    /// <inheritdoc/>
    public CompanionDescriptor Descriptor { get; } = new(
        "robot-intent", "Robot Intent (draft)", RI.Namespaces.RobotIntent,
        "DRAFT Robot Intent, not published OPC 40010. Nonphysical owned-simulation workflows only.");

    /// <inheritdoc/>
    public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetime = Begin(context, cancellationToken);
        NodeId root = await ChildAsync(context, Opc.Ua.ObjectIds.Server, "RobotIntent", true, lifetime.Token)
            .ConfigureAwait(false);
        if (root.IsNull)
        {
            return [];
        }
        NodeId controllers = await ChildAsync(context, root, "Controllers", false, lifetime.Token)
            .ConfigureAwait(false);
        return await IndustrialCompanionAccess.DiscoverAsync(
            context, Descriptor.Id, s_types, [controllers], lifetime.Token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<CompanionInspection> InspectAsync(
        CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetime = Begin(context, cancellationToken);
        await RequireControllerAsync(context, target, lifetime.Token).ConfigureAwait(false);
        var fields = new List<CompanionValue>();
        foreach (string name in s_controllerFields)
        {
            await AddFieldAsync(context, target.NodeId, name, name, fields, lifetime.Token).ConfigureAwait(false);
        }
        ArrayOf<NodeId> operations = await ChildrenAsync(context, target.NodeId, "Intents", lifetime.Token)
            .ConfigureAwait(false);
        ArrayOf<NodeId> missions = await ChildrenAsync(context, target.NodeId, "Missions", lifetime.Token)
            .ConfigureAwait(false);
        ArrayOf<NodeId> channels = await ChildrenAsync(context, target.NodeId, "RealTimeChannels", lifetime.Token)
            .ConfigureAwait(false);
        CellCompanionSupport.CheckCount(
            operations.Count + missions.Count + channels.Count, context.MaxTargets, "Robot Intent members");
        IRobotIntentTransport? transport = null;
        for (int index = 0; index < operations.Count; index++)
        {
            NodeId operation = operations[index];
            await RequireMemberTypeAsync(context, "Intents", operation, lifetime.Token).ConfigureAwait(false);
            (NodeId stateNode, ExecutionStateEnum state, DateTime? timestamp) =
                await ReadExecutionStateAsync(context, operation, lifetime.Token).ConfigureAwait(false);
            transport ??= m_createTransport(context, target.NodeId);
            IntentOperationSnapshot snapshot = await transport.ReadOperationSnapshotAsync(operation, lifetime.Token)
                .ConfigureAwait(false);
            RequireRuntimeId(snapshot.IntentId);
            Add(fields, context, new CompanionValue("Intent " + snapshot.IntentId,
                Variant.From(state.ToString()))
            { SourceNodeId = stateNode, SourceTimestamp = timestamp });
            Add(fields, context, new CompanionValue("Intent operation", Variant.From(operation)));
        }
        for (int index = 0; index < missions.Count; index++)
        {
            NodeId mission = missions[index];
            await RequireMemberTypeAsync(context, "Missions", mission, lifetime.Token).ConfigureAwait(false);
            (NodeId stateNode, ExecutionStateEnum state, DateTime? timestamp) =
                await ReadExecutionStateAsync(context, mission, lifetime.Token).ConfigureAwait(false);
            transport ??= m_createTransport(context, target.NodeId);
            MissionSnapshot snapshot = await transport.ReadMissionSnapshotAsync(mission, lifetime.Token)
                .ConfigureAwait(false);
            RequireRuntimeId(snapshot.MissionId);
            Add(fields, context, new CompanionValue("Mission " + snapshot.MissionId,
                Variant.From(state.ToString()))
            { SourceNodeId = stateNode, SourceTimestamp = timestamp });
            Add(fields, context, new CompanionValue("Mission operation", Variant.From(mission)));
        }
        for (int index = 0; index < channels.Count; index++)
        {
            NodeId channel = channels[index];
            Add(fields, context, new CompanionValue("Real-time channel", Variant.From(channel)));
            foreach (string name in s_channelFields)
            {
                await AddFieldAsync(context, channel, name, channel + " / " + name, fields, lifetime.Token)
                    .ConfigureAwait(false);
            }
        }
        List<CompanionOperation> available =
            [new("snapshot", "Read draft intent, mission and lease state", CompanionOperationSafety.ReadOnly)];
        for (int index = 0; index < s_commands.Count; index++)
        {
            Command command = s_commands[index];
            string reason;
            StatusCode quality = StatusCodes.Good;
            try
            {
                if (command.IsWait)
                {
                    _ = await ReadWaitCapabilityAsync(context, target.NodeId, command.IsMission, lifetime.Token)
                        .ConfigureAwait(false);
                }
                if (!command.IsPreview)
                {
                    _ = await ResolveMethodAsync(context, target.NodeId, command, lifetime.Token).ConfigureAwait(false);
                    var candidate = new RoboticsPreparedTask(
                        this, context, target, command.Operation, [], m_time, "Availability check only.");
                    await candidate.AuthorizeAsync(context, m_policy, lifetime.Token).ConfigureAwait(false);
                }
                if ((command.Id == "cancel-intent" && operations.Count == 0) ||
                    (command.Id == "cancel-mission" && missions.Count == 0) ||
                    (command.Id is "open-channel" or "close-channel" && channels.Count == 0))
                {
                    reason = "No selectable instance is exposed.";
                }
                else
                {
                    available.Add(command.Operation);
                    reason = command.IsPreview ? "Local preview only; not server admission or a safety guarantee." :
                        "Prepare and confirm for the explicitly authorized owned simulation.";
                }
            }
            catch (UnauthorizedAccessException)
            {
                reason = "Requires authenticated, explicitly authorized owned simulation.";
                quality = StatusCodes.BadUserAccessDenied;
            }
            catch (ServiceResultException failure)
            {
                reason = "Not available: " + failure.StatusCode;
                quality = failure.StatusCode;
            }
            Add(fields, context, new CompanionValue(command.Operation.DisplayName, Variant.From(reason))
            {
                Quality = quality
            });
        }
        return new CompanionInspection([.. fields], [.. available],
            "DRAFT Robot Intent; separate from published OPC 40010. No command was sent. " +
            "Preview is local only: this model has no server Preview method. " +
            "Only finite Wait intents and wait-only missions can be submitted here. " +
            "Authority is arbitration, not authorization; it has no timed lease. " +
            "Real-time leases are broker-only and never automatically renewed.");
    }

    /// <inheritdoc/>
    public async ValueTask<CompanionOperationResult> ExecuteAsync(
        CompanionContext context, CompanionTarget target, string operationId, string? input,
        CancellationToken cancellationToken)
    {
        CellCompanionSupport.RequireNoInput(input);
        if (operationId != "snapshot")
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported,
                "Prepare a supported draft simulation task; arbitrary robot commands are excluded.");
        }
        CompanionInspection inspection = await InspectAsync(context, target, cancellationToken).ConfigureAwait(false);
        return new CompanionOperationResult(inspection.Summary, inspection.Values);
    }

    private CancellationTokenSource Begin(CompanionContext context, CancellationToken cancellationToken)
    {
        CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        try
        {
            _ = new RobotIntentClient(context.Session, context.Telemetry);
            return lifetime;
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    private static ValueTask RequireControllerAsync(
        CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
    {
        return IndustrialCompanionAccess.RequireTargetAsync(
            context, target, "robot-intent", s_types, cancellationToken);
    }

    private static async ValueTask<ArrayOf<NodeId>> ChildrenAsync(
        CompanionContext context, NodeId controller, string name, CancellationToken cancellationToken)
    {
        NodeId folder = await ChildAsync(context, controller, name, true, cancellationToken).ConfigureAwait(false);
        if (folder.IsNull)
        {
            return [];
        }
        var result = new List<NodeId>();
        var visited = new HashSet<NodeId>();
        await foreach (ReferenceDescription child in IndustrialCompanionAccess.BrowseAsync(
            context, folder, BrowseDirection.Forward, Opc.Ua.ReferenceTypeIds.HierarchicalReferences, NodeClass.Object,
            new IndustrialBrowseBudget(context), cancellationToken).ConfigureAwait(false))
        {
            NodeId node = IndustrialCompanionAccess.LocalId(context, child.NodeId);
            if (node.IsNull || !visited.Add(node))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "A draft instance is null or duplicated.");
            }
            CellCompanionSupport.CheckCount(result.Count + 1, context.MaxTargets, "Robot Intent members");
            result.Add(node);
        }
        return [.. result];
    }

    private static ValueTask<NodeId> ChildAsync(
        CompanionContext context, NodeId parent, string name, bool optional, CancellationToken cancellationToken)
    {
        return IndustrialCompanionAccess.ResolveChildAsync(
            context, parent, RI.Namespaces.RobotIntent, name, optional, cancellationToken);
    }

    private static async ValueTask AddFieldAsync(
        CompanionContext context, NodeId parent, string name, string label, List<CompanionValue> fields,
        CancellationToken cancellationToken)
    {
        NodeId node = await ChildAsync(context, parent, name, true, cancellationToken).ConfigureAwait(false);
        if (node.IsNull)
        {
            Add(fields, context, new CompanionValue(label, Variant.From("Not exposed"))
            {
                Quality = StatusCodes.BadNotSupported
            });
            return;
        }
        DataValue value = await ReadOneAsync(context, node, Attributes.Value, cancellationToken).ConfigureAwait(false);
        Add(fields, context, new CompanionValue(label, value.WrappedValue)
        {
            SourceNodeId = node,
            Quality = value.StatusCode,
            SourceTimestamp = value.SourceTimestamp.IsNull ? null : value.SourceTimestamp.ToDateTime()
        });
    }

    private static void Add(List<CompanionValue> fields, CompanionContext context, CompanionValue value)
    {
        CellCompanionSupport.CheckCount(fields.Count + 1, context.MaxFields, "Robot Intent inspection");
        fields.Add(value);
    }

    private readonly IRoboticsSimulationPolicy? m_policy;
    private readonly TimeProvider m_time;
    private readonly Func<CompanionContext, NodeId, IRobotIntentTransport> m_createTransport;
    private static readonly string[] s_controllerFields =
        ["OperationalMode", "Ready", "ControlOwner", "ActiveIntent", "ActiveMission", "MaxQueueDepth"];
    private static readonly string[] s_channelFields = ["ChannelId", "LeaseHolder", "LeaseExpiry"];
    private static readonly ArrayOf<IndustrialCompanionType> s_types =
        [new(RI.ObjectTypeIds.IntentControllerType, "DraftIntentController")];
}
