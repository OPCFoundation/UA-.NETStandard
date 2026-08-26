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

namespace Opc.Ua.Aas.Server.V2
{
    /// <summary>
    /// OPC 30270 I4AAS conformance unit names.
    /// </summary>
    /// <remarks>
    /// The seventeen units of Table 83. A Server publishes the ones it
    /// enables in Server/ServerCapabilities/ConformanceUnits, and
    /// ServerProfileArray gains no I4AAS entry because Table 84, which would
    /// assign the profile URIs, is empty.
    /// <para>
    /// Two places where the specification disagrees with itself, resolved
    /// here in favour of the reading a Client is likely to hold. Table 83
    /// spells the multi-language unit "I4AAS MultiLangaugeProperty" while
    /// Table 85 spells it "I4AAS MultiLanguageProperty"; the corrected
    /// spelling is published, because it matches both the facet table and the
    /// AASMultiLanguagePropertyType the unit is named for. Table 85 also
    /// lists an "I4AAS Security" unit that Table 83 never defines, so it is
    /// not published at all.
    /// </para>
    /// </remarks>
    public static class AasV2ConformanceUnits
    {
        /// <summary>
        /// Supports an instance of the AASAssetAdministrationShellType.
        /// </summary>
        public const string Aas = "I4AAS AAS";

        /// <summary>
        /// Supports instances of the AASAssetType.
        /// </summary>
        public const string Asset = "I4AAS Asset";

        /// <summary>
        /// Supports instances of the AASSubmodelType.
        /// </summary>
        public const string Submodel = "I4AAS Submodel";

        /// <summary>
        /// Supports instances of any AAS concept description type.
        /// </summary>
        public const string ConceptDescription = "I4AAS ConceptDescription";

        /// <summary>
        /// Supports instances of the AASViewType.
        /// </summary>
        public const string View = "I4AAS View";

        /// <summary>
        /// Supports instances of the AASRelationshipElementType.
        /// </summary>
        public const string RelationshipElement = "I4AAS RelationshipElement";

        /// <summary>
        /// Supports instances of the AASPropertyType.
        /// </summary>
        public const string Property = "I4AAS Property";

        /// <summary>
        /// Supports instances of the AASMultiLanguagePropertyType.
        /// </summary>
        public const string MultiLanguageProperty = "I4AAS MultiLanguageProperty";

        /// <summary>
        /// Supports instances of the AASRangeType.
        /// </summary>
        public const string Range = "I4AAS Range";

        /// <summary>
        /// Supports instances of the AASBlobType.
        /// </summary>
        public const string Blob = "I4AAS Blob";

        /// <summary>
        /// Supports instances of the AASFileType.
        /// </summary>
        public const string File = "I4AAS File";

        /// <summary>
        /// Supports instances of the AASReferenceElementType.
        /// </summary>
        public const string ReferenceElement = "I4AAS ReferenceElement";

        /// <summary>
        /// Supports instances of the AASCapabilityType.
        /// </summary>
        public const string Capability = "I4AAS Capability";

        /// <summary>
        /// Supports instances of the AASSubmodelElementCollectionType.
        /// </summary>
        public const string SubmodelElementCollection = "I4AAS SubmodelElementCollection";

        /// <summary>
        /// Supports instances of the AASOperationType.
        /// </summary>
        public const string Operation = "I4AAS Operation";

        /// <summary>
        /// Supports instances of the AASEventType.
        /// </summary>
        public const string Event = "I4AAS Event";

        /// <summary>
        /// Supports instances of the AASEntityType.
        /// </summary>
        public const string Entity = "I4AAS Entity";
    }
}
