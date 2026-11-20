/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.AMB
{
    /// <summary>
    /// Shared select clauses for live and archived asset conditions.
    /// </summary>
    internal static class AmbEventFields
    {
        /// <summary>
        /// Builds the field contract against the host's namespace table.
        /// Interface fields use BaseEventType because an interface is not an event supertype.
        /// </summary>
        public static ArrayOf<SimpleAttributeOperand> Select(NamespaceTable namespaceUris)
        {
            int amb = namespaceUris.GetIndex(Namespaces.AMB);
            if (amb < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The AMB namespace is not loaded.");
            }
            return
            [
                new SimpleAttributeOperand
                {
                    TypeDefinitionId = Ua.ObjectTypeIds.ConditionType,
                    AttributeId = Attributes.NodeId
                },
                Standard(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.EventId),
                Standard(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.EventType),
                Standard(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.SourceNode),
                Standard(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.SourceName),
                Standard(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.Time),
                Standard(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.Message),
                Standard(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.Severity),
                Standard(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.ConditionClassId),
                Standard(Ua.ObjectTypeIds.BaseEventType, Ua.BrowseNames.ConditionClassName),
                Standard(Ua.ObjectTypeIds.ConditionType, Ua.BrowseNames.ConditionName),
                Standard(Ua.ObjectTypeIds.ConditionType, Ua.BrowseNames.Retain),
                Nested(Ua.ObjectTypeIds.AcknowledgeableConditionType, Ua.BrowseNames.AckedState, Ua.BrowseNames.Id),
                Nested(Ua.ObjectTypeIds.AlarmConditionType, Ua.BrowseNames.ActiveState, Ua.BrowseNames.Id),
                Amb(BrowseNames.PotentialRootCauses, (ushort)amb),
                new SimpleAttributeOperand
                {
                    TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                    AttributeId = Attributes.Value,
                    BrowsePath =
                    [
                        new QualifiedName(BrowseNames.MaintenanceState, (ushort)amb),
                        new QualifiedName(Ua.BrowseNames.CurrentState),
                        new QualifiedName(Ua.BrowseNames.Id)
                    ]
                },
                Amb(BrowseNames.PlannedDate, (ushort)amb),
                Amb(BrowseNames.EstimatedDowntime, (ushort)amb),
                Amb(BrowseNames.MaintenanceSupplier, (ushort)amb),
                Amb(BrowseNames.QualificationOfPersonnel, (ushort)amb),
                Amb(BrowseNames.PartsOfAssetReplaced, (ushort)amb),
                Amb(BrowseNames.PartsOfAssetServiced, (ushort)amb),
                Amb(BrowseNames.MaintenanceMethod, (ushort)amb),
                Amb(BrowseNames.ConfigurationChanged, (ushort)amb),
                Standard(Ua.ObjectTypeIds.ConditionType, Ua.BrowseNames.Comment)
            ];
        }

        /// <summary>
        /// Selects a standard event field.
        /// </summary>
        private static SimpleAttributeOperand Standard(NodeId type, string name)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = type,
                AttributeId = Attributes.Value,
                BrowsePath = [new QualifiedName(name)]
            };
        }

        /// <summary>
        /// Selects a standard nested event field.
        /// </summary>
        private static SimpleAttributeOperand Nested(NodeId type, string name, string child)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = type,
                AttributeId = Attributes.Value,
                BrowsePath = [new QualifiedName(name), new QualifiedName(child)]
            };
        }

        /// <summary>
        /// Selects a field contributed by an AMB interface.
        /// </summary>
        private static SimpleAttributeOperand Amb(string name, ushort ns)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                AttributeId = Attributes.Value,
                BrowsePath = [new QualifiedName(name, ns)]
            };
        }
    }
}
