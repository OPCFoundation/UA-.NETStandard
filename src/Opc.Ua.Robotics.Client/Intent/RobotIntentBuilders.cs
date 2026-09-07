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
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Client.Intent
{
    /// <summary>
    /// Factory methods for Robot Intent fluent builders.
    /// </summary>
    public static class RobotIntentBuilder
    {
        /// <summary>
        /// Creates a pose from position and quaternion values.
        /// </summary>
        public static Pose3DDataType Pose(
            double x,
            double y,
            double z,
            double qx,
            double qy,
            double qz,
            double qw,
            string frameId = "")
        {
            return new Pose3DDataType
            {
                FrameId = frameId,
                Position = [x, y, z],
                Orientation = [qx, qy, qz, qw]
            };
        }

        /// <summary>
        /// Creates a JointMove builder.
        /// </summary>
        public static JointMoveIntentBuilder JointMove(uint axisCount)
        {
            return new JointMoveIntentBuilder(axisCount);
        }

        /// <summary>
        /// Creates a LinearMove builder.
        /// </summary>
        public static LinearMoveIntentBuilder LinearMove(Pose3DDataType target, double speed)
        {
            return new LinearMoveIntentBuilder().To(target).Speed(speed);
        }

        /// <summary>
        /// Creates a CircularMove builder.
        /// </summary>
        public static CircularMoveIntentBuilder CircularMove(Pose3DDataType viaPoint, Pose3DDataType target)
        {
            return new CircularMoveIntentBuilder().Via(viaPoint).To(target);
        }

        /// <summary>
        /// Creates a Trajectory builder.
        /// </summary>
        public static TrajectoryIntentBuilder Trajectory()
        {
            return new TrajectoryIntentBuilder();
        }

        /// <summary>
        /// Creates a CartesianPath builder.
        /// </summary>
        public static CartesianPathIntentBuilder CartesianPath()
        {
            return new CartesianPathIntentBuilder();
        }

        /// <summary>
        /// Creates a Force builder.
        /// </summary>
        public static ForceIntentBuilder Force(ArrayOf<double> direction, double contactForce)
        {
            return new ForceIntentBuilder().Direction(direction).ContactForce(contactForce);
        }

        /// <summary>
        /// Creates an ArcWeld builder.
        /// </summary>
        public static ProcessIntentBuilder<ArcWeldIntentDataType> ArcWeld()
        {
            return new ProcessIntentBuilder<ArcWeldIntentDataType>(new ArcWeldIntentDataType());
        }

        /// <summary>
        /// Creates a SpotWeld builder.
        /// </summary>
        public static ProcessIntentBuilder<SpotWeldIntentDataType> SpotWeld()
        {
            return new ProcessIntentBuilder<SpotWeldIntentDataType>(new SpotWeldIntentDataType());
        }

        /// <summary>
        /// Creates a Dispense builder.
        /// </summary>
        public static ProcessIntentBuilder<DispenseIntentDataType> Dispense()
        {
            return new ProcessIntentBuilder<DispenseIntentDataType>(new DispenseIntentDataType());
        }

        /// <summary>
        /// Creates a Fasten builder.
        /// </summary>
        public static ProcessIntentBuilder<FastenIntentDataType> Fasten()
        {
            return new ProcessIntentBuilder<FastenIntentDataType>(new FastenIntentDataType());
        }

        /// <summary>
        /// Creates a Palletise builder.
        /// </summary>
        public static ProcessIntentBuilder<PalletiseIntentDataType> Palletise()
        {
            return new ProcessIntentBuilder<PalletiseIntentDataType>(new PalletiseIntentDataType());
        }

        /// <summary>
        /// Creates a SurfaceFinish builder.
        /// </summary>
        public static ProcessIntentBuilder<SurfaceFinishIntentDataType> SurfaceFinish()
        {
            return new ProcessIntentBuilder<SurfaceFinishIntentDataType>(new SurfaceFinishIntentDataType());
        }

        /// <summary>
        /// Creates a Grasp builder.
        /// </summary>
        public static SimpleIntentBuilder<GraspIntentDataType> Grasp(NodeId tool, double force)
        {
            GraspIntentDataType intent = new() { Tool = tool, Force = force };
            return new SimpleIntentBuilder<GraspIntentDataType>(intent);
        }

        /// <summary>
        /// Creates a Release builder.
        /// </summary>
        public static SimpleIntentBuilder<ReleaseIntentDataType> Release(NodeId tool)
        {
            return new SimpleIntentBuilder<ReleaseIntentDataType>(new ReleaseIntentDataType { Tool = tool });
        }

        /// <summary>
        /// Creates a Pick builder.
        /// </summary>
        /// <param name="source">The Location to pick from.</param>
        /// <param name="tool">The Tool to acquire the object with.</param>
        /// <param name="objectClass">
        /// What to pick, for a Location that can hold more than one kind of object. Empty
        /// means whatever is there, which is only unambiguous for a single-kind Location.
        /// </param>
        public static SimpleIntentBuilder<PickIntentDataType> Pick(
            NodeId source,
            NodeId tool,
            string objectClass = "")
        {
            return new SimpleIntentBuilder<PickIntentDataType>(
                new PickIntentDataType { Source = source, Tool = tool, ObjectClass = objectClass });
        }

        /// <summary>
        /// Creates a Place builder.
        /// </summary>
        public static SimpleIntentBuilder<PlaceIntentDataType> Place(NodeId destination, NodeId tool)
        {
            return new SimpleIntentBuilder<PlaceIntentDataType>(
                new PlaceIntentDataType { Destination = destination, Tool = tool });
        }

        /// <summary>
        /// Creates a ToolChange builder.
        /// </summary>
        public static SimpleIntentBuilder<ToolChangeIntentDataType> ToolChange(NodeId tool, NodeId dockStation)
        {
            return new SimpleIntentBuilder<ToolChangeIntentDataType>(
                new ToolChangeIntentDataType { Tool = tool, DockStation = dockStation });
        }

        /// <summary>
        /// Creates a SetOutput builder.
        /// </summary>
        public static SimpleIntentBuilder<SetOutputIntentDataType> SetOutput(NodeId output, Variant value)
        {
            return new SimpleIntentBuilder<SetOutputIntentDataType>(
                new SetOutputIntentDataType { Output = output, Value = value });
        }

        /// <summary>
        /// Creates a CallProgram builder.
        /// </summary>
        public static SimpleIntentBuilder<CallProgramIntentDataType> CallProgram(NodeId program)
        {
            return new SimpleIntentBuilder<CallProgramIntentDataType>(
                new CallProgramIntentDataType { Program = program });
        }

        /// <summary>
        /// Creates a Wait builder.
        /// </summary>
        public static SimpleIntentBuilder<WaitIntentDataType> Wait(double duration)
        {
            return new SimpleIntentBuilder<WaitIntentDataType>(new WaitIntentDataType { Duration = duration });
        }

        /// <summary>
        /// Creates a mission builder.
        /// </summary>
        public static MissionBuilder Mission(string missionId = "")
        {
            return new MissionBuilder(missionId);
        }
    }

    /// <summary>
    /// Base builder for intent structures.
    /// </summary>
    /// <typeparam name="TIntent">The concrete intent DataType.</typeparam>
    /// <typeparam name="TBuilder">The concrete builder type.</typeparam>
    public abstract class IntentBuilder<TIntent, TBuilder>
        where TIntent : IntentDataType
        where TBuilder : IntentBuilder<TIntent, TBuilder>
    {
        /// <summary>
        /// Initializes a new builder.
        /// </summary>
        protected IntentBuilder(TIntent intent)
        {
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        }

        /// <summary>
        /// Gets the intent being built.
        /// </summary>
        protected TIntent Intent { get; }

        /// <summary>
        /// Sets IntentId.
        /// </summary>
        public TBuilder WithIntentId(string intentId)
        {
            Intent.IntentId = intentId;
            return This();
        }

        /// <summary>
        /// Sets Label.
        /// </summary>
        public TBuilder WithLabel(LocalizedText label)
        {
            Intent.Label = label;
            return This();
        }

        /// <summary>
        /// Sets BufferMode.
        /// </summary>
        public TBuilder WithBufferMode(BufferModeEnum bufferMode)
        {
            Intent.BufferMode = bufferMode;
            return This();
        }

        /// <summary>
        /// Sets BlockingMode.
        /// </summary>
        public TBuilder WithBlockingMode(BlockingModeEnum blockingMode)
        {
            Intent.BlockingMode = blockingMode;
            return This();
        }

        /// <summary>
        /// Builds the intent.
        /// </summary>
        public virtual TIntent Build()
        {
            return Intent;
        }

        private TBuilder This()
        {
            return (TBuilder)this;
        }
    }

    /// <summary>
    /// Builder for non-motion intents.
    /// </summary>
    /// <typeparam name="TIntent">The concrete intent DataType.</typeparam>
    public sealed class SimpleIntentBuilder<TIntent> : IntentBuilder<TIntent, SimpleIntentBuilder<TIntent>>
        where TIntent : IntentDataType
    {
        /// <summary>
        /// Initializes a simple intent builder.
        /// </summary>
        public SimpleIntentBuilder(TIntent intent)
            : base(intent)
        {
        }
    }

    /// <summary>
    /// Base builder for motion intents.
    /// </summary>
    /// <typeparam name="TIntent">The concrete motion intent DataType.</typeparam>
    /// <typeparam name="TBuilder">The concrete builder type.</typeparam>
    public abstract class MotionIntentBuilder<TIntent, TBuilder> : IntentBuilder<TIntent, TBuilder>
        where TIntent : MotionIntentDataType
        where TBuilder : MotionIntentBuilder<TIntent, TBuilder>
    {
        /// <summary>
        /// Initializes a motion builder.
        /// </summary>
        protected MotionIntentBuilder(TIntent intent)
            : base(intent)
        {
        }

        /// <summary>
        /// Sets ToolFrame.
        /// </summary>
        public TBuilder WithToolFrame(NodeId toolFrame)
        {
            Intent.ToolFrame = toolFrame;
            return This();
        }

        /// <summary>
        /// Sets motion constraints.
        /// </summary>
        public TBuilder WithConstraints(MotionConstraintsDataType constraints)
        {
            Intent.Constraints = constraints;
            return This();
        }

        /// <summary>
        /// Sets a speed fraction.
        /// </summary>
        public TBuilder Speed(double speedFraction)
        {
            Intent.Constraints.SpeedFraction = speedFraction;
            return This();
        }

        /// <summary>
        /// Sets a Cartesian speed in metres per second.
        /// </summary>
        public TBuilder CartesianSpeed(double speed)
        {
            Intent.Constraints.CartesianSpeed = speed;
            return This();
        }

        /// <summary>
        /// Sets blending.
        /// </summary>
        public TBuilder WithBlend(BlendDataType blend)
        {
            Intent.Blend = blend;
            return This();
        }

        /// <summary>
        /// Sets an exact termination.
        /// </summary>
        public TBuilder Exact()
        {
            Intent.Blend.Termination = TerminationModeEnum.Exact;
            Intent.Blend.Radius = 0;
            return This();
        }

        /// <summary>
        /// Sets a blend radius.
        /// </summary>
        public TBuilder Blend(double radius)
        {
            Intent.Blend.Termination = TerminationModeEnum.Blend;
            Intent.Blend.Radius = radius;
            return This();
        }

        private TBuilder This()
        {
            return (TBuilder)this;
        }
    }

    /// <summary>
    /// Builder for JointMove intents.
    /// </summary>
    public sealed class JointMoveIntentBuilder : MotionIntentBuilder<JointMoveIntentDataType, JointMoveIntentBuilder>
    {
        /// <summary>
        /// Initializes a JointMove builder.
        /// </summary>
        public JointMoveIntentBuilder(uint axisCount)
            : base(new JointMoveIntentDataType())
        {
            m_axisCount = axisCount;
        }

        /// <summary>
        /// Sets joint targets and validates the axis count.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public JointMoveIntentBuilder ToJoints(ArrayOf<double> jointTargets)
        {
            if (m_axisCount > 0 && jointTargets.Count != m_axisCount)
            {
                throw new ArgumentException(
                    $"Joint target count {jointTargets.Count} does not match AxisCount {m_axisCount}.",
                    nameof(jointTargets));
            }
            Intent.HasJointTargets = true;
            Intent.JointTargets = jointTargets;
            Intent.TargetPose = new Pose3DDataType();
            return this;
        }

        /// <summary>
        /// Sets a target pose.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public JointMoveIntentBuilder ToPose(Pose3DDataType pose)
        {
            Intent.HasJointTargets = false;
            Intent.JointTargets = [];
            Intent.TargetPose = pose ?? throw new ArgumentNullException(nameof(pose));
            return this;
        }

        private readonly uint m_axisCount;
    }

    /// <summary>
    /// Builder for LinearMove intents.
    /// </summary>
    public sealed class LinearMoveIntentBuilder : MotionIntentBuilder<LinearMoveIntentDataType, LinearMoveIntentBuilder>
    {
        /// <summary>
        /// Initializes a LinearMove builder.
        /// </summary>
        public LinearMoveIntentBuilder()
            : base(new LinearMoveIntentDataType())
        {
        }

        /// <summary>
        /// Sets the target pose.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public LinearMoveIntentBuilder To(Pose3DDataType target)
        {
            Intent.Target = target ?? throw new ArgumentNullException(nameof(target));
            return this;
        }
    }

    /// <summary>
    /// Builder for CircularMove intents.
    /// </summary>
    public sealed class CircularMoveIntentBuilder :
        MotionIntentBuilder<CircularMoveIntentDataType, CircularMoveIntentBuilder>
    {
        /// <summary>
        /// Initializes a CircularMove builder.
        /// </summary>
        public CircularMoveIntentBuilder()
            : base(new CircularMoveIntentDataType())
        {
        }

        /// <summary>
        /// Sets the via point.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public CircularMoveIntentBuilder Via(Pose3DDataType viaPoint)
        {
            Intent.ViaPoint = viaPoint ?? throw new ArgumentNullException(nameof(viaPoint));
            return this;
        }

        /// <summary>
        /// Sets the target pose.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public CircularMoveIntentBuilder To(Pose3DDataType target)
        {
            Intent.Target = target ?? throw new ArgumentNullException(nameof(target));
            return this;
        }
    }

    /// <summary>
    /// Builder for trajectory intents.
    /// </summary>
    public sealed class TrajectoryIntentBuilder :
        MotionIntentBuilder<TrajectoryIntentDataType, TrajectoryIntentBuilder>
    {
        /// <summary>
        /// Initializes a trajectory builder.
        /// </summary>
        public TrajectoryIntentBuilder()
            : base(new TrajectoryIntentDataType())
        {
        }

        /// <summary>
        /// Replaces trajectory points.
        /// </summary>
        public TrajectoryIntentBuilder WithPoints(ArrayOf<TrajectoryPointDataType> points)
        {
            Intent.Points = points;
            return this;
        }
    }

    /// <summary>
    /// Builder for Cartesian path intents.
    /// </summary>
    public sealed class CartesianPathIntentBuilder :
        MotionIntentBuilder<CartesianPathIntentDataType, CartesianPathIntentBuilder>
    {
        /// <summary>
        /// Initializes a CartesianPath builder.
        /// </summary>
        public CartesianPathIntentBuilder()
            : base(new CartesianPathIntentDataType())
        {
        }

        /// <summary>
        /// Replaces waypoints.
        /// </summary>
        public CartesianPathIntentBuilder WithWaypoints(ArrayOf<PathWaypointDataType> waypoints)
        {
            Intent.Waypoints = waypoints;
            return this;
        }
    }

    /// <summary>
    /// Builder for Force intents.
    /// </summary>
    public sealed class ForceIntentBuilder : MotionIntentBuilder<ForceIntentDataType, ForceIntentBuilder>
    {
        /// <summary>
        /// Initializes a Force builder.
        /// </summary>
        public ForceIntentBuilder()
            : base(new ForceIntentDataType())
        {
        }

        /// <summary>
        /// Sets the force direction.
        /// </summary>
        public ForceIntentBuilder Direction(ArrayOf<double> direction)
        {
            Intent.Direction = direction;
            return this;
        }

        /// <summary>
        /// Sets ContactForce.
        /// </summary>
        public ForceIntentBuilder ContactForce(double force)
        {
            Intent.ContactForce = force;
            return this;
        }
    }

    /// <summary>
    /// Builder for process intents.
    /// </summary>
    /// <typeparam name="TIntent">The concrete process intent DataType.</typeparam>
    public sealed class ProcessIntentBuilder<TIntent> : MotionIntentBuilder<TIntent, ProcessIntentBuilder<TIntent>>
        where TIntent : ProcessIntentDataType
    {
        /// <summary>
        /// Initializes a process builder.
        /// </summary>
        public ProcessIntentBuilder(TIntent intent)
            : base(intent)
        {
        }

        /// <summary>
        /// Sets ProcessProgram.
        /// </summary>
        public ProcessIntentBuilder<TIntent> WithProcessProgram(NodeId program)
        {
            Intent.ProcessProgram = program;
            return this;
        }

        /// <summary>
        /// Sets process attributes.
        /// </summary>
        public ProcessIntentBuilder<TIntent> WithAttributes(ArrayOf<KeyValuePair> attributes)
        {
            Intent.Attributes = attributes;
            return this;
        }
    }

    /// <summary>
    /// Fluent builder for missions with a base, a horizon and a step graph.
    /// </summary>
    public sealed class MissionBuilder
    {
        /// <summary>
        /// Initializes a mission builder.
        /// </summary>
        public MissionBuilder(string missionId)
        {
            m_mission = new MissionDataType { MissionId = missionId };
        }

        /// <summary>
        /// Sets the mission update id.
        /// </summary>
        public MissionBuilder WithMissionUpdateId(uint updateId)
        {
            m_mission.MissionUpdateId = updateId;
            return this;
        }

        /// <summary>
        /// Sets the mission label.
        /// </summary>
        public MissionBuilder WithLabel(LocalizedText label)
        {
            m_mission.Label = label;
            return this;
        }

        /// <summary>
        /// Replaces the mission steps, then validates StepId uniqueness, SequenceId ordering and the released prefix.
        /// </summary>
        public MissionBuilder WithSteps(ArrayOf<MissionStepDataType> steps)
        {
            m_steps.Clear();
            for (int i = 0; i < steps.Count; i++)
            {
                m_steps.Add(steps[i]);
            }
            return this;
        }

        /// <summary>
        /// Replaces the mission transitions. An empty array is valid and means a flat ordered sequence.
        /// </summary>
        public MissionBuilder WithTransitions(ArrayOf<MissionTransitionDataType> transitions)
        {
            m_transitions.Clear();
            for (int i = 0; i < transitions.Count; i++)
            {
                m_transitions.Add(transitions[i]);
            }
            return this;
        }

        /// <summary>
        /// Adds a committed base step.
        /// </summary>
        public MissionBuilder ReleasedStep(string stepId, IntentDataType intent)
        {
            return Step(stepId, intent, released: true);
        }

        /// <summary>
        /// Adds a horizon step.
        /// </summary>
        public MissionBuilder HorizonStep(string stepId, IntentDataType intent)
        {
            return Step(stepId, intent, released: false);
        }

        /// <summary>
        /// Adds a transition. An empty ContentFilter always passes.
        /// </summary>
        public MissionBuilder Transition(
            string fromStepId,
            string toStepId,
            DivergenceKindEnum divergenceKind,
            ContentFilter? condition = null)
        {
            m_transitions.Add(new MissionTransitionDataType
            {
                FromStepId = fromStepId,
                ToStepId = toStepId,
                DivergenceKind = divergenceKind,
                Condition = condition ?? MissionCondition.Always()
            });
            return this;
        }

        /// <summary>
        /// Sets the error policy for an existing step.
        /// </summary>
        public MissionBuilder ErrorPolicy(
            string stepId,
            ErrorPolicyEnum errorPolicy,
            string fallbackStepId = "")
        {
            MissionStepDataType step = FindStep(stepId);
            step.ErrorPolicy = errorPolicy;
            step.FallbackStepId = fallbackStepId;
            return this;
        }

        /// <summary>
        /// Builds the mission and validates the client-side graph rules from clause 7.4.
        /// </summary>
        public MissionDataType Build()
        {
            Validate();
            m_mission.Steps = [.. m_steps];
            m_mission.Transitions = [.. m_transitions];
            return m_mission;
        }

        private MissionBuilder Step(string stepId, IntentDataType intent, bool released)
        {
            if (string.IsNullOrEmpty(stepId))
            {
                throw new ArgumentException("StepId is required.", nameof(stepId));
            }
            if (m_steps.Any(s => string.Equals(s.StepId, stepId, StringComparison.Ordinal)))
            {
                throw new ArgumentException($"Step '{stepId}' already exists.", nameof(stepId));
            }
            m_steps.Add(new MissionStepDataType
            {
                StepId = stepId,
                SequenceId = (uint)(m_steps.Count + 1),
                Released = released,
                Intent = intent ?? throw new ArgumentNullException(nameof(intent))
            });
            return this;
        }

        private MissionStepDataType FindStep(string stepId)
        {
            MissionStepDataType? step = m_steps.FirstOrDefault(s =>
                string.Equals(s.StepId, stepId, StringComparison.Ordinal));
            return step ?? throw new ArgumentException($"Unknown step '{stepId}'.", nameof(stepId));
        }

        private void Validate()
        {
            var stepIds = new HashSet<string>(m_steps.Select(s => s.StepId ?? string.Empty), StringComparer.Ordinal);
            if (stepIds.Count != m_steps.Count || stepIds.Contains(string.Empty))
            {
                throw new InvalidOperationException("StepId shall be non-empty and unique within the mission.");
            }

            uint previousSequenceId = 0;
            bool horizonStarted = false;
            foreach (MissionStepDataType step in m_steps)
            {
                if (step.SequenceId <= previousSequenceId)
                {
                    throw new InvalidOperationException("SequenceId shall ascend across mission steps.");
                }
                previousSequenceId = step.SequenceId;

                if (!step.Released)
                {
                    horizonStarted = true;
                }
                else if (horizonStarted)
                {
                    throw new InvalidOperationException("Released mission steps shall form a prefix.");
                }
            }

            foreach (MissionTransitionDataType transition in m_transitions)
            {
                if (!stepIds.Contains(transition.FromStepId ?? string.Empty))
                {
                    throw new InvalidOperationException(
                        $"Transition names unknown FromStepId '{transition.FromStepId}'.");
                }
                if (!stepIds.Contains(transition.ToStepId ?? string.Empty))
                {
                    throw new InvalidOperationException(
                        $"Transition names unknown ToStepId '{transition.ToStepId}'.");
                }
            }

            foreach (IGrouping<string, MissionTransitionDataType> group in m_transitions.GroupBy(
                t => t.FromStepId ?? string.Empty, StringComparer.Ordinal))
            {
                if (group.Select(t => t.DivergenceKind).Distinct().Count() > 1)
                {
                    throw new InvalidOperationException(
                        $"Step '{group.Key}' mixes Alternative and Parallel divergence kinds.");
                }
            }

            foreach (MissionStepDataType step in m_steps)
            {
                bool requiresFallback = step.ErrorPolicy is ErrorPolicyEnum.Fallback or ErrorPolicyEnum.Compensate;
                if (requiresFallback && !stepIds.Contains(step.FallbackStepId ?? string.Empty))
                {
                    throw new InvalidOperationException(
                        $"Step '{step.StepId}' names unknown fallback step '{step.FallbackStepId}'.");
                }
            }
        }

        private readonly List<MissionStepDataType> m_steps = [];
        private readonly List<MissionTransitionDataType> m_transitions = [];
        private readonly MissionDataType m_mission;
    }

    /// <summary>
    /// Helpers for mission transition conditions.
    /// </summary>
    public static class MissionCondition
    {
        /// <summary>
        /// Creates an empty ContentFilter. OPC UA defines an empty ContentFilter as always true.
        /// </summary>
        public static ContentFilter Always()
        {
            return new ContentFilter();
        }

        /// <summary>
        /// Creates an equality condition comparing an attribute operand with a literal value.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static ContentFilter Equals(SimpleAttributeOperand operand, Variant value)
        {
            if (operand is null)
            {
                throw new ArgumentNullException(nameof(operand));
            }
            ContentFilter filter = new();
            filter.Push(FilterOperator.Equals, new Variant(new ExtensionObject(operand)), value);
            return filter;
        }
    }
}
