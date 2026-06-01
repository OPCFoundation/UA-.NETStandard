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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Migration shim &#8212; restores synchronous <c>Encrypt</c>, <c>Decrypt</c>,
    /// <c>Sign</c> and <c>Verify</c> operations on the user identity token
    /// surface. In 1.6 the legacy synchronous methods on
    /// <c>UserIdentityToken</c> were removed in favour of asynchronous
    /// counterparts on <see cref="IUserIdentityTokenHandler"/>. These shims
    /// block the calling thread on the async path so existing call sites
    /// keep compiling; consumers should migrate to the <c>*Async</c>
    /// variants.
    /// </summary>
    public static class UserIdentityTokenHandlerShim
    {
        extension(IUserIdentityTokenHandler handler)
        {
            /// <summary>
            /// Encrypts the token. Synchronous shim for
            /// <see cref="IUserIdentityTokenHandler.EncryptAsync"/>.
            /// </summary>
            [Obsolete("Synchronous Encrypt was removed in 1.6. Use EncryptAsync. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/MigrationGuide.md#ua0011")]
            [OpcUaShim("UA0011")]
            public void Encrypt(
                Certificate receiverCertificate,
                byte[] receiverNonce,
                string securityPolicyUri,
                IServiceMessageContext context,
                Nonce? receiverEphemeralKey = null,
                Certificate? senderCertificate = null,
                CertificateCollection? senderIssuerCertificates = null,
                bool doNotEncodeSenderCertificate = false)
            {
                handler.EncryptAsync(
                    receiverCertificate,
                    receiverNonce,
                    securityPolicyUri,
                    context,
                    receiverEphemeralKey,
                    senderCertificate,
                    senderIssuerCertificates,
                    doNotEncodeSenderCertificate)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            /// <summary>
            /// Decrypts the token. Synchronous shim for
            /// <see cref="IUserIdentityTokenHandler.DecryptAsync"/>.
            /// </summary>
            [Obsolete("Synchronous Decrypt was removed in 1.6. Use DecryptAsync. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/MigrationGuide.md#ua0011")]
            [OpcUaShim("UA0011")]
            public void Decrypt(
                Certificate certificate,
                Nonce receiverNonce,
                string securityPolicyUri,
                IServiceMessageContext context,
                Nonce? ephemeralKey = null,
                Certificate? senderCertificate = null,
                CertificateCollection? senderIssuerCertificates = null,
                ICertificateValidatorEx? validator = null)
            {
                handler.DecryptAsync(
                    certificate,
                    receiverNonce,
                    securityPolicyUri,
                    context,
                    ephemeralKey,
                    senderCertificate,
                    senderIssuerCertificates,
                    validator)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            /// <summary>
            /// Creates a signature with the token. Synchronous shim for
            /// <see cref="IUserIdentityTokenHandler.SignAsync"/>.
            /// </summary>
            [Obsolete("Synchronous Sign was removed in 1.6. Use SignAsync. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/MigrationGuide.md#ua0011")]
            [OpcUaShim("UA0011")]
            public SignatureData Sign(byte[] dataToSign, string securityPolicyUri)
            {
                return handler.SignAsync(dataToSign, securityPolicyUri)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            /// <summary>
            /// Verifies a signature created with the token. Synchronous shim
            /// for <see cref="IUserIdentityTokenHandler.VerifyAsync"/>.
            /// </summary>
            [Obsolete("Synchronous Verify was removed in 1.6. Use VerifyAsync. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/MigrationGuide.md#ua0011")]
            [OpcUaShim("UA0011")]
            public bool Verify(
                byte[] dataToVerify,
                SignatureData signatureData,
                string securityPolicyUri)
            {
                return handler.VerifyAsync(dataToVerify, signatureData, securityPolicyUri)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        }
    }
}
