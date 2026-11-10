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
