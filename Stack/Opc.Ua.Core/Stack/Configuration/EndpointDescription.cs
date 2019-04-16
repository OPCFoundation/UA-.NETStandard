/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.ServiceModel.Channels;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// Describes how to connect to an endpoint.
    /// </summary>
    public partial class EndpointDescription
    {
        #region Constructors
        /// <summary>
        /// Creates an endpoint configuration from a url.
        /// </summary>
        public EndpointDescription(string url)
        {
            Initialize();
            
            UriBuilder parsedUrl = new UriBuilder(url);

            if (parsedUrl.Scheme != Utils.UriSchemeOpcTcp)
            {
                if (!parsedUrl.Path.EndsWith("/discovery"))
                {
                    parsedUrl.Path += "/discovery";
                }
            }

            Server.DiscoveryUrls.Add(parsedUrl.ToString());

            EndpointUrl            = url;
            Server.ApplicationUri  = url;
            Server.ApplicationName = url;
            SecurityMode           = MessageSecurityMode.None;
            SecurityPolicyUri      = SecurityPolicies.None;
        }
        #endregion
        
        #region Public Properties
        /// <summary>
        /// The encodings supported by the configuration.
        /// </summary>
        public BinaryEncodingSupport EncodingSupport
        {
            get
            {
                if (!String.IsNullOrEmpty(EndpointUrl) && EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp))
                {
                    return BinaryEncodingSupport.Required;
                }

                TransportProfileUri = Profiles.NormalizeUri(TransportProfileUri);

                switch (TransportProfileUri)
                {
                    case Profiles.HttpsBinaryTransport:
                    {
                        return BinaryEncodingSupport.Required;
                    }
                }
    
                return BinaryEncodingSupport.None;
            }
        }

        /// <summary>
        /// The proxy url to use when connecting to the endpoint.
        /// </summary>
        public Uri ProxyUrl
        {
            get { return m_proxyUrl;  }
            set { m_proxyUrl = value; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Finds the user token policy with the specified id.
        /// </summary>
        public UserTokenPolicy FindUserTokenPolicy(string policyId)
        {
            foreach (UserTokenPolicy policy in m_userIdentityTokens)
            {
                if (policy.PolicyId == policyId)
                {
                    return policy;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a token policy that matches the user identity specified.
        /// </summary>
        public UserTokenPolicy FindUserTokenPolicy(UserTokenType tokenType, XmlQualifiedName issuedTokenType)
        {
            if (issuedTokenType == null)
            {
                return FindUserTokenPolicy(tokenType, (string)null);
            }

            return FindUserTokenPolicy(tokenType, issuedTokenType.Namespace);
        }

        /// <summary>
        /// Finds a token policy that matches the user identity specified.
        /// </summary>
        public UserTokenPolicy FindUserTokenPolicy(UserTokenType tokenType, string issuedTokenType)
        {
            // construct issuer type.
            string issuedTokenTypeText = issuedTokenType;
            
            // find matching policy.
            foreach (UserTokenPolicy policy in m_userIdentityTokens)
            {
                // check token type.
                if (tokenType != policy.TokenType)
                {
                    continue;
                }

                // check issuer token type.
                if (issuedTokenTypeText != policy.IssuedTokenType)
                {
                    continue;
                }

                return policy;
            }

            // no policy found
            return null;
        }
        #endregion
        
        #region Private Fields
        private Uri m_proxyUrl;
        #endregion
    }
}
