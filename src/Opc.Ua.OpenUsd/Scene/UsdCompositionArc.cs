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

namespace Opc.Ua.OpenUsd.Scene
{
    /// <summary>
    /// A recorded composition arc — how a composed prim came to be
    /// (draft OPC UA — OpenUSD Scene Materialization §5.6). Preserving the arc list is
    /// what makes the §7.4 round-trip contract provenance-aware.
    /// </summary>
    public sealed class UsdCompositionArc
    {
        /// <summary>
        /// Creates a composition arc.
        /// </summary>
        /// <param name="arcKind">The kind of arc.</param>
        public UsdCompositionArc(UsdArcKindEnum arcKind)
        {
            ArcKind = arcKind;
        }

        /// <summary>
        /// The kind of composition arc.
        /// </summary>
        public UsdArcKindEnum ArcKind { get; }

        /// <summary>
        /// The referenced asset path, when the arc names one.
        /// </summary>
        public string AssetPath { get; set; } = string.Empty;

        /// <summary>
        /// The target prim path within the referenced asset, when authored.
        /// </summary>
        public string PrimPath { get; set; } = string.Empty;

        /// <summary>
        /// The list-edit position the arc was authored with.
        /// </summary>
        public UsdListOpTypeEnum ListPosition { get; set; } = UsdListOpTypeEnum.Explicit;

        /// <summary>
        /// The variant set name, for a <see cref="UsdArcKindEnum.VariantSet"/> arc.
        /// </summary>
        public string VariantSet { get; set; } = string.Empty;

        /// <summary>
        /// The selected variant, for a <see cref="UsdArcKindEnum.VariantSet"/> arc.
        /// </summary>
        public string VariantSelection { get; set; } = string.Empty;
    }
}
