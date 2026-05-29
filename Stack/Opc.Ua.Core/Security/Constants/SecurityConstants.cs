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
