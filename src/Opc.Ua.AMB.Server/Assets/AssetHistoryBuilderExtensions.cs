// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// Adds optional AMB history configuration to asset builders.
    /// </summary>
    public static class AssetHistoryBuilderExtensions
    {
        /// <summary>
        /// Overrides the registry's history configuration for this asset.
        /// Null disables AMB history; existing owner-provided history is left unchanged.
        /// </summary>
        /// <param name="builder">The asset builder.</param>
        /// <param name="history">The history configuration, or null to disable AMB history.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
        /// <exception cref="NotSupportedException">
        /// The builder does not implement <see cref="IAssetHistoryBuilder"/>.
        /// </exception>
        public static IAssetBuilder WithHistory(this IAssetBuilder builder, AssetHistoryOptions? history)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder is not IAssetHistoryBuilder historyBuilder)
            {
                throw new NotSupportedException("This asset builder does not support history configuration.");
            }
            return historyBuilder.WithHistory(history);
        }
    }
}
