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

namespace Opc.Ua.AMB.Client
{
    /// <summary>
    /// The fields of an asset condition - a health alarm (OPC 10000-110 §9.3)
    /// or a maintenance activity (§12) - both as the select clauses of an
    /// event filter and as relative paths from a condition object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fields of the AMB interfaces are selected with
    /// <c>BaseEventType</c> as the type definition, which every event has. A
    /// server checks the type definition of a select clause against the type
    /// hierarchy of the event, and an interface is not part of it, so a
    /// clause naming <c>IRootCauseIndicationType</c> would select nothing;
    /// the Device Integration alarm types are only what OPC 10000-110 §9.3
    /// and §12.1 recommend, so they cannot be relied on either.
    /// </para>
    /// <para>
    /// The where clauses ask for <c>ConditionType</c>. A maintenance activity
    /// is told apart by its condition class - <c>MaintenanceConditionClassType</c>
    /// or a subtype (§12.1) - and the health and maintenance filters select
    /// disjoint sets, so an event reaches one of the two monitored items.
    /// </para>
    /// </remarks>
    internal static class AmbConditionFields
    {
        public const int ConditionId = 0;
        public const int EventId = 1;
        public const int EventType = 2;
        public const int SourceNode = 3;
        public const int SourceName = 4;
        public const int Time = 5;
        public const int Message = 6;
        public const int Severity = 7;
        public const int ConditionClassId = 8;
        public const int ConditionClassName = 9;
        public const int ConditionName = 10;
        public const int Retain = 11;
        public const int Acked = 12;
        public const int Active = 13;
        public const int PotentialRootCauses = 14;
        public const int MaintenanceState = 15;
        public const int PlannedDate = 16;
        public const int EstimatedDowntime = 17;
        public const int MaintenanceSupplier = 18;
        public const int QualificationOfPersonnel = 19;
        public const int PartsOfAssetReplaced = 20;
        public const int PartsOfAssetServiced = 21;
        public const int MaintenanceMethod = 22;
        public const int ConfigurationChanged = 23;
        public const int Comment = 24;
        public const int Count = 25;

        /// <summary>
        /// Builds the fields against a namespace table.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotSupported"/> when the table lacks the
        /// AMB or the Device Integration namespace.
        /// </exception>
        public static Field[] Build(NamespaceTable namespaceUris)
        {
            ushort amb = IndexOf(namespaceUris, Namespaces.AMB);
            IndexOf(namespaceUris, Opc.Ua.Di.Namespaces.OpcUaDi);
            NodeId baseEvent = Ua.ObjectTypeIds.BaseEventType;
            NodeId condition = Ua.ObjectTypeIds.ConditionType;

            // The interface fields hang below every event that has them.
            NodeId health = baseEvent;
            NodeId maintenance = baseEvent;

            var fields = new Field[Count];
            fields[ConditionId] = new Field(condition, []);
            fields[EventId] = Standard(baseEvent, Ua.BrowseNames.EventId);
            fields[EventType] = Standard(baseEvent, Ua.BrowseNames.EventType);
            fields[SourceNode] = Standard(baseEvent, Ua.BrowseNames.SourceNode);
            fields[SourceName] = Standard(baseEvent, Ua.BrowseNames.SourceName);
            fields[Time] = Standard(baseEvent, Ua.BrowseNames.Time);
            fields[Message] = Standard(baseEvent, Ua.BrowseNames.Message);
            fields[Severity] = Standard(baseEvent, Ua.BrowseNames.Severity);
            fields[ConditionClassId] = Standard(baseEvent, Ua.BrowseNames.ConditionClassId);
            fields[ConditionClassName] = Standard(baseEvent, Ua.BrowseNames.ConditionClassName);
            fields[ConditionName] = Standard(condition, Ua.BrowseNames.ConditionName);
            fields[Retain] = Standard(condition, Ua.BrowseNames.Retain);
            fields[Acked] = new Field(
                Ua.ObjectTypeIds.AcknowledgeableConditionType,
                [new QualifiedName(Ua.BrowseNames.AckedState), new QualifiedName(Ua.BrowseNames.Id)]);
            fields[Active] = new Field(
                Ua.ObjectTypeIds.AlarmConditionType,
                [new QualifiedName(Ua.BrowseNames.ActiveState), new QualifiedName(Ua.BrowseNames.Id)]);
            fields[PotentialRootCauses] = new Field(health, [new QualifiedName(BrowseNames.PotentialRootCauses, amb)]);
            fields[MaintenanceState] = new Field(
                maintenance,
                [
                    new QualifiedName(BrowseNames.MaintenanceState, amb),
                    new QualifiedName(Ua.BrowseNames.CurrentState),
                    new QualifiedName(Ua.BrowseNames.Id)
                ]);
            fields[PlannedDate] = new Field(maintenance, [new QualifiedName(BrowseNames.PlannedDate, amb)]);
            fields[EstimatedDowntime] = new Field(maintenance, [new QualifiedName(BrowseNames.EstimatedDowntime, amb)]);
            fields[MaintenanceSupplier] = new Field(
                maintenance,
                [new QualifiedName(BrowseNames.MaintenanceSupplier, amb)]);
            fields[QualificationOfPersonnel] = new Field(
                maintenance,
                [new QualifiedName(BrowseNames.QualificationOfPersonnel, amb)]);
            fields[PartsOfAssetReplaced] = new Field(
                maintenance,
                [new QualifiedName(BrowseNames.PartsOfAssetReplaced, amb)]);
            fields[PartsOfAssetServiced] = new Field(
                maintenance,
                [new QualifiedName(BrowseNames.PartsOfAssetServiced, amb)]);
            fields[MaintenanceMethod] = new Field(maintenance, [new QualifiedName(BrowseNames.MaintenanceMethod, amb)]);
            fields[ConfigurationChanged] = new Field(
                maintenance,
                [new QualifiedName(BrowseNames.ConfigurationChanged, amb)]);
            fields[Comment] = Standard(condition, Ua.BrowseNames.Comment);
            return fields;
        }

