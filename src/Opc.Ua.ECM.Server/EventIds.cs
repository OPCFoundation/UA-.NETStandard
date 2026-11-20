// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Event-ID ranges for ECM server diagnostics.
    /// </summary>
    internal static class EcmServerEventIds
    {
        /// <summary>The node manager's diagnostic range.</summary>
        public const int EcmNodeManager = 0;

        /// <summary>The power binding's diagnostic range.</summary>
        public const int EcmPowerBinding = 10;

        /// <summary>The standalone WOL binding's diagnostic range.</summary>
        public const int EcmWolBinding = 20;

        /// <summary>The measurement binding's diagnostic range.</summary>
        public const int EcmMeasurementBinding = 30;
    }
}
