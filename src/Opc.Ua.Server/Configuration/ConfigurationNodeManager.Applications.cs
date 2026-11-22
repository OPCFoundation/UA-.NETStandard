// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    public partial class ConfigurationNodeManager
    {
        /// <summary>
        /// Identifies pending-key ownership without exposing or sharing the host's store.
        /// </summary>
        internal bool UsesPendingKeyStore(IPendingCertificateKeyStore store)
        {
            return ReferenceEquals(m_pendingKeyStore, store);
        }

        /// <summary>
        /// Excludes the host's provider from independently managed application configuration.
        /// </summary>
        internal bool UsesConfigurationFileProvider(IApplicationConfigurationFileProvider provider)
        {
            return ReferenceEquals(m_serverConfigurationOptions.ConfigurationFileProvider, provider);
        }

        /// <summary>
        /// Checks against the host engine's actual configuration rather than relying on a caller's descriptor.
        /// </summary>
        internal void ValidateManagedApplicationIsolation(ApplicationConfiguration application)
        {
            ApplicationConfigurationPushBinding.ValidateIsolation(application, m_configuration);
        }

        /// <summary>
        /// Reuses the push engine on an ApplicationConfiguration instance without loading host diagnostics.
        /// </summary>
        internal ApplicationPushAttachment BindApplicationConfiguration(
            ServerSystemContext context,
            ApplicationConfigurationState node,
            IApplicationConfigurationPushTarget target,
            ApplicationConfigurationPushBinding binding)
        {
            m_applicationPushTarget = target;
            m_applicationOperations =
                m_serverConfigurationOptions.PushAuthorization as IApplicationConfigurationPushOperations;
            ApplicationPushAttachment attachment = AttachApplicationPushNodes(context, node, binding);
            try
            {
                SubscribeTrustMaterialEnforcement();
                return attachment;
            }
            catch
            {
                attachment.Dispose();
                throw;
            }
        }

        internal ApplicationPushAttachment AttachApplicationPushNodes(
            ServerSystemContext context,
            ApplicationConfigurationState node,
            ApplicationConfigurationPushBinding binding,
            ApplicationPushAttachment? previous = null)
        {
            PrepareApplicationPushNodes(context, node);
            List<(ServerCertificateGroup Group, CertificateGroupState Node)> groups =
                GetApplicationCertificateGroupNodes(context, node);
            foreach ((ServerCertificateGroup group, CertificateGroupState groupNode) in groups)
            {
                if (previous == null)
                {
                    group.NodeId = groupNode.NodeId;
                    group.TrustListId = groupNode.TrustList!.NodeId;
                }
                else if (group.NodeId != groupNode.NodeId || group.TrustListId != groupNode.TrustList!.NodeId)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                }
            }

            var attachment = new ApplicationPushAttachment(this, context, node, groups, binding);
            try
            {
                ConfigurePushMethodNodes(node, m_configuration);
                ConfigureOptionalServerConfigurationSurface(node, m_configuration);
                attachment.File = ConfigureConfigurationFile(context, node, previous?.Node.ConfigurationFile);
                if (previous == null)
                {
                    ValidateSecurityAdminPermissionDefaults(node);
                }
                RegisterSecurityAdminCallPermissions(context, node, binding.PermissionOwner ?? this);
                WrapPushCalls(node, binding);
                foreach ((ServerCertificateGroup group, CertificateGroupState groupNode) in groups)
                {
                    ConfigurePushTrustList(context, group, groupNode);
                    WrapTrustListCalls(groupNode.TrustList!, binding);
                }
                if (node.ConfigurationFile != null)
                {
                    WrapConfigurationFileCalls(node.ConfigurationFile, binding);
                }
                attachment.RefreshDiagnostics();
                lock (m_applicationAttachmentLock)
                {
                    Volatile.Write(ref m_applicationAttachments, [.. m_applicationAttachments, attachment]);
                }
                return attachment;
            }
            catch
            {
                attachment.Dispose();
                throw;
            }
        }

        private void UpdateApplicationDiagnostics(ISystemContext context)
        {
            foreach (ApplicationPushAttachment attachment in Volatile.Read(ref m_applicationAttachments))
            {
                if (attachment.IsServing)
                {
                    UpdateTransactionDiagnostics(context, attachment.Node.TransactionDiagnostics);
                }
            }
        }

        /// <summary>
        /// Materializes the managed application's push surface without consulting its providers.
        /// </summary>
        private void PrepareApplicationPushNodes(ServerSystemContext context, ApplicationConfigurationState node)
        {
            node.AddGetCertificates(context)
                .AddCreateSelfSignedCertificate(context)
                .AddDeleteCertificate(context)
                .AddCancelChanges(context)
                .AddSupportsTransactions(context)
                .AddTransactionDiagnostics(context)
                .AddApplicationUri(context)
                .AddProductUri(context)
                .AddApplicationType(context)
                .AddApplicationNames(context);
            if (m_serverConfigurationOptions.ConfigurationFileProvider != null)
            {
                node.AddConfigurationFile(context);
            }
            else
            {
                node.ConfigurationFile = null;
            }
        }

        /// <summary>
        /// Resolves group nodes in the supplied tree without changing the engine's group descriptors.
        /// </summary>
        private List<(ServerCertificateGroup Group, CertificateGroupState Node)> GetApplicationCertificateGroupNodes(
            ServerSystemContext context, ApplicationConfigurationState node)
        {
            CertificateGroupFolderState groups = node.CertificateGroups ??
                throw new ArgumentException("The application has no certificate-groups folder.", nameof(node));
            var groupIds = new HashSet<NodeId>();
            var groupNodes = new List<(ServerCertificateGroup, CertificateGroupState)>(m_certificateGroups.Count);
            foreach (ServerCertificateGroup group in m_certificateGroups)
            {
                CertificateGroupState? groupNode;
                if (group.BrowseName == BrowseNames.DefaultHttpsGroup)
                {
                    groups.AddDefaultHttpsGroup(context);
                    groupNode = groups.DefaultHttpsGroup;
                }
                else if (group.BrowseName == BrowseNames.DefaultUserTokenGroup)
                {
                    groups.AddDefaultUserTokenGroup(context);
                    groupNode = groups.DefaultUserTokenGroup;
                }
                else
                {
                    groupNode = groups.DefaultApplicationGroup;
                }
                if (groupNode?.TrustList == null || groupNode.NodeId.IsNull ||
                    groupNode.NodeId.NamespaceIndex == 0 || !groupIds.Add(groupNode.NodeId) ||
                    groupNode.TrustList.NodeId.NamespaceIndex == 0)
                {
                    throw new ArgumentException("Application certificate groups require distinct own-namespace ids.",
                        nameof(node));
                }
                groupNodes.Add((group, groupNode));
            }
            return groupNodes;
        }

        /// <summary>
        /// Registers SecurityAdmin Browse and Call permissions without materializing namespace defaults.
        /// </summary>
        private void GrantSecurityAdminCallPermissions(ISystemContext context, NodeState node)
        {
            RevokeSecurityAdminCallPermissions();
            ValidateSecurityAdminPermissionDefaults(node);
            RegisterSecurityAdminCallPermissions(context, node);
        }

        /// <summary>
        /// Preserves synchronous startup's default-policy validation without storing inherited permissions.
        /// </summary>
        private void ValidateSecurityAdminPermissionDefaults(NodeState node)
        {
            if (node is MethodState or BaseObjectState && node.RolePermissions.IsEmpty)
            {
                NamespaceDefaultPermissions defaults = NamespaceDefaultPermissionReader.Read(
                    Server, SystemContext, node.NodeId.NamespaceIndex);
                if (defaults.Status != StatusCodes.BadWouldBlock && !StatusCode.IsGood(defaults.Status))
                {
                    throw new ServiceResultException(defaults.Status);
                }
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                ValidateSecurityAdminPermissionDefaults(child);
            }
        }

        /// <summary>
        /// Registers the current configuration tree after retiring any previous tree's grants.
        /// </summary>
        private void RegisterSecurityAdminCallPermissions(
            ISystemContext context, NodeState node, AsyncCustomNodeManager? owner = null)
        {
            if (node is MethodState or BaseObjectState)
            {
                RegisterSecurityAdminCallPermissions(owner ?? this, node);
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                RegisterSecurityAdminCallPermissions(context, child, owner);
            }
        }

        /// <summary>
        /// Preserves asynchronous startup's default-policy validation before registering live grants.
        /// </summary>
        private async ValueTask GrantSecurityAdminCallPermissionsAsync(
            ServerSystemContext context,
            NodeState node,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevokeSecurityAdminCallPermissions();
            await ValidateSecurityAdminPermissionDefaultsAsync(context, node, cancellationToken).ConfigureAwait(false);
            RegisterSecurityAdminCallPermissions(context, node);
        }

        /// <summary>
        /// Rejects unresolved or failed inherited policies during asynchronous configuration.
        /// </summary>
        private async ValueTask ValidateSecurityAdminPermissionDefaultsAsync(
            ServerSystemContext context,
            NodeState node,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is MethodState or BaseObjectState && node.RolePermissions.IsEmpty)
            {
                NamespaceDefaultPermissions defaults = await NamespaceDefaultPermissionReader.ReadAsync(
                    Server, context, node.NodeId.NamespaceIndex, cancellationToken).ConfigureAwait(false);
                if (!StatusCode.IsGood(defaults.Status))
                {
                    throw new ServiceResultException(defaults.Status);
                }
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                await ValidateSecurityAdminPermissionDefaultsAsync(context, child, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Admits all application file operations through the same deployment, registrar and software policy.
        /// </summary>
        private void WrapConfigurationFileCalls(
            ApplicationConfigurationFileState node,
            ApplicationConfigurationPushBinding binding)
        {
            var open = node.Open!.OnCallAsync!;
            node.Open.OnCallAsync = (c, m, o, mode, ct) =>
                RunAsync(c, () => open(c, m, o, mode, ct), ct);
            var read = node.Read!.OnCallAsync!;
            node.Read.OnCallAsync = (c, m, o, handle, length, ct) =>
                RunAsync(c, () => read(c, m, o, handle, length, ct), ct);
            var write = node.Write!.OnCallAsync!;
            node.Write.OnCallAsync = (c, m, o, handle, data, ct) =>
                RunAsync(c, () => write(c, m, o, handle, data, ct), ct);
            var close = node.Close!.OnCallAsync!;
            node.Close.OnCallAsync = (c, m, o, handle, ct) =>
                RunAsync(c, () => close(c, m, o, handle, ct), ct);
            var getPosition = node.GetPosition!.OnCallAsync!;
            node.GetPosition.OnCallAsync = (c, m, o, handle, ct) =>
                RunAsync(c, () => getPosition(c, m, o, handle, ct), ct);
            var setPosition = node.SetPosition!.OnCallAsync!;
            node.SetPosition.OnCallAsync = (c, m, o, handle, position, ct) =>
                RunAsync(c, () => setPosition(c, m, o, handle, position, ct), ct);
            var update = node.CloseAndUpdate!.OnCallAsync!;
            node.CloseAndUpdate.OnCallAsync = (c, m, o, handle, version, targets, revert, restart, ct) =>
                RunAsync(c, () => update(c, m, o, handle, version, targets, revert, restart, ct), ct);
            var confirm = node.ConfirmUpdate!.OnCallAsync!;
            node.ConfirmUpdate.OnCallAsync = (c, m, o, id, ct) =>
                RunAsync(c, () => confirm(c, m, o, id, ct), ct);

            ValueTask<T> RunAsync<T>(ISystemContext context, Func<ValueTask<T>> action, CancellationToken ct)
            {
                return binding.RunAsync(async () =>
                {
                    await AuthorizePushMutationAsync(context, false, ct).ConfigureAwait(false);
                    return await action().ConfigureAwait(false);
                }, ct);
            }
        }

        /// <summary>
        /// Retains the engine until each typed push call has completed, including authorization and store access.
        /// </summary>
        private void WrapPushCalls(
            ApplicationConfigurationState node,
            ApplicationConfigurationPushBinding binding)
        {
            var update = node.UpdateCertificate!.OnCallAsync!;
            node.UpdateCertificate.OnCall = null;
            node.UpdateCertificate.OnCallAsync = (c, m, o, g, t, cert, issuers, format, key, ct) =>
                binding.RunAsync(() => update(c, m, o, g, t, cert, issuers, format, key, ct), ct);
            var request = node.CreateSigningRequest!.OnCallAsync!;
            node.CreateSigningRequest.OnCall = null;
            node.CreateSigningRequest.OnCallAsync = (c, m, o, g, t, subject, regenerate, nonce, ct) =>
                binding.RunAsync(() => request(c, m, o, g, t, subject, regenerate, nonce, ct), ct);
            var create = node.CreateSelfSignedCertificate!.OnCallAsync!;
            node.CreateSelfSignedCertificate.OnCall = null;
            node.CreateSelfSignedCertificate.OnCallAsync =
                (c, m, o, g, t, subject, dns, ips, lifetime, keySize, ct) =>
                    binding.RunAsync(() =>
                        create(c, m, o, g, t, subject, dns, ips, lifetime, keySize, ct), ct);
            var delete = node.DeleteCertificate!.OnCallAsync!;
            node.DeleteCertificate.OnCall = null;
            node.DeleteCertificate.OnCallAsync = (c, m, o, g, t, ct) =>
                binding.RunAsync(() => delete(c, m, o, g, t, ct), ct);
            var apply = node.ApplyChanges!.OnCallMethod2Async!;
            node.ApplyChanges.OnCallMethod2Async = (c, m, o, inputs, outputs, ct) =>
                binding.RunAsync(() => apply(c, m, o, inputs, outputs, ct), ct);
            var cancel = node.CancelChanges!.OnCallMethod2Async!;
            node.CancelChanges.OnCallMethod2Async = (c, m, o, inputs, outputs, ct) =>
                binding.RunAsync(() => cancel(c, m, o, inputs, outputs, ct), ct);
            node.GetCertificates!.OnCall = null;
            node.GetCertificates.OnCallAsync = (c, m, o, g, ct) =>
                binding.RunAsync(() => GetApplicationCertificatesAsync(c, m, o, g, ct), ct);
            node.GetRejectedList!.OnCall = null;
            node.GetRejectedList.OnCallAsync = (c, _, _, ct) =>
                binding.RunAsync(() => GetApplicationRejectedListAsync(c, ct), ct);
        }

        /// <summary>
        /// Keeps trust-list reads, writes and closes inside the binding's asynchronous lifetime.
        /// </summary>
        private static void WrapTrustListCalls(TrustListState node, ApplicationConfigurationPushBinding binding)
        {
            var open = node.Open!.OnCallAsync!;
            node.Open.OnCallAsync = (c, m, o, mode, ct) =>
                binding.RunAsync(() => open(c, m, o, mode, ct), ct);
            var masks = node.OpenWithMasks!.OnCallAsync!;
            node.OpenWithMasks.OnCallAsync = (c, m, o, mask, ct) =>
                binding.RunAsync(() => masks(c, m, o, mask, ct), ct);
            var read = node.Read!.OnCallAsync!;
            node.Read.OnCallAsync = (c, m, o, handle, length, ct) =>
                binding.RunAsync(() => read(c, m, o, handle, length, ct), ct);
            var write = node.Write!.OnCallAsync!;
            node.Write.OnCallAsync = (c, m, o, handle, data, ct) =>
                binding.RunAsync(() => write(c, m, o, handle, data, ct), ct);
            var close = node.Close!.OnCallAsync!;
            node.Close.OnCallAsync = (c, m, o, handle, ct) =>
                binding.RunAsync(() => close(c, m, o, handle, ct), ct);
            var update = node.CloseAndUpdate!.OnCallAsync!;
            node.CloseAndUpdate.OnCallAsync = (c, m, o, handle, ct) =>
                binding.RunAsync(() => update(c, m, o, handle, ct), ct);
            var add = node.AddCertificate!.OnCallAsync!;
            node.AddCertificate.OnCallAsync = (c, m, o, certificate, trusted, ct) =>
                binding.RunAsync(() => add(c, m, o, certificate, trusted, ct), ct);
            var remove = node.RemoveCertificate!.OnCallAsync!;
            node.RemoveCertificate.OnCallAsync = (c, m, o, thumbprint, trusted, ct) =>
                binding.RunAsync(() => remove(c, m, o, thumbprint, trusted, ct), ct);
        }

        /// <summary>
        /// Authorizes certificate reads against the device and software gate before selecting occupied slots.
        /// </summary>
        private async ValueTask<GetCertificatesMethodStateResult> GetApplicationCertificatesAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId groupId,
            CancellationToken cancellationToken)
        {
            await AuthorizePushMutationAsync(context, false, cancellationToken).ConfigureAwait(false);
            ArrayOf<NodeId> types = [];
            ArrayOf<ByteString> certificates = [];
            ServiceResult result = GetCertificates(context, method, objectId, groupId, ref types, ref certificates);
            return new GetCertificatesMethodStateResult
            {
                ServiceResult = result,
                CertificateTypeIds = types,
                Certificates = certificates
            };
        }

        /// <summary>
        /// Reads the target rejected store asynchronously, without the legacy synchronous compatibility shim.
        /// </summary>
        private async ValueTask<GetRejectedListMethodStateResult> GetApplicationRejectedListAsync(
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            await AuthorizePushMutationAsync(context, false, cancellationToken).ConfigureAwait(false);
            var certificates = new List<ByteString>();
            if (m_rejectedStore != null)
            {
                using CertificateCollection rejected = await GetRejectedStore().EnumerateAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (Certificate certificate in rejected)
                {
                    certificates.Add(certificate.RawData.ToByteString());
                }
            }
            return new GetRejectedListMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Certificates = certificates.ToArrayOf()
            };
        }

        /// <summary>
        /// Applies committed effects only to the managed application's registry and runtime.
        /// Deployment admission covers registry reload as well as channel and listener effects.
        /// </summary>
        private ValueTask ApplyManagedApplicationChangesAsync(
            List<PendingCertificateRotation> rotations,
            ArrayOf<NodeId> certificateGroups,
            List<TrustListChangeEffect> trustListEffects)
        {
            return m_applicationOperations == null
                ? ApplyEffectsAsync()
                : m_applicationOperations.ExecuteAsync(ApplyEffectsAsync);

            async ValueTask ApplyEffectsAsync()
            {
                IApplicationConfigurationPushTarget target = m_applicationPushTarget!;
                if (certificateGroups.Count > 0)
                {
                    await m_configuration.CertificateManager!.UpdateAsync(
                        m_configuration.SecurityConfiguration,
                        m_configuration.ApplicationUri,
                        CancellationToken.None).ConfigureAwait(false);
                    var previousCertificates = new List<ApplicationCertificateRotation>();
                    foreach (PendingCertificateRotation rotation in rotations)
                    {
                        if (rotation.OldCertificate != null)
                        {
                            previousCertificates.Add(new ApplicationCertificateRotation(
                                rotation.CertificateType, rotation.OldCertificate.RawData.ToByteString()));
                        }
                    }
                    await target.ApplyCertificateChangesAsync(
                        certificateGroups, previousCertificates.ToArrayOf(), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                if (trustListEffects.Count > 0)
                {
                    NotifyTrustMaterialChanged(trustListEffects);
                    await target.ApplyTrustListChangesAsync(
                        trustListEffects.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
                }
                ReevaluateCertificateAlarms();
            }
        }

        /// <summary>
        /// Applies externally observed trust changes within the same deployment admission as method effects.
        /// </summary>
        private ValueTask ApplyManagedApplicationTrustChangesAsync(
            ArrayOf<TrustListChangeEffect> effects,
            CancellationToken cancellationToken)
        {
            return m_applicationOperations == null
                ? m_applicationPushTarget!.ApplyTrustListChangesAsync(effects, cancellationToken)
                : m_applicationOperations.ExecuteAsync(
                    () => m_applicationPushTarget!.ApplyTrustListChangesAsync(effects, cancellationToken),
                    cancellationToken);
        }

        /// <summary>
        /// Selects target-only endpoints; absence of a target is the normal ServerConfiguration path.
        /// </summary>
        private ArrayOf<EndpointDescription> PushEndpoints =>
            m_applicationPushTarget != null
                ? m_applicationPushTarget.Endpoints
                : Server is IServerEndpointRegistryProvider provider ? provider.ServerEndpoints : default;

        /// <summary>
        /// Selects the policy registry corresponding to the endpoints being managed.
        /// </summary>
        private ISecurityPolicyRegistry? PushSecurityPolicies =>
            m_applicationPushTarget != null
                ? m_applicationPushTarget.SecurityPolicies
                : (Server as ISecurityPolicyRegistryProvider)?.SecurityPolicyRegistry;

        /// <summary>The application-specific effects provider, absent for the management server's own engine.</summary>
        private IApplicationConfigurationPushTarget? m_applicationPushTarget;

        /// <summary>The optional deployment admission shared by calls, registry reload and runtime effects.</summary>
        private IApplicationConfigurationPushOperations? m_applicationOperations;

        private readonly Lock m_applicationAttachmentLock = new();
        private ApplicationPushAttachment[] m_applicationAttachments = [];

        /// <summary>
        /// Owns the nodes and handles of one attachment to the managed application's push engine.
        /// </summary>
        internal sealed class ApplicationPushAttachment(
            ConfigurationNodeManager engine,
            ServerSystemContext context,
            ApplicationConfigurationState node,
            List<(ServerCertificateGroup Group, CertificateGroupState Node)> groups,
            ApplicationConfigurationPushBinding binding) : IDisposable
        {
            internal ApplicationConfigurationState Node { get; } = node;

            internal ServerSystemContext Context { get; } = context;

            internal ApplicationConfigurationFile? File { get; set; }

            internal bool IsServing => binding.IsServing;

            internal bool HasOutstandingWork =>
                engine.m_coordinator.IsTransactionActive || engine.m_coordinator.HasOpenTrustListWriter ||
                File?.HasOutstandingAttachmentWork == true ||
                groups.Any(pair => pair.Node.TrustList?.Handle is TrustList { HasOpenHandles: true });

            internal void RefreshFrom(ApplicationPushAttachment previous)
            {
                if (Node.ConfigurationFile is { } file && previous.Node.ConfigurationFile is { } oldFile)
                {
                    file.CurrentVersion!.Value = oldFile.CurrentVersion!.Value;
                    file.LastUpdateTime!.Value = oldFile.LastUpdateTime!.Value;
                }
                RefreshDiagnostics();
            }

            internal void RefreshDiagnostics()
            {
                engine.UpdateTransactionDiagnostics(Context, Node.TransactionDiagnostics);
                Node.ClearChangeMasks(Context, true);
            }

            internal void SessionClosing(NodeId sessionId)
            {
                engine.m_coordinator.CancelForSessionClose(sessionId);
                engine.UpdateApplicationDiagnostics(Context);
                foreach ((_, CertificateGroupState group) in groups)
                {
                    (group.TrustList?.Handle as TrustList)?.NotifySessionClosing(sessionId);
                }
                File?.NotifySessionClosing(sessionId);
            }

            public void Dispose()
            {
                lock (engine.m_applicationAttachmentLock)
                {
                    Volatile.Write(ref engine.m_applicationAttachments,
                        engine.m_applicationAttachments.Where(attachment => attachment != this).ToArray());
                }
                foreach ((_, CertificateGroupState group) in groups)
                {
                    (group.TrustList?.Handle as TrustList)?.Dispose();
                }
                File?.Dispose();
            }
        }
    }
}
