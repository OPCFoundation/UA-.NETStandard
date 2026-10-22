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
using Opc.Ua.Client;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Client;
using R = Opc.Ua.Robotics;
using static UaLens.Plugins.Companions.CompanionMethodMetadata;

namespace UaLens.Plugins.Companions.Providers;

internal sealed partial class RoboticsCompanionProvider
{
    /// <inheritdoc/>
    public async ValueTask<CompanionTaskInput> PrepareInputAsync(
        CompanionContext context, CompanionTarget target, string operationId, ArrayOf<CompanionValue> inputs,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        Command command = FindCommand(target, operationId);
        CompanionInputContract.ValidateShape(command.Operation, inputs);
        ValidateCommandInput(command, inputs);
        var prepared = new RoboticsPreparedTask(this, context, target, command.Operation, inputs, m_time,
            $"{command.Operation.DisplayName} on {target.NodeId}. Published OPC 40010, owned simulation only. " +
            "No program or operation is sent during preparation. Acceptance is not observed completion.");
        await prepared.AuthorizeAsync(context, m_policy, lifetime.Token).ConfigureAwait(false);
        prepared.Bind(await ReadCommandBindingAsync(context, target, command, lifetime.Token).ConfigureAwait(false));
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
            throw new ArgumentException("Prepare this Robotics operation first.", nameof(input));
        }
        prepared.Consume(this, target, operationId);
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        Command command = FindCommand(target, operationId);
        prepared.RequireCurrent(context);
        prepared.RequireBinding(await ReadCommandBindingAsync(context, target, command, lifetime.Token)
            .ConfigureAwait(false));
        progress?.Report(new CompanionTaskProgress("Revalidating one owned simulation operation before dispatch."));
        await prepared.AuthorizeAsync(context, m_policy, lifetime.Token).ConfigureAwait(false);
        prepared.RequireBinding(await ReadCommandBindingAsync(context, target, command, lifetime.Token)
            .ConfigureAwait(false));
        prepared.RequireCurrent(context);
        using var client = new PreparedCompanionMethodClient(context, prepared.Binding.Owner,
            command.Declaration, prepared.Binding.Method, ValidateStatusOutput,
            () => prepared.RequireCurrent(context), static () => { });
        int status = await DispatchCommandAsync(client, context.Telemetry, prepared, lifetime.Token)
            .ConfigureAwait(false);
        return new CompanionOperationResult(
            status == 0
                ? "The simulator accepted the operation. Read fresh state to observe completion; never replay it."
                : $"The simulator returned operation status {status}. " +
                    "This is not a successful completion; inspect state.",
            [new CompanionValue("Operation status", Variant.From(status))]);
    }

    private async ValueTask<CompanionInspection> InspectWorkflowAsync(
        CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        RoboticsMethodBinding state = await ReadStateBindingAsync(context, target, lifetime.Token)
            .ConfigureAwait(false);
        var values = new List<CompanionValue>
        {
            new("State", state.Values[0]) { SourceNodeId = state.Nodes[0] },
            new("State id", state.Values[1]) { SourceNodeId = state.Nodes[1] }
        };
        if (target.TypeName == TaskControlKind)
        {
            TaskControlSnapshot task = await new RoboticsClient(context.Session, context.Telemetry)
                .ReadTaskControlAsync(target.NodeId, lifetime.Token).ConfigureAwait(false);
            AddProgramValue(values, "Program loaded", task.TaskProgramLoaded, task.TaskProgramLoadedId);
            AddProgramValue(values, "Program name", task.TaskProgramName, task.TaskProgramNameId);
            AddProgramValue(values, "Execution mode", task.ExecutionMode, task.ExecutionModeId);
        }
        List<CompanionOperation> operations =
            [new("snapshot", "Read fresh operation and program state", CompanionOperationSafety.ReadOnly)];
        for (int index = 0; index < s_commands.Count; index++)
        {
            Command command = s_commands[index];
            if (command.Kind != target.TypeName)
            {
                continue;
            }
            string reason;
            StatusCode quality = StatusCodes.Good;
            try
            {
                _ = await ReadCommandBindingAsync(context, target, command, lifetime.Token).ConfigureAwait(false);
                var candidate = new RoboticsPreparedTask(this, context, target, command.Operation, [], m_time,
                    "Availability check only; no command.");
                await candidate.AuthorizeAsync(context, m_policy, lifetime.Token).ConfigureAwait(false);
                operations.Add(command.Operation);
                reason = "Available on the explicitly authorized owned simulation; prepare and confirm.";
            }
            catch (UnauthorizedAccessException)
            {
                reason = "Requires an authenticated, explicitly authorized owned simulation.";
                quality = StatusCodes.BadUserAccessDenied;
            }
            catch (ServiceResultException failure)
            {
                reason = "Not available: " + failure.StatusCode;
                quality = failure.StatusCode;
            }
            values.Add(new CompanionValue(command.Operation.DisplayName, Variant.From(reason)) { Quality = quality });
        }
        CellCompanionSupport.CheckCount(values.Count, context.MaxFields, "Robotics operation inspection");
        return new CompanionInspection([.. values], [.. operations],
            "Published OPC 40010 operation/program state. No command was sent. Optional methods may be absent; " +
            "MinimalRobotServer currently registers no program/operation handlers. Robot Intent is a separate draft.");
    }

    private static void AddProgramValue(List<CompanionValue> values, string name, in DataValue value, NodeId source)
    {
        values.Add(new CompanionValue(name, value.WrappedValue)
        {
            SourceNodeId = source,
            Quality = source.IsNull ? StatusCodes.BadNotSupported : value.StatusCode,
            SourceTimestamp = value.SourceTimestamp.IsNull ? null : value.SourceTimestamp.ToDateTime()
        });
    }

    private static Command FindCommand(CompanionTarget target, string operationId)
    {
        foreach (Command command in s_commands)
        {
            if (target.ProviderId == "robotics" && target.TypeName == command.Kind &&
                operationId == command.Operation.Id)
            {
                return command;
            }
        }
        throw new ServiceResultException(
            StatusCodes.BadNotSupported, "Select a supported published Robotics operation.");
    }

    private static void ValidateCommandInput(Command command, ArrayOf<CompanionValue> inputs)
    {
        if (command.Operation.Id == "load-program" &&
            (!inputs[0].Value.TryGetValue(out string? name) ||
                string.IsNullOrWhiteSpace(name) || name.Length > 128))
        {
            throw new ArgumentException("Supply a program name of 1 through 128 characters.", nameof(inputs));
        }
        if (command.Name == R.BrowseNames.Stop &&
            (!inputs[0].Value.TryGetValue(out long mode) || mode is < 0 or > 3))
        {
            throw new ArgumentException("Supply a published RoboticsStopMode value from 0 through 3.", nameof(inputs));
        }
    }

    private static async ValueTask<RoboticsMethodBinding> ReadCommandBindingAsync(
        CompanionContext context, CompanionTarget target, Command command, CancellationToken cancellationToken)
    {
        RoboticsMethodBinding state = await ReadStateBindingAsync(context, target, cancellationToken)
            .ConfigureAwait(false);
        NodeId expected = ExpandedNodeId.ToNodeId(command.RequiredState, context.Session.NamespaceUris);
        if (state.Values[1] != Variant.From(expected))
        {
            throw new ServiceResultException(
                StatusCodes.BadInvalidState, "The operation is not available in this state.");
        }
        NodeId method = await RequireMethodAsync(context, state.Owner, R.Namespaces.Robotics,
            command.Name, command.ArgumentTypes, [Opc.Ua.DataTypeIds.Int32], cancellationToken).ConfigureAwait(false);
        return state with { Method = method };
    }

    private static async ValueTask<RoboticsMethodBinding> ReadStateBindingAsync(
        CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
    {
        await IndustrialCompanionAccess.RequireTargetAsync(
            context, target, "robotics", s_workflowTypes, cancellationToken).ConfigureAwait(false);
        NodeId owner;
        if (target.TypeName == SystemOperationKind)
        {
            var operation = new SystemOperationTypeClient(context.Session, target.NodeId, context.Telemetry);
            SystemOperationStateMachineTypeClient? machine = await operation.GetSystemOperationStateMachineAsync(
                context.Telemetry, cancellationToken).ConfigureAwait(false);
            owner = machine?.ObjectId ?? NodeId.Null;
        }
        else
        {
            var task = new TaskControlTypeClient(context.Session, target.NodeId, context.Telemetry);
            TaskControlOperationTypeClient? operation = await task.GetTaskControlOperationAsync(
                context.Telemetry, cancellationToken).ConfigureAwait(false);
            TaskControlStateMachineTypeClient? machine = operation is null ? null :
                await operation.GetTaskControlStateMachineAsync(context.Telemetry, cancellationToken)
                    .ConfigureAwait(false);
            owner = machine?.ObjectId ?? NodeId.Null;
        }
        if (owner.IsNull)
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "The optional operation state machine is absent.");
        }
        string kind = target.TypeName == SystemOperationKind ? "SystemStateMachine" : "TaskStateMachine";
        await IndustrialCompanionAccess.RequireTargetAsync(context,
            new CompanionTarget("robotics", owner, kind, kind), "robotics", s_machineTypes, cancellationToken)
            .ConfigureAwait(false);
        NodeId current = await IndustrialCompanionAccess.ResolveChildAsync(
            context, owner, Opc.Ua.Namespaces.OpcUa, Opc.Ua.BrowseNames.CurrentState, false, cancellationToken)
            .ConfigureAwait(false);
        NodeId id = await IndustrialCompanionAccess.ResolveChildAsync(
            context, current, Opc.Ua.Namespaces.OpcUa, Opc.Ua.BrowseNames.Id, false, cancellationToken)
            .ConfigureAwait(false);
        DataValue label = await ReadOneAsync(context, current, Attributes.Value, cancellationToken)
            .ConfigureAwait(false);
        DataValue identity = await ReadOneAsync(context, id, Attributes.Value, cancellationToken).ConfigureAwait(false);
        if (!label.WrappedValue.TryGetValue(out LocalizedText text) || text.IsNull ||
            text.Text is { Length: > 128 } ||
            !identity.WrappedValue.TryGetValue(out NodeId state) || state.IsNull)
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "The Robotics state is malformed or inactive.");
        }
        return new RoboticsMethodBinding(
            owner, NodeId.Null, [current, id], [label.WrappedValue, identity.WrappedValue]);
    }

    private static ValueTask<int> DispatchCommandAsync(
        ISessionClient client, ITelemetryContext telemetry, RoboticsPreparedTask task,
        CancellationToken cancellationToken)
    {
        var system = new SystemOperationStateMachineTypeClient(client, task.Binding.Owner, telemetry);
        var program = new TaskControlStateMachineTypeClient(client, task.Binding.Owner, telemetry);
        ArrayOf<CompanionValue> inputs = task.Draft.Inputs;
        return task.Draft.Operation.Id switch
        {
            "get-ready" => system.GetReadyAsync(cancellationToken),
            "start-system" => system.StartAsync(cancellationToken),
            "stand-down" => system.StandDownAsync(cancellationToken),
            "stop-system" => system.StopAsync(StopMode(inputs), cancellationToken),
            "load-program" => program.LoadByNameAsync(ProgramName(inputs), cancellationToken),
            "start-program" => program.StartAsync(cancellationToken),
            "stop-program" => program.StopAsync(StopMode(inputs), cancellationToken),
            "unload-program" => program.UnloadProgramAsync(cancellationToken),
            _ => throw new ServiceResultException(StatusCodes.BadNotSupported)
        };
    }

    private static long StopMode(ArrayOf<CompanionValue> inputs)
    {
        return inputs[0].Value.TryGetValue(out long mode) ? mode :
            throw new InvalidOperationException("The prepared stop mode is missing.");
    }

    private static string ProgramName(ArrayOf<CompanionValue> inputs)
    {
        return inputs[0].Value.TryGetValue(out string name) ? name :
            throw new InvalidOperationException("The prepared program name is missing.");
    }

    private static void ValidateStatusOutput(ArrayOf<Variant> outputs)
    {
        if (outputs.Count != 1 || !outputs[0].TryGetValue(out int _))
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError,
                "The Robotics operation outcome is unknown. Inspect; do not repeat it.");
        }
    }

    /// <summary>
    /// Published system-operation target kind, distinct from Robot Intent.
    /// </summary>
    internal const string SystemOperationKind = "SystemOperation";

    /// <summary>
    /// Published task-control target kind, distinct from Robot Intent.
    /// </summary>
    internal const string TaskControlKind = "TaskControl";

    private readonly IRoboticsSimulationPolicy? m_policy;
    private readonly TimeProvider m_time;

    private static readonly ArrayOf<IndustrialCompanionType> s_workflowTypes =
    [
        new(R.ObjectTypeIds.SystemOperationType, SystemOperationKind),
        new(R.ObjectTypeIds.TaskControlType, TaskControlKind)
    ];

    private static readonly ArrayOf<IndustrialCompanionType> s_machineTypes =
    [
        new(R.ObjectTypeIds.SystemOperationStateMachineType, "SystemStateMachine"),
        new(R.ObjectTypeIds.TaskControlStateMachineType, "TaskStateMachine")
    ];

    private static readonly ArrayOf<Command> s_commands =
    [
        new("get-ready", "Get ready (simulation)", SystemOperationKind, R.BrowseNames.GetReady,
            R.MethodIds.SystemOperationStateMachineType_GetReady, R.SystemOperationStateMachineTypeIds.StateIds.Idle),
        new("start-system", "Start system (simulation)", SystemOperationKind, R.BrowseNames.Start,
            R.MethodIds.SystemOperationStateMachineType_Start, R.SystemOperationStateMachineTypeIds.StateIds.Ready),
        new("stop-system", "Stop system (simulation)", SystemOperationKind, R.BrowseNames.Stop,
            R.MethodIds.SystemOperationStateMachineType_Stop, R.SystemOperationStateMachineTypeIds.StateIds.Executing),
        new("stand-down", "Stand down (simulation)", SystemOperationKind, R.BrowseNames.StandDown,
            R.MethodIds.SystemOperationStateMachineType_StandDown, R.SystemOperationStateMachineTypeIds.StateIds.Ready),
        new("load-program", "Load program (simulation)", TaskControlKind, R.BrowseNames.LoadByName,
            R.MethodIds.TaskControlStateMachineType_LoadByName, R.TaskControlStateMachineTypeIds.StateIds.Idle),
        new("start-program", "Start program (simulation)", TaskControlKind, R.BrowseNames.Start,
            R.MethodIds.TaskControlStateMachineType_Start, R.TaskControlStateMachineTypeIds.StateIds.Ready),
        new("stop-program", "Stop program (simulation)", TaskControlKind, R.BrowseNames.Stop,
            R.MethodIds.TaskControlStateMachineType_Stop, R.TaskControlStateMachineTypeIds.StateIds.Executing),
        new("unload-program", "Unload program (simulation)", TaskControlKind, R.BrowseNames.UnloadProgram,
            R.MethodIds.TaskControlStateMachineType_UnloadProgram, R.TaskControlStateMachineTypeIds.StateIds.Ready)
    ];

    /// <summary>
    /// A published generated method and its exact scalar input contract.
    /// </summary>
    private sealed class Command
    {
        /// <summary>
        /// Binds a published method to its applicable owner, state and primitive form inputs.
        /// </summary>
        public Command(
            string id, string label, string kind, string name, ExpandedNodeId declaration, ExpandedNodeId state)
        {
            Kind = kind;
            Name = name;
            Declaration = declaration;
            RequiredState = state;
            ArgumentTypes = id == "load-program" ? [Opc.Ua.DataTypeIds.String] :
                name == R.BrowseNames.Stop ? [Opc.Ua.DataTypeIds.Int64] : [];
            Operation = new CompanionOperation(id, label, CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = id == "load-program"
                    ? [new("program", "Program name", BuiltInType.String, "Published program name; 1–128 characters.")]
                    : name == R.BrowseNames.Stop
                        ? [new("stopMode", "Stop mode", BuiltInType.Int64,
                            "RoboticsStopMode: 0–3; not a safety category.")]
                        : []
            };
        }

        /// <summary>
        /// Gets the published owner kind.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Gets the model-qualified method BrowseName.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the generated method declaration.
        /// </summary>
        public ExpandedNodeId Declaration { get; }

        /// <summary>
        /// Gets the published prerequisite state identity.
        /// </summary>
        public ExpandedNodeId RequiredState { get; }

        /// <summary>
        /// Gets the explicit prepared-operation form.
        /// </summary>
        public CompanionOperation Operation { get; }

        /// <summary>
        /// Gets the scalar input types required from live method metadata.
        /// </summary>
        public ArrayOf<NodeId> ArgumentTypes { get; }
    }
}
