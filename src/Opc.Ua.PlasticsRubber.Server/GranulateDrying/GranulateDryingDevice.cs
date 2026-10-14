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
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// A live OPC 40082-6 instance with serialized authorized commands and atomic committed values.
    /// Provider ownership remains external; disposing this handle cancels requests and releases bindings.
    /// </summary>
    public sealed partial class GranulateDryingDevice : IAsyncDisposable
    {
        /// <summary>
        /// Creates a handle around a generated, not-yet-registered graph.
        /// </summary>
        internal GranulateDryingDevice(
            GranulateDryingRuntime runtime,
            GranulateDryingDeviceState state,
            GranulateDryingDeviceOptions options,
            IGranulateDryingProvider provider,
            IGranulateDryingAuthorization authorization,
            GranulateDryingCommonProviders commonProviders)
        {
            m_runtime = runtime;
            State = state;
            Options = options;
            m_provider = provider;
            m_authorization = authorization;
            m_commonProviders = commonProviders;
            m_cleanup = new Lazy<Task>(DisposeCoreAsync);
            m_targets.Add(GranulateDryingTarget.Device, state);
            for (int ii = 1; ii <= options.Bins.Count; ii++)
            {
                m_targets.Add(GranulateDryingTarget.Bin(ii), GranulateDryingNodes.Require<BinState>(
                    Context, state.Dryer!.Bins!, GranulateDryingNodes.Numbered("Bin", ii)));
            }
            for (int ii = 1; ii <= options.DesiccantBeds.Count; ii++)
            {
                m_targets.Add(GranulateDryingTarget.Bed(ii), GranulateDryingNodes.Require<DesiccantBedState>(
                    Context, state.Dryer!.DesiccantBeds!, GranulateDryingNodes.Numbered("DesiccantBed", ii)));
            }
            for (int ii = 1; ii <= options.Maintenance.Count; ii++)
            {
                m_targets.Add(GranulateDryingTarget.Maintenance(ii), GranulateDryingNodes.Require<GDDMaintenanceState>(
                    Context, state.Operation!.MaintenanceInformation!, GranulateDryingNodes.Numbered("Maintenance",
                        ii)));
            }
        }

        /// <summary>
        /// Gets the generated type-faithful instance. Read committed values through ReadCommittedValue,
        /// not a variable's transient write cache.
        /// </summary>
        public GranulateDryingDeviceState State { get; }

        /// <summary>
        /// Gets the application-owned instance identifier.
        /// </summary>
        public NodeId NodeId => State.NodeId;

        /// <summary>
        /// Gets the immutable capability selection.
        /// </summary>
        public GranulateDryingDeviceOptions Options { get; }

        /// <summary>
        /// Gets the latest committed authority revision.
        /// </summary>
        public ulong Revision => Volatile.Read(ref m_current).Revision;

        /// <summary>
        /// Gets the current control owner, or NodeId.Null when no session owns control.
        /// </summary>
        public NodeId ControlOwner
        {
            get
            {
                lock (m_controlLock)
                {
                    return m_controlOwner;
                }
            }
        }

        /// <summary>
        /// Gets the context that owns this device.
        /// </summary>
        private ISystemContext Context => m_runtime.Context;

        /// <summary>
        /// Gets a bin by its one-based standard placeholder number.
        /// </summary>
        public BinState Bin(int number) => (BinState)Target(GranulateDryingTarget.Bin(number));

        /// <summary>
        /// Gets a bed by its one-based standard placeholder number.
        /// </summary>
        public DesiccantBedState Bed(int number) => (DesiccantBedState)Target(GranulateDryingTarget.Bed(number));

        /// <summary>
        /// Gets a maintenance category by its one-based standard placeholder number.
        /// </summary>
        public GDDMaintenanceState Maintenance(int number) =>
            (GDDMaintenanceState)Target(GranulateDryingTarget.Maintenance(number));

        /// <summary>
        /// Resolves the registered device through the shared builder for AMB registration and composition.
        /// </summary>
        public INodeBuilder<GranulateDryingDeviceState> AsNode()
        {
            ThrowIfDisposed();
            if (!m_registered)
            {
                throw Error(StatusCodes.BadInvalidState, "The device has not been registered.");
            }
            return m_runtime.Manager.CreateMachineryBuildContext(new MachineryServerOptions
            {
                Parts = MachineryParts.BuildingBlocks,
                InstanceNamespaceUri = Context.NamespaceUris.GetString(m_runtime.InstanceNamespaceIndex) ??
                    throw Error(StatusCodes.BadConfigurationError, "The device instance namespace is missing.")
            }).Nodes.Node<GranulateDryingDeviceState>(NodeId);
        }

        /// <summary>
        /// Reads an absolute committed value with its original source status and timestamp.
        /// Unknown or unselected members are reported as BadNotSupported.
        /// </summary>
        public DataValue ReadCommittedValue(GranulateDryingTarget target, string member)
        {
            ThrowIfDisposed();
            ValueBinding binding = RequiredValue(target, member);
            return Volatile.Read(ref m_current).Values[binding.Node.NodeId].Copy();
        }

        /// <summary>
        /// Applies a validated absolute commit atomically. Older revisions or contradictory repeats fail.
        /// All values are checked before any process value changes.
        /// </summary>
        public async ValueTask PublishAsync(
            GranulateDryingCommit commit,
            CancellationToken cancellationToken = default)
        {
            if (commit == null)
            {
                throw new ArgumentNullException(nameof(commit));
            }
            using GranulateDryingOperationLifetime.Lease pending = Pending(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await ApplyCommitAsync(commit, null, pending.Token).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases this session's control lease without changing hardware state or the persisted remote setting.
        /// Applications can expose their own authorized orchestration surface; no extra standard method is invented.
        /// A closing UA session releases its lease automatically.
        /// </summary>
        public async ValueTask ReleaseControlAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            using GranulateDryingOperationLifetime.Lease pending = Pending(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                NodeId session = SessionId(context);
                if (session.IsNull || !m_authorization.CanControl(context))
                {
                    throw Error(StatusCodes.BadUserAccessDenied,
                        "Only an authorized control owner may release control.");
                }
                lock (m_controlLock)
                {
                    if (m_controlOwner != session)
                    {
                        throw Error(StatusCodes.BadUserAccessDenied, "This session does not own device control.");
                    }
                    m_controlOwner = NodeId.Null;
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Cancels and drains operations before releasing files, alarms, and common binders.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            Volatile.Write(ref m_disposed, true);
            List<Exception>? failures = null;
            try
            {
                await m_lifetime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures = [failure];
            }
            try
            {
                await m_cleanup.Value.ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            finally
            {
                if (Interlocked.Exchange(ref m_gateDisposed, 1) == 0)
                {
                    m_gate.Dispose();
                }
            }
            if (failures != null)
            {
                throw new AggregateException("Granulate drying device cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Binds generated states and reads the authority before address-space publication.
        /// </summary>
        internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            foreach (KeyValuePair<GranulateDryingTarget, BaseObjectState> pair in m_targets)
            {
                IndexValues(pair.Key, pair.Value, string.Empty);
            }
            BindOperations();
            BindFiles();
            await BindCommonAsync(cancellationToken).ConfigureAwait(false);
            BindUnsupportedMethods();
            GranulateDryingCommit initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (initial == null || !initial.RemoteControlEnabled.HasValue || initial.Errors.IsNull)
            {
                throw Error(StatusCodes.BadConfigurationError,
                    "Initial state must supply persisted remote-control enablement and the complete error list.");
            }
            await ApplyCommitAsync(initial, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Enables event publication after the graph has been registered.
        /// </summary>
        internal void Registered()
        {
            m_registered = true;
        }

        /// <summary>
        /// Opens the selected standard target.
        /// </summary>
        private BaseObjectState Target(GranulateDryingTarget target)
        {
            return m_targets.TryGetValue(target, out BaseObjectState? node)
                ? node
                : throw Error(StatusCodes.BadNotSupported, "The requested standard target is not configured.");
        }

        /// <summary>
        /// Resolves one selected process value.
        /// </summary>
        private ValueBinding RequiredValue(GranulateDryingTarget target, string member)
        {
            if (string.IsNullOrWhiteSpace(member))
            {
                throw new ArgumentException("A standard member path is required.", nameof(member));
            }
            return m_values.TryGetValue((target, member), out ValueBinding? binding)
                ? binding
                : throw Error(StatusCodes.BadNotSupported, "The requested process value is absent or is metadata.");
        }

        /// <summary>
        /// Serves maintenance instructions with the existing session-owned FileType implementation.
        /// </summary>
        private void BindFiles()
        {
            for (int ii = 1; ii <= Options.Maintenance.Count; ii++)
            {
                FileState? file = Maintenance(ii).MaintenanceInstructions;
                if (file != null)
                {
                    m_resources.Add(m_runtime.FileBinder.Bind(
                        file, Context, Options.Maintenance[ii - 1].Instructions,
                        maximumBytes: Options.MaximumInstructionBytes));
                    foreach (NodeState node in GranulateDryingNodes.Walk(Context, file))
                    {
                        if (node is MethodState method)
                        {
                            method.OnReadUserExecutable = ReadReadOnlyExecutable;
                            m_methods.Add(method);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Prevents an unbound inherited/optional method from becoming a success-shaped no-op.
        /// </summary>
        private void BindUnsupportedMethods()
        {
            foreach (NodeState node in GranulateDryingNodes.Walk(Context, State))
            {
                if (node is MethodState method && !IsFileMember(method))
                {
                    m_methods.Add(method);
                    if (!m_boundMethods.Contains(method))
                    {
                        method.OnCallMethod2Async ??= static (_, _, _, _, _, _) =>
                            new ValueTask<ServiceResult>(StatusCodes.BadNotSupported);
                    }
                    method.OnReadUserExecutable = method.BrowseName.Name is "GetPage" or "GetCurrentPage"
                        ? ReadReadOnlyExecutable : ReadUserExecutable;
                }
            }
        }

        /// <summary>
        /// Leaves file-handle reads under their existing ownership rules rather than claiming device control.
        /// </summary>
        private static bool IsFileMember(BaseInstanceState node)
        {
            for (NodeState? parent = node.Parent; parent != null; parent = (parent as BaseInstanceState)?.Parent)
            {
                if (parent is FileState)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Reports the current local ACL and control-ownership restriction without doing I/O.
        /// </summary>
        private ServiceResult ReadUserExecutable(ISystemContext context, NodeState node, ref bool executable)
        {
            executable &= CanMutate(context);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Reports read-only method availability without claiming control for observers.
        /// </summary>
        private ServiceResult ReadReadOnlyExecutable(ISystemContext context, NodeState node, ref bool executable)
        {
            executable &= !Volatile.Read(ref m_disposed) && Volatile.Read(ref m_current).RemoteControlEnabled;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Reports CurrentWrite only to a session that can currently control this device.
        /// </summary>
        private ServiceResult ReadUserAccessLevel(ISystemContext context, NodeState node, ref byte accessLevel)
        {
            bool automaticallyOwned = node is BaseVariableState variable &&
                ((variable.Parent is PlasticsRubber.MonitoredParameterState parameter &&
                    variable.BrowseName.Name?.Contains("Tolerance", StringComparison.Ordinal) == true &&
                    parameter.AutomaticMonitoring?.Value == true) ||
                (variable.Parent is PlasticsRubber.ClosedLoopControlState control &&
                    variable.BrowseName.Name == "PIDParameters" && control.AutomaticControllerMode?.Value == true));
            if (!CanMutate(context) || automaticallyOwned)
            {
                accessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Evaluates immediate ACL, persisted remote state, and the per-device session lease.
        /// </summary>
        private bool CanMutate(ISystemContext context)
        {
            if (Volatile.Read(ref m_disposed) ||
                !Volatile.Read(ref m_current).RemoteControlEnabled ||
                !m_authorization.CanControl(context))
            {
                return false;
            }
            NodeId session = SessionId(context);
            lock (m_controlLock)
            {
                return !session.IsNull && (m_controlOwner.IsNull || m_controlOwner == session);
            }
        }

        /// <summary>
        /// Releases session ownership synchronously; it performs no hardware or asynchronous operation.
        /// </summary>
        private void OnSessionClosing(Opc.Ua.Server.ISession session, SessionEventReason reason)
        {
            lock (m_controlLock)
            {
                if (m_controlOwner == session.Id)
                {
                    m_controlOwner = NodeId.Null;
                }
                if (m_activeControlSession == session.Id)
                {
                    m_activeSessionClosed = true;
                }
            }
        }

        /// <summary>
        /// Drains provider calls and disposes only resources owned by this handle.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            if (m_sessions != null)
            {
                m_sessions.SessionClosing -= OnSessionClosing;
                m_sessions = null;
            }
            foreach (MethodState method in m_methods)
            {
                method.Executable = false;
                method.UserExecutable = false;
            }
            foreach (ValueBinding binding in m_values.Values)
            {
                binding.Node.AccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
                binding.Node.UserAccessLevel &= unchecked((byte)~AccessLevels.CurrentWrite);
            }
            List<Exception>? failures = null;
            for (int ii = m_resources.Count - 1; ii >= 0; ii--)
            {
                try
                {
                    await m_resources[ii].DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            lock (m_controlLock)
            {
                m_controlOwner = NodeId.Null;
            }
            if (failures != null)
            {
                throw new AggregateException("Granulate drying capability cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Links caller cancellation with device shutdown.
        /// </summary>
        private GranulateDryingOperationLifetime.Lease Pending(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return m_lifetime.Enter(cancellationToken);
        }

        /// <summary>
        /// Rejects API calls after disposal has started.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(GranulateDryingDevice));
            }
        }

        /// <summary>
        /// Extracts transport-session identity without using user-supplied HMI fields.
        /// </summary>
        private static NodeId SessionId(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }

        /// <summary>
        /// Creates an explicit UA failure.
        /// </summary>
        private static ServiceResultException Error(StatusCode code, string message)
        {
            return ServiceResultException.Create(code, message);
        }

        /// <summary>
        /// A selected process variable and its semantic target/path.
        /// </summary>
        private sealed record ValueBinding(
            GranulateDryingTarget Target,
            string Member,
            BaseVariableState Node,
            byte SupportedAccessLevel,
            byte SupportedUserAccessLevel);

        /// <summary>
        /// An immutable publication snapshot; dictionaries are never changed after publication.
        /// </summary>
        private sealed record CommitState(
            bool Initialized,
            ulong Revision,
            Dictionary<NodeId, DataValue> Values,
            bool RemoteControlEnabled,
            ArrayOf<GranulateDryingError> Errors,
            GranulateDryingUser? User,
            GranulateDryingCommit? LastCommit);

        /// <summary>
        /// The shared address-space runtime.
        /// </summary>
        private readonly GranulateDryingRuntime m_runtime;

        /// <summary>
        /// The externally owned hardware/commit authority.
        /// </summary>
        private readonly IGranulateDryingProvider m_provider;

        /// <summary>
        /// The application's explicit authorization policy.
        /// </summary>
        private readonly IGranulateDryingAuthorization m_authorization;

        /// <summary>
        /// Reusable externally owned common-capability providers.
        /// </summary>
        private readonly GranulateDryingCommonProviders m_commonProviders;

        /// <summary>
        /// Standard targets indexed by their placeholder identity.
        /// </summary>
        private readonly Dictionary<GranulateDryingTarget, BaseObjectState> m_targets = [];

        /// <summary>
        /// Selected process values, excluding metadata and file handles.
        /// </summary>
        private readonly Dictionary<(GranulateDryingTarget Target, string Member), ValueBinding> m_values = [];

        /// <summary>
        /// Owned method states.
        /// </summary>
        private readonly List<MethodState> m_methods = [];

        /// <summary>
        /// Methods whose generated handlers have been bound.
        /// </summary>
        private readonly HashSet<MethodState> m_boundMethods = [];

        /// <summary>
        /// File and common-capability bindings in creation order.
        /// </summary>
        private readonly List<IAsyncDisposable> m_resources = [];

        /// <summary>
        /// Serializes requests and committed publications.
        /// </summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>
        /// Tracks all active and queued operations through device shutdown.
        /// </summary>
        private readonly GranulateDryingOperationLifetime m_lifetime = new();

        /// <summary>
        /// Protects the session identity used by synchronous ACL callbacks.
        /// </summary>
        private readonly Lock m_controlLock = new();

        /// <summary>
        /// Shares one resource cleanup after all operations have drained.
        /// </summary>
        private readonly Lazy<Task> m_cleanup;

        /// <summary>
        /// The immutable authoritative publication.
        /// </summary>
        private CommitState m_current = new(false, 0, [], false, [], null, null);

        /// <summary>
        /// The current authenticated control owner.
        /// </summary>
        private NodeId m_controlOwner;

        /// <summary>
        /// The session whose serialized operation is currently in progress.
        /// </summary>
        private NodeId m_activeControlSession;

        /// <summary>
        /// Whether that session closed before its hardware operation completed.
        /// </summary>
        private bool m_activeSessionClosed;

        /// <summary>
        /// The subscribed session manager, attached on the first control request.
        /// </summary>
        private ISessionManager? m_sessions;

        /// <summary>
        /// Claims execution-gate disposal after every waiting caller has left.
        /// </summary>
        private int m_gateDisposed;

        /// <summary>
        /// Whether the graph is registered for event delivery.
        /// </summary>
        private bool m_registered;

        /// <summary>
        /// Whether shutdown has begun.
        /// </summary>
        private bool m_disposed;
    }
}
