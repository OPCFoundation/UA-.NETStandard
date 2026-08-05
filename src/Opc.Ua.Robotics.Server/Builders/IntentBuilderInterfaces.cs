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
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Exposes the generated state built by a Robot Intent node builder.
    /// </summary>
    /// <typeparam name="TState">
    /// The generated state type.
    /// </typeparam>
    public interface IIntentNodeBuilder<out TState>
        where TState : NodeState
    {
        /// <summary>
        /// Gets the generated state.
        /// </summary>
        TState State { get; }
    }

    /// <summary>
    /// Publishes read-only safety state asserted by a safety system.
    /// </summary>
    public interface IRobotIntentSafetySource
    {
        /// <summary>
        /// Reads the current safety status.
        /// </summary>
        ValueTask<RobotIntentSafetySnapshot> ReadAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Immutable safety status sample.
    /// </summary>
    public readonly record struct RobotIntentSafetySnapshot(
        SafeMotionFunctionEnum ActiveFunction,
        bool EmergencyStopActive,
        bool ProtectiveStopActive,
        bool SafeSpeedLimitActive,
        double SafeSpeedLimit,
        bool SafetyControllerOk,
        LocalizedText LastStopReason);

    /// <summary>
    /// Builds one IntentControllerType instance.
    /// </summary>
    public interface IIntentControllerBuilder : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.IntentControllerState>
    {
        /// <summary>
        /// Gets the execution host that was started for this controller.
        /// </summary>
        IntentControllerHost Host { get; }

        /// <summary>
        /// Sets the reported operational mode.
        /// </summary>
        IIntentControllerBuilder WithOperationalMode(OperationalModeEnum mode);

        /// <summary>
        /// Sets the reported readiness flag.
        /// </summary>
        IIntentControllerBuilder WithReady(bool ready);

        /// <summary>
        /// Sets the maximum queue depth.
        /// </summary>
        IIntentControllerBuilder WithMaxQueueDepth(uint maxQueueDepth);

        /// <summary>
        /// Adds a coordinate frame under Frames.
        /// </summary>
        IIntentFrameBuilder AddFrame(
            string browseName,
            string frameId,
            FrameRoleEnum role,
            Pose3DDataType transform,
            Action<IIntentFrameBuilder>? configure = null);

        /// <summary>
        /// Adds a tool under Tools.
        /// </summary>
        IIntentToolBuilder AddTool(
            string browseName,
            IIntentFrameBuilder tcpFrame,
            bool fitted = false,
            Action<IIntentToolBuilder>? configure = null);

        /// <summary>
        /// Adds a location under Locations.
        /// </summary>
        IIntentLocationBuilder AddLocation(
            string browseName,
            Pose3DDataType pose,
            Action<IIntentLocationBuilder>? configure = null);

        /// <summary>
        /// Adds an axis under Axes.
        /// </summary>
        IIntentAxisBuilder AddAxis(string browseName, uint index, AxisKindEnum kind);

        /// <summary>
        /// Adds an output signal under Outputs.
        /// </summary>
        IIntentOutputSignalBuilder AddOutput(
            string browseName,
            NodeId dataType,
            Variant value = default);

        /// <summary>
        /// Adds a controller program under Programs.
        /// </summary>
        IIntentProgramBuilder AddProgram(string browseName, string programId);

        /// <summary>
        /// Adds the SafetyState object and binds an optional safety source.
        /// </summary>
        IIntentControllerBuilder WithSafetyState(IRobotIntentSafetySource? source = null);

        /// <summary>
        /// Adds and configures the Description object.
        /// </summary>
        IIntentDescriptionBuilder WithDescription(
            Action<IIntentDescriptionBuilder>? configure = null);

        /// <summary>
        /// Adds a real-time channel under RealTimeChannels.
        /// </summary>
        IIntentRealTimeChannelBuilder AddRealTimeChannel(
            string browseName,
            string channelId,
            RealTimeTransportEnum transport,
            string endpointUrl);

        /// <summary>
        /// Declares an accepted intent DataType.
        /// </summary>
        /// <typeparam name="TIntent">
        /// The accepted intent structure type.
        /// </typeparam>
        IIntentControllerBuilder Accepts<TIntent>(
            bool cancelSupported = true,
            bool pauseSupported = true,
            bool retrySupported = false,
            ArrayOf<BufferModeEnum> supportedBufferModes = default,
            ArrayOf<BlockingModeEnum> supportedBlockingModes = default)
            where TIntent : IntentDataType, new();

        /// <summary>
        /// Computes the conformance facets satisfied by this controller.
        /// </summary>
        ArrayOf<string> ComputeFacets();
    }

    /// <summary>
    /// Builds a CoordinateFrameType instance.
    /// </summary>
    public interface IIntentFrameBuilder : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.CoordinateFrameState>
    {
        /// <summary>
        /// Links this frame to its parent through HasFrameParent.
        /// </summary>
        IIntentFrameBuilder WithParent(IIntentFrameBuilder parent);
    }

    /// <summary>
    /// Builds a ToolType instance.
    /// </summary>
    public interface IIntentToolBuilder : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.ToolState>
    {
        /// <summary>
        /// Sets the fitted flag, enforcing the one-fitted-tool invariant.
        /// </summary>
        IIntentToolBuilder WithFitted(bool fitted = true);
    }

    /// <summary>
    /// Builds a LocationType instance.
    /// </summary>
    public interface IIntentLocationBuilder : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.LocationState>
    {
        /// <summary>
        /// Sets the occupancy report.
        /// </summary>
        IIntentLocationBuilder WithOccupancy(bool occupied, uint capacity = 1);
    }

    /// <summary>
    /// Builds an AxisType instance.
    /// </summary>
    public interface IIntentAxisBuilder : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.AxisState>;

    /// <summary>
    /// Builds an OutputSignalType instance.
    /// </summary>
    public interface IIntentOutputSignalBuilder : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.OutputSignalState>;

    /// <summary>
    /// Builds a ProgramType instance.
    /// </summary>
    public interface IIntentProgramBuilder : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.ProgramState>;

    /// <summary>
    /// Builds a SafetyStateType instance.
    /// </summary>
    public interface IIntentSafetyStateBuilder : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.SafetyStateState>;

    /// <summary>
    /// Builds a RobotDescriptionType instance.
    /// </summary>
    public interface IIntentDescriptionBuilder : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.RobotDescriptionState>
    {
        /// <summary>
        /// Sets the kinematic chain from base outwards.
        /// </summary>
        IIntentDescriptionBuilder WithKinematicChain(ArrayOf<KinematicJointDataType> chain);

        /// <summary>
        /// Sets the robot limits.
        /// </summary>
        IIntentDescriptionBuilder WithLimits(
            double reachRadius,
            double payloadLimit,
            double maxCartesianSpeed,
            double maxCartesianAcceleration);
    }

    /// <summary>
    /// Builds a RealTimeChannelType instance.
    /// </summary>
    public interface IIntentRealTimeChannelBuilder
        : IIntentNodeBuilder<global::Opc.Ua.RobotIntent.RealTimeChannelState>;
}
