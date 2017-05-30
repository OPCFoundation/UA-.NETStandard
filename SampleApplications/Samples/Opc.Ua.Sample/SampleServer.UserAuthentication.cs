/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.Server;
using System;
using System.IdentityModel.Selectors;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace Opc.Ua.Sample
{
    public partial class SampleServer
    {
        #region User Validation Functions
        /// <summary>
        /// Creates the objects used to validate the user identity tokens supported by the server.
        /// </summary>
        private void CreateUserIdentityValidators(ApplicationConfiguration configuration)
        { 
            for (int ii = 0; ii < configuration.ServerConfiguration.UserTokenPolicies.Count; ii++)
            {
                UserTokenPolicy policy = configuration.ServerConfiguration.UserTokenPolicies[ii];

                // ignore policies without an explicit id.
                if (String.IsNullOrEmpty(policy.PolicyId))
                {
                    continue;
                }
                
                // create a validator for an issued token policy.
                if (policy.TokenType == UserTokenType.IssuedToken)
                {
                    // the name of the element in the configuration file.
                    XmlQualifiedName qname = new XmlQualifiedName(policy.PolicyId, Namespaces.OpcUa);

                    // find the id for the issuer certificate.
                    CertificateIdentifier id = configuration.ParseExtension<CertificateIdentifier>(qname);
                    
                    if (id == null)
                    {
                        Utils.Trace(
                            (int)Utils.TraceMasks.Error, 
                            "Could not load CertificateIdentifier for UserTokenPolicy {0}", 
                            policy.PolicyId);

                        continue;
                    }
               }
                
                // create a validator for a certificate token policy.
                if (policy.TokenType == UserTokenType.Certificate)
                {
                    // the name of the element in the configuration file.
                    XmlQualifiedName qname = new XmlQualifiedName(policy.PolicyId, Namespaces.OpcUa);
                    
                    // find the location of the trusted issuers.
                    CertificateTrustList trustedIssuers = configuration.ParseExtension<CertificateTrustList>(qname);
                    
                    if (trustedIssuers == null)
                    {
                        Utils.Trace(
                            (int)Utils.TraceMasks.Error, 
                            "Could not load CertificateTrustList for UserTokenPolicy {0}", 
                            policy.PolicyId);

                        continue;
                    }

                    // trusts any certificate in the trusted people store.
                    m_certificateValidator = CertificateValidator.GetChannelValidator();
                }
            }
        }

        /// <summary>
        /// Called when a client tries to change its user identity.
        /// </summary>
        private void SessionManager_ImpersonateUser(Session session, ImpersonateEventArgs args)
        {
            // check for a WSS token.
            IssuedIdentityToken wssToken = args.NewIdentity as IssuedIdentityToken;

            // check for a user name token.
            UserNameIdentityToken userNameToken = args.NewIdentity as UserNameIdentityToken;
            
            if (userNameToken != null)
            {
                VerifyPassword(userNameToken.UserName, userNameToken.DecryptedPassword);
                args.Identity = new UserIdentity(userNameToken);
                Utils.Trace("UserName Token Accepted: {0}", args.Identity.DisplayName);
                return;
            }

            // check for x509 user token.
            X509IdentityToken x509Token = args.NewIdentity as X509IdentityToken;
            
            if (x509Token != null)
            {
                VerifyCertificate(x509Token.Certificate);
                args.Identity = new UserIdentity(x509Token);
                Utils.Trace("X509 Token Accepted: {0}", args.Identity.DisplayName);
                return;
            }
        }

        /// <summary>
        /// Validates the password for a username token.
        /// </summary>
        private void VerifyPassword(string userName, string password)
        {
            if (String.IsNullOrEmpty(password))
            {
                // construct translation object with default text.
                TranslationInfo info = new TranslationInfo(
                    "InvalidPassword",
                    "en-US",
                    "Specified password is not valid for user '{0}'.",
                    userName);

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(new ServiceResult(
                    StatusCodes.BadIdentityTokenRejected,
                    "InvalidPassword",
                    "http://opcfoundation.org/UA/Sample/",
                    new LocalizedText(info)));
            }
        }        

        /// <summary>
        /// Verifies that a certificate user token is trusted.
        /// </summary>
        private void VerifyCertificate(X509Certificate2 certificate)
        {        
            try
            {
                if (m_certificateValidator != null)
                {
                    m_certificateValidator.Validate(certificate);
                }
                else
                {
                    CertificateValidator.Validate(certificate);
                }

                // determine if self-signed.
                bool isSelfSigned = Utils.CompareDistinguishedName(certificate.Subject, certificate.Issuer);

                // do not allow self signed application certs as user token
                if (isSelfSigned && Utils.HasApplicationURN(certificate))
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateUseNotAllowed);
                }
            }
            catch (Exception)
            {
                // construct translation object with default text.
                TranslationInfo info = new TranslationInfo(
                    "InvalidCertificate",
                    "en-US",
                    "'{0}' is not a trusted user certificate.",
                    certificate.Subject);

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(new ServiceResult(
                    StatusCodes.BadIdentityTokenRejected,
                    "InvalidCertificate",
                    "http://opcfoundation.org/UA/Sample/",
                    new LocalizedText(info)));
            }
        }     
        #endregion
        
        #region Private Fields
        private X509CertificateValidator m_certificateValidator; 
        #endregion 
    }
}
