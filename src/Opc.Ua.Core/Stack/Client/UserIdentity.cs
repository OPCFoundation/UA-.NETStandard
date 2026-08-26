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
using System.Runtime.Serialization;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// A generic user identity class.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class UserIdentity : IUserIdentity
    {
        /// <summary>
        /// Initializes the object as an anonymous user.
        /// </summary>
        public UserIdentity()
        {
            m_token = new AnonymousIdentityTokenHandler();
        }

        /// <summary>
        /// Initializes the object with a username and utf8 password.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="password">The password.</param>
        public UserIdentity(string username, byte[] password)
            : this(username, password, securityPolicies: null)
        {
        }

        /// <summary>
        /// Initializes the object with a username, utf8 password and security policies.
        /// </summary>
        public UserIdentity(
            string username,
            byte[] password,
            ISecurityPolicyRegistry? securityPolicies)
        {
            m_token = new UserNameIdentityTokenHandler(username, password, securityPolicies);
        }

        /// <summary>
        /// Initializes the object with a username and utf8 password.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="password">The password.</param>
        public UserIdentity(string username, ReadOnlySpan<byte> password)
            : this(username, password, securityPolicies: null)
        {
        }

        /// <summary>
        /// Initializes the object with a username, utf8 password and security policies.
        /// </summary>
        public UserIdentity(
            string username,
            ReadOnlySpan<byte> password,
            ISecurityPolicyRegistry? securityPolicies)
        {
            m_token = new UserNameIdentityTokenHandler(username, password, securityPolicies);
        }

        /// <summary>
        /// Initializes the object with a decrypted issued token.
        /// </summary>
        /// <param name="decryptedTokenData">The decrypted token data.</param>
        /// <param name="issuedTokenTypeProfileUri">The issued token profile.</param>
        public UserIdentity(
            ReadOnlySpan<byte> decryptedTokenData,
            string issuedTokenTypeProfileUri)
            : this(decryptedTokenData, issuedTokenTypeProfileUri, securityPolicies: null)
        {
        }

        /// <summary>
        /// Initializes the object with a decrypted issued token and security policies.
        /// </summary>
        public UserIdentity(
            ReadOnlySpan<byte> decryptedTokenData,
            string issuedTokenTypeProfileUri,
            ISecurityPolicyRegistry? securityPolicies)
        {
            m_token = new IssuedIdentityTokenHandler(
                issuedTokenTypeProfileUri,
                decryptedTokenData,
                securityPolicies);
        }

        /// <summary>
        /// Create user identity with a custom token handler.
        /// </summary>
        /// <param name="token"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public UserIdentity(IUserIdentityTokenHandler token)
        {
            m_token = token?.Copy() ?? throw new ArgumentNullException(nameof(token));
        }

        /// <summary>
        /// Initializes the object with a UA identity token.
        /// </summary>
        /// <param name="token">The user identity token.</param>
        public UserIdentity(UserIdentityToken token)
            : this(token, securityPolicies: null)
        {
        }

        /// <summary>
        /// Initializes the object with a UA identity token and security policies.
        /// </summary>
        public UserIdentity(
            UserIdentityToken token,
            ISecurityPolicyRegistry? securityPolicies)
        {
            m_token = token.AsTokenHandler(securityPolicies) ??
                throw new ArgumentException(
                    "Unrecognized UA user identity token type.",
                    nameof(token));
        }

        /// <summary>
        /// Initializes the object with an X509 certificate identifier
        /// resolved on demand through a centralised
        /// <see cref="ICertificateProvider"/>. The handler does NOT
        /// hold a live <see cref="Certificate"/> reference; the
        /// provider materialises the cert (with private key) on each
        /// signing operation.
        /// </summary>
        /// <remarks>
        /// This is the only cert-based factory; the historical
        /// <c>UserIdentity(Certificate)</c> ctor and the legacy
        /// <c>CreateAsync</c> overloads that pre-resolved a
        /// <see cref="Certificate"/> have been removed. Long-lived
        /// identities held by an OPC UA <c>ISession</c> resolve the
        /// cert per signing operation through the provider's cache.
        /// </remarks>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ServiceResultException"/>
        /// <param name="certificateId">The user certificate identifier.</param>
        /// <param name="passwordProvider">Resolves the private key password.</param>
        /// <param name="certificateProvider">Materialises the certificate.</param>
        /// <param name="ct">The cancellation token.</param>
        public static Task<UserIdentity> CreateAsync(
            CertificateIdentifier certificateId,
            ICertificatePasswordProvider passwordProvider,
            ICertificateProvider certificateProvider,
            CancellationToken ct = default)
        {
            return CreateAsync(
                certificateId,
                passwordProvider,
                certificateProvider,
                securityPolicies: null,
                ct);
        }

        /// <summary>
        /// Creates a certificate identity using the specified security policies.
        /// </summary>
        public static Task<UserIdentity> CreateAsync(
            CertificateIdentifier certificateId,
            ICertificatePasswordProvider passwordProvider,
            ICertificateProvider certificateProvider,
            ISecurityPolicyRegistry? securityPolicies,
            CancellationToken ct = default)
        {
            if (certificateId == null)
            {
                throw new ArgumentNullException(nameof(certificateId));
            }
            if (passwordProvider == null)
            {
                throw new ArgumentNullException(nameof(passwordProvider));
            }
            if (certificateProvider == null)
            {
                throw new ArgumentNullException(nameof(certificateProvider));
            }

            var handler = new X509IdentityTokenHandler(
                certificateId,
                passwordProvider,
                certificateProvider,
                securityPolicies);
            return Task.FromResult(new UserIdentity(handler));
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <remarks>
        /// The user identity encodes only the token type,
        /// the issued token and the policy id, if available.
        /// Hence, the default constructor
        /// is used to initialize the token as anonymous.
        /// </remarks>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            m_token = new AnonymousIdentityTokenHandler();
        }

        /// <summary>
        /// Gets or sets the UserIdentityToken PolicyId associated
        /// with the UserIdentity.
        /// </summary>
        [DataMember(Name = "PolicyId", IsRequired = false, Order = 10)]
        public string PolicyId
        {
            get => m_token.Token.PolicyId!;
            set => m_token.Token.PolicyId = value;
        }

        /// <inheritdoc/>
        [DataMember(Name = "TokenType", IsRequired = true, Order = 20)]
        public UserTokenType TokenType
        {
            get => m_typeBackingField ?? m_token.TokenType;
            set => m_typeBackingField = value;
        }

        // TODO Fix the save/restore asap
        private UserTokenType? m_typeBackingField;

        /// <inheritdoc/>
        [DataMember(Name = "IssuedTokenType", IsRequired = false, Order = 30)]
        public XmlQualifiedName IssuedTokenType
        {
            // Legacy support for issued token type as XmlQualifiedName.
            // This will be removed in future releases.
            // Use UpdatePolicy to set the policy and thus token type.
            get
            {
                if (m_token is IssuedIdentityTokenHandler issuedToken)
                {
                    return new(null, issuedToken.IssuedTokenTypeProfileUri);
                }
                return field!;
            }
            set
            {
                if (m_token is IssuedIdentityTokenHandler issuedToken)
                {
                    issuedToken.IssuedTokenTypeProfileUri = value?.Namespace;
                    return;
                }
                field = value;
            }
        }

        /// <inheritdoc/>
        public string DisplayName
        {
            get => field ?? m_token.DisplayName;
            set;
        }

        /// <inheritdoc/>
        public IUserIdentityTokenHandler TokenHandler => m_token;

        /// <inheritdoc/>
        public bool SupportsSignatures => false;

        /// <summary>
        ///  Get or sets the list of granted role ids associated to the UserIdentity.
        /// </summary>
        public ArrayOf<NodeId> GrantedRoleIds => [ObjectIds.WellKnownRole_Anonymous];

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is UserIdentity identity)
            {
                return m_token.Equals(identity.m_token);
            }
            return base.Equals(obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                PolicyId,
                TokenType,
                IssuedTokenType,
                DisplayName,
                GrantedRoleIds);
        }

        private IUserIdentityTokenHandler m_token;
    }

    /// <summary>
    /// Stores information about the user that is currently being impersonated.
    /// </summary>
    public class ImpersonationContext
    {
        /// <summary>
        /// The security principal being impersonated.
        /// </summary>
        public IPrincipal? Principal { get; set; }
    }
}
