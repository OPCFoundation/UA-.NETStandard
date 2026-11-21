// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
