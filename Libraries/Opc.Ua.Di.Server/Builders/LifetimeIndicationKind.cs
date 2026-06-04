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

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Classification of a <see cref="LifetimeVariableState"/> per
    /// OPC 10000-100 §10.6. Determines which
    /// <c>BaseLifetimeIndicationType</c> subtype is referenced from the
    /// variable's <c>Indication</c> property.
    /// </summary>
    public enum LifetimeIndicationKind
    {
        /// <summary>
        /// Time-based lifetime (e.g. operating hours).
        /// </summary>
        Time,
        /// <summary>
        /// Count of produced parts.
        /// </summary>
        NumberOfParts,
        /// <summary>
        /// Count of usage cycles.
        /// </summary>
        NumberOfUsages,
        /// <summary>
        /// Linear length consumed (m).
        /// </summary>
        Length,
        /// <summary>
        /// Diameter remaining (m).
        /// </summary>
        Diameter,
        /// <summary>
        /// Substance volume remaining (m³).
        /// </summary>
        SubstanceVolume
    }
}