        /// <summary>
        /// Gets the maintenance condition classes the client knows without
        /// asking the server: <c>MaintenanceConditionClassType</c> and the AMB
        /// subtypes.
        /// </summary>
        public static HashSet<NodeId> KnownMaintenanceClasses(NamespaceTable namespaceUris)
        {
            var classes = new HashSet<NodeId> { Ua.ObjectTypeIds.MaintenanceConditionClassType };
            foreach (AmbConditionClass conditionClass in AmbConditionClass.All)
            {
                if (conditionClass.BaseClass == AmbConditionClassBase.Maintenance)
                {
                    NodeId typeId = conditionClass.GetTypeId(namespaceUris);
                    if (!typeId.IsNull)
                    {
                        classes.Add(typeId);
                    }
                }
            }
            return classes;
        }

        /// <summary>
        /// Builds the event filter of the health alarms or of the maintenance
        /// activities: conditions whose class is - or is not - one of the
        /// maintenance condition classes.
        /// </summary>
        /// <param name="namespaceUris">The namespace table of the session.</param>
        /// <param name="maintenanceClasses">The maintenance condition classes.</param>
        /// <param name="maintenance">Whether to select the maintenance activities.</param>
        public static EventFilter Filter(
            NamespaceTable namespaceUris,
            IReadOnlyCollection<NodeId> maintenanceClasses,
            bool maintenance)
        {
            EventFilter filter = Selecting(namespaceUris);
            ContentFilter where = filter.WhereClause;
            ContentFilterElement condition = where.Push(
                FilterOperator.OfType,
                Variant.From(Ua.ObjectTypeIds.ConditionType));
            var operands = new List<Variant>
            {
                Variant.FromStructure(Operand(Ua.BrowseNames.ConditionClassId))
            };
            foreach (NodeId conditionClass in maintenanceClasses)
            {
                operands.Add(Variant.From(conditionClass));
            }
            ContentFilterElement classes = where.Push(FilterOperator.InList, [.. operands]);
            ContentFilterElement selection = maintenance
                ? classes
                : where.Push(FilterOperator.Not, Variant.FromStructure(classes));
            where.Push(FilterOperator.And, Variant.FromStructure(condition), Variant.FromStructure(selection));
            return filter;
        }

        /// <summary>
        /// Builds the event filter of a condition refresh for one asset: its
        /// conditions and the events that bracket the refresh.
        /// </summary>
        public static EventFilter RefreshFilter(NamespaceTable namespaceUris, NodeId asset)
        {
            EventFilter filter = Selecting(namespaceUris);
            ContentFilter where = filter.WhereClause;
            ContentFilterElement start = where.Push(
                FilterOperator.OfType,
                Variant.From(Ua.ObjectTypeIds.RefreshStartEventType));
            ContentFilterElement end = where.Push(
                FilterOperator.OfType,
                Variant.From(Ua.ObjectTypeIds.RefreshEndEventType));
            ContentFilterElement condition = where.Push(
                FilterOperator.OfType,
                Variant.From(Ua.ObjectTypeIds.ConditionType));
            ContentFilterElement source = where.Push(
                FilterOperator.Equals,
                Variant.FromStructure(Operand(Ua.BrowseNames.SourceNode)),
                Variant.From(asset));
            ContentFilterElement ofAsset = where.Push(
                FilterOperator.And,
                Variant.FromStructure(condition),
                Variant.FromStructure(source));
            ContentFilterElement bracket = where.Push(
                FilterOperator.Or,
                Variant.FromStructure(start),
                Variant.FromStructure(end));
            where.Push(FilterOperator.Or, Variant.FromStructure(bracket), Variant.FromStructure(ofAsset));
            return filter;
        }

