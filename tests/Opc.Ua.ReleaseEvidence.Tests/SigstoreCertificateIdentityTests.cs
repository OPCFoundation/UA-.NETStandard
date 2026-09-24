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
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace Opc.Ua.ReleaseEvidence.Tests
{
    /// <summary>
    /// Checks immutable signer identity separately from the controller's source repository digest.
    /// </summary>
    [TestFixture]
    public sealed class SigstoreCertificateIdentityTests
    {
        /// <summary>
        /// Verifies certificate identity against the signer definition digest, issuer, ref, and hosted-runner identity.
        /// </summary>
        [TestCase("valid", true)]
        [TestCase("definition", false)]
        [TestCase("issuer", false)]
        [TestCase("ref", false)]
        [TestCase("runner", false)]
        public void VerifiedCertificateIdentityUsesSignerDigestNotSourceDigest(string mutation, bool expected)
        {
            var authority = new VerificationAuthority("isolated", "Synthetic/ReleaseTests",
                "https://token.actions.githubusercontent.com",
                "https://github.com/Synthetic/ReleaseTests/.github/workflows/release.yml@refs/heads/master",
                ".github/workflows/release.yml", new string('b', 40), "refs/heads/master", ["artifact-signatures"]);
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Ephemeral identity test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            AddExtension(request, "1.3.6.1.4.1.57264.1.8",
                mutation == "issuer" ? "https://unapproved.invalid" : authority.Issuer);
            AddExtension(request, "1.3.6.1.4.1.57264.1.9", authority.CertificateIdentity);
            AddExtension(request, "1.3.6.1.4.1.57264.1.10",
                mutation == "definition" ? new string('a', 40) : authority.DefinitionSha);
            AddExtension(request, "1.3.6.1.4.1.57264.1.11",
                mutation == "runner" ? "self-hosted" : "github-hosted");
            AddExtension(request, "1.3.6.1.4.1.57264.1.12", "https://github.com/" + authority.Repository);
            AddExtension(request, "1.3.6.1.4.1.57264.1.13", new string('a', 40));
            AddExtension(request, "1.3.6.1.4.1.57264.1.14",
                mutation == "ref" ? "refs/pull/1/merge" : authority.Ref);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
            Assert.That(SigstoreCertificateIdentity.MatchesVerifiedCertificate(certificate, authority),
                Is.EqualTo(expected));
        }

        private static void AddExtension(CertificateRequest request, string oid, string value)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.WriteCharacterString(UniversalTagNumber.UTF8String, value);
            request.CertificateExtensions.Add(new X509Extension(oid, writer.Encode(), false));
        }
    }
}
