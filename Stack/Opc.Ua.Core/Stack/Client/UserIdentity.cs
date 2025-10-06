/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;

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
            : this(new AnonymousIdentityToken())
        {
        }

        /// <summary>
        /// Initializes the object as an anonymous user.
        /// </summary>
        public UserIdentity(AnonymousIdentityToken anonymousIdentityToken)
        {
            Initialize(anonymousIdentityToken);
        }

        /// <summary>
        /// Initializes the object with a username and password.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="password">The password.</param>
        public UserIdentity(string username, string password)
            : this (new UserNameIdentityToken
            {
                UserName = username,
                DecryptedPassword = password
            })
        {
        }

        /// <summary>
        /// Initializes the object with a username token.
        /// </summary>
        public UserIdentity(UserNameIdentityToken userNameToken)
        {
            Initialize(userNameToken);
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
        [Obsolete("Use UserIdentityToken(X509IdentityToken, ITelemetryContext) instead.")]
        public UserIdentity(X509IdentityToken x509Token)
            : this (x509Token, null)
        {
        }

        /// <summary>
        /// Initializes the object with an X509 certificate identifier
        /// </summary>
        [Obsolete("Use CreateAsync method instead.")]
        public UserIdentity(CertificateIdentifier certificateId)
            : this(certificateId, new CertificatePasswordProvider(string.Empty))
        {
        }

        /// <summary>
        /// Initializes the object with an X509 certificate identifier and a CertificatePasswordProvider
        /// </summary>
        [Obsolete("Use CreateAsync method instead.")]
        public UserIdentity(
            CertificateIdentifier certificateId,
            CertificatePasswordProvider certificatePasswordProvider)
            : this(certificateId
                .LoadPrivateKeyExAsync(certificatePasswordProvider).GetAwaiter().GetResult(), null)
        {
        }

        /// <summary>
        /// Initializes the object with an X509 certificate
        /// </summary>
        [Obsolete("Use UserIdentityToken(X509Certificate2, ITelemetryContext) instead.")]
        public UserIdentity(X509Certificate2 certificate) : this(certificate, null)
        {
        }

        /// <summary>
        /// Initializes the object with an X509 certificate
        /// </summary>
        public UserIdentity(X509Certificate2 certificate, ITelemetryContext telemetry)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            if (!certificate.HasPrivateKey)
            {
                throw new ServiceResultException(
                    "Cannot create User Identity with Certificate that does not have a private key");
            }

            Initialize(certificate, telemetry);
        }

        /// <summary>
        /// Initializes the object with a UA identity token.
        /// </summary>
        /// <param name="token">The user identity token.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public UserIdentity(UserIdentityToken token, ITelemetryContext telemetry)
        {
            switch (token)
            {
                case X509IdentityToken x509Token:
                    Initialize(x509Token, telemetry);
                    break;
                case UserNameIdentityToken usernameToken:
                    Initialize(usernameToken);
                    break;
                case AnonymousIdentityToken anonymous:
                    Initialize(anonymous);
                    break;
                case IssuedIdentityToken issuedToken:
                    Initialize(issuedToken);
                    break;
                default:
                    throw new ArgumentException("Unrecognized UA user identity token type.", nameof(token));
            }
        }

        /// <summary>
        /// Initializes the object with an X509 certificate identifier and a CertificatePasswordProvider
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="certificateId"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<UserIdentity> CreateAsync(
            CertificateIdentifier certificateId,
            CertificatePasswordProvider certificatePasswordProvider,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            if (certificateId == null)
            {
                throw new ArgumentNullException(nameof(certificateId));
            }

            X509Certificate2 certificate = await certificateId
                .LoadPrivateKeyExAsync(certificatePasswordProvider, applicationUri: null, telemetry, ct)
                .ConfigureAwait(false);

            if (certificate == null || !certificate.HasPrivateKey)
            {
                throw new ServiceResultException(
                    "Cannot create User Identity with CertificateIdentifier that does not contain a private key");
            }

            return new UserIdentity(certificate, telemetry);
        }

        /// <summary>
        /// Initializes the object with an X509 certificate identifier
        /// </summary>
        public static Task<UserIdentity> CreateAsync(
            CertificateIdentifier certificateId,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            return CreateAsync(
                certificateId,
                new CertificatePasswordProvider(string.Empty),
                telemetry,
                ct);
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
            Initialize(new AnonymousIdentityToken());
        }

        /// <summary>
        /// Gets or sets the UserIdentityToken PolicyId associated with the UserIdentity.
        /// </summary>
        /// <remarks>
        /// This value is used to initialize the UserIdentityToken object when GetIdentityToken() is called.
        /// </remarks>
        [DataMember(Name = "PolicyId", IsRequired = false, Order = 10)]
        public string PolicyId
        {
            get => m_token.PolicyId;
            set => m_token.PolicyId = value;
        }

        /// <inheritdoc/>
        public string DisplayName { get; set; }

        /// <inheritdoc/>
        [DataMember(Name = "TokenType", IsRequired = true, Order = 20)]
        public UserTokenType TokenType { get; private set; }

        /// <inheritdoc/>
        [DataMember(Name = "IssuedTokenType", IsRequired = false, Order = 30)]
        public XmlQualifiedName IssuedTokenType { get; private set; }

        /// <inheritdoc/>
        public bool SupportsSignatures => false;

        /// <summary>
        ///  Get or sets the list of granted role ids associated to the UserIdentity.
        /// </summary>
        public NodeIdCollection GrantedRoleIds { get; private set; }

        /// <inheritdoc/>
        public UserIdentityToken GetIdentityToken()
        {
            // check for null and return anonymous.
            return m_token ?? new AnonymousIdentityToken();
        }

        private void Initialize()
        {
            GrantedRoleIds = [];
        }

        /// <summary>
        /// Initializes the object with a UA identity token
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        private void Initialize(UserNameIdentityToken usernameToken)
        {
            Initialize();

            m_token = usernameToken ?? throw new ArgumentNullException(nameof(usernameToken));
            TokenType = UserTokenType.UserName;
            IssuedTokenType = null;
            DisplayName = usernameToken.UserName;
        }

        /// <summary>
        /// Initializes the object with a UA identity token
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        private void Initialize(X509IdentityToken x509Token, ITelemetryContext telemetry)
        {
            Initialize();

            m_token = x509Token ?? throw new ArgumentNullException(nameof(x509Token));
            TokenType = UserTokenType.Certificate;
            IssuedTokenType = null;
            if (x509Token.Certificate != null)
            {
                DisplayName = x509Token.Certificate.Subject;
            }
            else
            {
                X509Certificate2 cert = CertificateFactory.Create(
                    x509Token.CertificateData,
                    true,
                    telemetry);
                DisplayName = cert.Subject;
            }
        }

        /// <summary>
        /// Initializes the object with a UA identity token
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        private void Initialize(AnonymousIdentityToken anonymousToken)
        {
            Initialize();

            m_token = anonymousToken ?? throw new ArgumentNullException(nameof(anonymousToken));

            TokenType = UserTokenType.Anonymous;
            IssuedTokenType = null;
            DisplayName = "Anonymous";
        }

        /// <summary>
        /// Initializes the object with a UA identity token
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        private void Initialize(IssuedIdentityToken issuedToken)
        {
            Initialize();
            m_token = issuedToken ?? throw new ArgumentNullException(nameof(issuedToken));

            if (issuedToken.IssuedTokenType == Ua.IssuedTokenType.JWT)
            {
                if (issuedToken.DecryptedTokenData == null ||
                    issuedToken.DecryptedTokenData.Length == 0)
                {
                    throw new ArgumentException(
                        "JSON Web Token has no data associated with it.",
                        nameof(issuedToken));
                }

                TokenType = UserTokenType.IssuedToken;
                IssuedTokenType = new XmlQualifiedName(string.Empty, Profiles.JwtUserToken);
                DisplayName = "JWT";
                return;
            }
            throw new NotSupportedException("Only JWT Issued Tokens are supported!");
        }

        /// <summary>
        /// Initializes the object with an X509 certificate
        /// </summary>
        private void Initialize(X509Certificate2 certificate, ITelemetryContext telemetry)
        {
            var token = new X509IdentityToken
            {
                CertificateData = certificate.RawData,
                Certificate = certificate
            };
            Initialize(token, telemetry);
        }

        private UserIdentityToken m_token;
    }

    /// <summary>
    /// Stores information about the user that is currently being impersonated.
    /// </summary>
    public class ImpersonationContext
    {
        /// <summary>
        /// The security principal being impersonated.
        /// </summary>
        public IPrincipal Principal { get; set; }
    }
}
