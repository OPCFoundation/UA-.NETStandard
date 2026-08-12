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
    /// The common fields of every AAS V3 SubmodelElement.
    /// </summary>
    /// <remarks>
    /// <see cref="Index"/> is optional because only list members and operation
    /// variables carry it. Clause 6.1.3 gives a list member no short name, so
    /// that member is represented by leaving <see cref="AasReferable.IdShort"/>
    /// absent and setting <see cref="Index"/> to its zero-based position.
    /// </remarks>
    public abstract record AasSubmodelElement :
        AasReferable,
        IAasHasSemantics,
        IAasHasDataSpecification,
        IAasQualifiable
    {
        /// <inheritdoc/>
        public AasOptional<AASReferenceDataType> SemanticId { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AASReferenceDataType>> SupplementalSemanticIds { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AASQualifierDataType>> Qualifiers { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AASEmbeddedDataSpecificationDataType>> EmbeddedDataSpecifications { get; init; }

        /// <summary>
        /// Gets the optional zero-based position inside a list or operation
        /// role.
        /// </summary>
        public AasOptional<uint> Index { get; init; }
    }

    /// <summary>
    /// An AAS Property element.
    /// </summary>
    public sealed record AasProperty : AasSubmodelElement
    {
        /// <summary>
        /// Gets the mandatory xsd value type.
        /// </summary>
        public required AASDataTypeDefXsdDataType ValueType { get; init; }

        /// <summary>
        /// Gets the optional value, encoded with the DataType assigned to
        /// <see cref="ValueType"/> by clause 6.3.1.
        /// </summary>
        public AasOptional<Variant> Value { get; init; }

        /// <summary>
        /// Gets the optional value identifier.
        /// </summary>
        public AasOptional<AASReferenceDataType> ValueId { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Property";
    }

    /// <summary>
    /// An AAS MultiLanguageProperty element.
    /// </summary>
    public sealed record AasMultiLanguageProperty : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional language-tagged values.
        /// </summary>
        public AasOptional<ArrayOf<AASLangStringDataType>> Value { get; init; }

        /// <summary>
        /// Gets the optional value identifier.
        /// </summary>
        public AasOptional<AASReferenceDataType> ValueId { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "MultiLanguageProperty";
    }

    /// <summary>
    /// An AAS Range element.
    /// </summary>
    public sealed record AasRange : AasSubmodelElement
    {
        /// <summary>
        /// Gets the mandatory xsd value type of the bounds.
        /// </summary>
        public required AASDataTypeDefXsdDataType ValueType { get; init; }

        /// <summary>
        /// Gets the optional lower bound; absent means unbounded below.
        /// </summary>
        public AasOptional<Variant> Min { get; init; }

        /// <summary>
        /// Gets the optional upper bound; absent means unbounded above.
        /// </summary>
        public AasOptional<Variant> Max { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Range";
    }

    /// <summary>
    /// An AAS Blob element.
    /// </summary>
    public sealed record AasBlob : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional binary value.
        /// </summary>
        public AasOptional<ByteString> Value { get; init; }

        /// <summary>
        /// Gets the mandatory content type.
        /// </summary>
        public required string ContentType { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Blob";
    }

    /// <summary>
    /// An AAS File element.
    /// </summary>
    public sealed record AasFile : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional file path or URI.
        /// </summary>
        public AasOptional<string> Value { get; init; }

        /// <summary>
        /// Gets the mandatory content type.
        /// </summary>
        public required string ContentType { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "File";
    }

    /// <summary>
    /// An AAS ReferenceElement.
    /// </summary>
    public sealed record AasReferenceElement : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional reference value.
        /// </summary>
        public AasOptional<AASReferenceDataType> Value { get; init; }

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
        public required AASReferenceDataType First { get; init; }

        /// <summary>
        /// Gets the mandatory second endpoint.
        /// </summary>
        public required AASReferenceDataType Second { get; init; }
    }

    /// <summary>
    /// An AAS RelationshipElement.
    /// </summary>
    public sealed record AasRelationshipElement : AasRelationshipElementBase
    {
        /// <inheritdoc/>
        public override string ModelType => "RelationshipElement";
    }

    /// <summary>
    /// An AAS AnnotatedRelationshipElement.
    /// </summary>
    public sealed record AasAnnotatedRelationshipElement : AasRelationshipElementBase
    {
        /// <summary>
        /// Gets the optional annotations.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> Annotations { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "AnnotatedRelationshipElement";
    }

    /// <summary>
    /// An AAS SubmodelElementCollection.
    /// </summary>
    public sealed record AasSubmodelElementCollection : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional member elements.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> Value { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "SubmodelElementCollection";
    }

    /// <summary>
    /// An AAS SubmodelElementList.
    /// </summary>
    public sealed record AasSubmodelElementList : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional order flag; absent defaults to <c>true</c>.
        /// </summary>
        public AasOptional<bool> OrderRelevant { get; init; }

        /// <summary>
        /// Gets the mandatory member element type.
        /// </summary>
        public required AASSubmodelElementsDataType TypeValueListElement { get; init; }

        /// <summary>
        /// Gets the optional semantic identifier all members share.
        /// </summary>
        public AasOptional<AASReferenceDataType> SemanticIdListElement { get; init; }

        /// <summary>
        /// Gets the optional xsd value type all data-element members share.
        /// </summary>
        public AasOptional<AASDataTypeDefXsdDataType> ValueTypeListElement { get; init; }

        /// <summary>
        /// Gets the optional ordered member elements.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> Value { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "SubmodelElementList";

        /// <summary>
        /// Gets the effective order relevance after applying the clause 6.1.4
        /// default.
        /// </summary>
        public bool EffectiveOrderRelevant => !OrderRelevant.IsPresent || OrderRelevant.Value;
    }

    /// <summary>
    /// An AAS Entity element.
    /// </summary>
    public sealed record AasEntity : AasSubmodelElement
    {
        /// <summary>
        /// Gets the mandatory entity type.
        /// </summary>
        public required AASEntityTypeDataType EntityType { get; init; }

        /// <summary>
        /// Gets the optional global asset identifier.
        /// </summary>
        public AasOptional<string> GlobalAssetId { get; init; }

        /// <summary>
        /// Gets the optional specific asset identifiers.
        /// </summary>
        public AasOptional<ArrayOf<AASSpecificAssetIdDataType>> SpecificAssetIds { get; init; }

        /// <summary>
        /// Gets the optional statements.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> Statements { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Entity";
    }

    /// <summary>
    /// An AAS BasicEventElement.
    /// </summary>
    public sealed record AasBasicEventElement : AasSubmodelElement
    {
        /// <summary>
        /// Gets the mandatory observed reference.
        /// </summary>
        public required AASReferenceDataType Observed { get; init; }

        /// <summary>
        /// Gets the mandatory direction.
        /// </summary>
        public required AASDirectionDataType Direction { get; init; }

        /// <summary>
        /// Gets the mandatory state.
        /// </summary>
        public required AASStateOfEventDataType State { get; init; }

        /// <summary>
        /// Gets the optional message topic.
        /// </summary>
        public AasOptional<string> MessageTopic { get; init; }

        /// <summary>
        /// Gets the optional message broker reference.
        /// </summary>
        public AasOptional<AASReferenceDataType> MessageBroker { get; init; }

        /// <summary>
        /// Gets the optional last-update value.
        /// </summary>
        public AasOptional<Variant> LastUpdate { get; init; }

        /// <summary>
        /// Gets the optional minimum interval value.
        /// </summary>
        public AasOptional<Variant> MinInterval { get; init; }

        /// <summary>
        /// Gets the optional maximum interval value.
        /// </summary>
        public AasOptional<Variant> MaxInterval { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "BasicEventElement";
    }

    /// <summary>
    /// An AAS Operation element.
    /// </summary>
    public sealed record AasOperation : AasSubmodelElement
    {
        /// <summary>
        /// Gets the optional input variables, ordered by array position.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> InputVariables { get; init; }

        /// <summary>
        /// Gets the optional output variables, ordered by array position.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> OutputVariables { get; init; }

        /// <summary>
        /// Gets the optional inoutput variables, ordered by array position.
        /// </summary>
        public AasOptional<ArrayOf<AasSubmodelElement>> InoutputVariables { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "Operation";
    }

    /// <summary>
    /// An AAS Capability element.
    /// </summary>
    public sealed record AasCapability : AasSubmodelElement
    {
        /// <inheritdoc/>
        public override string ModelType => "Capability";
    }
}
