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
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Handles user identity tokens. The individual token type handlers
    /// implement this interface. They wrap a token that they encrypt, decrypt,
    /// and sign / verify signatures for. The token can be retrieved using
    /// the Token property and passed as extension object in service calls.
    /// </summary>
    /// <remarks>
    /// Previously the tokens themselves implemented crypto operations, but
    /// for security and better separation of concerns, the handlers now
    /// perform these operations and are disposable/copyable to ensure better
    /// lifetime management of sensitive data.
    /// </remarks>
    public interface IUserIdentityTokenHandler :
        IDisposable, ICloneable, IEquatable<IUserIdentityTokenHandler>
    {
        /// <summary>
        /// The token the handler operates on.
        /// </summary>
        UserIdentityToken Token { get; }

        /// <summary>
        /// Get display name of the token. This is used only for logging and
        /// diagnostics.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// The type of the wrapped token
        /// </summary>
        UserTokenType TokenType { get; }

        /// <summary>
        /// Update the policy associated with the token
        /// </summary>
        /// <param name="userTokenPolicy"></param>
        void UpdatePolicy(UserTokenPolicy userTokenPolicy);

        /// <summary>
        /// Encrypts the token
        /// </summary>
        void Encrypt(
            X509Certificate2 receiverCertificate,
            byte[] receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce receiverEphemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            bool doNotEncodeSenderCertificate = false);

        /// <summary>
        /// Decrypts the token
        /// </summary>
        void Decrypt(
            X509Certificate2 certificate,
            Nonce receiverNonce,
            string securityPolicyUri,
            IServiceMessageContext context,
            Nonce ephemeralKey = null,
            X509Certificate2 senderCertificate = null,
            X509Certificate2Collection senderIssuerCertificates = null,
            CertificateValidator validator = null);

        /// <summary>
        /// Creates a signature with the token
        /// </summary>
        SignatureData Sign(
            byte[] dataToSign,
            string securityPolicyUri);

        /// <summary>
        /// Verifies a signature created with the token
        /// </summary>
        bool Verify(
            byte[] dataToVerify,
            SignatureData signatureData,
            string securityPolicyUri);
    }

    /// <summary>
    /// Extensions for user identity tokens.
    /// </summary>
    public static class UserIdentityTokenExtensions
    {
        /// <summary>
        /// Wraps the raw token inside a token handler to operate on it.
        /// Dispose the returned handler when done. When storing it for
        /// later use clone it and then dispose the original when done.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static IUserIdentityTokenHandler AsTokenHandler(
            this UserIdentityToken token)
        {
            switch (token)
            {
                case AnonymousIdentityToken:
                    return new AnonymousIdentityTokenHandler();
                case UserNameIdentityToken userNamePassword:
                    return new UserNameIdentityTokenHandler(userNamePassword);
                case X509IdentityToken x509Identity:
                    return new X509IdentityTokenHandler(x509Identity);
                case IssuedIdentityToken issuedToken:
                    return new IssuedIdentityTokenHandler(issuedToken);
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadNotSupported,
                        "The token type {0} is not supported in this implementation.",
                        token.GetType().Name);
            }
        }

        /// <summary>
        /// Simplified clone to produce a copy of the token handler.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static T Copy<T>(this T tokenHandler)
            where T : IUserIdentityTokenHandler
        {
            return (T)tokenHandler.Clone();
        }
    }
}
