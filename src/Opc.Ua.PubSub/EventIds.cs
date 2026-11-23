// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// Opc.Ua.PubSub assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location. The
    /// class name is prefixed with the assembly token to avoid CS0436 collisions with the
    /// event-id classes of other assemblies exposed through <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal static class PubSubEventIds
    {
        public const int DataSetMetaDataRegistry = 0;
        public const int DataSetReader = 10;
        public const int DataSetReaderTimeoutWatcher = 20;
        public const int InMemoryPubSubKeyServiceServer = 30;
        public const int LeaseActivationCoordinator = 40;
        public const int MetaDataPublisher = 50;
        public const int OpcUaSecurityKeyServiceClient = 60;
        public const int PubSubApplication = 70;
        public const int PubSubApplicationHostedService = 90;
        public const int PubSubConnection = 100;
        public const int PubSubScheduler = 130;
        public const int PubSubSecurityKeyProviderStarter = 140;
        public const int PubSubSecurityWrapperResolver = 150;
        public const int PubSubStateMachine = 160;
        public const int PubSubTranscoder = 170;
        public const int PubSubTranscodingBridge = 180;
        public const int PubSubTranscodingReloadCoordinator = 190;
        public const int PullSecurityKeyProvider = 200;
        public const int PushSecurityKeyProvider = 210;
        public const int ReaderGroup = 220;
        public const int Security = 230;
        public const int SksMethodHandler = 240;
        public const int UadpSecurityWrapper = 250;
        public const int WriterGroup = 260;
        public const int XmlPubSubConfigurationStore = 270;
    }
}
