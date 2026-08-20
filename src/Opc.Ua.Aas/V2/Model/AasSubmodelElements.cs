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
    /// The common fields of every AAS V2 SubmodelElement.
    /// </summary>
    public abstract record AasSubmodelElement :
        AasReferable,
        IAasHasDataSpecification,
        IAasQualifiable,
        IAasHasKind
    {
        /// <inheritdoc/>
        public AasOptional<ArrayOf<AasReference>> DataSpecifications { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AasQualifier>> Qualifiers { get; init; }

        /// <inheritdoc/>
        public required AASModelingKindDataType ModelingKind { get; init; }
    }

    /// <summary>
    /// An AAS V2 Blob element.
    /// </summary>
    public sealed record AasBlob : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional file object value.
        /// </summary>
        public AasOptional<AasFileObject> File { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Blob";
    }

    /// <summary>
    /// An AAS V2 Capability element.
    /// </summary>
    public sealed record AasCapability : AasSubmodelElement
    {
        /// <inheritdoc/>
        public override string ModelType => "Capability";
    }

    /// <summary>
    /// An AAS V2 Entity element.
    /// </summary>
    public sealed record AasEntity : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional referenced asset.
        /// </summary>
        public AasOptional<AasReference> Asset { get; init; }

        /// <summary>
        /// Gets the mandatory entity type.
        /// </summary>
        public required AASEntityTypeDataType EntityType { get; init; }

        /// <summary>
        /// Gets the optional statements.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> Statements { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Entity";
    }

    /// <summary>
    /// An AAS V2 Event element.
    /// </summary>
    public sealed record AasEvent : AasSubmodelElement
    {
        /// <inheritdoc/>
        public override string ModelType => "Event";
    }

    /// <summary>
    /// An AAS V2 file object shared by Blob and File members.
    /// </summary>
    public sealed record AasFileObject
    {
        /// <summary>
        /// Gets the optional binary content.
        /// </summary>
        public AasOptional<ByteString> Value { get; init; }
    }

    /// <summary>
    /// An AAS V2 File element.
    /// </summary>
    public sealed record AasFile : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional file object.
        /// </summary>
        public AasOptional<AasFileObject> File { get; init; }

        /// <summary>
        /// Gets the mandatory MIME type.
        /// </summary>
        public required string MimeType { get; init; }

        /// <summary>
        /// Gets the mandatory file path or URI value.
        /// </summary>
        public required string Value { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "File";
    }

    /// <summary>
    /// An AAS V2 MultiLanguageProperty element.
    /// </summary>
    public sealed record AasMultiLanguageProperty : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional language-tagged values.
        /// </summary>
        public AasOptional<ArrayOf<LocalizedText>> Value { get; init; }

        /// <summary>
        /// Gets the optional value identifier.
        /// </summary>
        public AasOptional<AasReference> ValueId { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "MultiLanguageProperty";
    }

    /// <summary>
    /// An AAS V2 Operation element.
    /// </summary>
    public sealed record AasOperation : AasSubmodelElement
    {
        /// <inheritdoc/>
        public override string ModelType => "Operation";
    }

    /// <summary>
    /// An AAS V2 Property element.
    /// </summary>
    public sealed record AasProperty : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional value encoded according to <see cref="ValueType"/>.
        /// </summary>
        public AasOptional<Variant> Value { get; init; }

        /// <summary>
        /// Gets the optional value identifier.
        /// </summary>
        public AasOptional<AasReference> ValueId { get; init; }

        /// <summary>
        /// Gets the mandatory value type.
        /// </summary>
        public required AASValueTypeDataType ValueType { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Property";
    }

    /// <summary>
    /// An AAS V2 Range element.
    /// </summary>
    public sealed record AasRange : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional upper bound.
        /// </summary>
        public AasOptional<Variant> Max { get; init; }

        /// <summary>
        /// Gets the optional lower bound.
        /// </summary>
        public AasOptional<Variant> Min { get; init; }

        /// <summary>
        /// Gets the mandatory value type.
        /// </summary>
        public required AASValueTypeDataType ValueType { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Range";
    }

    /// <summary>
    /// An AAS V2 ReferenceElement.
    /// </summary>
    public sealed record AasReferenceElement : AasSubmodelElement
    {
        /// <summary>
        /// Gets the mandatory reference value.
        /// </summary>
        public required AasReference Value { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "ReferenceElement";
    }

    /// <summary>
    /// The common endpoint fields for relationship elements.
    /// </summary>
    public abstract record AasRelationshipElementBase : AasSubmodelElement
    {
        /// <summary>
        /// Gets the mandatory first endpoint.
        /// </summary>
        public required AasReference First { get; init; }

        /// <summary>
        /// Gets the mandatory second endpoint.
        /// </summary>
        public required AasReference Second { get; init; }
    }

    /// <summary>
    /// An AAS V2 RelationshipElement.
    /// </summary>
    public sealed record AasRelationshipElement : AasRelationshipElementBase
    {
        /// <inheritdoc/>
        public override string ModelType => "RelationshipElement";
    }

    /// <summary>
    /// An AAS V2 AnnotatedRelationshipElement.
    /// </summary>
    public sealed record AasAnnotatedRelationshipElement : AasRelationshipElementBase
    {
        /// <summary>
        /// Gets the optional annotations.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> DataElements { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "AnnotatedRelationshipElement";
    }

    /// <summary>
    /// An AAS V2 SubmodelElementCollection.
    /// </summary>
    public abstract record AasSubmodelElementCollectionBase : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional member elements.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> SubmodelElements { get; init; }

        /// <summary>
        /// Gets the optional duplicate allowance flag.
        /// </summary>
        public AasOptional<bool> AllowDuplicates { get; init; }

    }

    /// <summary>
    /// An AAS V2 SubmodelElementCollection.
    /// </summary>
    public sealed record AasSubmodelElementCollection : AasSubmodelElementCollectionBase
    {
        /// <inheritdoc/>
        public override string ModelType => "SubmodelElementCollection";
    }

    /// <summary>
    /// An AAS V2 ordered SubmodelElementCollection.
    /// </summary>
    public sealed record AasOrderedSubmodelElementCollection : AasSubmodelElementCollectionBase
    {
        /// <inheritdoc/>
        public override string ModelType => "OrderedSubmodelElementCollection";
    }
}
