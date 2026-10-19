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
using Opc.Ua.AutoID.Server.Builders;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Creates and owns the OPC 30010 readers of one Device Integration node manager: the
    /// stand-alone <see cref="AutoIdNodeManager"/>, or the owner AutoID is composed into with
    /// <see cref="DiNodeManagerAutoIdExtensions.CreateAutoIdRuntime"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runtime contributes the AutoID conformance units and server facets of the readers it
    /// hosts (OPC 30010 10.2) to its node manager: a facet is advertised when a reader implements
    /// each mandatory conformance unit of it, which depends on the capabilities of the reader's
    /// provider.
    /// </para>
    /// <para>
    /// The node manager owns the runtime and disposes it on shutdown: the readers stop their
    /// scans, detach and, when they own it, dispose their provider. Disposing the runtime earlier
    /// stops further reader creation.
    /// </para>
    /// </remarks>
    public sealed class AutoIdRuntime : IAsyncDisposable, IConformanceContributor
    {
        internal AutoIdRuntime(DiNodeManager manager, AutoIdServerOptions options)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
            m_logger = manager.SystemContext.Telemetry.CreateLogger<AutoIdRuntime>();
            m_placement = new AutoIdReaderPlacement(manager);
            manager.RegisterConformanceContributor(this);
            m_sessionLease = manager.RegisterSessionClosingHandler(OnSessionClosingAsync);
        }

        /// <summary>
        /// Gets the node manager that owns the address space of the readers.
        /// </summary>
        public DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the options the runtime was created with.
        /// </summary>
        public AutoIdServerOptions Options { get; }

        /// <summary>
        /// Gets the namespace the node manager mints instance NodeIds in, for the reader browse
        /// names: the Machinery instance namespace on a <see cref="MachineryNodeManager"/>, else the
        /// instance namespace of the Device Integration node manager.
        /// </summary>
        public ushort InstanceNamespaceIndex => Manager is MachineryNodeManager machinery
            ? machinery.MachineryInstanceNamespaceIndex
            : Manager.InstanceNamespaceIndex;

        /// <summary>
        /// Gets the system context of the node manager.
        /// </summary>
        public ISystemContext SystemContext => Manager.SystemContext;

        /// <summary>
        /// Deterministic test seam invoked by reader creation after the reader is registered and its
        /// provider attached, and before the creation completes. Lets a test fail the creation at
        /// its last step and prove the rollback. Always <see langword="null"/> in production.
        /// </summary>
        internal Func<AutoIdReader, Task>? ReaderRegisteredForTest { get; set; }

        /// <summary>
        /// Gets the readers created so far, in creation order.
        /// </summary>
        public ArrayOf<AutoIdReader> Readers
        {
            get
            {
                lock (m_lock)
                {
                    return m_readers.ToArray().ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Starts building a reader.
        /// </summary>
        /// <param name="browseName">
        /// The browse name of the device, usually in <see cref="InstanceNamespaceIndex"/>. A name
        /// already taken below the parent is rejected when the reader is built.
        /// </param>
        /// <param name="provider">The hardware behind the reader.</param>
        /// <returns>The builder; nothing is registered until <see cref="IAutoIdReaderBuilder.BuildAsync"/>.</returns>
        /// <exception cref="ArgumentException">
        /// The browse name is empty or the provider does not fit its kind.
        /// </exception>
        /// <exception cref="ArgumentNullException"></exception>
        public IAutoIdReaderBuilder Reader(QualifiedName browseName, IAutoIdReaderProvider provider)
        {
            ThrowIfDisposed();
            if (browseName.IsNull || string.IsNullOrEmpty(browseName.Name))
            {
                throw new ArgumentException("A reader browse name is required.", nameof(browseName));
            }
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            ValidateProvider(provider);
            return new AutoIdReaderBuilder(this, browseName, provider);
        }

        /// <summary>
        /// Creates a reader below the DI <c>DeviceSet</c> with default configuration.
        /// </summary>
        /// <param name="browseName">The browse name of the device.</param>
        /// <param name="provider">The hardware behind the reader.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<AutoIdReader> CreateReaderAsync(
            QualifiedName browseName,
            IAutoIdReaderProvider provider,
            CancellationToken cancellationToken = default)
        {
            return Reader(browseName, provider).BuildAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                AutoIdReader[] readers = Snapshot();
                var units = new List<QualifiedName>();
                if (readers.Length == 0)
                {
                    return units.ToArrayOf();
                }
                ushort ns = SystemContext.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri);
                void Add(string unit) => units.Add(new QualifiedName(unit, ns));
                Add(AutoIdProfiles.DeviceTypeUnit);
                Add(AutoIdProfiles.SimpleSyncAccessUnit);
                if (Array.Exists(readers, reader => reader.SupportsScan))
                {
                    Add(AutoIdProfiles.SyncAccessUnit);
                }
                Add(AutoIdProfiles.AsyncAccessUnit);
                if (Array.Exists(readers, reader => reader.SupportsAsyncScan))
                {
                    Add(AutoIdProfiles.AsyncAccessControlUnit);
                }
                return units.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles
        {
            get
            {
                AutoIdReader[] readers = Snapshot();
                var profiles = new List<string>();
                if (readers.Length == 0)
                {
                    return profiles.ToArrayOf();
                }
                profiles.Add(AutoIdProfiles.SimpleSyncServer);
                if (Array.Exists(readers, reader => reader.SupportsScan))
                {
                    profiles.Add(AutoIdProfiles.BaseSyncServer);
                }
                profiles.Add(AutoIdProfiles.BaseAsyncServer);
                return profiles.ToArrayOf();
            }
        }

        /// <summary>
        /// Stops further reader creation, then shuts down every reader.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            bool first;
            lock (m_lock)
            {
                first = !m_disposed;
                m_disposed = true;
            }
            if (first)
            {
                // Started outside the lock: cancelling the shutdown token can run continuations of
                // other code, which must not run under the state lock.
                _ = CompleteDisposeAsync();
            }
            return new ValueTask(m_disposeCompletion.Task);
        }

        private async Task CompleteDisposeAsync()
        {
            try
            {
                await DisposeCoreAsync().ConfigureAwait(false);
                m_disposeCompletion.SetResult(true);
            }
            catch (Exception ex)
            {
                m_disposeCompletion.SetException(ex);
            }
        }

        private async Task DisposeCoreAsync()
        {
            await m_stopping.CancelAsync().ConfigureAwait(false);
            m_sessionLease.Dispose();
            await m_createGate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (AutoIdReader reader in Snapshot())
                {
                    await DisposeQuietlyAsync(reader).ConfigureAwait(false);
                }
            }
            finally
            {
                m_createGate.Release();
            }
            m_createGate.Dispose();
            m_stopping.Dispose();
        }

        /// <summary>
        /// Hands the runtime to the shared resources of a composed owner, which disposes it on
        /// shutdown. Only the first call registers it.
        /// </summary>
        internal void RegisterLifetime()
        {
            if (Interlocked.Exchange(ref m_lifetimeRegistered, 1) != 0)
            {
                return;
            }
            try
            {
                Manager.RegisterMachineryResource(this);
            }
            catch
            {
                // Not registered: the next call tries again rather than leaving the readers
                // without an owner that disposes them.
                Volatile.Write(ref m_lifetimeRegistered, 0);
                throw;
            }
        }

        internal async ValueTask<AutoIdReader> CreateReaderAsync(
            AutoIdReaderBuilder builder,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_stopping.Token);
            try
            {
                await m_createGate.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(AutoIdRuntime));
            }
            try
            {
                // Creations are serialized from the name check to the registration, so two
                // concurrent builds of one name cannot both pass the check.
                ThrowIfDisposed();
                AutoIdReader reader = await CreateAndRegisterAsync(builder, cancellationToken).ConfigureAwait(false);
                lock (m_lock)
                {
                    m_readers.Add(reader);
                }
                m_logger.ReaderMaterialised(builder.BrowseName.Name, reader.NodeId);
                return reader;
            }
            finally
            {
                m_createGate.Release();
            }
        }

        private async ValueTask<AutoIdReader> CreateAndRegisterAsync(
            AutoIdReaderBuilder builder,
            CancellationToken cancellationToken)
        {
            AutoIdReaderKind kind = builder.Provider.Kind;
            ISystemContext context = SystemContext;
            QualifiedName name = builder.BrowseName;
            AutoIdDeviceState device;
            Action applyIdentification;
            switch (kind)
            {
                case AutoIdReaderKind.Rfid:
                    (device, applyIdentification) = await CreateDeviceAsync(
                        builder,
                        ObjectTypeIds.RfidReaderDeviceType,
                        parent => context.CreateInstanceOfRfidReaderDeviceType(parent, name),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case AutoIdReaderKind.Ocr:
                    (device, applyIdentification) = await CreateDeviceAsync(
                        builder,
                        ObjectTypeIds.OcrReaderDeviceType,
                        parent => context.CreateInstanceOfOcrReaderDeviceType(parent, name),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case AutoIdReaderKind.Optical:
                    (device, applyIdentification) = await CreateDeviceAsync(
                        builder,
                        ObjectTypeIds.OpticalReaderDeviceType,
                        parent => context.CreateInstanceOfOpticalReaderDeviceType(parent, name),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case AutoIdReaderKind.OpticalVerifier:
                    (device, applyIdentification) = await CreateDeviceAsync(
                        builder,
                        ObjectTypeIds.OpticalVerifierDeviceType,
                        parent => context.CreateInstanceOfOpticalVerifierDeviceType(parent, name),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case AutoIdReaderKind.Rtls:
                    (device, applyIdentification) = await CreateDeviceAsync(
                        builder,
                        ObjectTypeIds.RtlsDeviceType,
                        parent => context.CreateInstanceOfRtlsDeviceType(parent, name),
                        cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(builder), kind, "Unknown reader kind.");
            }

            NodeState parentNode = device.Parent ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The reader '{0}' has no parent.",
                    name);
            AutoIdReader? reader = null;
            NodeState? organizedBy = null;
            try
            {
                applyIdentification();
                organizedBy = KeepReachableFromDeviceSet(parentNode, device);
                reader = new AutoIdReader(
                    this,
                    device,
                    builder.Provider,
                    builder.OwnsProviderOnDispose,
                    builder.DeviceLocation != null);
                reader.Bind();
                await Manager.RegisterRootNotifierAsync(device, cancellationToken).ConfigureAwait(false);
                await reader.AttachAsync(cancellationToken).ConfigureAwait(false);
                if (ReaderRegisteredForTest != null)
                {
                    await ReaderRegisteredForTest(reader).ConfigureAwait(false);
                }
                return reader;
            }
            catch
            {
                // Left in place, the half-registered device would keep its name taken: a retry
                // fails with Bad_BrowseNameDuplicated.
                if (reader != null)
                {
                    await DisposeQuietlyAsync(reader).ConfigureAwait(false);
                }
                else if (builder.OwnsProviderOnDispose)
                {
                    // The reader never took ownership of the provider.
                    await DisposeProviderQuietlyAsync(builder.Provider, builder.BrowseName.Name).ConfigureAwait(false);
                }
                if (organizedBy != null)
                {
                    AutoIdReaderPlacement.RemoveOrganize(organizedBy, device);
                }
                await RemoveUnregisteredDeviceAsync(parentNode, device).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Keeps a reader placed below another parent reachable from the DI <c>DeviceSet</c>
        /// (OPC 10000-100 4.9): when <c>DeviceSet</c> does not reach the parent, it organizes the
        /// reader directly.
        /// </summary>
        /// <returns>The <c>DeviceSet</c> that now organizes the reader, else <see langword="null"/>.</returns>
        /// <exception cref="ServiceResultException">The node manager publishes no <c>DeviceSet</c>.</exception>
        private NodeState? KeepReachableFromDeviceSet(NodeState parent, AutoIdDeviceState device)
        {
            NodeState deviceSet = m_placement.FindDeviceSet() ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The node manager publishes no DeviceSet to locate the reader '{0}' from.",
                    device.BrowseName);
            if (m_placement.IsReachableFrom(deviceSet, parent))
            {
                return null;
            }
            AutoIdReaderPlacement.Organize(deviceSet, device);
            return deviceSet;
        }

        private async ValueTask<(AutoIdDeviceState Device, Action ApplyIdentification)> CreateDeviceAsync<TDevice>(
            AutoIdReaderBuilder builder,
            ExpandedNodeId typeId,
            Func<NodeState, TDevice> create,
            CancellationToken cancellationToken)
            where TDevice : AutoIdDeviceState
        {
            var typeDefinitionId = ExpandedNodeId.ToNodeId(typeId, SystemContext.NamespaceUris);
            Di.Server.Builders.IDeviceBuilder<TDevice> deviceBuilder = await Manager.CreateDeviceAsync(
                builder.BrowseName,
                typeDefinitionId,
                parent =>
                {
                    TDevice device = create(parent);
                    AutoIdDeviceConfigurator.Configure(SystemContext, device, builder);
                    return device;
                },
                builder.Parent,
                cancellationToken).ConfigureAwait(false);
            return (
                deviceBuilder.Device,
                () =>
                {
                    if (builder.Identification != null)
                    {
                        deviceBuilder.WithIdentification(builder.Identification);
                    }
                }
            );
        }

        private async ValueTask DisposeProviderQuietlyAsync(IAutoIdReaderProvider provider, string? name)
        {
            try
            {
                if (provider is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                m_logger.ReaderRollbackFailed(ex, name);
            }
        }

        private async ValueTask DisposeQuietlyAsync(AutoIdReader reader)
        {
            try
            {
                await reader.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.ReaderRollbackFailed(ex, reader.Name.Name);
            }
        }

        /// <summary>
        /// Undoes a reader creation that failed after the device was attached to its parent: deletes
        /// what was registered of it and detaches it from the parent.
        /// </summary>
        /// <remarks>
        /// A failure of the cleanup is logged rather than thrown, so the caller sees the exception
        /// that failed the creation.
        /// </remarks>
        private async ValueTask RemoveUnregisteredDeviceAsync(NodeState parent, AutoIdDeviceState device)
        {
            try
            {
                if (ReferenceEquals(Manager.FindPredefinedNode(device.NodeId), device))
                {
                    await Manager.DeleteNodeAsync(Manager.SystemContext, device.NodeId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.ReaderRollbackFailed(ex, device.BrowseName.Name);
            }
            try
            {
                parent.RemoveChild(device);
            }
            catch (Exception ex)
            {
                m_logger.ReaderRollbackFailed(ex, device.BrowseName.Name);
            }
        }

        private async ValueTask OnSessionClosingAsync(NodeId sessionId, CancellationToken cancellationToken)
        {
            List<Exception>? errors = null;
            foreach (AutoIdReader reader in Snapshot())
            {
                try
                {
                    await reader.OnSessionClosingAsync(sessionId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }
            if (errors != null)
            {
                throw new AggregateException("Stopping the scans of the closing session failed.", errors);
            }
        }

        private static void ValidateProvider(IAutoIdReaderProvider provider)
        {
            if (!AutoIdCompat.IsDefined(provider.Kind))
            {
                throw new ArgumentException("The provider reports an unknown reader kind.", nameof(provider));
            }
            if (provider.Kind == AutoIdReaderKind.OpticalVerifier && provider is IAutoIdScanProvider)
            {
                // The NodeSet declares the Scan method of OpticalVerifierDeviceType with WGS84Coordinate
                // results (node i=7054) where OPC 30010 6.4.3.1 names OpticalVerifierScanResult, so
                // the method cannot be served consistently; verifier results flow through ScanStart
                // and the scan events.
                throw new ArgumentException(
                    "An optical verifier reports through ScanStart and scan events; its Scan method is not published.",
                    nameof(provider));
            }
            if (provider is IAutoIdRfidProvider rfid)
            {
                if (provider.Kind != AutoIdReaderKind.Rfid)
                {
                    throw new ArgumentException(
                        "A provider with RFID tag access must be an RFID reader.",
                        nameof(provider));
                }
                if ((rfid.SupportedOperations & ~AutoIdRfidOperations.All) != 0)
                {
                    throw new ArgumentException(
                        "The provider reports unknown tag operations.",
                        nameof(provider));
                }
            }
            else if (provider.Kind == AutoIdReaderKind.Rfid && provider is IAutoIdRtlsProvider)
            {
                throw new ArgumentException("An RFID reader cannot provide RTLS locations.", nameof(provider));
            }
            if (provider is IAutoIdRtlsProvider rtls)
            {
                if (provider.Kind != AutoIdReaderKind.Rtls)
                {
                    throw new ArgumentException(
                        "A provider with RTLS locations must be an RTLS device.",
                        nameof(provider));
                }
                if (rtls.SupportedLocationTypes.Count == 0)
                {
                    throw new ArgumentException(
                        "An RTLS provider must support at least one location type.",
                        nameof(provider));
                }
                foreach (LocationTypeEnumeration type in rtls.SupportedLocationTypes)
                {
                    if (!AutoIdCompat.IsDefined(type))
                    {
                        throw new ArgumentException(
                            "An RTLS provider reports an unknown location type.",
                            nameof(provider));
                    }
                }
            }
            else if (provider.Kind == AutoIdReaderKind.Rtls)
            {
                throw new ArgumentException(
                    "An RTLS device needs an IAutoIdRtlsProvider.",
                    nameof(provider));
            }
        }

        private AutoIdReader[] Snapshot()
        {
            lock (m_lock)
            {
                return [.. m_readers];
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(AutoIdRuntime));
            }
        }

        private readonly ILogger m_logger;
        private readonly AutoIdReaderPlacement m_placement;
        private readonly Lock m_lock = new();
        private readonly List<AutoIdReader> m_readers = [];
        private readonly SemaphoreSlim m_createGate = new(1, 1);
        private readonly CancellationTokenSource m_stopping = new();
        private readonly IDisposable m_sessionLease;

        private readonly TaskCompletionSource<bool> m_disposeCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool m_disposed;
        private int m_lifetimeRegistered;
    }

    /// <summary>
    /// Source-generated logging for the runtime.
    /// </summary>
    internal static partial class AutoIdRuntimeLog
    {
        /// <summary>
        /// Reports a registered reader.
        /// </summary>
        [LoggerMessage(
            EventId = AutoIdServerEventIds.AutoIdRuntime + 0,
            Level = LogLevel.Information,
            Message = "Materialised AutoID reader '{Name}', NodeId={NodeId}.")]
        public static partial void ReaderMaterialised(
            this ILogger logger,
            string? name,
            NodeId nodeId);

        /// <summary>
        /// Reports a failed creation rollback.
        /// </summary>
        [LoggerMessage(
            EventId = AutoIdServerEventIds.AutoIdRuntime + 1,
            Level = LogLevel.Error,
            Message = "Could not remove AutoID reader '{Name}' after its creation failed.")]
        public static partial void ReaderRollbackFailed(
            this ILogger logger,
            Exception exception,
            string? name);
    }
}
