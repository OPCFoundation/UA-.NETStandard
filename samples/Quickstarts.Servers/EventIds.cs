/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Quickstarts.Servers
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// Quickstarts.Servers assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location.
    /// </remarks>
    internal static class QuickstartsServersEventIds
    {
        public const int AlarmController = 0;
        public const int AlarmHolder = 20;
        public const int AlarmNodeManager = 30;
        public const int BatchPersistor = 50;
        public const int BoilerState = 60;
        public const int ConditionTypeHolder = 70;
        public const int DiscreteHolder = 80;
        public const int DurableMonitoredItemQueue = 90;
        public const int DurableMonitoredItemQueueFactory = 100;
        public const int MemoryBufferState = 110;
        public const int NonExclusiveLevelHolder = 120;
        public const int ReferenceNodeManager = 130;
        public const int ReferenceServer = 140;
        public const int SampleNodeManager = 160;
        public const int SubscriptionStore = 170;
        public const int TestDataNodeManager = 180;
        public const int TestDataSystem = 190;
        public const int Utils = 200;
    }
}
