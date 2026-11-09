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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Maps already authenticated UA identities to bounded, distinct sample permissions.
    /// Does not replace certificate trust validation or accept an actor supplied in a method payload.
    /// </summary>
    public sealed class RecyclingAuthorization
    {
        /// <summary>
        /// Validates the explicit certificate allow-list.
        /// </summary>
        public RecyclingAuthorization(ArrayOf<RecyclingIdentityBinding> bindings)
        {
            if (bindings.Count > 128)
            {
                throw new ArgumentException("At most 128 sample identities may be configured.", nameof(bindings));
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var processes = new HashSet<RecyclingProcess>();
            foreach (RecyclingIdentityBinding binding in bindings)
            {
                if (string.IsNullOrEmpty(binding.CertificateSha256) || binding.CertificateSha256.Length != 64 ||
                    !binding.CertificateSha256.All(Uri.IsHexDigit) ||
                    string.IsNullOrWhiteSpace(binding.Identity) || binding.Identity.Length > 128 ||
                    !Enum.IsDefined(binding.Access) || !Enum.IsDefined(binding.Process) ||
                    !names.Add(binding.Identity) ||
                    !m_bindings.TryAdd(binding.CertificateSha256, binding) ||
                    (binding.Access == RecyclingAccess.ProcessView && !processes.Add(binding.Process)))
                {
                    throw new ArgumentException(
                        "Invalid, duplicate or ambiguous identity/role binding.", nameof(bindings));
                }
            }
        }

        /// <summary>
        /// Resolves only the authenticated session's X.509 token and actual server-side session identifier.
        /// </summary>
        public RecyclingPrincipal Resolve(ISystemContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context is not ISessionSystemContext session || session.UserIdentity == null ||
                session.SessionId.GetValueOrDefault().IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied, "An authenticated session is required.");
            }
            string hash = CertificateHash(session.UserIdentity);
            if (!m_bindings.TryGetValue(hash, out RecyclingIdentityBinding? binding))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied, "Identity has no configured sample role.");
            }
            return new RecyclingPrincipal(
                binding.Identity, binding.Access, binding.Process,
                session.SessionId.GetValueOrDefault().ToString(), hash);
        }

        /// <summary>
        /// Computes the non-secret fingerprint of an authenticated user certificate with SHA-256.
        /// </summary>
        public static string CertificateHash(IUserIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            if (identity.TokenType != UserTokenType.Certificate ||
                identity.TokenHandler.Token is not X509IdentityToken token || token.CertificateData.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied, "A trusted X.509 user identity is required.");
            }
            return Convert.ToHexString(SHA256.HashData(token.CertificateData.Span));
        }

        private readonly Dictionary<string, RecyclingIdentityBinding> m_bindings =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
