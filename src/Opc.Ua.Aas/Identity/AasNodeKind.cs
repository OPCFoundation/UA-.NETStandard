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

namespace Opc.Ua.Aas
{
    /// <summary>
    /// The kind of AAS node a String NodeId identifies, carried by the
    /// discriminator that follows the <c>i4aas3:</c> prefix.
    /// </summary>
    /// <remarks>
    /// Clause 6.1.3 gives the four kinds distinct discriminators so that a
    /// shell, a submodel, a concept description and a submodel element can
    /// never collide even where their source strings coincide.
    /// </remarks>
    public enum AasNodeKind
    {
        /// <summary>
        /// An Asset Administration Shell, discriminator <c>A</c>.
        /// </summary>
        Shell,

        /// <summary>
        /// A Submodel, discriminator <c>S</c>.
        /// </summary>
        Submodel,

        /// <summary>
        /// A Concept Description, discriminator <c>C</c>.
        /// </summary>
        ConceptDescription,

        /// <summary>
        /// A Submodel Element, discriminator <c>E</c>.
        /// </summary>
        SubmodelElement
    }
}
