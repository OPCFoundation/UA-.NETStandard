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
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Recycling.Model;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Read-only equipment projection on a single DI/Machinery owner. Native instrument runtimes
    /// consume absolute committed state; none owns a production clock or accepts local production commands.
    /// </summary>
    public sealed partial class CompanionProcessViewAdapter : IRecyclingProcessViewAdapter, IAsyncDisposable
    {
        /// <summary>
        /// Creates a directly usable adapter. The optional display coefficients remain sample configuration.
        /// </summary>
        public CompanionProcessViewAdapter(
            RecyclingHostOptions options,
            RecyclingAuthorization authorization,
            RecyclingSignalDefaults defaults,
            RecyclingNativeProcessOptions? nativeOptions = null)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            m_defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
            defaults.Validate();
            m_nativeOptions = nativeOptions ?? new RecyclingNativeProcessOptions();
            m_nativeOptions.Validate();
            m_nativeCommands = new RecyclingNativeCommandRefusal(authorization);
        }

        /// <inheritdoc/>
        public bool CompanionBindingsComplete => true;

        /// <inheritdoc/>
        public async ValueTask InitializeAsync(IMachineryBuildContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (m_context != null)
            {
                throw new InvalidOperationException("The process adapter already has an address-space owner.");
            }
            m_context = context;
            context.Manager.SystemContext.NodeIdFactory = new RecyclingInstanceNodeIds(context.InstanceNamespaceIndex);
            ushort modelIndex = context.Context.NamespaceUris.GetIndexOrAppend(RecyclingModelMapping.NamespaceUri);
            NodeState view = context.Manager.FindPredefinedNode(new NodeId(Model.Objects.ProcessView, modelIndex)) ??
                throw new InvalidOperationException("The typed process-view entry point was not loaded.");
            for (int index = 0; index < m_options.Simulation.Stages.Count; index++)
            {
                StageRecipe recipe = m_options.Simulation.Stages[index];
                if (recipe.Process != m_options.Process)
                {
                    continue;
                }
                RecyclingProcessAssetState state = CreateStage(
                    context.Context,
                    context.MachinesFolder,
                    new QualifiedName(recipe.Name, context.InstanceNamespaceIndex),
                    index);
                state.MachineId!.Value = AssetUri(recipe.Name);
                state.Process!.Value = (ProcessEnum)recipe.Process;
                state.StageIndex!.Value = index;
                state.BindingStatus!.Value =
                    "Sample process context with selected native equipment; production controls are not bound.";
                state.Description = new LocalizedText(
                    "Sample-owned causal process context. Equipment references the actual companion instances.");
                state.SignalOrigin!.Value =
                    "Synthetic committed-state projection, not calibrated telemetry or original-schema parity.";
                if (state.Manufacturer != null)
                {
                    state.Manufacturer.Value = new LocalizedText("OPC Foundation sample");
                }
                if (state.SerialNumber != null)
                {
                    state.SerialNumber.Value = m_options.LineId + "-" + recipe.Name;
                }
                if (state.Model != null)
                {
                    state.Model.Value = new LocalizedText("Synthetic " + recipe.Name);
                }
                ConfigureMeasurements(state);
                IMachineHandle<RecyclingProcessAssetState> machine = await context.AddMachine(state)
                    .WithIdentification(identity => Identify(identity, recipe.Name))
                    .WithMonitoring(monitoring => monitoring
                        .WithMachineryItemState()
                        .WithOperationMode(MachineryOperationModeValue.Processing))
                    .BuildAsync(cancellationToken).ConfigureAwait(false);
                Link(context.DeviceSet, state);
                Link(view, state);
                await RegisterAssetAsync(state, cancellationToken).ConfigureAwait(false);
                var stage = new StageBinding(index, machine);
                m_stages.Add(index, stage);
                foreach (BaseVariableState variable in Descendants(state).OfType<BaseVariableState>())
                {
                    if (variable.ReferenceTypeId != ReferenceTypeIds.HasProperty &&
                        variable.BrowseName.NamespaceIndex == modelIndex)
                    {
                        Track(variable);
                    }
                }
                TrackStateMachines(state);
                await BuildInstrumentsAsync(stage, cancellationToken).ConfigureAwait(false);
                Protect(state);
            }
        }

        /// <inheritdoc/>
        public async ValueTask ApplyAsync(CommittedSnapshot snapshot, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IMachineryBuildContext context = RequireContext();
                ProjectionImage? previous = Volatile.Read(ref m_image);
                if (previous != null && previous.RunId == snapshot.RunId && previous.Revision > snapshot.Revision)
                {
                    return;
                }
                if (previous == null || previous.RunId != snapshot.RunId || previous.Revision != snapshot.Revision)
                {
                    SnapshotDataType wire = RecyclingModelMapping.ToWire(snapshot);
                    m_signalTimestamps.Clear();
                    m_clock.Timestamp = snapshot.SourceTimestampUtc;
                    foreach (StageBinding binding in m_stages.Values)
                    {
                        StageSnapshot stage = snapshot.Stages[binding.Index];
                        RecyclingProcessAssetState state = binding.Machine.State;
                        ProjectStage(state, stage, snapshot, wire);
                        MachineryItemStateValue itemState = stage.State switch
                        {
                            EquipmentState.Unavailable => MachineryItemStateValue.NotAvailable,
                            EquipmentState.Stopped => MachineryItemStateValue.OutOfService,
                            EquipmentState.Executing => MachineryItemStateValue.Executing,
                            _ => MachineryItemStateValue.NotExecuting
                        };
                        if (binding.Machine.ItemState!.CurrentState != itemState)
                        {
                            await binding.Machine.ItemState.SetStateAsync(itemState, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        await ProjectInstrumentsAsync(binding, stage, snapshot, cancellationToken)
                            .ConfigureAwait(false);
                        await ProjectNativeProcessAsync(binding, stage, snapshot, cancellationToken)
                            .ConfigureAwait(false);
                        Protect(state);
                    }
                    var values = new Dictionary<NodeId, ProjectedValue>();
                    foreach (BaseVariableState variable in m_projected.Values)
                    {
                        if (variable.BrowseName.Name is "TransitionTime" or "EffectiveTransitionTime" &&
                            (previous == null ||
                                !previous.Values.TryGetValue(variable.NodeId, out ProjectedValue old) ||
                                old.Value != variable.WrappedValue))
                        {
                            variable.WrappedValue = Variant.From((DateTimeUtc)snapshot.SourceTimestampUtc);
                        }
                        DateTime sourceTimestamp = m_signalTimestamps.TryGetValue(
                            variable.NodeId, out DateTime measured)
                            ? measured : m_nativeTimestampNodes.Contains(variable.NodeId)
                                ? variable.Timestamp.ToDateTime() : snapshot.SourceTimestampUtc;
                        values.Add(variable.NodeId, new ProjectedValue(
                            CoreUtils.Clone(variable.WrappedValue), variable.StatusCode, sourceTimestamp));
                        variable.Timestamp = sourceTimestamp;
                    }
                    Volatile.Write(ref m_image, new ProjectionImage(
                        snapshot.RunId, snapshot.Revision, snapshot.SourceTimestampUtc, true, values));
                }
                else
                {
                    Volatile.Write(ref m_image, previous with { Available = true });
                }
                await NotifyAsync(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask MarkUnavailableAsync(DateTime sourceTimestampUtc, CancellationToken cancellationToken)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ProjectionImage? previous = Volatile.Read(ref m_image);
                if (previous != null)
                {
                    // The caller's outage-detection time is not a new source sample.
                    Volatile.Write(ref m_image, previous with { Available = false });
                }
                await NotifyAsync(RequireContext(), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            if (m_scalesRuntime != null)
            {
                await m_scalesRuntime.DisposeAsync().ConfigureAwait(false);
            }
            if (m_pumpsRuntime != null)
            {
                await m_pumpsRuntime.DisposeAsync().ConfigureAwait(false);
            }
            if (m_extrusionRuntime != null)
            {
                await m_extrusionRuntime.DisposeAsync().ConfigureAwait(false);
            }
            if (m_dryingRuntime != null)
            {
                await m_dryingRuntime.DisposeAsync().ConfigureAwait(false);
            }
            m_gate.Dispose();
        }

        private static RecyclingProcessAssetState CreateStage(
            ISystemContext context,
            NodeState parent,
            QualifiedName name,
            int index)
        {
            return index switch
            {
                0 => context.CreateInstanceOfCollectionBinType(parent, name),
                1 => context.CreateInstanceOfCollectionTransportType(parent, name),
                2 => context.CreateInstanceOfInboundIdentificationType(parent, name),
                3 => context.CreateInstanceOfFeedConveyorType(parent, name),
                4 or 5 => context.CreateInstanceOfOpticalSortingStationType(parent, name),
                6 => context.CreateInstanceOfPrimaryReductionType(parent, name),
                7 => context.CreateInstanceOfSecondaryReductionType(parent, name),
                8 => context.CreateInstanceOfPreWashType(parent, name),
                9 => context.CreateInstanceOfFrictionWashType(parent, name),
                10 => context.CreateInstanceOfHotWashType(parent, name),
                11 => context.CreateInstanceOfRinsingType(parent, name),
                12 => context.CreateInstanceOfDensitySeparationType(parent, name),
                13 => context.CreateInstanceOfMechanicalDewateringType(parent, name),
                14 => context.CreateInstanceOfSampleThermalDryingType(parent, name),
                15 => context.CreateInstanceOfDryMaterialBufferType(parent, name),
                16 => context.CreateInstanceOfSampleExtrusionType(parent, name),
                17 => context.CreateInstanceOfSampleMeltFiltrationType(parent, name),
                18 => context.CreateInstanceOfSamplePelletizingType(parent, name),
                19 => context.CreateInstanceOfPelletScreeningType(parent, name),
                20 => context.CreateInstanceOfBaggingStationType(parent, name),
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        private string AssetUri(string name)
        {
            return "urn:recycling-sample:" + Uri.EscapeDataString(m_options.LineId) + ":" + name;
        }

        private void Identify(MachineryIdentificationData identity, string name)
        {
            identity.Manufacturer = new LocalizedText("OPC Foundation sample");
            identity.SerialNumber = m_options.LineId + "-" + name;
            identity.ProductInstanceUri = AssetUri(name);
            identity.Model = new LocalizedText("Synthetic recycling " + name);
        }

        private async ValueTask RegisterAssetAsync(BaseObjectState asset, CancellationToken cancellationToken)
        {
            IMachineryBuildContext context = RequireContext();
            await context.GetRequiredService<IAssetManagement>().RegisterAssetAsync(
                context.Nodes.Node<BaseObjectState>(asset.NodeId), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private static void Link(NodeState source, NodeState target)
        {
            if (!source.ReferenceExists(ReferenceTypeIds.Organizes, false, target.NodeId))
            {
                source.AddReference(ReferenceTypeIds.Organizes, false, target.NodeId);
                target.AddReference(ReferenceTypeIds.Organizes, true, source.NodeId);
            }
        }

        private void Track(BaseVariableState variable)
        {
            if (!m_projected.TryAdd(variable.NodeId, variable))
            {
                return;
            }
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.MinimumSamplingInterval = 100;
            variable.OnReadUserAccessLevel = ReadAccess;
            bool availability = variable.BrowseName == new QualifiedName("Available",
                RequireContext().Context.NamespaceUris.GetIndexOrAppend(RecyclingModelMapping.NamespaceUri));
            variable.OnReadValueAsync = (_, _, range, encoding, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                ProjectionImage? image = Volatile.Read(ref m_image);
                if (!encoding.IsNull)
                {
                    return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                        StatusCodes.BadDataEncodingUnsupported, Variant.Null,
                        StatusCodes.BadDataEncodingUnsupported, image?.Timestamp ?? DateTime.UnixEpoch));
                }
                if (!range.IsNull)
                {
                    return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                        StatusCodes.BadIndexRangeInvalid, Variant.Null,
                        StatusCodes.BadIndexRangeInvalid, image?.Timestamp ?? DateTime.UnixEpoch));
                }
                bool found = image != null && image.Values.TryGetValue(variable.NodeId, out _);
                ProjectedValue value = found ? image!.Values[variable.NodeId] :
                    new ProjectedValue(Variant.Null, StatusCodes.BadWaitingForInitialData, DateTime.UnixEpoch);
                if (availability)
                {
                    value = new ProjectedValue(
                        Variant.From(image?.Available == true),
                        StatusCodes.Good,
                        image?.Timestamp ?? DateTime.UnixEpoch);
                }
                else if (image?.Available == false && !value.Value.IsNull && !StatusCode.IsBad(value.Status))
                {
                    // UA deliberately omits a Value for Bad status. Uncertain retains a last-known sample;
                    // Available is false, so no observer can mistake it for a current Good measurement.
                    value = value with { Status = StatusCodes.UncertainLastUsableValue };
                }
                return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                    ServiceResult.Good, CoreUtils.Clone(value.Value),
                    value.Status,
                    value.SourceTimestamp));
            };
        }

        private void TrackStateMachines(NodeState root)
        {
            foreach (FiniteStateMachineState machine in Descendants(root).OfType<FiniteStateMachineState>())
            {
                if (machine.CurrentState != null)
                {
                    foreach (BaseVariableState variable in Descendants(machine.CurrentState)
                        .OfType<BaseVariableState>())
                    {
                        Track(variable);
                    }
                }
                if (machine.LastTransition != null)
                {
                    foreach (BaseVariableState variable in Descendants(machine.LastTransition)
                        .OfType<BaseVariableState>())
                    {
                        Track(variable);
                    }
                }
                machine.SuppressTransitionEvents = true;
            }
        }

        private ServiceResult ReadAccess(ISystemContext context, NodeState _, ref byte value)
        {
            try
            {
                m_authorization.Resolve(context);
                value = AccessLevels.CurrentRead;
            }
            catch (ServiceResultException)
            {
                value = AccessLevels.None;
            }
            return ServiceResult.Good;
        }

        private void Protect(NodeState root)
        {
            foreach (NodeState node in Descendants(root))
            {
                if (node is BaseVariableState variable)
                {
                    variable.AccessLevel = AccessLevels.CurrentRead;
                    variable.UserAccessLevel = AccessLevels.CurrentRead;
                    variable.OnReadUserAccessLevel = ReadAccess;
                }
                if (node is MethodState method)
                {
                    if (method.Parent is Vision.MachineVision.ResultManagementState &&
                        method.BrowseName.Name is "GetResultById" or "GetResultComponentsById" or
                            "GetResultListFiltered" or "ReleaseResultHandle")
                    {
                        if (m_readMethods.Add(method.NodeId))
                        {
                            GenericMethodCalledEventHandler2Async handler = method.OnCallMethod2Async ??
                                throw new InvalidOperationException("The native result method was not bound.");
                            method.OnReadUserExecutable = ReadResultExecutable;
                            method.OnCallMethod2Async = async (ctx, called, target, inputs, outputs, ct) =>
                            {
                                m_authorization.Resolve(ctx);
                                if (Volatile.Read(ref m_image)?.Available != true)
                                {
                                    return new ServiceResult(StatusCodes.BadNoCommunication);
                                }
                                return await handler(ctx, called, target, inputs, outputs, ct).ConfigureAwait(false);
                            };
                        }
                        continue;
                    }
                    method.OnCallMethod2 = null;
                    method.OnCallMethod2Async = (_, _, _, _, _, _) =>
                        new ValueTask<ServiceResult>(RefuseLocalCommand());
                    method.Executable = false;
                    method.UserExecutable = false;
                    method.OnReadExecutable = RefuseExecutable;
                    method.OnReadUserExecutable = RefuseExecutable;
                }
            }
        }

        private static ServiceResult RefuseLocalCommand()
        {
            return ServiceResult.Create(StatusCodes.BadNotSupported,
                "Read-only committed projection. Use ProcessView.ForwardCommand with same-user authentication.");
        }

        private static ServiceResult RefuseExecutable(ISystemContext _, NodeState node, ref bool value)
        {
            value = false;
            return ServiceResult.Good;
        }

        private ServiceResult ReadResultExecutable(ISystemContext context, NodeState _, ref bool value)
        {
            try
            {
                m_authorization.Resolve(context);
                value = Volatile.Read(ref m_image)?.Available == true;
            }
            catch (ServiceResultException)
            {
                value = false;
            }
            return ServiceResult.Good;
        }

        private IEnumerable<NodeState> Descendants(NodeState root)
        {
            var pending = new Stack<NodeState>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                NodeState node = pending.Pop();
                yield return node;
                var children = new List<BaseInstanceState>();
                node.GetChildren(RequireContext().Context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
        }

        private async ValueTask NotifyAsync(IMachineryBuildContext context, CancellationToken cancellationToken)
        {
            foreach (BaseVariableState variable in m_projected.Values)
            {
                variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
                await variable.ClearChangeMasksAsync(context.Context, false, cancellationToken).ConfigureAwait(false);
            }
        }

        private IMachineryBuildContext RequireContext()
        {
            return m_context ?? throw new InvalidOperationException("Initialize the process adapter first.");
        }

        /// <summary>
        /// Local handles only; never authoritative production state.
        /// </summary>
        private sealed class StageBinding(int index, IMachineHandle<RecyclingProcessAssetState> machine)
        {
            /// <summary>
            /// The immutable recipe position.
            /// </summary>
            public int Index { get; } = index;

            /// <summary>
            /// The sample process asset and Machinery controllers.
            /// </summary>
            public IMachineHandle<RecyclingProcessAssetState> Machine { get; } = machine;

            /// <summary>
            /// The actual weighing instrument, where present.
            /// </summary>
            public ScaleHandle? Scale { get; set; }

            /// <summary>
            /// Native pump process readings.
            /// </summary>
            public List<(BaseVariableState Running, BaseVariableState Temperature)> Pumps { get; } = [];

            /// <summary>
            /// The actual vision control system, not the whole sorter.
            /// </summary>
            public MachineVisionSystemHandle? Vision { get; set; }

            /// <summary>
            /// The last committed inspection already published by this local runtime.
            /// </summary>
            public string LastVisionResult { get; set; } = string.Empty;

            /// <summary>
            /// The Part 2 image sensor health member.
            /// </summary>
            public BaseVariableState? CameraTemperature { get; set; }

            /// <summary>
            /// Genuine extrusion device projections, including a separate line and extruder.
            /// </summary>
            public List<NativeExtrusionProjection> Extrusion { get; } = [];

            /// <summary>
            /// The native thermal GDD device; mechanical dewatering and buffering remain distinct assets.
            /// </summary>
            public GranulateDrying.Server.GranulateDryingDevice? Drying { get; set; }
        }

        /// <summary>
        /// One immutable read image prevents clients observing intermediate native-runtime updates.
        /// </summary>
        private sealed record ProjectionImage(
            string RunId, long Revision, DateTime Timestamp, bool Available, Dictionary<NodeId, ProjectedValue> Values);

        /// <summary>
        /// A detached committed value and its intrinsic (not connectivity) status.
        /// </summary>
        private readonly record struct ProjectedValue(Variant Value, StatusCode Status, DateTime SourceTimestamp);

        /// <summary>
        /// Supplies only committed source time to native runtimes that accept a clock.
        /// </summary>
        private sealed class ProjectionClock : TimeProvider
        {
            /// <summary>
            /// The source timestamp selected by ApplyAsync.
            /// </summary>
            public DateTime Timestamp { get; set; } = DateTime.UnixEpoch;

            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return new DateTimeOffset(Timestamp);
            }
        }

        /// <summary>Host identity and recipe layout.</summary>
        private readonly RecyclingHostOptions m_options;
        /// <summary>Authenticated read authorization.</summary>
        private readonly RecyclingAuthorization m_authorization;
        /// <summary>Explicit synthetic coefficients.</summary>
        private readonly RecyclingSignalDefaults m_defaults;
        /// <summary>Explicit synthetic native-equipment coefficients.</summary>
        private readonly RecyclingNativeProcessOptions m_nativeOptions;
        /// <summary>Original-user-aware refusal until production forwarding is configured.</summary>
        private readonly RecyclingNativeCommandRefusal m_nativeCommands;
        /// <summary>Serialized application and availability changes.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);
        /// <summary>Native runtime time is derived from commits.</summary>
        private readonly ProjectionClock m_clock = new();
        /// <summary>Registered process assets.</summary>
        private readonly Dictionary<int, StageBinding> m_stages = [];
        /// <summary>Variables whose reads come from an atomic source image.</summary>
        private readonly Dictionary<NodeId, BaseVariableState> m_projected = [];
        /// <summary>Field-specific committed sample times; never projection/reconnect wall time.</summary>
        private readonly Dictionary<NodeId, DateTime> m_signalTimestamps = [];
        /// <summary>Result methods already wrapped with authenticated read-only access.</summary>
        private readonly HashSet<NodeId> m_readMethods = [];
        /// <summary>The only DI/Machinery address-space owner.</summary>
        private IMachineryBuildContext? m_context;
        /// <summary>Detached source image, atomically replaced.</summary>
        private ProjectionImage? m_image;
        /// <summary>Idempotent host/direct lifetime cleanup.</summary>
        private int m_disposed;
    }
}
