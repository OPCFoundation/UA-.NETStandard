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

namespace Opc.Ua.Aas.V2
{
    /// <summary>
    /// An AAS V2.0.1 Environment and its top-level Identifiables.
    /// </summary>
    public sealed record AasEnvironment
    {
        /// <summary>
        /// Gets the optional Asset Administration Shells.
        /// </summary>
        public AasOptional<ArrayOf<AasShell>> AssetAdministrationShells { get; init; }

        /// <summary>
        /// Gets the optional Assets.
        /// </summary>
        public AasOptional<ArrayOf<AasAsset>> Assets { get; init; }

        /// <summary>
        /// Gets the optional Submodels.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodel>> Submodels { get; init; }

        /// <summary>
        /// Gets the optional concept descriptions keyed by custom identifiers.
        /// </summary>
        public AasOptional<ArrayOf<AasCustomConceptDescription>> CustomConceptDescriptions { get; init; }

        /// <summary>
        /// Gets the optional concept descriptions keyed by IRDI identifiers.
        /// </summary>
        public AasOptional<ArrayOf<AasIrdiConceptDescription>> IrdiConceptDescriptions { get; init; }

        /// <summary>
        /// Gets the optional concept descriptions keyed by IRI identifiers.
        /// </summary>
        public AasOptional<ArrayOf<AasIriConceptDescription>> IriConceptDescriptions { get; init; }

        /// <summary>
        /// Gets the optional data specifications.
        /// </summary>
        public AasOptional<ArrayOf<AasDataSpecification>> DataSpecifications { get; init; }
    }

    /// <summary>
    /// An AAS V2 Asset Administration Shell.
    /// </summary>
    public sealed record AasShell : AasIdentifiable, IAasHasDataSpecification
    {
        /// <summary>
        /// Gets the optional concept dictionaries.
        /// </summary>
        public AasOptional<ArrayOf<AasConceptDictionary>> ConceptDictionaries { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AasReference>> DataSpecifications { get; init; }

        /// <summary>
        /// Gets the optional directly contained submodels.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodel>> Submodels { get; init; }

        /// <summary>
        /// Gets the optional submodel references.
        /// </summary>
        public AasOptional<ArrayOf<AasReference>> SubmodelReferences { get; init; }

        /// <summary>
        /// Gets the optional views.
        /// </summary>
        public AasOptional<ArrayOf<AasView>> Views { get; init; }

        /// <summary>
        /// Gets the mandatory Asset identifiable.
        /// </summary>
        public required AasAsset Asset { get; init; }

        /// <summary>
        /// Gets the optional shell this shell derives from.
        /// </summary>
        public AasOptional<AasReference> DerivedFrom { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "AssetAdministrationShell";
    }

    /// <summary>
    /// An AAS V2 Asset identifiable.
    /// </summary>
    public sealed record AasAsset : AasIdentifiable, IAasHasDataSpecification
    {
        /// <inheritdoc/>
        public AasOptional<ArrayOf<AasReference>> DataSpecifications { get; init; }

        /// <summary>
        /// Gets the optional asset identification model reference.
        /// </summary>
        public AasOptional<AasReference> AssetIdentificationModel { get; init; }

        /// <summary>
        /// Gets the mandatory asset kind.
        /// </summary>
        public required AASAssetKindDataType AssetKind { get; init; }

        /// <summary>
        /// Gets the optional bill of material reference.
        /// </summary>
        public AasOptional<AasReference> BillOfMaterial { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Asset";
    }

    /// <summary>
    /// An AAS V2 Submodel.
    /// </summary>
    public sealed record AasSubmodel :
        AasIdentifiable,
        IAasHasDataSpecification,
        IAasQualifiable,
        IAasHasKind
    {
        /// <inheritdoc/>
        public AasOptional<ArrayOf<AasReference>> DataSpecifications { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AasQualifier>> Qualifiers { get; init; }

        /// <summary>
        /// Gets the optional direct submodel elements.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> SubmodelElements { get; init; }

        /// <inheritdoc/>
        public required AASModelingKindDataType ModelingKind { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Submodel";
    }
}
