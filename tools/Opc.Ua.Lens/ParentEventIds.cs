/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Parent-owned UaLens modules reserve event IDs 500-999.
    /// IDs 550-999 remain available within this allocation.
    /// </summary>
    internal static partial class UaLensEventIds
    {
        /// <summary>
        /// ChannelV2EngineAdapter reserves event IDs 500-509.
        /// </summary>
        public const int ChannelV2EngineAdapter = 500;

        /// <summary>
        /// ClassicEngineAdapter reserves event IDs 510-519.
        /// </summary>
        public const int ClassicEngineAdapter = 510;

        /// <summary>
        /// DiscoveryService reserves event IDs 520-529.
        /// </summary>
        public const int DiscoveryService = 520;

        /// <summary>
        /// SubscriptionViewModel reserves event IDs 530-549.
        /// </summary>
        public const int SubscriptionViewModel = 530;
        public const int CertificateStoreService = 550;
        public const int NodeSetExporter = 560;
        public const int BrowserViewModel = 570;
        public const int NodeAttributesViewModel = 580;
        public const int ReferencesViewModel = 590;
    }
}
