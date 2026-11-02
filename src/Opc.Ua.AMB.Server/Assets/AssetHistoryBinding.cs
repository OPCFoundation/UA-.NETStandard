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
                await binding.StartCaptureAsync(options, cancellationToken).ConfigureAwait(false);
                if (binding.m_health != null)
                {
                    AssetNodes.Register(handle, binding.m_health);
                }
                if ((binding.Kinds & (AssetHistoryKinds.HealthEvents | AssetHistoryKinds.MaintenanceEvents)) != 0)
                {
                    AssetNodes.Register(handle, handle.Asset);
                }
                binding.ReportInitialState();
                return binding;
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                try
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    errors.Add(cleanupFailure);
                }
                try
                {
                    binding.RollBackMetadata();
                }
                catch (Exception rollbackFailure)
                {
                    errors.Add(rollbackFailure);
                }
                if (errors.Count > 1)
                {
                    throw new AggregateException("AMB history binding and its cleanup failed.", errors);
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
            List<Exception>? failures = null;
            try
            {
                await m_builder.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            try
            {
                m_healthBinding?.Dispose();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            try
            {
                m_eventBinding?.Dispose();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            Kinds = AssetHistoryKinds.None;
            if (failures != null)
            {
                throw new AggregateException("Asset history cleanup failed.", failures);
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
                if (!m_registry.TryRegisterForNode(health, provider, out m_healthBinding))
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

                BaseInstanceState? existingConfiguration = health.FindChild(
                    m_handle.Context, new QualifiedName(Ua.BrowseNames.HAConfiguration));
                bool installHealthConfiguration = existingConfiguration == null;
                if (!installHealthConfiguration)
                {
                    var expectedParent = new BaseDataVariableState(null) { NodeId = health.NodeId };
                    HistoricalDataConfigurationState expected =
                        await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                            m_handle.Context, expectedParent, provider, cancellationToken).ConfigureAwait(false);
                    ValidateConfiguration(health, existingConfiguration!, expected);
                }
                m_originalHealthTimestamp = health.Timestamp;
                m_healthRestored = await RestoreTimestampAsync(data, health, cancellationToken).ConfigureAwait(false);
                m_health = health;
                m_healthCapabilities = capabilities;
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
                        autoCapture: false,
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
            if (!m_registry.TryRegisterForNode(m_handle.Asset, provider, out m_eventBinding))
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
            BaseInstanceState? existingEventConfiguration = m_handle.Asset.FindChild(
                m_handle.Context, new QualifiedName(Ua.BrowseNames.HAConfiguration));
            bool installEventConfiguration = existingEventConfiguration == null;
            if (!installEventConfiguration)
            {
                var expectedParent = new BaseObjectState(null) { NodeId = m_handle.NodeId };
                HistoricalEventConfigurationState expected =
                    await HistoricalEventConfigurationInstaller.EnsureInstalledAsync(
                        m_handle.Context, expectedParent, provider, cancellationToken).ConfigureAwait(false);
                ValidateConfiguration(m_handle.Asset, existingEventConfiguration!, expected);
            }
            m_eventCapabilities = eventCapabilities with
            {
                EventFields = fields,
                EventTypes = eventTypes.ToArrayOf()
            };
            m_selectedConditions = selectedConditions;
            Kinds |= events;
            try
            {
                await m_builder.HistorizeEventsAsync(
                    m_handle.Asset,
                    m_handle.Context,
                    eventNotifier: EventNotifiers.HistoryRead,
                    installConfiguration: installEventConfiguration,
                    capabilities: m_eventCapabilities,
                    autoCapture: false,
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
        }

        /// <summary>
        /// Attaches capture only after every requested kind and its metadata have been accepted.
        /// </summary>
        private async ValueTask StartCaptureAsync(
            AssetHistoryOptions options,
            CancellationToken cancellationToken)
        {
            if (m_health != null)
            {
                m_builder.Historize(
                    m_health,
                    systemContext: m_handle.Context,
                    capabilities: m_healthCapabilities,
                    captureOptions: options.Capture);
            }
            if (m_eventCapabilities != null)
            {
                await m_builder.HistorizeEventsAsync(
                    m_handle.Asset,
                    m_handle.Context,
                    eventNotifier: EventNotifiers.HistoryRead,
                    installConfiguration: false,
                    capabilities: m_eventCapabilities,
                    captureOptions: options.Capture,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Emits initial records only after capture setup and owner indexing have completed.
        /// </summary>
        private void ReportInitialState()
        {
            if (m_health != null && !m_healthRestored)
            {
                m_health.UpdateChangeMasks(NodeStateChangeMasks.Value);
                m_health.ClearChangeMasks(m_handle.Context, false);
            }
            foreach (ConditionState condition in m_selectedConditions)
            {
                AssetNodes.ReportEvent(m_handle.Context, condition);
            }
        }

        /// <summary>
        /// Rejects an existing companion that does not describe this provider without changing it.
        /// </summary>
        private void ValidateConfiguration(
            NodeState parent,
            BaseInstanceState existing,
            BaseInstanceState expected)
        {
            if (existing.GetType() != expected.GetType() ||
                existing.NodeId.IsNull ||
                !parent.ReferenceExists(Ua.ReferenceTypeIds.HasHistoricalConfiguration, false, existing.NodeId) ||
                !existing.ReferenceExists(Ua.ReferenceTypeIds.HasHistoricalConfiguration, true, parent.NodeId) ||
                !ConfigurationMatches(existing, expected))
            {
                throw ConfigurationError(
                    "The owner's historical configuration is malformed or disagrees with the selected provider.");
            }
        }

        /// <summary>
        /// Compares the provider-derived companion subtree without comparing instance identifiers.
        /// </summary>
        private bool ConfigurationMatches(BaseInstanceState actual, BaseInstanceState expected)
        {
            if (actual.TypeDefinitionId != expected.TypeDefinitionId ||
                actual.ReferenceTypeId != expected.ReferenceTypeId)
            {
                return false;
            }
            if (expected is BaseVariableState expectedValue &&
                (actual is not BaseVariableState actualValue ||
                    !StatusCode.IsGood(actualValue.StatusCode) ||
                    actualValue.DataType != expectedValue.DataType ||
                    actualValue.ValueRank != expectedValue.ValueRank ||
                    actualValue.WrappedValue != expectedValue.WrappedValue))
            {
                return false;
            }
            var actualChildren = new List<BaseInstanceState>();
            var expectedChildren = new List<BaseInstanceState>();
            actual.GetChildren(m_handle.Context, actualChildren);
            expected.GetChildren(m_handle.Context, expectedChildren);
            if (actualChildren.Count != expectedChildren.Count)
            {
                return false;
            }
            foreach (BaseInstanceState child in expectedChildren)
            {
                BaseInstanceState? actualChild = actual.FindChild(m_handle.Context, child.BrowseName);
                if (actualChild == null || !ConfigurationMatches(actualChild, child))
                {
                    return false;
                }
            }
            var actualReferences = new List<IReference>();
            var expectedReferences = new List<IReference>();
            actual.GetReferences(m_handle.Context, actualReferences);
            expected.GetReferences(m_handle.Context, expectedReferences);
            var actualTargets = new HashSet<ExpandedNodeId>();
            var expectedTargets = new HashSet<ExpandedNodeId>();
            foreach (IReference reference in actualReferences)
            {
                if (!reference.IsInverse && reference.ReferenceTypeId == Ua.ReferenceTypeIds.Organizes)
                {
                    actualTargets.Add(reference.TargetId);
                }
            }
            foreach (IReference reference in expectedReferences)
            {
                if (!reference.IsInverse && reference.ReferenceTypeId == Ua.ReferenceTypeIds.Organizes)
                {
                    expectedTargets.Add(reference.TargetId);
                }
            }
            return actualTargets.SetEquals(expectedTargets);
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

        /// <summary>The owner's state-entry timestamp before history restoration.</summary>
        private DateTimeUtc m_originalHealthTimestamp;

        /// <summary>The accepted raw-history capabilities used when starting capture.</summary>
        private HistorianNodeCapabilities? m_healthCapabilities;

        /// <summary>The accepted event selection used when starting capture.</summary>
        private HistorianNodeCapabilities? m_eventCapabilities;

        /// <summary>The selected live conditions whose initial state is captured after acceptance.</summary>
        private List<ConditionState> m_selectedConditions = [];

        /// <summary>Whether the matching archived health timestamp was restored.</summary>
        private bool m_healthRestored;

        /// <summary>Whether this binding has already detached its capture pipelines.</summary>
        private int m_disposed;
    }
}
