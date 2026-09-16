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

using System.Formats.Asn1;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Checks authenticated Sigstore certificate extensions against the expected workflow and source authority.
    /// </summary>
    internal static class SigstoreCertificateIdentity
    {
        /// <summary>
        /// Matches verified certificate identity, issuer, workflow revision, hosted-runner environment, repository, and
        /// ref.
        /// </summary>
        public static bool MatchesVerifiedCertificate(
            X509Certificate2 certificate, VerificationAuthority authority)
        {
            return ReadExtension(certificate, "1.3.6.1.4.1.57264.1.8") == authority.Issuer &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.9") == authority.CertificateIdentity &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.10") == authority.DefinitionSha &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.11") == "github-hosted" &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.12") ==
                    "https://github.com/" + authority.Repository &&
                ReadExtension(certificate, "1.3.6.1.4.1.57264.1.14") == authority.Ref;
        }

        private static string? ReadExtension(X509Certificate2 certificate, string oid)
        {
            X509Extension[] extensions = [.. certificate.Extensions.Cast<X509Extension>()
                .Where(e => e.Oid?.Value == oid)];
            if (extensions.Length != 1)
            {
                return null;
            }
            try
            {
                var reader = new AsnReader(extensions[0].RawData, AsnEncodingRules.DER);
                string value = reader.ReadCharacterString(UniversalTagNumber.UTF8String);
                reader.ThrowIfNotEmpty();
                return value;
            }
            catch (AsnContentException)
            {
                return null;
            }
        }
    }
}
