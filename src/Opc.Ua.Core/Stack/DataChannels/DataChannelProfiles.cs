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

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Provisional version-one carrier and payload identifiers, independently selected.
    /// </summary>
    public static class DataChannelProfiles
    {
        /// <summary>Dedicated FLO/UASC transport profile.</summary>
        public const string DedicatedUasc = "http://opcfoundation.org/UA-Profile/Transport/flow-uasc-v1";

        /// <summary>Shared FLO/UASC transport profile.</summary>
        public const string SharedUasc = "http://opcfoundation.org/UA-Profile/Transport/flow-shared-uasc-v1";

        /// <summary>Native QUIC transport profile.</summary>
        public const string Quic = "http://opcfoundation.org/UA-Profile/Transport/flow-quic-v1";

        /// <summary>Native WebTransport transport profile.</summary>
        public const string WebTransport = "http://opcfoundation.org/UA-Profile/Transport/flow-webtransport-v1";

        /// <summary>Explicit dedicated UASC endpoint resource.</summary>
        public const string DedicatedResource = "/flow/v1/dedicated";

        /// <summary>Explicit shared UASC endpoint resource.</summary>
        public const string SharedResource = "/flow/v1/shared";

        /// <summary>Complete Part 14 UADP NetworkMessage payloads.</summary>
        public const string UadpPayload = "urn:opcfoundation:ua:flow:payload:uadp:1";

        /// <summary>Complete Part 14 JSON NetworkMessage payloads.</summary>
        public const string JsonPayload = "urn:opcfoundation:ua:flow:payload:json:1";

        /// <summary>Complete application-defined opaque payloads.</summary>
        public const string OpaquePayload = "urn:opcfoundation:ua:flow:payload:opaque:1";

        /// <summary>The optional explicitly configured native Client/Server lane resource.</summary>
        public const string ClientServerResource = "urn:opcfoundation:ua:flow:resource:client-server:1";

        /// <summary>Native QUIC flow ALPN.</summary>
        public const string QuicAlpn = "opcua-flow/1";

        /// <summary>HTTP/3 ALPN; WebTransport additionally requires its specified CONNECT/settings.</summary>
        public const string WebTransportAlpn = "h3";
    }
}
