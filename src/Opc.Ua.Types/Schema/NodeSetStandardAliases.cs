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
 *
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
using System.Globalization;
using Opc.Ua.Types;

namespace Opc.Ua.Export
{
    /// <summary>
    /// The standard base-namespace names a NodeSet2 document conventionally
    /// writes where a NodeId is expected, and the identifiers they stand for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NodeSet2 document may write a name such as <c>HasComponent</c> or
    /// <c>Double</c> in place of an identifier, but only where its own
    /// <c>&lt;Aliases&gt;</c> table declares that name: the importer resolves
    /// through that table and reports <c>BadNodeIdInvalid</c> for a name it
    /// does not find. This table says what the base-namespace names stand for,
    /// so a producer can declare the ones a document it writes uses. It states
    /// nothing about what any particular document means, and no reader may
    /// treat a name as an alias the document did not declare.
    /// </para>
    /// <para>
    /// The ReferenceType entries also carry the InverseName OPC 10000-5 gives
    /// each type, because a ReferenceType has two names and a table of forward
    /// names alone cannot tell <c>HasComponent</c> from <c>ComponentOf</c>.
    /// Every lookup below is built from the one array so a name and the
    /// identifier it stands for cannot drift apart.
    /// </para>
    /// </remarks>
    internal static class NodeSetStandardAliases
    {
        /// <summary>
        /// Resolves a standard base-namespace ReferenceType or DataType name
        /// to the identifier it stands for.
        /// </summary>
        /// <param name="name">The unqualified BrowseName.</param>
        /// <param name="nodeId">The base-namespace NodeId, when known.</param>
        /// <returns><c>true</c> when the name is one this table knows.</returns>
        public static bool TryResolve(string? name, out string nodeId)
        {
            if (name is not null)
            {
                if (s_referenceTypeNameToNodeId.TryGetValue(name, out nodeId!))
                {
                    return true;
                }
                if (s_dataTypeNameToNodeId.TryGetValue(name, out nodeId!))
                {
                    return true;
                }
            }
            nodeId = string.Empty;
            return false;
        }

        /// <summary>
        /// Resolves a base-namespace ReferenceType named by its BrowseName.
        /// </summary>
        public static bool TryGetReferenceTypeNodeId(string? browseName, out string nodeId)
        {
            if (browseName is not null &&
                s_referenceTypeNameToNodeId.TryGetValue(browseName, out nodeId!))
            {
                return true;
            }
            nodeId = string.Empty;
            return false;
        }

        /// <summary>
        /// Resolves a base-namespace ReferenceType named by either its
        /// BrowseName or its InverseName, reporting which of the two matched.
        /// </summary>
        /// <remarks>
        /// The BrowseName is tried first, so a name that is both a BrowseName
        /// and some other type's InverseName reads forward. No standard name
        /// is currently ambiguous in that way; the order fixes the outcome if
        /// one ever is.
        /// </remarks>
        /// <param name="name">The unqualified BrowseName or InverseName.</param>
        /// <param name="nodeId">The ReferenceType's base-namespace NodeId.</param>
        /// <param name="isForward">
        /// <c>true</c> when the BrowseName matched, <c>false</c> when the
        /// InverseName did.
        /// </param>
        /// <returns><c>true</c> when the name resolved.</returns>
        public static bool TryResolveReferenceTypeName(
            string? name,
            out string nodeId,
            out bool isForward)
        {
            if (name is not null)
            {
                if (s_referenceTypeNameToNodeId.TryGetValue(name, out nodeId!))
                {
                    isForward = true;
                    return true;
                }
                if (s_referenceTypeInverseNameToNodeId.TryGetValue(name, out nodeId!))
                {
                    isForward = false;
                    return true;
                }
            }
            nodeId = string.Empty;
            isForward = true;
            return false;
        }

        /// <summary>
        /// Gets the InverseName OPC 10000-5 gives a base-namespace
        /// ReferenceType, which is the name that states the same reference
        /// backwards.
        /// </summary>
        /// <param name="nodeId">The ReferenceType's base-namespace NodeId.</param>
        /// <param name="inverseName">
        /// The InverseName, or an empty string when the ReferenceType is
        /// abstract enough to have none.
        /// </param>
        /// <returns><c>true</c> when an InverseName is known.</returns>
        public static bool TryGetReferenceTypeInverseName(string? nodeId, out string inverseName)
        {
            if (nodeId is not null &&
                s_referenceTypeNodeIdToInverseName.TryGetValue(nodeId, out inverseName!))
            {
                return true;
            }
            inverseName = string.Empty;
            return false;
        }

        /// <summary>
        /// Gets the BrowseName of a base-namespace ReferenceType named by
        /// either its identifier or that BrowseName.
        /// </summary>
        public static bool TryGetReferenceTypeBrowseName(
            string? referenceType,
            out string browseName)
        {
            if (referenceType is not null)
            {
                if (s_referenceTypeNodeIdToName.TryGetValue(referenceType, out browseName!))
                {
                    return true;
                }
                if (s_referenceTypeNameToNodeId.ContainsKey(referenceType))
                {
                    browseName = referenceType;
                    return true;
                }
            }
            browseName = string.Empty;
            return false;
        }

