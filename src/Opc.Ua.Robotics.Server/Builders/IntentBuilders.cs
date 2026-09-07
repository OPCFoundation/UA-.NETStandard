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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;

namespace Opc.Ua.Robotics.Server.Builders
{
    internal sealed class IntentControllerBuilder : IIntentControllerBuilder
    {
        // Explicit calls select RobotIntent generated extensions over same-named Robotics extensions.
        // TODO: Remove when RCS1196 recognizes deliberate extension-method disambiguation.
#pragma warning disable RCS1196
        public IntentControllerBuilder(IRobotIntentBuildContext context, string browseName)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_logger = context.Context.Telemetry.CreateLogger<IntentControllerBuilder>();
            QualifiedName normalized = Normalize(browseName);
            State = global::Opc.Ua.RobotIntent.OpcUaRobotIntentExtensions
                .CreateInstanceOfIntentControllerType(
                    context.Context,
                    context.Root.Controllers!,
                    normalized);
            State.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            InitializeController();
        }

        public global::Opc.Ua.RobotIntent.IntentControllerState State { get; }

        public IntentControllerHost Host => m_host ??
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidState,
                "IntentController '{0}' has not been registered yet.",
                State.BrowseName);

        internal ISystemContext Context => m_context.Context;

        public IIntentControllerBuilder WithOperationalMode(OperationalModeEnum mode)
        {
            EnsureMutable();
            SetValue(State.OperationalMode!, mode);
            m_hostOptions.OperationalMode = mode;
            return this;
        }

        public IIntentControllerBuilder WithReady(bool ready)
        {
            EnsureMutable();
            SetValue(State.Ready!, ready);
            return this;
        }

        public IIntentControllerBuilder WithMaxQueueDepth(uint maxQueueDepth)
        {
            EnsureMutable();
            SetValue(State.MaxQueueDepth!, maxQueueDepth);
            m_hostOptions.MaxQueueDepth = maxQueueDepth;
            return this;
        }

        public IIntentControllerBuilder WithExecutor(IIntentExecutor executor)
        {
            EnsureMutable();
            m_executor = executor ?? throw new ArgumentNullException(nameof(executor));
            return this;
        }

        public IIntentFrameBuilder AddFrame(
            string browseName,
            string frameId,
            FrameRoleEnum role,
            Pose3DDataType transform,
            Action<IIntentFrameBuilder>? configure = null)
        {
            EnsureMutable();
            if (string.IsNullOrWhiteSpace(frameId))
            {
                throw new ArgumentException("A non-empty frame identifier is required.", nameof(frameId));
            }
            global::Opc.Ua.RobotIntent.CoordinateFrameState state = AddContained(State.Frames!, browseName,
                (parent, name) => global::Opc.Ua.RobotIntent.OpcUaRobotIntentExtensions
                    .CreateInstanceOfCoordinateFrameType(m_context.Context, parent, name));
            state.CreateOrReplaceFrameId(m_context.Context, null);
            state.CreateOrReplaceRole(m_context.Context, null);
            state.CreateOrReplaceTransform(m_context.Context, null);
            SetValue(state.FrameId!, frameId);
            SetValue(state.Role!, role);
            SetValue(state.Transform!, transform ?? throw new ArgumentNullException(nameof(transform)));
            var builder = new IntentFrameBuilder(this, state);
            m_frames.Add(builder);
            configure?.Invoke(builder);
            return builder;
        }

        public IIntentToolBuilder AddTool(
            string browseName,
            IIntentFrameBuilder tcpFrame,
            bool fitted = false,
            Action<IIntentToolBuilder>? configure = null)
        {
            EnsureMutable();
            IntentFrameBuilder frame = RequireFrame(tcpFrame, nameof(tcpFrame));
            if (frame.State.Role!.Value != FrameRoleEnum.Tool)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Tool '{0}' requires a TCP frame with Role=Tool.",
                    browseName);
            }
            global::Opc.Ua.RobotIntent.ToolState state = AddContained(State.Tools!, browseName,
                (parent, name) => global::Opc.Ua.RobotIntent.OpcUaRobotIntentExtensions
                    .CreateInstanceOfToolType(m_context.Context, parent, name));
            state.CreateOrReplaceToolId(m_context.Context, null);
            state.CreateOrReplaceName(m_context.Context, null);
            state.CreateOrReplaceTcpFrame(m_context.Context, null);
            state.CreateOrReplaceFitted(m_context.Context, null);
            SetValue(state.ToolId!, browseName);
            SetValue(state.Name!, LocalizedText.From(browseName));
            SetValue(state.TcpFrame!, frame.State.NodeId);
            var builder = new IntentToolBuilder(this, state);
            m_tools.Add(builder);
            builder.WithFitted(fitted);
            configure?.Invoke(builder);
            return builder;
        }

        public IIntentLocationBuilder AddLocation(
            string browseName,
            Pose3DDataType pose,
            Action<IIntentLocationBuilder>? configure = null)
        {
            EnsureMutable();
            global::Opc.Ua.RobotIntent.LocationState state = AddContained(State.Locations!, browseName,
                (parent, name) => global::Opc.Ua.RobotIntent.OpcUaRobotIntentExtensions
                    .CreateInstanceOfLocationType(m_context.Context, parent, name));
            state.CreateOrReplaceLocationId(m_context.Context, null);
            state.CreateOrReplaceName(m_context.Context, null);
            state.CreateOrReplacePose(m_context.Context, null);
            state.CreateOrReplaceOccupied(m_context.Context, null);
            state.CreateOrReplaceCapacity(m_context.Context, null);
            SetValue(state.LocationId!, browseName);
            SetValue(state.Name!, LocalizedText.From(browseName));
            SetValue(state.Pose!, pose ?? throw new ArgumentNullException(nameof(pose)));
            var builder = new IntentLocationBuilder(state);
            m_locations.Add(builder);
            configure?.Invoke(builder);
            return builder;
        }

        public IIntentAxisBuilder AddAxis(string browseName, uint index, AxisKindEnum kind)
        {
            EnsureMutable();
            global::Opc.Ua.RobotIntent.AxisState state = AddContained(State.Axes!, browseName,
                (parent, name) => global::Opc.Ua.RobotIntent.OpcUaRobotIntentExtensions
                    .CreateInstanceOfAxisType(m_context.Context, parent, name));
            state.CreateOrReplaceAxisId(m_context.Context, null);
            state.CreateOrReplaceIndex(m_context.Context, null);
            state.CreateOrReplaceKind(m_context.Context, null);
            state.CreateOrReplacePosition(m_context.Context, null);
            SetValue(state.AxisId!, browseName);
            SetValue(state.Index!, index);
            SetValue(state.Kind!, kind);
            var builder = new IntentAxisBuilder(state);
            m_axes.Add(builder);
            return builder;
        }

        public IIntentOutputSignalBuilder AddOutput(
            string browseName,
            NodeId dataType,
            Variant value = default)
        {
            EnsureMutable();
            if (dataType.IsNull)
            {
                throw new ArgumentException("A non-null DataType NodeId is required.", nameof(dataType));
            }
            State.AddOutputs(m_context.Context);
            global::Opc.Ua.RobotIntent.OutputSignalState state = AddContained(State.Outputs!, browseName,
                (parent, name) => global::Opc.Ua.RobotIntent.OpcUaRobotIntentExtensions
                    .CreateInstanceOfOutputSignalType(m_context.Context, parent, name));
            state.CreateOrReplaceSignalId(m_context.Context, null);
            state.CreateOrReplaceName(m_context.Context, null);
            state.CreateOrReplaceValue(m_context.Context, null);
            state.CreateOrReplaceWritable(m_context.Context, null);
            SetValue(state.SignalId!, browseName);
            SetValue(state.Name!, LocalizedText.From(browseName));
            state.Value!.DataType = dataType;
            state.Value.Value = value;
            SetValue(state.Writable!, true);
            var builder = new IntentOutputSignalBuilder(state);
            m_outputs.Add(builder);
            return builder;
        }

        public IIntentProgramBuilder AddProgram(string browseName, string programId)
        {
            EnsureMutable();
            if (string.IsNullOrWhiteSpace(programId))
            {
                throw new ArgumentException("A non-empty program identifier is required.", nameof(programId));
            }
            State.AddPrograms(m_context.Context);
            global::Opc.Ua.RobotIntent.ProgramState state = AddContained(State.Programs!, browseName,
                (parent, name) => global::Opc.Ua.RobotIntent.OpcUaRobotIntentExtensions
                    .CreateInstanceOfProgramType(m_context.Context, parent, name));
            state.CreateOrReplaceProgramId(m_context.Context, null);
            state.CreateOrReplaceName(m_context.Context, null);
            SetValue(state.ProgramId!, programId);
            SetValue(state.Name!, LocalizedText.From(browseName));
            var builder = new IntentProgramBuilder(state);
            m_programs.Add(builder);
            return builder;
        }

        public IIntentControllerBuilder WithSafetyState(IRobotIntentSafetySource? source = null)
        {
            EnsureMutable();
            m_safetySource = source;
            State.CreateOrReplaceSafetyState(m_context.Context, null);
            InitializeSafety(State.SafetyState!);
            return this;
        }

        public IIntentDescriptionBuilder WithDescription(Action<IIntentDescriptionBuilder>? configure = null)
        {
            EnsureMutable();
            State.AddDescription(m_context.Context);
            State.Description!.CreateOrReplaceKinematicChain(m_context.Context, null);
            State.Description.CreateOrReplaceMountingPose(m_context.Context, null);
            State.Description.CreateOrReplaceReachRadius(m_context.Context, null);
            State.Description.CreateOrReplacePayloadLimit(m_context.Context, null);
            State.Description.CreateOrReplaceMaxCartesianSpeed(m_context.Context, null);
            State.Description.CreateOrReplaceMaxCartesianAcceleration(m_context.Context, null);
            m_description ??= new IntentDescriptionBuilder(State.Description!);
            configure?.Invoke(m_description);
            return m_description;
        }

        public IIntentRealTimeChannelBuilder AddRealTimeChannel(
            string browseName,
            string channelId,
            RealTimeTransportEnum transport,
            string endpointUrl)
        {
            EnsureMutable();
            if (string.IsNullOrWhiteSpace(channelId))
            {
                throw new ArgumentException("A non-empty channel identifier is required.", nameof(channelId));
            }
            State.AddRealTimeChannels(m_context.Context);
            global::Opc.Ua.RobotIntent.RealTimeChannelState state = AddContained(State.RealTimeChannels!, browseName,
                (parent, name) => global::Opc.Ua.RobotIntent.OpcUaRobotIntentExtensions
                    .CreateInstanceOfRealTimeChannelType(m_context.Context, parent, name));
            state.CreateOrReplaceChannelId(m_context.Context, null);
            state.CreateOrReplaceTransport(m_context.Context, null);
            state.CreateOrReplaceEndpointUrl(m_context.Context, null);
            state.CreateOrReplaceInitiator(m_context.Context, null);
            state.CreateOrReplaceNominalRate(m_context.Context, null);
            state.CreateOrReplacePayloadDescriptor(m_context.Context, null);
            state.CreateOrReplaceRequiredMode(m_context.Context, null);
            state.CreateOrReplaceAvailable(m_context.Context, null);
            state.CreateOrReplaceLeaseHolder(m_context.Context, null);
            state.CreateOrReplaceLeaseExpiry(m_context.Context, null);
            SetValue(state.ChannelId!, channelId);
            SetValue(state.Transport!, transport);
            SetValue(state.EndpointUrl!, endpointUrl ?? string.Empty);
            SetValue(state.Initiator!, ChannelInitiatorEnum.Client);
            SetValue(state.NominalRate!, 0.0);
            SetValue(state.PayloadDescriptor!, string.Empty);
            SetValue(state.RequiredMode!, OperationalModeEnum.AutomaticExternal);
            SetValue(state.Available!, true);
            SetValue(state.LeaseHolder!, NodeId.Null);
            SetValue(state.LeaseExpiry!, DateTimeUtc.MinValue);
            SetValue(State.Capabilities!.RealTimeChannelsSupported!, true);
            m_hostOptions.RealTimeChannelsSupported = true;
            m_hostOptions.Channels.Add(new DeclaredChannel
            {
                ChannelId = channelId,
                Transport = transport,
                EndpointUrl = endpointUrl ?? string.Empty,
                Initiator = ChannelInitiatorEnum.Client,
                NominalRate = 0.0,
                PayloadDescriptor = string.Empty,
                RequiredMode = OperationalModeEnum.AutomaticExternal
            });
            var builder = new IntentRealTimeChannelBuilder(state);
            m_realTimeChannels.Add(builder);
            return builder;
        }
