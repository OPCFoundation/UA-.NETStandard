// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// Opc.Ua.Bindings.Https assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location. The
    /// class name is prefixed with the assembly token to avoid CS0436 collisions with the
    /// event-id classes of other assemblies exposed through <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal static class BindingsHttpsEventIds
    {
        public const int HttpsServiceHost = 0;
        public const int HttpsTransportListener = 10;
        public const int KestrelTcpConnectionHandler = 30;
        public const int KestrelTcpTransportListener = 40;
        public const int PipeByteTransport = 50;
        public const int WebApiEndpointDispatcher = 60;
        public const int WebSocketByteTransportBase = 70;
        public const int WssJsonTransportChannel = 80;
        public const int KestrelTcpChannelLifetime = 90;
        public const int WssChannelLifetime = 100;
    }
}
