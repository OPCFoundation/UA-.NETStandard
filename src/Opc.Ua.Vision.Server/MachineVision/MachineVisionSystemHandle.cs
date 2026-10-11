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
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Vision.MachineVision;
using Automatic = Opc.Ua.Vision.MachineVision.VisionAutomaticModeStateMachineTypeIds;
using Control = Opc.Ua.Vision.MachineVision.VisionStateMachineTypeIds;
using Mv = Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Vision.Server
{
    public static partial class VisionMachineVisionBuilderExtensions
    {
        /// <summary>
        /// Creates and binds a released Part 1 system in the existing Vision address space.
        /// The returned handle owns execution and file resources until disposed by the manager.
        /// </summary>
        public static ValueTask<MachineVisionSystemHandle> AddMachineVisionSystemAsync(
            this IVisionBuildContext context,
            string browseName,
            IMachineVisionOperationsProvider provider,
            MachineVisionSystemOptions? options = null,
            IMachineVisionStore? store = null,
            TimeProvider? timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            return MachineVisionSystemHandle.CreateAsync(
                context, browseName, provider, options, store, timeProvider, cancellationToken);
        }
    }

    /// <summary>
    /// Owns an OPC 40100-1 system's control, management, diagnostic, and transfer runtime.
    /// Provider work is asynchronous; one serialized command changes protocol state at a time.
    /// </summary>
    public sealed partial class MachineVisionSystemHandle : IAsyncDisposable, IConformanceContributor
    {
        private MachineVisionSystemHandle(
            IVisionBuildContext context,
            VisionSystemState state,
            IMachineVisionOperationsProvider provider,
            MachineVisionSystemOptions options,
            IMachineVisionStore store,
            TimeProvider clock)
        {
            m_context = context;
            State = state;
            m_provider = provider;
            m_options = options;
            m_store = store;
            m_clock = clock;
            m_logger = context.Context.Telemetry.CreateLogger<MachineVisionSystemHandle>();
            m_externalReady = options.InitiallyReady;
            m_simulated = options.InitiallySimulated;
        }

        /// <summary>
        /// Gets the generated system state.
        /// </summary>
        public VisionSystemState State { get; }

        /// <summary>
        /// Gets the storage used by this system.
        /// </summary>
        public IMachineVisionStore Store => m_store;

        /// <summary>
        /// Gets the top-level state machine.
        /// </summary>
        public VisionStateMachineState ControlState => State.VisionStateMachine!;

        /// <summary>
        /// Gets the automatic-mode state machine.
        /// </summary>
        public VisionAutomaticModeStateMachineState AutomaticState => ControlState.AutomaticModeStateMachine!;

        /// <summary>
        /// Gets the specification-named units enabled by this runtime, qualified by the released model namespace.
        /// Provider-dependent processing-time claims and unverified profile URIs are not inferred.
        /// </summary>
        public ArrayOf<QualifiedName> ConformanceUnits => m_disposed
            ? [] : MachineVisionConformance.Control(m_context.Context, m_options.Features);

        /// <summary>
        /// No unverified OPC Foundation profile URI is advertised.
        /// </summary>
        public ArrayOf<string> ServerProfiles => [];

        /// <summary>
        /// Creates a directly usable runtime without requiring a dependency-injection container.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<MachineVisionSystemHandle> CreateAsync(
            IVisionBuildContext context,
            string browseName,
            IMachineVisionOperationsProvider provider,
            MachineVisionSystemOptions? options = null,
            IMachineVisionStore? store = null,
            TimeProvider? timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (string.IsNullOrWhiteSpace(browseName))
            {
                throw new ArgumentException("A system browse name is required.", nameof(browseName));
            }
            if (context.Context.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri) < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Enable MachineVisionParts.Control before building a system.");
            }
            var name = new QualifiedName(browseName, context.InstanceNamespaceIndex);
            if (context.Root.FindChild(context.Context, name) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated, "The system browse name already exists.");
            }
            MachineVisionSystemOptions configuration = (options ?? new MachineVisionSystemOptions()).CopyValidated();
            VisionSystemState state = context.Context.CreateInstanceOfVisionSystemType(context.Root, name);
            state.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            state.EventNotifier = EventNotifiers.SubscribeToEvents;
            var runtime = new MachineVisionSystemHandle(
                context, state, provider, configuration, store ?? new InMemoryMachineVisionStore(),
                timeProvider ?? TimeProvider.System);
            bool published = false;
            try
            {
                runtime.BindControl();
                await runtime.BindManagementAsync(cancellationToken).ConfigureAwait(false);
                runtime.BindSteps();
                runtime.BindSafety();
                MachineVisionNodes.AttachRoot(context, state);
                await context.Manager.AddPredefinedNodeAsync(state, cancellationToken).ConfigureAwait(false);
                context.Manager.AddRootNotifier(state);
                runtime.ControlState.SetState(context.Context, Control.StateIds.Preoperational);
                runtime.AutomaticState.SetState(context.Context, 0);
                await runtime.RefreshStepsAsync().ConfigureAwait(false);
                if (context is VisionBuildContext visionContext)
                {
                    visionContext.Registry.AdoptMachineVisionResource(runtime);
                }
                published = true;
                return runtime;
            }
            finally
            {
                if (!published)
                {
                    context.Root.RemoveChild(state);
                    await runtime.DisposeAsync().ConfigureAwait(false);
                    await context.Manager.DeleteNodeAsync(
                        context.Manager.SystemContext, state.NodeId, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Updates readiness for a provider-managed recipe, including systems without
        /// optional recipe-management methods.
        /// </summary>
        public async ValueTask SetReadyAsync(bool ready, CancellationToken cancellationToken = default)
        {
            await EnterRuntimeAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                m_externalReady = ready;
                await UpdateReadyStateAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Publishes the provider's SEMI E10 system-state description.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask SetSystemStateAsync(
            SystemStateDescriptionDataType state,
            CancellationToken cancellationToken = default)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            await EnterRuntimeAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (!Has(MachineVisionFeatures.Diagnostics))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotSupported, "Diagnostics are not enabled.");
                }
                BaseVariableState value = MachineVisionNodes.Require<BaseVariableState>(
                    m_context.Manager, State, "SystemState");
                MachineVisionNodes.Set(m_context.Context, value, Variant.FromStructure(state, copy: true), Now);
                await m_context.Manager.AddPredefinedNodeAsync(value, cancellationToken).ConfigureAwait(false);
                await value.ClearChangeMasksAsync(m_context.Context, false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposeStarted, 1) != 0)
            {
                return;
            }
            await m_shutdown.CancelAsync().ConfigureAwait(false);
            Task[] pending;
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                if (m_executionCancellation != null)
                {
                    await m_executionCancellation.CancelAsync().ConfigureAwait(false);
                }
                pending = [.. m_executions];
            }
            finally
            {
                m_gate.Release();
            }
            await Task.WhenAll(pending).ConfigureAwait(false);
            await DisposeTransfersAsync().ConfigureAwait(false);
            m_executionCancellation?.Dispose();
            m_shutdown.Dispose();
            m_transferGate.Dispose();
            m_gate.Dispose();
        }

        private DateTimeUtc Now => m_clock.GetUtcNow().UtcDateTime;

        private async ValueTask EnterRuntimeAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref m_disposeStarted) != 0)
            {
                throw InvalidState("The vision system is being disposed.");
            }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
            await m_gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        }

        private void BindControl()
        {
            MachineVisionNodes.Require<VisionAutomaticModeStateMachineState>(
                m_context.Manager, ControlState, "AutomaticModeStateMachine");
            InitializeFiniteState(ControlState);
            InitializeFiniteState(AutomaticState);
            ControlState.SuppressTransitionEvents = true;
            AutomaticState.SuppressTransitionEvents = true;
            Bind(ControlState, "Halt", (ctx, args, output, ct) =>
                ApplyControlAsync(MachineVisionControlAction.Halt, Int32(args, 0), Text(args, 1), output, ct));
            Bind(ControlState, "Reset", (ctx, args, output, ct) =>
                ApplyControlAsync(MachineVisionControlAction.Reset, Int32(args, 0), Text(args, 1), output, ct));
            Bind(ControlState, "SelectModeAutomatic", (ctx, args, output, ct) =>
                ApplyControlAsync(MachineVisionControlAction.SelectAutomatic, 0, string.Empty, output, ct));
            Bind(ControlState, "ConfirmAll", ConfirmAllAsync);
            Bind(AutomaticState, "StartSingleJob", (ctx, args, output, ct) =>
                StartAsync(args, output, continuous: false, ct));
            Bind(AutomaticState, "StartContinuous", (ctx, args, output, ct) =>
                StartAsync(args, output, continuous: true, ct));
            Bind(AutomaticState, "Stop", (ctx, args, output, ct) =>
                ApplyControlAsync(MachineVisionControlAction.Stop, Int32(args, 0), Text(args, 1), output, ct));
            Bind(AutomaticState, "Abort", (ctx, args, output, ct) =>
                ApplyControlAsync(MachineVisionControlAction.Abort, Int32(args, 0), Text(args, 1), output, ct));
            if (Has(MachineVisionFeatures.Simulation))
            {
                Bind(AutomaticState, "SimulationMode", (ctx, args, output, ct) =>
                    ApplyControlAsync(Boolean(args, 0) ? MachineVisionControlAction.EnableSimulation :
                        MachineVisionControlAction.DisableSimulation, Int32(args, 1), Text(args, 2), output, ct));
            }
            if (Has(MachineVisionFeatures.Diagnostics))
            {
                BaseVariableState level = MachineVisionNodes.Require<BaseVariableState>(
                    m_context.Manager, State, "DiagnosticLevel");
                MachineVisionNodes.Set(m_context.Context, level, Variant.From((ushort)200), Now);
                MachineVisionNodes.Require<BaseVariableState>(m_context.Manager, State, "SystemState");
            }
        }

        private void InitializeFiniteState(FiniteStateMachineState state)
        {
            MachineVisionNodes.Require<FiniteStateVariableState>(m_context.Manager, state, "CurrentState")
                .CreateOrReplaceNumber(m_context.Context, null);
            FiniteTransitionVariableState transition = state.CreateOrReplaceLastTransition(m_context.Context, null);
            transition.Create(m_context.Context, NodeId.Null,
                new QualifiedName(global::Opc.Ua.BrowseNames.LastTransition),
                new LocalizedText(global::Opc.Ua.BrowseNames.LastTransition), true);
            transition.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            transition.CreateOrReplaceNumber(m_context.Context, null);
            transition.CreateOrReplaceTransitionTime(m_context.Context, null);
            MachineVisionNodes.Rebase(m_context.Context, transition);
        }

        private async ValueTask<MachineVisionOperationResult> ApplyControlAsync(
            MachineVisionControlAction action,
            int cause,
            string description,
            List<Variant> output,
            CancellationToken ct)
        {
            uint current = ControlState.GetStateId(ControlState.CurrentState!.Id!.Value);
            if (action == MachineVisionControlAction.SelectAutomatic && current != Control.StateIds.Preoperational)
            {
                throw InvalidState("Automatic mode can only be selected from Preoperational.");
            }
            if (action == MachineVisionControlAction.SelectAutomatic && HasActiveErrors())
            {
                throw InvalidState("Resolve and confirm blocking conditions before entering automatic operation.");
            }
            MachineVisionOperationResult outcome;
            try
            {
                outcome = await m_provider.ControlAsync(action, cause, description, ct).ConfigureAwait(false);
            }
            catch
            {
                if (action is MachineVisionControlAction.Halt or MachineVisionControlAction.Stop or
                    MachineVisionControlAction.Abort)
                {
                    await EnterErrorAsync(action + " did not complete.", CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }
            if (!outcome.Succeeded)
            {
                if (action is MachineVisionControlAction.Halt or MachineVisionControlAction.Stop or
                    MachineVisionControlAction.Abort)
                {
                    await EnterErrorAsync(action.ToString(), ct).ConfigureAwait(false);
                }
                return outcome;
            }
            if (action is MachineVisionControlAction.EnableSimulation or MachineVisionControlAction.DisableSimulation)
            {
                m_simulated = action == MachineVisionControlAction.EnableSimulation;
            }
            else if (action == MachineVisionControlAction.SelectAutomatic)
            {
                await TransitionAsync(ControlState, Control.TransitionIds.PreoperationalToInitialized)
                    .ConfigureAwait(false);
                AutomaticState.SetState(m_context.Context, Automatic.StateIds.Initialized);
                await RefreshStepsAsync().ConfigureAwait(false);
                await UpdateReadyStateAsync(ct).ConfigureAwait(false);
            }
            else
            {
                if (m_executionCancellation != null)
                {
                    await m_executionCancellation.CancelAsync().ConfigureAwait(false);
                }
                m_executionGeneration++;
                if (action == MachineVisionControlAction.Reset)
                {
                    await ResetPreparationAsync(ct).ConfigureAwait(false);
                }
                if (action is MachineVisionControlAction.Stop or MachineVisionControlAction.Abort)
                {
                    uint automatic = AutomaticState.GetStateId(AutomaticState.CurrentState!.Id!.Value);
                    uint transition = (automatic, action) switch
                    {
                        (Automatic.StateIds.SingleExecution, MachineVisionControlAction.Stop) =>
                            Automatic.TransitionIds.SingleExecutionToReadyStop,
                        (Automatic.StateIds.SingleExecution, _) => Automatic.TransitionIds.SingleExecutionToReadyAbort,
                        (Automatic.StateIds.ContinuousExecution, MachineVisionControlAction.Stop) =>
                            Automatic.TransitionIds.ContinuousExecutionToReadyStop,
                        (Automatic.StateIds.ContinuousExecution, _) =>
                            Automatic.TransitionIds.ContinuousExecutionToReadyAbort,
                        _ => 0
                    };
                    if (transition != 0)
                    {
                        await TransitionAsync(AutomaticState, transition).ConfigureAwait(false);
                    }
                }
                else
                {
                    uint transition = (current, action) switch
                    {
                        (Control.StateIds.Preoperational, MachineVisionControlAction.Halt) =>
                            Control.TransitionIds.PreoperationalToHalted,
                        (Control.StateIds.Error, MachineVisionControlAction.Halt) =>
                            Control.TransitionIds.ErrorToHalted,
                        (Control.StateIds.Operational, MachineVisionControlAction.Halt) =>
                            Control.TransitionIds.OperationalToHalted,
                        (Control.StateIds.Halted, MachineVisionControlAction.Reset) =>
                            Control.TransitionIds.HaltedToPreoperational,
                        (Control.StateIds.Error, MachineVisionControlAction.Reset) =>
                            Control.TransitionIds.ErrorToPreoperational,
                        (Control.StateIds.Operational, MachineVisionControlAction.Reset) =>
                            Control.TransitionIds.OperationalToPreoperational,
                        _ => 0
                    };
                    if (transition != 0)
                    {
                        await TransitionAsync(ControlState, transition).ConfigureAwait(false);
                    }
                    AutomaticState.SetState(m_context.Context, 0);
                    await RefreshStepsAsync().ConfigureAwait(false);
                }
            }
            Outputs(output, Variant.From(0));
            return outcome;
        }

        private async ValueTask ResetPreparationAsync(CancellationToken ct)
        {
            m_externalReady = false;
            try
            {
                ArrayOf<MachineVisionDocument> recipes = await m_store.ListAsync(MachineVisionDocumentKind.Recipe, ct)
                    .ConfigureAwait(false);
                for (int ii = 0; ii < recipes.Count; ii++)
                {
                    RecipeRecord recipe = RecipeRecord.From(recipes[ii]);
                    if (recipe.Prepared)
                    {
                        await m_store.WriteAsync(recipes[ii] with
                        {
                            Metadata = (recipe with { Prepared = false }).ToVariant()
                        }, ct).ConfigureAwait(false);
                    }
                }
                await RefreshFoldersAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                await EnterErrorAsync("Reset preparation state could not be persisted.", CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }

        private async ValueTask<MachineVisionOperationResult> StartAsync(
            ArrayOf<Variant> args,
            List<Variant> output,
            bool continuous,
            CancellationToken ct)
        {
            if (ControlState.GetStateId(ControlState.CurrentState!.Id!.Value) != Control.StateIds.Operational ||
                HasActiveErrors() ||
                AutomaticState.GetStateId(AutomaticState.CurrentState!.Id!.Value) != Automatic.StateIds.Ready ||
                CurrentStepCompletion().Status != TaskStatus.RanToCompletion)
            {
                throw InvalidState("A vision job requires Ready automatic mode.");
            }
            var job = new MachineVisionJob(
                new JobIdDataType { Id = Guid.NewGuid().ToString("N") },
                Structure<MeasIdDataType>(args, 0),
                Structure<PartIdDataType>(args, 1),
                await SelectRecipeAsync(Structure<RecipeIdExternalDataType>(args, 2),
                    Structure<ProductIdDataType>(args, 3), ct).ConfigureAwait(false),
                Structure<ProductIdDataType>(args, 3),
                await ActiveConfigurationAsync(ct).ConfigureAwait(false),
                Variants(args, 4),
                continuous,
                m_simulated);
            MachineVisionOperationResult outcome = await m_provider.StartJobAsync(job, ct).ConfigureAwait(false);
            if (!outcome.Succeeded)
            {
                return outcome;
            }
            m_executionCancellation?.Dispose();
            m_executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(m_shutdown.Token);
            long generation = ++m_executionGeneration;
            m_activeJob = job;
            await TransitionAsync(AutomaticState, continuous
                ? Automatic.TransitionIds.ReadyToContinuousExecution
                : Automatic.TransitionIds.ReadyToSingleExecution).ConfigureAwait(false);
            await EmitAsync(new JobStartedEventState(null), "Vision job started.",
                notification => notification.JobId!.Value = CloneStructure(job.JobId), AutomaticState)
                .ConfigureAwait(false);
            m_executions.RemoveAll(static task => task.IsCompleted);
            m_executions.Add(RunJobAsync(
                job, generation, CurrentStepCompletion(), m_executionCancellation.Token));
            Outputs(output, Variant.FromStructure(job.JobId), Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async Task RunJobAsync(MachineVisionJob job, long generation, Task ready, CancellationToken ct)
        {
            try
            {
                await Task.Yield();
                await ready.WaitAsync(ct).ConfigureAwait(false);
                await foreach (MachineVisionAcquisition acquisition in m_provider.ExecuteAsync(job, ct)
                    .WithCancellation(ct).ConfigureAwait(false))
                {
                    await EnterRuntimeAsync(ct).ConfigureAwait(false);
                    try
                    {
                        if (m_disposed || generation != m_executionGeneration)
                        {
                            return;
                        }
                        if (acquisition.AcquisitionDone)
                        {
                            await EmitAsync(new AcquisitionDoneEventState(null), "Image acquisition completed.",
                                notification => notification.JobId!.Value = CloneStructure(job.JobId), AutomaticState)
                                .ConfigureAwait(false);
                        }
                        if (acquisition.Result != null)
                        {
                            await PublishResultCoreAsync(job, acquisition, ct).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        m_gate.Release();
                    }
                }
                await EnterRuntimeAsync(ct).ConfigureAwait(false);
                try
                {
                    if (!m_disposed && generation == m_executionGeneration)
                    {
                        await TransitionAsync(AutomaticState, job.Continuous
                            ? Automatic.TransitionIds.ContinuousExecutionToReadyAuto
                            : Automatic.TransitionIds.SingleExecutionToReadyAuto).ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_gate.Release();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancellation belongs to Stop, Abort, Reset, Halt, or disposal.
            }
            catch (Exception exception)
            {
                m_logger.ExecutionFailed(job.JobId.Id ?? string.Empty, exception);
                await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    if (!m_disposed && generation == m_executionGeneration)
                    {
                        await EnterErrorAsync("Acquisition provider failed.", CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_gate.Release();
                }
            }
        }

        private async ValueTask TransitionAsync(
            FiniteStateMachineState machine, uint transitionId, string? reason = null)
        {
            LocalizedText before = machine.CurrentState!.Value;
            NodeId beforeId = machine.CurrentState.Id!.Value;
            ServiceResult result = machine.DoTransition(m_context.Context, transitionId, 0, default, []);
            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
            await machine.ClearChangeMasksAsync(m_context.Context, true).ConfigureAwait(false);
            await EmitTransitionAsync(new StateChangedEventState(null), machine, before, beforeId,
                reason ?? "Vision state changed.").ConfigureAwait(false);
            if (ReferenceEquals(machine, ControlState))
            {
                if (machine.GetStateId(machine.CurrentState.Id.Value) == Control.StateIds.Error)
                {
                    await EmitTransitionAsync(new ErrorEventState(null), machine, before, beforeId,
                        reason ?? "Vision system entered Error.").ConfigureAwait(false);
                }
                else if (machine.GetStateId(beforeId) == Control.StateIds.Error && !HasActiveErrors())
                {
                    await EmitTransitionAsync(new ErrorResolvedEventState(null), machine, before, beforeId,
                        "All blocking vision errors have been resolved.").ConfigureAwait(false);
                }
            }
            if (ReferenceEquals(machine, ControlState) || ReferenceEquals(machine, AutomaticState))
            {
                await RefreshStepsAsync().ConfigureAwait(false);
            }
            if (ReferenceEquals(machine, AutomaticState) &&
                machine.GetStateId(machine.CurrentState.Id.Value) == Automatic.StateIds.Ready &&
                machine.GetStateId(beforeId) is
                    Automatic.StateIds.SingleExecution or Automatic.StateIds.ContinuousExecution)
            {
                MachineVisionJob job = m_activeJob ??
                    throw new InvalidOperationException("An execution transition has no active job.");
                m_activeJob = null;
                await EmitAsync(new ReadyEventState(null), "Vision job finished; ready for another job.",
                    notification => notification.JobId!.Value = CloneStructure(job.JobId), AutomaticState)
                    .ConfigureAwait(false);
            }
            else if (ReferenceEquals(machine, ControlState) &&
                machine.GetStateId(machine.CurrentState.Id.Value) != Control.StateIds.Operational)
            {
                m_activeJob = null;
            }
        }

        private ValueTask EmitTransitionAsync<T>(
            T notification, FiniteStateMachineState machine, LocalizedText before, NodeId beforeId, string message)
            where T : TransitionEventState
        {
            return EmitAsync(notification, message, value =>
            {
                value.CreateOrReplaceFromState(m_context.Context, null).Value = before;
                value.FromState!.CreateOrReplaceId(m_context.Context, null).Value = beforeId;
                value.CreateOrReplaceToState(m_context.Context, null).Value = machine.CurrentState!.Value;
                value.ToState!.CreateOrReplaceId(m_context.Context, null).Value = machine.CurrentState.Id!.Value;
                value.CreateOrReplaceTransition(m_context.Context, null).Value = machine.LastTransition!.Value;
                value.Transition!.CreateOrReplaceId(m_context.Context, null).Value = machine.LastTransition.Id!.Value;
            }, machine);
        }

        private bool Has(MachineVisionFeatures feature)
        {
            return (m_options.Features & feature) != 0;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw InvalidState("The vision system has been disposed.");
            }
        }

        private static ServiceResultException InvalidState(string message)
        {
            return ServiceResultException.Create(StatusCodes.BadInvalidState, "{0}", message);
        }

        private readonly IVisionBuildContext m_context;
        private readonly IMachineVisionOperationsProvider m_provider;
        private readonly MachineVisionSystemOptions m_options;
        private readonly IMachineVisionStore m_store;
        private readonly TimeProvider m_clock;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly List<Task> m_executions = [];
        private CancellationTokenSource? m_executionCancellation;
        private MachineVisionJob? m_activeJob;
        private long m_executionGeneration;
        private bool m_externalReady;
        private bool m_simulated;
        private bool m_disposed;
        private int m_disposeStarted;
    }

    /// <summary>
    /// Source-generated diagnostics for the released vision runtime.
    /// </summary>
    internal static partial class MachineVisionSystemHandleLog
    {
        /// <summary>
        /// Reports a failed acquisition provider.
        /// </summary>
        [LoggerMessage(
            EventId = VisionServerEventIds.MachineVisionRuntime,
            Level = LogLevel.Error,
            Message = "Machine Vision job {JobId} failed in its execution provider.")]
        public static partial void ExecutionFailed(this ILogger logger, string jobId, Exception exception);

        /// <summary>
        /// Reports a failed method without including input payloads.
        /// </summary>
        [LoggerMessage(
            EventId = VisionServerEventIds.MachineVisionRuntime + 1,
            Level = LogLevel.Warning,
            Message = "Machine Vision method {Method} failed.")]
        public static partial void OperationFailed(this ILogger logger, string method, Exception exception);
    }
}
