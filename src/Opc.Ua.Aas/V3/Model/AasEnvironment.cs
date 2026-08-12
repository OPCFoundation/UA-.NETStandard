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

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// An AAS V3 Environment and its top-level Identifiables.
    /// </summary>
    /// <remarks>
    /// The top-level collections are optional for the same reason as the
    /// member collections below them: clause 6.1.5 requires a serializer to
    /// distinguish a missing collection node from a present empty collection
    /// node.
    /// </remarks>
    public sealed record AasEnvironment
    {
        /// <summary>
        /// Gets the optional Asset Administration Shells.
        /// </summary>
        public AasOptional<ArrayOf<AasShell>> AssetAdministrationShells { get; init; }

        /// <summary>
        /// Gets the optional Submodels.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodel>> Submodels { get; init; }

        /// <summary>
        /// Gets the optional ConceptDescriptions.
        /// </summary>
        public AasOptional<ArrayOf<AasConceptDescription>> ConceptDescriptions { get; init; }
    }

    /// <summary>
    /// An AAS V3 Asset Administration Shell.
    /// </summary>
    public sealed record AasShell : AasIdentifiable, IAasHasDataSpecification
    {
        /// <summary>
        /// Gets the mandatory asset information.
        /// </summary>
        public required AasAssetInformation AssetInformation { get; init; }

        /// <summary>
        /// Gets the optional submodel references.
        /// </summary>
        public AasOptional<ArrayOf<AASReferenceDataType>> SubmodelReferences { get; init; }

        /// <summary>
        /// Gets the optional shell this shell derives from.
        /// </summary>
        public AasOptional<AASReferenceDataType> DerivedFrom { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AASEmbeddedDataSpecificationDataType>> EmbeddedDataSpecifications { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "AssetAdministrationShell";
    }

    /// <summary>
    /// The AAS V3 asset information object carried by a shell.
    /// </summary>
    public sealed record AasAssetInformation
    {
        /// <summary>
        /// Gets the mandatory asset kind.
        /// </summary>
        public required AASAssetKindDataType AssetKind { get; init; }

        /// <summary>
        /// Gets the optional global asset identifier.
        /// </summary>
        public AasOptional<string> GlobalAssetId { get; init; }

        /// <summary>
        /// Gets the optional asset type.
        /// </summary>
        public AasOptional<string> AssetType { get; init; }

        /// <summary>
        /// Gets the optional specific asset identifiers.
        /// </summary>
        public AasOptional<ArrayOf<AASSpecificAssetIdDataType>> SpecificAssetIds { get; init; }

        /// <summary>
        /// Gets the optional default thumbnail.
        /// </summary>
        public AasOptional<AASResourceDataType> DefaultThumbnail { get; init; }
    }

    /// <summary>
    /// An AAS V3 Submodel.
    /// </summary>
    public sealed record AasSubmodel :
        AasIdentifiable,
        IAasHasSemantics,
        IAasHasKind,
        IAasHasDataSpecification,
        IAasQualifiable
    {
        /// <inheritdoc/>
        public AasOptional<AASModellingKindDataType> Kind { get; init; }

        /// <inheritdoc/>
        public AasOptional<AASReferenceDataType> SemanticId { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AASReferenceDataType>> SupplementalSemanticIds { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AASQualifierDataType>> Qualifiers { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AASEmbeddedDataSpecificationDataType>> EmbeddedDataSpecifications { get; init; }

        /// <summary>
        /// Gets the optional direct submodel elements.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> SubmodelElements { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Submodel";
    }

    /// <summary>
    /// An AAS V3 ConceptDescription.
    /// </summary>
    public sealed record AasConceptDescription : AasIdentifiable, IAasHasDataSpecification
    {
        /// <summary>
        /// Gets the optional cases this concept is a case of.
        /// </summary>
        public AasOptional<ArrayOf<AASReferenceDataType>> IsCaseOf { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AASEmbeddedDataSpecificationDataType>> EmbeddedDataSpecifications { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "ConceptDescription";
    }
}
