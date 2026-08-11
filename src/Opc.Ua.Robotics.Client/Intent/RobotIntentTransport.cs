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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Client.Intent
{
    /// <summary>
    /// Transport abstraction used by the high-level Robot Intent client.
    /// </summary>
    public interface IRobotIntentTransport
    {
        /// <summary>
        /// Raised when a ManagedSession reconnects.
        /// </summary>
        event RobotIntentReconnectHandler? Reconnected;

        /// <summary>
        /// Gets the source-generated logging sink.
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Gets the controller node.
        /// </summary>
        NodeId ControllerId { get; }

        /// <summary>
        /// Gets the session namespace table.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Gets the service message context used to decode ExtensionObject bodies.
        /// </summary>
        IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Browses the RobotIntent entry point for controllers.
        /// </summary>
        ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseControllersAsync(CancellationToken ct = default);

        /// <summary>
        /// Reads the complete controller capability snapshot.
        /// </summary>
        ValueTask<RobotIntentControllerInfo> ReadControllerAsync(CancellationToken ct = default);

        /// <summary>
        /// Reads the current controller runtime state.
        /// </summary>
        ValueTask<RobotIntentControllerState> ReadControllerStateAsync(CancellationToken ct = default);

        /// <summary>
        /// Lists operations published below the controller's Intents folder.
        /// </summary>
        ValueTask<ArrayOf<IntentOperationSnapshot>> ListOperationsAsync(CancellationToken ct = default);

        /// <summary>
        /// Lists missions published below the controller's Missions folder.
        /// </summary>
        ValueTask<ArrayOf<MissionSnapshot>> ListMissionsAsync(CancellationToken ct = default);

        /// <summary>
        /// Submits an intent.
        /// </summary>
        ValueTask<IntentSubmissionResult> SubmitIntentAsync(IntentDataType intent, CancellationToken ct = default);

        /// <summary>
        /// Cancels an intent.
        /// </summary>
        ValueTask<IntentCommandOutcome> CancelIntentAsync(
            string intentId,
            StopModeEnum stopMode,
            CancellationToken ct = default);

        /// <summary>
        /// Cancels all work.
        /// </summary>
        ValueTask<uint> CancelAllAsync(StopModeEnum stopMode, CancellationToken ct = default);

        /// <summary>
        /// Pauses execution.
        /// </summary>
        ValueTask<IntentCommandOutcome> PauseAsync(CancellationToken ct = default);

        /// <summary>
        /// Resumes execution.
        /// </summary>
        ValueTask<IntentCommandOutcome> ResumeAsync(CancellationToken ct = default);

        /// <summary>
        /// Retries an intent.
        /// </summary>
        ValueTask<IntentSubmissionResult> RetryAsync(string intentId, CancellationToken ct = default);

        /// <summary>
        /// Submits a mission.
        /// </summary>
        ValueTask<MissionSubmissionResult> SubmitMissionAsync(MissionDataType mission, CancellationToken ct = default);

        /// <summary>
        /// Updates a mission horizon.
        /// </summary>
        ValueTask<MissionUpdateOutcome> UpdateMissionAsync(
            string missionId,
            uint missionUpdateId,
            ArrayOf<MissionStepDataType> steps,
            CancellationToken ct = default);

        /// <summary>
        /// Cancels a mission.
        /// </summary>
        ValueTask<IntentCommandOutcome> CancelMissionAsync(
            string missionId,
            StopModeEnum stopMode,
            CancellationToken ct = default);

        /// <summary>
        /// Requests command authority.
        /// </summary>
        ValueTask<CommandAuthorityOutcome> RequestControlAsync(CancellationToken ct = default);

        /// <summary>
        /// Releases command authority.
        /// </summary>
        ValueTask ReleaseControlAsync(CancellationToken ct = default);

        /// <summary>
        /// Opens or renews a real-time channel lease.
        /// </summary>
        ValueTask<RealTimeChannelOpenResult> OpenRealTimeChannelAsync(
            string channelId,
            double requestedLease,
            CancellationToken ct = default);

        /// <summary>
        /// Closes a real-time channel.
        /// </summary>
        ValueTask<bool> CloseRealTimeChannelAsync(string channelId, CancellationToken ct = default);

        /// <summary>
        /// Resolves a child value node below a root object.
        /// </summary>
        ValueTask<NodeId> ResolveChildAsync(NodeId root, string browseName, CancellationToken ct = default);

        /// <summary>
        /// Reads an operation snapshot from the server.
        /// </summary>
        ValueTask<IntentOperationSnapshot> ReadOperationSnapshotAsync(NodeId operation, CancellationToken ct = default);

        /// <summary>
        /// Reads the current control owner.
        /// </summary>
        ValueTask<NodeId> ReadControlOwnerAsync(CancellationToken ct = default);

        /// <summary>
        /// Subscribes to data changes.
        /// </summary>
        IAsyncEnumerable<RobotIntentDataChange> SubscribeDataChangesAsync(
            ArrayOf<NodeId> nodeIds,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Default OPC UA implementation of the Robot Intent transport.
    /// </summary>
    public sealed class UaRobotIntentTransport : IRobotIntentTransport
    {
        /// <summary>
        /// Creates a transport for a controller.
        /// </summary>
        public UaRobotIntentTransport(
            ISession session,
            NodeId controllerId,
            ITelemetryContext telemetry,
            IStreamingSubscription? streaming = null,
            bool observeReconnect = true)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            ControllerId = controllerId;
            telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Logger = telemetry.CreateLogger<UaRobotIntentTransport>();
            m_streaming = streaming ?? RoboticsClient.GetDefaultStreaming(session);
            m_proxy = new IntentControllerTypeClient(session, controllerId, telemetry);
            if (observeReconnect && session is ManagedSession managedSession)
            {
                managedSession.ConnectionStateChanged += OnConnectionStateChanged;
            }
        }

        /// <inheritdoc/>
        public event RobotIntentReconnectHandler? Reconnected;

        /// <inheritdoc/>
        public ILogger Logger { get; }

        /// <inheritdoc/>
        public NodeId ControllerId { get; }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_session.NamespaceUris;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext => m_session.MessageContext;

        private ushort NamespaceIndex
        {
            get
            {
                int index = m_session.NamespaceUris.GetIndex(RobotIntentNamespace);
                return index < 0 ? (ushort)0 : (ushort)index;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseControllersAsync(
            CancellationToken ct = default)
        {
            NodeId server = await TranslateAsync(
                global::Opc.Ua.ObjectIds.ObjectsFolder,
                new List<QualifiedName> { new("Server", 0) },
                ct).ConfigureAwait(false);
            NodeId controllers = server.IsNull
                ? NodeId.Null
                : await TranslateAsync(
                    server,
                    new List<QualifiedName>
                    {
                        new("RobotIntent", NamespaceIndex),
                        new("Controllers", NamespaceIndex)
                    },
                    ct).ConfigureAwait(false);
            return controllers.IsNull ? [] : await BrowseFolderAsync(controllers, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<RobotIntentControllerInfo> ReadControllerAsync(CancellationToken ct = default)
        {
            ArrayOf<IntentCapabilityDataType> supported = await ReadSupportedIntentsAsync(
                ControllerId,
                ["Capabilities", "SupportedIntents"],
                ct).ConfigureAwait(false);
            uint axisCount = await ReadChildValueOrDefaultAsync(ControllerId, ["Capabilities", "AxisCount"], 0u, ct)
                .ConfigureAwait(false);
            uint maxQueueDepth = await ReadChildValueOrDefaultAsync(ControllerId, ["MaxQueueDepth"], 0u, ct)
                .ConfigureAwait(false);

            RobotIntentLookups lookups = new()
            {
                Frames = await BrowseOptionalFolderAsync("Frames", ct).ConfigureAwait(false),
                Tools = await BrowseOptionalFolderAsync("Tools", ct).ConfigureAwait(false),
                Locations = await BrowseOptionalFolderAsync("Locations", ct).ConfigureAwait(false),
                Axes = await BrowseOptionalFolderAsync("Axes", ct).ConfigureAwait(false),
                Outputs = await BrowseOptionalFolderAsync("Outputs", ct).ConfigureAwait(false),
                Programs = await BrowseOptionalFolderAsync("Programs", ct).ConfigureAwait(false)
            };
            lookups = lookups with { FramesByFrameId = lookups.Frames };

            RobotIntentControllerInfo info = new()
            {
                NodeId = ControllerId,
                BrowseName = QualifiedName.Null,
                SupportedIntents = supported,
                AxisCount = axisCount,
                MaxQueueDepth = maxQueueDepth,
                MissionsSupported = await ReadChildValueOrDefaultAsync(
                    ControllerId,
                    ["Capabilities", "MissionsSupported"],
                    false,
                    ct).ConfigureAwait(false),
                MissionHorizonSupported = await ReadChildValueOrDefaultAsync(
                    ControllerId,
                    ["Capabilities", "MissionHorizonSupported"],
                    false,
                    ct).ConfigureAwait(false),
                MissionBranchingSupported = await ReadChildValueOrDefaultAsync(
                    ControllerId,
                    ["Capabilities", "MissionBranchingSupported"],
                    false,
                    ct).ConfigureAwait(false),
                BlendingSupported = await ReadChildValueOrDefaultAsync(
                    ControllerId,
                    ["Capabilities", "BlendingSupported"],
                    false,
                    ct).ConfigureAwait(false),
                TrajectorySupported = await ReadChildValueOrDefaultAsync(
                    ControllerId,
                    ["Capabilities", "TrajectorySupported"],
                    false,
                    ct).ConfigureAwait(false),
                ForceControlSupported = await ReadChildValueOrDefaultAsync(
                    ControllerId,
                    ["Capabilities", "ForceControlSupported"],
                    false,
                    ct).ConfigureAwait(false),
                RealTimeChannelsSupported = await ReadChildValueOrDefaultAsync(
                    ControllerId,
                    ["Capabilities", "RealTimeChannelsSupported"],
                    false,
                    ct).ConfigureAwait(false),
                MaxTrajectoryPoints = await ReadChildValueOrDefaultAsync(
                    ControllerId,
                    ["Capabilities", "MaxTrajectoryPoints"],
                    0u,
                    ct).ConfigureAwait(false),
                SupportedFacets = await ReadChildValueOrDefaultAsync<ArrayOf<string>>(
                    ControllerId,
                    ["Capabilities", "SupportedFacets"],
                    [],
                    ct).ConfigureAwait(false),
                Lookups = lookups
            };
            return info with { Facets = RobotIntentRules.DeriveFacets(info) };
        }

        /// <inheritdoc/>
        public async ValueTask<RobotIntentControllerState> ReadControllerStateAsync(CancellationToken ct = default)
        {
            return new RobotIntentControllerState
            {
                ControllerId = ControllerId,
                OperationalMode = await ReadOptionalEnumChildAsync<OperationalModeEnum>(
                    ControllerId,
                    ["OperationalMode"],
                    ct).ConfigureAwait(false),
                Ready = await ReadOptionalChildAsync<bool>(ControllerId, ["Ready"], ct).ConfigureAwait(false),
                ControlOwner = await ReadOptionalNodeIdChildAsync(ControllerId, ["ControlOwner"], ct)
                    .ConfigureAwait(false),
                MaxQueueDepth = await ReadOptionalChildAsync<uint>(ControllerId, ["MaxQueueDepth"], ct)
                    .ConfigureAwait(false),
                ActiveIntent = await ReadOptionalNodeIdChildAsync(ControllerId, ["ActiveIntent"], ct)
                    .ConfigureAwait(false),
                ActiveMission = await ReadOptionalNodeIdChildAsync(ControllerId, ["ActiveMission"], ct)
                    .ConfigureAwait(false),
                SafetyState = await ReadSafetyStateAsync(ct).ConfigureAwait(false),
                Operations = await BrowseOptionalFolderAsync("Intents", ct).ConfigureAwait(false),
                Missions = await BrowseOptionalFolderAsync("Missions", ct).ConfigureAwait(false)
            };
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<IntentOperationSnapshot>> ListOperationsAsync(CancellationToken ct = default)
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries = await BrowseOptionalFolderAsync("Intents", ct)
                .ConfigureAwait(false);
            var operationIds = new List<NodeId>(entries.Count);
            for (int ii = 0; ii < entries.Count; ii++)
            {
                operationIds.Add(entries[ii].NodeId);
            }

            var snapshots = new List<IntentOperationSnapshot>(operationIds.Count);
            for (int ii = 0; ii < operationIds.Count; ii++)
            {
                IntentOperationSnapshot snapshot = await ReadOperationSnapshotAsync(operationIds[ii], ct)
                    .ConfigureAwait(false);
                snapshots.Add(snapshot);
            }
            return [.. snapshots];
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<MissionSnapshot>> ListMissionsAsync(CancellationToken ct = default)
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries = await BrowseOptionalFolderAsync("Missions", ct)
                .ConfigureAwait(false);
            var missionIds = new List<NodeId>(entries.Count);
            for (int ii = 0; ii < entries.Count; ii++)
            {
                missionIds.Add(entries[ii].NodeId);
            }

            var snapshots = new List<MissionSnapshot>(missionIds.Count);
            for (int ii = 0; ii < missionIds.Count; ii++)
            {
                snapshots.Add(await ReadMissionSnapshotAsync(missionIds[ii], ct).ConfigureAwait(false));
            }
            return [.. snapshots];
        }

        /// <inheritdoc/>
        public async ValueTask<IntentSubmissionResult> SubmitIntentAsync(
            IntentDataType intent,
            CancellationToken ct = default)
        {
            (bool accepted, string intentId, NodeId operation, IntentFailureEnum failure, LocalizedText message) =
                await m_proxy.SubmitIntentAsync(intent, ct).ConfigureAwait(false);
            if (accepted)
            {
                Logger.IntentSubmitted(intentId, operation);
            }
            else
            {
                Logger.IntentRefused(failure, message.Text ?? string.Empty);
            }
            return new IntentSubmissionResult
            {
                Accepted = accepted,
                IntentId = intentId ?? string.Empty,
                Operation = operation.IsNull ? NodeId.Null : operation,
                Failure = failure,
                Message = message.IsNull ? LocalizedText.Null : message
            };
        }

        /// <inheritdoc/>
        public async ValueTask<IntentCommandOutcome> CancelIntentAsync(
            string intentId,
            StopModeEnum stopMode,
            CancellationToken ct = default)
        {
            return new IntentCommandOutcome(await m_proxy.CancelIntentAsync(intentId, stopMode, ct)
                .ConfigureAwait(false));
        }

        /// <inheritdoc/>
        public ValueTask<uint> CancelAllAsync(StopModeEnum stopMode, CancellationToken ct = default)
        {
            return m_proxy.CancelAllAsync(stopMode, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<IntentCommandOutcome> PauseAsync(CancellationToken ct = default)
        {
            return new IntentCommandOutcome(await m_proxy.PauseAsync(ct).ConfigureAwait(false));
        }

        /// <inheritdoc/>
        public async ValueTask<IntentCommandOutcome> ResumeAsync(CancellationToken ct = default)
        {
            return new IntentCommandOutcome(await m_proxy.ResumeAsync(ct).ConfigureAwait(false));
        }

        /// <inheritdoc/>
        public async ValueTask<IntentSubmissionResult> RetryAsync(string intentId, CancellationToken ct = default)
        {
            (bool accepted, NodeId operation, IntentFailureEnum failure, LocalizedText message) =
                await m_proxy.RetryAsync(intentId, ct).ConfigureAwait(false);
            return new IntentSubmissionResult
            {
                Accepted = accepted,
                IntentId = intentId,
                Operation = operation.IsNull ? NodeId.Null : operation,
                Failure = failure,
                Message = message.IsNull ? LocalizedText.Null : message
            };
        }

        /// <inheritdoc/>
        public async ValueTask<MissionSubmissionResult> SubmitMissionAsync(
            MissionDataType mission,
            CancellationToken ct = default)
        {
            (bool accepted, string missionId, NodeId operation, IntentFailureEnum failure, LocalizedText message) =
                await m_proxy.SubmitMissionAsync(mission, ct).ConfigureAwait(false);
            return new MissionSubmissionResult
            {
                Accepted = accepted,
                MissionId = missionId ?? string.Empty,
                Operation = operation.IsNull ? NodeId.Null : operation,
                Failure = failure,
                Message = message.IsNull ? LocalizedText.Null : message
            };
        }

        /// <inheritdoc/>
        public async ValueTask<MissionUpdateOutcome> UpdateMissionAsync(
            string missionId,
            uint missionUpdateId,
            ArrayOf<MissionStepDataType> steps,
            CancellationToken ct = default)
        {
            (MissionUpdateResultEnum result, LocalizedText message) = await m_proxy.UpdateMissionAsync(
                missionId,
                missionUpdateId,
                steps,
                ct).ConfigureAwait(false);
            if (result != MissionUpdateResultEnum.Accepted)
            {
                Logger.MissionUpdateRefused(missionId, missionUpdateId, result, message.Text ?? string.Empty);
            }
            return new MissionUpdateOutcome(result, message.IsNull ? LocalizedText.Null : message);
        }

        /// <inheritdoc/>
        public async ValueTask<IntentCommandOutcome> CancelMissionAsync(
            string missionId,
            StopModeEnum stopMode,
            CancellationToken ct = default)
        {
            return new IntentCommandOutcome(await m_proxy.CancelMissionAsync(missionId, stopMode, ct)
                .ConfigureAwait(false));
        }

        /// <inheritdoc/>
        public async ValueTask<CommandAuthorityOutcome> RequestControlAsync(CancellationToken ct = default)
        {
            (bool granted, NodeId currentOwner) = await m_proxy.RequestControlAsync(ct).ConfigureAwait(false);
            return new CommandAuthorityOutcome(granted, currentOwner.IsNull ? NodeId.Null : currentOwner);
        }

        /// <inheritdoc/>
        public ValueTask ReleaseControlAsync(CancellationToken ct = default)
        {
            return m_proxy.ReleaseControlAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask<RealTimeChannelOpenResult> OpenRealTimeChannelAsync(
            string channelId,
            double requestedLease,
            CancellationToken ct = default)
        {
            (bool granted, string endpointUrl, string payloadDescriptor, DateTimeUtc leaseExpiry,
                LocalizedText message) = await m_proxy.OpenRealTimeChannelAsync(channelId, requestedLease, ct)
                    .ConfigureAwait(false);
            return new RealTimeChannelOpenResult
            {
                Granted = granted,
                EndpointUrl = endpointUrl ?? string.Empty,
                PayloadDescriptor = payloadDescriptor ?? string.Empty,
                LeaseExpiry = leaseExpiry,
                Message = message.IsNull ? LocalizedText.Null : message
            };
        }

        /// <inheritdoc/>
        public ValueTask<bool> CloseRealTimeChannelAsync(string channelId, CancellationToken ct = default)
        {
            return m_proxy.CloseRealTimeChannelAsync(channelId, ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> ResolveChildAsync(NodeId root, string browseName, CancellationToken ct = default)
        {
            return TranslateAsync(root, [browseName], ct);
        }

        /// <inheritdoc/>
        public async ValueTask<IntentOperationSnapshot> ReadOperationSnapshotAsync(
            NodeId operation,
            CancellationToken ct = default)
        {
            NodeId stateNode = await TranslateAsync(operation, ["ExecutionState"], ct).ConfigureAwait(false);
            NodeId progressNode = await TranslateAsync(operation, ["Progress"], ct).ConfigureAwait(false);
            NodeId poseNode = await TranslateAsync(operation, ["CurrentPose"], ct).ConfigureAwait(false);
            NodeId resultNode = await TranslateAsync(operation, ["Result"], ct).ConfigureAwait(false);
            return new IntentOperationSnapshot
            {
                Operation = operation,
                IntentId = await ReadChildValueOrDefaultAsync(operation, ["IntentId"], string.Empty, ct)
                    .ConfigureAwait(false),
                ExecutionState = stateNode.IsNull
                    ? ExecutionStateEnum.Accepted
                    : await ReadEnumValueAsync<ExecutionStateEnum>(stateNode, ct).ConfigureAwait(false),
                Progress = progressNode.IsNull ? -1 : await m_session.ReadValueAsync<double>(progressNode, ct)
                    .ConfigureAwait(false),
                CurrentPose = poseNode.IsNull ? new Pose3DDataType() : await m_session
                    .ReadValueAsync<Pose3DDataType>(poseNode, ct).ConfigureAwait(false),
                Result = resultNode.IsNull ? new IntentResultDataType() : await m_session
                    .ReadValueAsync<IntentResultDataType>(resultNode, ct).ConfigureAwait(false),
                MissionId = await ReadChildValueOrDefaultAsync(operation, ["MissionId"], string.Empty, ct)
                    .ConfigureAwait(false),
                QueuePosition = await ReadChildValueOrDefaultAsync(operation, ["QueuePosition"], 0u, ct)
                    .ConfigureAwait(false)
            };
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> ReadControlOwnerAsync(CancellationToken ct = default)
        {
            NodeId ownerNode = await TranslateAsync(ControllerId, ["ControlOwner"], ct).ConfigureAwait(false);
            return ownerNode.IsNull ? NodeId.Null : await m_session.ReadValueAsync<NodeId>(ownerNode, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<RobotIntentDataChange> SubscribeDataChangesAsync(
            ArrayOf<NodeId> nodeIds,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var nodes = new List<NodeId>();
            foreach (NodeId nodeId in nodeIds)
            {
                nodes.Add(nodeId);
            }
            await foreach (DataValueChange change in m_streaming.SubscribeDataChangesAsync(nodes, ct: ct)
                .ConfigureAwait(false))
            {
                yield return new RobotIntentDataChange(
                    ResolveMonitoredNodeId(change.MonitoredItem, nodes),
                    change.Value.WrappedValue);
            }
        }

        private static NodeId ResolveMonitoredNodeId(IMonitoredItem? monitoredItem, List<NodeId> nodes)
        {
            if (monitoredItem == null)
            {
                return NodeId.Null;
            }
            string name = monitoredItem.Name;
            foreach (NodeId node in nodes)
            {
                if (name.EndsWith("_" + node, StringComparison.Ordinal))
                {
                    return node;
                }
            }
            return NodeId.Null;
        }

        private async ValueTask<TEnum> ReadEnumValueAsync<TEnum>(NodeId nodeId, CancellationToken ct)
            where TEnum : struct, Enum
        {
            // OPC UA encodes Enumeration values as Int32 in Variants; convert explicitly to the generated enum type.
            int value = await m_session.ReadValueAsync<int>(nodeId, ct).ConfigureAwait(false);
            return EnumHelper.Int32ToEnum<TEnum>(value);
        }

        private async ValueTask<RobotIntentSafetyStateSnapshot> ReadSafetyStateAsync(CancellationToken ct)
        {
            NodeId safetyState = await TranslateAsync(ControllerId, ["SafetyState"], ct).ConfigureAwait(false);
            if (safetyState.IsNull)
            {
                return new RobotIntentSafetyStateSnapshot();
            }
            return new RobotIntentSafetyStateSnapshot
            {
                Available = true,
                ActiveFunction = await ReadOptionalEnumChildAsync<SafeMotionFunctionEnum>(
                    safetyState,
                    ["ActiveFunction"],
                    ct).ConfigureAwait(false),
                EmergencyStopActive = await ReadOptionalChildAsync<bool>(
                    safetyState,
                    ["EmergencyStopActive"],
                    ct).ConfigureAwait(false),
                ProtectiveStopActive = await ReadOptionalChildAsync<bool>(
                    safetyState,
                    ["ProtectiveStopActive"],
                    ct).ConfigureAwait(false),
                SafeSpeedLimitActive = await ReadOptionalChildAsync<bool>(
                    safetyState,
                    ["SafeSpeedLimitActive"],
                    ct).ConfigureAwait(false),
                SafeSpeedLimit = await ReadOptionalChildAsync<double>(safetyState, ["SafeSpeedLimit"], ct)
                    .ConfigureAwait(false),
                SafetyControllerOk = await ReadOptionalChildAsync<bool>(
                    safetyState,
                    ["SafetyControllerOk"],
                    ct).ConfigureAwait(false),
                LastStopReason = await ReadOptionalChildAsync<LocalizedText>(
                    safetyState,
                    ["LastStopReason"],
                    ct).ConfigureAwait(false)
            };
        }

        private async ValueTask<MissionSnapshot> ReadMissionSnapshotAsync(NodeId mission, CancellationToken ct)
        {
            NodeId stateNode = await TranslateAsync(mission, ["ExecutionState"], ct).ConfigureAwait(false);
            return new MissionSnapshot
            {
                MissionNode = mission,
                MissionId = await ReadChildValueOrDefaultAsync(mission, ["MissionId"], string.Empty, ct)
                    .ConfigureAwait(false),
                MissionUpdateId = await ReadChildValueOrDefaultAsync(mission, ["MissionUpdateId"], 0u, ct)
                    .ConfigureAwait(false),
                Mission = await ReadChildValueOrDefaultAsync(mission, ["Mission"], new MissionDataType(), ct)
                    .ConfigureAwait(false),
                ExecutionState = stateNode.IsNull
                    ? ExecutionStateEnum.Accepted
                    : await ReadEnumValueAsync<ExecutionStateEnum>(stateNode, ct).ConfigureAwait(false),
                CurrentStepId = await ReadChildValueOrDefaultAsync(mission, ["CurrentStepId"], string.Empty, ct)
                    .ConfigureAwait(false),
                ReleasedStepCount = await ReadChildValueOrDefaultAsync(mission, ["ReleasedStepCount"], 0u, ct)
                    .ConfigureAwait(false)
            };
        }

        private async ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseOptionalFolderAsync(
            string browseName,
            CancellationToken ct)
        {
            NodeId folder = await TranslateAsync(ControllerId, [browseName], ct).ConfigureAwait(false);
            if (folder.IsNull)
            {
                return [];
            }
            return await BrowseFolderAsync(folder, ct).ConfigureAwait(false);
        }

        private async ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseFolderAsync(
            NodeId folder,
            CancellationToken ct)
        {
            ArrayOf<ReferenceDescription> references = await BrowseObjectReferencesAsync(folder, ct)
                .ConfigureAwait(false);
            var entries = new List<RobotIntentNodeLookupEntry>();
            foreach (ReferenceDescription reference in references)
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris);
                if (!nodeId.IsNull)
                {
                    entries.Add(new RobotIntentNodeLookupEntry(
                        nodeId,
                        reference.BrowseName,
                        reference.BrowseName.Name ?? string.Empty));
                }
            }
            return [.. entries];
        }

        private async ValueTask<ArrayOf<ReferenceDescription>> BrowseObjectReferencesAsync(
            NodeId folder,
            CancellationToken ct)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> results, _) = await m_session.ManagedBrowseAsync(
                null,
                null,
                [folder],
                0,
                BrowseDirection.Forward,
                global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                includeSubtypes: true,
                (uint)NodeClass.Object,
                ct).ConfigureAwait(false);
            if (results.Count == 0)
            {
                return [];
            }
            return results[0];
        }

        private async ValueTask<T> ReadChildValueOrDefaultAsync<T>(
            NodeId root,
            IReadOnlyList<string> path,
            T defaultValue,
            CancellationToken ct)
        {
            NodeId nodeId = await TranslateAsync(root, path, ct).ConfigureAwait(false);
            if (nodeId.IsNull)
            {
                return defaultValue;
            }
            return await m_session.ReadValueAsync<T>(nodeId, ct).ConfigureAwait(false);
        }

        private async ValueTask<RobotIntentOptionalValue<T>> ReadOptionalChildAsync<T>(
            NodeId root,
            IReadOnlyList<string> path,
            CancellationToken ct)
        {
            NodeId nodeId = await TranslateAsync(root, path, ct).ConfigureAwait(false);
            if (nodeId.IsNull)
            {
                return RobotIntentOptionalValue<T>.Unavailable;
            }
            return RobotIntentOptionalValue<T>.FromValue(await m_session.ReadValueAsync<T>(nodeId, ct)
                .ConfigureAwait(false));
        }

        private async ValueTask<RobotIntentOptionalValue<NodeId>> ReadOptionalNodeIdChildAsync(
            NodeId root,
            IReadOnlyList<string> path,
            CancellationToken ct)
        {
            NodeId nodeId = await TranslateAsync(root, path, ct).ConfigureAwait(false);
            if (nodeId.IsNull)
            {
                return RobotIntentOptionalValue<NodeId>.Unavailable;
            }
            NodeId value = await m_session.ReadValueAsync<NodeId>(nodeId, ct).ConfigureAwait(false);
            return RobotIntentOptionalValue<NodeId>.FromValue(value.IsNull ? NodeId.Null : value);
        }

        private async ValueTask<RobotIntentOptionalValue<TEnum>> ReadOptionalEnumChildAsync<TEnum>(
            NodeId root,
            IReadOnlyList<string> path,
            CancellationToken ct)
            where TEnum : struct, Enum
        {
            NodeId nodeId = await TranslateAsync(root, path, ct).ConfigureAwait(false);
            if (nodeId.IsNull)
            {
                return RobotIntentOptionalValue<TEnum>.Unavailable;
            }
            return RobotIntentOptionalValue<TEnum>.FromValue(await ReadEnumValueAsync<TEnum>(nodeId, ct)
                .ConfigureAwait(false));
        }

        private async ValueTask<ArrayOf<IntentCapabilityDataType>> ReadSupportedIntentsAsync(
            NodeId root,
            IReadOnlyList<string> path,
            CancellationToken ct)
        {
            NodeId nodeId = await TranslateAsync(root, path, ct).ConfigureAwait(false);
            if (nodeId.IsNull)
            {
                return [];
            }

            DataValue value = await m_session.ReadValueAsync(nodeId, ct).ConfigureAwait(false);
            if (value.WrappedValue.TryGetValue(
                out ArrayOf<IntentCapabilityDataType> supported,
                m_session.MessageContext))
            {
                return supported;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadTypeMismatch,
                "SupportedIntents did not decode as IntentCapabilityDataType[].");
        }

        private ValueTask<NodeId> TranslateAsync(NodeId root, IReadOnlyList<string> path, CancellationToken ct)
        {
            var names = new List<QualifiedName>(path.Count);
            foreach (string element in path)
            {
                names.Add(new QualifiedName(element, NamespaceIndex));
            }
            return TranslateAsync(root, names, ct);
        }

        private async ValueTask<NodeId> TranslateAsync(NodeId root, List<QualifiedName> path, CancellationToken ct)
        {
            if (root.IsNull || path.Count == 0)
            {
                return NodeId.Null;
            }
            BrowsePath browsePath = new()
            {
                StartingNode = root,
                RelativePath = new RelativePath()
            };
            foreach (QualifiedName element in path)
            {
                browsePath.RelativePath.Elements = [.. browsePath.RelativePath.Elements, new RelativePathElement
                {
                    ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = element
                }];
            }
            ArrayOf<BrowsePath> paths = [browsePath];
            TranslateBrowsePathsToNodeIdsResponse response = await m_session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                paths,
                ct).ConfigureAwait(false);
            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0)
            {
                return NodeId.Null;
            }
            return ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, m_session.NamespaceUris);
        }

        private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            if (e.NewState == ConnectionState.Connected &&
                e.PreviousState is ConnectionState.Reconnecting or ConnectionState.Failover)
            {
                Reconnected?.Invoke();
            }
        }

        private const string RobotIntentNamespace = "http://opcfoundation.org/UA/RobotIntent/";

        private readonly ISession m_session;
        private readonly IStreamingSubscription m_streaming;
        private readonly IntentControllerTypeClient m_proxy;
    }

    internal static partial class UaRobotIntentTransportLog
    {
        [LoggerMessage(
            EventId = RobotIntentClientEventIds.IntentSubmitted,
            Level = LogLevel.Information,
            Message = "Robot Intent submitted. IntentId={IntentId}, Operation={Operation}.")]
        public static partial void IntentSubmitted(this ILogger logger, string intentId, NodeId operation);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.IntentRefused,
            Level = LogLevel.Warning,
            Message = "Robot Intent refused. Failure={Failure}, Message={Message}.")]
        public static partial void IntentRefused(
            this ILogger logger,
            IntentFailureEnum failure,
            string message);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.MissionUpdateRefused,
            Level = LogLevel.Warning,
            Message = "Robot Intent mission update refused. MissionId={MissionId}, UpdateId={UpdateId}, " +
                "Result={Result}, Message={Message}.")]
        public static partial void MissionUpdateRefused(
            this ILogger logger,
            string missionId,
            uint updateId,
            MissionUpdateResultEnum result,
            string message);
    }
}