        /// <summary>
        /// One standard ReferenceType: its base-namespace NodeId, its
        /// BrowseName and its InverseName.
        /// </summary>
        /// <remarks>
        /// OPC 10000-3 gives a ReferenceType two names: the BrowseName reads
        /// the reference in the forward direction and the InverseName reads
        /// the same reference backwards. A symmetric ReferenceType has no
        /// InverseName; its entry leaves the inverse name empty.
        /// </remarks>
        private readonly record struct StandardReferenceType(
            string NodeId,
            string BrowseName,
            string InverseName);

        /// <summary>
        /// The standard ReferenceTypes this library names, with the InverseName
        /// OPC 10000-5 gives each.
        /// </summary>
        private static readonly StandardReferenceType[] s_standardReferenceTypes =
        [
            new("i=31", "References", string.Empty),
            new("i=32", "NonHierarchicalReferences", string.Empty),
            new("i=33", "HierarchicalReferences", "InverseHierarchicalReferences"),
            new("i=34", "HasChild", "ChildOf"),
            new("i=35", "Organizes", "OrganizedBy"),
            new("i=36", "HasEventSource", "EventSourceOf"),
            new("i=37", "HasModellingRule", "ModellingRuleOf"),
            new("i=38", "HasEncoding", "EncodingOf"),
            new("i=39", "HasDescription", "DescriptionOf"),
            new("i=40", "HasTypeDefinition", "TypeDefinitionOf"),
            new("i=41", "GeneratesEvent", "GeneratedBy"),
            new("i=3065", "AlwaysGeneratesEvent", "AlwaysGeneratedBy"),
            new("i=44", "Aggregates", "AggregatedBy"),
            new("i=45", "HasSubtype", "SubtypeOf"),
            new("i=46", "HasProperty", "PropertyOf"),
            new("i=47", "HasComponent", "ComponentOf"),
            new("i=48", "HasNotifier", "NotifierOf"),
            new("i=49", "HasOrderedComponent", "OrderedComponentOf"),
            new("i=51", "FromState", "ToTransition"),
            new("i=52", "ToState", "FromTransition"),
            new("i=53", "HasCause", "MayBeCausedBy"),
            new("i=54", "HasEffect", "MayBeEffectedBy"),
            new("i=56", "HasHistoricalConfiguration", "HistoricalConfigurationOf"),
            new("i=117", "HasSubStateMachine", "SubStateMachineOf"),
            new("i=129", "HasArgumentDescription", "ArgumentDescriptionOf"),
            new(
                "i=131",
                "HasOptionalInputArgumentDescription",
                "OptionalInputArgumentDescriptionOf"),
            new("i=9004", "HasTrueSubState", "IsTrueSubStateOf"),
            new("i=9005", "HasFalseSubState", "IsFalseSubStateOf"),
            new("i=9006", "HasCondition", "IsConditionOf"),
            new("i=15112", "HasGuard", "GuardOf"),
            new("i=16361", "HasAlarmSuppressionGroup", "IsAlarmSuppressionGroupOf"),
            new("i=16362", "AlarmGroupMember", "MemberOfAlarmGroup"),
            new("i=17597", "HasDictionaryEntry", "DictionaryEntryOf"),
            new("i=17603", "HasInterface", "InterfaceOf"),
            new("i=17604", "HasAddIn", "AddInOf"),
            new("i=32059", "AlarmSuppressionGroupMember", "MemberOfAlarmSuppressionGroup")
        ];

