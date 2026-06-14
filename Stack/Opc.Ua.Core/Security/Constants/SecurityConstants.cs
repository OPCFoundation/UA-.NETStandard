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

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Defines constants for key security policies.
    /// </summary>
    public static class SecurityAlgorithms
    {
        /// <summary>
        /// The HMAC-SHA1 algorithm used to create symmetric key signatures.
        /// </summary>
        public const string HmacSha1 = "http://www.w3.org/2000/09/xmldsig#hmac-sha1";

        /// <summary>
        /// The HMAC-SHA256 algorithm used to create symmetric key signatures.
        /// </summary>
        public const string HmacSha256 = "http://www.w3.org/2000/09/xmldsig#hmac-sha256";

        /// <summary>
        /// The RSA-SHA1 algorithm used to create asymmetric key signatures.
        /// </summary>
        public const string RsaSha1 = "http://www.w3.org/2000/09/xmldsig#rsa-sha1";

        /// <summary>
        /// The RSA-SHA256 algorithm used to create asymmetric key signatures.
        /// </summary>
        public const string RsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

        /// <summary>
        /// The RSA-PSS-SHA256 algorithm used to create asymmetric key signatures.
        /// </summary>
        public const string RsaPssSha256 = "http://opcfoundation.org/UA/security/rsa-pss-sha2-256";

        /// <summary>
        /// The AES128 algorithm used to encrypt data.
        /// </summary>
        public const string Aes128 = "http://www.w3.org/2001/04/xmlenc#aes128-cbc";

        /// <summary>
        /// The AES256 algorithm used to encrypt data.
        /// </summary>
        public const string Aes256 = "http://www.w3.org/2001/04/xmlenc#aes256-cbc";

        /// <summary>
        /// The RSA-OAEP algorithm used to encrypt data.
        /// </summary>
        public const string RsaOaep = "http://www.w3.org/2001/04/xmlenc#rsa-oaep";

        /// <summary>
        /// The RSA-OAEP-SHA256 algorithm used to encrypt data.
        /// </summary>
        public const string RsaOaepSha256
            = "http://opcfoundation.org/UA/security/rsa-oaep-sha2-256";

        /// <summary>
        /// The RSA-PKCSv1.5 algorithm used to encrypt data.
        /// </summary>
        public const string Rsa15 = "http://www.w3.org/2001/04/xmlenc#rsa-1_5";

        /// <summary>
        /// The RSA-PKCSv1.5 SHA256 algorithm used to encrypt data.
        /// </summary>
        public const string Rsa15Sha256 = "http://www.w3.org/2001/04/xmlenc#rsa-1_5-sha2-256";

        /// <summary>
        /// The RSA-OAEP algorithm used to encrypt keys.
        /// </summary>
        public const string KwRsaOaep = "http://www.w3.org/2001/04/xmlenc#rsa-oaep-mgf1p";

        /// <summary>
        /// The RSA-PKCSv1.5 algorithm used to encrypt keys.
        /// </summary>
        public const string KwRsa15 = "http://www.w3.org/2001/04/xmlenc#rsa-1_5";

        /// <summary>
        /// The P-SHA1 algorithm used to generate keys.
        /// </summary>
        public const string PSha1
            = "http://docs.oasis-open.org/ws-sx/ws-secureconversation/200512/dk/p_sha1";

        /// <summary>
        /// The P-SHA256 algorithm used to generate keys.
        /// </summary>
        public const string PSha256 = "http://opcfoundation.org/ua/security/p_sha2-256";
    }

    /// <summary>
    /// Common profiles that UA applications may support.
    /// </summary>
    public static class Profiles
    {
        /// <summary>
        /// Communicates with UA TCP, UA Security and UA Binary.
        /// </summary>
        public const string UaTcpTransport
            = "http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary";

        /// <summary>
        /// Communicates with UA TCP over secure Websockets, UA Security and UA Binary.
        /// </summary>
        public const string UaWssTransport
            = "http://opcfoundation.org/UA-Profile/Transport/uawss-uasc-uabinary";

        /// <summary>
        /// Communicates with UA JSON over secure Websockets.
        /// </summary>
        /// <remarks>
        /// Per OPC UA Part 6 §7.5.2 each WebSocket sub-protocol has its own
        /// TransportProfileUri defined in OPC 10000-7. The URI here follows the
        /// established naming pattern of <see cref="UaWssTransport"/> (and matches
        /// the WebSocket sub-protocol identifier <c>opcua+uajson</c>). The JSON
        /// sub-protocol does not use UA Secure Conversation and is therefore
        /// restricted to <see cref="MessageSecurityMode.None"/>.
        /// </remarks>
        public const string UaWssJsonTransport
            = "http://opcfoundation.org/UA-Profile/Transport/uawss-uajson";

        /// <summary>
        /// Communicates with UA Binary over HTTPS.
        /// </summary>
        public const string HttpsBinaryTransport
            = "http://opcfoundation.org/UA-Profile/Transport/https-uabinary";

        /// <summary>
        /// Communicates with UA JSON over HTTPS.
        /// </summary>
        public const string HttpsJsonTransport
            = "http://opcfoundation.org/UA-Profile/Transport/https-uajson";

        /// <summary>
        /// Communicates with the OPC UA service set as a REST API over HTTPS,
        /// following the OPC UA "OpenAPI Mapping" (OPC UA Part 6 §G.3, v1.05.07).
        /// </summary>
        /// <remarks>
        /// Each OPC UA service is exposed as a <c>POST /&lt;service&gt;</c> route
        /// whose request and response bodies are the corresponding
        /// <c>&lt;Service&gt;Request</c> / <c>&lt;Service&gt;Response</c> serialized
        /// with the OPC UA JSON encoding from Part 6 §5.4. Compact (mandatory per
        /// §5.4.9) and Verbose forms are negotiated through the
        /// <c>application/json; encoding=compact|verbose</c> media-type parameter.
        /// The HTTPS REST binding does not use UA Secure Conversation and is
        /// therefore restricted to <see cref="MessageSecurityMode.None"/>; transport
        /// security is provided exclusively by TLS at the HTTPS layer.
        /// </remarks>
        public const string HttpsWebApiTransport
            = "http://opcfoundation.org/UA-Profile/Transport/https-webapi";

        /// <summary>
        /// Uri for "PubSub UDP UADP" Profile.
        /// This PubSub transport Facet defines a combination of the UDP transport protocol mapping with UADP message mapping
        /// </summary>
        public const string PubSubUdpUadpTransport
            = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

        /// <summary>
        /// Uri for "PubSub MQTT UADP" Profile.
        /// This PubSub transport Facet defines a combination of the MQTT transport protocol mapping with UADP message mapping.
        /// This Facet is used for broker-based messaging.
        /// </summary>
        public const string PubSubMqttUadpTransport
            = "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-uadp";

        /// <summary>
        /// Uri for "PubSub MQTT JSON" Profile.
        /// This PubSub transport Facet defines a combination of the MQTT transport protocol mapping with JSON message mapping.
        /// This Facet is used for broker-based messaging.
        /// </summary>
        public const string PubSubMqttJsonTransport
            = "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json";

        /// <summary>
        /// An Issued User Token that complies with the JWT specification.
        /// </summary>
        public const string JwtUserToken = "http://opcfoundation.org/UA/UserToken#JWT";

        /// <summary>
        /// The security policy header used by the Https transport.
        /// </summary>
        public const string HttpsSecurityPolicyHeader = "OPCUA-SecurityPolicy";

        /// <summary>
        /// HTTP <c>Content-Type</c> value identifying an OPC UA binary-encoded message body
        /// for the https-uabinary transport (OPC UA Part 6 §7.4.4).
        /// </summary>
        public const string OpcUaBinaryContentType = "application/opcua+uabinary";

        /// <summary>
        /// HTTP <c>Content-Type</c> value identifying an OPC UA JSON-encoded message body
        /// for the https-uajson transport (OPC UA Part 6 §7.4.5).
        /// </summary>
        public const string OpcUaJsonContentType = "application/opcua+uajson";

        /// <summary>
        /// WebSocket sub-protocol identifier (<c>Sec-WebSocket-Protocol</c>) for the
        /// UA Connection Protocol over secure WebSockets carrying UA Binary
        /// SecureChannel chunks (OPC UA Part 6 §7.5.2). All MessageSecurityModes
        /// are supported under this sub-protocol.
        /// </summary>
        public const string OpcUaWsSubProtocolUacp = "opcua+uacp";

        /// <summary>
        /// WebSocket sub-protocol identifier (<c>Sec-WebSocket-Protocol</c>) for the
        /// UA JSON encoding over secure WebSockets (OPC UA Part 6 §7.5.2). This
        /// sub-protocol does not use UA Secure Conversation and is therefore
        /// restricted to <see cref="MessageSecurityMode.None"/>; transport
        /// security is provided exclusively by TLS at the WebSocket layer.
        /// </summary>
        public const string OpcUaWsSubProtocolUaJson = "opcua+uajson";

        /// <summary>
        /// Returns <c>true</c> if <paramref name="transportProfileUri"/> identifies the
        /// HTTPS binary transport profile (<see cref="HttpsBinaryTransport"/>).
        /// </summary>
        /// <param name="transportProfileUri">The transport profile URI to test.</param>
        public static bool IsHttpsBinary(string? transportProfileUri)
        {
            return string.Equals(transportProfileUri, HttpsBinaryTransport, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="transportProfileUri"/> identifies the
        /// HTTPS JSON transport profile (<see cref="HttpsJsonTransport"/>).
        /// </summary>
        /// <param name="transportProfileUri">The transport profile URI to test.</param>
        public static bool IsHttpsJson(string? transportProfileUri)
        {
            return string.Equals(transportProfileUri, HttpsJsonTransport, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="transportProfileUri"/> identifies the
        /// HTTPS REST API transport profile (<see cref="HttpsWebApiTransport"/>).
        /// </summary>
        /// <param name="transportProfileUri">The transport profile URI to test.</param>
        public static bool IsHttpsWebApi(string? transportProfileUri)
        {
            return string.Equals(transportProfileUri, HttpsWebApiTransport, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="transportProfileUri"/> identifies the
        /// WebSocket Secure UA Binary transport profile (<see cref="UaWssTransport"/>).
        /// </summary>
        /// <param name="transportProfileUri">The transport profile URI to test.</param>
        public static bool IsWssBinary(string? transportProfileUri)
        {
            return string.Equals(transportProfileUri, UaWssTransport, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="transportProfileUri"/> identifies the
        /// WebSocket Secure UA JSON transport profile (<see cref="UaWssJsonTransport"/>).
        /// </summary>
        /// <param name="transportProfileUri">The transport profile URI to test.</param>
        public static bool IsWssJson(string? transportProfileUri)
        {
            return string.Equals(transportProfileUri, UaWssJsonTransport, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns the WebSocket sub-protocol identifier (<c>Sec-WebSocket-Protocol</c>)
        /// that corresponds to the supplied WebSocket-based <paramref name="transportProfileUri"/>,
        /// or <c>null</c> if the URI is not a WebSocket profile.
        /// </summary>
        /// <param name="transportProfileUri">The transport profile URI.</param>
        public static string? ToWebSocketSubProtocol(string? transportProfileUri)
        {
            if (IsWssBinary(transportProfileUri))
            {
                return OpcUaWsSubProtocolUacp;
            }
            if (IsWssJson(transportProfileUri))
            {
                return OpcUaWsSubProtocolUaJson;
            }
            return null;
        }

        /// <summary>
        /// Converts the URI to a URI that can be used for comparison.
        /// </summary>
        /// <param name="profileUri">The profile URI.</param>
        /// <returns>The normalized URI.</returns>
        public static string NormalizeUri(string profileUri)
        {
            if (string.IsNullOrEmpty(profileUri))
            {
                return profileUri;
            }

            return profileUri;
        }
    }
}
