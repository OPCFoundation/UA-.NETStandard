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

namespace Opc.Ua
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// Opc.Ua.Server assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location. The
    /// class name is prefixed with the assembly token to avoid CS0436 collisions with the
    /// event-id classes of other assemblies exposed through <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal static class ServerEventIds
    {
        public const int AggregateCalculator = 0;
        public const int AliasNameNodeManager = 10;
        public const int AuditEvents = 20;
        public const int ConfigurationNodeManager = 50;
        public const int DataChangeMonitoredItemQueue = 80;
        public const int DataChangeQueueHandler = 90;
        public const int DiagnosticsNodeManager = 100;
        public const int EventMonitoredItemQueue = 110;
        public const int EventSourceRegistry = 120;
        public const int HistorianCaptureSink = 140;
        public const int HistorianEventFilterTarget = 150;
        public const int JsonUserDatabase = 160;
        public const int MasterNodeManager = 170;
        public const int MonitoredItem = 200;
        public const int MonitoredItemQueue = 220;
        public const int MonitoredNode = 230;
        public const int NodeManager = 250;
        public const int OpcUaServerHostedService = 260;
        public const int RequestManager = 270;
        public const int ReverseConnectServer = 280;
        public const int RoleStateBinding = 300;
        public const int RuntimeNodeSetNodeManagerFactory = 310;
        public const int SamplingGroup = 320;
        public const int SentMessageQueue = 330;
        public const int Session = 340;
        public const int SessionManager = 350;
        public const int SessionPublishQueue = 370;
        public const int SessionSecurityPolicyHelper = 380;
        public const int SimulationRegistry = 390;
        public const int StandardServer = 400;
        public const int Subscription = 440;
        public const int SubscriptionManager = 450;
        public const int TrustList = 490;
        public const int UserManagementBinding = 500;
    }
}
