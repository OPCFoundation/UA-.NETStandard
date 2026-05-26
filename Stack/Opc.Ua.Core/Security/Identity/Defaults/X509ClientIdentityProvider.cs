/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 *
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Client identity provider for X.509 user-certificate activation.
    /// </summary>
    public sealed class X509ClientIdentityProvider : IClientIdentityProvider
    {
        /// <summary>
        /// Creates an X.509 client identity provider.
        /// </summary>
        public X509ClientIdentityProvider(
            CertificateIdentifier certificateId,
            ICertificatePasswordProvider passwordProvider,
            ICertificateProvider certificateProvider)
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

            m_certificateId = certificateId;
            m_passwordProvider = passwordProvider;
            m_certificateProvider = certificateProvider;
        }

        /// <inheritdoc/>
        public IReadOnlyList<UserTokenType> SupportedTokenTypes { get; }
            = [UserTokenType.Certificate];

        /// <inheritdoc/>
        public IReadOnlyList<string> SupportedIssuedTokenProfileUris { get; }
            = Array.Empty<string>();

        /// <inheritdoc/>
        public DateTime ExpiresAt => DateTime.MaxValue;

        /// <inheritdoc/>
        public bool CanSatisfy(
            UserTokenPolicy policy,
            IdentitySelectionContext context)
        {
            return policy != null && policy.TokenType == UserTokenType.Certificate;
        }

        /// <inheritdoc/>
        public async ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (!CanSatisfy(policy, context))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "X.509 identity provider can only satisfy certificate user token policies.");
            }

            UserIdentity identity = await UserIdentity
                .CreateAsync(m_certificateId, m_passwordProvider, m_certificateProvider, ct)
                .ConfigureAwait(false);
            identity.PolicyId = policy.PolicyId ?? string.Empty;
            return identity;
        }

        private readonly CertificateIdentifier m_certificateId;
        private readonly ICertificatePasswordProvider m_passwordProvider;
        private readonly ICertificateProvider m_certificateProvider;
    }
}
