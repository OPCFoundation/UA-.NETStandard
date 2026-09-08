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
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Well-known identifiers, reference types, modelling rules and DataType
    /// mappings used by the WoT/NodeSet conversion. Kept in one place so that
    /// the numeric OPC UA base-namespace NodeIds are not scattered.
    /// </summary>
    internal static class WotVocabulary
    {
        public const string VocabularyNamespace = "http://opcfoundation.org/UA/WoT-Binding/";
        public const string OpcUaNamespace = "http://opcfoundation.org/UA/";
        public const string NodeSetXmlNamespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
        public const string NodeSetContentType = "application/opcua-nodeset+xml";
        public const string Base64Encoding = "base64";
        public const string EnvelopeType = "uav:nodeSet";
        public const string ProfileVersion = "1.0";
        public const string ThingModelType = "tm:ThingModel";
        public const string WotContext = "https://www.w3.org/2022/wot/td/v1.1";

        /// <summary>
        /// The published, version-pinned JSON-LD context of the WoT Binding.
        /// </summary>
        /// <remarks>
        /// The Binding mints several of its terms as <em>short</em> members
        /// under a type-scoped context - <c>displayName</c>, <c>unitId</c> and
        /// <c>namespaceUri</c> inside <c>uav:engineeringUnits</c>, for example.
        /// A short member is a term only while the context that defines it is
        /// in scope, so a generated document that names the prefix but not the
        /// context expands those members to nothing and silently loses them.
        /// The version-pinned form is used rather than the unversioned alias
        /// because a document states which revision it was written against, and
        /// a context that moves under a document is a document whose meaning
        /// changed without it being edited.
        /// </remarks>
        public const string BindingContext =
            "http://opcfoundation.org/UA/WoT-Binding/v1.1/opc-ua-wot-binding.context.jsonld";

        // Reference types (base namespace).
        public const string HasSubtype = "i=45";
        public const string HasProperty = "i=46";
        public const string HasComponent = "i=47";
        public const string HasOrderedComponent = "i=49";
        public const string HasEncoding = "i=38";
        public const string Organizes = "i=35";
        public const string HasTypeDefinition = "i=40";
        public const string HasModellingRule = "i=37";
        public const string GeneratesEvent = "i=41";

        /// <summary>
        /// The abstract root of every non-hierarchical ReferenceType
        /// (OPC 10000-5). Spec PR #19 replaced <c>uav:reference</c> with this
        /// name used directly as a link <c>rel</c>.
        /// </summary>
        public const string NonHierarchicalReferences = "i=32";

        /// <summary>
        /// The ReferenceType that names an Interface an Object exposes
        /// (OPC 10000-3). Spec PR #19 replaced <c>uav:capability</c> with this
        /// name used directly as a link <c>rel</c>.
        /// </summary>
        public const string HasInterface = "i=17603";

        // Type-annotation term for an event affordance projecting a UA EventType.
        public const string EventTypeAnnotation = "uav:eventType";

        // Type-annotation term marking a document as a projection document,
        // which declares rather than defines its affordances (Section 12.1).
        public const string ProjectionAnnotation = "uav:projection";

        // Link relation naming an organized projection group (Section 12.7).
        public const string OrganizesRel = "ua:Organizes";

        // Term naming the group an organizing link reaches (Section 12.7).
        public const string RefNameAnnotation = "uav:refName";

        /// <summary>
        /// The ConditionTypes of OPC 10000-9 that WoT Binding Section 13 maps.
        /// </summary>
        /// <remarks>
        /// Section 13.1 scopes the mapping to exactly these four, so this is
        /// the whole set rather than a convenience subset. Shelving,
        /// suppression, dialog conditions and <c>ConditionRefresh</c> are
        /// outside the mapping. A ConditionType outside this set is named by
        /// <c>uav:conditionTypeId</c>, which is definitive and needs no lookup.
        /// </remarks>
        public const string ConditionType = "i=2782";

        /// <inheritdoc cref="ConditionType"/>
        public const string AcknowledgeableConditionType = "i=2881";

        /// <inheritdoc cref="ConditionType"/>
        public const string AlarmConditionType = "i=2915";

        /// <inheritdoc cref="ConditionType"/>
        public const string LimitAlarmType = "i=2955";

        /// <summary>
        /// The Condition Methods OPC 10000-9 declares, by the type that
        /// declares each. A Method materialized for a
        /// <c>uav:conditionAction</c> carries the declaration as its
        /// <c>MethodDeclarationId</c>, which is what tells a Server the
        /// instance Method is that standard Method rather than a same-named
        /// Method of its own.
        /// </summary>
        public const string ConditionTypeEnableMethod = "i=9027";

        /// <inheritdoc cref="ConditionTypeEnableMethod"/>
        public const string ConditionTypeDisableMethod = "i=9028";

        /// <inheritdoc cref="ConditionTypeEnableMethod"/>
        public const string ConditionTypeAddCommentMethod = "i=9029";

        /// <inheritdoc cref="ConditionTypeEnableMethod"/>
        public const string AcknowledgeableConditionTypeAcknowledgeMethod = "i=9111";

        /// <inheritdoc cref="ConditionTypeEnableMethod"/>
        public const string AcknowledgeableConditionTypeConfirmMethod = "i=9113";

        private static readonly Dictionary<string, string> s_conditionTypeNameToNodeId =
            new(StringComparer.Ordinal)
            {
                ["ConditionType"] = ConditionType,
                ["AcknowledgeableConditionType"] = AcknowledgeableConditionType,
                ["AlarmConditionType"] = AlarmConditionType,
                ["LimitAlarmType"] = LimitAlarmType
            };

        /// <summary>
        /// Resolves the NodeId of a ConditionType named by its BrowseName in
        /// the base OPC UA namespace.
        /// </summary>
        /// <param name="browseName">The unqualified BrowseName.</param>
        /// <param name="nodeId">The NodeId, when the name is one this maps.</param>
        /// <returns><c>true</c> when the name resolved.</returns>
        public static bool TryGetConditionTypeNodeId(
            string? browseName,
            out string nodeId)
        {
            if (browseName is not null &&
                s_conditionTypeNameToNodeId.TryGetValue(browseName, out string? found))
            {
                nodeId = found;
                return true;
            }
            nodeId = string.Empty;
            return false;
        }

        /// <summary>
        /// Resolves the BrowseName of a ConditionType named by its
        /// base-namespace NodeId, which is what lets the forward direction
        /// state <c>uav:conditionType</c> as the compact model name Section
        /// 13.2 requires.
        /// </summary>
        /// <param name="nodeId">The base-namespace NodeId.</param>
        /// <param name="browseName">The BrowseName, when the id is one this maps.</param>
        /// <returns><c>true</c> when the identifier resolved.</returns>
        public static bool TryGetConditionTypeName(
            string? nodeId,
            out string browseName)
        {
            foreach (KeyValuePair<string, string> entry in s_conditionTypeNameToNodeId)
            {
                if (string.Equals(entry.Value, nodeId, StringComparison.Ordinal))
                {
                    browseName = entry.Key;
                    return true;
                }
            }
            browseName = string.Empty;
            return false;
        }

        // HasComponent subtypes (base namespace) that carry stronger semantics
        // than plain HasComponent and must be pinned by a link whose rel is
        // the ReferenceType model name (WoT Binding Section 5.3). Keyed by both the reference-type
        // BrowseName and its base-namespace NodeId; the value is the canonical
        // base-namespace ExpandedNodeId used for the typed link's uav:refId.
        // HasComponent and HasProperty are intentionally excluded: they are the
        // baseline parent-child forms surfaced directly as affordances.
        private static readonly Dictionary<string, string> s_hasComponentSubtypes =
            new(StringComparer.Ordinal)
            {
                ["HasOrderedComponent"] = HasOrderedComponent,
                [HasOrderedComponent] = HasOrderedComponent
            };

        // Base types (base namespace).
        public const string BaseObjectType = "i=58";
        public const string BaseVariableType = "i=62";
        public const string BaseDataVariableType = "i=63";
        public const string PropertyType = "i=68";
        public const string BaseEventType = "i=2041";
        public const string BaseDataType = "i=24";
        public const string Structure = "i=22";
        public const string Union = "i=12756";
        public const string Enumeration = "i=29";
        public const string DataTypeEncodingType = "i=76";
        public const string String = "i=12";
        public const string ByteString = "i=15";
        public const string Integer = "i=27";
        public const string Number = "i=26";
        public const string UriString = "i=23751";

        // Modelling rules (base namespace). The two placeholder identifiers are
        // not adjacent and are not in name order: OPC 10000-5 assigns
        // OptionalPlaceholder 11508 and MandatoryPlaceholder 11510, and 11509
        // is not a ModellingRule Object at all. Both lookup tables below derive
        // from these constants, so every mapping path shares one definition.
        public const string ModellingRuleMandatory = "i=78";
        public const string ModellingRuleOptional = "i=80";
        public const string ModellingRuleMandatoryPlaceholder = "i=11510";
        public const string ModellingRuleOptionalPlaceholder = "i=11508";

        private static readonly Dictionary<string, string> s_modellingRuleToNodeId =
            new(StringComparer.Ordinal)
            {
                ["Mandatory"] = ModellingRuleMandatory,
                ["Optional"] = ModellingRuleOptional,
                ["MandatoryPlaceholder"] = ModellingRuleMandatoryPlaceholder,
                ["OptionalPlaceholder"] = ModellingRuleOptionalPlaceholder
            };

        private static readonly Dictionary<string, string> s_nodeIdToModellingRule =
            new(StringComparer.Ordinal)
            {
                [ModellingRuleMandatory] = "Mandatory",
                [ModellingRuleOptional] = "Optional",
                [ModellingRuleMandatoryPlaceholder] = "MandatoryPlaceholder",
                [ModellingRuleOptionalPlaceholder] = "OptionalPlaceholder"
            };

        private static readonly Dictionary<string, string> s_jsonTypeToDataType =
            new(StringComparer.Ordinal)
            {
                ["boolean"] = "i=1",
                ["integer"] = Integer,
                ["number"] = Number,
                ["string"] = String,
                ["object"] = "i=22",
                ["null"] = BaseDataType
            };

        private static readonly Dictionary<string, string> s_stringFormatToDataType =
            new(StringComparer.Ordinal)
            {
                ["date-time"] = "i=13",
                ["uuid"] = "i=14",
                ["uri"] = UriString
            };

        public static bool TryGetModellingRuleNodeId(string modellingRule, out string nodeId)
        {
            return s_modellingRuleToNodeId.TryGetValue(modellingRule, out nodeId!);
        }

        public static bool TryGetModellingRuleName(string nodeId, out string modellingRule)
        {
            return s_nodeIdToModellingRule.TryGetValue(nodeId, out modellingRule!);
        }

        /// <summary>
        /// Infers the OPC UA DataType a DataSchema denotes, using the canonical
        /// table of WoT Binding §6.11.4.
        /// </summary>
        /// <remarks>
        /// A bare <c>integer</c> or <c>number</c> infers the <em>abstract</em>
        /// Integer and Number, not a concrete width: the schema says only that
        /// the value is whole or numeric, and §6.11.4 makes the abstract type
        /// the honest reading of that, permitting subtype values. A concrete
        /// type is recovered from an explicit annotation, never guessed here.
        /// The <c>string</c> row is refined by <paramref name="contentEncoding"/>
        /// and <paramref name="format"/>, which is how a ByteString, DateTime,
        /// Guid or UriString survives the round trip through JSON Schema.
        /// </remarks>
        public static string MapJsonTypeToDataType(
            string? jsonType,
            string? contentEncoding = null,
            string? format = null)
        {
            if (string.Equals(jsonType, "string", StringComparison.Ordinal))
            {
                if (string.Equals(contentEncoding, Base64Encoding, StringComparison.Ordinal))
                {
                    return ByteString;
                }
                if (format is not null &&
                    s_stringFormatToDataType.TryGetValue(format, out string? formatted))
                {
                    return formatted;
                }
                return String;
            }
            if (jsonType is not null &&
                s_jsonTypeToDataType.TryGetValue(jsonType, out string? dataType))
            {
                return dataType;
            }
            return BaseDataType;
        }

        public static bool IsModellingRule(string modellingRule)
        {
            return s_modellingRuleToNodeId.ContainsKey(modellingRule);
        }

        /// <summary>
        /// Determines whether a reference type (given as a BrowseName or a NodeId)
        /// is a HasComponent subtype whose exact semantics must be pinned by a
        /// typed Reference link, and returns the canonical
        /// base-namespace ExpandedNodeId to use for the link's <c>uav:refId</c>.
        /// </summary>
        public static bool TryGetHasComponentSubtype(string? referenceType, out string subtypeNodeId)
        {
            if (referenceType is not null &&
                s_hasComponentSubtypes.TryGetValue(referenceType, out subtypeNodeId!))
            {
                return true;
            }
            subtypeNodeId = string.Empty;
            return false;
        }

        public static bool TryGetReferenceTypeNodeId(
            string? browseName,
            out string nodeId)
        {
            return NodeSetStandardAliases.TryGetReferenceTypeNodeId(browseName, out nodeId);
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
        /// InverseName did. A reference whose InverseName was used is emitted
        /// with <c>IsForward = false</c>.
        /// </param>
        /// <returns><c>true</c> when the name resolved.</returns>
        public static bool TryResolveReferenceTypeName(
            string? name,
            out string nodeId,
            out bool isForward)
        {
            return NodeSetStandardAliases.TryResolveReferenceTypeName(
                name, out nodeId, out isForward);
        }

        /// <summary>
        /// Gets the InverseName OPC 10000-5 gives a base-namespace
        /// ReferenceType, which is the name a link <c>rel</c> uses to state the
        /// same reference backwards.
        /// </summary>
        /// <param name="nodeId">The ReferenceType's base-namespace NodeId.</param>
        /// <param name="inverseName">
        /// The InverseName, or an empty string when the ReferenceType is
        /// abstract enough to have none.
        /// </param>
        /// <returns><c>true</c> when an InverseName is known.</returns>
        public static bool TryGetReferenceTypeInverseName(
            string? nodeId,
            out string inverseName)
        {
            return NodeSetStandardAliases.TryGetReferenceTypeInverseName(nodeId, out inverseName);
        }

        public static bool TryGetReferenceTypeBrowseName(
            string? referenceType,
            out string browseName)
        {
            return NodeSetStandardAliases.TryGetReferenceTypeBrowseName(
                referenceType, out browseName);
        }

        public static string FormatUInt(uint value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
