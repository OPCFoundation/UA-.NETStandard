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
    /// The common AAS Referable fields of Annex B.1.
    /// </summary>
    /// <remarks>
    /// <see cref="IdShort"/> is optional because the three top-level
    /// Identifiables may omit it and clause 6.1.3 states that a
    /// SubmodelElementList member has no short name. A present empty
    /// <see cref="DisplayName"/>, <see cref="Description"/> or
    /// <see cref="Extensions"/> value means the corresponding OPC UA node is
    /// present with an empty array; <see cref="AasOptional{T}.Absent"/> means
    /// no node is materialized.
    /// </remarks>
    public abstract record AasReferable
    {
        /// <summary>
        /// Gets the optional short name.
        /// </summary>
        public AasOptional<string> IdShort { get; init; }

        /// <summary>
        /// Gets the optional category.
        /// </summary>
        public AasOptional<string> Category { get; init; }

        /// <summary>
        /// Gets the optional display names.
        /// </summary>
        public AasOptional<ArrayOf<AASLangStringDataType>> DisplayName { get; init; }

        /// <summary>
        /// Gets the optional descriptions.
        /// </summary>
        public AasOptional<ArrayOf<AASLangStringDataType>> Description { get; init; }

        /// <summary>
        /// Gets the optional extensions.
        /// </summary>
        public AasOptional<ArrayOf<AASExtensionDataType>> Extensions { get; init; }

        /// <summary>
        /// Gets the metamodel class name materialized in the mandatory
        /// <c>ModelType</c> node.
        /// </summary>
        public abstract string ModelType { get; }
    }

    /// <summary>
    /// The common AAS Identifiable fields of Annex B.1.
    /// </summary>
    public abstract record AasIdentifiable : AasReferable
    {
        /// <summary>
        /// Gets the mandatory global identifier.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// Gets the optional administrative information.
        /// </summary>
        public AasOptional<AASAdministrativeInformationDataType> Administration { get; init; }
    }
}
