/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// Opc.Ua.WotCon.Server assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location. The
    /// class name is prefixed with the assembly token to avoid CS0436 collisions with the
    /// event-id classes of other assemblies exposed through <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal static class WotConServerEventIds
    {
        /// <summary>
        /// The first event identifier reserved for asset-registry log messages.
        /// </summary>
        public const int AssetRegistry = 0;

        /// <summary>
        /// The first event identifier reserved for asset FileType log messages.
        /// </summary>
        public const int WotAssetFileManager = 40;

        /// <summary>
        /// The first event identifier reserved for connectivity node-manager log messages.
        /// </summary>
        public const int WotConnectivityNodeManager = 50;

        /// <summary>
        /// The first event identifier reserved for registry node-manager log messages.
        /// </summary>
        public const int WotRegistryNodeManager = 60;

        /// <summary>
        /// The first event identifier reserved for projection-view log messages.
        /// </summary>
        public const int WotProjectionViewNodeManager = 70;

        /// <summary>
        /// The WoT observed property source used by <c>WotConServerEventIds</c>.
        /// </summary>
        public const int WotObservedPropertySource = 80;

        /// <summary>
        /// The WoT structured property observation used by <c>WotConServerEventIds</c>.
        /// </summary>
        public const int WotStructuredPropertyObservation = 90;

        /// <summary>
        /// The WoT projection binding runtime used by <c>WotConServerEventIds</c>.
        /// </summary>
        public const int WotProjectionBindingRuntime = 100;

        /// <summary>
        /// The WoT registry projection used by <c>WotConServerEventIds</c>.
        /// </summary>
        public const int WotRegistryProjection = 110;
    }
}
