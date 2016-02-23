/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

namespace Opc.Ua
{    
    /// <summary>
    /// Constants that identify certificate store locations.
    /// </summary>
    public static class StoreLocations
    {
        /// <summary>
        /// The store assigned to the current user.
        /// </summary>
        public const string CurrentUser = "CurrentUser";

        /// <summary>
        /// The store assigned to the local machine.
        /// </summary>
        public const string LocalMachine = "LocalMachine";
    }
        
    /// <summary>
    /// Constants that identify certificate store names.
    /// </summary>
    public static class StoreNames
    {
        /// <summary>
        /// The store used for personal certificates.
        /// </summary>
        public const string Personal = "My";

        /// <summary>
        /// The store used for UA application certificates.
        /// </summary>
        public const string Applications = "UA Applications";

        /// <summary>
        /// The store used for UA certificate authorities certificates.
        /// </summary>
        public const string CertificateAuthorities = "UA Certificate Authorities";

        /// <summary>
        /// The store used for trusted root certificate authorities.
        /// </summary>
        public const string Root = "Root";
    }

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
        public const string RsaSha256 = "RsaSha256Signature";
        
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
        internal const string UaTcpTransport_DoesNotMatchSpec = "http://opcfoundation.org/UA/profiles/transport/uatcp";
        
        /// <summary>
        /// Communicates with SOAP 1.2, WS Security and UA XML.
        /// </summary>
        internal const string WsHttpXmlTransport_DoesNotMatchSpec = "http://opcfoundation.org/UA/profiles/transport/wsxml";

        /// <summary>
        /// Communicates with SOAP 1.2, WS Security and UA XML or UA Binary.
        /// </summary>
        internal const string WsHttpXmlOrBinaryTransport_DoesNotMatchSpec = "http://opcfoundation.org/UA/profiles/transport/wsxmlorbinary";
        
        /// <summary>
        /// Communicates with SOAP 1.2, WS Security and UA Binary.
        /// </summary>
        internal const string WsHttpBinaryTransport_DoesNotMatchSpec = "http://opcfoundation.org/UA/profiles/transport/wsbinary";

        /// <summary>
        /// Communicates with UA TCP, UA Security and UA Binary.
        /// </summary>
        public const string UaTcpTransport = "http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary";
        
        /// <summary>
        /// Communicates with SOAP 1.2, WS Security and UA XML.
        /// </summary>
        public const string WsHttpXmlTransport = "http://opcfoundation.org/UA-Profile/Transport/soaphttp-wssc-uaxml";

        /// <summary>
        /// Communicates with SOAP 1.2, WS Security and UA XML or UA Binary.
        /// </summary>
        public const string WsHttpXmlOrBinaryTransport = "http://opcfoundation.org/UA-Profile/Transport/soaphttp-wssc-uaxml-uabinary";

        /// <summary>
        /// Communicates with UA XML or UA Binary over HTTPS.
        /// </summary>
        public const string HttpsXmlOrBinaryTransport = "http://opcfoundation.org/UA-Profile/Transport/https-uasoapxml-uabinary";

        /// <summary>
        /// Communicates with UA XML over HTTPS.
        /// </summary>
        public const string HttpsXmlTransport = "http://opcfoundation.org/UA-Profile/Transport/https-uasoapxml";

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

            switch (profileUri)
            {
                case Profiles.UaTcpTransport_DoesNotMatchSpec: { return Profiles.UaTcpTransport; }
                case Profiles.WsHttpXmlOrBinaryTransport_DoesNotMatchSpec: { return Profiles.WsHttpXmlOrBinaryTransport; }
                case Profiles.WsHttpXmlTransport_DoesNotMatchSpec: { return Profiles.WsHttpXmlTransport; }
            }

            return profileUri;
        }
    }
}
