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
                    case Profiles.WsHttpXmlOrBinaryTransport:
                    case Profiles.HttpsXmlOrBinaryTransport:
                    {
                        return BinaryEncodingSupport.Optional;
                    }

                    case Profiles.HttpsBinaryTransport:
                    {
                        return BinaryEncodingSupport.Required;
                    }

                    case Profiles.WsHttpXmlTransport:
                    case Profiles.HttpsXmlTransport:
                    {
                        return BinaryEncodingSupport.None;
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
