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

using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    internal static class SessionClientUserId
    {
        public static string? Get(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity)
        {
            return identityToken.Token switch
            {
                AnonymousIdentityToken => null,
                UserNameIdentityToken userNameToken => userNameToken.UserName,
                X509IdentityToken x509Token => GetX509Subject(x509Token),
                IssuedIdentityToken => GetIssuedTokenOwner(authenticatedIdentity),
                _ => throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The UserIdentityToken type does not define a ClientUserId.")
            };
        }

        private static string GetX509Subject(X509IdentityToken token)
        {
            using Certificate? certificate = token.CertificateData.IsEmpty
                ? null
                : Certificate.FromRawData(token.CertificateData);
            return certificate?.Subject ??
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The X509IdentityToken does not contain a valid Certificate.");
        }

        private static string GetIssuedTokenOwner(IUserIdentity authenticatedIdentity)
        {
            if (authenticatedIdentity is not IIdentityClaims claims ||
                claims.Subject == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The authenticated IssuedIdentityToken does not expose its subject.");
            }

            return claims.Issuer == null
                ? claims.Subject
                : claims.Issuer + claims.Subject;
        }
    }
}
