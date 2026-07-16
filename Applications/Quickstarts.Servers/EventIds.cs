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
