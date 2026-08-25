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

namespace Opc.Ua.RobotIntent.Server
{
    /// <summary>
    /// What a Server will accept, and under what constraints.
    /// </summary>
    /// <remarks>
    /// The capability list is a CONTRACT, not documentation: the host refuses anything
    /// not declared here, so a client that reads it once knows what it may submit
    /// instead of submitting to find out.
    /// </remarks>
    public sealed class IntentControllerHostOptions
    {
        /// <summary>
        /// The operational mode the robot reports. Submission is permitted only in
        /// Automatic and AutomaticExternal, and this specification defines no way to
        /// command a change: mode selection is a safety function performed by
        /// safety-rated means.
        /// </summary>
        public OperationalModeEnum OperationalMode { get; set; } = OperationalModeEnum.AutomaticExternal;

        /// <summary>
        /// How many intents may queue behind the executing one. Zero accepts only
        /// Aborting submissions.
        /// </summary>
        public uint MaxQueueDepth { get; set; } = 8;

        /// <summary>
        /// Maximum accepted SpeedFraction; values above are clamped. Zero means no host ceiling.
        /// </summary>
        public double MaxSpeedFraction { get; set; } = 1.0;

        /// <summary>
        /// Maximum accepted Cartesian speed in metres per second; values above are clamped.
        /// </summary>
        public double MaxCartesianSpeed { get; set; }

        /// <summary>
        /// Maximum accepted Cartesian acceleration in metres per second squared; values above are clamped.
        /// </summary>
        public double MaxCartesianAcceleration { get; set; }

        /// <summary>
        /// Maximum accepted jerk; values above are clamped.
        /// </summary>
        public double MaxJerk { get; set; }

        /// <summary>
        /// Whether a caller must hold command authority to submit. Defaults to true;
        /// turning it off is for single-client test hosts only.
        /// </summary>
        public bool RequireControlAuthority { get; set; } = true;

        /// <summary>
        /// Reads the current safety status before admission decisions and live readiness reads.
        /// </summary>
        public Func<CancellationToken, ValueTask<SafetyStatus>>? SafetyStatusReader { get; set; }

        /// <summary>
        /// Whether SubmitMission is implemented.
        /// </summary>
        public bool MissionsSupported { get; set; } = true;

        /// <summary>
        /// Whether UpdateMission can revise the horizon of a running mission.
        /// </summary>
        public bool MissionHorizonSupported { get; set; } = true;

        /// <summary>
        /// Whether the blending buffer modes actually blend. A host that treats them
        /// as Buffered reports false, so a client is not misled about the path.
        /// </summary>
        public bool BlendingSupported { get; set; }

        /// <summary>
        /// How many axes a JointMoveIntentDataType must carry.
        /// </summary>
        public uint AxisCount { get; set; } = 6;

        /// <summary>
        /// Whether trajectory and Cartesian path intents are accepted.
        /// </summary>
        public bool TrajectorySupported { get; set; } = true;

        /// <summary>
        /// Whether force intents are accepted AND the robot genuinely regulates force.
        /// A host that would ignore the force reports false rather than accepting an
        /// intent it cannot honour.
        /// </summary>
        public bool ForceControlSupported { get; set; }

        /// <summary>
        /// Whether the host brokers real-time channels.
        /// </summary>
        public bool RealTimeChannelsSupported { get; set; }

        /// <summary>
        /// Whether mission transitions are evaluated. A host that reports false runs
        /// the steps in order and ignores any transitions supplied.
        /// </summary>
        public bool MissionBranchingSupported { get; set; } = true;

        /// <summary>
        /// Largest trajectory accepted, in points. Zero states no limit.
        /// </summary>
        public uint MaxTrajectoryPoints { get; set; }

        /// <summary>
        /// How many times a step whose ErrorPolicy is Retry may be re-attempted.
        /// </summary>
        public uint MaxStepRetries { get; set; } = 2;

        /// <summary>
        /// Longest lease a real-time channel is granted, in milliseconds.
        /// </summary>
        public double MaxChannelLeaseMs { get; set; } = 30000;

        /// <summary>
        /// Path tolerance used when a trajectory asks the Server to choose one, in metres.
        /// </summary>
        public double DefaultPathTolerance { get; set; } = 0.001;

        /// <summary>
        /// Goal tolerance used when a trajectory asks the Server to choose one, in metres.
        /// </summary>
        public double DefaultGoalTolerance { get; set; } = 0.001;

        /// <summary>
        /// Goal-time tolerance used when a trajectory asks the Server to choose one, in milliseconds.
        /// </summary>
        public double DefaultGoalTimeTolerance { get; set; } = 100;

