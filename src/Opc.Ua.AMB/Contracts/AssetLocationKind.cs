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

namespace Opc.Ua.AMB
{
    /// <summary>
    /// The kinds of location OPC 10000-110 §13 distinguishes.
    /// </summary>
    public enum AssetLocationKind
    {
        /// <summary>
        /// Where the asset is in a hierarchy such as plant, hall and line
        /// (§13.3): the <c>HierarchicalLocation</c> Property and the
        /// <c>HierarchicalLocations</c> objects.
        /// </summary>
        Hierarchical,

        /// <summary>
        /// Where the asset is used operationally (§13.4): the
        /// <c>OperationalLocation</c> Property and the
        /// <c>OperationalLocations</c> objects.
        /// </summary>
        Operational,

        /// <summary>
        /// Where a digital asset is deployed (§13.5): the
        /// <c>DigitalLocation</c> Property. It has no location objects.
        /// </summary>
        Digital
    }
}
