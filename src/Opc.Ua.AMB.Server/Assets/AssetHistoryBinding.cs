/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
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
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// Binds an owner's health variable and asset notifier to the shared historian.
    /// </summary>
    internal sealed class AssetHistoryBinding : IAsyncDisposable
    {
        /// <summary>
        /// Creates a capture owner for the asset's existing namespace manager.
        /// </summary>
        private AssetHistoryBinding(AssetHandle handle, IServerInternal server, IHistorianProvider provider)
        {
            m_handle = handle;
            m_registry = ((IHistorianRegistryProvider)server).HistorianRegistry as IHistorianNodeBindingRegistry
                ?? throw ConfigurationError("AMB history requires atomic scoped-node historian bindings.");
            m_builder = new HistorianBuilder(server);
            m_builder.UseProvider(provider, provisionNodes: false);
        }

        /// <summary>
        /// Gets the history kinds for which capture and provider access were installed.
        /// </summary>
        public AssetHistoryKinds Kinds { get; private set; }

        /// <summary>
        /// Installs only capabilities the provider can actually read and capture.
        /// A requested but unsupported capability aborts registration.
        /// </summary>
        public static async ValueTask<AssetHistoryBinding?> CreateAsync(
            AssetHandle handle,
            IServerInternal server,
            AssetHistoryOptions? options,
            CancellationToken cancellationToken)
        {
            if (options == null || options.Kinds == AssetHistoryKinds.None)
            {
                return null;
            }
            var binding = new AssetHistoryBinding(handle, server, options.Provider);
            try
            {
                await binding.BindAsync(options, cancellationToken).ConfigureAwait(false);
                if (binding.m_health != null)
                {
                    AssetNodes.Register(handle, binding.m_health);
                }
                if ((binding.Kinds & (AssetHistoryKinds.HealthEvents | AssetHistoryKinds.MaintenanceEvents)) != 0)
                {
                    AssetNodes.Register(handle, handle.Asset);
                }
                return binding;
            }
            catch
            {
                try
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    binding.RollBackMetadata();
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            try
            {
                await m_builder.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (m_health != null)
                {
                    m_health.Historizing = m_originalHistorizing;
                    m_health.AccessLevel = m_originalAccessLevel;
                    m_health.UserAccessLevel = m_originalUserAccessLevel;
                }
                if ((Kinds & (AssetHistoryKinds.HealthEvents | AssetHistoryKinds.MaintenanceEvents)) != 0)
                {
                    m_handle.Asset.EventNotifier = m_originalEventNotifier;
                }
                try
                {
                    m_healthBinding?.Dispose();
                }
                finally
                {
                    try
                    {
                        m_eventBinding?.Dispose();
                    }
                    finally
                    {
                        Kinds = AssetHistoryKinds.None;
                    }
                }
            }
        }

        /// <summary>
        /// Installs health data and event capture in the asset owner, never the sidecar.
        /// </summary>
        private async ValueTask BindAsync(AssetHistoryOptions options, CancellationToken cancellationToken)
        {
            IHistorianProvider provider = options.Provider;
            if ((options.Kinds & AssetHistoryKinds.DeviceHealth) != 0 &&
                m_handle.Asset.FindChildWithQualifiedName(
                    m_handle.Context,
                    new QualifiedName(Opc.Ua.Di.BrowseNames.DeviceHealth, m_handle.DiNamespaceIndex))
                    is BaseVariableState health)
            {
                if (health.Historizing)
                {
                    throw ConfigurationError("DeviceHealth is already historized by its owner.");
                }
                if (!m_registry.TryRegisterForNode(health.NodeId, provider, out m_healthBinding))
                {
                    throw ConfigurationError("DeviceHealth already has an exact-node historian provider binding.");
                }
                if (provider is InMemoryHistorianProvider memory)
                {
                    memory.TryRegister(health.NodeId);
                }
                HistorianNodeCapabilities capabilities = await provider.GetCapabilitiesAsync(
                    health.NodeId, cancellationToken).ConfigureAwait(false);
                if (provider is not IHistorianDataProvider data ||
                    !capabilities.ReadRawData || !capabilities.InsertData ||
                    !await provider.IsHistorizingAsync(health.NodeId, cancellationToken).ConfigureAwait(false))
                {
                    throw ConfigurationError(
                        "DeviceHealth history requires provisioned raw read and capture support.");
                }

                m_originalHealthTimestamp = health.Timestamp;
                bool restored = await RestoreTimestampAsync(data, health, cancellationToken).ConfigureAwait(false);
                m_health = health;
                m_originalHistorizing = health.Historizing;
                m_originalAccessLevel = health.AccessLevel;
                m_originalUserAccessLevel = health.UserAccessLevel;
                bool installHealthConfiguration = health.FindChild(
                    m_handle.Context, new QualifiedName(Ua.BrowseNames.HAConfiguration)) == null;
                m_annotationsBefore = health.FindChild(
                    m_handle.Context, new QualifiedName(Ua.BrowseNames.Annotations));
                if (m_annotationsBefore is BaseVariableState annotations)
                {
                    m_originalAnnotationAccessLevel = annotations.AccessLevel;
                    m_originalAnnotationUserAccessLevel = annotations.UserAccessLevel;
                }
                m_healthMetadataTouched = true;
                try
                {
                    m_builder.Historize(
                        health,
                        systemContext: m_handle.Context,
                        capabilities: capabilities,
                        captureOptions: options.Capture);
                }
                finally
                {
                    if (m_annotationsBefore == null)
                    {
                        m_addedAnnotations = health.FindChild(
                            m_handle.Context, new QualifiedName(Ua.BrowseNames.Annotations));
                    }
                }
                if (installHealthConfiguration)
                {
                    try
                    {
                        await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                            m_handle.Context, health, provider, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        m_addedHealthConfiguration = health.FindChild(
                            m_handle.Context, new QualifiedName(Ua.BrowseNames.HAConfiguration));
                    }
                }
                Kinds |= AssetHistoryKinds.DeviceHealth;
                if (!restored)
                {
                    health.UpdateChangeMasks(NodeStateChangeMasks.Value);
                    health.ClearChangeMasks(m_handle.Context, false);
                }
            }

            var eventTypes = new HashSet<NodeId>();
            var excludedTypes = new HashSet<NodeId>();
            var selectedConditions = new List<ConditionState>();
            AssetHistoryKinds events = AssetHistoryKinds.None;
            var children = new List<BaseInstanceState>();
            m_handle.Asset.GetChildren(m_handle.Context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is not ConditionState condition)
                {
                    continue;
                }
                bool maintenance = condition.ConditionClassId != null &&
                    m_handle.Context.TypeTable.IsTypeOf(
                        condition.ConditionClassId.Value, Ua.ObjectTypeIds.MaintenanceConditionClassType);
                AssetHistoryKinds kind = maintenance
                    ? AssetHistoryKinds.MaintenanceEvents
                    : AssetHistoryKinds.HealthEvents;
                if ((options.Kinds & kind) != 0)
                {
                    events |= kind;
                    eventTypes.Add(condition.TypeDefinitionId);
                    selectedConditions.Add(condition);
                }
                else
                {
                    excludedTypes.Add(condition.TypeDefinitionId);
                }
            }
            if (events == AssetHistoryKinds.None)
            {
                return;
            }
            if ((m_handle.Asset.EventNotifier & (EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite)) != 0)
            {
                throw ConfigurationError("The asset notifier is already historized by its owner.");
            }
            foreach (NodeId excludedType in excludedTypes)
            {
                foreach (NodeId selectedType in eventTypes)
                {
                    if (m_handle.Context.TypeTable.IsTypeOf(excludedType, selectedType))
                    {
                        throw ConfigurationError(
                            "Selected and excluded conditions share an event type. " +
                            "Enable server-defined AMB types or select both event history kinds.");
                    }
                }
            }

            ArrayOf<SimpleAttributeOperand> fields = AmbEventFields.Select(m_handle.Context.NamespaceUris);
            if (!m_registry.TryRegisterForNode(m_handle.NodeId, provider, out m_eventBinding))
            {
                throw ConfigurationError("The asset already has an exact-node historian provider binding.");
            }
            if (provider is InMemoryHistorianProvider eventMemory)
            {
                eventMemory.TryRegister(m_handle.NodeId, HistorianNodeCapabilities.EventReadWrite with
                {
                    EventFields = fields,
                    EventTypes = eventTypes.ToArrayOf()
                });
            }
            HistorianNodeCapabilities eventCapabilities = await provider.GetCapabilitiesAsync(
                m_handle.NodeId, cancellationToken).ConfigureAwait(false);
            if (provider is not IHistorianEventProvider ||
                !eventCapabilities.ReadEventHistory || !eventCapabilities.InsertEvent ||
                !await provider.IsHistorizingAsync(m_handle.NodeId, cancellationToken).ConfigureAwait(false))
            {
                throw ConfigurationError("Asset event history requires provisioned event read and capture support.");
            }
            if (!eventCapabilities.EventTypes.IsEmpty)
            {
                foreach (NodeId eventType in eventTypes)
                {
                    bool supported = false;
                    for (int i = 0; i < eventCapabilities.EventTypes.Count; i++)
                    {
                        supported |= m_handle.Context.TypeTable.IsTypeOf(
                            eventType, eventCapabilities.EventTypes[i]);
                    }
                    if (!supported)
                    {
                        throw ConfigurationError("The provider does not retain a configured asset condition type.");
                    }
                }
            }
            m_originalEventNotifier = m_handle.Asset.EventNotifier;
            bool installEventConfiguration = m_handle.Asset.FindChild(
                m_handle.Context, new QualifiedName(Ua.BrowseNames.HAConfiguration)) == null;
            Kinds |= events;
            try
            {
                await m_builder.HistorizeEventsAsync(
                    m_handle.Asset,
                    m_handle.Context,
                    eventNotifier: EventNotifiers.HistoryRead,
                    installConfiguration: installEventConfiguration,
                    capabilities: eventCapabilities with
                    {
                        EventFields = fields,
                        EventTypes = eventTypes.ToArrayOf()
                    },
                    captureOptions: options.Capture,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (installEventConfiguration)
                {
                    m_addedEventConfiguration = m_handle.Asset.FindChild(
                        m_handle.Context, new QualifiedName(Ua.BrowseNames.HAConfiguration));
                }
            }
            foreach (ConditionState condition in selectedConditions)
            {
                AssetNodes.ReportEvent(m_handle.Context, condition);
            }
        }

        /// <summary>
        /// Removes metadata added before a failed registration, leaving existing configuration untouched.
        /// Nodes are not indexed with their owner until the complete binding succeeds.
        /// </summary>
        private void RollBackMetadata()
        {
            if (m_health != null && m_healthMetadataTouched)
            {
                m_health.Timestamp = m_originalHealthTimestamp;
                RemoveAddedMetadata(
                    m_health, m_addedHealthConfiguration, Ua.ReferenceTypeIds.HasHistoricalConfiguration);
                if (m_annotationsBefore == null)
                {
                    RemoveAddedMetadata(m_health, m_addedAnnotations, Ua.ReferenceTypeIds.HasProperty);
                }
                else if (m_annotationsBefore is BaseVariableState annotations)
                {
                    annotations.AccessLevel = m_originalAnnotationAccessLevel;
                    annotations.UserAccessLevel = m_originalAnnotationUserAccessLevel;
                }
            }
            RemoveAddedMetadata(
                m_handle.Asset, m_addedEventConfiguration, Ua.ReferenceTypeIds.HasHistoricalConfiguration);
        }

        /// <summary>
        /// Detaches newly created configuration and both directions of its explicit reference.
        /// </summary>
        private void RemoveAddedMetadata(NodeState parent, BaseInstanceState? child, NodeId referenceType)
        {
            if (child != null && ReferenceEquals(
                parent.FindChild(m_handle.Context, child.BrowseName), child))
            {
                parent.RemoveReference(referenceType, false, child.NodeId);
                child.RemoveReference(referenceType, true, parent.NodeId);
                parent.RemoveChild(child);
            }
        }

        /// <summary>
        /// Keeps the last transition timestamp when the owner's current health agrees with its archive.
        /// A different current health starts a new state at registration.
        /// </summary>
        private async ValueTask<bool> RestoreTimestampAsync(
            IHistorianDataProvider provider,
            BaseVariableState health,
            CancellationToken cancellationToken)
        {
            if (m_handle.Context is not ServerSystemContext context)
            {
                throw ConfigurationError("History capture requires the asset owner's ServerSystemContext.");
            }
            using var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None);
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                new HistorianOperationContext(context, operation, health, HistoryUpdateType.Insert),
                new HistorianRawReadRequest
                {
                    NodeId = health.NodeId,
                    StartTime = DateTimeUtc.MinValue,
                    EndTime = DateTimeUtc.MaxValue,
                    MaxValues = 1,
                    PageLimit = 1,
                    IsForward = false
                },
                default,
                cancellationToken).ConfigureAwait(false);
            if (page.Values.Count == 0)
            {
                return false;
            }
            DataValue last = page.Values[0].Value;
            if (StatusCode.IsGood(last.StatusCode) &&
                last.WrappedValue == health.WrappedValue &&
                !last.SourceTimestamp.IsNull)
            {
                health.Timestamp = last.SourceTimestamp;
                health.ClearChangeMasks(context, false);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reports an invalid requested historian binding instead of silently omitting it.
        /// </summary>
        private static ServiceResultException ConfigurationError(string message)
        {
            return new ServiceResultException(StatusCodes.BadConfigurationError, message);
        }

        /// <summary>The owning asset and node-manager context.</summary>
        private readonly AssetHandle m_handle;

        /// <summary>The shared Part 11 capture pipelines.</summary>
        private readonly HistorianBuilder m_builder;

        /// <summary>The server's provider bindings.</summary>
        private readonly IHistorianNodeBindingRegistry m_registry;

        /// <summary>The raw-data provider binding owned by this capture.</summary>
        private IDisposable? m_healthBinding;

        /// <summary>The event provider binding owned by this capture.</summary>
        private IDisposable? m_eventBinding;

        /// <summary>The owner's annotation property before raw capture setup.</summary>
        private BaseInstanceState? m_annotationsBefore;

        /// <summary>The new raw-history configuration created during this attempt.</summary>
        private BaseInstanceState? m_addedHealthConfiguration;

        /// <summary>The new event-history configuration created during this attempt.</summary>
        private BaseInstanceState? m_addedEventConfiguration;

        /// <summary>The new annotations property created during this attempt.</summary>
        private BaseInstanceState? m_addedAnnotations;

        /// <summary>The original annotation access level.</summary>
        private byte m_originalAnnotationAccessLevel;

        /// <summary>The original per-user annotation access level.</summary>
        private byte m_originalAnnotationUserAccessLevel;

        /// <summary>Whether raw capture began altering metadata.</summary>
        private bool m_healthMetadataTouched;

        /// <summary>The health variable whose binding must be restored on disposal.</summary>
        private BaseVariableState? m_health;

        /// <summary>The owner's original health historizing flag.</summary>
        private bool m_originalHistorizing;

        /// <summary>The owner's state-entry timestamp before history restoration.</summary>
        private DateTimeUtc m_originalHealthTimestamp;

        /// <summary>The owner's original health access level.</summary>
        private byte m_originalAccessLevel;

        /// <summary>The owner's original per-user health access level.</summary>
        private byte m_originalUserAccessLevel;

        /// <summary>The owner's original asset notifier attributes.</summary>
        private byte m_originalEventNotifier;

        /// <summary>Whether this binding has already detached its capture pipelines.</summary>
        private int m_disposed;
    }
}
