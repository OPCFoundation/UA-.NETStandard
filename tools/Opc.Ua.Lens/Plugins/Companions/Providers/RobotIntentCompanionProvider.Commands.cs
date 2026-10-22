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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.RobotIntent;
using RI = Opc.Ua.RobotIntent;
using UaTypes = Opc.Ua.DataTypeIds;

namespace UaLens.Plugins.Companions.Providers;

internal sealed partial class RobotIntentCompanionProvider
{
    private static async ValueTask<CompanionOperationResult> DispatchAsync(
        CompanionContext context, ISessionClient client, Command command, RoboticsPreparedTask task,
        CancellationToken cancellationToken)
    {
        var controller = new IntentControllerTypeClient(client, task.Binding.Owner, context.Telemetry);
        ArrayOf<CompanionValue> inputs = task.Draft.Inputs;
        if (command.Id is "submit-wait" or "submit-wait-mission")
        {
            (bool accepted, string id, NodeId operation, IntentFailureEnum failure, LocalizedText message) =
                command.IsMission
                    ? await controller.SubmitMissionAsync(CreateMission(inputs), cancellationToken)
                        .ConfigureAwait(false)
                    : await controller.SubmitIntentAsync(CreateWait(inputs), cancellationToken).ConfigureAwait(false);
            return new CompanionOperationResult(
                accepted
                    ? "The simulator admitted the wait-only work. " +
                        "Admission is not completion; inspect its operation. No replay."
                    : "The simulator refused admission. No success or completion is claimed.",
                [
                    new("Accepted", Variant.From(accepted)),
                    new("Id", Variant.From(id)),
                    new("Operation", Variant.From(operation)),
                    new("Failure", Variant.From(failure.ToString())),
                    new("Message", Variant.From(message))
                ]);
        }
        if (command.Id == "request-control")
        {
            (bool granted, NodeId owner) = await controller.RequestControlAsync(cancellationToken)
                .ConfigureAwait(false);
            return new CompanionOperationResult(
                granted
                    ? "Command authority granted to this session. " +
                        "This is arbitration, not authorization or a timed lease."
                    : "Command authority refused. No authority or authorization was acquired.",
                [new("Granted", Variant.From(granted)), new("Control owner", Variant.From(owner))]);
        }
        if (command.Id == "release-control")
        {
            await controller.ReleaseControlAsync(cancellationToken).ConfigureAwait(false);
            return new CompanionOperationResult(
                "The simulator acknowledged the explicit authority release. " +
                "Inspect ControlOwner; no work was replayed.", []);
        }
        if (command.Id is "cancel-intent" or "cancel-mission")
        {
            string id = BoundSubjectId(task);
            if (!inputs[1].Value.TryGetValue(out int mode))
            {
                throw new InvalidOperationException("The prepared stop mode is missing.");
            }
            bool accepted = command.IsMission
                ? await controller.CancelMissionAsync(id, (StopModeEnum)mode, cancellationToken).ConfigureAwait(false)
                : await controller.CancelIntentAsync(id, (StopModeEnum)mode, cancellationToken).ConfigureAwait(false);
            return new CompanionOperationResult(
                accepted ? "Cancellation accepted; inspect the selected operation for its terminal state." :
                    "Cancellation refused; no terminal state is claimed.",
                [new("Accepted", Variant.From(accepted)), new("Id", Variant.From(id))]);
        }
        if (command.Id == "open-channel")
        {
            (bool granted, string endpoint, string descriptor, DateTimeUtc expiry, LocalizedText message) =
                await controller.OpenRealTimeChannelAsync(
                    BoundSubjectId(task), DoubleInput(inputs, 1), cancellationToken).ConfigureAwait(false);
            return new CompanionOperationResult(
                granted
                    ? "Finite broker lease granted. " +
                        "No transport was connected, no data published and no renewal scheduled."
                    : "Broker lease refused. No transport was connected or renewal scheduled.",
                [
                    new("Granted", Variant.From(granted)),
                    new("Endpoint", Variant.From(endpoint)),
                    new("Payload descriptor", Variant.From(descriptor)),
                    new("Lease expiry", Variant.From(expiry)),
                    new("Message", Variant.From(message))
                ]);
        }
        if (command.Id == "close-channel")
        {
            bool closed = await controller.CloseRealTimeChannelAsync(BoundSubjectId(task), cancellationToken)
                .ConfigureAwait(false);
            return new CompanionOperationResult(closed ? "The simulator closed the selected broker lease." :
                "The simulator refused the broker lease close.",
                [new("Closed", Variant.From(closed))]);
        }
        throw new ServiceResultException(StatusCodes.BadNotSupported, "No draft simulation command is selected.");
    }

