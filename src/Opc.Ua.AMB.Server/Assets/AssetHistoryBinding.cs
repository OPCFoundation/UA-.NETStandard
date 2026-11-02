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
            m_registry = ((IHistorianRegistryProvider)server).HistorianRegistry;
            m_builder = new HistorianBuilder(server);
            m_builder.UseProvider(provider);
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
                return binding;
            }
            catch
            {
                await binding.DisposeAsync().ConfigureAwait(false);
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
                    m_registry.UnregisterForNode(m_health.NodeId);
                    m_health.Historizing = m_originalHistorizing;
                    m_health.AccessLevel = m_originalAccessLevel;
                    m_health.UserAccessLevel = m_originalUserAccessLevel;
                }
                if ((Kinds & (AssetHistoryKinds.HealthEvents | AssetHistoryKinds.MaintenanceEvents)) != 0)
                {
                    m_registry.UnregisterForNode(m_handle.NodeId);
                    m_handle.Asset.EventNotifier = m_originalEventNotifier;
                }
                Kinds = AssetHistoryKinds.None;
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
                if (provider is InMemoryHistorianProvider memory)
                {
                    memory.Register(health.NodeId);
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

                bool restored = await RestoreTimestampAsync(data, health, cancellationToken).ConfigureAwait(false);
                m_health = health;
                m_originalHistorizing = health.Historizing;
                m_originalAccessLevel = health.AccessLevel;
                m_originalUserAccessLevel = health.UserAccessLevel;
                m_builder.RegisterForNode(health.NodeId);
                await m_builder.HistorizeAsync(
                    health,
                    m_handle.Context,
                    capabilities: capabilities,
                    captureOptions: options.Capture,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                AssetNodes.Register(m_handle, health);
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
            if (provider is InMemoryHistorianProvider eventMemory)
            {
                eventMemory.Register(m_handle.NodeId, HistorianNodeCapabilities.EventReadWrite with
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
            m_builder.RegisterForNode(m_handle.NodeId);
            Kinds |= events;
            await m_builder.HistorizeEventsAsync(
                m_handle.Asset,
                m_handle.Context,
                eventNotifier: EventNotifiers.HistoryRead,
                capabilities: eventCapabilities with
                {
                    EventFields = fields,
                    EventTypes = eventTypes.ToArrayOf()
                },
                captureOptions: options.Capture,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            AssetNodes.Register(m_handle, m_handle.Asset);
            foreach (ConditionState condition in selectedConditions)
            {
                AssetNodes.ReportEvent(m_handle.Context, condition);
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
        private readonly IHistorianProviderRegistry m_registry;

        /// <summary>The health variable whose binding must be restored on disposal.</summary>
        private BaseVariableState? m_health;

        /// <summary>The owner's original health historizing flag.</summary>
        private bool m_originalHistorizing;

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
