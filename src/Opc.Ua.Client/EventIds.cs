/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
    /// Opc.Ua.Client assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location.
    /// </remarks>
    internal static class ClientEventIds
    {
        public const int Browser = 0;
        public const int ClassicSubscriptionEngine = 10;
        public const int ClientIdentityProviderExtensions = 50;
        public const int Client = 60;
        public const int ClientReplicaCoordinator = 70;
        public const int ConnectionStateMachine = 80;
        public const int ManagedSession = 110;
        public const int MessageProcessor = 150;
        public const int ModelChangeTracker = 170;
        public const int MonitoredItem = 180;
        public const int MonitoredItemManager = 200;
        public const int NodeCache = 220;
        public const int NodeCacheResolver = 230;
        public const int ReverseConnectManager = 240;
        public const int Session = 260;
        public const int SessionReconnectHandler = 340;
        public const int Subscription = 360;
        public const int SubscriptionManager = 440;
        public const int WebApiTransportChannel = 480;
        public const int WebApiWssTransportChannel = 490;
    }
}
