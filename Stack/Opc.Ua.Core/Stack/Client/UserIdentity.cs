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

using System;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// A generic user identity class.
    /// </summary>
    public class UserIdentity : IUserIdentity
    {
        #region Constructors
        /// <summary>
        /// Initializes the object as an anonymous user.
        /// </summary>
        public UserIdentity()
        {
            AnonymousIdentityToken token = new AnonymousIdentityToken();
            Initialize(token);
        }

        /// <summary>
        /// Initializes the object with a username and password.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="password">The password.</param>
        public UserIdentity(string username, string password)
        {
            UserNameIdentityToken token = new UserNameIdentityToken();
            token.UserName = username;
            token.DecryptedPassword = password;
            Initialize(token);
        }

        /// <summary>
        /// Initializes the object with a UA identity token.
        /// </summary>
        /// <param name="issuedToken">The token.</param>
        public UserIdentity(IssuedIdentityToken issuedToken)
        {
            Initialize(issuedToken);
        }

        /// <summary>
        /// Initializes the object with an X509 certificate identifier
        /// </summary>
        public UserIdentity(CertificateIdentifier certificateId)
        {
            if (certificateId == null) throw new ArgumentNullException(nameof(certificateId));

            X509Certificate2 certificate = certificateId.Find().Result;
            if (certificate != null)
            {
                Initialize(certificate);
            }
        }

        /// <summary>
        /// Initializes the object with an X509 certificate
        /// </summary>
        public UserIdentity(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            Initialize(certificate);
        }

        /// <summary>
        /// Initializes the object with a UA identity token.
        /// </summary>
        /// <param name="token">The user identity token.</param>
        public UserIdentity(UserIdentityToken token)
        {
            Initialize(token);
        }
        #endregion

        #region IUserIdentity Members
        /// <summary>
        /// Gets or sets the UserIdentityToken PolicyId associated with the UserIdentity.
        /// </summary>
        /// <remarks>
        /// This value is used to initialize the UserIdentityToken object when GetIdentityToken() is called.
        /// </remarks>
        public string PolicyId
        {
            get { return m_token.PolicyId; }
            set { m_token.PolicyId = value; }
        }

        /// <summary cref="IUserIdentity.DisplayName" />
        public string DisplayName
        {
            get { return m_displayName; }
        }

        /// <summary cref="IUserIdentity.TokenType" />
        public UserTokenType TokenType
        {
            get { return m_tokenType; }
        }

        /// <summary cref="IUserIdentity.IssuedTokenType" />
        public XmlQualifiedName IssuedTokenType
        {
            get { return m_issuedTokenType; }
        }

        /// <summary cref="IUserIdentity.SupportsSignatures" />
        public bool SupportsSignatures
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        ///  Get or sets the list of granted role ids associated to the UserIdentity.
        /// </summary>
        public NodeIdCollection GrantedRoleIds
        {
            get { return m_grantedRoleIds; }
            set { m_grantedRoleIds = value; }
        }

        /// <summary cref="IUserIdentity.GetIdentityToken" />
        public UserIdentityToken GetIdentityToken()
        {
            // check for null and return anonymous.
            if (m_token == null)
            {
                return new AnonymousIdentityToken();
            }
            else
            {
                return m_token;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Initializes the object with a UA identity token
        /// </summary>
        private void Initialize(UserIdentityToken token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            m_grantedRoleIds = new NodeIdCollection();
            m_token = token;

            UserNameIdentityToken usernameToken = token as UserNameIdentityToken;
            if (usernameToken != null)
            {
                m_tokenType = UserTokenType.UserName;
                m_issuedTokenType = null;
                m_displayName = usernameToken.UserName;
                return;
            }

            X509IdentityToken x509Token = token as X509IdentityToken;
            if (x509Token != null)
            {
                m_tokenType = UserTokenType.Certificate;
                m_issuedTokenType = null;
                if (x509Token.Certificate != null)
                {
                    m_displayName = x509Token.Certificate.Subject;
                }
                else
                {
                    X509Certificate2 cert = CertificateFactory.Create(x509Token.CertificateData, true);
                    m_displayName = cert.Subject;
                }
                return;
            }

            IssuedIdentityToken issuedToken = token as IssuedIdentityToken;
            if (issuedToken != null)
            {
                if (issuedToken.IssuedTokenType == Ua.IssuedTokenType.JWT)
                {
                    if (issuedToken.DecryptedTokenData == null || issuedToken.DecryptedTokenData.Length == 0)
                    {
                        throw new ArgumentException("JSON Web Token has no data associated with it.", nameof(token));
                    }

                    m_tokenType = UserTokenType.IssuedToken;
                    m_issuedTokenType = new XmlQualifiedName("", Opc.Ua.Profiles.JwtUserToken);
                    m_displayName = "JWT";
                    return;
                }
                else
                {
                    throw new NotSupportedException("Only JWT Issued Tokens are supported!");
                }
            }

            AnonymousIdentityToken anonymousToken = token as AnonymousIdentityToken;
            if (anonymousToken != null)
            {
                m_tokenType = UserTokenType.Anonymous;
                m_issuedTokenType = null;
                m_displayName = "Anonymous";
                return;
            }

            throw new ArgumentException("Unrecognized UA user identity token type.", nameof(token));
        }

        /// <summary>
        /// Initializes the object with an X509 certificate
        /// </summary>
        private void Initialize(X509Certificate2 certificate)
        {
            X509IdentityToken token = new X509IdentityToken();
            token.CertificateData = certificate.RawData;
            token.Certificate = certificate;
            Initialize(token);
        }
        #endregion

        #region Private Fields
        private UserIdentityToken m_token;
        private string m_displayName;
        private UserTokenType m_tokenType;
        private XmlQualifiedName m_issuedTokenType;
        private NodeIdCollection m_grantedRoleIds;
        #endregion
    }

    #region ImpersonationContext Class
    /// <summary>
    /// Stores information about the user that is currently being impersonated.
    /// </summary>
    public class ImpersonationContext
    {
    }
    #endregion
}