        /// <summary>
        /// Gets whether the values belong to a maintenance activity: its
        /// condition class is a maintenance condition class.
        /// </summary>
        public static bool IsMaintenance(IReadOnlyList<Variant> values, IReadOnlyCollection<NodeId> maintenanceClasses)
        {
            if (values.Count <= ConditionClassId || !values[ConditionClassId].TryGetValue(out NodeId conditionClass))
            {
                return false;
            }
            foreach (NodeId maintenanceClass in maintenanceClasses)
            {
                if (maintenanceClass == conditionClass)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets whether the values are those of the event that ends a
        /// condition refresh.
        /// </summary>
        public static bool IsRefreshEnd(IReadOnlyList<Variant> values)
        {
            return values.Count > EventType &&
                values[EventType].TryGetValue(out NodeId eventType) &&
                eventType == Ua.ObjectTypeIds.RefreshEndEventType;
        }

        /// <summary>
        /// Gets whether the values are those of a refresh bracket event.
        /// </summary>
        public static bool IsRefreshBracket(IReadOnlyList<Variant> values)
        {
            return values.Count > EventType &&
                values[EventType].TryGetValue(out NodeId eventType) &&
                (eventType == Ua.ObjectTypeIds.RefreshStartEventType ||
                    eventType == Ua.ObjectTypeIds.RefreshEndEventType);
        }

        private static EventFilter Selecting(NamespaceTable namespaceUris)
        {
            Field[] fields = Build(namespaceUris);
            var clauses = new SimpleAttributeOperand[fields.Length];
            for (int ii = 0; ii < fields.Length; ii++)
            {
                clauses[ii] = new SimpleAttributeOperand
                {
                    TypeDefinitionId = fields[ii].TypeDefinitionId,
                    BrowsePath = fields[ii].Path.ToArrayOf(),
                    AttributeId = ii == ConditionId ? Attributes.NodeId : Attributes.Value
                };
            }
            return new EventFilter { SelectClauses = clauses.ToArrayOf() };
        }

        private static SimpleAttributeOperand Operand(string browseName)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName(browseName)],
                AttributeId = Attributes.Value
            };
        }

        /// <summary>
        /// Maps the values of the fields to a health alarm.
        /// </summary>
        public static AssetAlarmRecord ToAlarm(IReadOnlyList<Variant> values, IServiceMessageContext context)
        {
            NodeId conditionClassId = NodeIdOf(values, ConditionClassId);
            ushort severity = values[Severity].TryGetValue(out ushort value) ? value : (ushort)0;
            return new AssetAlarmRecord
            {
                ConditionId = NodeIdOf(values, ConditionId),
                EventId = values[EventId].TryGetValue(out ByteString eventId) ? eventId : ByteString.Empty,
                EventType = NodeIdOf(values, EventType),
                SourceNode = NodeIdOf(values, SourceNode),
                SourceName = AmbClient.StringOf(values[SourceName]),
                ConditionName = AmbClient.StringOf(values[ConditionName]),
                Time = values[Time].TryGetValue(out DateTimeUtc time) ? time : default,
                Message = AmbClient.TextOf(values[Message]),
                Severity = severity,
                ConditionClassId = conditionClassId,
                ConditionClassName = AmbClient.TextOf(values[ConditionClassName]),
                ConditionClass = AmbConditionClass.Find(conditionClassId, context.NamespaceUris),
                IsActive = BooleanOf(values, Active),
                IsAcknowledged = BooleanOf(values, Acked),
                Retain = BooleanOf(values, Retain),
                Comment = AmbClient.TextOf(values[Comment]),
                PotentialRootCauses =
                    values[PotentialRootCauses].TryGetValue(out ArrayOf<RootCauseDataType> causes, context)
                        ? causes
                        : default
            };
        }

