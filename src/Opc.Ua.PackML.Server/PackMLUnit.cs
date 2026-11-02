/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// One provider-backed PackML unit. The owner registers Node and ModeDataType
    /// and disposes the unit before deleting its address space. Providers are
    /// borrowed and remain owned by the application or dependency container.
    /// </summary>
    public sealed class PackMLUnit : IAsyncDisposable, IConformanceContributor
    {
        /// <summary>
        /// Binds an unregistered unit and its concrete mode enumeration.
        /// </summary>
        private PackMLUnit(
            ISystemContext context,
            PackMLBaseObjectState node,
            DataTypeState modeDataType,
            IPackMLEquipmentProvider provider,
            PackMLUnitOptions options)
        {
            m_context = context;
            Node = node;
            ModeDataType = modeDataType;
            m_provider = provider;
            m_options = options;
            m_modes = options.Modes.Span.ToArray().ToDictionary(mode => mode.Value);
            m_currentMode = options.InitialMode;
            Administration = new PackMLAdministration(
                context, Require(node.Admin), options.TimeProvider, m_modes.Keys.Max());
            m_controller = new PackMLStateMachineController(context, Require(node.BaseStateMachine))
            {
                AutoCompleteActingStates = false
            };
            BindMethods();
        }

        /// <summary>
        /// Gets the generated address-space instance.
        /// </summary>
        public PackMLBaseObjectState Node { get; }

        /// <summary>
        /// Gets the per-unit concrete Enumeration to register with the owner.
        /// </summary>
        public DataTypeState ModeDataType { get; }

        /// <summary>
        /// Gets the local administrative data projector. Remote reset permission
        /// is enforced through RequestAsync; local owners enforce their own policy.
        /// </summary>
        public PackMLAdministration Administration { get; }

        /// <summary>
        /// Gets the active innermost state.
        /// </summary>
        public uint CurrentState => m_controller.CurrentState;

        /// <summary>
        /// Gets the accepted command identity. Equipment must echo this identity
        /// and the expected acting state when reporting completion.
        /// </summary>
        public long PendingRequestId => Interlocked.Read(ref m_pendingRequestId);

        /// <summary>
        /// Gets the implemented PackML conformance units for configured capabilities.
        /// Equipment integration and inherited server facets require deployment validation.
        /// </summary>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var names = new List<string>
                {
                    "PackML Base Functionality", "PackML Base Admin", "PackML Base Status",
                    "PackML State Information", "PackML SetUnitMode", "PackML Set Product",
                    "PackML Machine Speed", "PackML Status UnitModeRequested",
                    "PackML Status UnitModeChangeInProcess", "PackML Status State", "PackML Status Product",
                    "PackML Admin mode statistics", "PackML Admin state Statistics", "PackML Admin ResetTime",
                    "PackML Admin machine speed.",
                    "PackML Equipment Blocked", "PackML Equipment Starved"
                };
                (string Name, uint State)[] commands =
                [
                    ("Abort", PackMLStateNumbers.Aborting), ("Clear", PackMLStateNumbers.Clearing),
                    ("Stop", PackMLStateNumbers.Stopping), ("Reset", PackMLStateNumbers.Resetting),
                    ("Complete", PackMLStateNumbers.Completing), ("Start", PackMLStateNumbers.Starting),
                    ("Unhold", PackMLStateNumbers.Unholding), ("Suspend", PackMLStateNumbers.Suspending),
                    ("Hold", PackMLStateNumbers.Holding), ("Unsuspend", PackMLStateNumbers.Unsuspending)
                ];
                foreach ((string name, uint state) in commands)
                {
                    if (m_modes.Values.Any(mode => mode.States.Count == 0 || mode.States.Span.Contains(state)))
                    {
                        names.Add($"PackML State {name}");
                    }
                }
                if (m_options.TagId is not null)
                {
                    names.Add("PackML Base TagID");
                }
                if (m_options.IncludeVersion)
                {
                    names.Add("PackML Base Version");
                }
                if (m_options.EnableRemoteCommand)
                {
                    names.Add("PackML RemoteCommand");
                    names.Add("PackML Status RemoteParameter");
                }
                if (m_options.MaterialCount > 0)
                {
                    names.Add("PackML SetInterlock");
                    names.Add("PackML Status Interlock");
                }
                PackMLAdministrationExtents extents = m_options.AdministrationExtents;
                if (extents.Alarms > 0)
                {
                    names.Add("PackML Admin Alarm");
                }
                if (extents.AlarmHistory > 0)
                {
                    names.Add("PackML Admin Alarm History");
                }
                if (extents.Warnings > 0)
                {
                    names.Add("PackML Admin Warning");
                }
                if (extents.StopReasons > 0)
                {
                    names.Add("PackML Admin Stop");
                }
                if (extents.ProcessedCounts > 0)
                {
                    names.Add("PackML ProdProcessedCount");
                }
                if (extents.DefectiveCounts > 0)
                {
                    names.Add("PackML ProdDefectiveCount");
                }
                if (extents.ConsumedCounts > 0 && extents.ProcessedCounts > 0 && extents.DefectiveCounts > 0)
                {
                    names.Add("PackML Admin");
                }
                ushort ns = checked((ushort)m_context.NamespaceUris.GetIndex(Namespaces.PackML));
                return names.Select(name => new QualifiedName(name, ns)).ToArray().ToArrayOf();
            }
        }

        /// <summary>
        /// Gets no blanket profile URI; the host must verify inherited server facets.
        /// </summary>
        public ArrayOf<string> ServerProfiles => [];

        /// <summary>
        /// Gets the required, materialized status object.
        /// </summary>
        private PackMLStatusObjectState Status => Require(Node.Status);

        /// <summary>
        /// Creates a generated instance beneath an application-selected parent.
        /// The returned unit is not registered or attached to the parent yet.
        /// </summary>
        public static async ValueTask<PackMLUnit> CreateAsync(
            ISystemContext context,
            NodeState? parent,
            QualifiedName browseName,
            IPackMLEquipmentProvider provider,
            PackMLUnitOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (browseName.IsNull || string.IsNullOrWhiteSpace(browseName.Name) || browseName.NamespaceIndex == 0)
            {
                throw new ArgumentException("An application-qualified unit name is required.", nameof(browseName));
            }
            PackMLUnitOptions captured = (options ?? new PackMLUnitOptions()).Capture();
            PackMLBaseObjectState node = parent is null
                ? context.CreateInstanceOfPackMLBaseObjectType(browseName: browseName)
                : context.CreateInstanceOfPackMLBaseObjectType(parent, browseName);
            Materialize(context, node, captured);
            DataTypeState modeDataType = CreateModeDataType(context, node, captured.Modes);
            PackMLStatusObjectState status = Require(node.Status);
            Require(status.UnitModeCurrent).DataType = modeDataType.NodeId;
            Require(status.UnitSupportedModes).Value = modeDataType.NodeId;
            var unit = new PackMLUnit(context, node, modeDataType, provider, captured);
            try
            {
                if (captured.AdministrationStore is { } store)
                {
                    PackMLAdministrationSnapshot? saved = await store.LoadAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (saved is not null)
                    {
                        unit.Administration.Restore(saved);
                    }
                }
                unit.ApplyMode(captured.InitialMode);
                unit.Set(status.UnitModeRequested, false);
                unit.Set(status.UnitModeChangeInProcess, false);
                unit.Set(status.StateRequested, (int)unit.CurrentState);
                unit.Set(status.MachSpeed, 0f, StatusCodes.BadWaitingForInitialData);
                unit.Set(status.Parameter, Variant.FromStructure(ArrayOf<PackMLDescriptorDataType>.Empty),
                    StatusCodes.BadWaitingForInitialData);
                unit.Set(status.Product, Variant.FromStructure(ArrayOf<PackMLProductDataType>.Empty),
                    StatusCodes.BadWaitingForInitialData);
                if (captured.EnableRemoteCommand)
                {
                    unit.Set(status.RemoteParameter,
                        Variant.FromStructure(ArrayOf<PackMLRemoteInterfaceDataType>.Empty),
                        StatusCodes.BadWaitingForInitialData);
                }
                unit.Administration.Apply(new PackMLAdministrationData
                {
                    SourceTimestamp = captured.TimeProvider.GetUtcNow().UtcDateTime
                }, captured.AdministrationExtents);
                await unit.RefreshAsync(cancellationToken).ConfigureAwait(false);
                return unit;
            }
            catch
            {
                await unit.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Authorizes and submits an operation. Good reports acceptance, not
        /// completion. Requests are serialized with snapshots and completion.
        /// </summary>
        public ValueTask<ServiceResult> RequestAsync(
            ISystemContext context,
            PackMLRequest request,
            CancellationToken cancellationToken = default)
        {
            return RequestCoreAsync(context, request, null, cancellationToken);
        }

        /// <summary>
        /// Serializes the exact wire method's eligibility with provider execution.
        /// </summary>
        private async ValueTask<ServiceResult> RequestCoreAsync(
            ISystemContext context,
            PackMLRequest request,
            MethodState? requiredMethod,
            CancellationToken cancellationToken)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            using PackMLLifetime.Lease pending = await m_lifetime.EnterAsync(cancellationToken).ConfigureAwait(false);
            pending.Token.ThrowIfCancellationRequested();
            if (requiredMethod is not null && !requiredMethod.Executable)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState, "The command's state machine is inactive.");
            }
            ServiceResult permitted = Validate(request);
            if (ServiceResult.IsBad(permitted))
            {
                return permitted;
            }
            request = request with
            {
                RequestId = checked(++m_nextRequestId),
                CurrentMode = m_currentMode,
                CurrentState = CurrentState,
                Products = request.Products.ConvertAll(product => (PackMLProductDataType)product.Clone()),
                Parameters = request.Parameters.ConvertAll(parameter => (PackMLDescriptorDataType)parameter.Clone()),
                RemoteInterfaces = request.RemoteInterfaces.ConvertAll(
                    remote => (PackMLRemoteInterfaceDataType)remote.Clone())
            };
            ServiceResult authorization = await m_provider.AuthorizeAsync(context, request, pending.Token)
                .ConfigureAwait(false);
            if (ServiceResult.IsUncertain(authorization))
            {
                throw new InvalidOperationException("PackML authorization must return a Good or Bad status.");
            }
            if (!ServiceResult.IsGood(authorization))
            {
                return authorization;
            }
            Variant previousStateRequest = Require(Status.StateRequested).WrappedValue;
            SetRequestIndicators(request, true);
            ServiceResult accepted;
            try
            {
                accepted = await m_provider.ExecuteAsync(request, pending.Token).ConfigureAwait(false);
            }
            catch
            {
                RestoreRequestIndicators(request, previousStateRequest);
                throw;
            }
            if (!ServiceResult.IsGood(accepted))
            {
                RestoreRequestIndicators(request, previousStateRequest);
                if (ServiceResult.IsUncertain(accepted))
                {
                    throw new InvalidOperationException(
                        "PackML equipment acceptance must return a Good or Bad status.");
                }
                return accepted;
            }
            ApplyAccepted(request);
            await SaveAdministrationAsync(pending.Token).ConfigureAwait(false);
            return accepted;
        }

        /// <summary>
        /// Reports equipment completion for the current accepted command.
        /// A stale identity or a different state is rejected without transition.
        /// </summary>
        public async ValueTask<ServiceResult> CompleteActingStateAsync(
            long requestId,
            uint expectedState,
            CancellationToken cancellationToken = default)
        {
            using PackMLLifetime.Lease pending = await m_lifetime.EnterAsync(cancellationToken).ConfigureAwait(false);
            pending.Token.ThrowIfCancellationRequested();
            if (requestId == 0 || requestId != m_pendingRequestId || CurrentState != expectedState)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "The equipment completion is stale.");
            }
            ServiceResult result = m_controller.CompleteActingState();
            PublishState();
            await SaveAdministrationAsync(pending.Token).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Acquires provider data and publishes its quality and acquisition time.
        /// A requested mode is committed only when the equipment confirms it.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            using PackMLLifetime.Lease pending = await m_lifetime.EnterAsync(cancellationToken).ConfigureAwait(false);
            pending.Token.ThrowIfCancellationRequested();
            PackMLEquipmentSnapshot snapshot = await m_provider.ReadAsync(pending.Token).ConfigureAwait(false);
            ValidateSnapshot(snapshot);
            if (StatusCode.IsGood(snapshot.StatusCode))
            {
                if (m_requestedMode is int requested && snapshot.CurrentMode == requested)
                {
                    ApplyMode(requested);
                    m_requestedMode = null;
                    Set(Status.UnitModeRequested, false);
                    Set(Status.UnitModeChangeInProcess, false);
                }
                else if (snapshot.CurrentMode != m_currentMode)
                {
                    throw new InvalidOperationException("The provider changed mode without an accepted request.");
                }
            }
            Set(Status.CurMachSpeed, snapshot.CurrentMachineSpeed, snapshot.StatusCode, snapshot.SourceTimestamp);
            Set(Status.EquipmentBlocked, snapshot.EquipmentBlocked, snapshot.StatusCode, snapshot.SourceTimestamp);
            Set(Status.EquipmentStarved, snapshot.EquipmentStarved, snapshot.StatusCode, snapshot.SourceTimestamp);
            if (m_options.EnableRemoteCommand)
            {
                Set(Status.RemoteParameter, Variant.FromStructure(snapshot.RemoteParameters, true),
                    snapshot.StatusCode, snapshot.SourceTimestamp);
            }
            if (m_options.MaterialCount > 0)
            {
                Set(Status.MaterialInterlock, Variant.From(snapshot.MaterialReadiness.Span.ToArray().ToArrayOf()),
                    snapshot.StatusCode, snapshot.SourceTimestamp);
                bool interlocked = false;
                foreach (bool ready in snapshot.MaterialReadiness)
                {
                    interlocked |= !ready;
                }
                Set(Status.MaterialInterlocked, interlocked, snapshot.StatusCode, snapshot.SourceTimestamp);
            }
            Set(Status.UnitModeCurrent, m_currentMode, snapshot.StatusCode, snapshot.SourceTimestamp);
            PublishState();
            Administration.Refresh();
        }

        /// <summary>
        /// Cancels pending provider work and drains the unit before teardown.
        /// Does not dispose the application-owned equipment provider.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await m_lifetime.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Projects a coherent administration sample obtained from the existing
        /// alarm/equipment owner and its configured history storage.
        /// </summary>
        public async ValueTask PublishAdministrationAsync(
            PackMLAdministrationData data,
            CancellationToken cancellationToken = default)
        {
            using PackMLLifetime.Lease pending = await m_lifetime.EnterAsync(cancellationToken).ConfigureAwait(false);
            pending.Token.ThrowIfCancellationRequested();
            Administration.Apply(data, m_options.AdministrationExtents);
            await SaveAdministrationAsync(pending.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Persists an explicit timer checkpoint. Provider failures are propagated
        /// without silently reverting to memory or replaying equipment commands.
        /// </summary>
        public async ValueTask CheckpointAsync(CancellationToken cancellationToken = default)
        {
            using PackMLLifetime.Lease pending = await m_lifetime.EnterAsync(cancellationToken).ConfigureAwait(false);
            await SaveAdministrationAsync(pending.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Commits the configured timer snapshot after accepted changes.
        /// </summary>
        private ValueTask SaveAdministrationAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_options.AdministrationStore is { } store)
            {
                return store.SaveAsync(Administration.Capture(), cancellationToken);
            }
            Administration.Refresh();
            return default;
        }

        /// <summary>
        /// Validates requests before invoking application equipment code.
        /// </summary>
        private ServiceResult Validate(PackMLRequest request)
        {
#if NET8_0_OR_GREATER
            bool defined = Enum.IsDefined(request.Operation);
#else
            bool defined = Enum.IsDefined(typeof(PackMLOperation), request.Operation);
#endif
            if (!defined)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument, "The PackML operation is unknown.");
            }
            if (ContainsNull(request.Products) || ContainsNull(request.Parameters) ||
                ContainsNull(request.RemoteInterfaces))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument, "PackML structure arrays cannot contain null entries.");
            }
            if (m_requestedMode.HasValue)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "A mode change is in progress.");
            }
            switch (request.Operation)
            {
                case PackMLOperation.Command:
                    if (!m_controller.CanExecute(request.Command))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidState, "The command is not available.");
                    }
                    break;
                case PackMLOperation.SetUnitMode:
                    if (!m_modes.ContainsKey(request.Mode))
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadInvalidArgument, "The requested mode is not configured.");
                    }
                    if (!m_options.ModeChangeStates.Span.Contains(CurrentState))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidState, "A mode change is not allowed here.");
                    }
                    break;
                case PackMLOperation.SetMachSpeed:
                    if (float.IsNaN(request.MachineSpeed) || float.IsInfinity(request.MachineSpeed) ||
                        request.MachineSpeed < 0 || request.MachineSpeed > m_options.MaximumSpeed)
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadInvalidArgument, "The requested speed is out of range.");
                    }
                    break;
                case PackMLOperation.RemoteCommand when !m_options.EnableRemoteCommand:
                case PackMLOperation.SetInterlock when m_options.MaterialCount == 0:
                    return ServiceResult.Create(
                        StatusCodes.BadNotSupported, "The optional capability is not configured.");
                case PackMLOperation.SetInterlock:
                    if (request.InterlockId < 0 || request.InterlockId >= m_options.MaterialCount)
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadInvalidArgument, "The material interlock is unknown.");
                    }
                    break;
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Commits accepted setpoints without fabricating actual equipment data.
        /// </summary>
        private void ApplyAccepted(PackMLRequest request)
        {
            switch (request.Operation)
            {
                case PackMLOperation.Command:
                    ServiceResult result = m_controller.Execute(request.Command, request.Parameters);
                    if (ServiceResult.IsBad(result))
                    {
                        throw new ServiceResultException(result);
                    }
                    m_pendingRequestId = request.RequestId;
                    PublishState();
                    break;
                case PackMLOperation.SetUnitMode:
                    m_requestedMode = request.Mode;
                    break;
                case PackMLOperation.SetMachSpeed:
                    Set(Status.MachSpeed, request.MachineSpeed);
                    break;
                case PackMLOperation.SetProduct:
                    Set(Status.Product, Variant.FromStructure(request.Products, true));
                    break;
                case PackMLOperation.SetParameter:
                    Set(Status.Parameter, Variant.FromStructure(request.Parameters, true));
                    break;
                case PackMLOperation.ResetCounters:
                    Administration.ResetCounters();
                    break;
            }
        }

        /// <summary>
        /// Updates indicators while authorization/execution is pending.
        /// </summary>
        private void SetRequestIndicators(PackMLRequest request, bool pending)
        {
            if (request.Operation == PackMLOperation.SetUnitMode)
            {
                Set(Status.UnitModeRequested, pending);
                Set(Status.UnitModeChangeInProcess, pending);
            }
            if (request.Operation == PackMLOperation.Command)
            {
                Set(Status.StateRequested, pending ? (int)RequestedState(request.Command) : (int)CurrentState);
                Set(Status.StateChangeInProcess, pending);
            }
        }

        /// <summary>
        /// Restores an existing acting sequence when a superseding request is rejected.
        /// </summary>
        private void RestoreRequestIndicators(PackMLRequest request, Variant previousStateRequest)
        {
            SetRequestIndicators(request, false);
            if (request.Operation == PackMLOperation.Command)
            {
                Set(Status.StateRequested, previousStateRequest);
                PublishState();
            }
        }

        /// <summary>
        /// Maps commands to the stable target of their acting-state sequence.
        /// </summary>
        private static uint RequestedState(PackMLCommand command)
        {
            return command switch
            {
                PackMLCommand.Abort => PackMLStateNumbers.Aborted,
                PackMLCommand.Clear or PackMLCommand.Stop => PackMLStateNumbers.Stopped,
                PackMLCommand.Reset => PackMLStateNumbers.Idle,
                PackMLCommand.Hold => PackMLStateNumbers.Held,
                PackMLCommand.Suspend => PackMLStateNumbers.Suspended,
                PackMLCommand.ToComplete => PackMLStateNumbers.Complete,
                _ => PackMLStateNumbers.Execute
            };
        }

        /// <summary>
        /// Publishes acting-state progress independently of method acceptance.
        /// </summary>
        private void PublishState()
        {
            Administration.RecordState(m_currentMode, CurrentState);
            bool acting = CurrentState is PackMLStateNumbers.Aborting or PackMLStateNumbers.Clearing or
                PackMLStateNumbers.Stopping or PackMLStateNumbers.Resetting or PackMLStateNumbers.Starting or
                PackMLStateNumbers.Holding or PackMLStateNumbers.Unholding or PackMLStateNumbers.Suspending or
                PackMLStateNumbers.Unsuspending or PackMLStateNumbers.Completing;
            Set(Status.StateChangeInProcess, acting);
            if (!acting)
            {
                m_pendingRequestId = 0;
            }
        }

        /// <summary>
        /// Applies a confirmed mode and its initial state.
        /// </summary>
        private void ApplyMode(int mode)
        {
            PackMLMode definition = m_modes[mode];
            ArrayOf<uint> states = definition.States.Count == 0
                ? Enumerable.Range(1, (int)PackMLStateNumbers.Cleared).Select(state => (uint)state).ToArrayOf()
                : definition.States;
            m_controller.ConfigureStates(states, definition.InitialState);
            m_currentMode = mode;
            Set(Status.UnitModeCurrent, mode);
            PublishState();
        }

        /// <summary>
        /// Rejects inconsistent provider snapshots before publishing any field.
        /// </summary>
        private void ValidateSnapshot(PackMLEquipmentSnapshot snapshot)
        {
            if (snapshot is null || !m_modes.ContainsKey(snapshot.CurrentMode) ||
                snapshot.MaterialReadiness.Count != m_options.MaterialCount ||
                ContainsNull(snapshot.RemoteParameters) ||
                float.IsNaN(snapshot.CurrentMachineSpeed) || float.IsInfinity(snapshot.CurrentMachineSpeed) ||
                snapshot.SourceTimestamp.IsNull)
            {
                throw new InvalidOperationException("The equipment snapshot violates the configured PackML contract.");
            }
        }

        /// <summary>
        /// Sets a generated variable with quality and time, then notifies monitors.
        /// </summary>
        private void Set(
            BaseVariableState? variable,
            Variant value,
            StatusCode status = default,
            DateTimeUtc timestamp = default)
        {
            if (variable is null)
            {
                throw new InvalidOperationException("A configured PackML field is missing.");
            }
            variable.WrappedValue = value;
            variable.StatusCode = status;
            variable.Timestamp = timestamp.IsNull ? m_options.TimeProvider.GetUtcNow().UtcDateTime : timestamp;
            variable.ClearChangeMasks(m_context, false);
        }

        /// <summary>
        /// Binds generated typed method callbacks and overrides controller wire
        /// bindings with the asynchronous equipment/authorization path.
        /// </summary>
        private void BindMethods()
        {
            Require(Node.SetUnitMode).OnCallAsync = async (context, method, objectId, mode, ct) =>
                new SetUnitModeMethodStateResult
                {
                    ServiceResult = await RequestAsync(context, new PackMLRequest
                    {
                        Operation = PackMLOperation.SetUnitMode,
                        Mode = mode
                    }, ct).ConfigureAwait(false)
                };
            Require(Node.SetMachSpeed).OnCallAsync = async (context, method, objectId, speed, ct) =>
                new SetMachSpeedMethodStateResult
                {
                    ServiceResult = await RequestAsync(context, new PackMLRequest
                    {
                        Operation = PackMLOperation.SetMachSpeed,
                        MachineSpeed = speed
                    }, ct).ConfigureAwait(false)
                };
            Require(Node.SetProduct).OnCallAsync = async (context, method, objectId, products, ct) =>
                new SetProductMethodStateResult
                {
                    ServiceResult = await RequestAsync(context, new PackMLRequest
                    {
                        Operation = PackMLOperation.SetProduct,
                        Products = products
                    }, ct).ConfigureAwait(false)
                };
            Require(Node.SetParameter).OnCallAsync = async (context, method, objectId, parameters, ct) =>
                new SetParameterMethodStateResult
                {
                    ServiceResult = await RequestAsync(context, new PackMLRequest
                    {
                        Operation = PackMLOperation.SetParameter,
                        Parameters = parameters
                    }, ct).ConfigureAwait(false)
                };
            if (Node.RemoteCommand is { } remote)
            {
                remote.OnCallAsync = async (context, method, objectId, interfaces, ct) =>
                    new RemoteCommandMethodStateResult
                    {
                        ServiceResult = await RequestAsync(context, new PackMLRequest
                        {
                            Operation = PackMLOperation.RemoteCommand,
                            RemoteInterfaces = interfaces
                        }, ct).ConfigureAwait(false)
                    };
            }
            if (Node.SetInterlock is { } interlock)
            {
                interlock.OnCallAsync = async (context, method, objectId, id, state, ct) =>
                    new SetInterlockMethodStateResult
                    {
                        ServiceResult = await RequestAsync(context, new PackMLRequest
                        {
                            Operation = PackMLOperation.SetInterlock,
                            InterlockId = id,
                            InterlockState = state
                        }, ct).ConfigureAwait(false)
                    };
            }
            PackMLBaseStateMachineState root = Require(Node.BaseStateMachine);
            PackMLMachineStateMachineState machine = Require(root.MachineState);
            PackMLExecuteStateMachineState execute = Require(machine.ExecuteState);
            Bind(root.Abort, PackMLCommand.Abort);
            Bind(root.Clear, PackMLCommand.Clear);
            Bind(machine.Stop, PackMLCommand.Stop);
            Bind(machine.Reset, PackMLCommand.Reset);
            Bind(execute.Reset, PackMLCommand.Reset);
            Bind(execute.Hold, PackMLCommand.Hold);
            Bind(execute.Unhold, PackMLCommand.Unhold);
            Bind(execute.Suspend, PackMLCommand.Suspend);
            Bind(execute.Unsuspend, PackMLCommand.Unsuspend);
            Bind(execute.ToComplete, PackMLCommand.ToComplete);
            StartMethodState start = Require(execute.Start);
            start.OnCallAsync = async (context, method, objectId, parameters, ct) =>
                new StartMethodStateResult
                {
                    ServiceResult = await RequestCoreAsync(context, new PackMLRequest
                    {
                        Operation = PackMLOperation.Command,
                        Command = PackMLCommand.Start,
                        Parameters = parameters
                    }, method, ct).ConfigureAwait(false)
                };
            start.OnCall = null;
        }

        /// <summary>
        /// Binds a no-argument state command without synchronous waits.
        /// </summary>
        private void Bind(MethodState? node, PackMLCommand command)
        {
            MethodState method = Require(node);
            method.OnCallMethod2Async = (context, called, objectId, inputs, outputs, ct) =>
                RequestCoreAsync(context,
                    new PackMLRequest { Operation = PackMLOperation.Command, Command = command }, called, ct);
            method.OnCallMethod2 = null;
        }

        /// <summary>
        /// Materializes only optional operations that the unit explicitly enables.
        /// </summary>
        private static void Materialize(ISystemContext context, PackMLBaseObjectState node, PackMLUnitOptions options)
        {
            node.EventNotifier = EventNotifiers.SubscribeToEvents;
            if (options.TagId is { } tag)
            {
                node.AddTagID(context);
                Require(node.TagID).Value = tag;
            }
            if (options.IncludeVersion)
            {
                node.AddPackMLVersion(context);
            }
            PackMLStatusObjectState status = Require(node.Status);
            AnalogItemState<float> setpoint = Require(status.MachSpeed);
            AnalogItemState<float> actual = Require(status.CurMachSpeed);
            setpoint.AddEngineeringUnits(context);
            actual.AddEngineeringUnits(context);
            var speedUnits = new EUInformation
            {
                NamespaceUri = "urn:opcfoundation:PackML:units",
                UnitId = 1,
                DisplayName = new LocalizedText("packages/min"),
                Description = new LocalizedText("Primary packages per minute")
            };
            Require(setpoint.EngineeringUnits).Value = speedUnits;
            Require(actual.EngineeringUnits).Value = (EUInformation)speedUnits.Clone();
            Require(setpoint.EURange).Value = new Ua.Range(0, options.MaximumSpeed);
            Require(actual.EURange).Value = new Ua.Range(0, options.MaximumSpeed);
            status.AddUnitModeRequested(context);
            status.AddUnitModeChangeInProcess(context);
            status.AddStateRequested(context);
            status.AddStateChangeInProcess(context);
            status.AddProduct(context);
            status.AddParameter(context);
            if (options.EnableRemoteCommand)
            {
                node.AddRemoteCommand(context);
                status.AddRemoteParameter(context);
            }
            if (options.MaterialCount > 0)
            {
                node.AddSetInterlock(context);
                status.AddMaterialInterlock(context);
                status.AddMaterialInterlocked(context);
            }
            PackMLBaseStateMachineState root = Require(node.BaseStateMachine);
            root.AddAbort(context);
            root.AddClear(context);
            PackMLMachineStateMachineState machine = Require(root.MachineState);
            machine.AddStop(context);
            machine.AddReset(context);
            PackMLExecuteStateMachineState execute = Require(machine.ExecuteState);
            node.AddNotifier(context, Ua.ReferenceTypeIds.HasEventSource, false, root);
            root.AddNotifier(context, Ua.ReferenceTypeIds.HasEventSource, true, node);
            root.AddNotifier(context, Ua.ReferenceTypeIds.HasEventSource, false, machine);
            machine.AddNotifier(context, Ua.ReferenceTypeIds.HasEventSource, true, root);
            machine.AddNotifier(context, Ua.ReferenceTypeIds.HasEventSource, false, execute);
            execute.AddNotifier(context, Ua.ReferenceTypeIds.HasEventSource, true, machine);
            execute.AddStart(context);
            execute.AddHold(context);
            execute.AddUnhold(context);
            execute.AddSuspend(context);
            execute.AddUnsuspend(context);
            execute.AddToComplete(context);
            execute.AddReset(context);
            PackMLAdminObjectState admin = Require(node.Admin);
            admin.AddParameter(context);
            admin.AddAlarm(context);
            admin.AddAlarmExtent(context);
            admin.AddAlarmHistory(context);
            admin.AddAlarmHistoryExtent(context);
            admin.AddWarning(context);
            admin.AddWarningExtent(context);
            admin.AddStopReason(context);
            admin.AddStopReasonExtent(context);
            admin.AddModeCurrentTime(context);
            admin.AddModeCumulativeTime(context);
            admin.AddStateCurrentTime(context);
            admin.AddStateCumulativeTime(context);
            admin.AddProdConsumedCount(context);
            admin.AddProdProcessedCount(context);
            admin.AddProdDefectiveCount(context);
            admin.AddAccTimeSinceReset(context);
            admin.AddMachDesignSpeed(context);
            ConfigureReadOnlyTree(context, status, default);
            ConfigureReadOnlyTree(context, admin, options.AdministrationRolePermissions);
        }

        /// <summary>
        /// Applies access restrictions to descendants even when addressed directly.
        /// </summary>
        private static void ConfigureReadOnlyTree(
            ISystemContext context,
            BaseInstanceState node,
            ArrayOf<RolePermissionType> permissions)
        {
            if (!permissions.IsNull)
            {
                node.RolePermissions = permissions;
            }
            if (node is BaseVariableState variable)
            {
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                ConfigureReadOnlyTree(context, child, permissions);
            }
        }

        /// <summary>
        /// Rejects an incomplete generated instance before accessing a required child.
        /// </summary>
        /// <typeparam name="T">The required generated node type.</typeparam>
        private static T Require<T>(T? node) where T : NodeState
        {
            return node ?? throw new InvalidOperationException("A required PackML model child is missing.");
        }

        /// <summary>
        /// Checks structure arrays before cloning or publishing their entries.
        /// </summary>
        /// <typeparam name="T">The structure element type.</typeparam>
        private static bool ContainsNull<T>(ArrayOf<T> values) where T : class
        {
            foreach (T value in values)
            {
                if (value is null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Defines an instance-specific mode enumeration without modifying the
        /// PackML namespace or registering a duplicate model producer.
        /// </summary>
        private static DataTypeState CreateModeDataType(
            ISystemContext context,
            PackMLBaseObjectState unit,
            ArrayOf<PackMLMode> modes)
        {
            var fields = new List<EnumField>();
            var values = new List<EnumValueType>();
            foreach (PackMLMode mode in modes)
            {
                fields.Add(new EnumField
                {
                    Name = mode.Name,
                    Value = mode.Value,
                    DisplayName = new LocalizedText(mode.Name)
                });
                values.Add(new EnumValueType { Value = mode.Value, DisplayName = new LocalizedText(mode.Name) });
            }
            var type = new DataTypeState
            {
                NodeId = new NodeId($"{unit.NodeId}.SupportedModes", unit.BrowseName.NamespaceIndex),
                BrowseName = new QualifiedName($"{unit.BrowseName.Name}Modes", unit.BrowseName.NamespaceIndex),
                DisplayName = new LocalizedText($"{unit.BrowseName.Name} modes"),
                IsAbstract = false,
                SuperTypeId = Ua.DataTypeIds.Enumeration,
                DataTypeDefinition = new ExtensionObject(new EnumDefinition { Fields = fields.ToArrayOf() })
            };
            type.AddReference(Ua.ReferenceTypeIds.HasSubtype, true, Ua.DataTypeIds.Enumeration);
            var enumValues = new PropertyState(type)
            {
                NodeId = new NodeId($"{unit.NodeId}.SupportedModes.EnumValues", unit.BrowseName.NamespaceIndex),
                BrowseName = new QualifiedName(Ua.BrowseNames.EnumValues),
                DisplayName = new LocalizedText(Ua.BrowseNames.EnumValues),
                ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                TypeDefinitionId = Ua.VariableTypeIds.PropertyType,
                DataType = Ua.DataTypeIds.EnumValueType,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = Variant.FromStructure(values.ToArrayOf())
            };
            type.AddChild(enumValues);
            return type;
        }

        /// <summary>The owning address-space context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The borrowed equipment and authorization provider.</summary>
        private readonly IPackMLEquipmentProvider m_provider;

        /// <summary>The captured unit configuration.</summary>
        private readonly PackMLUnitOptions m_options;

        /// <summary>The captured modes indexed by concrete enumeration value.</summary>
        private readonly Dictionary<int, PackMLMode> m_modes;

        /// <summary>The existing controller used with explicit equipment completion.</summary>
        private readonly PackMLStateMachineController m_controller;

        /// <summary>Serializes unit operations and drains them during shutdown.</summary>
        private readonly PackMLLifetime m_lifetime = new();

        /// <summary>The last allocated operation identity.</summary>
        private long m_nextRequestId;

        /// <summary>The accepted acting-state operation awaiting equipment completion.</summary>
        private long m_pendingRequestId;

        /// <summary>The last equipment-confirmed mode.</summary>
        private int m_currentMode;

        /// <summary>The accepted mode request awaiting an equipment snapshot.</summary>
        private int? m_requestedMode;
    }
}
