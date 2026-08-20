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
    /// The AAS V2 identifier pair.
    /// </summary>
    public sealed record AasIdentifier
    {
        /// <summary>
        /// Gets the mandatory identifier value.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// Gets the mandatory identifier type.
        /// </summary>
        public required AASIdentifierTypeDataType IdType { get; init; }

        /// <inheritdoc/>
        public string ModelType => "Identifier";
    }

    /// <summary>
    /// The AAS V2 administrative information.
    /// </summary>
    public sealed record AasAdministrativeInformation
    {
        /// <summary>
        /// Gets the mandatory revision.
        /// </summary>
        public required string Revision { get; init; }

        /// <summary>
        /// Gets the mandatory version.
        /// </summary>
        public required string Version { get; init; }

        /// <inheritdoc/>
        public string ModelType => "AdministrativeInformation";
    }

    /// <summary>
    /// An AAS V2 reference.
    /// </summary>
    public sealed record AasReference
    {
        /// <summary>
        /// Gets the optional referable objects resolved for the reference.
        /// </summary>
        public AasOptional<ArrayOf<AasReferable>> Referables { get; init; }

        /// <summary>
        /// Gets the mandatory reference keys.
        /// </summary>
        public required ArrayOf<AASKeyDataType> Keys { get; init; }

        /// <inheritdoc/>
        public string ModelType => "Reference";
    }

    /// <summary>
    /// An AAS V2 qualifier.
    /// </summary>
    public sealed record AasQualifier
    {
        /// <summary>
        /// Gets the mandatory qualifier type.
        /// </summary>
        public required string Type { get; init; }

        /// <summary>
        /// Gets the optional qualifier value.
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
        public string ModelType => "Qualifier";
    }
}
