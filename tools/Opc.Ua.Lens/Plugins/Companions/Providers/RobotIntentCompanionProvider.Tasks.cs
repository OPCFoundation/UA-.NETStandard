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
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;
using UaLens.Connection;
using RI = Opc.Ua.RobotIntent;
using static UaLens.Plugins.Companions.CompanionMethodMetadata;

namespace UaLens.Plugins.Companions.Providers;

internal sealed partial class RobotIntentCompanionProvider
{
    /// <inheritdoc/>
    public async ValueTask<CompanionTaskInput> PrepareInputAsync(
        CompanionContext context, CompanionTarget target, string operationId, ArrayOf<CompanionValue> inputs,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetime = Begin(context, cancellationToken);
        Command command = FindCommand(operationId);
        CompanionInputContract.ValidateShape(command.Operation, inputs);
        ValidateInput(context, command, inputs);
        var prepared = new RoboticsPreparedTask(this, context, target, command.Operation, inputs, m_time,
            $"DRAFT Robot Intent: {command.Operation.DisplayName} on {target.NodeId}. " +
            string.Join("; ", inputs.ToList().Select(value => value.Name + "=" + value.Text)) + ". " +
            "Preview/preparation is local, not server admission or a safety guarantee. " +
            "Finite wait-only execution; no motion, transport connection, automatic renewal or replay. " +
            (command.Id == "close-channel"
                ? "If optional LeaseExpiry is absent, only the server can decide whether this lease is still active."
                : string.Empty));
        await prepared.AuthorizeAsync(context, m_policy, lifetime.Token).ConfigureAwait(false);
        prepared.Bind(await ReadTaskBindingAsync(context, target, command, prepared.Draft.Inputs, lifetime.Token)
            .ConfigureAwait(false));
        prepared.RequireCurrent(context);
        return prepared;
    }