    private void ValidateOutput(
        CompanionContext context, Command command, RoboticsPreparedTask task, ArrayOf<Variant> outputs)
    {
        bool valid = outputs.Count == command.OutputTypes.Count;
        if (valid && command.Id is "submit-wait" or "submit-wait-mission")
        {
            valid = outputs[0].TryGetValue(out bool accepted) &&
                outputs[1].TryGetValue(out string id) && id is { Length: <= 128 } &&
                outputs[2].TryGetValue(out NodeId operation) &&
                outputs[3].TryGetValue(out int failure) && Enum.IsDefined((IntentFailureEnum)failure) &&
                outputs[4].TryGetValue(out LocalizedText message) && message.Text is not { Length: > 2048 } &&
                (accepted
                    ? id == StringInput(task.Draft.Inputs, 0) && !operation.IsNull &&
                        operation.NamespaceIndex < context.Session.NamespaceUris.Count &&
                        failure == (int)IntentFailureEnum.None
                    : operation.IsNull && failure != (int)IntentFailureEnum.None);
        }
        else if (valid && command.Id == "request-control")
        {
            valid = outputs[0].TryGetValue(out bool granted) && outputs[1].TryGetValue(out NodeId owner) &&
                (!granted || owner == context.Session.SessionId);
        }
        else if (valid && command.Id == "open-channel")
        {
            valid = outputs[0].TryGetValue(out bool granted) &&
                outputs[1].TryGetValue(out string endpoint) && endpoint is { Length: <= 2048 } &&
                outputs[2].TryGetValue(out string descriptor) && descriptor is { Length: <= 4096 } &&
                outputs[3].TryGetValue(out DateTimeUtc expiry) &&
                outputs[4].TryGetValue(out LocalizedText message) && message.Text is not { Length: > 2048 } &&
                (!granted || (!expiry.IsNull &&
                    expiry.ToDateTime() > m_time.GetUtcNow().UtcDateTime &&
                    expiry.ToDateTime() <= m_time.GetUtcNow().AddSeconds(15).UtcDateTime &&
                    Uri.TryCreate(endpoint, UriKind.Absolute, out _)));
        }
        else if (valid && command.Id is "cancel-intent" or "cancel-mission" or "close-channel")
        {
            valid = outputs[0].TryGetValue(out bool _);
        }
        if (!valid)
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError,
                "The draft operation outcome is unknown or inconsistent. Inspect; do not replay the preparation.");
        }
    }

    private static string BoundSubjectId(RoboticsPreparedTask task)
    {
        return task.Binding.Values[^1].TryGetValue(out string id) ? id :
            throw new InvalidOperationException("The prepared instance correlation is missing.");
    }

    private static Command FindCommand(string id)
    {
        foreach (Command command in s_commands)
        {
            if (command.Id == id)
            {
                return command;
            }
        }
        throw new ServiceResultException(StatusCodes.BadNotSupported,
            "Only local previews and bounded wait, authority or broker-lease tasks are supported.");
    }

    private static readonly ArrayOf<Command> s_commands =
    [
        new("preview-wait", "Preview finite wait locally", "SubmitIntent",
            RI.MethodIds.IntentControllerType_SubmitIntent),
        new("preview-wait-mission", "Preview wait-only mission locally", "SubmitMission",
            RI.MethodIds.IntentControllerType_SubmitMission),
        new("request-control", "Request authority (simulation)", "RequestControl",
            RI.MethodIds.IntentControllerType_RequestControl),
        new("submit-wait", "Submit finite wait (simulation)", "SubmitIntent",
            RI.MethodIds.IntentControllerType_SubmitIntent),
        new("submit-wait-mission", "Submit wait-only mission (simulation)", "SubmitMission",
            RI.MethodIds.IntentControllerType_SubmitMission),
        new("cancel-intent", "Cancel selected wait (simulation)", "CancelIntent",
            RI.MethodIds.IntentControllerType_CancelIntent),
        new("cancel-mission", "Cancel selected wait-only mission (simulation)", "CancelMission",
            RI.MethodIds.IntentControllerType_CancelMission),
        new("release-control", "Release authority (simulation)", "ReleaseControl",
            RI.MethodIds.IntentControllerType_ReleaseControl),
        new("open-channel", "Open finite broker lease (no transport)", "OpenRealTimeChannel",
            RI.MethodIds.IntentControllerType_OpenRealTimeChannel),
        new("close-channel", "Close selected broker lease", "CloseRealTimeChannel",
            RI.MethodIds.IntentControllerType_CloseRealTimeChannel)
    ];

    /// <summary>
    /// A generated draft method with a deliberately narrower nonphysical input surface.
    /// </summary>
    private sealed class Command
    {
        /// <summary>
        /// Binds a draft method to a nonphysical form and its exact wire signature.
        /// </summary>
        public Command(string id, string label, string name, ExpandedNodeId declaration)
        {
            Id = id;
            Name = name;
            Declaration = declaration;
            Operation = new CompanionOperation(id, label,
                IsPreview ? CompanionOperationSafety.ReadOnly : CompanionOperationSafety.DeploymentMutation)
            {
                Inputs = Inputs()
            };
            InputTypes = name switch
            {
                "SubmitIntent" => [RI.DataTypeIds.IntentDataType],
                "SubmitMission" => [RI.DataTypeIds.MissionDataType],
                "CancelIntent" or "CancelMission" => [UaTypes.String, RI.DataTypeIds.StopModeEnum],
                "OpenRealTimeChannel" => [UaTypes.String, UaTypes.Duration],
                "CloseRealTimeChannel" => [UaTypes.String],
                _ => []
            };
            OutputTypes = name switch
            {
                "SubmitIntent" or "SubmitMission" =>
                    [UaTypes.Boolean, UaTypes.String, UaTypes.NodeId,
                        RI.DataTypeIds.IntentFailureEnum, UaTypes.LocalizedText],
                "RequestControl" => [UaTypes.Boolean, UaTypes.NodeId],
                "ReleaseControl" => [],
                "OpenRealTimeChannel" =>
                    [UaTypes.Boolean, UaTypes.String, UaTypes.String, UaTypes.UtcTime, UaTypes.LocalizedText],
                _ => [UaTypes.Boolean]
            };
        }

        /// <summary>
        /// Gets the stable task identifier.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the draft method BrowseName.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the generated method declaration.
        /// </summary>
        public ExpandedNodeId Declaration { get; }

        /// <summary>
        /// Gets the explicit input form and safety classification.
        /// </summary>
        public CompanionOperation Operation { get; }

        /// <summary>
        /// Gets whether this is a purely local preview.
        /// </summary>
        public bool IsPreview => Id.StartsWith("preview-", StringComparison.Ordinal);

        /// <summary>
        /// Gets whether this task constructs bounded wait-only data.
        /// </summary>
        public bool IsWait => Name is "SubmitIntent" or "SubmitMission";

        /// <summary>
        /// Gets whether the method concerns a mission rather than one intent.
        /// </summary>
        public bool IsMission => Name is "SubmitMission" or "CancelMission";

        /// <summary>
        /// Gets the scalar input types required from live method metadata.
        /// </summary>
        public ArrayOf<ExpandedNodeId> InputTypes { get; }

        /// <summary>
        /// Gets the scalar output types required from live method metadata.
        /// </summary>
        public ArrayOf<ExpandedNodeId> OutputTypes { get; }

        private ArrayOf<CompanionInputDefinition> Inputs()
        {
            if (IsWait)
            {
                ArrayOf<CompanionInputDefinition> wait =
                [
                    new("id", "Correlation id", BuiltInType.String,
                        "Unique id, 1–96 characters. Never reuse after an unknown outcome."),
                    new("duration", "Wait per step (ms)", BuiltInType.Double,
                        "Finite, positive; total at most 10000 ms.")
                ];
                return IsMission
                    ? [.. wait, new("count", "Wait steps", BuiltInType.UInt32, "1–8 released wait-only steps.")] :
                    wait;
            }
            if (Id is "cancel-intent" or "cancel-mission")
            {
                return
                [
                    new("operation", "Selected operation", BuiltInType.NodeId,
                        "NodeId listed by this controller; wait-only."),
                    new("stopMode", "Stop mode", BuiltInType.Int32,
                        "Draft StopModeEnum 1–5; not a safety stop category.")
                ];
            }
            if (Id is "open-channel" or "close-channel")
            {
                ArrayOf<CompanionInputDefinition> channel =
                    [new("channel", "Selected channel", BuiltInType.NodeId, "NodeId listed by this controller.")];
                return Id == "open-channel" ? [.. channel,
                    new("lease", "Requested lease (ms)", BuiltInType.Double,
                        "100–10000 ms. No transport connection or automatic renewal.")] :
                    channel;
            }
            return [];
        }
    }
}
