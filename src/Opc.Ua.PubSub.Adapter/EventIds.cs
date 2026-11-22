// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// Opc.Ua.PubSub.Adapter assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location. The
    /// class name is prefixed with the assembly token to avoid CS0436 collisions with the
    /// event-id classes of other assemblies exposed through <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal static class PubSubAdapterEventIds
    {
        public const int CyclicReadStrategy = 0;
        public const int DataChangeSubscription = 10;
        public const int DataSetMetaDataBuilder = 20;
        public const int ServerActionHandler = 30;
        public const int ServerAdapterReloadCoordinator = 40;
        public const int ServerPublishedDataSetSource = 50;
        public const int ServerSession = 60;
        public const int ServerTargetVariableWriter = 80;
        public const int SubscriptionCoordinator = 90;
        public const int SubscriptionReadStrategy = 100;
    }
}
