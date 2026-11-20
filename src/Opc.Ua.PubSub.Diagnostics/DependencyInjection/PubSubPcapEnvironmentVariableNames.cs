/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Pcap.DependencyInjection
{
    /// <summary>
    /// Names of the environment variables the
    /// <c>AddPubSubPcapFromEnvironment</c> registration consults at host
    /// start time. Exposed as constants so operators and tests can reference
    /// the canonical spelling without hard-coding it.
    /// </summary>
    /// <remarks>
    /// All variables are read once when the host starts; changing them later
    /// in the process lifetime has no effect.
    /// </remarks>
    public static class PubSubPcapEnvironmentVariableNames
    {
        /// <summary>
        /// Path of the pcap (or pcapng) file the env-var driven registration
        /// writes captured PubSub frames to on host shutdown. When set, an
        /// in-process PubSub capture session is auto-started on host start.
        /// A <c>.pcapng</c> extension selects the pcapng writer; anything
        /// else selects libpcap. Relative paths resolve against the current
        /// working directory at host-start time.
        /// </summary>
        public const string OpcuaPubSubPcapFile = "OPCUA_PUBSUB_PCAP_FILE";
    }
}