        /// <summary>
        /// How many terminal operations to keep browsable per controller, or zero to
        /// keep every one of them.
        /// <para>
        /// An operation instance survives the work it describes, because a client has
        /// to be able to read the result after the fact. Nothing then removes it, so a
        /// controller that runs continuously accumulates operation nodes for as long as
        /// it is up. Setting a bound lets the host drop the oldest terminal operations
        /// once that many have accrued. When no <c>removeNode</c> callback was supplied,
        /// the host still drops its retained bookkeeping and cancellation resources.
        /// </para>
        /// <para>
        /// The default keeps the latest 128 terminal operations, which gives reconnecting
        /// clients a result window without letting a continuously running controller retain
        /// every operation forever. Set zero only when the embedding server supplies a
        /// separate retention policy.
        /// </para>
        /// </summary>
        public uint RetainedTerminalOperations { get; set; } = 128;

        /// <summary>
        /// How many terminal missions to keep browsable per controller, or zero to keep
        /// every one of them. Mirrors <see cref="RetainedTerminalOperations"/> for
        /// mission nodes. The default keeps the latest 32 terminal missions.
        /// </summary>
        public uint RetainedTerminalMissions { get; set; } = 32;

        /// <summary>
        /// How long asynchronous disposal waits for an executing intent to observe
        /// shutdown cancellation and let the pump drain, in milliseconds.
        /// </summary>
        /// <remarks>
        /// A well-behaved executor should return promptly when its cancellation token
        /// is signalled. This bound prevents server shutdown from hanging forever if
        /// application executor code fails to do so. Zero or less skips the wait.
        /// </remarks>
        public double ExecutorShutdownTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Whether this host can arbitrate between an intent and a held real-time
        /// channel. Defaults to false, which is the safe answer: clause 6.9 then
        /// requires motion intents to be refused while a lease is held.
        /// </summary>
        public bool ArbitratesWithRealTimeChannel { get; set; }

        /// <summary>
        /// The real-time channels this host offers.
        /// </summary>
        public IList<DeclaredChannel> Channels { get; } = [];

        /// <summary>
        /// Evaluates a transition condition.
        /// </summary>
        /// <remarks>
        /// When none is supplied, an EMPTY ContentFilter is true and a non-empty one is
        /// false. That is deterministic and states its own limitation: an
        /// unconditional edge works, and an edge nobody can evaluate is simply not
        /// taken rather than being taken by accident.
        /// </remarks>
        public Func<ContentFilter, bool>? ConditionEvaluator { get; set; }

        /// <summary>
        /// One entry per intent type this host accepts.
        /// </summary>
        /// <remarks>
        /// The intent type is held as an <see cref="ExpandedNodeId"/> and resolved
        /// against the Server's namespace table when the host starts. Resolving it at
        /// declaration time would bind it to whatever table happened to exist then,
        /// which is how a capability list silently stops matching anything.
        /// </remarks>
        public IList<DeclaredCapability> Capabilities { get; } = [];

        /// <summary>
        /// The evaluator actually used, defaulted as described above.
        /// </summary>
        public bool EvaluateCondition(ContentFilter? condition)
        {
            // A filter with no elements is unconditional. Testing only for a NULL
            // filter is not enough: an encoded ContentFilter arrives with an empty
            // element array, and treating that as unevaluatable makes every
            // unconditional transition silently untaken.
            if (condition == null || condition.Elements.IsNull || condition.Elements.IsEmpty)
            {
                return true;
            }
            return ConditionEvaluator?.Invoke(condition) ?? false;
        }

        /// <summary>
        /// Declares support for an intent type.
        /// </summary>
        public IntentControllerHostOptions Accept(
            ExpandedNodeId intentType,
            bool cancelSupported = true,
            bool pauseSupported = false,
            bool retrySupported = false,
            string? description = null)
        {
            Capabilities.Add(new DeclaredCapability
            {
                IntentType = intentType,
                Description = description,
                CancelSupported = cancelSupported,
                PauseSupported = pauseSupported,
                RetrySupported = retrySupported
            });
            return this;
        }
    }

    /// <summary>
    /// One declared intent type, before it is resolved against a namespace table.
    /// </summary>
    public sealed record DeclaredCapability
    {
        /// <summary>
        /// The intent DataType this host accepts.
        /// </summary>
        public ExpandedNodeId IntentType { get; init; } = ExpandedNodeId.Null;

        /// <summary>
        /// What this host does with it.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Whether Cancel is honoured for it.
        /// </summary>
        public bool CancelSupported { get; init; } = true;

        /// <summary>
        /// Whether Pause and Resume are honoured for it.
        /// </summary>
        public bool PauseSupported { get; init; }

        /// <summary>
        /// Whether it can terminate Retriable.
        /// </summary>
        public bool RetrySupported { get; init; }

        /// <summary>
        /// Buffer modes accepted for it. Aborting is always accepted.
        /// </summary>
        public ArrayOf<BufferModeEnum> SupportedBufferModes { get; init; } = new[]
        {
            BufferModeEnum.Aborting,
            BufferModeEnum.Buffered
        };

