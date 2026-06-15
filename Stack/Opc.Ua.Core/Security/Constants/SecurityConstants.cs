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
        /// The HTTPS OpenAPI binding does not use UA Secure Conversation and is
        /// therefore restricted to <see cref="MessageSecurityMode.None"/>; transport
        /// security is provided exclusively by TLS at the HTTPS layer. Maps to OPC
        /// Foundation <a href="https://profiles.opcfoundation.org/profile/2338">profile/2338</a>.
        /// </remarks>
        public const string HttpsOpenApiTransport
            = "http://opcfoundation.org/UA-Profile/Transport/https-uajson-openapi";

        /// <summary>
        /// Communicates with the OPC UA service set as a REST API over a
        /// secure WebSocket sub-protocol (<c>opcua+openapi</c>; OPC UA
        /// Part 6 §7.5.2). Same on-wire OpenAPI envelope as
        /// <see cref="HttpsOpenApiTransport"/> but multiplexed over a
        /// WebSocket text-frame transport for bidirectional, lower-latency
        /// scenarios (e.g. long-running notification streams). Maps to OPC
        /// Foundation <a href="https://profiles.opcfoundation.org/profile/2339">profile/2339</a>.
        /// </summary>
        public const string WssOpenApiTransport
            = "http://opcfoundation.org/UA-Profile/Transport/wss-uajson-openapi";

        /// <summary>
        /// Renamed to <see cref="HttpsOpenApiTransport"/> for alignment with the
        /// OPC UA spec profile name (Part 6 §G.3 / profile/2338). The .NET
        /// binding surface (<c>WebApiClient</c>, <c>WebApiServer</c>, etc.)
        /// keeps the <c>WebApi*</c> prefix to match the OPC Foundation
        /// <c>UA-WebApi-StarterKit</c> reference. This obsolete alias maps
        /// to the same URI so consumers compiled against it continue to
        /// work.
        /// </summary>
        [Obsolete("Renamed to HttpsOpenApiTransport. The .NET binding surface (WebApiClient, " +
            "WebApiServer, WebApiBodyCodec, …) keeps the WebApi* prefix; only the OPC UA " +
            "Profiles constants align with the spec OpenAPI naming.")]
        public const string HttpsWebApiTransport = HttpsOpenApiTransport;

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
        /// WebSocket sub-protocol identifier (<c>Sec-WebSocket-Protocol</c>) for the
        /// OPC UA OpenAPI mapping (Part 6 §G.3) carried over secure WebSockets
        /// (Part 6 §7.5.2). The wire format is the standard
        /// <c>{TypeId, Body}</c> OPC UA JSON envelope per WebSocket text frame;
        /// the OpenAPI sub-protocol is distinguished from
        /// <see cref="OpcUaWsSubProtocolUaJson"/> by the
        /// <see cref="Profiles.WssOpenApiTransport"/> profile URI
        /// advertised on discovery and by the server-side request handler
        /// (which routes via <c>WebApiServiceRoutes</c> and uses the
        /// <c>WebApiBodyCodec</c> compact / verbose encoder options).
        /// </summary>
        public const string OpcUaWsSubProtocolOpenApi = "opcua+openapi";

        /// <summary>
        /// Prefix for the bearer-token variant of the OPC UA OpenAPI
        /// WebSocket sub-protocol (Part 6 §7.5.2). The full sub-protocol
        /// name is <c>opcua+openapi+&lt;accesstoken&gt;</c>; the trailing
        /// token segment carries the bearer credential because browser
        /// WebSocket APIs do not accept custom HTTP request headers (no
        /// <c>Authorization: Bearer</c>). The server-side handler extracts
        /// the token from the sub-protocol name and feeds it through the
        /// standard <c>ISessionlessIdentityProvider</c> pipeline.
        /// </summary>
        public const string OpcUaWsSubProtocolOpenApiBearerPrefix = "opcua+openapi+";

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
        /// HTTPS OpenAPI transport profile (<see cref="HttpsOpenApiTransport"/>;
        /// OPC Foundation profile/2338).
        /// </summary>
        /// <param name="transportProfileUri">The transport profile URI to test.</param>
        public static bool IsHttpsOpenApi(string? transportProfileUri)
        {
            return string.Equals(transportProfileUri, HttpsOpenApiTransport, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="transportProfileUri"/> identifies the
        /// WSS OpenAPI transport profile (<see cref="WssOpenApiTransport"/>;
        /// OPC Foundation profile/2339).
        /// </summary>
        /// <param name="transportProfileUri">The transport profile URI to test.</param>
        public static bool IsWssOpenApi(string? transportProfileUri)
        {
            return string.Equals(transportProfileUri, WssOpenApiTransport, StringComparison.Ordinal);
        }

        /// <summary>
        /// Renamed to <see cref="IsHttpsOpenApi(string)"/>.
        /// </summary>
        /// <param name="transportProfileUri">The transport profile URI to test.</param>
        [Obsolete("Renamed to IsHttpsOpenApi.")]
        public static bool IsHttpsWebApi(string? transportProfileUri)
        {
            return IsHttpsOpenApi(transportProfileUri);
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
