/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading.Tasks;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// A fluent API to build the application configuration.
    /// </summary>
    public interface IApplicationConfigurationBuilder :
        IApplicationConfigurationBuilderTypes,
        IApplicationConfigurationBuilderServerSelected,
        IApplicationConfigurationBuilderClientSelected,
        IApplicationConfigurationBuilderSecurity,
        IApplicationConfigurationBuilderSecurityOptions,
        IApplicationConfigurationBuilderServerPolicies,
        IApplicationConfigurationBuilderCreate
    {
    };

    /// <summary>
    /// The client or server configuration types to chose.
    /// </summary>
    public interface IApplicationConfigurationBuilderTypes :
        IApplicationConfigurationBuilderServer,
        IApplicationConfigurationBuilderClient
    {
    }

    /// <summary>
    /// The interfaces to implement if a server is selected.
    /// </summary>
    public interface IApplicationConfigurationBuilderServerSelected :
        IApplicationConfigurationBuilderServerPolicies,
        IApplicationConfigurationBuilderClient,
        IApplicationConfigurationBuilderSecurity
    {
    }

    /// <summary>
    /// The interfaces to implement if a client is selected.
    /// </summary>
    public interface IApplicationConfigurationBuilderClientSelected :
        IApplicationConfigurationBuilderSecurity
    {
    }

    /// <summary>
    /// Add the server configuration (optional).
    /// </summary>
    public interface IApplicationConfigurationBuilderServer
    {
        /// <summary>
        /// Configure instance to be used for UA server.
        /// </summary>
        IApplicationConfigurationBuilderServerSelected AsServer(
            string[] baseAddresses,
            string[] alternateBaseAddresses = null);
    }

    /// <summary>
    /// Add the client configuration (optional).
    /// </summary>
    public interface IApplicationConfigurationBuilderClient
    {
        /// <summary>
        /// Configure instance to be used for UA client.
        /// </summary>
        IApplicationConfigurationBuilderClientSelected AsClient();
    }

    /// <summary>
    /// Add the supported server policies.
    /// </summary>
    public interface IApplicationConfigurationBuilderServerPolicies
    {
        /// <summary>
        /// Add the unsecure security policy type none to server configuration.
        /// </summary>
        IApplicationConfigurationBuilderServerSelected AddUnsecurePolicyNone();

        /// <summary>
        /// Add the sign security policies to the server configuration.
        /// </summary>
        IApplicationConfigurationBuilderServerSelected AddSignPolicies();

        /// <summary>
        /// Add the sign and encrypt security policies to the server configuration.
        /// </summary>
        IApplicationConfigurationBuilderServerSelected AddSignAndEncryptPolicies();

        /// <summary>
        /// Add the specified security policy with the specified security mode.
        /// </summary>
        /// <param name="securityMode">The message security mode to add the policy to.</param>
        /// <param name="securityPolicy">The security policy Uri string.</param>
        IApplicationConfigurationBuilderServerSelected AddPolicy(MessageSecurityMode securityMode, string securityPolicy);

        /// <summary>
        /// Add user token policy to the server configuration.
        /// </summary>
        /// <param name="userTokenType">The user token type to add.</param>
        IApplicationConfigurationBuilderServerSelected AddUserTokenPolicy(UserTokenType userTokenType);
    }

    /// <summary>
    /// Add the security configuration (mandatory).
    /// </summary>
    public interface IApplicationConfigurationBuilderSecurity
    {
        /// <summary>
        /// Add the security configuration.
        /// </summary>
        /// <remarks>
        /// The pki root path default to the certificate store
        /// location as defined in <see cref="CertificateStoreIdentifier.DefaultPKIRoot"/>
        /// A <see cref="CertificateStoreType"/> defaults to the corresponding default store location.
        /// </remarks>
        /// <param name="subjectName">Application certificate subject name as distinguished name. A DC=localhost entry is converted to the hostname. The common name CN= is mandatory.</param>
        /// <param name="pkiRoot">The path to the pki root. By default all cert stores use the pki root.</param>
        /// <param name="appRoot">The path to the app cert store, if different than the pki root.</param>
        /// <param name="rejectedRoot">The path to the rejected certificate store.</param>
        IApplicationConfigurationBuilderSecurityOptions AddSecurityConfiguration(
            string subjectName,
            string pkiRoot = null,
            string appRoot = null,
            string rejectedRoot = null
            );
    }

    /// <summary>
    /// Add security options to the configuration.
    /// </summary>
    public interface IApplicationConfigurationBuilderSecurityOptions :
        IApplicationConfigurationBuilderCreate
    {
        /// <summary>
        /// Whether an unknown application certificate should be accepted
        /// once all other security checks passed.
        /// </summary>
        /// <param name="autoAccept"><see langword="true"/> to accept unknown application certificates.</param>
        IApplicationConfigurationBuilderSecurityOptions SetAutoAcceptUntrustedCertificates(bool autoAccept);

        /// <summary>
        /// Whether a newly created application certificate should be added to the trusted store.
        /// This function is only useful if multiple UA applications share the same trusted store.
        /// </summary>
        /// <param name="addToTrustedStore"><see langword="true"/> to add the cert to the trusted store.</param>
        IApplicationConfigurationBuilderSecurityOptions SetAddAppCertToTrustedStore(bool addToTrustedStore);

        /// <summary>
        /// Reject SHA1 signed certificates.
        /// </summary>
        /// <param name="rejectSHA1Signed"><see langword="false"/> to accept SHA1 signed certificates.</param>
        IApplicationConfigurationBuilderSecurityOptions SetRejectSHA1SignedCertificates(bool rejectSHA1Signed);

        /// <summary>
        /// Reject chain validation with CA certs with unknown revocation status,
        /// e.g. when the CRL is not available or the OCSP provider is offline.
        /// </summary>
        /// <param name="rejectUnknownRevocationStatus"><see langword="false"/> to accept CA certs with unknown revocation status.</param>
        IApplicationConfigurationBuilderSecurityOptions SetRejectUnknownRevocationStatus(bool rejectUnknownRevocationStatus);

        /// <summary>
        /// Whether to suppress errors which are caused by clients and servers which provide
        /// zero nonce values or nonce with insufficient entropy.
        /// Suppressing this error is a security risk and may allow an attacker to decrypt user tokens.
        /// Only use if interoperability issues with legacy servers or clients leave no other choice to operate.
        /// </summary>
        /// <param name="suppressNonceValidationErrors"><see langword="true"/> to suppress nonce validation errors.</param>
        IApplicationConfigurationBuilderSecurityOptions SetSuppressNonceValidationErrors(bool suppressNonceValidationErrors);

        /// <summary>
        /// Whether a certificate chain should be sent with the application certificate.
        /// Only used if the application certificate is CA signed.
        /// </summary>
        /// <param name="sendCertificateChain"><see langword="true"/> to send the certificate chain with the application certificate.</param>
        IApplicationConfigurationBuilderSecurityOptions SetSendCertificateChain(bool sendCertificateChain);

        /// <summary>
        /// The minimum RSA key size to accept.
        /// By default the key size is set to <see cref="CertificateFactory.DefaultKeySize"/>.
        /// </summary>
        /// <param name="keySize">The minimum RSA key size to accept.</param>
        IApplicationConfigurationBuilderSecurityOptions SetMinimumCertificateKeySize(ushort keySize);

        /// <summary>
        /// Add a certificate password provider.
        /// </summary>
        /// <param name="certificatePasswordProvider">The certificate password provider to use.</param>
        IApplicationConfigurationBuilderSecurityOptions AddCertificatePasswordProvider(ICertificatePasswordProvider certificatePasswordProvider);
    }

    /// <summary>
    /// Create and validate the application configuration.
    /// </summary>
    public interface IApplicationConfigurationBuilderCreate
    {
        /// <summary>
        /// The application configuration.
        /// </summary>
        ApplicationConfiguration ApplicationConfiguration { get; }

        /// <summary>
        /// Creates and updates the application configuration.
        /// </summary>
        Task<ApplicationConfiguration> Create();
    }
}

