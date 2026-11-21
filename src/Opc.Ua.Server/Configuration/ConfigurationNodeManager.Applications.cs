/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
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
        internal void BindApplicationConfiguration(
            ServerSystemContext context,
            ApplicationConfigurationState node,
            IApplicationConfigurationPushTarget target,
            ApplicationConfigurationPushBinding binding)
        {
            m_applicationPushTarget = target;
            m_applicationOperations =
                m_serverConfigurationOptions.PushAuthorization as IApplicationConfigurationPushOperations;
            m_serverConfigurationNode = node;
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

            CertificateGroupFolderState groups = node.CertificateGroups ??
                throw new ArgumentException("The application has no certificate-groups folder.", nameof(node));
            var groupIds = new HashSet<NodeId>();
            foreach (ServerCertificateGroup group in m_certificateGroups)
            {
                if (group.BrowseName == BrowseNames.DefaultHttpsGroup)
                {
                    groups.AddDefaultHttpsGroup(context);
                    group.Node = groups.DefaultHttpsGroup!;
                }
                else if (group.BrowseName == BrowseNames.DefaultUserTokenGroup)
                {
                    groups.AddDefaultUserTokenGroup(context);
                    group.Node = groups.DefaultUserTokenGroup!;
                }
                else
                {
                    group.Node = groups.DefaultApplicationGroup!;
                }
                NodeId id = group.Node is null ? NodeId.Null : group.Node.NodeId;
                if (id.IsNull || id.NamespaceIndex == 0 || !groupIds.Add(id) ||
                    group.Node?.TrustList == null || group.Node.TrustList.NodeId.NamespaceIndex == 0)
                {
                    throw new ArgumentException("Application certificate groups require distinct own-namespace ids.",
                        nameof(node));
                }
                group.NodeId = id;
            }

            ConfigurePushConfiguration(context, m_configuration);
            WrapPushCalls(node, binding);
            foreach (ServerCertificateGroup group in m_certificateGroups)
            {
                WrapTrustListCalls(group.Node!.TrustList!, binding);
            }
            if (node.ConfigurationFile != null)
            {
                WrapConfigurationFileCalls(node.ConfigurationFile, binding);
            }
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
        private void RegisterSecurityAdminCallPermissions(ISystemContext context, NodeState node)
        {
            if (node is MethodState or BaseObjectState)
            {
                RegisterSecurityAdminCallPermissions(this, node);
            }

            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                RegisterSecurityAdminCallPermissions(context, child);
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
    }
}
