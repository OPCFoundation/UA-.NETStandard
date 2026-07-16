/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// The optional Part 17 children that an <see cref="AliasNameCategoryDescriptor"/>
    /// exposes on its <c>AliasNameCategoryType</c> instance. The mandatory
    /// <c>FindAlias</c> method is always created; this enum controls only the
    /// optional members defined by Part 17 §6.3.1.
    /// </summary>
    [Flags]
    public enum AliasNameCapabilities
    {
        /// <summary>
        /// Only the mandatory <c>FindAlias</c> method is exposed.
        /// </summary>
        None = 0,

        /// <summary>
        /// The category exposes <c>FindAliasVerbose</c> (Part 17 §6.3.3).
        /// </summary>
        FindAliasVerbose = 1 << 0,

        /// <summary>
        /// The category exposes <c>AddAliasesToCategory</c> (Part 17 §6.3.4).
        /// </summary>
        AddAliasesToCategory = 1 << 1,

        /// <summary>
        /// The category exposes <c>DeleteAliasesFromCategory</c> (Part 17 §6.3.5).
        /// </summary>
        DeleteAliasesFromCategory = 1 << 2,

        /// <summary>
        /// The category exposes the <c>LastChange</c> property and reflects
        /// updates to it (Part 17 §6.3.1).
        /// </summary>
        LastChange = 1 << 3,

        /// <summary>
        /// Convenience value: read-only category supporting both find variants
        /// plus <c>LastChange</c>.
        /// </summary>
        ReadOnly = FindAliasVerbose | LastChange,

        /// <summary>
        /// Convenience value: read-write category supporting every Part 17
        /// optional member.
        /// </summary>
        All = FindAliasVerbose | AddAliasesToCategory | DeleteAliasesFromCategory | LastChange
    }
}