        /// <summary>
        /// The DataType names a NodeSet2 document conventionally aliases.
        /// </summary>
        /// <remarks>
        /// Written as name/identifier pairs taken from the generated
        /// identifier table, so a name and the identifier it stands for cannot
        /// drift apart. Only DataTypes of the base namespace appear: a name in
        /// any other namespace is the source document's to declare.
        /// </remarks>
        private static readonly (string Name, uint Identifier)[] s_standardDataTypes =
        [
            (nameof(DataTypes.BaseDataType), DataTypes.BaseDataType),
            (nameof(DataTypes.Boolean), DataTypes.Boolean),
            (nameof(DataTypes.SByte), DataTypes.SByte),
            (nameof(DataTypes.Byte), DataTypes.Byte),
            (nameof(DataTypes.Int16), DataTypes.Int16),
            (nameof(DataTypes.UInt16), DataTypes.UInt16),
            (nameof(DataTypes.Int32), DataTypes.Int32),
            (nameof(DataTypes.UInt32), DataTypes.UInt32),
            (nameof(DataTypes.Int64), DataTypes.Int64),
            (nameof(DataTypes.UInt64), DataTypes.UInt64),
            (nameof(DataTypes.Float), DataTypes.Float),
            (nameof(DataTypes.Double), DataTypes.Double),
            (nameof(DataTypes.String), DataTypes.String),
            (nameof(DataTypes.DateTime), DataTypes.DateTime),
            (nameof(DataTypes.Guid), DataTypes.Guid),
            (nameof(DataTypes.ByteString), DataTypes.ByteString),
            (nameof(DataTypes.XmlElement), DataTypes.XmlElement),
            (nameof(DataTypes.NodeId), DataTypes.NodeId),
            (nameof(DataTypes.ExpandedNodeId), DataTypes.ExpandedNodeId),
            (nameof(DataTypes.StatusCode), DataTypes.StatusCode),
            (nameof(DataTypes.QualifiedName), DataTypes.QualifiedName),
            (nameof(DataTypes.LocalizedText), DataTypes.LocalizedText),
            (nameof(DataTypes.Structure), DataTypes.Structure),
            (nameof(DataTypes.DataValue), DataTypes.DataValue),
            (nameof(DataTypes.DiagnosticInfo), DataTypes.DiagnosticInfo),
            (nameof(DataTypes.Number), DataTypes.Number),
            (nameof(DataTypes.Integer), DataTypes.Integer),
            (nameof(DataTypes.UInteger), DataTypes.UInteger),
            (nameof(DataTypes.Enumeration), DataTypes.Enumeration),
            (nameof(DataTypes.Image), DataTypes.Image),
            (nameof(DataTypes.Decimal), DataTypes.Decimal),
            (nameof(DataTypes.Union), DataTypes.Union),
            (nameof(DataTypes.UriString), DataTypes.UriString),
            (nameof(DataTypes.Argument), DataTypes.Argument),
            (nameof(DataTypes.EnumValueType), DataTypes.EnumValueType),
            (nameof(DataTypes.OptionSet), DataTypes.OptionSet),
            (nameof(DataTypes.Duration), DataTypes.Duration),
            (nameof(DataTypes.UtcTime), DataTypes.UtcTime),
            (nameof(DataTypes.LocaleId), DataTypes.LocaleId),
            (nameof(DataTypes.IntegerId), DataTypes.IntegerId),
            (nameof(DataTypes.NumericRange), DataTypes.NumericRange),
            (nameof(DataTypes.Counter), DataTypes.Counter),
            (nameof(DataTypes.VersionTime), DataTypes.VersionTime),
            (nameof(DataTypes.NormalizedString), DataTypes.NormalizedString),
            (nameof(DataTypes.DecimalString), DataTypes.DecimalString),
            (nameof(DataTypes.DurationString), DataTypes.DurationString),
            (nameof(DataTypes.TimeString), DataTypes.TimeString),
            (nameof(DataTypes.DateString), DataTypes.DateString),
            (nameof(DataTypes.TimeZoneDataType), DataTypes.TimeZoneDataType),
            (nameof(DataTypes.RolePermissionType), DataTypes.RolePermissionType),
            (nameof(DataTypes.PermissionType), DataTypes.PermissionType),
            (nameof(DataTypes.AccessRestrictionType), DataTypes.AccessRestrictionType),
            (nameof(DataTypes.StructureDefinition), DataTypes.StructureDefinition),
            (nameof(DataTypes.EnumDefinition), DataTypes.EnumDefinition),
            (nameof(DataTypes.StructureField), DataTypes.StructureField),
            (nameof(DataTypes.EnumField), DataTypes.EnumField)
        ];

        private static readonly Dictionary<string, string> s_referenceTypeNameToNodeId =
            BuildForwardNameTable();

        private static readonly Dictionary<string, string> s_referenceTypeInverseNameToNodeId =
            BuildInverseNameTable();

        private static readonly Dictionary<string, string> s_referenceTypeNodeIdToName =
            BuildNodeIdTable();

        private static readonly Dictionary<string, string> s_referenceTypeNodeIdToInverseName =
            BuildNodeIdInverseNameTable();

        private static readonly Dictionary<string, string> s_dataTypeNameToNodeId =
            BuildDataTypeTable();

        private static Dictionary<string, string> BuildForwardNameTable()
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (StandardReferenceType entry in s_standardReferenceTypes)
            {
                table[entry.BrowseName] = entry.NodeId;
            }
            return table;
        }

        private static Dictionary<string, string> BuildInverseNameTable()
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (StandardReferenceType entry in s_standardReferenceTypes)
            {
                if (entry.InverseName.Length != 0)
                {
                    table[entry.InverseName] = entry.NodeId;
                }
            }
            return table;
        }

        private static Dictionary<string, string> BuildNodeIdTable()
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (StandardReferenceType entry in s_standardReferenceTypes)
            {
                table[entry.NodeId] = entry.BrowseName;
            }
            return table;
        }

        private static Dictionary<string, string> BuildNodeIdInverseNameTable()
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (StandardReferenceType entry in s_standardReferenceTypes)
            {
                if (entry.InverseName.Length != 0)
                {
                    table[entry.NodeId] = entry.InverseName;
                }
            }
            return table;
        }

        private static Dictionary<string, string> BuildDataTypeTable()
        {
            var table = new Dictionary<string, string>(
                s_standardDataTypes.Length,
                StringComparer.Ordinal);
            foreach ((string name, uint identifier) in s_standardDataTypes)
            {
                table[name] = "i=" + identifier.ToString(CultureInfo.InvariantCulture);
            }
            return table;
        }
    }
}
