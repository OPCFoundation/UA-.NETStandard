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

        /// <summary>
        /// Path of the key-log file the env-var driven registration writes
        /// captured PubSub security key material to, so encrypted UADP
        /// captures can be decrypted offline. Reserved for the key-capture
        /// path; honored together with
        /// <see cref="OpcuaPubSubPcapFile"/>.
        /// </summary>
        public const string OpcuaPubSubKeyLogFile = "OPCUA_PUBSUB_KEYLOGFILE";
    }
}
