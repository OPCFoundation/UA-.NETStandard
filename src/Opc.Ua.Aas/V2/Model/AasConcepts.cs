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
    /// An AAS V2 ConceptDictionary.
    /// </summary>
    public sealed record AasConceptDictionary
    {
        /// <summary>
        /// Gets the optional concept descriptions grouped by this dictionary.
        /// </summary>
        public AasOptional<ArrayOf<AasReference>> ConceptDescriptions { get; init; }

        /// <inheritdoc/>
        public string ModelType => "ConceptDictionary";
    }

    /// <summary>
    /// The common fields for AAS V2 concept descriptions.
    /// </summary>
    public abstract record AasConceptDescription : AasIdentifiable, IAasHasDataSpecification
    {
        /// <summary>
        /// Gets the optional concept descriptions this concept is a case of.
        /// </summary>
        public AasOptional<ArrayOf<AasReference>> ConceptDescriptions { get; init; }

        /// <inheritdoc/>
        public AasOptional<ArrayOf<AasReference>> DataSpecifications { get; init; }
    }

    /// <summary>
    /// An AAS V2 concept description identified by a custom identifier.
    /// </summary>
    public sealed record AasCustomConceptDescription : AasConceptDescription
    {
        /// <inheritdoc/>
        public override string ModelType => "CustomConceptDescription";
    }

    /// <summary>
    /// An AAS V2 concept description identified by an IRDI.
    /// </summary>
    public sealed record AasIrdiConceptDescription : AasConceptDescription
    {
        /// <inheritdoc/>
        public override string ModelType => "IrdiConceptDescription";
    }

    /// <summary>
    /// An AAS V2 concept description identified by an IRI.
    /// </summary>
    public sealed record AasIriConceptDescription : AasConceptDescription
    {
        /// <inheritdoc/>
        public override string ModelType => "IriConceptDescription";
    }

    /// <summary>
    /// An AAS V2 View.
    /// </summary>
    public sealed record AasView : IAasHasDataSpecification
    {
        /// <inheritdoc/>
        public AasOptional<ArrayOf<AasReference>> DataSpecifications { get; init; }

        /// <summary>
        /// Gets the optional referable references collected by this view.
        /// </summary>
        public AasOptional<ArrayOf<AasReference>> Referables { get; init; }

        /// <inheritdoc/>
        public string ModelType => "View";
    }
}
