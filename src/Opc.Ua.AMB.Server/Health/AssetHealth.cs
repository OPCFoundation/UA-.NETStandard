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
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Di;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Server.Health
{
    /// <summary>
    /// What an application asked for about the health of an asset.
    /// </summary>
    /// <param name="Name">The name of the alarm.</param>
    /// <param name="Kind">The alarm type.</param>
    /// <param name="ConditionClass">The condition class.</param>
    internal sealed record HealthAlarmRequest(string Name, AssetHealthAlarmKind Kind, AmbConditionClass ConditionClass);

    /// <summary>
    /// The <c>DeviceHealth</c> and the health alarms of an asset
    /// (OPC 10000-110 §9).
    /// </summary>
    /// <remarks>
    /// The nodes live in the node manager that owns the asset. An asset that
    /// does not declare them through its type - a Device Integration device
    /// does - gets <c>2:DeviceHealth</c> and <c>2:DeviceHealthAlarms</c>
    /// together with a <c>HasInterface</c> reference to
    /// <c>2:IDeviceHealthType</c>. Each alarm is created on the asset, so
    /// its <c>SourceNode</c> and <c>SourceName</c> identify the asset as
    /// §9.3 requires, and the <c>DeviceHealthAlarms</c> folder organizes it.
    /// </remarks>
    internal sealed class AssetHealth : IAssetHealth
    {
        /// <summary>
        /// The severity of an inactive alarm, inside the inactive band of
        /// OPC 10000-110 Table 15.
        /// </summary>
        public const ushort InactiveSeverity = 100;

        private AssetHealth(AssetHandle handle, bool deriveFromAlarms, ILogger logger)
        {
            m_handle = handle;
            DerivesFromAlarms = deriveFromAlarms;
            Logger = logger;
        }

        /// <inheritdoc/>
        public DeviceHealthEnumeration? DeviceHealth
        {
            get
            {
                lock (SyncRoot)
                {
                    return m_deviceHealth == null ? null : ReadDeviceHealth(m_deviceHealth);
                }
            }
        }

        /// <inheritdoc/>
        public bool DerivesFromAlarms { get; }

        /// <inheritdoc/>
        public ArrayOf<IAssetHealthAlarm> Alarms
        {
            get
            {
                lock (SyncRoot)
                {
                    var alarms = new IAssetHealthAlarm[m_alarms.Count];
                    for (int ii = 0; ii < alarms.Length; ii++)
                    {
                        alarms[ii] = m_alarms[ii];
                    }
                    return alarms.ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets the number of health alarms.
        /// </summary>
        internal int AlarmCount
        {
            get
            {
                lock (SyncRoot)
                {
                    return m_alarms.Count;
                }
            }
        }

        /// <summary>
        /// Gets the lock that serializes the changes of the health nodes.
        /// </summary>
        internal Lock SyncRoot { get; } = new();

        /// <summary>
        /// Gets the context of the node manager that owns the asset.
        /// </summary>
        internal ISystemContext Context => m_handle.Context;

        /// <summary>
        /// Gets the logger.
        /// </summary>
        internal ILogger Logger { get; }

        /// <summary>
        /// Registers a node created on the asset with its owner.
        /// </summary>
        internal void Register(NodeState node)
        {
            AssetNodes.Register(m_handle, node);
        }

        /// <inheritdoc/>
        public bool TryGetAlarm(string name, [NotNullWhen(true)] out IAssetHealthAlarm? alarm)
        {
            lock (SyncRoot)
            {
                foreach (AssetHealthAlarm candidate in m_alarms)
                {
                    if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                    {
                        alarm = candidate;
                        return true;
                    }
                }
            }
            alarm = null;
            return false;
        }

        /// <inheritdoc/>
        public ValueTask SetDeviceHealthAsync(
            DeviceHealthEnumeration health,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (SyncRoot)
            {
                if (m_deviceHealth == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "Asset '{0}' was registered without DeviceHealth.",
                        m_handle.BrowseName);
                }
                if (DerivesFromAlarms)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "Asset '{0}' derives its DeviceHealth from its alarms.",
                        m_handle.BrowseName);
                }
                WriteDeviceHealth(health);
            }
            return default;
        }

        /// <summary>
        /// Creates the health nodes of an asset while it is registered.
        /// </summary>
        /// <param name="manager">The AMB node manager.</param>
        /// <param name="assetNode">The builder of the asset object.</param>
        /// <param name="handle">The handle of the asset.</param>
        /// <param name="builder">What the application asked for.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// The health, or <see langword="null"/> when the application asked
        /// for neither <c>DeviceHealth</c> nor an alarm.
        /// </returns>
        public static async ValueTask<AssetHealth?> CreateAsync(
            AmbNodeManager manager,
            INodeBuilder<BaseObjectState> assetNode,
            AssetHandle handle,
            AssetBuilder builder,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (!builder.DeviceHealthRequested && builder.HealthAlarms.Count == 0)
            {
                return null;
            }

            // §9.2: an asset that reports health alarms provides DeviceHealth.
            // Without an explicit request it follows the alarms, so it never
            // reads NORMAL while a failure is active.
            bool implied = !builder.DeviceHealthRequested;
            var health = new AssetHealth(handle, builder.DeriveDeviceHealth || implied, logger);
            health.m_deviceHealth = health.EnsureDeviceHealth();
            health.WriteDeviceHealth(builder.InitialDeviceHealth);

            if (builder.HealthAlarms.Count > 0)
            {
                AmbAlarmTypes? types = manager.UseServerDefinedAlarmTypes
                    ? await manager.EnsureAlarmTypesAsync(cancellationToken).ConfigureAwait(false)
                    : null;
                FolderState folder = AssetNodes.EnsureDeviceHealthAlarms(handle);
                foreach (HealthAlarmRequest request in builder.HealthAlarms)
                {
                    var alarm = AssetHealthAlarm.Create(health, assetNode, folder, request, types, manager);
                    lock (health.SyncRoot)
                    {
                        health.m_alarms.Add(alarm);
                    }
                }
            }

            lock (health.SyncRoot)
            {
                health.DeriveDeviceHealth();
            }
            return health;
        }

        /// <summary>
        /// Recomputes <c>DeviceHealth</c> from the active alarms when the asset
        /// derives it; the caller holds the lock.
        /// </summary>
        /// <remarks>
        /// The first active kind in the order of
        /// <see cref="AssetHealthAlarmKind"/> decides, after NAMUR NE 107:
        /// failure before function check before out of specification before
        /// maintenance required.
        /// </remarks>
        internal void DeriveDeviceHealth()
        {
            if (!DerivesFromAlarms || m_deviceHealth == null)
            {
                return;
            }

            AssetHealthAlarmKind? decisive = null;
            foreach (AssetHealthAlarm alarm in m_alarms)
            {
                if (alarm.IsActiveUnlocked && (decisive == null || alarm.Kind < decisive))
                {
                    decisive = alarm.Kind;
                }
            }
            WriteDeviceHealth(decisive switch
            {
                AssetHealthAlarmKind.Failure => DeviceHealthEnumeration.FAILURE,
                AssetHealthAlarmKind.CheckFunction => DeviceHealthEnumeration.CHECK_FUNCTION,
                AssetHealthAlarmKind.OffSpec => DeviceHealthEnumeration.OFF_SPEC,
                AssetHealthAlarmKind.MaintenanceRequired => DeviceHealthEnumeration.MAINTENANCE_REQUIRED,
                _ => DeviceHealthEnumeration.NORMAL
            });
        }

        /// <summary>
        /// Writes <c>DeviceHealth</c>; the source timestamp changes only with
        /// the value, because it marks when the asset entered the state.
        /// </summary>
        private void WriteDeviceHealth(DeviceHealthEnumeration health)
        {
            BaseVariableState variable = m_deviceHealth!;
            if (m_deviceHealthWritten && ReadDeviceHealth(variable) == health)
            {
                return;
            }
            m_deviceHealthWritten = true;

            if (variable is BaseDataVariableState<DeviceHealthEnumeration> typed)
            {
                typed.Value = health;
            }
            else
            {
                variable.WrappedValue = Variant.From((int)health);
            }
            variable.Timestamp = DateTimeUtc.Now;
            variable.ClearChangeMasks(Context, false);
            Logger.DeviceHealthChanged(m_handle.BrowseName, health);
        }

        private static DeviceHealthEnumeration? ReadDeviceHealth(BaseVariableState variable)
        {
            if (variable is BaseDataVariableState<DeviceHealthEnumeration> typed)
            {
                return typed.Value;
            }
            return variable.WrappedValue.TryGetValue(out int value) ? (DeviceHealthEnumeration)value : null;
        }

        private BaseVariableState EnsureDeviceHealth()
        {
            ISystemContext context = Context;
            BaseObjectState asset = m_handle.Asset;
            var browseName = new QualifiedName(Opc.Ua.Di.BrowseNames.DeviceHealth, m_handle.DiNamespaceIndex);
            if (asset.FindChildWithQualifiedName(context, browseName) is BaseVariableState existing)
            {
                return existing;
            }

            BaseVariableState variable;
            if (asset is DeviceState device)
            {
                device.AddDeviceHealth(context);
                variable = device.DeviceHealth!;
            }
            else
            {
                BaseDataVariableState<DeviceHealthEnumeration> created = BaseDataVariableState<DeviceHealthEnumeration>
                    .With<EnumerationBuilder<DeviceHealthEnumeration>>(asset);
                created.SymbolicName = Opc.Ua.Di.BrowseNames.DeviceHealth;
                created.BrowseName = browseName;
                created.DisplayName = new LocalizedText(Opc.Ua.Di.BrowseNames.DeviceHealth);
                created.TypeDefinitionId = Ua.VariableTypeIds.BaseDataVariableType;
                created.ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent;
                created.DataType = ExpandedNodeId.ToNodeId(
                    Opc.Ua.Di.DataTypeIds.DeviceHealthEnumeration,
                    context.NamespaceUris);
                created.ValueRank = ValueRanks.Scalar;
                created.AccessLevel = AccessLevels.CurrentRead;
                created.UserAccessLevel = AccessLevels.CurrentRead;
                created.MinimumSamplingInterval = MinimumSamplingIntervals.Continuous;
                created.Value = DeviceHealthEnumeration.NORMAL;
                asset.AddChild(created);
                AssetNodes.AddInterface(m_handle, Opc.Ua.Di.ObjectTypeIds.IDeviceHealthType);
                variable = created;
            }
            AssetNodes.Register(m_handle, variable);
            return variable;
        }

        private readonly AssetHandle m_handle;
        private readonly List<AssetHealthAlarm> m_alarms = [];
        private BaseVariableState? m_deviceHealth;
        private bool m_deviceHealthWritten;
    }

    /// <summary>
    /// A health alarm of an asset.
    /// </summary>
    internal sealed class AssetHealthAlarm : IAssetHealthAlarm
    {
        private AssetHealthAlarm(
            AssetHealth health,
            HealthAlarmRequest request,
            DeviceHealthDiagnosticAlarmState alarm,
            PropertyState<ArrayOf<RootCauseDataType>> potentialRootCauses)
        {
            m_health = health;
            Name = request.Name;
            Kind = request.Kind;
            ConditionClass = request.ConditionClass;
            m_alarm = alarm;
            m_potentialRootCauses = potentialRootCauses;
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public NodeId NodeId => m_alarm.NodeId;

        /// <inheritdoc/>
        public AssetHealthAlarmKind Kind { get; }

        /// <inheritdoc/>
        public AmbConditionClass ConditionClass { get; }

        /// <inheritdoc/>
        public bool IsActive
        {
            get
            {
                lock (m_health.SyncRoot)
                {
                    return IsActiveUnlocked;
                }
            }
        }

        /// <inheritdoc/>
        public ushort Severity
        {
            get
            {
                lock (m_health.SyncRoot)
                {
                    return m_alarm.Severity!.Value;
                }
            }
        }

        /// <inheritdoc/>
        public ArrayOf<RootCauseDataType> PotentialRootCauses
        {
            get
            {
                lock (m_health.SyncRoot)
                {
                    return m_potentialRootCauses.Value;
                }
            }
        }

        /// <summary>
        /// Gets whether the alarm is active; the caller holds the lock.
        /// </summary>
        internal bool IsActiveUnlocked => m_alarm.ActiveState?.Id?.Value == true;

        /// <inheritdoc/>
        public ValueTask RaiseAsync(
            ushort severity,
            LocalizedText message,
            ArrayOf<RootCauseDataType> potentialRootCauses = default,
            CancellationToken cancellationToken = default)
        {
            if (severity <= AssetFaultSeverities.InactiveMaximum || severity > 1000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(severity),
                    severity,
                    "An active asset health alarm has a severity from 201 to 1000 (OPC 10000-110 Table 15).");
            }
            ArrayOf<RootCauseDataType> rootCauses = AssetRootCauses.Normalize(potentialRootCauses);
            cancellationToken.ThrowIfCancellationRequested();

            ISystemContext context = m_health.Context;
            lock (m_health.SyncRoot)
            {
                m_potentialRootCauses.Value = rootCauses;
                m_potentialRootCauses.Timestamp = DateTimeUtc.Now;
                m_alarm.Message!.Value = message.IsNullOrEmpty
                    ? new LocalizedText(Name + " is active.")
                    : message;
                m_alarm.SetSeverity(context, (EventSeverity)severity);
                if (!IsActiveUnlocked)
                {
                    m_alarm.SetActiveState(context, true);
                    m_alarm.SetAcknowledgedState(context, false);
                }
                m_alarm.Retain!.Value = true;
                AssetNodes.ReportEvent(context, m_alarm);
                m_health.DeriveDeviceHealth();
            }
            m_health.Logger.HealthAlarmRaised(Name, severity);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask ClearAsync(LocalizedText message = default, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ISystemContext context = m_health.Context;
            lock (m_health.SyncRoot)
            {
                if (!IsActiveUnlocked)
                {
                    return default;
                }
                m_alarm.Message!.Value = message.IsNullOrEmpty
                    ? new LocalizedText(Name + " is no longer active.")
                    : message;
                m_alarm.SetActiveState(context, false);
                m_alarm.SetSeverity(context, (EventSeverity)AssetHealth.InactiveSeverity);

                // An unacknowledged alarm stays retained until a client
                // acknowledges it (OPC 10000-9 §5.5.2).
                m_alarm.Retain!.Value = m_alarm.AckedState?.Id?.Value != true;
                AssetNodes.ReportEvent(context, m_alarm);
                m_health.DeriveDeviceHealth();
            }
            m_health.Logger.HealthAlarmCleared(Name);
            return default;
        }

        /// <summary>
        /// Creates the alarm on the asset.
        /// </summary>
        public static AssetHealthAlarm Create(
            AssetHealth health,
            INodeBuilder<BaseObjectState> assetNode,
            FolderState folder,
            HealthAlarmRequest request,
            AmbAlarmTypes? types,
            AmbNodeManager manager)
        {
            ISystemContext context = health.Context;
            BaseObjectState asset = assetNode.Node;
            DeviceHealthDiagnosticAlarmState alarm = assetNode
                .CreateAlarm<DeviceHealthDiagnosticAlarmState>(
                    new QualifiedName(request.Name, asset.NodeId.NamespaceIndex),
                    parent => request.Kind switch
                    {
                        AssetHealthAlarmKind.Failure => new FailureAlarmState(parent),
                        AssetHealthAlarmKind.CheckFunction => new CheckFunctionAlarmState(parent),
                        AssetHealthAlarmKind.OffSpec => new OffSpecAlarmState(parent),
                        _ => new MaintenanceRequiredAlarmState(parent)
                    })
                .Alarm;

            // OPC 10000-110 §9.4.2 applies IRootCauseIndicationType to alarm
            // types: the alarm is an instance of the server-specific subtype,
            // or - without one - references the interface itself.
            NodeId rootCauseInterface = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.IRootCauseIndicationType,
                context.NamespaceUris);
            if (types != null)
            {
                alarm.TypeDefinitionId = types.GetHealthAlarmType(request.Kind);
            }
            else
            {
                alarm.AddReference(Ua.ReferenceTypeIds.HasInterface, false, rootCauseInterface);
            }
            if (alarm.EventType != null)
            {
                alarm.EventType.Value = alarm.TypeDefinitionId;
            }

            PropertyState<ArrayOf<RootCauseDataType>> potentialRootCauses =
                PropertyState<ArrayOf<RootCauseDataType>>.With<StructureBuilder<RootCauseDataType>>(alarm);
            potentialRootCauses.SymbolicName = BrowseNames.PotentialRootCauses;
            potentialRootCauses.BrowseName = new QualifiedName(
                BrowseNames.PotentialRootCauses,
                manager.AmbNamespaceIndex);
            potentialRootCauses.DisplayName = new LocalizedText(BrowseNames.PotentialRootCauses);
            potentialRootCauses.TypeDefinitionId = Ua.VariableTypeIds.PropertyType;
            potentialRootCauses.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
            potentialRootCauses.DataType = ExpandedNodeId.ToNodeId(
                DataTypeIds.RootCauseDataType,
                context.NamespaceUris);
            potentialRootCauses.ValueRank = ValueRanks.OneDimension;
            potentialRootCauses.AccessLevel = AccessLevels.CurrentRead;
            potentialRootCauses.UserAccessLevel = AccessLevels.CurrentRead;
            potentialRootCauses.Value = AssetRootCauses.Unknown;
            alarm.AddChild(potentialRootCauses);
            health.Register(potentialRootCauses);

            alarm.ConditionClassId?.Value = request.ConditionClass.GetTypeId(context.NamespaceUris);
            alarm.ConditionClassName?.Value = new LocalizedText(request.ConditionClass.Name);

            // The folder lists the alarm; the alarm stays a condition of the
            // asset, so the asset is its source (§9.3).
            AssetNodes.Organize(folder, alarm);

            // A client acknowledging or commenting on the alarm changes its
            // state, which OPC 10000-9 reports as an event like any other.
            alarm.AutoReportStateChanges = true;
            alarm.SetAcknowledgedState(context, true);
            alarm.SetActiveState(context, false);
            alarm.SetSeverity(context, (EventSeverity)AssetHealth.InactiveSeverity);
            alarm.Retain!.Value = false;
            alarm.Message!.Value = new LocalizedText(request.Name + " is not active.");
            alarm.ClearChangeMasks(context, true);

            return new AssetHealthAlarm(health, request, alarm, potentialRootCauses);
        }

        private readonly AssetHealth m_health;
        private readonly DeviceHealthDiagnosticAlarmState m_alarm;
        private readonly PropertyState<ArrayOf<RootCauseDataType>> m_potentialRootCauses;
    }

    internal static partial class AssetHealthLog
    {
        [LoggerMessage(
            EventId = AmbServerEventIds.AssetHealth + 0,
            Level = LogLevel.Information,
            Message = "DeviceHealth of asset {BrowseName} is {DeviceHealth}.")]
        public static partial void DeviceHealthChanged(
            this ILogger logger,
            QualifiedName browseName,
            DeviceHealthEnumeration deviceHealth);

        [LoggerMessage(
            EventId = AmbServerEventIds.AssetHealth + 1,
            Level = LogLevel.Information,
            Message = "Health alarm {Name} raised with severity {Severity}.")]
        public static partial void HealthAlarmRaised(this ILogger logger, string name, ushort severity);

        [LoggerMessage(
            EventId = AmbServerEventIds.AssetHealth + 2,
            Level = LogLevel.Information,
            Message = "Health alarm {Name} cleared.")]
        public static partial void HealthAlarmCleared(this ILogger logger, string name);
    }
}
