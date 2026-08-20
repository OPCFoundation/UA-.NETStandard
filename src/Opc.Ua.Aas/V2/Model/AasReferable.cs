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
    /// The common AAS V2 Referable fields modelled by IAASReferableType.
    /// </summary>
    public abstract record AasReferable
    {
        /// <summary>
        /// Gets the mandatory short name.
        /// </summary>
        public required string IdShort { get; init; }

        /// <summary>
        /// Gets the mandatory category.
        /// </summary>
        public required string Category { get; init; }

        /// <summary>
        /// Gets the metamodel class name represented by the object type.
        /// </summary>
        public abstract string ModelType { get; }
    }

    /// <summary>
    /// The common AAS V2 Identifiable fields modelled by IAASIdentifiableType.
    /// </summary>
    public abstract record AasIdentifiable : AasReferable
    {
        /// <summary>
        /// Gets the mandatory identification pair.
        /// </summary>
        public required AasIdentifier Identification { get; init; }

        /// <summary>
        /// Gets the mandatory administrative information.
        /// </summary>
        public required AasAdministrativeInformation Administration { get; init; }
    }
}
