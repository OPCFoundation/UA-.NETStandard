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
    /// The AAS V2 HasKind aspect.
    /// </summary>
    public interface IAasHasKind
    {
        /// <summary>
        /// Gets the mandatory modelling kind.
        /// </summary>
        AASModelingKindDataType ModelingKind { get; init; }
    }

    /// <summary>
    /// The AAS V2 HasDataSpecification aspect.
    /// </summary>
    public interface IAasHasDataSpecification
    {
        /// <summary>
        /// Gets the optional data specification references.
        /// </summary>
        AasOptional<ArrayOf<AasReference>> DataSpecifications { get; init; }
    }

    /// <summary>
    /// The AAS V2 Qualifiable aspect.
    /// </summary>
    public interface IAasQualifiable
    {
        /// <summary>
        /// Gets the optional qualifiers.
        /// </summary>
        AasOptional<ArrayOf<AasQualifier>> Qualifiers { get; init; }
    }
}
