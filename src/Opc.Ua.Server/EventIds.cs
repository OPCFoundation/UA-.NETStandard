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
        /// <summary>
        /// Event identifier offset for aggregate calculation messages.
        /// </summary>
        public const int AggregateCalculator = 0;

        /// <summary>
        /// Event identifier offset for alias-name node management messages.
        /// </summary>
        public const int AliasNameNodeManager = 10;

        /// <summary>
        /// Event identifier offset for application configuration file messages.
        /// </summary>
        public const int ApplicationConfigurationFile = 20;

        /// <summary>
        /// Event identifier offset for audit event reporting messages.
        /// </summary>
        public const int AuditEvents = 30;

        /// <summary>
        /// Event identifier offset for certificate alarm scheduling messages.
        /// </summary>
        public const int CertificateAlarmScheduler = 580;

        /// <summary>
        /// Event identifier offset for certificate group alarm monitoring messages.
        /// </summary>
        public const int CertificateGroupAlarmMonitor = 60;

        /// <summary>
        /// Event identifier offset for configuration node management messages.
        /// </summary>
        public const int ConfigurationNodeManager = 70;

        /// <summary>
        /// Event identifier offset for data-change monitored-item queue messages.
        /// </summary>
        public const int DataChangeMonitoredItemQueue = 110;

        /// <summary>
        /// Event identifier offset for data-change queue handling messages.
        /// </summary>
        public const int DataChangeQueueHandler = 120;

        /// <summary>
        /// Event identifier offset for diagnostics node management messages.
        /// </summary>
        public const int DiagnosticsNodeManager = 130;

        /// <summary>
        /// Event identifier offset for event monitored-item queue messages.
        /// </summary>
        public const int EventMonitoredItemQueue = 140;

        /// <summary>
        /// Event identifier offset for event source registry messages.
        /// </summary>
        public const int EventSourceRegistry = 150;

        /// <summary>
        /// Event identifier offset for historian sample capture messages.
        /// </summary>
        public const int HistorianCaptureSink = 170;

        /// <summary>
        /// Event identifier offset for historian event filtering messages.
        /// </summary>
        public const int HistorianEventFilterTarget = 180;

        /// <summary>
        /// Event identifier offset for the JSON user database's messages.
        /// </summary>
        public const int JsonUserDatabase = 190;

        /// <summary>
        /// Event identifier offset for master node management messages.
        /// </summary>
        public const int MasterNodeManager = 200;

        /// <summary>
        /// Event identifier offset for monitored-item lifecycle and notification messages.
        /// </summary>
        public const int MonitoredItem = 230;

        /// <summary>
        /// Event identifier offset for monitored source registry messages.
        /// </summary>
        public const int MonitoredSourceRegistry = 240;

        /// <summary>
        /// Event identifier offset for monitored-item queue messages.
        /// </summary>
        public const int MonitoredItemQueue = 250;

        /// <summary>
        /// Event identifier offset for monitored node messages.
        /// </summary>
        public const int MonitoredNode = 260;

        /// <summary>
        /// Event identifier offset for namespace metadata publication messages.
        /// </summary>
        public const int NamespaceMetadataPublisher = 270;

        /// <summary>
        /// Event identifier offset for namespace metadata registry messages.
        /// </summary>
        public const int NamespaceMetadataRegistry = 570;

        /// <summary>
        /// Event identifier offset for node management messages.
        /// </summary>
        public const int NodeManager = 280;

        /// <summary>
        /// Event identifier offset for the hosted OPC UA server's lifecycle messages.
        /// </summary>
        public const int OpcUaServerHostedService = 290;

        /// <summary>
        /// Event identifier offset for push-configuration transaction coordination messages.
        /// </summary>
        public const int PushConfigurationTransactionCoordinator = 300;

        /// <summary>
        /// Event identifier offset for applying push-configuration trust list changes.
        /// </summary>
        public const int PushConfigurationTrustListEffectHandler = 310;

        /// <summary>
        /// Event identifier offset for request management messages.
        /// </summary>
        public const int RequestManager = 320;

        /// <summary>
        /// Event identifier offset for reverse-connect server messages.
        /// </summary>
        public const int ReverseConnectServer = 330;

        /// <summary>
        /// Event identifier offset for role state binding messages.
        /// </summary>
        public const int RoleStateBinding = 350;

        /// <summary>
        /// Event identifier offset for runtime NodeSet node manager factory messages.
        /// </summary>
        public const int RuntimeNodeSetNodeManagerFactory = 360;

        /// <summary>
        /// Event identifier offset for monitored-item sampling group messages.
        /// </summary>
        public const int SamplingGroup = 370;

        /// <summary>
        /// Event identifier offset for sent notification message queue messages.
        /// </summary>
        public const int SentMessageQueue = 380;

        /// <summary>
        /// Event identifier offset for session lifecycle messages.
        /// </summary>
        public const int Session = 390;

        /// <summary>
        /// Event identifier offset for session management messages.
        /// </summary>
        public const int SessionManager = 400;

        /// <summary>
        /// Event identifier offset for session publish queue messages.
        /// </summary>
        public const int SessionPublishQueue = 420;

        /// <summary>
        /// Event identifier offset for session security policy messages.
        /// </summary>
        public const int SessionSecurityPolicyHelper = 430;

        /// <summary>
        /// Event identifier offset for simulation registry messages.
        /// </summary>
        public const int SimulationRegistry = 440;

        /// <summary>
        /// Event identifier offset for standard server lifecycle and service messages.
        /// </summary>
        public const int StandardServer = 450;

        /// <summary>
        /// Event identifier offset for subscription lifecycle and publishing messages.
        /// </summary>
        public const int Subscription = 490;

        /// <summary>
        /// Event identifier offset for subscription management messages.
        /// </summary>
        public const int SubscriptionManager = 500;

        /// <summary>
        /// Event identifier offset for trust list management messages.
        /// </summary>
        public const int TrustList = 540;

        /// <summary>
        /// Event identifier offset for user management binding messages.
        /// </summary>
        public const int UserManagementBinding = 550;

        /// <summary>
        /// Event identifier offset for runtime NodeSet node management messages.
        /// </summary>
        public const int RuntimeNodeSetNodeManager = 560;

        /// <summary>
        /// Event identifier offset for historian event capture messages.
        /// </summary>
        public const int HistorianEventCapture = 570;

        /// <summary>
        /// Event identifier offset for fluent node manager messages.
        /// </summary>
        public const int FluentNodeManager = 590;
    }

    /// <summary>
    /// Retained event ids for the removed "OPC-UA-Server" <c>EventSource</c> provider.
    /// </summary>
    /// <remarks>
    /// See docs/DeveloperGuide.md, "Narrow exception: retained EventSource-compatibility
    /// ids". These are the literal legacy numeric ids, scoped to the "OPC-UA-Server"
    /// <see cref="Microsoft.Extensions.Logging.ILogger"/> category, so they intentionally
    /// overlap the ordinary per-class offsets in <see cref="ServerEventIds"/> above. Id 1
    /// (the legacy <c>SendResponse</c> event) was never implemented by the provider and is
    /// intentionally left unused.
    /// </remarks>
    internal static class ServerCompatibilityEventIds
    {
        /// <summary>
        /// Logging category that preserves the legacy server EventSource identity.
        /// </summary>
        public const string CategoryName = "OPC-UA-Server";

        /// <summary>
        /// Legacy event identifier for a server service call.
        /// </summary>
        public const int ServerCall = 2;

        /// <summary>
        /// Legacy event identifier for a session state change.
        /// </summary>
        public const int SessionState = 3;

        /// <summary>
        /// Legacy event identifier for a monitored item becoming ready to publish.
        /// </summary>
        public const int MonitoredItemReady = 4;
    }
}
