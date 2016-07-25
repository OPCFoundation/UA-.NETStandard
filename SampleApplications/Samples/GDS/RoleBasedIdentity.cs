using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IdentityModel.Tokens;

namespace Opc.Ua.GdsServer
{
    public enum GdsRole
    {
        GdsAdmin,
        ApplicationAdmin,
        ApplicationUser
    }

    public class RoleBasedIdentity : IUserIdentity
    {
        private IUserIdentity m_identity;
        private GdsRole m_role;

        public RoleBasedIdentity(IUserIdentity identity, GdsRole role)
        {
            m_identity = identity;
            m_role = role;
        }

        public GdsRole Role
        {
            get { return m_role; }
        }

        public string DisplayName
        {
            get { return m_identity.DisplayName; }
        }

        /// <summary>
        /// The user token policy.
        /// </summary>
        /// <value>The user token policy.</value>
        public string PolicyId
        {
            get { return m_identity.PolicyId; }
        }

        public UserTokenType TokenType
        {
            get { return m_identity.TokenType; }
        }

        public XmlQualifiedName IssuedTokenType
        {
            get { return m_identity.IssuedTokenType; }
        }

        public bool SupportsSignatures
        {
            get { return m_identity.SupportsSignatures; }
        }

        public SecurityToken GetSecurityToken()
        {
            return m_identity.GetSecurityToken();
        }

        public UserIdentityToken GetIdentityToken()
        {
            return m_identity.GetIdentityToken();
        }
    }
}
