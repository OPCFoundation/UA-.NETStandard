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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Robotics.Server.Hosting;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Standalone node manager for OPC UA Robot Intent.
    /// </summary>
    public sealed class RobotIntentNodeManager :
        FluentNodeManagerBase,
        INodeIdFactory,
        IAsyncDisposable,
        IConformanceContributor
    {
        /// <summary>
        /// Creates a standalone Robot Intent node manager.
        /// </summary>
        public RobotIntentNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : this(
                server,
                configuration,
                new IRobotIntentModelProvider[] { new RobotIntentModelProvider() },
                new RobotIntentServerOptions())
        {
        }

        /// <summary>
        /// Creates a standalone Robot Intent node manager with explicit services.
        /// </summary>
        public RobotIntentNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ArrayOf<IRobotIntentModelProvider> providers,
            RobotIntentServerOptions options,
            IRobotIntentPostSetupRunner? runner = null,
            IServiceProvider? services = null)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<RobotIntentNodeManager>(),
                GetNamespaceUris(providers, options))
        {
            m_providers = NormalizeProviders(providers);
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_options.Validate();
            m_runner = runner;
            m_services = services;
            SystemContext.NodeIdFactory = this;
            RegisterEncodeables(SystemContext);
        }

        /// <summary>
        /// Gets the Robot Intent root object.
        /// </summary>
        public RobotIntentRootState Root => m_root ??
            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "The Robot Intent address space is not available yet.");

        /// <summary>
        /// Gets the execution hosts started for the controllers owned by this node manager.
        /// </summary>
        public ArrayOf<IntentControllerHost> IntentControllerHosts
        {
            get
            {
                lock (m_hostsLock)
                {
                    return m_hosts.ToArray().ToArrayOf();
                }
            }
        }

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits => [];

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => ComputeServerProfileArrayEntries();

        internal bool BaseDisposeStarted => Volatile.Read(ref m_baseDisposeStarted) != 0;

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance && instance.Parent != null)
            {
                return new NodeId(
                    $"{instance.Parent.NodeId.IdentifierAsString}_{instance.SymbolicName}",
                    GetInstanceNamespaceIndex(context));
            }
            if (node.NodeId.IsNull)
            {
                return new NodeId(Guid.NewGuid(), GetInstanceNamespaceIndex(context));
            }
            return node.NodeId;
        }

        /// <summary>
        /// Creates a direct build context for non-DI configuration.
        /// </summary>
        public IRobotIntentBuildContext CreateRobotIntentBuildContext(
            CancellationToken cancellationToken = default)
        {
            return new RobotIntentBuildContext(
                this,
                Root,
                m_options,
                cancellationToken,
                RobotIntentBuildServiceProvider.RequireExecutor(m_services));
        }

        /// <summary>
        /// Creates a direct build context for non-DI configuration with an explicit executor.
        /// </summary>
        public IRobotIntentBuildContext CreateRobotIntentBuildContext(
            IIntentExecutor executor,
            CancellationToken cancellationToken = default)
        {
            if (executor == null)
            {
                throw new ArgumentNullException(nameof(executor));
            }
            return new RobotIntentBuildContext(
                this,
                Root,
                m_options,
                cancellationToken,
                RobotIntentBuildServiceProvider.ForExecutor(executor, m_services));
        }

        /// <summary>
        /// Gets the execution host for a controller node.
        /// </summary>
        public IntentControllerHost GetIntentControllerHost(NodeId controllerNodeId)
        {
            if (controllerNodeId.IsNull)
            {
                throw new ArgumentException("A non-null controller NodeId is required.", nameof(controllerNodeId));
            }
            lock (m_hostsLock)
            {
                for (int ii = 0; ii < m_hosts.Count; ii++)
                {
                    if (m_hosts[ii].Controller.NodeId == controllerNodeId)
                    {
                        return m_hosts[ii];
                    }
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNodeIdUnknown,
                "No Robot Intent execution host is registered for controller '{0}'.",
                controllerNodeId);
        }

        /// <summary>
        /// Starts execution hosts after the server runtime has created the SessionManager.
        /// </summary>
        public void StartIntentControllerHosts()
        {
            var pending = new List<IntentControllerHost>();
            bool sessionManagerAvailable = SystemContext.Server.SessionManager != null;
            lock (m_hostsLock)
            {
                for (int ii = 0; ii < m_hosts.Count; ii++)
                {
                    if (m_startedHosts.Add(m_hosts[ii]) || sessionManagerAvailable)
                    {
                        pending.Add(m_hosts[ii]);
                    }
                }
            }
            for (int ii = 0; ii < pending.Count; ii++)
            {
                pending[ii].Start(SystemContext);
            }
        }

        /// <summary>
        /// Asynchronously disposes the execution hosts owned by this node manager.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            ArrayOf<IntentControllerHost> deferredHosts = await DisposeHostsAsync().ConfigureAwait(false);
            if (deferredHosts.Count == 0)
            {
                DisposeBase(disposing: true);
                GC.SuppressFinalize(this);
                return;
            }
            _ = DisposeBaseWhenHostsCompleteAsync(deferredHosts);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            RegisterEncodeables(SystemContext);
            m_root = await GetOrCreateRootAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            if (m_runner != null)
            {
                await m_runner.RunAsync(this, m_root, m_options, cancellationToken).ConfigureAwait(false);
            }
            StartIntentControllerHosts();
            PublishServerProfiles();
            m_logger.NodeManagerReady();
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            for (int ii = 0; ii < m_providers.Count; ii++)
            {
                m_providers[ii].AddPredefinedNodes(nodes, context);
            }
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        [SuppressMessage(
            "Usage",
            "CA2215:Dispose methods should call base class dispose",
            Justification = DeferredBaseDisposeJustification)]
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ArrayOf<IntentControllerHost> deferredHosts = DisposeHosts();
                if (deferredHosts.Count != 0)
                {
                    // Base disposal is completed by the continuation once every deferred host has released resources.
                    _ = DisposeBaseWhenHostsCompleteAsync(deferredHosts);
                    return;
                }
            }
            DisposeBase(disposing);
        }

        internal void RegisterIntentControllerHost(IntentControllerHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }
            lock (m_hostsLock)
            {
                m_hosts.Add(host);
            }
            PublishServerProfiles();
        }

        internal static ArrayOf<IRobotIntentModelProvider> NormalizeProviders(
            ArrayOf<IRobotIntentModelProvider> providers)
        {
            if (providers.IsNull || providers.Count == 0)
            {
                return new IRobotIntentModelProvider[] { new RobotIntentModelProvider() };
            }
            var sorted = new List<IRobotIntentModelProvider>();
            for (int ii = 0; ii < providers.Count; ii++)
            {
                sorted.Add(providers[ii]);
            }
            sorted.Sort(static (left, right) => left.Order.CompareTo(right.Order));
            return sorted.ToArray().ToArrayOf();
        }

        internal static string[] GetNamespaceUris(
            ArrayOf<IRobotIntentModelProvider> providers,
            RobotIntentServerOptions options)
        {
            options ??= new RobotIntentServerOptions();
            options.Validate();
            var uris = new List<string>();
            ArrayOf<IRobotIntentModelProvider> normalized = NormalizeProviders(providers);
            for (int ii = 0; ii < normalized.Count; ii++)
            {
                ArrayOf<string> providerUris = normalized[ii].NamespaceUris;
                for (int jj = 0; jj < providerUris.Count; jj++)
                {
                    if (!uris.Contains(providerUris[jj]))
                    {
                        uris.Add(providerUris[jj]);
                    }
                }
            }
            if (!uris.Contains(options.InstanceNamespaceUri))
            {
                uris.Add(options.InstanceNamespaceUri);
            }
            return [.. uris];
        }

        private static void RegisterEncodeables(ServerSystemContext context)
        {
            var probe = new Pose3DDataType();
            if (!context.EncodeableFactory.TryGetEncodeableType(probe.BinaryEncodingId, out _))
            {
                context.EncodeableFactory.Builder.AddOpcUaRobotIntent().Commit();
            }
        }

        private async ValueTask<RobotIntentRootState> GetOrCreateRootAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken)
        {
            var robotIntentRootId = ExpandedNodeId.ToNodeId(
                global::Opc.Ua.RobotIntent.ObjectIds.RobotIntent,
                SystemContext.NamespaceUris);
            if (FindPredefinedNode<RobotIntentRootState>(robotIntentRootId) is RobotIntentRootState root)
            {
                await EnsureRootChildrenAsync(root, cancellationToken).ConfigureAwait(false);
                return root;
            }

            root = CreateRoot();
            await AddPredefinedNodeAsync(root, cancellationToken).ConfigureAwait(false);
            if (!externalReferences.TryGetValue(global::Opc.Ua.ObjectIds.Server, out IList<IReference>? references))
            {
                externalReferences[global::Opc.Ua.ObjectIds.Server] = references = [];
            }
            references.Add(new NodeStateReference(
                global::Opc.Ua.ReferenceTypeIds.HasComponent,
                false,
                root.NodeId));
            return root;
        }

        private async ValueTask EnsureRootChildrenAsync(
            RobotIntentRootState root,
            CancellationToken cancellationToken)
        {
            root.CreateOrReplaceControllers(SystemContext, null);
            root.CreateOrReplaceSpecificationVersion(SystemContext, null);
            root.SpecificationVersion!.Value = m_options.SpecificationVersion;
            if (FindPredefinedNode<NodeState>(root.Controllers!.NodeId) == null)
            {
                await AddPredefinedNodeAsync(root.Controllers, cancellationToken).ConfigureAwait(false);
            }
            if (FindPredefinedNode<NodeState>(root.SpecificationVersion.NodeId) == null)
            {
                await AddPredefinedNodeAsync(root.SpecificationVersion, cancellationToken).ConfigureAwait(false);
            }
        }

        private RobotIntentRootState CreateRoot()
        {
            var browseName = new QualifiedName("RobotIntent", GetInstanceNamespaceIndex(SystemContext));
            RobotIntentRootState root = global::Opc.Ua.RobotIntent.OpcUaRobotIntentExtensions
                .CreateInstanceOfRobotIntentRootType(
                    SystemContext,
                    Server.ServerObject,
                    browseName);
            root.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            root.CreateOrReplaceControllers(SystemContext, null);
            root.CreateOrReplaceSpecificationVersion(SystemContext, null);
            root.SpecificationVersion!.Value = m_options.SpecificationVersion;
            root.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, true, global::Opc.Ua.ObjectIds.Server);
            return root;
        }

        private ushort GetInstanceNamespaceIndex(ISystemContext context)
        {
            return (ushort)context.NamespaceUris.GetIndex(m_options.InstanceNamespaceUri);
        }

        private ArrayOf<IntentControllerHost> DisposeHosts()
        {
            ArrayOf<IntentControllerHost> hosts = TakeHosts();
            var deferredHosts = new List<IntentControllerHost>();
            for (int ii = 0; ii < hosts.Count; ii++)
            {
                hosts[ii].Dispose();
                if (hosts[ii].IsShutdownDeferred)
                {
                    deferredHosts.Add(hosts[ii]);
                }
            }
            return deferredHosts.ToArray().ToArrayOf();
        }

        private async ValueTask<ArrayOf<IntentControllerHost>> DisposeHostsAsync()
        {
            ArrayOf<IntentControllerHost> hosts = TakeHosts();
            var deferredHosts = new List<IntentControllerHost>();
            for (int ii = 0; ii < hosts.Count; ii++)
            {
                await hosts[ii].DisposeAsync().ConfigureAwait(false);
                if (hosts[ii].IsShutdownDeferred)
                {
                    deferredHosts.Add(hosts[ii]);
                }
            }
            return deferredHosts.ToArray().ToArrayOf();
        }

        private async Task DisposeBaseWhenHostsCompleteAsync(ArrayOf<IntentControllerHost> deferredHosts)
        {
            while (!AllResourcesDisposed(deferredHosts))
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
            DisposeBase(disposing: true);
        }

        private static bool AllResourcesDisposed(ArrayOf<IntentControllerHost> hosts)
        {
            for (int ii = 0; ii < hosts.Count; ii++)
            {
                if (!hosts[ii].ResourcesDisposed)
                {
                    return false;
                }
            }
            return true;
        }

        private void DisposeBase(bool disposing)
        {
            if (Interlocked.Exchange(ref m_baseDisposeStarted, 1) == 0)
            {
                base.Dispose(disposing);
            }
        }

        private ArrayOf<IntentControllerHost> TakeHosts()
        {
            lock (m_hostsLock)
            {
                ArrayOf<IntentControllerHost> hosts = m_hosts.ToArray().ToArrayOf();
                m_hosts.Clear();
                m_startedHosts.Clear();
                return hosts;
            }
        }

        private ArrayOf<string> ComputeServerProfileArrayEntries()
        {
            var profiles = new List<string>();
            var facetNames = new List<string>();
            var seenFacetNames = new HashSet<string>(StringComparer.Ordinal);
            ArrayOf<IntentControllerHost> hosts = IntentControllerHosts;
            for (int ii = 0; ii < hosts.Count; ii++)
            {
                ArrayOf<string> facets = RobotIntentFacetCalculator.Compute(hosts[ii].Controller);
                for (int jj = 0; jj < facets.Count; jj++)
                {
                    if (!string.IsNullOrEmpty(facets[jj]) && seenFacetNames.Add(facets[jj]))
                    {
                        facetNames.Add(facets[jj]);
                    }
                }
                AddProfileIfSatisfied(
                    profiles,
                    facets,
                    RobotIntentConformanceUris.Profiles.Motion,
                    RobotIntentConformanceUris.FacetNames.Base,
                    RobotIntentConformanceUris.FacetNames.MotionJoint,
                    RobotIntentConformanceUris.FacetNames.MotionLinear,
                    RobotIntentConformanceUris.FacetNames.Description,
                    RobotIntentConformanceUris.FacetNames.Safety);
                AddProfileIfSatisfied(
                    profiles,
                    facets,
                    RobotIntentConformanceUris.Profiles.Handling,
                    RobotIntentConformanceUris.FacetNames.Base,
                    RobotIntentConformanceUris.FacetNames.MotionJoint,
                    RobotIntentConformanceUris.FacetNames.MotionLinear,
                    RobotIntentConformanceUris.FacetNames.Description,
                    RobotIntentConformanceUris.FacetNames.Safety,
                    RobotIntentConformanceUris.FacetNames.MotionCircular,
                    RobotIntentConformanceUris.FacetNames.Grasp,
                    RobotIntentConformanceUris.FacetNames.PickPlace,
                    RobotIntentConformanceUris.FacetNames.ToolChange,
                    RobotIntentConformanceUris.FacetNames.Output,
                    RobotIntentConformanceUris.FacetNames.Queue);
                AddProfileIfSatisfied(
                    profiles,
                    facets,
                    RobotIntentConformanceUris.Profiles.Path,
                    RobotIntentConformanceUris.FacetNames.Base,
                    RobotIntentConformanceUris.FacetNames.MotionJoint,
                    RobotIntentConformanceUris.FacetNames.MotionLinear,
                    RobotIntentConformanceUris.FacetNames.Description,
                    RobotIntentConformanceUris.FacetNames.Safety,
                    RobotIntentConformanceUris.FacetNames.Trajectory,
                    RobotIntentConformanceUris.FacetNames.Path,
                    RobotIntentConformanceUris.FacetNames.Blending);
                AddProfileIfSatisfied(
                    profiles,
                    facets,
                    RobotIntentConformanceUris.Profiles.Mission,
                    RobotIntentConformanceUris.FacetNames.Base,
                    RobotIntentConformanceUris.FacetNames.MotionJoint,
                    RobotIntentConformanceUris.FacetNames.MotionLinear,
                    RobotIntentConformanceUris.FacetNames.Description,
                    RobotIntentConformanceUris.FacetNames.Safety,
                    RobotIntentConformanceUris.FacetNames.Mission,
                    RobotIntentConformanceUris.FacetNames.Program,
                    RobotIntentConformanceUris.FacetNames.Wait,
                    RobotIntentConformanceUris.FacetNames.Pause,
                    RobotIntentConformanceUris.FacetNames.Retry);
            }
            var entries = new List<string>(profiles);
            for (int ii = 0; ii < facetNames.Count; ii++)
            {
                AddFacetUriIfClaimed(entries, facetNames[ii]);
            }
            return entries.ToArrayOf();
        }

        private static void AddProfileIfSatisfied(
            List<string> profiles,
            ArrayOf<string> facets,
            string profileUri,
            params string[] requiredFacets)
        {
            if (profiles.Contains(profileUri))
            {
                return;
            }
            for (int ii = 0; ii < requiredFacets.Length; ii++)
            {
                if (!facets.Contains(requiredFacets[ii]))
                {
                    return;
                }
            }
            profiles.Add(profileUri);
        }

        private static void AddFacetUriIfClaimed(
            List<string> entries,
            string facetName)
        {
            if (RobotIntentConformanceUris.TryGetFacetUri(facetName, out string facetUri) &&
                !entries.Contains(facetUri))
            {
                entries.Add(facetUri);
            }
        }

        private void PublishServerProfiles()
        {
            ServerObjectState? serverObject = Server?.ServerObject;
            BaseVariableState? profileArray = serverObject?.ServerCapabilities?.ServerProfileArray;
            if (profileArray == null)
            {
                return;
            }
            ArrayOf<string> profiles = ServerProfiles;
            var merged = new List<string>();
            if (profileArray.Value.TryGetValue(out ArrayOf<string> existing))
            {
                for (int ii = 0; ii < existing.Count; ii++)
                {
                    if (!string.IsNullOrEmpty(existing[ii]) && !merged.Contains(existing[ii]))
                    {
                        merged.Add(existing[ii]);
                    }
                }
            }
            for (int ii = 0; ii < profiles.Count; ii++)
            {
                if (!string.IsNullOrEmpty(profiles[ii]) && !merged.Contains(profiles[ii]))
                {
                    merged.Add(profiles[ii]);
                }
            }
            profileArray.Value = Variant.From(merged.ToArrayOf());
            profileArray.ClearChangeMasks(SystemContext, false);
        }

        private readonly ArrayOf<IRobotIntentModelProvider> m_providers;
        private readonly RobotIntentServerOptions m_options;
        private readonly IRobotIntentPostSetupRunner? m_runner;
        private readonly IServiceProvider? m_services;
        private readonly Lock m_hostsLock = new();
        private readonly List<IntentControllerHost> m_hosts = [];
        private readonly HashSet<IntentControllerHost> m_startedHosts = [];
        private const string DeferredBaseDisposeJustification =
            "Deferred host shutdown must postpone base teardown; TODO: remove when CA2215 models async handoff.";
        private int m_baseDisposeStarted;
        private RobotIntentRootState? m_root;
    }

    internal static partial class RobotIntentNodeManagerLog
    {
        [LoggerMessage(
            EventId = RobotIntentServerEventIds.NodeManagerReady,
            Level = LogLevel.Information,
            Message = "Robot Intent node manager loaded the Robot Intent model.")]
        public static partial void NodeManagerReady(this ILogger logger);
    }
}
