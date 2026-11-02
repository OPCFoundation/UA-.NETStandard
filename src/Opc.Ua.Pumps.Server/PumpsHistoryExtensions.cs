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
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Activates shared Part 11 history for explicitly selected pump nodes.
    /// </summary>
    /// <remarks>
    /// Providers must already serve the selected NodeIds. No default provider or pump-specific
    /// archive is created. Providers remain caller-owned; the address-space owner drains capture
    /// when it stops. Use the same extensions in direct construction and DI configuration callbacks.
    /// Clients read raw, processed and event history through their session's shared HistoryClient.
    /// These extensions do not establish the inherited Advanced historical-access facets.
    /// </remarks>
    public static class PumpsHistoryExtensions
    {
        /// <summary>
        /// Enables raw history and the provider's processed-history capabilities on selected variables.
        /// </summary>
        /// <remarks>
        /// Configure each node in the provider before calling this method. Activation is performed once,
        /// before publishing values; duplicate or already historizing variables are rejected.
        /// Automatic capture retains the variable's status and source timestamp through HistorianBuilder.
        /// HistoryUpdate access is not granted. Processed reads use the shared dispatcher's native-provider
        /// or raw-data aggregate path only when the provider advertises processed reads.
        /// </remarks>
        /// <param name="runtime">The owning Pumps runtime.</param>
        /// <param name="pump">The pump containing the variables.</param>
        /// <param name="variables">Registered descendants to historize; optional nodes are not materialized.</param>
        /// <param name="provider">The explicitly configured provider, supplied directly or resolved from DI.</param>
        /// <param name="autoCapture">Captures value changes; requires the provider's InsertData capability.</param>
        /// <param name="captureOptions">Shared capture buffering options for this activation.</param>
        /// <param name="cancellationToken">Cancels activation.</param>
        /// <returns>The same pump builder for further configuration.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// A variable is missing, duplicated, or belongs to another pump.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// History is already enabled or the provider cannot serve it.
        /// </exception>
        public static async ValueTask<IPumpBuilder> HistorizeAsync(
            this PumpsRuntime runtime,
            IPumpBuilder pump,
            ArrayOf<BaseVariableState> variables,
            IHistorianProvider provider,
            bool autoCapture = true,
            HistorianCaptureOptions? captureOptions = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePump(runtime, pump);
            SemaphoreSlim gate = s_activationGates.GetValue(runtime, static _ => new(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ValidatePump(runtime, pump);
                if (provider == null)
                {
                    throw new ArgumentNullException(nameof(provider));
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (variables.IsEmpty)
                {
                    throw new ArgumentException("Select at least one pump variable.", nameof(variables));
                }
                if (provider is not IHistorianDataProvider)
                {
                    throw Unsupported("The provider does not implement raw data history.");
                }

                var selected = new HashSet<NodeId>();
                var capabilities = new HistorianNodeCapabilities[variables.Count];
                for (int i = 0; i < variables.Count; i++)
                {
                    BaseVariableState variable = variables[i] ??
                        throw new ArgumentException("A selected variable is null.", nameof(variables));
                    if (!IsDescendant(variable, pump.Pump) ||
                        !ReferenceEquals(runtime.Manager.FindPredefinedNode(variable.NodeId), variable) ||
                        !selected.Add(variable.NodeId))
                    {
                        throw new ArgumentException(
                            "Select distinct, registered variables belonging to this pump.",
                            nameof(variables));
                    }
                    if (variable.Historizing ||
                        variable.FindChild(runtime.SystemContext, new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration))
                            is not null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            "History is already enabled for a selected variable.");
                    }
                    capabilities[i] = await RequireCapabilitiesAsync(provider, variable.NodeId, cancellationToken)
                        .ConfigureAwait(false);
                    if (!capabilities[i].ReadRawData || (autoCapture && !capabilities[i].InsertData))
                    {
                        throw Unsupported(
                            "The provider must support raw reads and, for automatic capture, data insertion.");
                    }
                }

                HistoryActivation? activation = null;
                Exception? activationError = null;
                Exception? rollbackError = null;
                try
                {
                    activation = new(
                        runtime, provider, variables.ConvertAll<NodeState>(static variable => variable));
                    HistorianBuilder historian = activation.Builder;
                    historian.UseProvider(provider);
                    for (int i = 0; i < variables.Count; i++)
                    {
                        await historian.HistorizeAsync(
                            variables[i],
                            runtime.SystemContext,
                            capabilities: capabilities[i],
                            autoCapture: autoCapture,
                            captureOptions: captureOptions,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        activation.RegisterForNode(variables[i].NodeId);
                        await runtime.Manager.AddPredefinedNodeAsync(variables[i], cancellationToken)
                            .ConfigureAwait(false);
                    }
                    ValidatePump(runtime, pump);
                    runtime.Manager.RegisterResource(activation);
                    activation = null;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    activationError = error;
                }
                finally
                {
                    if (activation is not null)
                    {
                        Exception? disposalError = null;
                        try
                        {
                            await activation.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception error) when (error is not OutOfMemoryException)
                        {
                            disposalError = error;
                        }
                        try
                        {
                            await activation.RollbackAsync(disposalError).ConfigureAwait(false);
                        }
                        catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
                        {
                            rollbackError = cleanupError;
                        }
                    }
                }
                ThrowActivationErrors(activationError, rollbackError);
                return pump;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Enables shared event-history capture on the pump's existing event notifier.
        /// </summary>
        /// <remarks>
        /// Configure the pump NodeId in the provider first. Events reported on the pump use the shared
        /// event snapshot pipeline, including event identity, source and event time. Live event delivery
        /// is unchanged. HistoryWrite is not granted, even when archival insertion is supported.
        /// Activation is performed once; no event types, alarms or conditions are manufactured.
        /// </remarks>
        /// <param name="runtime">The owning Pumps runtime.</param>
        /// <param name="pump">The existing pump notifier.</param>
        /// <param name="provider">The explicitly configured event provider.</param>
        /// <param name="autoCapture">Captures reported events; requires InsertEvent support.</param>
        /// <param name="captureOptions">Shared event-capture buffering options.</param>
        /// <param name="cancellationToken">Cancels activation.</param>
        /// <returns>The same pump builder for further configuration.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException">The pump belongs to another owner.</exception>
        /// <exception cref="ServiceResultException">
        /// History is already enabled or the provider cannot serve it.
        /// </exception>
        public static async ValueTask<IPumpBuilder> HistorizeEventsAsync(
            this PumpsRuntime runtime,
            IPumpBuilder pump,
            IHistorianProvider provider,
            bool autoCapture = true,
            HistorianCaptureOptions? captureOptions = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePump(runtime, pump);
            SemaphoreSlim gate = s_activationGates.GetValue(runtime, static _ => new(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ValidatePump(runtime, pump);
                if (provider == null)
                {
                    throw new ArgumentNullException(nameof(provider));
                }
                cancellationToken.ThrowIfCancellationRequested();
                if ((pump.Pump.EventNotifier & (EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite)) != 0 ||
                    pump.Pump.FindChild(runtime.SystemContext, new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration))
                        is not null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Pump event history is already enabled.");
                }
                if (provider is not IHistorianEventProvider)
                {
                    throw Unsupported("The provider does not implement event history.");
                }
                HistorianNodeCapabilities capabilities = await RequireCapabilitiesAsync(
                    provider, pump.NodeId, cancellationToken).ConfigureAwait(false);
                if (!capabilities.ReadEventHistory || (autoCapture && !capabilities.InsertEvent))
                {
                    throw Unsupported(
                        "The provider must support event reads and, for automatic capture, event insertion.");
                }

                HistoryActivation? activation = null;
                Exception? activationError = null;
                Exception? rollbackError = null;
                try
                {
                    activation = new(runtime, provider, [pump.Pump]);
                    HistorianBuilder historian = activation.Builder;
                    historian.UseProvider(provider);
                    await historian.HistorizeEventsAsync(
                        pump.Pump,
                        runtime.SystemContext,
                        eventNotifier: EventNotifiers.HistoryRead,
                        capabilities: capabilities,
                        autoCapture: autoCapture,
                        captureOptions: captureOptions,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    activation.RegisterForNode(pump.NodeId);
                    await runtime.Manager.AddPredefinedNodeAsync(pump.Pump, cancellationToken).ConfigureAwait(false);
                    ValidatePump(runtime, pump);
                    runtime.Manager.RegisterResource(activation);
                    activation = null;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    activationError = error;
                }
                finally
                {
                    if (activation is not null)
                    {
                        Exception? disposalError = null;
                        try
                        {
                            await activation.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception error) when (error is not OutOfMemoryException)
                        {
                            disposalError = error;
                        }
                        try
                        {
                            await activation.RollbackAsync(disposalError).ConfigureAwait(false);
                        }
                        catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
                        {
                            rollbackError = cleanupError;
                        }
                    }
                }
                ThrowActivationErrors(activationError, rollbackError);
                return pump;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Preserves the activation stack and reports rollback failures after cleanup has completed.
        /// </summary>
        private static void ThrowActivationErrors(Exception? activationError, Exception? rollbackError)
        {
            if (activationError is not null && rollbackError is not null)
            {
                throw new AggregateException(
                    "History activation and rollback failed.", activationError, rollbackError);
            }
            if (activationError is not null)
            {
                ExceptionDispatchInfo.Capture(activationError).Throw();
            }
            if (rollbackError is not null)
            {
                ExceptionDispatchInfo.Capture(rollbackError).Throw();
            }
        }

        /// <summary>
        /// Rejects foreign pumps before changing provider bindings or node metadata.
        /// </summary>
        private static void ValidatePump(PumpsRuntime runtime, IPumpBuilder pump)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (pump == null)
            {
                throw new ArgumentNullException(nameof(pump));
            }
            if (!ReferenceEquals(runtime.Pump(pump.NodeId)?.Pump, pump.Pump))
            {
                throw new ArgumentException("The pump must belong to this runtime's owner.", nameof(pump));
            }
        }

        /// <summary>
        /// Checks parent identity rather than namespace membership.
        /// </summary>
        private static bool IsDescendant(BaseInstanceState node, PumpState pump)
        {
            for (NodeState? parent = node.Parent; parent != null; parent = (parent as BaseInstanceState)?.Parent)
            {
                if (ReferenceEquals(parent, pump))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Requires an explicit provider-side node registration without creating a fallback archive.
        /// </summary>
        private static async ValueTask<HistorianNodeCapabilities> RequireCapabilitiesAsync(
            IHistorianProvider provider,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            if (!await provider.IsHistorizingAsync(nodeId, cancellationToken).ConfigureAwait(false))
            {
                throw Unsupported("Configure the selected NodeId in the provider before activating pump history.");
            }
            return await provider.GetCapabilitiesAsync(nodeId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reports an unsupported historian configuration without substituting a provider.
        /// </summary>
        private static ServiceResultException Unsupported(string message)
        {
            return new ServiceResultException(StatusCodes.BadHistoryOperationUnsupported, message);
        }

        /// <summary>
        /// Owns the exact routes and capture handlers installed for one activation.
        /// </summary>
        private sealed class HistoryActivation : IAsyncDisposable
        {
            /// <summary>
            /// Captures the metadata that must survive a failed activation.
            /// </summary>
            public HistoryActivation(PumpsRuntime runtime, IHistorianProvider provider, ArrayOf<NodeState> nodes)
            {
                m_runtime = runtime;
                m_provider = provider;
                m_nodes = new NodeSnapshot[nodes.Count];
                for (int i = 0; i < nodes.Count; i++)
                {
                    NodeState node = nodes[i];
                    m_nodes[i] = new NodeSnapshot(
                        node,
                        node is BaseVariableState variable && variable.Historizing,
                        (node as BaseVariableState)?.AccessLevel ?? 0,
                        (node as BaseVariableState)?.UserAccessLevel ?? 0,
                        (node as BaseObjectState)?.EventNotifier ?? 0,
                        node.FindChild(runtime.SystemContext, new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration)),
                        node.FindChild(runtime.SystemContext, new QualifiedName(Opc.Ua.BrowseNames.Annotations)));
                }
                Builder = new HistorianBuilder(runtime.Manager.Server);
            }

            /// <summary>
            /// The shared capture and configuration builder.
            /// </summary>
            public HistorianBuilder Builder { get; }

            /// <summary>
            /// Adds a route only when no existing application route serves this node.
            /// </summary>
            public void RegisterForNode(NodeId nodeId)
            {
                IHistorianProviderRegistry registry =
                    ((IHistorianRegistryProvider)m_runtime.Manager.Server).HistorianRegistry;
                IHistorianProvider? existing = registry.Resolve(nodeId);
                if (existing is not null)
                {
                    if (!ReferenceEquals(existing, m_provider))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "Another history provider already serves this node.");
                    }
                    return;
                }
                registry.RegisterForNode(nodeId, m_provider);
                m_routes.Add(nodeId);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Builder.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    IHistorianProviderRegistry registry =
                        ((IHistorianRegistryProvider)m_runtime.Manager.Server).HistorianRegistry;
                    foreach (NodeId nodeId in m_routes)
                    {
                        if (ReferenceEquals(registry.Resolve(nodeId), m_provider))
                        {
                            registry.UnregisterForNode(nodeId);
                        }
                    }
                    m_routes.Clear();
                }
            }

            /// <summary>
            /// Restores metadata and removes newly installed configuration after capture and routes are detached.
            /// </summary>
            public async ValueTask RollbackAsync(Exception? disposalError)
            {
                List<Exception>? errors = disposalError is null ? null : [disposalError];
                foreach (NodeSnapshot snapshot in m_nodes)
                {
                    if (snapshot.Node is BaseVariableState variable)
                    {
                        variable.Historizing = snapshot.Historizing;
                        variable.AccessLevel = snapshot.Access;
                        variable.UserAccessLevel = snapshot.UserAccess;
                    }
                    if (snapshot.Node is BaseObjectState notifier)
                    {
                        notifier.EventNotifier = snapshot.Notifier;
                    }
                    foreach ((string name, BaseInstanceState? original) in new[]
                    {
                        (Opc.Ua.BrowseNames.HAConfiguration, snapshot.Configuration),
                        (Opc.Ua.BrowseNames.Annotations, snapshot.Annotations)
                    })
                    {
                        try
                        {
                            await RemoveNewChildAsync(snapshot.Node, name, original).ConfigureAwait(false);
                        }
                        catch (Exception error) when (error is not OutOfMemoryException)
                        {
                            errors ??= [];
                            errors.Add(error);
                        }
                    }
                }
                if (errors is not null)
                {
                    throw new AggregateException(
                        "History rollback could not complete every cleanup operation.", errors);
                }
            }

            /// <summary>
            /// Deletes only a child absent before this activation.
            /// </summary>
            private async ValueTask RemoveNewChildAsync(
                NodeState node, string name, BaseInstanceState? original)
            {
                if (original is null &&
                    node.FindChild(m_runtime.SystemContext, new QualifiedName(name)) is BaseInstanceState added)
                {
                    await m_runtime.Manager.DeleteNodeAsync(
                        m_runtime.Manager.SystemContext, added.NodeId, CancellationToken.None).ConfigureAwait(false);
                    node.RemoveReference(
                        ReferenceTypeIds.HasHistoricalConfiguration, false, added.NodeId);
                    node.RemoveChild(added);
                }
            }

            /// <summary>The owning runtime.</summary>
            private readonly PumpsRuntime m_runtime;

            /// <summary>The caller-owned provider.</summary>
            private readonly IHistorianProvider m_provider;

            /// <summary>The affected nodes and their original metadata.</summary>
            private readonly NodeSnapshot[] m_nodes;

            /// <summary>Exact routes added by this activation, excluding application-owned registrations.</summary>
            private readonly List<NodeId> m_routes = [];
        }

        /// <summary>
        /// Metadata captured before historical configuration is installed.
        /// </summary>
        private sealed record NodeSnapshot(
            NodeState Node,
            bool Historizing,
            byte Access,
            byte UserAccess,
            byte Notifier,
            BaseInstanceState? Configuration,
            BaseInstanceState? Annotations);

        /// <summary>Serializes startup activation without retaining disposed owners.</summary>
        private static readonly ConditionalWeakTable<PumpsRuntime, SemaphoreSlim> s_activationGates = new();
    }
}
