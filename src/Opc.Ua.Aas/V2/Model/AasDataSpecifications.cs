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
    /// The common fields for AAS V2 data specifications.
    /// </summary>
    public abstract record AasDataSpecification : AasIdentifiable
    {
    }

    /// <summary>
    /// An AAS V2 IEC 61360 data specification.
    /// </summary>
    public sealed record AasDataSpecificationIec61360 : AasDataSpecification
    {
        /// <summary>
        /// Gets the mandatory administration information.
        /// </summary>
        public required AasAdministrativeInformation DataSpecificationAdministration { get; init; }

        /// <summary>
        /// Gets the optional category.
        /// </summary>
        public AasOptional<AASCategoryDataType> DataSpecificationCategory { get; init; }

        /// <summary>
        /// Gets the optional IEC 61360 data type.
        /// </summary>
        public AasOptional<AASDataTypeIEC61360DataType> DataType { get; init; }

        /// <summary>
        /// Gets the mandatory default instance browse name.
        /// </summary>
        public required string DefaultInstanceBrowseName { get; init; }

        /// <summary>
        /// Gets the optional definitions.
        /// </summary>
        public AasOptional<ArrayOf<LocalizedText>> Definition { get; init; }

        /// <summary>
        /// Gets the mandatory IEC 61360 identification.
        /// </summary>
        public required AasIdentifier DataSpecificationIdentification { get; init; }

        /// <summary>
        /// Gets the optional level types.
        /// </summary>
        public AasOptional<ArrayOf<AASLevelTypeDataType>> LevelType { get; init; }

        /// <summary>
        /// Gets the mandatory preferred names.
        /// </summary>
        public required ArrayOf<LocalizedText> PreferredName { get; init; }

        /// <summary>
        /// Gets the optional short names.
        /// </summary>
        public AasOptional<ArrayOf<LocalizedText>> ShortName { get; init; }

        /// <summary>
        /// Gets the optional source of definition.
        /// </summary>
        public AasOptional<string> SourceOfDefinition { get; init; }

        /// <summary>
        /// Gets the optional symbol.
        /// </summary>
        public AasOptional<string> Symbol { get; init; }

        /// <summary>
        /// Gets the optional unit.
        /// </summary>
        public AasOptional<string> Unit { get; init; }

        /// <summary>
        /// Gets the optional unit identifier.
        /// </summary>
        public AasOptional<AasReference> UnitId { get; init; }

        /// <summary>
        /// Gets the optional value.
        /// </summary>
        public AasOptional<Variant> Value { get; init; }

        /// <summary>
        /// Gets the optional value format.
        /// </summary>
        public AasOptional<string> ValueFormat { get; init; }

        /// <summary>
        /// Gets the optional value identifier.
        /// </summary>
        public AasOptional<AasReference> ValueId { get; init; }

        /// <summary>
        /// Gets the optional value list reference.
        /// </summary>
        public AasOptional<AasReference> ValueList { get; init; }

        /// <inheritdoc/>
        public override string ModelType => "DataSpecificationIEC61360";
    }
}
