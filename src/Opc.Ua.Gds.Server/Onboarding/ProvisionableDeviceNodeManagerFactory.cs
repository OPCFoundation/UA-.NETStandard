// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Publishes one generated ProvisionableDevice under the Part 22 Resources object.
    /// </summary>
    public sealed partial class ProvisionableDeviceNodeManagerFactory : INodeManagerHandoffFactory
    {
        /// <summary>
        /// Creates a factory that borrows the hosted device runtime.
        /// </summary>
        /// <param name="runtime">The ticket and registrar-configuration runtime.</param>
        public ProvisionableDeviceNodeManagerFactory(IProvisionableDeviceRuntime runtime)
            : this(runtime, null, null, null)
        {
        }

        /// <summary>
        /// Creates a factory that also binds application components when the host selects a provider.
        /// </summary>
        /// <param name="runtime">The ticket and registrar-configuration runtime.</param>
        /// <param name="applicationsProvider">The provider for device application components.</param>
        /// <param name="security">The device trust and reset gate for application push.</param>
        /// <param name="softwareUpdates">The optional software-update gate for application push.</param>
        public ProvisionableDeviceNodeManagerFactory(
            IProvisionableDeviceRuntime runtime,
            IProvisionableDeviceApplicationsProvider? applicationsProvider = null,
            IProvisionableDeviceSecurity? security = null,
            IProvisionableDeviceSoftwareUpdatePolicy? softwareUpdates = null)
        {
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_applicationsProvider = applicationsProvider;
            m_security = security;
            m_softwareUpdates = softwareUpdates;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [NamespaceUri];

        /// <summary>
        /// Gets the namespace used for the provisionable-device instance and its children.
        /// </summary>
        public const string NamespaceUri = "urn:opcfoundation.org:UA:Onboarding:Instances";

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // The returned manager is owned by the server. TODO: Express ownership transfer to the analyzer.
            return new ValueTask<IAsyncNodeManager>(CreateManager(server, configuration));
#pragma warning restore CA2000
        }

        /// <summary>
        /// Creates a generation-local node manager for the configured runtime.
        /// </summary>
        private ProvisionableDeviceNodeManager CreateManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ProvisionableDeviceNodeManager? predecessor = null)
        {
            server.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            server.MessageContext.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            return new ProvisionableDeviceNodeManager(
                server, configuration, m_runtime, m_applicationsProvider, m_security, m_softwareUpdates, predecessor);
        }

        /// <summary>
        /// Creates the generated device object with its standard namespace-zero browse name and Resources parent.
        /// </summary>
        internal static ProvisionableDeviceState CreateProvisionableDevice(
            ISystemContext context,
            IProvisionableDeviceRuntime runtime)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            ushort namespaceIndex = context.NamespaceUris.GetIndexOrAppend(NamespaceUri);
            var device = new ProvisionableDeviceState(null);
            device.SetRegistrarEndpoints = new SetRegistrarEndpointsMethodState(device);
            device.Create(
                context,
                new NodeId(29878),
                new QualifiedName("ProvisionableDevice", 0),
                new LocalizedText("Provisionable Device"),
                assignNodeIds: false);

            var mapping = new Dictionary<NodeId, NodeId>();
            var assigned = new HashSet<NodeId>();
            AssignStableIdentifiers(context, device, "ProvisionableDevice", namespaceIndex, mapping, assigned);
            device.UpdateReferenceTargets(context, mapping);
            device.BindToRuntime(context, runtime);
            device.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = Ua.ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                }
            ];
            PropertyState<bool> singleton = device.IsSingleton ??
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            singleton.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = Ua.ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                }
            ];
            MethodState requestTickets = device.RequestTickets ??
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            requestTickets.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = Ua.ObjectIds.WellKnownRole_AuthenticatedUser,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                }
            ];
            device.AddReference(ReferenceTypeIds.Organizes, true, Ua.ObjectIds.Resources);
            return device;
        }

        /// <summary>
        /// Assigns stable identifiers to the generated instance and each generated child.
        /// </summary>
        private static void AssignStableIdentifiers(
            ISystemContext context,
            NodeState node,
            string path,
            ushort namespaceIndex,
            Dictionary<NodeId, NodeId> mapping,
            HashSet<NodeId> assigned)
        {
            NodeId identifier = path switch
            {
                "ProvisionableDevice" => new NodeId(29878),
                "ProvisionableDevice/IsSingleton" => new NodeId(29879),
                "ProvisionableDevice/RequestTickets" => new NodeId(29880),
                "ProvisionableDevice/RequestTickets/OutputArguments" => new NodeId(29881),
                _ => new NodeId(path, namespaceIndex)
            };
            if (!assigned.Add(identifier))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            if (!node.NodeId.IsNull)
            {
                mapping[node.NodeId] = identifier;
            }
            node.NodeId = identifier;
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                string name = child.BrowseName.Name ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                AssignStableIdentifiers(
                    context,
                    child,
                    path + "/" + Uri.EscapeDataString(name),
                    namespaceIndex,
                    mapping,
                    assigned);
            }
        }

        /// <summary>The borrowed runtime shared with the device's generated methods.</summary>
        private readonly IProvisionableDeviceRuntime m_runtime;

        /// <summary>The optional provider for application components on the device.</summary>
        private readonly IProvisionableDeviceApplicationsProvider? m_applicationsProvider;

        /// <summary>The optional device trust gate used by application push bindings.</summary>
        private readonly IProvisionableDeviceSecurity? m_security;

        /// <summary>The optional software-update gate used by application push bindings.</summary>
        private readonly IProvisionableDeviceSoftwareUpdatePolicy? m_softwareUpdates;

        /// <summary>
        /// Owns the generated device and its application components while leaving Resources to its standard manager.
        /// </summary>
        private sealed partial class ProvisionableDeviceNodeManager :
            AsyncCustomNodeManager, INodeManagerShutdown, INodeManagerNodeOverrides
        {
            /// <summary>
            /// Creates the namespace-specific manager for one provisionable device.
            /// </summary>
            public ProvisionableDeviceNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                IProvisionableDeviceRuntime runtime,
                IProvisionableDeviceApplicationsProvider? applicationsProvider,
                IProvisionableDeviceSecurity? security,
                IProvisionableDeviceSoftwareUpdatePolicy? softwareUpdates,
                ProvisionableDeviceNodeManager? predecessor)
                : base(server, configuration, server.Telemetry.CreateLogger<ProvisionableDeviceNodeManager>())
            {
                m_runtime = runtime;
                m_applicationsProvider = applicationsProvider;
                m_security = security;
                m_softwareUpdates = softwareUpdates;
                m_managementConfiguration = configuration;
                m_predecessor = predecessor;
                m_calls = new DeviceCalls(this, server, runtime, predecessor?.Singleton);
                NamespaceUris = [NamespaceUri];
            }

            /// <inheritdoc/>
            public ArrayOf<NodeId> OverriddenNodeIds => s_nodeOverrides;

            /// <inheritdoc/>
            protected override bool IsNodeIdInNamespace(NodeId nodeId)
            {
                return base.IsNodeIdInNamespace(nodeId) || OverriddenNodeIds.Contains(nodeId);
            }

            /// <inheritdoc/>
            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProvisionableDeviceState device = CreateProvisionableDevice(context, m_calls);
                m_device = device;
                return new ValueTask<NodeStateCollection>([device]);
            }

            /// <inheritdoc/>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                ProvisionableDeviceState device = m_device ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                AddExternalReference(
                    sourceId: Ua.ObjectIds.Resources,
                    referenceTypeId: ReferenceTypeIds.Organizes,
                    isInverse: false,
                    targetId: device.NodeId,
                    externalReferences: externalReferences);
                foreach (NodeId nodeId in s_nodeOverrides)
                {
                    if (FindPredefinedNode<NodeState>(nodeId) is null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError, $"The required device node {nodeId} is missing.");
                    }
                }
                NodeState? resources = Server.DiagnosticsNodeManager.FindPredefinedNode<NodeState>(
                    Ua.ObjectIds.Resources);
                if (resources is null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "The standard Resources node is missing.");
                }
                if (m_predecessor is { } predecessor)
                {
                    foreach (ApplicationSurface surface in predecessor.m_applicationSurfaces)
                    {
                        await CreateApplicationAsync(device, surface.Descriptor, surface.Binding,
                            externalReferences, cancellationToken).ConfigureAwait(false);
                    }
                    m_predecessor = null;
                    return;
                }
                if (m_applicationsProvider == null)
                {
                    return;
                }

                ArrayOf<ManagedApplicationPushRegistration> applications = await m_applicationsProvider
                    .GetApplicationsAsync(cancellationToken).ConfigureAwait(false);
                if (applications.Count == 0)
                {
                    return;
                }
                if (m_runtime.IsSingleton)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "A singleton provisionable device cannot contain application components.");
                }
                IProvisionableDeviceSecurity security = m_security ??
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "Application components require a provisionable-device security provider.");
                var applicationUris = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    for (int i = 0; i < applications.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ManagedApplicationPushRegistration registration = applications[i] ??
                            throw new ServiceResultException(StatusCodes.BadConfigurationError);
                        ApplicationConfiguration configuration = registration.Target.Configuration;
                        string uri = configuration.ApplicationUri ??
                            throw new ServiceResultException(StatusCodes.BadConfigurationError);
                        if (!applicationUris.Add(uri))
                        {
                            throw new ServiceResultException(StatusCodes.BadConfigurationError,
                                "Application URIs on one provisionable device must be unique.");
                        }
                        var descriptor = new ApplicationDescriptor(registration, security, uri,
                            configuration.ApplicationName, configuration.ProductUri, configuration.ApplicationType);
                        await CreateApplicationAsync(device, descriptor, null, externalReferences, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    try
                    {
                        await DisposePushBindingsAsync().ConfigureAwait(false);
                    }
                    catch (Exception disposalError)
                    {
                        throw new AggregateException(error, disposalError);
                    }
                    throw;
                }
            }

            /// <inheritdoc/>
            public override async ValueTask SessionClosingAsync(
                OperationContext context,
                NodeId sessionId,
                bool deleteSubscriptions,
                CancellationToken cancellationToken = default)
            {
                foreach (ApplicationConfigurationPushBinding binding in m_pushBindings)
                {
                    await binding.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                        .ConfigureAwait(false);
                }
                await base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public override ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
            {
                return DisposePushBindingsThenAsync(() => base.DeleteAddressSpaceAsync(cancellationToken));
            }

            /// <inheritdoc/>
            ValueTask INodeManagerShutdown.PrepareForShutdownAsync()
            {
                return DisposePushBindingsAsync();
            }

            /// <inheritdoc/>
            protected override async ValueTask DisposeAsyncCore()
            {
                Exception? bindingFailure = null;
                try
                {
                    await DisposePushBindingsAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    bindingFailure = error;
                }
                try
                {
                    await base.DisposeAsyncCore().ConfigureAwait(false);
                }
                catch (Exception error) when (bindingFailure != null)
                {
                    throw new AggregateException(bindingFailure, error);
                }
                if (bindingFailure != null)
                {
                    ExceptionDispatchInfo.Capture(bindingFailure).Throw();
                }
            }

            /// <summary>
            /// Creates one application node and its independent certificate and trust handlers.
            /// </summary>
            private async ValueTask CreateApplicationAsync(
                ProvisionableDeviceState device,
                ApplicationDescriptor descriptor,
                ApplicationConfigurationPushBinding? predecessor,
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken)
            {
                ManagedApplicationPushRegistration registration = descriptor.Registration;
                string applicationUri = descriptor.Uri;
                ushort namespaceIndex = NamespaceIndexes[0];
                string path = $"Application:{applicationUri}";
                var node = new ApplicationConfigurationState(device);
                node.Create(
                    SystemContext,
                    new NodeId(path, namespaceIndex),
                    new QualifiedName(applicationUri, namespaceIndex),
                    new LocalizedText(descriptor.Name ?? applicationUri),
                    assignNodeIds: false);
                node.AddProductUri(SystemContext);
                node.AddIsNonUaApplication(SystemContext);
                node.WriteMask = AttributeWriteMask.None;
                node.UserWriteMask = AttributeWriteMask.None;
                node.ApplicationUri!.Value = applicationUri;
                node.ProductUri!.Value = descriptor.ProductUri ?? string.Empty;
                node.ApplicationType!.Value = descriptor.Type;
                node.Enabled!.Value = true;
                node.IsNonUaApplication!.Value = false;
                if (registration.ConfigurationFileProvider != null)
                {
                    node.AddConfigurationFile(SystemContext);
                }

                var mapping = new Dictionary<NodeId, NodeId>();
                var assigned = new HashSet<NodeId>();
                AssignStableIdentifiers(
                    SystemContext,
                    node,
                    path, namespaceIndex, mapping, assigned);
                node.UpdateReferenceTargets(SystemContext, mapping);

                ApplicationConfigurationPushBinding? binding = null;
                try
                {
                    if (predecessor == null)
                    {
                        var authorization = new ProvisionableDeviceApplicationAuthorization(
                            applicationUri, descriptor.Security, m_softwareUpdates, registration.Authorization);
                        binding = new ApplicationConfigurationPushBinding(
                            Server, m_managementConfiguration, SystemContext, node,
                            registration.Target, authorization, registration.PendingKeyStore,
                            registration.KeyGenerator, registration.ConfigurationFileProvider,
                            this, m_calls.IsServing, m_calls.Enter, m_calls.Exit);
                        await binding.RecoverConfigurationFileUpdateAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        binding = new ApplicationConfigurationPushBinding(
                            predecessor, SystemContext, node, this, m_calls.IsServing, m_calls.Enter, m_calls.Exit);
                    }
                    await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
                    node.AddReference(ReferenceTypeIds.HasComponent, true, device.NodeId);
                    if (!externalReferences.TryGetValue(device.NodeId, out IList<IReference>? references))
                    {
                        references = [];
                        externalReferences[device.NodeId] = references;
                    }
                    references.Add(new NodeStateReference(ReferenceTypeIds.HasComponent, false, node.NodeId));
                    device.AddChild(node);
                    m_pushBindings.Add(binding);
                    m_applicationSurfaces.Add(new ApplicationSurface(descriptor, binding));
                    binding = null;
                }
                finally
                {
                    if (binding != null)
                    {
                        await binding.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            /// <summary>
            /// Disposes all application handlers before the owning address space or target runtimes are released.
            /// </summary>
            private async ValueTask DisposePushBindingsAsync()
            {
                var disposals = new List<Task>(m_pushBindings.Count);
                foreach (ApplicationConfigurationPushBinding binding in m_pushBindings)
                {
                    disposals.Add(binding.DisposeAsync().AsTask());
                }
                Task all = Task.WhenAll(disposals);
                try
                {
                    await all.ConfigureAwait(false);
                }
                catch
                {
                    if (all.Exception is { } errors)
                    {
                        throw errors;
                    }
                    throw;
                }
            }

            /// <summary>
            /// Preserves handler-disposal failures while still releasing the address space.
            /// </summary>
            private async ValueTask DisposePushBindingsThenAsync(Func<ValueTask> cleanup)
            {
                Exception? bindingFailure = null;
                try
                {
                    await DisposePushBindingsAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    bindingFailure = error;
                }
                try
                {
                    await cleanup().ConfigureAwait(false);
                }
                catch (Exception error) when (bindingFailure != null)
                {
                    throw new AggregateException(bindingFailure, error);
                }
                if (bindingFailure != null)
                {
                    ExceptionDispatchInfo.Capture(bindingFailure).Throw();
                }
            }

            /// <summary>The host-owned device runtime.</summary>
            private readonly IProvisionableDeviceRuntime m_runtime;

            /// <summary>The optional provider for application components.</summary>
            private readonly IProvisionableDeviceApplicationsProvider? m_applicationsProvider;

            /// <summary>The optional device trust gate for application push.</summary>
            private readonly IProvisionableDeviceSecurity? m_security;

            /// <summary>The optional software-update gate for application push.</summary>
            private readonly IProvisionableDeviceSoftwareUpdatePolicy? m_softwareUpdates;

            /// <summary>The host configuration used to isolate child application handlers.</summary>
            private readonly ApplicationConfiguration m_managementConfiguration;

            /// <summary>The independent push handlers owned by this node manager.</summary>
            private readonly List<ApplicationConfigurationPushBinding> m_pushBindings = [];

            private readonly List<ApplicationSurface> m_applicationSurfaces = [];
            private ProvisionableDeviceNodeManager? m_predecessor;

            private sealed record ApplicationDescriptor(
                ManagedApplicationPushRegistration Registration,
                IProvisionableDeviceSecurity Security,
                string Uri,
                string? Name,
                string? ProductUri,
                ApplicationType Type);

            private sealed record ApplicationSurface(
                ApplicationDescriptor Descriptor,
                ApplicationConfigurationPushBinding Binding);

            /// <summary>The device instance loaded into this manager's namespace.</summary>
            private ProvisionableDeviceState? m_device;

            /// <summary>The well-known instance nodes owned by this generation.</summary>
            private static readonly ArrayOf<NodeId> s_nodeOverrides =
                [new NodeId(29878), new NodeId(29879), new NodeId(29880), new NodeId(29881)];
        }
    }
}
