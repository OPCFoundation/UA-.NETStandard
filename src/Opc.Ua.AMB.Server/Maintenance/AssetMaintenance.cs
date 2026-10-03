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
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.Di;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Server.Maintenance
{
    /// <summary>
    /// What an application asked for about a maintenance activity.
    /// </summary>
    /// <param name="Name">The name of the activity.</param>
    /// <param name="ConditionClass">The maintenance condition class.</param>
    /// <param name="Details">What the activity publishes initially.</param>
    /// <param name="OnStateChangedAsync">Runs after every transition.</param>
    internal sealed record MaintenanceRequest(
        string Name,
        AmbConditionClass ConditionClass,
        MaintenanceActivityDetails Details,
        Func<MaintenanceStateKind, CancellationToken, ValueTask>? OnStateChangedAsync);

    /// <summary>
    /// The maintenance activities of an asset (OPC 10000-110 §12).
    /// </summary>
    internal sealed class AssetMaintenance : IAssetMaintenance
    {
        /// <summary>
        /// The severity of a planned or executing activity: the lowest of
        /// the "maintenance needed" band of OPC 10000-110 Table 15.
        /// </summary>
        public const ushort ActiveSeverity = 301;

        private AssetMaintenance(AssetHandle handle, ILogger logger)
        {
            Handle = handle;
            Logger = logger;
        }

        /// <inheritdoc/>
        public ArrayOf<IMaintenanceActivity> Activities
        {
            get
            {
                lock (m_lock)
                {
                    var activities = new IMaintenanceActivity[m_activities.Count];
                    for (int ii = 0; ii < activities.Length; ii++)
                    {
                        activities[ii] = m_activities[ii];
                    }
                    return activities.ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets the number of activities.
        /// </summary>
        internal int ActivityCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_activities.Count;
                }
            }
        }

        /// <summary>
        /// Gets the asset.
        /// </summary>
        internal AssetHandle Handle { get; }

        /// <summary>
        /// Gets the logger.
        /// </summary>
        internal ILogger Logger { get; }

        /// <inheritdoc/>
        public bool TryGetActivity(string name, [NotNullWhen(true)] out IMaintenanceActivity? activity)
        {
            lock (m_lock)
            {
                foreach (MaintenanceActivity candidate in m_activities)
                {
                    if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                    {
                        activity = candidate;
                        return true;
                    }
                }
            }
            activity = null;
            return false;
        }

        /// <summary>
        /// Creates the maintenance conditions of an asset while it is
        /// registered.
        /// </summary>
        /// <returns>
        /// The maintenance, or <see langword="null"/> when the application
        /// asked for no activity.
        /// </returns>
        public static async ValueTask<AssetMaintenance?> CreateAsync(
            AmbNodeManager manager,
            INodeBuilder<BaseObjectState> assetNode,
            AssetHandle handle,
            AssetBuilder builder,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (builder.Maintenance.Count == 0)
            {
                return null;
            }

            AmbAlarmTypes? types = manager.UseServerDefinedAlarmTypes
                ? await manager.EnsureAlarmTypesAsync(cancellationToken).ConfigureAwait(false)
                : null;
            FolderState folder = AssetNodes.EnsureDeviceHealthAlarms(handle);
            var maintenance = new AssetMaintenance(handle, logger);
            foreach (MaintenanceRequest request in builder.Maintenance)
            {
                var activity = MaintenanceActivity.Create(maintenance, assetNode, folder, request, types, manager);
                lock (maintenance.m_lock)
                {
                    maintenance.m_activities.Add(activity);
                }
            }
            return maintenance;
        }

        private readonly Lock m_lock = new();
        private readonly List<MaintenanceActivity> m_activities = [];
    }

    /// <summary>
    /// A maintenance activity of an asset.
    /// </summary>
    internal sealed class MaintenanceActivity : IMaintenanceActivity
    {
        private MaintenanceActivity(
            AssetMaintenance maintenance,
            MaintenanceRequest request,
            MaintenanceRequiredAlarmState condition,
            MaintenanceEventStateMachineDriver driver,
            ushort ambNamespaceIndex)
        {
            m_maintenance = maintenance;
            Name = request.Name;
            ConditionClass = request.ConditionClass;
            m_condition = condition;
            m_driver = driver;
            m_ambNamespaceIndex = ambNamespaceIndex;
            m_details = request.Details.Clone();
            m_onStateChangedAsync = request.OnStateChangedAsync;
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public NodeId NodeId => m_condition.NodeId;

        /// <inheritdoc/>
        public AmbConditionClass ConditionClass { get; }

        /// <inheritdoc/>
        public MaintenanceStateKind State
        {
            get
            {
                lock (m_lock)
                {
                    return m_driver.State;
                }
            }
        }

        /// <inheritdoc/>
        public MaintenanceActivityDetails Details
        {
            get
            {
                lock (m_lock)
                {
                    return m_details.Clone();
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask StartAsync(LocalizedText message = default, CancellationToken cancellationToken = default)
        {
            return TransitionAsync(MaintenanceStateKind.Executing, null, message, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask FinishAsync(LocalizedText message = default, CancellationToken cancellationToken = default)
        {
            return TransitionAsync(MaintenanceStateKind.Finished, null, message, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ReplanAsync(
            DateTimeUtc? plannedDate = null,
            LocalizedText message = default,
            CancellationToken cancellationToken = default)
        {
            return TransitionAsync(MaintenanceStateKind.Planned, plannedDate, message, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask UpdateAsync(
            Action<MaintenanceActivityDetails> update,
            LocalizedText message = default,
            CancellationToken cancellationToken = default)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ISystemContext context = m_maintenance.Handle.Context;
            MaintenanceActivityDetails before;
            lock (m_lock)
            {
                before = m_details.Clone();
            }

            // The delegate of the application runs outside the lock; what it
            // changed is applied to the details as they are then, so an update
            // or a replan in between is not lost.
            MaintenanceActivityDetails edited = before.Clone();
            update(edited);
            lock (m_lock)
            {
                m_details = m_details.WithChanges(before, edited);
                Publish(context);
                m_condition.Message!.Value = MessageOf(message);
                AssetNodes.ReportEvent(context, m_condition);
            }
            return default;
        }

        /// <summary>
        /// Creates the condition of an activity on the asset.
        /// </summary>
        public static MaintenanceActivity Create(
            AssetMaintenance maintenance,
            INodeBuilder<BaseObjectState> assetNode,
            FolderState folder,
            MaintenanceRequest request,
            AmbAlarmTypes? types,
            AmbNodeManager manager)
        {
            AssetHandle handle = maintenance.Handle;
            ISystemContext context = handle.Context;
            BaseObjectState asset = assetNode.Node;
            MaintenanceRequiredAlarmState condition = assetNode
                .CreateAlarm(
                    new QualifiedName(request.Name, asset.NodeId.NamespaceIndex),
                    parent => new MaintenanceRequiredAlarmState(parent))
                .Alarm;

            // OPC 10000-110 §12.1 applies IMaintenanceEventType to the
            // condition type: the server-specific subtype implements it, or
            // - without one - the condition references it itself.
            if (types != null)
            {
                condition.TypeDefinitionId = types.MaintenanceActivityType;
            }
            else
            {
                condition.AddReference(
                    Ua.ReferenceTypeIds.HasInterface,
                    false,
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.IMaintenanceEventType, context.NamespaceUris));
            }
            if (condition.EventType != null)
            {
                condition.EventType.Value = condition.TypeDefinitionId;
            }

            var stateMachine = new MaintenanceEventStateMachineState(condition);
            var stateMachineName = new QualifiedName(BrowseNames.MaintenanceState, manager.AmbNamespaceIndex);
            stateMachine.Create(
                context,
                NodeId.Null,
                stateMachineName,
                new LocalizedText(BrowseNames.MaintenanceState),
                false);
            stateMachine.ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent;
            condition.AddChild(stateMachine);
            NodeId previous = context.AssignInstanceNodeId(stateMachine);
            context.AssignInstanceChildNodeIds(stateMachine, previous);
            AssetNodes.Register(handle, stateMachine);

            var driver = new MaintenanceEventStateMachineDriver(stateMachine, context, manager.AmbNamespaceIndex);
            driver.SetInitialState(MaintenanceStateKind.Planned);

            condition.ConditionClassId?.Value = request.ConditionClass.GetTypeId(context.NamespaceUris);
            condition.ConditionClassName?.Value = new LocalizedText(request.ConditionClass.Name);
            AssetNodes.Organize(folder, condition);

            var activity = new MaintenanceActivity(maintenance, request, condition, driver, manager.AmbNamespaceIndex);
            activity.Publish(context);

            // A planned activity is active and retained, so a client sees it
            // through a condition refresh (§12.1). An acknowledgement or a
            // comment of a client is reported as an event like any other
            // state change.
            condition.AutoReportStateChanges = true;
            condition.SetActiveState(context, true);
            condition.SetAcknowledgedState(context, false);
            condition.SetSeverity(context, (EventSeverity)AssetMaintenance.ActiveSeverity);
            condition.Retain!.Value = true;
            condition.Message!.Value = activity.MessageOf(default);
            condition.ClearChangeMasks(context, true);
            return activity;
        }

        private async ValueTask TransitionAsync(
            MaintenanceStateKind target,
            DateTimeUtc? plannedDate,
            LocalizedText message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ISystemContext context = m_maintenance.Handle.Context;
            MaintenanceEventStateMachineTables.TransitionDefinition transition;
            lock (m_lock)
            {
                transition = m_driver.Plan(target);
            }

            // The guard of the application runs outside the lock: it may read
            // the activity or wait for code that does.
            m_driver.Guard(transition);

            lock (m_lock)
            {
                if (m_driver.CurrentStateId != transition.FromStateId)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The maintenance activity '{0}' changed its state while the transition {1} was checked.",
                        Name,
                        transition.Name);
                }
                m_driver.Commit(transition);
                if (plannedDate != null)
                {
                    m_details.PlannedDate = plannedDate;
                    Publish(context);
                }

                bool active = target != MaintenanceStateKind.Finished;
                m_condition.Message!.Value = MessageOf(message);
                if (active)
                {
                    if (m_condition.ActiveState?.Id?.Value != true)
                    {
                        m_condition.SetActiveState(context, true);
                        m_condition.SetAcknowledgedState(context, false);
                    }
                    m_condition.SetSeverity(context, (EventSeverity)AssetMaintenance.ActiveSeverity);
                    m_condition.Retain!.Value = true;
                }
                else
                {
                    m_condition.SetActiveState(context, false);
                    m_condition.SetSeverity(context, (EventSeverity)AssetHealth.InactiveSeverity);
                    m_condition.Retain!.Value = m_condition.AckedState?.Id?.Value != true;
                }
                AssetNodes.ReportEvent(context, m_condition);
            }
            m_driver.NotifyCommitted(transition);
            m_maintenance.Logger.MaintenanceStateChanged(m_maintenance.Handle.BrowseName, Name, target);

            Func<MaintenanceStateKind, CancellationToken, ValueTask>? changed = m_onStateChangedAsync;
            if (changed != null)
            {
                await changed(target, cancellationToken).ConfigureAwait(false);
            }
        }

        private LocalizedText MessageOf(LocalizedText message)
        {
            if (!message.IsNullOrEmpty)
            {
                return message;
            }
            if (!m_details.Description.IsNullOrEmpty)
            {
                return m_details.Description;
            }
            return new LocalizedText(Name);
        }

        /// <summary>
        /// Writes the optional members that are set to their properties,
        /// creating a property the first time its member is set.
        /// </summary>
        private void Publish(ISystemContext context)
        {
            MaintenanceActivityDetails details = m_details;
            if (details.PlannedDate is DateTimeUtc plannedDate)
            {
                Property<DateTimeUtc, VariantBuilder>(
                    context,
                    BrowseNames.PlannedDate,
                    Ua.DataTypeIds.UtcTime,
                    ValueRanks.Scalar).Value = plannedDate;
            }
            if (details.EstimatedDowntime is TimeSpan downtime)
            {
                Property<double, VariantBuilder>(
                    context,
                    BrowseNames.EstimatedDowntime,
                    Ua.DataTypeIds.Duration,
                    ValueRanks.Scalar).Value = downtime.TotalMilliseconds;
            }
            if (details.MaintenanceSupplier is NameNodeIdDataType supplier)
            {
                Property<NameNodeIdDataType, StructureBuilder<NameNodeIdDataType>>(
                    context,
                    BrowseNames.MaintenanceSupplier,
                    NameNodeIdDataTypeId(context),
                    ValueRanks.Scalar).Value = supplier;
            }
            if (details.QualificationOfPersonnel is NameNodeIdDataType qualification)
            {
                Property<NameNodeIdDataType, StructureBuilder<NameNodeIdDataType>>(
                    context,
                    BrowseNames.QualificationOfPersonnel,
                    NameNodeIdDataTypeId(context),
                    ValueRanks.Scalar).Value = qualification;
            }
            if (!details.PartsOfAssetReplaced.IsNull)
            {
                Property<ArrayOf<NameNodeIdDataType>, StructureBuilder<NameNodeIdDataType>>(
                    context,
                    BrowseNames.PartsOfAssetReplaced,
                    NameNodeIdDataTypeId(context),
                    ValueRanks.OneDimension).Value = details.PartsOfAssetReplaced;
            }
            if (!details.PartsOfAssetServiced.IsNull)
            {
                Property<ArrayOf<NameNodeIdDataType>, StructureBuilder<NameNodeIdDataType>>(
                    context,
                    BrowseNames.PartsOfAssetServiced,
                    NameNodeIdDataTypeId(context),
                    ValueRanks.OneDimension).Value = details.PartsOfAssetServiced;
            }
            if (details.MaintenanceMethod is MaintenanceMethodEnum method)
            {
                Property<MaintenanceMethodEnum, EnumerationBuilder<MaintenanceMethodEnum>>(
                    context,
                    BrowseNames.MaintenanceMethod,
                    ExpandedNodeId.ToNodeId(DataTypeIds.MaintenanceMethodEnum, context.NamespaceUris),
                    ValueRanks.Scalar).Value = method;
            }
            if (details.ConfigurationChanged is bool configurationChanged)
            {
                Property<bool, VariantBuilder>(
                    context,
                    BrowseNames.ConfigurationChanged,
                    Ua.DataTypeIds.Boolean,
                    ValueRanks.Scalar).Value = configurationChanged;
            }
            m_condition.ClearChangeMasks(context, true);
        }

        private PropertyState<T> Property<T, TBuilder>(
            ISystemContext context,
            string name,
            NodeId dataType,
            int valueRank)
            where TBuilder : struct, IVariantBuilder<T>
        {
            var browseName = new QualifiedName(name, m_ambNamespaceIndex);
            if (m_condition.FindChildWithQualifiedName(context, browseName) is PropertyState<T> existing)
            {
                return existing;
            }

            PropertyState<T> property = PropertyState<T>.With<TBuilder>(m_condition);
            property.SymbolicName = name;
            property.BrowseName = browseName;
            property.DisplayName = new LocalizedText(name);
            property.TypeDefinitionId = Ua.VariableTypeIds.PropertyType;
            property.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
            property.DataType = dataType;
            property.ValueRank = valueRank;
            property.AccessLevel = AccessLevels.CurrentRead;
            property.UserAccessLevel = AccessLevels.CurrentRead;
            m_condition.AddChild(property);
            AssetNodes.Register(m_maintenance.Handle, property);
            return property;
        }

        private static NodeId NameNodeIdDataTypeId(ISystemContext context)
        {
            return ExpandedNodeId.ToNodeId(DataTypeIds.NameNodeIdDataType, context.NamespaceUris);
        }

        private readonly AssetMaintenance m_maintenance;
        private readonly MaintenanceRequiredAlarmState m_condition;
        private readonly MaintenanceEventStateMachineDriver m_driver;
        private readonly ushort m_ambNamespaceIndex;
        private readonly Func<MaintenanceStateKind, CancellationToken, ValueTask>? m_onStateChangedAsync;
        private readonly Lock m_lock = new();
        private MaintenanceActivityDetails m_details;
    }

    internal static partial class AssetMaintenanceLog
    {
        [LoggerMessage(
            EventId = AmbServerEventIds.AssetMaintenance + 0,
            Level = LogLevel.Information,
            Message = "Maintenance activity {Name} of asset {BrowseName} is {State}.")]
        public static partial void MaintenanceStateChanged(
            this ILogger logger,
            QualifiedName browseName,
            string name,
            MaintenanceStateKind state);
    }
}
