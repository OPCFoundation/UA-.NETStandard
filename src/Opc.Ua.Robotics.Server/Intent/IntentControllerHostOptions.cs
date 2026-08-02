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
        /// Whether a caller must hold command authority to submit. Defaults to true;
        /// turning it off is for single-client test hosts only.
        /// </summary>
        public bool RequireControlAuthority { get; set; } = true;

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
        /// Declares support for an intent type.
        /// </summary>
        public IntentControllerHostOptions Accept(
            ExpandedNodeId intentType,
            bool cancelSupported = true,
            bool pauseSupported = true,
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
        /// <summary>The intent DataType this host accepts.</summary>
        public ExpandedNodeId IntentType { get; init; } = ExpandedNodeId.Null;

        /// <summary>What this host does with it.</summary>
        public string? Description { get; init; }

        /// <summary>Whether Cancel is honoured for it.</summary>
        public bool CancelSupported { get; init; } = true;

        /// <summary>Whether Pause and Resume are honoured for it.</summary>
        public bool PauseSupported { get; init; } = true;

        /// <summary>Whether it can terminate Retriable.</summary>
        public bool RetrySupported { get; init; }

        /// <summary>Buffer modes accepted for it. Aborting is always accepted.</summary>
        public ArrayOf<BufferModeEnum> SupportedBufferModes { get; init; } = new[]
        {
            BufferModeEnum.Aborting,
            BufferModeEnum.Buffered
        };

        /// <summary>Blocking modes accepted for it.</summary>
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
            return new IntentCapabilityDataType
            {
                IntentType = ExpandedNodeId.ToNodeId(IntentType, namespaceUris),
                Description = new LocalizedText(Description ?? string.Empty),
                CancelSupported = CancelSupported,
                PauseSupported = PauseSupported,
                RetrySupported = RetrySupported,
                SupportedBufferModes = SupportedBufferModes,
                SupportedBlockingModes = SupportedBlockingModes
            };
        }
    }

    /// <summary>
    /// The outcome of admitting one intent.
    /// </summary>
    public sealed record IntentAdmission
    {
        /// <summary>Whether the intent was admitted.</summary>
        public bool Accepted { get; init; }

        /// <summary>The identifier it was admitted under.</summary>
        public string IntentId { get; init; } = string.Empty;

        /// <summary>The IntentOperation that tracks it.</summary>
        public NodeId Operation { get; init; } = NodeId.Null;

        /// <summary>Why it was refused.</summary>
        public IntentFailureEnum Failure { get; init; } = IntentFailureEnum.None;

        /// <summary>Human-readable detail on a refusal.</summary>
        public string? Message { get; init; }

        /// <summary>Creates an accepted admission.</summary>
        public static IntentAdmission Admitted(string intentId, NodeId operation)
        {
            return new IntentAdmission
            {
                Accepted = true,
                IntentId = intentId,
                Operation = operation
            };
        }

        /// <summary>Creates a refusal.</summary>
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
        /// <summary>Whether the mission was admitted.</summary>
        public bool Accepted { get; init; }

        /// <summary>The identifier it was admitted under.</summary>
        public string MissionId { get; init; } = string.Empty;

        /// <summary>The Mission that tracks it.</summary>
        public NodeId Operation { get; init; } = NodeId.Null;

        /// <summary>Why it was refused.</summary>
        public MissionUpdateResultEnum Result { get; init; } = MissionUpdateResultEnum.Accepted;

        /// <summary>Human-readable detail on a refusal.</summary>
        public string? Message { get; init; }

        /// <summary>Creates an accepted admission.</summary>
        public static MissionAdmission Admitted(string missionId, NodeId operation)
        {
            return new MissionAdmission
            {
                Accepted = true,
                MissionId = missionId,
                Operation = operation
            };
        }

        /// <summary>Creates a refusal.</summary>
        public static MissionAdmission Refused(MissionUpdateResultEnum result, string message)
        {
            return new MissionAdmission { Result = result, Message = message };
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
        /// <summary>A rule that passed.</summary>
        public static Check Pass { get; } = new(true, null);

        /// <summary>A rule that failed.</summary>
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
        /// <summary>
        /// A quaternion whose norm differs from 1 by more than this is not a rotation.
        /// </summary>
        private const double OrientationTolerance = 1e-6;

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
                default:
                    break;
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
                return Bad(string.Create(CultureInfo.InvariantCulture,
                    $"{name}.Orientation must be a unit quaternion; its norm is {Math.Sqrt(norm)}."));
            }
            return Check.Pass;
        }

        private static Check Bad(string message)
        {
            return Check.Fail(message);
        }
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
                    !now.Released)
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