    /// <inheritdoc/>
    public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
        CompanionContext context, CompanionTarget target, string operationId, CompanionTaskInput input,
        IProgress<CompanionTaskProgress>? progress, CancellationToken cancellationToken)
    {
        if (input is not RoboticsPreparedTask prepared)
        {
            throw new ArgumentException("Prepare this draft simulation task first.", nameof(input));
        }
        prepared.Consume(this, target, operationId);
        using CancellationTokenSource lifetime = Begin(context, cancellationToken);
        Command command = FindCommand(operationId);
        prepared.RequireCurrent(context);
        prepared.RequireBinding(await ReadTaskBindingAsync(
            context, target, command, prepared.Draft.Inputs, lifetime.Token)
            .ConfigureAwait(false));
        if (!command.IsPreview)
        {
            progress?.Report(new CompanionTaskProgress(
                "Revalidating one owned draft simulation task before dispatch."));
        }
        await prepared.AuthorizeAsync(context, m_policy, lifetime.Token).ConfigureAwait(false);
        prepared.RequireBinding(await ReadTaskBindingAsync(
            context, target, command, prepared.Draft.Inputs, lifetime.Token)
            .ConfigureAwait(false));
        prepared.RequireCurrent(context);
        if (command.IsPreview)
        {
            return new CompanionOperationResult(
                "Local DRAFT Robot Intent preview only. No method was called, authority acquired or work admitted. " +
                "This is not a path/safety check; the current model has no server Preview method.",
                [new CompanionValue(command.IsMission ? "Mission preview" : "Intent preview",
                    command.IsMission
                        ? Variant.FromStructure(CreateMission(prepared.Draft.Inputs))
                        : Variant.FromStructure(CreateWait(prepared.Draft.Inputs)))]);
        }
        using var client = new PreparedCompanionMethodClient(context, target.NodeId,
            command.Declaration, prepared.Binding.Method,
            outputs => ValidateOutput(context, command, prepared, outputs),
            () => prepared.RequireCurrent(context), static () => { });
        return await DispatchAsync(context, client, command, prepared, lifetime.Token).ConfigureAwait(false);
    }

    private async ValueTask<RoboticsMethodBinding> ReadTaskBindingAsync(
        CompanionContext context, CompanionTarget target, Command command, ArrayOf<CompanionValue> inputs,
        CancellationToken cancellationToken)
    {
        await RequireControllerAsync(context, target, cancellationToken).ConfigureAwait(false);
        var nodes = new List<NodeId>();
        var values = new List<Variant>();
        foreach (string name in s_controlFields)
        {
            (NodeId node, Variant value) = await ReadFieldAsync(context, target.NodeId, name, cancellationToken)
                .ConfigureAwait(false);
            nodes.Add(node);
            values.Add(value);
        }
        if (!values[0].TryGetValue(out NodeId owner) ||
            !values[1].TryGetValue(out bool ready) ||
            !values[2].TryGetValue(out int mode) ||
            !Enum.IsDefined((OperationalModeEnum)mode))
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "Draft authority/readiness metadata is malformed.");
        }
        if (!command.IsPreview && command.Id != "request-control" && owner != context.Session.SessionId)
        {
            throw new UnauthorizedAccessException(
                "This primary session does not hold command authority. Request it explicitly.");
        }
        if (command.IsWait)
        {
            RoboticsMethodBinding capability = await ReadWaitCapabilityAsync(
                context, target.NodeId, command.IsMission, cancellationToken).ConfigureAwait(false);
            foreach (NodeId node in capability.Nodes)
            {
                nodes.Add(node);
            }
            foreach (Variant value in capability.Values)
            {
                values.Add(value);
            }
            if (!command.IsPreview && (!ready || mode != (int)OperationalModeEnum.AutomaticExternal))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The simulation is not Ready in AutomaticExternal mode. Local preview is not admission.");
            }
        }
        DateTimeOffset? expires = null;
        if (command.Id is "cancel-intent" or "cancel-mission")
        {
            NodeId subject = NodeInput(inputs, 0);
            await RequireMemberAsync(context, target.NodeId, command.IsMission ? "Missions" : "Intents",
                subject, cancellationToken).ConfigureAwait(false);
            IRobotIntentTransport transport = m_createTransport(context, target.NodeId);
            string id;
            ExecutionStateEnum state;
            if (command.IsMission)
            {
                MissionSnapshot mission = await transport.ReadMissionSnapshotAsync(subject, cancellationToken)
                    .ConfigureAwait(false);
                RequireWaitMission(mission.Mission);
                id = mission.MissionId;
                state = mission.ExecutionState;
                values.Add(DataValueCodec.Snapshot(
                    Variant.FromStructure(mission.Mission), context.Session.MessageContext));
                values.Add(Variant.From(mission.MissionUpdateId));
            }
            else
            {
                (_, Variant payload) = await ReadFieldAsync(context, subject, "Intent", cancellationToken)
                    .ConfigureAwait(false);
                if (!payload.TryGetValue<WaitIntentDataType>(
                    out WaitIntentDataType? wait, context.Session.MessageContext) ||
                    wait is null || !IsFiniteWait(wait))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "Only a finite wait may be cancelled here.");
                }
                IntentOperationSnapshot operation = await transport.ReadOperationSnapshotAsync(
                    subject, cancellationToken)
                    .ConfigureAwait(false);
                id = operation.IntentId;
                state = operation.ExecutionState;
                values.Add(DataValueCodec.Snapshot(payload, context.Session.MessageContext));
            }
            RequireRuntimeId(id);
            (NodeId stateNode, ExecutionStateEnum liveState, _) =
                await ReadExecutionStateAsync(context, subject, cancellationToken).ConfigureAwait(false);
            if (liveState != state ||
                state is not (ExecutionStateEnum.Accepted or ExecutionStateEnum.Queued or
                    ExecutionStateEnum.Executing or ExecutionStateEnum.Suspended))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "Select a correlated nonterminal finite wait or wait-only mission.");
            }
            nodes.Add(subject);
            nodes.Add(stateNode);
            values.Add(Variant.From((int)state));
            values.Add(Variant.From(id));
        }
        if (command.Id is "open-channel" or "close-channel")
        {
            NodeId channel = NodeInput(inputs, 0);
            await RequireMemberAsync(context, target.NodeId, "RealTimeChannels", channel, cancellationToken)
                .ConfigureAwait(false);
            (NodeId holderNode, Variant holderValue) = await ReadFieldAsync(
                context, channel, "LeaseHolder", cancellationToken).ConfigureAwait(false);
            NodeId expiryNode = await ChildAsync(context, channel, "LeaseExpiry", true, cancellationToken)
                .ConfigureAwait(false);
            Variant expiryValue = expiryNode.IsNull ? Variant.Null :
                (await ReadOneAsync(context, expiryNode, Attributes.Value, cancellationToken).ConfigureAwait(false))
                .WrappedValue;
            (NodeId idNode, Variant idValue) = await ReadFieldAsync(
                context, channel, "ChannelId", cancellationToken).ConfigureAwait(false);
            DateTimeUtc expiry = default;
            if (!holderValue.TryGetValue(out NodeId holder) ||
                (!expiryNode.IsNull && !expiryValue.TryGetValue(out expiry)) ||
                !idValue.TryGetValue(out string? id) || string.IsNullOrWhiteSpace(id) || id.Length > 128)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The channel lease metadata is malformed.");
            }
            if (command.Id == "close-channel")
            {
                if (holder != context.Session.SessionId ||
                    (!expiryNode.IsNull && (expiry.IsNull || expiry.ToDateTime() <= m_time.GetUtcNow().UtcDateTime)))
                {
                    throw new UnauthorizedAccessException(
                        "The selected real-time lease is expired or belongs to another session.");
                }
                expires = expiryNode.IsNull ? null : new DateTimeOffset(expiry.ToDateTime());
            }
            nodes.AddRange([channel, holderNode, expiryNode, idNode]);
            values.AddRange([holderValue, expiryValue, idValue]);
        }
        NodeId method = command.IsPreview ? NodeId.Null :
            await ResolveMethodAsync(context, target.NodeId, command, cancellationToken).ConfigureAwait(false);
        return new RoboticsMethodBinding(target.NodeId, method, [.. nodes], [.. values], expires);
    }

    private static async ValueTask<RoboticsMethodBinding> ReadWaitCapabilityAsync(
        CompanionContext context, NodeId controller, bool mission, CancellationToken cancellationToken)
    {
        NodeId capabilities = await ChildAsync(context, controller, "Capabilities", false, cancellationToken)
            .ConfigureAwait(false);
        (NodeId node, Variant value) = await ReadFieldAsync(
            context, capabilities, "SupportedIntents", cancellationToken).ConfigureAwait(false);
        if (!value.TryGetValue(out ArrayOf<IntentCapabilityDataType> supported, context.Session.MessageContext) ||
            supported.IsNull || supported.Count > 64)
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "Bounded typed intent capabilities are required.");
        }
        NodeId waitType = ExpandedNodeId.ToNodeId(RI.DataTypeIds.WaitIntentDataType, context.Session.NamespaceUris);
        IntentCapabilityDataType? wait = null;
        foreach (IntentCapabilityDataType capability in supported)
        {
            if (capability is null)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "An intent capability is null.");
            }
            if (capability.IntentType != waitType)
            {
                continue;
            }
            if (wait is not null)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The wait capability is duplicated.");
            }
            wait = capability;
        }
        if (wait is null ||
            !wait.SupportedBufferModes.Contains(BufferModeEnum.Buffered) ||
            !wait.SupportedBlockingModes.Contains(BlockingModeEnum.None))
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported,
                "This controller does not advertise non-blocking buffered Wait intents.");
        }
        ArrayOf<NodeId> nodes = [capabilities, node];
        ArrayOf<Variant> values =
            [DataValueCodec.Snapshot(Variant.FromStructure(wait), context.Session.MessageContext)];
        if (mission)
        {
            (NodeId flag, Variant enabled) = await ReadFieldAsync(
                context, capabilities, "MissionsSupported", cancellationToken).ConfigureAwait(false);
            if (!enabled.TryGetValue(out bool accepted) || !accepted)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Missions are not supported.");
            }
            nodes = [.. nodes, flag];
            values = [.. values, enabled];
        }
        return new RoboticsMethodBinding(controller, NodeId.Null, nodes, values);
    }

    private static async ValueTask RequireMemberAsync(
        CompanionContext context, NodeId controller, string folder, NodeId subject, CancellationToken cancellationToken)
    {
        ArrayOf<NodeId> members = await ChildrenAsync(context, controller, folder, cancellationToken)
            .ConfigureAwait(false);
        if (subject.IsNull || !members.Contains(subject))
        {
            throw new ServiceResultException(
                StatusCodes.BadNodeIdInvalid, "The selected instance is not in this controller.");
        }
        await RequireMemberTypeAsync(context, folder, subject, cancellationToken).ConfigureAwait(false);
    }

    private static ValueTask RequireMemberTypeAsync(
        CompanionContext context, string folder, NodeId subject, CancellationToken cancellationToken)
    {
        string kind = folder switch
        {
            "Intents" => "IntentOperation",
            "Missions" => "Mission",
            "RealTimeChannels" => "RealTimeChannel",
            _ => throw new ArgumentException("Unknown draft instance folder.", nameof(folder))
        };
        return IndustrialCompanionAccess.RequireTargetAsync(context,
            new CompanionTarget("robot-intent", subject, kind, kind), "robot-intent", s_memberTypes, cancellationToken);
    }

    private static async ValueTask<(NodeId Node, ExecutionStateEnum State, DateTime? Timestamp)>
        ReadExecutionStateAsync(
        CompanionContext context, NodeId operation, CancellationToken cancellationToken)
    {
        NodeId node = await ChildAsync(context, operation, "ExecutionState", false, cancellationToken)
            .ConfigureAwait(false);
        DataValue value = await ReadOneAsync(context, node, Attributes.Value, cancellationToken).ConfigureAwait(false);
        if (!value.WrappedValue.TryGetValue(out int state) || !Enum.IsDefined((ExecutionStateEnum)state))
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "The draft execution state is not a defined enum.");
        }
        return (node, (ExecutionStateEnum)state,
            value.SourceTimestamp.IsNull ? null : value.SourceTimestamp.ToDateTime());
    }

    private static void RequireRuntimeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "The draft operation has no bounded correlation id.");
        }
    }

    private static async ValueTask<(NodeId Node, Variant Value)> ReadFieldAsync(
        CompanionContext context, NodeId parent, string name, CancellationToken cancellationToken)
    {
        NodeId node = await ChildAsync(context, parent, name, false, cancellationToken).ConfigureAwait(false);
        DataValue value = await ReadOneAsync(context, node, Attributes.Value, cancellationToken).ConfigureAwait(false);
        return (node, value.WrappedValue);
    }

    private static Task<NodeId> ResolveMethodAsync(
        CompanionContext context, NodeId controller, Command command, CancellationToken cancellationToken)
    {
        return RequireMethodAsync(context, controller, RI.Namespaces.RobotIntent, command.Name,
            command.InputTypes.ConvertAll(type => ExpandedNodeId.ToNodeId(type, context.Session.NamespaceUris)),
            command.OutputTypes.ConvertAll(type => ExpandedNodeId.ToNodeId(type, context.Session.NamespaceUris)),
            cancellationToken);
    }

    private static void ValidateInput(
        CompanionContext context, Command command, ArrayOf<CompanionValue> inputs)
    {
        if (command.IsWait)
        {
            string id = StringInput(inputs, 0);
            double duration = DoubleInput(inputs, 1);
            uint count = command.IsMission ? CountInput(inputs) : 1u;
            if (string.IsNullOrWhiteSpace(id) || id.Length > 96 ||
                !double.IsFinite(duration) || duration < 1 || count is < 1 or > 8 ||
                duration * count > 10000)
            {
                throw new ArgumentException(
                    "Use a 1–96 character id and 1–8 finite wait steps totaling at most 10000 ms.",
                    nameof(inputs));
            }
        }
        else if (command.Id is "cancel-intent" or "cancel-mission")
        {
            RequireLocalInputNode(context, NodeInput(inputs, 0));
            if (!inputs[1].Value.TryGetValue(out int mode) || mode is < 1 or > 5)
            {
                throw new ArgumentException("Choose the draft StopModeEnum value 1 through 5.", nameof(inputs));
            }
        }
        else if (command.Id is "open-channel" or "close-channel")
        {
            RequireLocalInputNode(context, NodeInput(inputs, 0));
            if (command.Id == "open-channel")
            {
                double lease = DoubleInput(inputs, 1);
                if (!double.IsFinite(lease) || lease is < 100 or > 10000)
                {
                    throw new ArgumentException(
                        "A broker-only lease must be from 100 through 10000 ms.", nameof(inputs));
                }
            }
        }
    }

    private static void RequireLocalInputNode(CompanionContext context, NodeId node)
    {
        if (node.IsNull || node.NamespaceIndex >= context.Session.NamespaceUris.Count)
        {
            throw new ArgumentException("Select a local instance in the connected server.");
        }
    }

    private static WaitIntentDataType CreateWait(ArrayOf<CompanionValue> inputs)
    {
        return Wait(StringInput(inputs, 0), DoubleInput(inputs, 1));
    }

    private static WaitIntentDataType Wait(string id, double duration)
    {
        return RobotIntentBuilder.Wait(duration).WithIntentId(id)
            .WithBufferMode(BufferModeEnum.Buffered).WithBlockingMode(BlockingModeEnum.None)
            .WithLabel(new LocalizedText("UaLens nonphysical owned-simulation wait")).Build();
    }

    private static MissionDataType CreateMission(ArrayOf<CompanionValue> inputs)
    {
        string id = StringInput(inputs, 0);
        MissionBuilder mission = RobotIntentBuilder.Mission(id);
        uint count = CountInput(inputs);
        for (uint index = 0; index < count; index++)
        {
            string step = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            mission.ReleasedStep(step, Wait(id + ":" + step, DoubleInput(inputs, 1)));
        }
        return mission.Build();
    }

    private static bool IsFiniteWait(WaitIntentDataType wait)
    {
        return wait.Signal.IsNull && double.IsFinite(wait.Duration) && wait.Duration is >= 1 and <= 10000 &&
            wait.BufferMode == BufferModeEnum.Buffered && wait.BlockingMode == BlockingModeEnum.None;
    }

    private static void RequireWaitMission(MissionDataType mission)
    {
        if (mission.Steps.Count is < 1 or > 8 || mission.Transitions.Count != 0)
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "Only bounded flat wait-only missions are supported.");
        }
        double duration = 0;
        foreach (MissionStepDataType step in mission.Steps)
        {
            if (step is null || !step.Released || step.Intent is not WaitIntentDataType wait || !IsFiniteWait(wait))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The selected mission is not wait-only.");
            }
            duration += wait.Duration;
        }
        if (duration > 10000)
        {
            throw new ServiceResultException(StatusCodes.BadOutOfRange, "The selected mission exceeds 10000 ms.");
        }
    }

    private static string StringInput(ArrayOf<CompanionValue> inputs, int index)
    {
        return inputs[index].Value.TryGetValue(out string value) ? value :
            throw new ArgumentException("Expected a string.");
    }

    private static double DoubleInput(ArrayOf<CompanionValue> inputs, int index)
    {
        return inputs[index].Value.TryGetValue(out double value) ? value :
            throw new ArgumentException("Expected a Double.");
    }

    private static uint CountInput(ArrayOf<CompanionValue> inputs)
    {
        return inputs[2].Value.TryGetValue(out uint value) ? value :
            throw new ArgumentException("Expected a UInt32 count.");
    }

    private static NodeId NodeInput(ArrayOf<CompanionValue> inputs, int index)
    {
        return inputs[index].Value.TryGetValue(out NodeId value) ? value :
            throw new ArgumentException("Expected a NodeId.");
    }

    private static readonly string[] s_controlFields = ["ControlOwner", "Ready", "OperationalMode"];
    private static readonly ArrayOf<IndustrialCompanionType> s_memberTypes =
    [
        new(RI.ObjectTypeIds.IntentOperationType, "IntentOperation"),
        new(RI.ObjectTypeIds.MissionType, "Mission"),
        new(RI.ObjectTypeIds.RealTimeChannelType, "RealTimeChannel")
    ];
}