        /// <summary>
        /// Maps the values of the fields to a maintenance activity.
        /// </summary>
        public static MaintenanceActivityRecord ToMaintenance(
            IReadOnlyList<Variant> values,
            IServiceMessageContext context)
        {
            NodeId conditionClassId = NodeIdOf(values, ConditionClassId);
            return new MaintenanceActivityRecord
            {
                ConditionId = NodeIdOf(values, ConditionId),
                EventId = values[EventId].TryGetValue(out ByteString eventId) ? eventId : ByteString.Empty,
                EventType = NodeIdOf(values, EventType),
                SourceNode = NodeIdOf(values, SourceNode),
                ConditionName = AmbClient.StringOf(values[ConditionName]),
                Time = values[Time].TryGetValue(out DateTimeUtc time) ? time : default,
                Message = AmbClient.TextOf(values[Message]),
                ConditionClassId = conditionClassId,
                ConditionClass = AmbConditionClass.Find(conditionClassId, context.NamespaceUris),
                IsActive = BooleanOf(values, Active),
                IsAcknowledged = BooleanOf(values, Acked),
                Retain = BooleanOf(values, Retain),
                Comment = AmbClient.TextOf(values[Comment]),
                State = StateOf(NodeIdOf(values, MaintenanceState), context.NamespaceUris),
                PlannedDate = values[PlannedDate].TryGetValue(out DateTimeUtc planned) ? planned : default,
                EstimatedDowntime = values[EstimatedDowntime].TryGetValue(out double downtime)
                    ? TimeSpan.FromMilliseconds(downtime)
                    : null,
                MaintenanceSupplier = StructureOf<NameNodeIdDataType>(values[MaintenanceSupplier], context),
                QualificationOfPersonnel = StructureOf<NameNodeIdDataType>(values[QualificationOfPersonnel], context),
                PartsOfAssetReplaced =
                    values[PartsOfAssetReplaced].TryGetValue(out ArrayOf<NameNodeIdDataType> replaced, context)
                        ? replaced
                        : default,
                PartsOfAssetServiced =
                    values[PartsOfAssetServiced].TryGetValue(out ArrayOf<NameNodeIdDataType> serviced, context)
                        ? serviced
                        : default,
                MaintenanceMethod = values[MaintenanceMethod].TryGetValue(out int method)
                    ? (MaintenanceMethodEnum)method
                    : null,
                ConfigurationChanged = values[ConfigurationChanged].TryGetValue(out bool changed) ? changed : null
            };
        }

        /// <summary>
        /// Maps the <c>Id</c> of the current state to the state.
        /// </summary>
        public static MaintenanceStateKind? StateOf(NodeId stateId, NamespaceTable namespaceUris)
        {
            if (stateId.IsNull ||
                namespaceUris.GetString(stateId.NamespaceIndex) != Namespaces.AMB ||
                !stateId.TryGetValue(out uint id))
            {
                return null;
            }
            return id switch
            {
                MaintenanceEventStateMachineTypeIds.StateIds.Planned => MaintenanceStateKind.Planned,
                MaintenanceEventStateMachineTypeIds.StateIds.Executing => MaintenanceStateKind.Executing,
                MaintenanceEventStateMachineTypeIds.StateIds.Finished => MaintenanceStateKind.Finished,
                _ => null
            };
        }

        private static T? StructureOf<T>(Variant value, IServiceMessageContext context)
            where T : class, IEncodeable, new()
        {
            T structure;
            return value.TryGetStructure(context, out structure!) ? structure : null;
        }

        private static NodeId NodeIdOf(IReadOnlyList<Variant> values, int index)
        {
            return values[index].TryGetValue(out NodeId nodeId) ? nodeId : NodeId.Null;
        }

        private static bool BooleanOf(IReadOnlyList<Variant> values, int index)
        {
            return values[index].TryGetValue(out bool value) && value;
        }

        private static Field Standard(NodeId typeDefinitionId, string name)
        {
            return new Field(typeDefinitionId, [new QualifiedName(name)]);
        }

        private static ushort IndexOf(NamespaceTable namespaceUris, string uri)
        {
            int index = namespaceUris.GetIndex(uri);
            if (index < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The server does not publish the namespace '{0}'.",
                    uri);
            }
            return (ushort)index;
        }

        /// <summary>
        /// A field of a condition.
        /// </summary>
        /// <param name="TypeDefinitionId">The type that declares it.</param>
        /// <param name="Path">The browse path from the condition; empty for the condition itself.</param>
        internal readonly record struct Field(NodeId TypeDefinitionId, QualifiedName[] Path);
    }

    /// <summary>
    /// Event filters the client subscribes with.
    /// </summary>
    internal static class AmbEventFilters
    {
        /// <summary>
        /// The <c>GeneralModelChangeEvent</c>s: the event type and the changes.
        /// </summary>
        public static EventFilter ModelChanges()
        {
            var filter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(Ua.BrowseNames.EventType)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = Ua.ObjectTypeIds.GeneralModelChangeEventType,
                        BrowsePath = [new QualifiedName(Ua.BrowseNames.Changes)],
                        AttributeId = Attributes.Value
                    }
                ]
            };
            filter.WhereClause.Push(
                FilterOperator.OfType,
                Variant.From(Ua.ObjectTypeIds.GeneralModelChangeEventType));
            return filter;
        }
    }
}
