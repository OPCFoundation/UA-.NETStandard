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
    /// Opc.Ua.Core assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location.
    /// </remarks>
    internal static class CoreEventIds
    {
        // Compatibility events retain their former EventSource ids. They are scoped by
        // their dedicated ILogger categories and intentionally overlap the per-class ids below.
        public const string CoreCompatibilityCategory = "OPC-UA-Core";
        public const int CoreServiceCallStart = 10;
        public const int CoreServiceCallStop = 11;
        public const int CoreServiceCallBadStop = 12;
        public const int CoreSendResponse = 14;

        public const string ChannelManagerCompatibilityCategory = "Opc.Ua.ChannelManager";
        public const int ChannelManagerChannelOpened = 1;
        public const int ChannelManagerChannelClosed = 2;
        public const int ChannelManagerStateChanged = 3;
        public const int ChannelManagerReconnectStarted = 4;
        public const int ChannelManagerReconnectCompleted = 5;
        public const int ChannelManagerReconnectFailed = 6;
        public const int ChannelManagerParticipantAttached = 7;
        public const int ChannelManagerParticipantDetached = 8;

        public const int ApplicationConfiguration = 0;
        public const int AsyncResultBase = 10;
        public const int Audit = 20;
        // Buffer-manager logging: #3994 split the former monolithic BufferManager
        // into pluggable managers, so the former BufferManager offset block is
        // reused here for the logging of the tracing and array-pool implementations.
        public const int ArrayPoolBufferManagerBase = 30;
        public const int TracingBufferManager = 40;
        public const int CertificateLifecycleMonitor = 50;
        public const int CertificateManager = 60;
        public const int CertificateTrustList = 70;
        public const int CertificateValidationCore = 80;
        public const int ChannelAsyncOperation = 110;
        public const int ChannelBaseObsolete = 120;
        public const int ChannelEntry = 130;
        public const int ClientBase = 150;
        public const int ClientChannelManagerCertRotation = 160;
        public const int ConfigurationWatcher = 170;
        public const int ConfiguredEndpoints = 180;
        public const int DirectoryCertificateStore = 190;
        public const int DiscoveryClient = 220;
        public const int EndpointBase = 230;
        public const int HttpsTransportChannel = 240;
        public const int RejectedCertificateProcessor = 260;
        public const int ReverseConnectHost = 270;
        public const int RsaUtils = 280;
        public const int SecuredApplicationHelpers = 290;
        public const int SecurityConfiguration = 300;
        public const int ServerBase = 310;
        public const int SharedKeyValueCertificateStore = 330;
        public const int SharedStoreLeaseElection = 340;
        public const int TcpByteTransport = 350;
        public const int TcpListenerChannel = 360;
        public const int TcpReverseConnectChannel = 380;
        public const int TcpServerChannel = 390;
        public const int TcpServiceHost = 420;
        public const int TcpTransportListener = 430;
        public const int UaSCBinaryChannel = 470;
        public const int UaSCBinaryClientChannel = 490;
        public const int UaSCBinaryTransportChannel = 540;
        public const int UtilsObsolete = 560;
        public const int X509CertificateStore = 570;
        public const int X509CrlHelper = 580;
    }
}
