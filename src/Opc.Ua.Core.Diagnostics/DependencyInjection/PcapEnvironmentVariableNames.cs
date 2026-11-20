/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Pcap.DependencyInjection
{
    /// <summary>
    /// Names of the environment variables the
    /// <c>AddPcapFromEnvironment</c> registration consults
    /// at host start time. Exposed as constants so operators and tests
    /// can reference the canonical spelling without hard-coding it.
    /// </summary>
    /// <remarks>
    /// All variables are read once when the host starts; changing them
    /// later in the process lifetime has no effect (matches the existing
    /// <c>OPCUA_PCAP_ENABLE_DIAGNOSTICS</c> opt-in semantics).
    /// </remarks>
    public static class PcapEnvironmentVariableNames
    {
        /// <summary>
        /// Path of the pcap file that the env-var driven registration
        /// writes captured frames to. When set, an in-process capture
        /// session is auto-started on host start. The literal path is
        /// honored; relative paths resolve against the current working
        /// directory at host-start time.
        /// </summary>
        public const string OpcuaPcapFile = "OPCUA_PCAP_FILE";

        /// <summary>
        /// Path of the keylog file that the env-var driven registration
        /// writes OPC UA channel symmetric key material to. When set
        /// without <see cref="OpcuaPcapFile"/>, a stand-alone keylog
        /// observer is installed (SSLKEYLOGFILE-style) without starting
        /// a packet capture. When both variables are set, the keylog
        /// is written to this path inside the auto-started session.
        /// File extension <c>.txt</c> selects the NSS-style text
        /// format; everything else (including <c>.json</c> and
        /// <c>.uakeys.json</c>) selects JSON-lines.
        /// </summary>
        public const string OpcuaKeyLogFile = "OPCUA_KEYLOGFILE";
    }
}
