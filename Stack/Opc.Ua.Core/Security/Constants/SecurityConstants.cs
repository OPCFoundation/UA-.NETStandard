/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
        public const string RsaOaepSha256 = "http://opcfoundation.org/UA/security/rsa-oaep-sha2-256";

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
        public const string PSha1 = "http://docs.oasis-open.org/ws-sx/ws-secureconversation/200512/dk/p_sha1";

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
        public const string UaTcpTransport = "http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary";

        /// <summary>
        /// Communicates with UA TCP over secure Websockets, UA Security and UA Binary.
        /// </summary>
        public const string UaWssTransport = "http://opcfoundation.org/UA-Profile/Transport/uawss-uasc-uabinary";

        /// <summary>
        /// Communicates with UA Binary over HTTPS.
        /// </summary>
        public const string HttpsBinaryTransport = "http://opcfoundation.org/UA-Profile/Transport/https-uabinary";

        /// <summary>
        /// Communicates with PubSub for UADP transport protocol.
        /// </summary>
        public const string UadpTransport = "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

        /// <summary>
        /// An Issued User Token that complies with the JWT specification.
        /// </summary>
        public const string JwtUserToken = "http://opcfoundation.org/UA/UserToken#JWT";

        /// <summary>
        /// Converts the URI to a URI that can be used for comparison.
        /// </summary>
        /// <param name="profileUri">The profile URI.</param>
        /// <returns>The normalixed URI.</returns>
        public static string NormalizeUri(string profileUri)
        {
            if (System.String.IsNullOrEmpty(profileUri))
            {
                return profileUri;
            }

            return profileUri;
        }
    }
}
