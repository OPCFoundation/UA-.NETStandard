// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// The alias categories through which clients discover the assets of a
    /// server (OPC 10000-110 §8.2).
    /// </summary>
    [Flags]
    public enum AssetDiscovery
    {
        /// <summary>
        /// The assets are not published as aliases; the categories stay empty
        /// and <c>FindAlias</c> finds nothing.
        /// </summary>
        None = 0,

        /// <summary>
        /// <c>AssetsByProductInstanceUri</c> lists every asset under its
        /// <c>ProductInstanceUri</c>.
        /// </summary>
        ProductInstanceUri = 1,

        /// <summary>
        /// <c>AssetsByAssetId</c> lists every asset under its <c>AssetId</c>,
        /// or under <c>NoAssetIdAssigned</c> while it has none.
        /// </summary>
        AssetId = 2,

        /// <summary>
        /// Both categories.
        /// </summary>
        All = ProductInstanceUri | AssetId
    }
}
