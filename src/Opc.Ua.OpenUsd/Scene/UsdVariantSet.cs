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

using System.Collections.Generic;

namespace Opc.Ua.OpenUsd.Scene
{
    /// <summary>
    /// A variant set on a prim together with its resolved selection and the full set of
    /// authored variant branches (draft OPC UA — OpenUSD Scene Materialization §5.6).
    /// </summary>
    public sealed class UsdVariantSet
    {
        /// <summary>
        /// Creates a variant set.
        /// </summary>
        /// <param name="setName">The variant set name.</param>
        /// <param name="selection">The selected variant, or an empty string when nothing
        /// is selected.</param>
        public UsdVariantSet(string setName, string selection = "")
        {
            SetName = setName ?? string.Empty;
            Selection = selection ?? string.Empty;
        }

        /// <summary>
        /// The variant set name.
        /// </summary>
        public string SetName { get; }

        /// <summary>
        /// The selected variant.
        /// </summary>
        public string Selection { get; set; }

        /// <summary>
        /// The authored variant branches of this set, in authored order (§5.6). Each branch
        /// is prim-shaped: its <see cref="UsdPrim.Name"/> is the variant name and its
        /// attributes, relationships and child prims are the content the branch contributes
        /// when it is the selection. All branches are captured — not only the selected one —
        /// so the Composition Provenance CU can materialize the full
        /// <c>&lt;Variant&gt;</c> structure the model defines under <c>UsdVariantSetType</c>.
        /// This authoring provenance is intentionally separate from the composed result and
        /// therefore excluded from the §7.4 composed-scene signature.
        /// </summary>
        public IList<UsdPrim> Variants { get; } = new List<UsdPrim>();
    }
}
