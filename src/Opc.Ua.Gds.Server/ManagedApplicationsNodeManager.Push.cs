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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    public partial class DefaultManagedApplicationsNodeManager
    {
        /// <summary>
        /// Awaits committed application activation and trust effects without waiting for unrelated host effects.
        /// </summary>
        public async ValueTask DrainPendingApplicationChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (ApplicationConfigurationPushBinding binding in m_pushBindings)
            {
                await binding.DrainPendingApplyChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Binds application components on an existing non-singleton device without creating Resources.
        /// Call before publishing the device. This manager owns the child nodes and their push-engine lifetimes.
        /// The device's own runtime methods remain the host's responsibility.
        /// </summary>
        public async ValueTask BindProvisionableDeviceApplicationsAsync(
            ProvisionableDeviceState device,
            IProvisionableDeviceRuntime runtime,
            IProvisionableDeviceSecurity security,
            ArrayOf<ManagedApplicationPushRegistration> applications,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            IProvisionableDeviceSoftwareUpdatePolicy? softwareUpdates = null,
            CancellationToken cancellationToken = default)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (security == null)
            {
                throw new ArgumentNullException(nameof(security));
            }
            if (externalReferences == null)
            {
                throw new ArgumentNullException(nameof(externalReferences));
            }
            if (runtime.IsSingleton || device.IsSingleton?.Value != false)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "ApplicationConfiguration components are only allowed on non-singleton devices.");
            }
            for (int i = 0; i < applications.Count; i++)
            {
                ManagedApplicationPushRegistration registration = applications[i];
                var authorization = new ProvisionableDeviceApplicationAuthorization(
                    registration.Target.Configuration.ApplicationUri!,
                    security,
                    softwareUpdates,
                    registration.Authorization);
                await CreatePushApplicationAsync(
                    new ManagedApplicationPushRegistration(registration.Target, authorization,
                        registration.PendingKeyStore, registration.KeyGenerator,
                        registration.ConfigurationFileProvider),
                    device,
                    externalReferences,
                    cancellationToken).ConfigureAwait(false);
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
        /// Creates real application push methods before adding their nodes to the visible address space.
        /// </summary>
        private async ValueTask CreatePushApplicationAsync(
            ManagedApplicationPushRegistration registration,
            ProvisionableDeviceState? device,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken)
        {
            ApplicationConfiguration configuration = registration.Target.Configuration;
            string applicationUri = configuration.ApplicationUri ??
                throw new ArgumentException("Managed applications require an application URI.", nameof(registration));
            if (m_appNodes.ContainsKey(applicationUri))
            {
                throw new ArgumentException("The application URI is already bound.", nameof(registration));
            }
            var node = new ApplicationConfigurationState(device);
            node.Create(
                SystemContext,
                new NodeId(applicationUri, NamespaceIndexes[0]),
                new QualifiedName(applicationUri, NamespaceIndexes[0]),
                new LocalizedText(configuration.ApplicationName ?? applicationUri),
                true);
            node.ApplicationUri!.Value = applicationUri;
            node.Enabled!.Value = true;
            ApplicationConfigurationPushBinding? binding = null;
            try
            {
                binding = new ApplicationConfigurationPushBinding(
                    Server,
                    m_managementConfiguration,
                    SystemContext,
                    node,
                    registration.Target,
                    registration.Authorization,
                    registration.PendingKeyStore,
                    registration.KeyGenerator,
                    registration.ConfigurationFileProvider);
                await binding.RecoverConfigurationFileUpdateAsync(cancellationToken).ConfigureAwait(false);
                await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
                NodeId parentId = device is null ? ManagedApplicationsFolderId : device.NodeId;
                NodeId referenceType = device == null ? ReferenceTypeIds.Organizes : ReferenceTypeIds.HasComponent;
                node.AddReference(referenceType, true, parentId);
                if (!externalReferences.TryGetValue(parentId, out IList<IReference>? references))
                {
                    references = [];
                    externalReferences[parentId] = references;
                }
                references.Add(new NodeStateReference(referenceType, false, node.NodeId));
                device?.AddChild(node);
                m_appNodes.Add(applicationUri, node);
                m_pushBindings.Add(binding);
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
        /// Drains every independent engine before the owning address space or target runtimes are torn down.
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
        /// Preserves binding failures while still releasing the owning node manager's remaining state.
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

        /// <summary>The externally owned application runtime provider.</summary>
        private readonly IManagedApplicationPushProvider? m_pushProvider;

        /// <summary>The management server's configuration, excluded from child application security.</summary>
        private readonly ApplicationConfiguration m_managementConfiguration;

        /// <summary>The independent engines owned by this node manager.</summary>
        private readonly List<ApplicationConfigurationPushBinding> m_pushBindings = [];
    }
}
