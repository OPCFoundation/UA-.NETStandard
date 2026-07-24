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
        public const string EnvelopePreservationType = "uav:NodeSet2Preservation";
        public const string ProfileVersion = "1.0";
        public const string ThingModelType = "tm:ThingModel";
        public const string WotContext = "https://www.w3.org/2022/wot/td/v1.1";

        // Reference types (base namespace).
        public const string HasSubtype = "i=45";
        public const string HasProperty = "i=46";
        public const string HasComponent = "i=47";
        public const string HasOrderedComponent = "i=49";
        public const string Organizes = "i=35";
        public const string HasTypeDefinition = "i=40";
        public const string HasModellingRule = "i=37";
        public const string GeneratesEvent = "i=41";

        // Type-annotation term for an event affordance projecting a UA EventType.
        public const string EventTypeAnnotation = "uav:eventType";

        private static readonly Dictionary<string, string> s_referenceTypeNameToNodeId =
            new(StringComparer.Ordinal)
            {
                ["Organizes"] = Organizes,
                ["HasModellingRule"] = HasModellingRule,
                ["HasTypeDefinition"] = HasTypeDefinition,
                ["GeneratesEvent"] = GeneratesEvent,
                ["HasSubtype"] = HasSubtype,
                ["HasProperty"] = HasProperty,
                ["HasComponent"] = HasComponent,
                ["HasOrderedComponent"] = HasOrderedComponent
            };

        private static readonly Dictionary<string, string> s_referenceTypeNodeIdToName =
            new(StringComparer.Ordinal)
            {
                [Organizes] = "Organizes",
                [HasModellingRule] = "HasModellingRule",
                [HasTypeDefinition] = "HasTypeDefinition",
                [GeneratesEvent] = "GeneratesEvent",
                [HasSubtype] = "HasSubtype",
                [HasProperty] = "HasProperty",
                [HasComponent] = "HasComponent",
                [HasOrderedComponent] = "HasOrderedComponent"
            };

        // HasComponent subtypes (base namespace) that carry stronger semantics
        // than plain HasComponent and must be pinned by a link whose rel is
        // the ReferenceType model name (WoT Binding Section 5.3). Keyed by both the reference-type
        // BrowseName and its base-namespace NodeId; the value is the canonical
        // base-namespace ExpandedNodeId used for the typed link's uav:refType.
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

        // Modelling rules (base namespace).
        public const string ModellingRuleMandatory = "i=78";
        public const string ModellingRuleOptional = "i=80";
        public const string ModellingRuleMandatoryPlaceholder = "i=11508";
        public const string ModellingRuleOptionalPlaceholder = "i=11509";

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
                ["integer"] = "i=8",
                ["number"] = "i=11",
                ["string"] = "i=12",
                ["object"] = "i=22",
                ["null"] = "i=24"
            };

        public static bool TryGetModellingRuleNodeId(string modellingRule, out string nodeId)
        {
            return s_modellingRuleToNodeId.TryGetValue(modellingRule, out nodeId!);
        }

        public static bool TryGetModellingRuleName(string nodeId, out string modellingRule)
        {
            return s_nodeIdToModellingRule.TryGetValue(nodeId, out modellingRule!);
        }

        public static string MapJsonTypeToDataType(string? jsonType)
        {
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
        /// base-namespace ExpandedNodeId to use for the link's <c>uav:refType</c>.
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
            if (browseName is not null &&
                s_referenceTypeNameToNodeId.TryGetValue(browseName, out nodeId!))
            {
                return true;
            }
            nodeId = string.Empty;
            return false;
        }

        public static bool TryGetReferenceTypeBrowseName(
            string? referenceType,
            out string browseName)
        {
            if (referenceType is not null)
            {
                if (s_referenceTypeNodeIdToName.TryGetValue(
                    referenceType,
                    out browseName!))
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