#pragma warning restore RCS1196

        public IIntentControllerBuilder Accepts<TIntent>(
            bool cancelSupported = true,
            bool pauseSupported = false,
            bool retrySupported = false,
            ArrayOf<BufferModeEnum> supportedBufferModes = default,
            ArrayOf<BlockingModeEnum> supportedBlockingModes = default)
            where TIntent : IntentDataType, new()
        {
            EnsureMutable();
            ArrayOf<BufferModeEnum> buffers = supportedBufferModes.IsNull || supportedBufferModes.IsEmpty
                ? new[] { BufferModeEnum.Aborting, BufferModeEnum.Buffered }.ToArrayOf()
                : supportedBufferModes;
            if (!buffers.Contains(BufferModeEnum.Aborting))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Every Robot Intent capability must include BufferMode Aborting.");
            }
            ArrayOf<BlockingModeEnum> blocking = supportedBlockingModes.IsNull || supportedBlockingModes.IsEmpty
                ? new[] { BlockingModeEnum.None, BlockingModeEnum.Soft, BlockingModeEnum.Single, BlockingModeEnum.Hard }
                    .ToArrayOf()
                : supportedBlockingModes;
            ExpandedNodeId expandedIntentType = new TIntent().TypeId;
            NodeId intentType = GetIntentDataType(expandedIntentType, typeof(TIntent), m_context.Context);
            IntentCapabilityDataType? existingCapability = m_capabilities.FirstOrDefault(
                capability => capability.IntentType == intentType);
            if (existingCapability != null)
            {
                if (existingCapability.CancelSupported != cancelSupported ||
                    existingCapability.PauseSupported != pauseSupported ||
                    existingCapability.RetrySupported != retrySupported ||
                    !HasSameValues(existingCapability.SupportedBufferModes, buffers) ||
                    !HasSameValues(existingCapability.SupportedBlockingModes, blocking))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "Intent capability for '{0}' is already declared with different options.",
                        typeof(TIntent).Name);
                }
                return this;
            }
            m_capabilities.Add(new IntentCapabilityDataType
            {
                IntentType = intentType,
                Description = LocalizedText.From(typeof(TIntent).Name),
                CancelSupported = cancelSupported,
                PauseSupported = pauseSupported,
                RetrySupported = retrySupported,
                SupportedBufferModes = buffers,
                SupportedBlockingModes = blocking
            });
            m_hostOptions.Capabilities.Add(new DeclaredCapability
            {
                IntentType = expandedIntentType,
                Description = typeof(TIntent).Name,
                CancelSupported = cancelSupported,
                PauseSupported = pauseSupported,
                RetrySupported = retrySupported,
                SupportedBufferModes = buffers,
                SupportedBlockingModes = blocking
            });
            if (typeof(TIntent) == typeof(TrajectoryIntentDataType) ||
                typeof(TIntent) == typeof(CartesianPathIntentDataType))
            {
                SetValue(State.Capabilities!.TrajectorySupported!, true);
                m_hostOptions.TrajectorySupported = true;
            }
            if (typeof(TIntent) == typeof(ForceIntentDataType))
            {
                SetValue(State.Capabilities!.ForceControlSupported!, true);
                m_hostOptions.ForceControlSupported = true;
            }
            if (pauseSupported)
            {
                State.AddPause(m_context.Context)
                    .AddResume(m_context.Context);
                MarkCommandMethod(State.Pause!);
                MarkCommandMethod(State.Resume!);
            }
            if (retrySupported)
            {
                State.AddRetry(m_context.Context);
                MarkCommandMethod(State.Retry!);
            }
            return this;
        }

        private static bool HasSameValues<T>(ArrayOf<T> left, ArrayOf<T> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }
            var remaining = right.ToList();
            foreach (T item in left)
            {
                int index = remaining.FindIndex(candidate => EqualityComparer<T>.Default.Equals(candidate, item));
                if (index < 0)
                {
                    return false;
                }
                remaining.RemoveAt(index);
            }
            return true;
        }

        public ArrayOf<string> ComputeFacets()
        {
            return RobotIntentFacetCalculator.Compute(State);
        }

        internal async ValueTask RegisterAsync(CancellationToken cancellationToken)
        {
            EnsureMutable();
            Validate();
            State.Capabilities!.SupportedIntents!.Value = m_capabilities.ToArrayOf();
            SetValue(State.Capabilities.AxisCount!, (uint)m_axes.Count);
            SynchronizeHostOptions();
            EnsureOptionalMethods();
            EnsureMethodArguments();
            WireNotStartedMethodGuards();
            EnsureControllerBrowseNameIsUnique();
            PublishSupportedFacets();
            m_context.Root.Controllers!.AddChild(State);
            await m_context.Manager.AddPredefinedNodeAsync(State, cancellationToken).ConfigureAwait(false);
            m_host = CreateHost();
            if (m_safetySource != null)
            {
                BindSafetySource(m_safetySource);
                await ReadAndPushSafetyAsync(m_safetySource, cancellationToken).ConfigureAwait(false);
            }
            if (m_context.Manager is RobotIntentNodeManager robotIntentNodeManager)
            {
                robotIntentNodeManager.RegisterIntentControllerHost(m_host);
            }
            m_registered = true;
        }

        internal void SetToolFitted(IntentToolBuilder tool, bool fitted)
        {
            if (fitted)
            {
                for (int ii = 0; ii < m_tools.Count; ii++)
                {
                    if (!ReferenceEquals(m_tools[ii], tool) && m_tools[ii].State.Fitted!.Value)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                            "At most one ToolType under an IntentController may have Fitted=true.");
                    }
                }
            }
            SetValue(tool.State.Fitted!, fitted);
        }

        private IntentControllerHost CreateHost()
        {
            IIntentExecutor? executor = m_executor;
            if (executor == null &&
                m_context is RobotIntentBuildContext buildContext &&
                buildContext.TryGetIntentExecutor(State, out IIntentExecutor? controllerExecutor) &&
                controllerExecutor != null)
            {
                executor = controllerExecutor;
            }
            if (executor == null &&
                m_context is RobotIntentBuildContext buildContextWithServices &&
                buildContextWithServices.TryGetService(out IIntentExecutor? registeredExecutor) &&
                registeredExecutor != null)
            {
                executor = registeredExecutor;
            }
            if (executor == null)
            {
                throw new InvalidOperationException(
                    global::Opc.Ua.Robotics.Server.RobotIntentBuildServiceProvider.MissingExecutorMessage);
            }
            var host = new IntentControllerHost(
                State,
                executor,
                m_context.Manager.AddPredefinedNodeAsync,
                m_hostOptions,
                RemovePredefinedNodeAsync);

            // Command authority is held per Session, so it has to be given back when the
            // Session goes away. Without this subscription a client that crashes or is
            // killed keeps the robot locked, and the next client is refused with no way
            // to recover short of restarting the Server.
            if (m_context.Manager.Server?.SessionManager is { } sessionManager)
            {
                host.AttachSessionManager(m_context.Context, sessionManager);
            }
            return host;
        }

        private async ValueTask RemovePredefinedNodeAsync(NodeState node, CancellationToken cancellationToken)
        {
            if (node.NodeId.IsNull || m_context.Context is not ServerSystemContext serverContext)
            {
                return;
            }
            await m_context.Manager.DeleteNodeAsync(
                serverContext,
                node.NodeId,
                cancellationToken).ConfigureAwait(false);
        }

        private void SynchronizeHostOptions()
        {
            global::Opc.Ua.RobotIntent.IntentCapabilitiesState capabilities = State.Capabilities!;
            m_hostOptions.MissionsSupported = capabilities.MissionsSupported!.Value;
            m_hostOptions.MissionHorizonSupported =
                capabilities.MissionsSupported.Value && capabilities.MissionHorizonSupported!.Value;
            m_hostOptions.MissionBranchingSupported =
                capabilities.MissionsSupported.Value && capabilities.MissionBranchingSupported!.Value;
            m_hostOptions.BlendingSupported = capabilities.BlendingSupported!.Value;
            m_hostOptions.TrajectorySupported = capabilities.TrajectorySupported!.Value;
            m_hostOptions.ForceControlSupported = capabilities.ForceControlSupported!.Value;
            m_hostOptions.RealTimeChannelsSupported = capabilities.RealTimeChannelsSupported!.Value;
            m_hostOptions.MaxTrajectoryPoints = capabilities.MaxTrajectoryPoints!.Value;
            m_hostOptions.AxisCount = (uint)m_axes.Count;
            m_hostOptions.Channels.Clear();
            foreach (IntentRealTimeChannelBuilder channel in m_realTimeChannels)
            {
                m_hostOptions.Channels.Add(new DeclaredChannel
                {
                    ChannelId = channel.State.ChannelId!.Value,
                    Transport = channel.State.Transport!.Value,
                    EndpointUrl = channel.State.EndpointUrl!.Value ?? string.Empty,
                    Initiator = channel.State.Initiator!.Value,
                    NominalRate = channel.State.NominalRate!.Value,
                    PayloadDescriptor = channel.State.PayloadDescriptor!.Value ?? string.Empty,
                    RequiredMode = channel.State.RequiredMode!.Value
                });
            }
        }

        private void EnsureOptionalMethods()
        {
            if (m_hostOptions.MissionsSupported)
            {
                State.AddSubmitMission(m_context.Context)
                    .AddCancelMission(m_context.Context);
                MarkCommandMethod(State.SubmitMission!);
                MarkCommandMethod(State.CancelMission!);
            }
            if (m_hostOptions.MissionsSupported && m_hostOptions.MissionHorizonSupported)
            {
                State.AddUpdateMission(m_context.Context);
                MarkCommandMethod(State.UpdateMission!);
            }
            if (m_hostOptions.RealTimeChannelsSupported)
            {
                State.AddOpenRealTimeChannel(m_context.Context)
                    .AddCloseRealTimeChannel(m_context.Context);
                EnsureOpenRealTimeChannelArguments(State.OpenRealTimeChannel!);
                EnsureCloseRealTimeChannelArguments(State.CloseRealTimeChannel!);
                MarkCommandMethod(State.OpenRealTimeChannel!);
                MarkCommandMethod(State.CloseRealTimeChannel!);
            }
            if (m_capabilities.Any(static capability => capability.PauseSupported))
            {
                State.AddPause(m_context.Context)
                    .AddResume(m_context.Context);
                MarkCommandMethod(State.Pause!);
                MarkCommandMethod(State.Resume!);
            }
            if (m_capabilities.Any(static capability => capability.RetrySupported))
            {
                State.AddRetry(m_context.Context);
                MarkCommandMethod(State.Retry!);
            }
        }

        private void WireNotStartedMethodGuards()
        {
            State.RequestControl!.OnCallAsync = (ctx, method, objectId, ct) =>
                new ValueTask<RequestControlMethodStateResult>(new RequestControlMethodStateResult
                {
                    ServiceResult = HostNotStartedResult("RequestControl"),
                    Granted = false,
                    CurrentOwner = NodeId.Null
                });
            State.ReleaseControl!.OnCallMethod2Async = (ctx, method, objectId, inputArguments, outputArguments, ct) =>
                new ValueTask<ServiceResult>(HostNotStartedResult("ReleaseControl"));
            State.SubmitIntent!.OnCallAsync = (ctx, method, objectId, intent, ct) =>
                new ValueTask<SubmitIntentMethodStateResult>(new SubmitIntentMethodStateResult
                {
                    ServiceResult = HostNotStartedResult("SubmitIntent")
                });
            State.CancelIntent!.OnCallAsync = (ctx, method, objectId, intentId, stopMode, ct) =>
                new ValueTask<CancelIntentMethodStateResult>(new CancelIntentMethodStateResult
                {
                    ServiceResult = HostNotStartedResult("CancelIntent")
                });
            State.CancelAll!.OnCallAsync = (ctx, method, objectId, stopMode, ct) =>
                new ValueTask<CancelAllMethodStateResult>(new CancelAllMethodStateResult
                {
                    ServiceResult = HostNotStartedResult("CancelAll")
                });
            if (State.Pause != null)
            {
                State.Pause.OnCallAsync = (ctx, method, objectId, ct) =>
                    new ValueTask<PauseMethodStateResult>(new PauseMethodStateResult
                    {
                        ServiceResult = HostNotStartedResult("Pause")
                    });
            }
            if (State.Resume != null)
            {
                State.Resume.OnCallAsync = (ctx, method, objectId, ct) =>
                    new ValueTask<ResumeMethodStateResult>(new ResumeMethodStateResult
                    {
                        ServiceResult = HostNotStartedResult("Resume")
                    });
            }
            if (State.Retry != null)
            {
                State.Retry.OnCallAsync = (ctx, method, objectId, intentId, ct) =>
                    new ValueTask<RetryMethodStateResult>(new RetryMethodStateResult
                    {
                        ServiceResult = HostNotStartedResult("Retry")
                    });
            }
            if (State.SubmitMission != null)
            {
                State.SubmitMission.OnCallAsync = (ctx, method, objectId, mission, ct) =>
                    new ValueTask<SubmitMissionMethodStateResult>(new SubmitMissionMethodStateResult
                    {
                        ServiceResult = HostNotStartedResult("SubmitMission")
                    });
            }
            if (State.UpdateMission != null)
            {
                State.UpdateMission.OnCallAsync = (ctx, method, objectId, missionId, updateId, steps, ct) =>
                    new ValueTask<UpdateMissionMethodStateResult>(new UpdateMissionMethodStateResult
                    {
                        ServiceResult = HostNotStartedResult("UpdateMission")
                    });
            }
            if (State.CancelMission != null)
            {
                State.CancelMission.OnCallAsync = (ctx, method, objectId, missionId, stopMode, ct) =>
                    new ValueTask<CancelMissionMethodStateResult>(new CancelMissionMethodStateResult
                    {
                        ServiceResult = HostNotStartedResult("CancelMission")
                    });
            }
            if (State.OpenRealTimeChannel != null)
            {
                State.OpenRealTimeChannel.OnCallAsync = (ctx, method, objectId, channelId, requestedLease, ct) =>
                    new ValueTask<OpenRealTimeChannelMethodStateResult>(new OpenRealTimeChannelMethodStateResult
                    {
                        ServiceResult = HostNotStartedResult("OpenRealTimeChannel")
                    });
            }
            if (State.CloseRealTimeChannel != null)
            {
                State.CloseRealTimeChannel.OnCallAsync = (ctx, method, objectId, channelId, ct) =>
                    new ValueTask<CloseRealTimeChannelMethodStateResult>(new CloseRealTimeChannelMethodStateResult
                    {
                        ServiceResult = HostNotStartedResult("CloseRealTimeChannel")
                    });
            }
        }

        private ServiceResult HostNotStartedResult(string methodName)
        {
            m_logger.HostNotStarted(methodName);
            return new ServiceResult(
                StatusCodes.BadInvalidState,
                new LocalizedText(
                    $"Robot Intent controller host for '{State.BrowseName}' is not started."));
        }

        private void EnsureMethodArguments()
        {
            EnsureRequestControlArguments(State.RequestControl!);
            EnsureReleaseControlArguments(State.ReleaseControl!);
            EnsureSubmitIntentArguments(State.SubmitIntent!);
            EnsureCancelIntentArguments(State.CancelIntent!);
            EnsureCancelAllArguments(State.CancelAll!);
            if (State.Pause != null)
            {
                EnsureAcceptedArguments(State.Pause);
            }
            if (State.Resume != null)
            {
                EnsureAcceptedArguments(State.Resume);
            }
            if (State.Retry != null)
            {
                EnsureRetryArguments(State.Retry);
            }
            if (State.SubmitMission != null)
            {
                EnsureSubmitMissionArguments(State.SubmitMission);
            }
            if (State.UpdateMission != null)
            {
                EnsureUpdateMissionArguments(State.UpdateMission);
            }
            if (State.CancelMission != null)
            {
                EnsureCancelMissionArguments(State.CancelMission);
            }
            if (State.OpenRealTimeChannel != null)
            {
                EnsureOpenRealTimeChannelArguments(State.OpenRealTimeChannel);
            }
            if (State.CloseRealTimeChannel != null)
            {
                EnsureCloseRealTimeChannelArguments(State.CloseRealTimeChannel);
            }
        }

        private void EnsureRequestControlArguments(RequestControlMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value = [];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Granted", global::Opc.Ua.DataTypeIds.Boolean),
                Argument("CurrentOwner", global::Opc.Ua.DataTypeIds.NodeId)
            ];
        }

        private void EnsureReleaseControlArguments(MethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value = [];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value = [];
        }

        private void EnsureSubmitIntentArguments(SubmitIntentMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value =
            [
                Argument("Intent", IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.IntentDataType))
            ];
            EnsureIntentAcceptedArguments(method);
        }

        private void EnsureCancelIntentArguments(CancelIntentMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value =
            [
                Argument("IntentId", global::Opc.Ua.DataTypeIds.String),
                Argument("StopMode", IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.StopModeEnum))
            ];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Accepted", global::Opc.Ua.DataTypeIds.Boolean)
            ];
        }

        private void EnsureCancelAllArguments(CancelAllMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value =
            [
                Argument("StopMode", IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.StopModeEnum))
            ];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Cancelled", global::Opc.Ua.DataTypeIds.UInt32)
            ];
        }

        private void EnsureAcceptedArguments(MethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value = [];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Accepted", global::Opc.Ua.DataTypeIds.Boolean)
            ];
        }

        private void EnsureRetryArguments(RetryMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value =
            [
                Argument("IntentId", global::Opc.Ua.DataTypeIds.String)
            ];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Accepted", global::Opc.Ua.DataTypeIds.Boolean),
                Argument("Operation", global::Opc.Ua.DataTypeIds.NodeId),
                Argument("Failure", IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.IntentFailureEnum)),
                Argument("Message", global::Opc.Ua.DataTypeIds.LocalizedText)
            ];
        }

        private void EnsureSubmitMissionArguments(SubmitMissionMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value =
            [
                Argument("Mission", IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.MissionDataType))
            ];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Accepted", global::Opc.Ua.DataTypeIds.Boolean),
                Argument("MissionId", global::Opc.Ua.DataTypeIds.String),
                Argument("Operation", global::Opc.Ua.DataTypeIds.NodeId),
                Argument("Failure", IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.IntentFailureEnum)),
                Argument("Message", global::Opc.Ua.DataTypeIds.LocalizedText)
            ];
        }

        private void EnsureUpdateMissionArguments(UpdateMissionMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value =
            [
                Argument("MissionId", global::Opc.Ua.DataTypeIds.String),
                Argument("MissionUpdateId", global::Opc.Ua.DataTypeIds.UInt32),
                Argument(
                    "Steps",
                    IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.MissionStepDataType),
                    ValueRanks.OneDimension)
            ];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Result", IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.MissionUpdateResultEnum)),
                Argument("Message", global::Opc.Ua.DataTypeIds.LocalizedText)
            ];
        }

        private void EnsureCancelMissionArguments(CancelMissionMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value =
            [
                Argument("MissionId", global::Opc.Ua.DataTypeIds.String),
                Argument("StopMode", IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.StopModeEnum))
            ];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Accepted", global::Opc.Ua.DataTypeIds.Boolean)
            ];
        }

        private void EnsureOpenRealTimeChannelArguments(OpenRealTimeChannelMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value =
            [
                Argument("ChannelId", global::Opc.Ua.DataTypeIds.String),
                Argument("RequestedLease", global::Opc.Ua.DataTypeIds.Duration)
            ];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Granted", global::Opc.Ua.DataTypeIds.Boolean),
                Argument("EndpointUrl", global::Opc.Ua.DataTypeIds.String),
                Argument("PayloadDescriptor", global::Opc.Ua.DataTypeIds.String),
                Argument("LeaseExpiry", global::Opc.Ua.DataTypeIds.UtcTime),
                Argument("Message", global::Opc.Ua.DataTypeIds.LocalizedText)
            ];
        }

        private void EnsureCloseRealTimeChannelArguments(CloseRealTimeChannelMethodState method)
        {
            method.CreateOrReplaceInputArguments(m_context.Context, null).Value =
            [
                Argument("ChannelId", global::Opc.Ua.DataTypeIds.String)
            ];
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Released", global::Opc.Ua.DataTypeIds.Boolean)
            ];
        }

        private void EnsureIntentAcceptedArguments(SubmitIntentMethodState method)
        {
            method.CreateOrReplaceOutputArguments(m_context.Context, null).Value =
            [
                Argument("Accepted", global::Opc.Ua.DataTypeIds.Boolean),
                Argument("IntentId", global::Opc.Ua.DataTypeIds.String),
                Argument("Operation", global::Opc.Ua.DataTypeIds.NodeId),
                Argument("Failure", IntentDataType(global::Opc.Ua.RobotIntent.DataTypeIds.IntentFailureEnum)),
                Argument("Message", global::Opc.Ua.DataTypeIds.LocalizedText)
            ];
        }

        private NodeId IntentDataType(ExpandedNodeId expanded)
        {
            return ExpandedNodeId.ToNodeId(expanded, m_context.Context.NamespaceUris);
        }

        private static Argument Argument(string name, NodeId dataType, int valueRank = ValueRanks.Scalar)
        {
            return new Argument
            {
                Name = name,
                DataType = dataType,
                ValueRank = valueRank
            };
        }

        private void InitializeController()
        {
            State.CreateOrReplaceOperationalMode(m_context.Context, null);
            State.CreateOrReplaceReady(m_context.Context, null);
            State.CreateOrReplaceControlOwner(m_context.Context, null);
            State.CreateOrReplaceActiveIntent(m_context.Context, null);
            State.CreateOrReplaceActiveMission(m_context.Context, null);
            State.CreateOrReplaceCapabilities(m_context.Context, null);
            State.CreateOrReplaceFrames(m_context.Context, null);
            State.CreateOrReplaceTools(m_context.Context, null);
            State.CreateOrReplaceLocations(m_context.Context, null);
            State.CreateOrReplaceAxes(m_context.Context, null);
            State.CreateOrReplaceIntents(m_context.Context, null);
            State.CreateOrReplaceRequestControl(m_context.Context, null);
            State.CreateOrReplaceSubmitIntent(m_context.Context, null);
            State.CreateOrReplaceCancelIntent(m_context.Context, null);
            State.CreateOrReplaceCancelAll(m_context.Context, null);
            State.CreateOrReplaceSafetyState(m_context.Context, null);
            InitializeCapabilities(State.Capabilities!);
            InitializeSafety(State.SafetyState!);
            SetValue(State.OperationalMode!, OperationalModeEnum.AutomaticExternal);
            SetValue(State.Ready!, true);
            SetValue(State.MaxQueueDepth!, m_hostOptions.MaxQueueDepth);
            SetValue(State.ControlOwner!, NodeId.Null);
            SetValue(State.ActiveIntent!, NodeId.Null);
            SetValue(State.ActiveMission!, NodeId.Null);
            MarkReadOnly(State.OperationalMode!);
            MarkReadOnly(State.Ready!);
            MarkReadOnly(State.ControlOwner!);
            MarkReadOnly(State.MaxQueueDepth!);
            MarkReadOnly(State.ActiveIntent!);
            MarkReadOnly(State.ActiveMission!);
            MarkCommandMethod(State.RequestControl!);
            MarkCommandMethod(State.ReleaseControl!);
            MarkCommandMethod(State.SubmitIntent!);
            MarkCommandMethod(State.CancelIntent!);
            MarkCommandMethod(State.CancelAll!);
        }

        private void InitializeCapabilities(global::Opc.Ua.RobotIntent.IntentCapabilitiesState state)
        {
            state.CreateOrReplaceSupportedIntents(m_context.Context, null);
            state.CreateOrReplaceAxisCount(m_context.Context, null);
            state.CreateOrReplaceMissionsSupported(m_context.Context, null);
            state.CreateOrReplaceMissionHorizonSupported(m_context.Context, null);
            state.CreateOrReplaceMissionBranchingSupported(m_context.Context, null);
            state.CreateOrReplaceBlendingSupported(m_context.Context, null);
            state.CreateOrReplaceTrajectorySupported(m_context.Context, null);
            state.CreateOrReplaceForceControlSupported(m_context.Context, null);
            state.CreateOrReplaceRealTimeChannelsSupported(m_context.Context, null);
            state.CreateOrReplaceMaxTrajectoryPoints(m_context.Context, null);
            SetValue(state.SupportedIntents!, ArrayOf<IntentCapabilityDataType>.Empty);
            SetValue(state.AxisCount!, 0u);
            SetValue(state.MissionsSupported!, m_hostOptions.MissionsSupported);
            SetValue(state.MissionHorizonSupported!, m_hostOptions.MissionHorizonSupported);
            SetValue(state.MissionBranchingSupported!, m_hostOptions.MissionBranchingSupported);
            SetValue(state.BlendingSupported!, false);
            SetValue(state.TrajectorySupported!, false);
            SetValue(state.ForceControlSupported!, false);
            SetValue(state.RealTimeChannelsSupported!, false);
            SetValue(state.MaxTrajectoryPoints!, 0u);
        }

        private void InitializeSafety(global::Opc.Ua.RobotIntent.SafetyStateState state)
        {
            state.CreateOrReplaceActiveFunction(m_context.Context, null);
            state.CreateOrReplaceEmergencyStopActive(m_context.Context, null);
            state.CreateOrReplaceProtectiveStopActive(m_context.Context, null);
            state.CreateOrReplaceSafeSpeedLimitActive(m_context.Context, null);
            state.CreateOrReplaceSafeSpeedLimit(m_context.Context, null);
            state.CreateOrReplaceSafetyControllerOk(m_context.Context, null);
            state.CreateOrReplaceLastStopReason(m_context.Context, null);
            SetValue(state.ActiveFunction!, SafeMotionFunctionEnum.None);
            SetValue(state.EmergencyStopActive!, false);
            SetValue(state.ProtectiveStopActive!, false);
            SetValue(state.SafeSpeedLimitActive!, false);
            SetValue(state.SafeSpeedLimit!, 0.0);
            SetValue(state.SafetyControllerOk!, true);
            SetValue(state.LastStopReason!, LocalizedText.Null);
            MarkReadOnly(state.ActiveFunction!);
            MarkReadOnly(state.EmergencyStopActive!);
            MarkReadOnly(state.ProtectiveStopActive!);
            MarkReadOnly(state.SafeSpeedLimitActive!);
            MarkReadOnly(state.SafeSpeedLimit!);
            MarkReadOnly(state.SafetyControllerOk!);
            MarkReadOnly(state.LastStopReason!);
        }

        private void Validate()
        {
            var errors = new List<string>();
            var indices = new HashSet<uint>();
            for (int ii = 0; ii < m_axes.Count; ii++)
            {
                uint index = m_axes[ii].State.Index!.Value;
                if (!indices.Add(index))
                {
                    errors.Add($"Axis index {index} is declared more than once.");
                }
            }
            for (uint ii = 0; ii < m_axes.Count; ii++)
            {
                if (!indices.Contains(ii))
                {
                    errors.Add($"Axis indices must be contiguous from 0 to AxisCount-1; missing {ii}.");
                }
            }
            if (m_capabilities.Count == 0)
            {
                errors.Add("At least one supported intent capability is required.");
            }
            if (errors.Count > 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "IntentController '{0}' is invalid: {1}",
                    State.BrowseName,
                    string.Join(" ", errors));
            }
        }

        private void PublishSupportedFacets()
        {
            State.Capabilities!.CreateOrReplaceSupportedFacets(m_context.Context, null);
            MarkReadOnly(State.Capabilities.SupportedFacets!);
            BindRead(
                State.Capabilities.SupportedFacets!,
                _ => new ValueTask<DataValue>(ToDataValue(RobotIntentFacetCalculator.Compute(State))));
        }

        private void EnsureControllerBrowseNameIsUnique()
        {
            var children = new List<BaseInstanceState>();
            m_context.Root.Controllers!.GetChildren(m_context.Context, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii].BrowseName == State.BrowseName)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated,
                        "An IntentController with BrowseName '{0}' already exists.",
                        State.BrowseName);
                }
            }
        }

        private void BindSafetySource(IRobotIntentSafetySource source)
        {
            global::Opc.Ua.RobotIntent.SafetyStateState safety = State.SafetyState!;
            IntentControllerFacetMetadata.MarkSafetyAdmissionGated(State);
            m_hostOptions.SafetyStatusReader = ct => ReadSafetyStatusAsync(source, ct);
            BindRead(
                safety.ActiveFunction!,
                async ct => ToDataValue((int)(await ReadAndPushSafetyAsync(source, ct).ConfigureAwait(false)).ActiveFunction));
            BindRead(
                safety.EmergencyStopActive!,
                async ct => new DataValue((await ReadAndPushSafetyAsync(source, ct).ConfigureAwait(false)).EmergencyStopActive));
            BindRead(
                safety.ProtectiveStopActive!,
                async ct => new DataValue((await ReadAndPushSafetyAsync(source, ct).ConfigureAwait(false)).ProtectiveStopActive));
            BindRead(
                safety.SafeSpeedLimitActive!,
                async ct => new DataValue((await ReadAndPushSafetyAsync(source, ct).ConfigureAwait(false)).SafeSpeedLimitActive));
            BindRead(
                safety.SafeSpeedLimit!,
                async ct => new DataValue((await ReadAndPushSafetyAsync(source, ct).ConfigureAwait(false)).SafeSpeedLimit));
            BindRead(
                safety.SafetyControllerOk!,
                async ct => new DataValue((await ReadAndPushSafetyAsync(source, ct).ConfigureAwait(false)).SafetyControllerOk));
            BindRead(
                safety.LastStopReason!,
                async ct => new DataValue((await ReadAndPushSafetyAsync(source, ct).ConfigureAwait(false)).LastStopReason));
        }

        private async ValueTask<RobotIntentSafetySnapshot> ReadAndPushSafetyAsync(
            IRobotIntentSafetySource source,
            CancellationToken cancellationToken)
        {
            RobotIntentSafetySnapshot snapshot = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_host?.UpdateSafetyState(m_context.Context, ToSafetyStatus(snapshot));
            return snapshot;
        }

        private static async ValueTask<SafetyStatus> ReadSafetyStatusAsync(
            IRobotIntentSafetySource source,
            CancellationToken cancellationToken)
        {
            RobotIntentSafetySnapshot snapshot = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            return ToSafetyStatus(snapshot);
        }

        private static SafetyStatus ToSafetyStatus(RobotIntentSafetySnapshot snapshot)
        {
            return new SafetyStatus
            {
                ActiveFunction = snapshot.ActiveFunction,
                EmergencyStopActive = snapshot.EmergencyStopActive,
                ProtectiveStopActive = snapshot.ProtectiveStopActive,
                SafeSpeedLimitActive = snapshot.SafeSpeedLimitActive,
                SafeSpeedLimit = snapshot.SafeSpeedLimit,
                SafetyControllerOk = snapshot.SafetyControllerOk,
                LastStopReason = snapshot.LastStopReason.Text
            };
        }

        private TState AddContained<TState>(
            NodeState parent,
            string browseName,
            Func<NodeState, QualifiedName, TState> factory)
            where TState : BaseInstanceState
        {
            QualifiedName normalized = Normalize(browseName);
            return RoboticsBuilderUtilities.AddContained(
                m_context.Context,
                parent,
                normalized,
                global::Opc.Ua.ReferenceTypeIds.Organizes,
                factory);
        }

        private QualifiedName Normalize(string browseName)
        {
            if (string.IsNullOrWhiteSpace(browseName))
            {
                throw new ArgumentException("A non-empty browse name is required.", nameof(browseName));
            }
            return new QualifiedName(browseName, m_context.InstanceNamespaceIndex);
        }

        private IntentFrameBuilder RequireFrame(IIntentFrameBuilder frame, string parameterName)
        {
            if (frame is not IntentFrameBuilder builder || !m_frames.Contains(builder))
            {
                throw new ArgumentException(
                    "The frame must belong to this IntentController builder.",
                    parameterName);
            }
            return builder;
        }

        private void EnsureMutable()
        {
            if (m_registered)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "IntentController '{0}' has already been registered.",
                    State.BrowseName);
            }
        }

        private static void SetValue<T>(BaseVariableState variable, T value)
        {
            switch (variable)
            {
                case BaseDataVariableState<T> dataVariable:
                    dataVariable.Value = value;
                    break;
                case PropertyState<T> property:
                    property.Value = value;
                    break;
                case BaseVariableState when value is Enum enumValue:
                    variable.WrappedValue = ToVariant(
                        Convert.ToInt32(enumValue, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default:
                    variable.WrappedValue = value is null ? Variant.Null : ToVariant(value);
                    break;
            }
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
        }

        private static Variant ToVariant<T>(T value)
        {
            var builder = new VariantBuilder();
            return value switch
            {
                bool typed => ((IVariantBuilder<bool>)builder).WithValue(typed),
                uint typed => ((IVariantBuilder<uint>)builder).WithValue(typed),
                int typed => ((IVariantBuilder<int>)builder).WithValue(typed),
                double typed => ((IVariantBuilder<double>)builder).WithValue(typed),
                string typed => ((IVariantBuilder<string>)builder).WithValue(typed),
                NodeId typed => ((IVariantBuilder<NodeId>)builder).WithValue(typed),
                DateTimeUtc typed => ((IVariantBuilder<DateTimeUtc>)builder).WithValue(typed),
                LocalizedText typed => ((IVariantBuilder<LocalizedText>)builder).WithValue(typed),
                ArrayOf<string> typed => ((IVariantBuilder<ArrayOf<string>>)builder).WithValue(typed),
                ArrayOf<IntentCapabilityDataType> typed =>
                    ((IVariantBuilder<ArrayOf<ExtensionObject>>)builder).WithValue(
                        EncodeableToExtensionObjects(typed)),
                ArrayOf<KinematicJointDataType> typed =>
                    ((IVariantBuilder<ArrayOf<ExtensionObject>>)builder).WithValue(
                        EncodeableToExtensionObjects(typed)),
                Pose3DDataType typed => ((IVariantBuilder<ExtensionObject>)builder).WithValue(
                    new ExtensionObject(typed)),
                _ => Variant.Null
            };
        }

        private static ArrayOf<ExtensionObject> EncodeableToExtensionObjects<TEncodeable>(
            ArrayOf<TEncodeable> values)
            where TEncodeable : IEncodeable
        {
            var result = new ExtensionObject[values.Count];
            for (int ii = 0; ii < values.Count; ii++)
            {
                result[ii] = new ExtensionObject(values[ii]);
            }
            return result.ToArrayOf();
        }

        private static DataValue ToDataValue<T>(T value)
        {
            return new DataValue(ToVariant(value));
        }

        private static void MarkReadOnly(BaseVariableState variable)
        {
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
        }

        private static void MarkCommandMethod(MethodState method)
        {
            method.Executable = true;
            method.UserExecutable = true;
            method.RolePermissions = new[]
            {
                new RolePermissionType
                {
                    RoleId = global::Opc.Ua.ObjectIds.WellKnownRole_Operator,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Call)
                }
            }.ToArrayOf();
        }

        private static void BindRead(
            BaseVariableState variable,
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            RoboticsBuilderUtilities.BindRead(variable, read);
        }

        private static NodeId GetIntentDataType(
            ExpandedNodeId expanded,
            Type intentType,
            ISystemContext context)
        {
            var nodeId = ExpandedNodeId.ToNodeId(expanded, context.NamespaceUris);
            if (nodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Could not resolve intent DataType '{0}'.",
                    intentType.Name);
            }
            return nodeId;
        }

        private readonly IRobotIntentBuildContext m_context;
        private readonly ILogger m_logger;
        private readonly List<IntentFrameBuilder> m_frames = [];
        private readonly List<IntentToolBuilder> m_tools = [];
        private readonly List<IntentAxisBuilder> m_axes = [];
        private readonly List<IntentLocationBuilder> m_locations = [];
        private readonly List<IntentOutputSignalBuilder> m_outputs = [];
        private readonly List<IntentProgramBuilder> m_programs = [];
        private readonly List<IntentRealTimeChannelBuilder> m_realTimeChannels = [];
        private readonly List<IntentCapabilityDataType> m_capabilities = [];
        private readonly IntentControllerHostOptions m_hostOptions = new();
        private IIntentExecutor? m_executor;
        private IRobotIntentSafetySource? m_safetySource;
        private IntentDescriptionBuilder? m_description;
        private IntentControllerHost? m_host;
        private bool m_registered;
    }

    internal static class IntentControllerFacetMetadata
    {
        public static void MarkSafetyAdmissionGated(IntentControllerState controller)
        {
            s_safetyAdmissionGated.GetValue(controller, static _ => new SafetyAdmissionGateBinding());
        }

        public static bool HasSafetyAdmissionGate(IntentControllerState controller)
        {
            return s_safetyAdmissionGated.TryGetValue(controller, out _);
        }

        public static void MarkInterop40010Binding(
            IntentControllerState controller,
            MotionDeviceSystemState motionDeviceSystem)
        {
            s_interop40010Bindings.Remove(controller);
            s_interop40010Bindings.Add(controller, new Interop40010Binding(motionDeviceSystem));
        }

        public static bool TryGetInterop40010Binding(
            IntentControllerState controller,
            out MotionDeviceSystemState motionDeviceSystem)
        {
            if (s_interop40010Bindings.TryGetValue(controller, out Interop40010Binding? binding))
            {
                motionDeviceSystem = binding.MotionDeviceSystem;
                return true;
            }
            motionDeviceSystem = null!;
            return false;
        }

        private sealed class SafetyAdmissionGateBinding;

        private sealed class Interop40010Binding
        {
            public Interop40010Binding(MotionDeviceSystemState motionDeviceSystem)
            {
                MotionDeviceSystem = motionDeviceSystem;
            }

            public MotionDeviceSystemState MotionDeviceSystem { get; }
        }

        private static readonly ConditionalWeakTable<IntentControllerState, SafetyAdmissionGateBinding>
            s_safetyAdmissionGated = new();

        private static readonly ConditionalWeakTable<IntentControllerState, Interop40010Binding>
            s_interop40010Bindings = new();
    }

    internal sealed class IntentFrameBuilder : IIntentFrameBuilder
    {
        public IntentFrameBuilder(
            IntentControllerBuilder controller,
            global::Opc.Ua.RobotIntent.CoordinateFrameState state)
        {
            m_controller = controller;
            State = state;
        }

        public global::Opc.Ua.RobotIntent.CoordinateFrameState State { get; }

        public IIntentFrameBuilder WithParent(IIntentFrameBuilder parent)
        {
            if (parent is not IntentFrameBuilder builder)
            {
                throw new ArgumentException("The parent frame must be a frame builder.", nameof(parent));
            }
            var referenceTypeId = NodeId.Create(
                global::Opc.Ua.RobotIntent.ReferenceTypes.HasFrameParent,
                global::Opc.Ua.RobotIntent.Namespaces.RobotIntent,
                m_controller.Context.NamespaceUris);
            if (!State.ReferenceExists(referenceTypeId, false, builder.State.NodeId))
            {
                State.AddReference(referenceTypeId, false, builder.State.NodeId);
                builder.State.AddReference(referenceTypeId, true, State.NodeId);
            }
            return this;
        }

        private readonly IntentControllerBuilder m_controller;
    }

    internal sealed class IntentToolBuilder : IIntentToolBuilder
    {
        public IntentToolBuilder(IntentControllerBuilder controller, global::Opc.Ua.RobotIntent.ToolState state)
        {
            m_controller = controller;
            State = state;
        }

        public global::Opc.Ua.RobotIntent.ToolState State { get; }

        public IIntentToolBuilder WithFitted(bool fitted = true)
        {
            m_controller.SetToolFitted(this, fitted);
            return this;
        }

        private readonly IntentControllerBuilder m_controller;
    }

    internal sealed class IntentLocationBuilder : IIntentLocationBuilder
    {
        public IntentLocationBuilder(global::Opc.Ua.RobotIntent.LocationState state)
        {
            State = state;
        }

        public global::Opc.Ua.RobotIntent.LocationState State { get; }

        public IIntentLocationBuilder WithOccupancy(bool occupied, uint capacity = 1)
        {
            State.Occupied!.Value = occupied;
            State.Capacity!.Value = capacity;
            return this;
        }
    }

    internal sealed class IntentAxisBuilder : IIntentAxisBuilder
    {
        public IntentAxisBuilder(global::Opc.Ua.RobotIntent.AxisState state)
        {
            State = state;
        }

        public global::Opc.Ua.RobotIntent.AxisState State { get; }
    }

    internal sealed class IntentOutputSignalBuilder : IIntentOutputSignalBuilder
    {
        public IntentOutputSignalBuilder(global::Opc.Ua.RobotIntent.OutputSignalState state)
        {
            State = state;
        }

        public global::Opc.Ua.RobotIntent.OutputSignalState State { get; }
    }

    internal sealed class IntentProgramBuilder : IIntentProgramBuilder
    {
        public IntentProgramBuilder(global::Opc.Ua.RobotIntent.ProgramState state)
        {
            State = state;
        }

        public global::Opc.Ua.RobotIntent.ProgramState State { get; }
    }

    internal sealed class IntentDescriptionBuilder : IIntentDescriptionBuilder
    {
        public IntentDescriptionBuilder(global::Opc.Ua.RobotIntent.RobotDescriptionState state)
        {
            State = state;
        }

        public global::Opc.Ua.RobotIntent.RobotDescriptionState State { get; }

        public IIntentDescriptionBuilder WithKinematicChain(ArrayOf<KinematicJointDataType> chain)
        {
            State.KinematicChain!.Value = chain.IsNull ? [] : chain;
            return this;
        }

        public IIntentDescriptionBuilder WithLimits(
            double reachRadius,
            double payloadLimit,
            double maxCartesianSpeed,
            double maxCartesianAcceleration)
        {
            State.ReachRadius!.Value = reachRadius;
            State.PayloadLimit!.Value = payloadLimit;
            State.MaxCartesianSpeed!.Value = maxCartesianSpeed;
            State.MaxCartesianAcceleration!.Value = maxCartesianAcceleration;
            return this;
        }
    }

    internal sealed class IntentRealTimeChannelBuilder : IIntentRealTimeChannelBuilder
    {
        public IntentRealTimeChannelBuilder(global::Opc.Ua.RobotIntent.RealTimeChannelState state)
        {
            State = state;
        }

        public global::Opc.Ua.RobotIntent.RealTimeChannelState State { get; }
    }

    internal static partial class IntentControllerBuilderLog
    {
        [LoggerMessage(
            EventId = RobotIntentServerEventIds.HostNotStarted,
            Level = LogLevel.Warning,
            Message = "Robot Intent Method {MethodName} was invoked before the controller host was started.")]
        public static partial void HostNotStarted(this ILogger logger, string methodName);
    }
}
