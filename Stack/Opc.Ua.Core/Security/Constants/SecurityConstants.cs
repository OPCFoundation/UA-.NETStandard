/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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
        public const string HmacSha1 = "HmacSha1Signature";

        /// <summary>
        /// The HMAC-SHA256 algorithm used to create symmetric key signatures.
        /// </summary>
        public const string HmacSha256 = "HmacSha256Signature";

        /// <summary>
        /// The RSA-SHA1 algorithm used to create asymmetric key signatures.
        /// </summary>
        public const string RsaSha1 = "http://www.w3.org/2000/09/xmldsig#rsa-sha1";

        /// <summary>
        /// The RSA-SHA256 algorithm used to create asymmetric key signatures.
        /// </summary>
        public const string RsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
        
        /// <summary>
        /// The SHA1 algorithm used to create message digests.
        /// </summary>
        public const string Sha1 = "Sha1Digest";
        
        /// <summary>
        /// The SHA256 algorithm used to create message digests.
        /// </summary>
        public const string Sha256 = "Sha256Digest";
        
        /// <summary>
        /// The SHA512 algorithm used to create message digests.
        /// </summary>
        public const string Sha512 = "Sha512Digest";
        
        /// <summary>
        /// The AES128 algorithm used to encrypt data.
        /// </summary>
        public const string Aes128 = "Aes128Encryption";
        
        /// <summary>
        /// The AES192 algorithm used to encrypt data.
        /// </summary>
        public const string Aes192 = "Aes192Encryption";
        
        /// <summary>
        /// The AES256 algorithm used to encrypt data.
        /// </summary>
        public const string Aes256 = "Aes256Encryption";
        
        /// <summary>
        /// The AES128 algorithm used to encrypt keys.
        /// </summary>        
        public const string KwAes128 = "Aes128KeyWrap";
        
        /// <summary>
        /// The AES192 algorithm used to encrypt keys.
        /// </summary>        
        public const string KwAes192 = "Aes192KeyWrap";
        
        /// <summary>
        /// The AES256 algorithm used to encrypt keys.
        /// </summary>        
        public const string KwAes256 = "Aes256KeyWrap";
        
        /// <summary>
        /// The RSA-OAEP algorithm used to encrypt data.
        /// </summary>        
        public const string RsaOaep = "http://www.w3.org/2001/04/xmlenc#rsa-oaep";
        
        /// <summary>
        /// The RSA-PKCSv1.5 algorithm used to encrypt data.
        /// </summary>        
        public const string Rsa15 = "http://www.w3.org/2001/04/xmlenc#rsa-1_5";        
        
        /// <summary>
        /// The RSA-OAEP algorithm used to encrypt keys.
        /// </summary>        
        public const string KwRsaOaep = "RsaOaepKeyWrap";
        
        /// <summary>
        /// The RSA-PKCSv1.5 algorithm used to encrypt keys.
        /// </summary>        
        public const string KwRsa15 = "RsaV15KeyWrap";

        /// <summary>
        /// The P-SHA1 algorithm used to generate keys.
        /// </summary>
        public const string PSha1 = "Psha1KeyDerivation";
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
        /// Communicates with UA Binary over HTTPS.
        /// </summary>
        public const string HttpsBinaryTransport = "http://opcfoundation.org/UA-Profile/Transport/https-uabinary";
        
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
