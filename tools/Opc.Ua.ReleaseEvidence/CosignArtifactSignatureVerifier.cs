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
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Verifies OCI artifact attestation bundles using a pinned cosign executable and independent trust roots.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    /// <param name="runner">The service for bounded external tool execution.</param>
    internal sealed class CosignArtifactSignatureVerifier(
        EvidenceFiles files, ProcessRunner runner) : IArtifactSignatureVerifier
    {
        /// <summary>
        /// Checks the pinned offline verifier, signer identity, artifact subject, and preserved attestation bytes.
        /// </summary>
        public async Task<bool> VerifyAsync(
            string artifactPath, ArtifactSignatureProof proof, string bundleRoot,
            TrustedPolicySnapshot policy, CancellationToken cancellationToken)
        {
            if (policy.CosignVerifier is not { } pin ||
                proof.Kind is not ("oci-index" or "oci-manifest") || proof.AuthorityId == null)
            {
                return false;
            }
            VerificationAuthority[] authorities = [.. policy.Authorities.Where(a => a.Id == proof.AuthorityId &&
                a.RecordKinds.Contains("artifact-signatures", StringComparer.Ordinal))];
            if (authorities.Length != 1)
            {
                return false;
            }
            VerificationAuthority authority = authorities[0];
            string bundlePath = EvidenceFiles.Confined(bundleRoot, proof.BundlePath);
            ProtectedTrustPolicySource.RequireOutside(pin.Path, [bundleRoot, Path.GetDirectoryName(artifactPath)!]);
            if (await files.DigestAsync(pin.Path, cancellationToken).ConfigureAwait(false) != pin.Digest ||
                await files.DigestAsync(policy.TrustedRoot.Path, cancellationToken).ConfigureAwait(false) !=
                    policy.TrustedRoot.Digest ||
                await files.DigestAsync(bundlePath, cancellationToken).ConfigureAwait(false) !=
                    proof.SignatureDigest ||
                await files.DigestAsync(artifactPath, cancellationToken).ConfigureAwait(false) != proof.ArtifactDigest)
            {
                return false;
            }
            string version = await runner.RunAsync(
                Path.GetDirectoryName(pin.Path)!, pin.Path, ["version", "--json"], cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument versionDocument = JsonDocument.Parse(version);
            if (versionDocument.RootElement.GetProperty("gitVersion").GetString() != "v" + pin.Version)
            {
                throw new InvalidDataException("The protected cosign version differs from its pin.");
            }
            try
            {
                await runner.RunAsync(Path.GetDirectoryName(pin.Path)!, pin.Path,
                [
                    "verify-blob-attestation", "--bundle", bundlePath,
                    "--trusted-root", policy.TrustedRoot.Path,
                    "--certificate-identity", authority.CertificateIdentity,
                    "--certificate-oidc-issuer", authority.Issuer,
                    "--type", GitHubRecordSignatureVerifier.PredicateType,
                    "--new-bundle-format", "--check-claims", "--offline", Path.GetFullPath(artifactPath)
                ], cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return false;
            }
            // Only parse the preserved statement after cosign verifies the bundle against independent roots.
            using JsonDocument bundle = await files.ReadJsonAsync(bundlePath, cancellationToken).ConfigureAwait(false);
            byte[] certificate = Convert.FromBase64String(bundle.RootElement.GetProperty("verificationMaterial")
                .GetProperty("certificate").GetProperty("rawBytes").GetString()!);
            using var signer = X509CertificateLoader.LoadCertificate(certificate);
            using JsonDocument statement = JsonDocument.Parse(Convert.FromBase64String(
                bundle.RootElement.GetProperty("dsseEnvelope").GetProperty("payload").GetString()!));
            JsonElement root = statement.RootElement;
            JsonElement subjects = root.GetProperty("subject");
            return root.GetProperty("_type").GetString() == "https://in-toto.io/Statement/v1" &&
                EvidenceFiles.Digest(certificate) == proof.SignerDigest &&
                SigstoreCertificateIdentity.MatchesVerifiedCertificate(signer, authority) &&
                root.GetProperty("predicateType").GetString() == GitHubRecordSignatureVerifier.PredicateType &&
                subjects.GetArrayLength() == 1 &&
                subjects[0].GetProperty("name").GetString() == proof.Id &&
                "sha256:" + subjects[0].GetProperty("digest").GetProperty("sha256").GetString() ==
                    proof.ArtifactDigest &&
                await files.DigestAsync(bundlePath, cancellationToken).ConfigureAwait(false) ==
                    proof.SignatureDigest &&
                await files.DigestAsync(artifactPath, cancellationToken).ConfigureAwait(false) ==
                    proof.ArtifactDigest &&
                await files.DigestAsync(pin.Path, cancellationToken).ConfigureAwait(false) == pin.Digest &&
                await files.DigestAsync(policy.TrustedRoot.Path, cancellationToken).ConfigureAwait(false) ==
                    policy.TrustedRoot.Digest;
        }
    }
}
