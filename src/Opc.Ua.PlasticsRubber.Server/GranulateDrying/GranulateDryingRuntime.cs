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
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// Direct-construction entry point for hosts that already own a Machinery/DI address space.
    /// </summary>
    public static class GranulateDryingRuntimeExtensions
    {
        /// <summary>
        /// Returns the owner's one drying runtime without requiring a DI container.
        /// The stock Machinery owner disposes it automatically.
        /// </summary>
        public static GranulateDryingRuntime CreateGranulateDryingRuntime(
            this DiNodeManager manager,
            IFileTypeBinder? fileBinder = null)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            return s_runtimes.GetValue(manager, owner => new Lazy<GranulateDryingRuntime>(
                () => new GranulateDryingRuntime(owner, fileBinder ?? new FileTypeBinder()))).Value;
        }

        /// <summary>
        /// One runtime and construction gate per address-space owner.
        /// </summary>
        private static readonly ConditionalWeakTable<DiNodeManager, Lazy<GranulateDryingRuntime>> s_runtimes = new();
    }

    /// <summary>
    /// Materializes provider-backed standard devices under Machines.
    /// It neither derives a fictitious DI device type nor starts a simulation.
    /// </summary>
    public sealed class GranulateDryingRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Creates a runtime after the compiled models have been loaded.
        /// </summary>
        internal GranulateDryingRuntime(DiNodeManager manager, IFileTypeBinder fileBinder)
        {
            Manager = manager;
            FileBinder = fileBinder;
            NodeId type = ExpandedNodeId.ToNodeId(ObjectTypeIds.GranulateDryingDeviceType, Context.NamespaceUris);
            NodeId machines = ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, Context.NamespaceUris);
            if (manager.FindPredefinedNode(type) == null ||
                manager.FindPredefinedNode(machines) is not BaseObjectState folder)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Load Machinery building blocks and GranulateDryingModelProvider before creating devices.");
            }
            Machines = folder;
            m_cleanup = new Lazy<Task>(DisposeCoreAsync);
            manager.RegisterMachineryResource(this);
        }

        /// <summary>
        /// Gets the single DI/Machinery owner.
        /// </summary>
        public DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the owner's context.
        /// </summary>
        public ISystemContext Context => Manager.SystemContext;

        /// <summary>
        /// Gets the application namespace, never the GDD model namespace.
        /// </summary>
        public ushort InstanceNamespaceIndex => Manager is MachineryNodeManager machinery
            ? machinery.MachineryInstanceNamespaceIndex
            : Manager.InstanceNamespaceIndex;

        /// <summary>
        /// Gets an immutable snapshot of the created devices.
        /// </summary>
        public ArrayOf<GranulateDryingDevice> Devices
        {
            get
            {
                lock (m_stateLock)
                {
                    return m_devices.ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets the standard discovery folder.
        /// </summary>
        internal BaseObjectState Machines { get; }

        /// <summary>
        /// Gets the reusable namespace-zero file binding module.
        /// </summary>
        internal IFileTypeBinder FileBinder { get; }

        /// <summary>
        /// Gets a deterministic registration-failure test seam.
        /// </summary>
        internal Func<GranulateDryingDevice, CancellationToken, ValueTask>? RegisteredForTest { get; set; }

        /// <summary>
        /// Registers a concrete application subtype of the specification's abstract maintenance alarm.
        /// </summary>
        internal async ValueTask<NodeId> MaintenanceAlarmTypeAsync(CancellationToken cancellationToken)
        {
            using GranulateDryingOperationLifetime.Lease pending = m_lifetime.Enter(cancellationToken);
            var id = new NodeId("GranulateDrying.MaintenanceConditionType", InstanceNamespaceIndex);
            await m_typeGate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                if (Manager.FindPredefinedNode(id) == null)
                {
                    await Manager.AddPredefinedNodeAsync(new BaseObjectTypeState
                    {
                        NodeId = id,
                        BrowseName = new QualifiedName("GranulateDryingMaintenanceConditionType",
                            InstanceNamespaceIndex),
                        DisplayName = new LocalizedText("Granulate drying maintenance condition"),
                        SuperTypeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.MaintenanceAlarmType,
                            Context.NamespaceUris),
                        IsAbstract = false,
                        IsPartOfTypeHierarchy = true
                    }, pending.Token).ConfigureAwait(false);
                }
                return id;
            }
            finally
            {
                m_typeGate.Release();
            }
        }

        /// <summary>
        /// Creates and initializes a selected device before exposing it.
        /// Failures release bindings and remove the exact graph this invocation registered.
        /// Injected providers and authorization policies remain owned by their caller/container.
        /// </summary>
        public ValueTask<GranulateDryingDevice> CreateDeviceAsync(
            GranulateDryingDeviceOptions options,
            IGranulateDryingProvider provider,
            IGranulateDryingAuthorization authorization,
            CancellationToken cancellationToken = default)
        {
            return CreateDeviceAsync(
                options, provider, authorization, new GranulateDryingCommonProviders(), cancellationToken);
        }

        /// <summary>
        /// Creates a selected device with reusable typed common-parameter and controller providers.
        /// Missing common providers explicitly reject their optional control operations.
        /// </summary>
        public async ValueTask<GranulateDryingDevice> CreateDeviceAsync(
            GranulateDryingDeviceOptions options,
            IGranulateDryingProvider provider,
            IGranulateDryingAuthorization authorization,
            GranulateDryingCommonProviders commonProviders,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (authorization == null)
            {
                throw new ArgumentNullException(nameof(authorization));
            }
            if (commonProviders == null)
            {
                throw new ArgumentNullException(nameof(commonProviders));
            }
            options.Validate();
            ThrowIfDisposed();
            using GranulateDryingOperationLifetime.Lease pending = m_lifetime.Enter(cancellationToken);
            await m_creationGate.WaitAsync(pending.Token).ConfigureAwait(false);
            GranulateDryingDevice? device = null;
            try
            {
                ThrowIfDisposed();
                var name = new QualifiedName(options.BrowseName, InstanceNamespaceIndex);
                var children = new List<BaseInstanceState>();
                Machines.GetChildren(Context, children);
                if (children.Any(child => child.BrowseName == name))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated, "Machines already contains '{0}'.", options.BrowseName);
                }
                GranulateDryingDeviceState state = GranulateDryingNodes.Create(Manager, Machines, name, options);
                device = new GranulateDryingDevice(this, state, options, provider, authorization, commonProviders);
                await device.InitializeAsync(pending.Token).ConfigureAwait(false);
                pending.Token.ThrowIfCancellationRequested();
                Machines.AddChild(state);
                await Manager.AddPredefinedNodeAsync(state, pending.Token).ConfigureAwait(false);
                Manager.AddRootNotifier(state);
                if (RegisteredForTest != null)
                {
                    await RegisteredForTest(device, pending.Token).ConfigureAwait(false);
                }
                pending.Token.ThrowIfCancellationRequested();
                device.Registered();
                lock (m_stateLock)
                {
                    m_devices.Add(device);
                }
                return device;
            }
            catch
            {
                if (device != null)
                {
                    await device.DisposeAsync().ConfigureAwait(false);
                    if (ReferenceEquals(Manager.FindPredefinedNode(device.NodeId), device.State))
                    {
                        await Manager.DeleteNodeAsync(Manager.SystemContext, device.NodeId, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    Machines.RemoveChild(device.State);
                }
                throw;
            }
            finally
            {
                m_creationGate.Release();
            }
        }

        /// <summary>
        /// Cancels pending construction and device operations, then releases protocol resources.
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
                if (Interlocked.Exchange(ref m_gatesDisposed, 1) == 0)
                {
                    m_creationGate.Dispose();
                    m_typeGate.Dispose();
                }
            }
            if (failures != null)
            {
                throw new AggregateException("Granulate drying runtime cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Drains construction before releasing the successfully created devices.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            ArrayOf<GranulateDryingDevice> devices = Devices;
            List<Exception>? failures = null;
            for (int index = 0; index < devices.Count; index++)
            {
                try
                {
                    await devices[index].DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures != null)
            {
                throw new AggregateException("Granulate drying device cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Rejects new work after shutdown begins.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(GranulateDryingRuntime));
            }
        }

        /// <summary>
        /// Protects the device snapshot and disposal task.
        /// </summary>
        private readonly Lock m_stateLock = new();

        /// <summary>
        /// Serializes creation and rollback on the shared discovery folder.
        /// </summary>
        private readonly SemaphoreSlim m_creationGate = new(1, 1);

        /// <summary>
        /// Serializes the one application-owned concrete maintenance event type.
        /// </summary>
        private readonly SemaphoreSlim m_typeGate = new(1, 1);

        /// <summary>
        /// Drains construction and shared-type work before releasing their gates.
        /// </summary>
        private readonly GranulateDryingOperationLifetime m_lifetime = new();

        /// <summary>
        /// Successfully registered devices.
        /// </summary>
        private readonly List<GranulateDryingDevice> m_devices = [];

        /// <summary>
        /// The shared device cleanup task after construction drains.
        /// </summary>
        private readonly Lazy<Task> m_cleanup;

        /// <summary>
        /// Claims release of the drained construction and event-type gates.
        /// </summary>
        private int m_gatesDisposed;

        /// <summary>
        /// Whether new work is rejected.
        /// </summary>
        private bool m_disposed;
    }
}
