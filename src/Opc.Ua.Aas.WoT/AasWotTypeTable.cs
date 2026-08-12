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

#pragma warning disable CA1845 // TODO: remove when netstandard2.1 is no longer a target.

namespace Opc.Ua.Aas.WoT
{
    /// <summary>
    /// Resolves the local I4AAS type names used by Annex F.6.
    /// </summary>
    internal static class AasWotTypeTable
    {
        public static bool TryGetNodeId(string token, out string? nodeId)
        {
            if (token.StartsWith("i4aas:", StringComparison.Ordinal))
            {
                return s_nameToNodeId.TryGetValue(token.Substring(6), out nodeId);
            }
            return s_nameToNodeId.TryGetValue(token, out nodeId);
        }

        public static string? NameFromNodeId(string nodeId)
        {
            return s_nodeIdToName.TryGetValue(Normalize(nodeId), out string? name) ? name : null;
        }

        public static string? AasTypeFromObjectType(string? typeName)
        {
            return typeName is not null && s_objectTypeToAasType.TryGetValue(typeName, out string? aasType)
                ? aasType
                : null;
        }

        private static string Normalize(string nodeId)
        {
            if (nodeId.StartsWith("ns=1;", StringComparison.Ordinal))
            {
                return "nsu=" + Opc.Ua.Aas.V3.Namespaces.AasV3 + ";" + nodeId.Substring(5);
            }
            return nodeId;
        }

        private static readonly Dictionary<string, string> s_nameToNodeId =
            new(StringComparer.Ordinal)
            {
                ["AASType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1011",
                ["AASAssetInformationType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1012",
                ["AASSubmodelType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1013",
                ["AASPropertyType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1021",
                ["AASMultiLanguagePropertyType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1022",
                ["AASRangeType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1023",
                ["AASBlobType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1024",
                ["AASFileType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1025",
                ["AASReferenceElementType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1026",
                ["AASRelationshipElementType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1027",
                ["AASAnnotatedRelationshipElementType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1028",
                ["AASSubmodelElementCollectionType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1029",
                ["AASConceptDescriptionType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1030",
                ["AASSubmodelElementListType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1031",
                ["AASEntityType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1032",
                ["AASBasicEventElementType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1033",
                ["AASOperationType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1034",
                ["AASCapabilityType"] = "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1035"
            };

        private static readonly Dictionary<string, string> s_nodeIdToName =
            BuildReverse();

        private static readonly Dictionary<string, string> s_objectTypeToAasType =
            new(StringComparer.Ordinal)
            {
                ["AASType"] = "AssetAdministrationShell",
                ["AASSubmodelType"] = "Submodel",
                ["AASConceptDescriptionType"] = "ConceptDescription",
                ["AASPropertyType"] = "Property",
                ["AASMultiLanguagePropertyType"] = "MultiLanguageProperty",
                ["AASRangeType"] = "Range",
                ["AASBlobType"] = "Blob",
                ["AASFileType"] = "File",
                ["AASReferenceElementType"] = "ReferenceElement",
                ["AASRelationshipElementType"] = "RelationshipElement",
                ["AASAnnotatedRelationshipElementType"] = "AnnotatedRelationshipElement",
                ["AASSubmodelElementCollectionType"] = "SubmodelElementCollection",
                ["AASSubmodelElementListType"] = "SubmodelElementList",
                ["AASEntityType"] = "Entity",
                ["AASBasicEventElementType"] = "BasicEventElement",
                ["AASOperationType"] = "Operation",
                ["AASCapabilityType"] = "Capability"
            };

        private static Dictionary<string, string> BuildReverse()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in s_nameToNodeId)
            {
                result[pair.Value] = pair.Key;
            }
            return result;
        }
    }

    #pragma warning restore CA1845
}
