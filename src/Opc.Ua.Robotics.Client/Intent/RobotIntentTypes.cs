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
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Client.Intent
{
    /// <summary>
    /// Describes which optional Robot Intent facets a controller satisfies.
    /// </summary>
    public sealed record RobotIntentFacets
    {
        /// <summary>
        /// Gets a value indicating whether the RI-Base facet is satisfied.
        /// </summary>
        public bool Base { get; init; }

        /// <summary>
        /// Gets a value indicating whether queued intent submission is supported.
        /// </summary>
        public bool QueuedIntents { get; init; }

        /// <summary>
        /// Gets a value indicating whether trajectory intents are supported.
        /// </summary>
        public bool Trajectories { get; init; }

        /// <summary>
        /// Gets a value indicating whether force-control intents are supported.
        /// </summary>
        public bool ForceControl { get; init; }

        /// <summary>
        /// Gets a value indicating whether missions are supported.
        /// </summary>
        public bool Missions { get; init; }

        /// <summary>
        /// Gets a value indicating whether mission horizon updates are supported.
        /// </summary>
        public bool MissionHorizon { get; init; }

        /// <summary>
        /// Gets a value indicating whether branching mission graphs are supported.
        /// </summary>
        public bool MissionBranching { get; init; }

        /// <summary>
        /// Gets a value indicating whether path blending is supported.
        /// </summary>
        public bool Blending { get; init; }

        /// <summary>
        /// Gets a value indicating whether brokered real-time channels are supported.
        /// </summary>
        public bool RealTimeChannels { get; init; }

        /// <summary>
        /// Gets a value indicating whether every intent capability includes Aborting.
        /// </summary>
        public bool EveryCapabilitySupportsAborting { get; init; }
    }

    /// <summary>
    /// Cached NodeId and BrowseName for a controller child instance.
    /// </summary>
    public sealed record RobotIntentNodeLookupEntry(NodeId NodeId, QualifiedName BrowseName, string Name);

    /// <summary>
    /// Cached lookup tables for address-space objects named by intent structures.
    /// </summary>
    public sealed record RobotIntentLookups
    {
        /// <summary>
        /// Gets an empty lookup snapshot.
        /// </summary>
        public static RobotIntentLookups Empty { get; } = new();

        /// <summary>
        /// Gets coordinate frame entries.
        /// </summary>
        public ArrayOf<RobotIntentNodeLookupEntry> Frames { get; init; } = [];

        /// <summary>
        /// Gets frame entries keyed by Pose3DDataType.FrameId.
        /// </summary>
        public ArrayOf<RobotIntentNodeLookupEntry> FramesByFrameId { get; init; } = [];

        /// <summary>
        /// Gets tool entries.
        /// </summary>
        public ArrayOf<RobotIntentNodeLookupEntry> Tools { get; init; } = [];

        /// <summary>
        /// Gets location entries.
        /// </summary>
        public ArrayOf<RobotIntentNodeLookupEntry> Locations { get; init; } = [];

        /// <summary>
        /// Gets axis entries.
        /// </summary>
        public ArrayOf<RobotIntentNodeLookupEntry> Axes { get; init; } = [];

        /// <summary>
        /// Gets output entries.
        /// </summary>
        public ArrayOf<RobotIntentNodeLookupEntry> Outputs { get; init; } = [];

        /// <summary>
        /// Gets program entries.
        /// </summary>
        public ArrayOf<RobotIntentNodeLookupEntry> Programs { get; init; } = [];
    }

    /// <summary>
    /// A discovered intent controller and its cached capabilities.
    /// </summary>
    public sealed record RobotIntentControllerInfo
    {
        /// <summary>
        /// Gets the controller node.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// Gets the controller browse name.
        /// </summary>
        public QualifiedName BrowseName { get; init; } = QualifiedName.Null;

        /// <summary>
        /// Gets the declared supported intents.
        /// </summary>
        public ArrayOf<IntentCapabilityDataType> SupportedIntents { get; init; } = [];

        /// <summary>
        /// Gets the axis count.
        /// </summary>
        public uint AxisCount { get; init; }

        /// <summary>
        /// Gets the maximum queue depth.
        /// </summary>
        public uint MaxQueueDepth { get; init; }

        /// <summary>
        /// Gets a value indicating whether missions are supported.
        /// </summary>
        public bool MissionsSupported { get; init; }

        /// <summary>
        /// Gets a value indicating whether mission horizon updates are supported.
        /// </summary>
        public bool MissionHorizonSupported { get; init; }

        /// <summary>
        /// Gets a value indicating whether mission branching is supported.
        /// </summary>
        public bool MissionBranchingSupported { get; init; }

        /// <summary>
        /// Gets a value indicating whether blending is supported.
        /// </summary>
        public bool BlendingSupported { get; init; }

        /// <summary>
        /// Gets a value indicating whether trajectory intents are supported.
        /// </summary>
        public bool TrajectorySupported { get; init; }

        /// <summary>
        /// Gets a value indicating whether force control is supported.
        /// </summary>
        public bool ForceControlSupported { get; init; }

        /// <summary>
        /// Gets a value indicating whether real-time channels are supported.
        /// </summary>
        public bool RealTimeChannelsSupported { get; init; }

        /// <summary>
        /// Gets the maximum number of trajectory points.
        /// </summary>
        public uint MaxTrajectoryPoints { get; init; }

        /// <summary>
        /// Gets the cached lookup tables.
        /// </summary>
        public RobotIntentLookups Lookups { get; init; } = RobotIntentLookups.Empty;

        /// <summary>
        /// Gets the facets the controller claims, as published in
        /// <c>Capabilities.SupportedFacets</c>.
        /// </summary>
        /// <remarks>
        /// This is the controller's own conformance claim and is what a client should consult. It is
        /// empty against a server that predates the member, in which case <see cref="Facets"/> is the
        /// only information available.
        /// </remarks>
        public ArrayOf<string> SupportedFacets { get; init; }

        /// <summary>
        /// Gets a facet snapshot projected from the individual capability variables.
        /// </summary>
        /// <remarks>
        /// This is a convenience projection, not a conformance model. It can only see the capability
        /// flags, so for any facet whose requirements go beyond a single flag it will disagree with
        /// <see cref="SupportedFacets"/> — and the requirements it cannot see are the ones that matter
        /// most, because several of them are behavioural and settle nothing by being read. Prefer
        /// <see cref="SupportedFacets"/> wherever the server publishes it.
        /// </remarks>
        public RobotIntentFacets Facets { get; init; } = new();
    }

    /// <summary>
    /// Outcome returned when a submission may be refused without a Bad StatusCode.
    /// </summary>
    public sealed record IntentSubmissionResult
    {
        /// <summary>
        /// Gets a value indicating whether the request was accepted.
        /// </summary>
        public bool Accepted { get; init; }

        /// <summary>
        /// Gets the admitted intent id.
        /// </summary>
        public string IntentId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the operation node.
        /// </summary>
        public NodeId Operation { get; init; } = NodeId.Null;

        /// <summary>
        /// Gets the refusal reason.
        /// </summary>
        public IntentFailureEnum Failure { get; init; } = IntentFailureEnum.None;

        /// <summary>
        /// Gets the human-readable refusal message.
        /// </summary>
        public LocalizedText Message { get; init; } = LocalizedText.Null;
    }

    /// <summary>
    /// Outcome returned when a mission submission may be refused without a Bad StatusCode.
    /// </summary>
    public sealed record MissionSubmissionResult
    {
        /// <summary>
        /// Gets a value indicating whether the request was accepted.
        /// </summary>
        public bool Accepted { get; init; }

        /// <summary>
        /// Gets the admitted mission id.
        /// </summary>
        public string MissionId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the mission operation node.
        /// </summary>
        public NodeId Operation { get; init; } = NodeId.Null;

        /// <summary>
        /// Gets the refusal reason.
        /// </summary>
        public IntentFailureEnum Failure { get; init; } = IntentFailureEnum.None;

        /// <summary>
        /// Gets the human-readable refusal message.
        /// </summary>
        public LocalizedText Message { get; init; } = LocalizedText.Null;
    }

    /// <summary>
    /// Outcome returned when a mission horizon update completes.
    /// </summary>
    public sealed record MissionUpdateOutcome(
        MissionUpdateResultEnum Result,
        LocalizedText Message);

    /// <summary>
    /// Outcome returned when a cancel request may be refused.
    /// </summary>
    public sealed record IntentCommandOutcome(bool Accepted);

    /// <summary>
    /// Outcome returned for command authority requests.
    /// </summary>
    public sealed record CommandAuthorityOutcome(bool Granted, NodeId CurrentOwner);

    /// <summary>
    /// Outcome returned when a real-time channel lease may be refused.
    /// </summary>
    public sealed record RealTimeChannelOpenResult
    {
        /// <summary>
        /// Gets a value indicating whether the lease was granted.
        /// </summary>
        public bool Granted { get; init; }

        /// <summary>
        /// Gets the endpoint URL to connect to.
        /// </summary>
        public string EndpointUrl { get; init; } = string.Empty;

        /// <summary>
        /// Gets the transport payload descriptor.
        /// </summary>
        public string PayloadDescriptor { get; init; } = string.Empty;

        /// <summary>
        /// Gets the lease expiry time.
        /// </summary>
        public DateTimeUtc LeaseExpiry { get; init; }

        /// <summary>
        /// Gets the refusal message.
        /// </summary>
        public LocalizedText Message { get; init; } = LocalizedText.Null;
    }

    /// <summary>
    /// Data-change notification supplied by the transport abstraction.
    /// </summary>
    public readonly record struct RobotIntentDataChange(NodeId NodeId, Variant Value);

    /// <summary>
    /// Current observable state of an intent operation.
    /// </summary>
    public sealed record IntentOperationSnapshot
    {
        /// <summary>
        /// Gets the operation node.
        /// </summary>
        public NodeId Operation { get; init; } = NodeId.Null;

        /// <summary>
        /// Gets the execution state.
        /// </summary>
        public ExecutionStateEnum ExecutionState { get; init; } = ExecutionStateEnum.Accepted;

        /// <summary>
        /// Gets the progress value, or a negative value when unknown.
        /// </summary>
        public double Progress { get; init; } = -1;

        /// <summary>
        /// Gets the current pose.
        /// </summary>
        public Pose3DDataType CurrentPose { get; init; } = new();

        /// <summary>
        /// Gets the operation result.
        /// </summary>
        public IntentResultDataType Result { get; init; } = new();
    }

    /// <summary>
    /// Event surface for operation state changes.
    /// </summary>
    public delegate void IntentOperationChangedHandler(IntentOperationSnapshot snapshot);

    /// <summary>
    /// Event surface for command owner changes.
    /// </summary>
    public delegate void CommandAuthorityChangedHandler(NodeId currentOwner);

    /// <summary>
    /// Event surface for managed-session reconnect notifications.
    /// </summary>
    public delegate void RobotIntentReconnectHandler();

    /// <summary>
    /// Helper methods for Robot Intent facets and terminal states.
    /// </summary>
    public static class RobotIntentRules
    {
        /// <summary>
        /// Returns true for terminal ExecutionStateEnum values.
        /// </summary>
        public static bool IsTerminal(ExecutionStateEnum state)
        {
            return state is ExecutionStateEnum.Succeeded
                or ExecutionStateEnum.Failed
                or ExecutionStateEnum.Cancelled
                or ExecutionStateEnum.Retriable;
        }

        /// <summary>
        /// Derives the client-facing facet snapshot from a capability declaration.
        /// </summary>
        public static RobotIntentFacets DeriveFacets(RobotIntentControllerInfo controller)
        {
            if (controller is null)
            {
                throw new ArgumentNullException(nameof(controller));
            }

            bool hasAborting = true;
            for (int i = 0; i < controller.SupportedIntents.Count; i++)
            {
                ArrayOf<BufferModeEnum> modes = controller.SupportedIntents[i].SupportedBufferModes;
                if (!modes.Contains(BufferModeEnum.Aborting))
                {
                    hasAborting = false;
                    break;
                }
            }

            return new RobotIntentFacets
            {
                Base = controller.SupportedIntents.Count > 0 && controller.AxisCount > 0,
                QueuedIntents = controller.MaxQueueDepth > 0,
                Trajectories = controller.TrajectorySupported,
                ForceControl = controller.ForceControlSupported,
                Missions = controller.MissionsSupported,
                MissionHorizon = controller.MissionsSupported && controller.MissionHorizonSupported,
                MissionBranching = controller.MissionsSupported && controller.MissionBranchingSupported,
                Blending = controller.BlendingSupported,
                RealTimeChannels = controller.RealTimeChannelsSupported,
                EveryCapabilitySupportsAborting = hasAborting
            };
        }
    }
}