        /// <summary>
        /// Blocking modes accepted for it.
        /// </summary>
        public ArrayOf<BlockingModeEnum> SupportedBlockingModes { get; init; } = new[]
        {
            BlockingModeEnum.None,
            BlockingModeEnum.Soft,
            BlockingModeEnum.Single,
            BlockingModeEnum.Hard
        };

        /// <summary>
        /// Resolves this declaration into the value published in the address space.
        /// </summary>
        public IntentCapabilityDataType Resolve(NamespaceTable namespaceUris)
        {
            ArrayOf<BufferModeEnum> buffers = SupportedBufferModes.IsNull || SupportedBufferModes.IsEmpty
                ? new[] { BufferModeEnum.Aborting, BufferModeEnum.Buffered }.ToArrayOf()
                : SupportedBufferModes;
            if (!buffers.Contains(BufferModeEnum.Aborting))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Every Robot Intent capability must include BufferMode Aborting.");
            }
            ArrayOf<BlockingModeEnum> blocking =
                SupportedBlockingModes.IsNull || SupportedBlockingModes.IsEmpty
                    ? new[]
                    {
                        BlockingModeEnum.None,
                        BlockingModeEnum.Soft,
                        BlockingModeEnum.Single,
                        BlockingModeEnum.Hard
                    }.ToArrayOf()
                    : SupportedBlockingModes;
            return new IntentCapabilityDataType
            {
                IntentType = ExpandedNodeId.ToNodeId(IntentType, namespaceUris),
                Description = new LocalizedText(Description ?? string.Empty),
                CancelSupported = CancelSupported,
                PauseSupported = PauseSupported,
                RetrySupported = RetrySupported,
                SupportedBufferModes = buffers,
                SupportedBlockingModes = blocking
            };
        }
    }

    /// <summary>
    /// One real-time channel this host can broker.
    /// </summary>
    /// <remarks>
    /// The host describes and leases it. It defines no transport, opens no socket and
    /// inspects no payload: the descriptor is passed through to the client in whatever
    /// form the transport itself uses.
    /// </remarks>
    public sealed record DeclaredChannel
    {
        /// <summary>
        /// Identifier unique within the controller.
        /// </summary>
        public string ChannelId { get; init; } = string.Empty;

        /// <summary>
        /// The transport this channel speaks.
        /// </summary>
        public RealTimeTransportEnum Transport { get; init; }

        /// <summary>
        /// Where the channel is reached.
        /// </summary>
        public string EndpointUrl { get; init; } = string.Empty;

        /// <summary>
        /// Which end opens the connection.
        /// </summary>
        public ChannelInitiatorEnum Initiator { get; init; }

        /// <summary>
        /// The rate the channel runs at, in hertz.
        /// </summary>
        public double NominalRate { get; init; }

        /// <summary>
        /// The transport's own recipe or signal list.
        /// </summary>
        public string PayloadDescriptor { get; init; } = string.Empty;

        /// <summary>
        /// The operational mode required before it will carry motion.
        /// </summary>
        public OperationalModeEnum RequiredMode { get; init; } = OperationalModeEnum.AutomaticExternal;
    }

    /// <summary>
    /// The outcome of asking for a channel lease.
    /// </summary>
    public sealed record RealTimeLease
    {
        /// <summary>
        /// Whether the lease was taken.
        /// </summary>
        public bool Granted { get; init; }

        /// <summary>
        /// Where to connect.
        /// </summary>
        public string EndpointUrl { get; init; } = string.Empty;

        /// <summary>
        /// The transport's own configuration.
        /// </summary>
        public string PayloadDescriptor { get; init; } = string.Empty;

        /// <summary>
        /// When the lease lapses.
        /// </summary>
        public DateTime Expiry { get; init; }

        /// <summary>
        /// Why it was refused.
        /// </summary>
        public string? Message { get; init; }

        /// <summary>
        /// Creates a refusal.
        /// </summary>
        public static RealTimeLease Refused(string message)
        {
            return new RealTimeLease { Message = message };
        }
    }

    /// <summary>
    /// What the safety system is enforcing, as the application reports it.
    /// </summary>
    /// <remarks>
    /// Every field is a REPORT. The safety system enforces these independently and
    /// remains effective when this Server is unreachable; the host reads them only so
    /// that it can refuse work the safety system would then have to reject, which is a
    /// courtesy and not a protective measure. See OPC UA - Robot Intent clause 10.4.
    /// </remarks>
    public sealed record SafetyStatus
    {
        /// <summary>
        /// The safe motion function currently enforced.
        /// </summary>
        public SafeMotionFunctionEnum ActiveFunction { get; init; } = SafeMotionFunctionEnum.None;

        /// <summary>
        /// True while an emergency stop is asserted.
        /// </summary>
        public bool EmergencyStopActive { get; init; }

        /// <summary>
        /// True while a protective stop is asserted.
        /// </summary>
        public bool ProtectiveStopActive { get; init; }

        /// <summary>
        /// True while a safely limited speed is being enforced.
        /// </summary>
        public bool SafeSpeedLimitActive { get; init; }

        /// <summary>
        /// The enforced tool centre point speed limit, in metres per second.
        /// </summary>
        public double SafeSpeedLimit { get; init; }

        /// <summary>
        /// False when the safety system reports its own fault.
        /// </summary>
        public bool SafetyControllerOk { get; init; } = true;

        /// <summary>
        /// Why the last stop occurred, for a human.
        /// </summary>
        public string? LastStopReason { get; init; }

        /// <summary>
        /// Nothing asserted and the safety controller healthy.
        /// </summary>
        public static SafetyStatus Nominal { get; } = new();

        /// <summary>
        /// Whether this state permits an intent to be admitted at all.
        /// </summary>
        public bool PermitsSubmission =>
            SafetyControllerOk && !EmergencyStopActive && !ProtectiveStopActive;
    }

    /// <summary>
    /// The outcome of admitting one intent.
    /// </summary>
    public sealed record IntentAdmission
    {
        /// <summary>
        /// Whether the intent was admitted.
        /// </summary>
        public bool Accepted { get; init; }

        /// <summary>
        /// The identifier it was admitted under.
        /// </summary>
        public string IntentId { get; init; } = string.Empty;

        /// <summary>
        /// The IntentOperation that tracks it.
        /// </summary>
        public NodeId Operation { get; init; } = NodeId.Null;

        /// <summary>
        /// Why it was refused.
        /// </summary>
        public IntentFailureEnum Failure { get; init; } = IntentFailureEnum.None;

        /// <summary>
        /// Human-readable detail on a refusal.
        /// </summary>
        public string? Message { get; init; }

        /// <summary>
        /// Creates an accepted admission.
        /// </summary>
        public static IntentAdmission Admitted(string intentId, NodeId operation)
        {
            return new IntentAdmission
            {
                Accepted = true,
                IntentId = intentId,
                Operation = operation
            };
        }

        /// <summary>
        /// Creates a refusal.
        /// </summary>
        public static IntentAdmission Refused(IntentFailureEnum failure, string message)
        {
            return new IntentAdmission { Failure = failure, Message = message };
        }
    }

    /// <summary>
    /// The outcome of admitting one mission.
    /// </summary>
    public sealed record MissionAdmission
    {
        /// <summary>
        /// Whether the mission was admitted.
        /// </summary>
        public bool Accepted { get; init; }

        /// <summary>
        /// The identifier it was admitted under.
        /// </summary>
        public string MissionId { get; init; } = string.Empty;

        /// <summary>
        /// The Mission that tracks it.
        /// </summary>
        public NodeId Operation { get; init; } = NodeId.Null;

        /// <summary>
        /// Why it was refused.
        /// </summary>
        public MissionUpdateResultEnum Result { get; init; } = MissionUpdateResultEnum.Accepted;

        /// <summary>
        /// Why the mission was refused.
        /// </summary>
        public IntentFailureEnum Failure { get; init; } = IntentFailureEnum.None;

        /// <summary>
        /// Human-readable detail on a refusal.
        /// </summary>
        public string? Message { get; init; }

        /// <summary>
        /// Creates an accepted admission.
        /// </summary>
        public static MissionAdmission Admitted(string missionId, NodeId operation)
        {
            return new MissionAdmission
            {
                Accepted = true,
                MissionId = missionId,
                Operation = operation
            };
        }

        /// <summary>
        /// Creates a refusal.
        /// </summary>
        public static MissionAdmission Refused(MissionUpdateResultEnum result, string message)
        {
            return new MissionAdmission
            {
                Result = result,
                Failure = result == MissionUpdateResultEnum.Accepted
                    ? IntentFailureEnum.None
                    : IntentFailureEnum.ParameterInvalid,
                Message = message
            };
        }

        /// <summary>
        /// Creates a refusal.
        /// </summary>
        public static MissionAdmission Refused(IntentFailureEnum failure, string message)
        {
            return new MissionAdmission
            {
                Result = MissionUpdateResultEnum.Rejected,
                Failure = failure,
                Message = message
            };
        }
    }

    /// <summary>
    /// The outcome of a mission update.
    /// </summary>
    /// <param name="Result">What happened.</param>
    /// <param name="Message">Human-readable detail on a refusal.</param>
    public sealed record MissionUpdateOutcome(MissionUpdateResultEnum Result, string? Message);

    /// <summary>
    /// The result of one admission rule: whether it passed and, when it did not, the
    /// text a client is told. A StatusCode alone cannot say WHICH parameter was wrong.
    /// </summary>
    /// <param name="Ok">Whether the rule passed.</param>
    /// <param name="Message">Why it did not.</param>
    internal readonly record struct Check(bool Ok, string? Message)
    {
        /// <summary>
        /// A rule that passed.
        /// </summary>
        public static Check Pass { get; } = new(true, null);

        /// <summary>
        /// A rule that failed.
        /// </summary>
        public static Check Fail(string message)
        {
            return new Check(false, message);
        }
    }

    /// <summary>
    /// The parameter rules of clause 5, applied before an intent is admitted.
    /// </summary>
    internal static class IntentValidation
    {
        public static Check Validate(IntentDataType intent, IntentControllerHostOptions options)
        {
            switch (intent)
            {
                case JointMoveIntentDataType joint:
                    if (joint.HasJointTargets)
                    {
                        if (joint.JointTargets.IsNull ||
                            joint.JointTargets.Count != (int)options.AxisCount)
                        {
                            return Bad($"JointTargets must carry {options.AxisCount} values.");
                        }
                    }
                    else
                    {
                        Check pose = ValidatePose(joint.TargetPose, nameof(joint.TargetPose));
                        if (!pose.Ok)
                        {
                            return pose;
                        }
                    }
                    break;
                case LinearMoveIntentDataType linear:
                    return ValidatePose(linear.Target, nameof(linear.Target));
                case CircularMoveIntentDataType circular:
                    Check via = ValidatePose(circular.ViaPoint, nameof(circular.ViaPoint));
                    return !via.Ok
                        ? via
                        : ValidatePose(circular.Target, nameof(circular.Target));
                case PickIntentDataType pick:
                    return pick.Source.IsNull
                        ? Bad("Pick requires a Source Location.")
                        : Check.Pass;
                case PlaceIntentDataType place:
                    return place.Destination.IsNull
                        ? Bad("Place requires a Destination Location.")
                        : Check.Pass;
                case SetOutputIntentDataType output:
                    return output.Output.IsNull
                        ? Bad("SetOutput requires an OutputSignal.")
                        : Check.Pass;
                case CallProgramIntentDataType program:
                    return program.Program.IsNull
                        ? Bad("CallProgram requires a Program.")
                        : Check.Pass;
                case TrajectoryIntentDataType trajectory:
                    return ValidateTrajectory(trajectory, options);
                case CartesianPathIntentDataType path:
                    if (path.Waypoints.IsNull || path.Waypoints.IsEmpty)
                    {
                        return Bad("A Cartesian path requires at least one waypoint.");
                    }
                    for (int ii = 0; ii < path.Waypoints.Count; ii++)
                    {
                        Check wp = ValidatePose(path.Waypoints[ii]?.Pose, $"Waypoints[{ii}].Pose");
                        if (!wp.Ok)
                        {
                            return wp;
                        }
                    }
                    return Check.Pass;
                case ForceIntentDataType force:
                    if (force.Direction.IsNull || force.Direction.Count != 3)
                    {
                        return Bad("ForceIntent.Direction must carry three values.");
                    }
                    double magnitude = 0;
                    for (int ii = 0; ii < 3; ii++)
                    {
                        magnitude += force.Direction[ii] * force.Direction[ii];
                    }
                    if (magnitude <= 0)
                    {
                        return Bad("ForceIntent.Direction must not be the zero vector.");
                    }
                    if (force.ContactForce <= 0)
                    {
                        return Bad("ForceIntent.ContactForce must be greater than zero.");
                    }
                    return force.MaxDistance <= 0
                        ? Bad("ForceIntent.MaxDistance must be greater than zero.")
                        : Check.Pass;
                case ProcessIntentDataType process:
                    return ValidateProcess(process);
            }
            return Check.Pass;
        }

        private static Check ValidateProcess(ProcessIntentDataType process)
        {
            if (process.Attributes.IsNull)
            {
                return Bad("ProcessIntent.Attributes must not be null.");
            }
            return process switch
            {
                ArcWeldIntentDataType arcWeld => NonNegative(
                    (arcWeld.Voltage, nameof(arcWeld.Voltage)),
                    (arcWeld.WireFeedSpeed, nameof(arcWeld.WireFeedSpeed)),
                    (arcWeld.TravelSpeed, nameof(arcWeld.TravelSpeed)),
                    (arcWeld.GasPreflowTime, nameof(arcWeld.GasPreflowTime)),
                    (arcWeld.GasPostflowTime, nameof(arcWeld.GasPostflowTime)),
                    (arcWeld.ArcStartDelay, nameof(arcWeld.ArcStartDelay)),
                    (arcWeld.CraterFillTime, nameof(arcWeld.CraterFillTime)),
                    (arcWeld.WeaveAmplitude, nameof(arcWeld.WeaveAmplitude)),
                    (arcWeld.WeaveFrequency, nameof(arcWeld.WeaveFrequency))),
                SpotWeldIntentDataType spotWeld => NonNegative(
                    (spotWeld.GunForce, nameof(spotWeld.GunForce)),
                    (spotWeld.ApproachDistance, nameof(spotWeld.ApproachDistance)),
                    (spotWeld.RetractDistance, nameof(spotWeld.RetractDistance)),
                    (spotWeld.MaterialThickness, nameof(spotWeld.MaterialThickness))),
                DispenseIntentDataType dispense => NonNegative(
                    (dispense.FlowRate, nameof(dispense.FlowRate)),
                    (dispense.TriggerOnDistance, nameof(dispense.TriggerOnDistance)),
                    (dispense.TriggerOffDistance, nameof(dispense.TriggerOffDistance)),
                    (dispense.BeadWidth, nameof(dispense.BeadWidth)),
                    (dispense.MaterialTemperature, nameof(dispense.MaterialTemperature))),
                FastenIntentDataType fasten => NonNegative(
                    (fasten.TargetTorque, nameof(fasten.TargetTorque)),
                    (fasten.TargetAngle, nameof(fasten.TargetAngle)),
                    (fasten.SnugTorque, nameof(fasten.SnugTorque))),
                PalletiseIntentDataType => Check.Pass,
                SurfaceFinishIntentDataType surfaceFinish => NonNegative(
                    (surfaceFinish.ContactForce, nameof(surfaceFinish.ContactForce)),
                    (surfaceFinish.FeedRate, nameof(surfaceFinish.FeedRate)),
                    (surfaceFinish.ToolSpeed, nameof(surfaceFinish.ToolSpeed)),
                    (surfaceFinish.StepOver, nameof(surfaceFinish.StepOver))),
                _ => Check.Pass
            };
        }

        private static Check NonNegative(params (double Value, string Name)[] values)
        {
            foreach ((double value, string name) in values)
            {
                if (value < 0)
                {
                    return Bad($"{name} must not be negative.");
                }
            }
            return Check.Pass;
        }

        /// <summary>
        /// A trajectory is handed over whole, so everything about it has to be right
        /// at admission: there is no later exchange in which to complain.
        /// </summary>
        private static Check ValidateTrajectory(
            TrajectoryIntentDataType trajectory, IntentControllerHostOptions options)
        {
            if (trajectory.Points.IsNull || trajectory.Points.IsEmpty)
            {
                return Bad("A trajectory requires at least one point.");
            }
            if (options.MaxTrajectoryPoints > 0 &&
                trajectory.Points.Count > (int)options.MaxTrajectoryPoints)
            {
                return Bad(FormattableString.Invariant(
                    $"A trajectory may carry at most {options.MaxTrajectoryPoints} points."));
            }
            double previous = double.NegativeInfinity;
            for (int ii = 0; ii < trajectory.Points.Count; ii++)
            {
                TrajectoryPointDataType point = trajectory.Points[ii];
                if (point == null)
                {
                    return Bad($"Points[{ii}] is null.");
                }
                if (point.TimeFromStart <= previous)
                {
                    return Bad($"Points[{ii}].TimeFromStart must exceed its predecessor's.");
                }
                previous = point.TimeFromStart;
                if (point.Positions.IsNull || point.Positions.Count != (int)options.AxisCount)
                {
                    return Bad(FormattableString.Invariant(
                        $"Points[{ii}].Positions must carry {options.AxisCount} values."));
                }
            }
            return Check.Pass;
        }

        /// <summary>
        /// Orientation is a UNIT quaternion. A Server that accepted an unnormalised one
        /// would be commanding a rotation nobody specified.
        /// </summary>
        private static Check ValidatePose(Pose3DDataType? pose, string name)
        {
            if (pose == null)
            {
                return Bad($"{name} is required.");
            }
            if (pose.Position.IsNull || pose.Position.Count != 3)
            {
                return Bad($"{name}.Position must carry three values.");
            }
            if (pose.Orientation.IsNull || pose.Orientation.Count != 4)
            {
                return Bad($"{name}.Orientation must carry four values.");
            }
            double norm = 0;
            for (int ii = 0; ii < 4; ii++)
            {
                norm += pose.Orientation[ii] * pose.Orientation[ii];
            }
            if (Math.Abs(Math.Sqrt(norm) - 1.0) > OrientationTolerance)
            {
                return Bad(FormattableString.Invariant(
                    $"{name}.Orientation must be a unit quaternion; its norm is {Math.Sqrt(norm)}."));
            }
            return Check.Pass;
        }

        private static Check Bad(string message)
        {
            return Check.Fail(message);
        }

        /// <summary>
        /// A quaternion whose norm differs from 1 by more than this is not a rotation.
        /// </summary>
        private const double OrientationTolerance = 1e-6;
    }

    /// <summary>
    /// The base and horizon rules of clause 7.
    /// </summary>
    internal static class MissionRules
    {
        /// <summary>
        /// Steps ascend by SequenceId and carry unique StepIds, and the released ones
        /// form a PREFIX - a released step after an unreleased one would make "the
        /// base" meaningless.
        /// </summary>
        public static Check ValidateSteps(ArrayOf<MissionStepDataType> steps)
        {
            if (steps.IsNull || steps.IsEmpty)
            {
                return Check.Fail("A mission must carry at least one step.");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            uint previous = 0;
            bool seenHorizon = false;
            for (int ii = 0; ii < steps.Count; ii++)
            {
                MissionStepDataType step = steps[ii];
                if (step == null || step.Intent == null)
                {
                    return Check.Fail("Every mission step must carry an intent.");
                }
                if (!ids.Add(step.StepId ?? string.Empty))
                {
                    return Check.Fail($"StepId '{step.StepId}' is not unique within the mission.");
                }
                if (ii > 0 && step.SequenceId <= previous)
                {
                    return Check.Fail("SequenceId must ascend across the steps of a mission.");
                }
                previous = step.SequenceId;
                if (!step.Released)
                {
                    seenHorizon = true;
                }
                else if (seenHorizon)
                {
                    return Check.Fail("Released steps must form a prefix: the base cannot follow the horizon.");
                }
            }
            return Check.Pass;
        }

        /// <summary>
        /// The base is committed and may already have executed, so an update that would
        /// alter, remove or reorder a released step is refused rather than partly
        /// applied.
        /// </summary>
        public static Check ValidateBasePreserved(
            ArrayOf<MissionStepDataType> current, ArrayOf<MissionStepDataType> replacement)
        {
            uint released = ReleasedCount(current);
            if (replacement.IsNull || (uint)replacement.Count < released)
            {
                return Check.Fail("The update would remove a released step.");
            }
            for (int ii = 0; ii < (int)released; ii++)
            {
                MissionStepDataType was = current[ii];
                MissionStepDataType now = replacement[ii];
                if (now == null ||
                    !string.Equals(was.StepId, now.StepId, StringComparison.Ordinal) ||
                    was.SequenceId != now.SequenceId ||
                    !now.Released ||
                    was.ErrorPolicy != now.ErrorPolicy ||
                    !string.Equals(was.FallbackStepId, now.FallbackStepId, StringComparison.Ordinal) ||
                    !IntentEqual(was.Intent, now.Intent))
                {
                    return Check.Fail($"The update would alter released step '{was.StepId}'.");
                }
            }
            return Check.Pass;
        }

        /// <summary>
        /// How many steps are in the base.
        /// </summary>
        public static uint ReleasedCount(ArrayOf<MissionStepDataType> steps)
        {
            if (steps.IsNull)
            {
                return 0;
            }
            uint count = 0;
            for (int ii = 0; ii < steps.Count; ii++)
            {
                if (steps[ii] is { Released: true })
                {
                    count++;
                }
                else
                {
                    break;
                }
            }
            return count;
        }

        /// <summary>
        /// Checks the step graph against clause 7.4.
        /// </summary>
        /// <remarks>
        /// A transition naming a step that does not exist, or a step whose outgoing
        /// transitions mix Alternative with Parallel, is refused rather than resolved
        /// by guessing: a mission that branches somewhere the author did not intend is
        /// worse than one that will not start.
        /// </remarks>
        public static Check ValidateTransitions(
            ArrayOf<MissionStepDataType> steps, ArrayOf<MissionTransitionDataType> transitions)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < steps.Count; ii++)
            {
                ids.Add(steps[ii]?.StepId ?? string.Empty);
            }

            for (int ii = 0; ii < steps.Count; ii++)
            {
                MissionStepDataType step = steps[ii];
                if (step == null)
                {
                    continue;
                }
                bool needsFallback = step.ErrorPolicy is ErrorPolicyEnum.Fallback
                    or ErrorPolicyEnum.Compensate;
                if (needsFallback && !ids.Contains(step.FallbackStepId ?? string.Empty))
                {
                    return Check.Fail(
                        $"Step '{step.StepId}' declares {step.ErrorPolicy} but its " +
                        "FallbackStepId names no step of this mission.");
                }
            }

            if (transitions.IsNull || transitions.IsEmpty)
            {
                return Check.Pass;
            }

            var divergence = new Dictionary<string, DivergenceKindEnum>(StringComparer.Ordinal);
            for (int ii = 0; ii < transitions.Count; ii++)
            {
                MissionTransitionDataType edge = transitions[ii];
                if (edge == null)
                {
                    return Check.Fail($"Transitions[{ii}] is null.");
                }
                if (!ids.Contains(edge.FromStepId ?? string.Empty))
                {
                    return Check.Fail(
                        $"Transitions[{ii}].FromStepId '{edge.FromStepId}' names no step " +
                        "of this mission.");
                }
                if (!ids.Contains(edge.ToStepId ?? string.Empty))
                {
                    return Check.Fail(
                        $"Transitions[{ii}].ToStepId '{edge.ToStepId}' names no step " +
                        "of this mission.");
                }
                string from = edge.FromStepId ?? string.Empty;
                if (edge.DivergenceKind == DivergenceKindEnum.Parallel)
                {
                    return Check.Fail(
                        "Parallel mission divergence requires concurrent branch execution, which this " +
                        "serial host does not support.");
                }
                if (divergence.TryGetValue(from, out DivergenceKindEnum seen))
                {
                    if (seen != edge.DivergenceKind)
                    {
                        return Check.Fail(
                            $"Step '{from}' mixes {seen} and {edge.DivergenceKind} " +
                            "divergence on its outgoing transitions.");
                    }
                }
                else
                {
                    divergence[from] = edge.DivergenceKind;
                }
            }
            return Check.Pass;
        }

        /// <summary>
        /// The first transition out of a step whose condition holds.
        /// </summary>
        /// <remarks>
        /// Evaluated in array order so that two clients reading one mission predict the
        /// same branch. An empty ContentFilter is always true, which is what makes a
        /// default branch expressible without a special case.
        /// </remarks>
        public static MissionTransitionSelection SelectTransition(
            ArrayOf<MissionTransitionDataType> transitions,
            string fromStepId,
            Func<ContentFilter, bool> evaluate)
        {
            if (transitions.IsNull)
            {
                return MissionTransitionSelection.Terminus;
            }
            bool hasOutgoingTransitions = false;
            for (int ii = 0; ii < transitions.Count; ii++)
            {
                MissionTransitionDataType edge = transitions[ii];
                if (edge == null || !string.Equals(edge.FromStepId, fromStepId, StringComparison.Ordinal))
                {
                    continue;
                }
                hasOutgoingTransitions = true;
                if (evaluate(edge.Condition))
                {
                    return MissionTransitionSelection.Selected(edge);
                }
            }
            return hasOutgoingTransitions
                ? MissionTransitionSelection.NoConditionMatched
                : MissionTransitionSelection.Terminus;
        }

        /// <summary>
        /// The index of the step with the given identifier, or -1.
        /// </summary>
        public static int IndexOfStep(ArrayOf<MissionStepDataType> steps, string stepId)
        {
            if (steps.IsNull)
            {
                return -1;
            }
            for (int ii = 0; ii < steps.Count; ii++)
            {
                if (string.Equals(steps[ii]?.StepId, stepId, StringComparison.Ordinal))
                {
                    return ii;
                }
            }

            return -1;
        }

        /// <summary>
        /// The step at the given index, when there is one.
        /// </summary>
        public static MissionStepDataType? NextPending(ArrayOf<MissionStepDataType> steps, int index)
        {
            if (steps.IsNull || index < 0 || index >= steps.Count)
            {
                return null;
            }
            return steps[index];
        }

        /// <summary>
        /// Records a step's reported status.
        /// </summary>
        /// <remarks>
        /// Status is a HINT. Where Operation is not null the IntentOperation's state
        /// machine decides, and this keeps the hint faithful to it.
        /// </remarks>
        public static void SetStatus(
            ArrayOf<MissionStepDataType> steps, int index, ExecutionStateEnum state, NodeId? operation)
        {
            if (steps.IsNull || index < 0 || index >= steps.Count)
            {
                return;
            }
            MissionStepDataType step = steps[index];
            if (step == null)
            {
                return;
            }
            step.Status = state;
            if (operation.HasValue)
            {
                step.Operation = operation.Value;
            }
        }

        private static bool IntentEqual(IntentDataType? left, IntentDataType? right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }
            if (left.Clone() is not IntentDataType leftComparable ||
                right.Clone() is not IntentDataType rightComparable)
            {
                return false;
            }
            leftComparable.IntentId = string.Empty;
            rightComparable.IntentId = string.Empty;
            return leftComparable.IsEqual(rightComparable);
        }
    }

    internal sealed record MissionTransitionSelection(
        MissionTransitionDataType? Transition,
        bool HasOutgoingTransitions)
    {
        public static MissionTransitionSelection Terminus { get; } = new(null, false);

        public static MissionTransitionSelection NoConditionMatched { get; } = new(null, true);

        public static MissionTransitionSelection Selected(MissionTransitionDataType transition)
        {
            return new MissionTransitionSelection(transition, true);
        }
    }
}

namespace Opc.Ua.RobotIntent.Server
{
    /// <summary>
    /// Reports a per-invocation node that could not be published.
    /// </summary>
    /// <param name="Node">The node that could not be added.</param>
    /// <param name="Error">Why it could not.</param>
    public sealed record IntentNodeAddFailure(NodeState Node, Exception Error);
}
