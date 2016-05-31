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

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
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
            m_tokenType = UserTokenType.Anonymous;
            m_displayName = "Anonymous";
        }

        /// <summary>
        /// Initializes the object with a username and password.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="password">The password.</param>
        public UserIdentity(string username, string password)
        {
            // TODO: We could add Microsoft Live ID account identity here (which is the
            // supported user identity in Universal Windows Platform apps), but I'm not
            // sure how useful this would be in industrial contexts.
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

        #region IUserIdentity Methods
        /// <summary>
        /// Gets or sets the UserIdentityToken PolicyId associated with the UserIdentity.
        /// </summary>
        /// <remarks>
        /// This value is used to initialize the UserIdentityToken object when GetIdentityToken() is called.
        /// </remarks>
        public string PolicyId
        {
            get { return m_policyId; }
            set { m_policyId = value; }
        }
        #endregion

        #region IUserIdentity Methods
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

        /// <summary cref="IUserIdentity.GetIdentityToken" />
        public UserIdentityToken GetIdentityToken()
        {
            AnonymousIdentityToken token = new AnonymousIdentityToken();
            token.PolicyId = m_policyId;
            return token;
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Initializes the object with a UA identity token
        /// </summary>
        private void Initialize(UserIdentityToken token)
        {
            if (token == null) throw new ArgumentNullException("token");

            m_policyId = token.PolicyId;
  
            AnonymousIdentityToken anonymousToken = token as AnonymousIdentityToken;
            if (anonymousToken != null)
            {
                m_tokenType = UserTokenType.Anonymous;
                m_issuedTokenType = null;
                m_displayName = "Anonymous";
                return;
            }        
  
            throw new ArgumentException("Unrecognized UA user identity token type.", "token");
        }
        #endregion
        
        #region Private Fields
        private string m_displayName;
        private UserTokenType m_tokenType;
        private XmlQualifiedName m_issuedTokenType;
        private string m_policyId;
        #endregion
    }

    #region ImpersonationContext Class
    /// <summary>
    /// Stores information about the user that is currently being impersonated.
    /// </summary>
    public class ImpersonationContext : IDisposable
    {
        #region Public Members
        /// <summary>
        /// The security principal being impersonated.
        /// </summary>
        public IPrincipal Principal { get; set; }
        #endregion

        #region Internal Members
        internal IntPtr Handle { get; set; }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~ImpersonationContext()
        {
            Dispose(false);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (Handle != IntPtr.Zero)
            {
                Handle = IntPtr.Zero;
            }
        }
        #endregion
    }
    #endregion
}
