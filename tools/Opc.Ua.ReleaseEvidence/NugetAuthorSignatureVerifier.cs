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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Verifies primary author signatures using an independently pinned NuGet executable and approved certificate.
    /// </summary>
    /// <param name="files">The bounded evidence-file reader and digest provider.</param>
    /// <param name="runner">The bounded asynchronous verifier process runner.</param>
    internal sealed class NugetAuthorSignatureVerifier(
        EvidenceFiles files, ProcessRunner runner) : IArtifactSignatureVerifier
    {
        /// <summary>
        /// Authenticates the archive's primary author signature without trusting tools from candidate directories.
        /// </summary>
        public async Task<bool> VerifyAsync(
            string artifactPath,
            ArtifactSignatureProof proof,
            string bundleRoot,
            TrustedPolicySnapshot policy,
            CancellationToken cancellationToken)
        {
            artifactPath = Path.GetFullPath(artifactPath);
            if (proof.Kind != "nuget-package" || policy.NugetVerifier is not { } pin ||
                policy.NugetAuthorFingerprints == null ||
                Array.IndexOf(policy.NugetAuthorFingerprints, proof.SignerDigest) < 0 ||
                !VerificationControls.IsDigest(proof.SignerDigest))
            {
                return false;
            }
            NugetPrimaryIdentity? identity = await NugetPrimaryIdentity.ReadAsync(artifactPath, cancellationToken)
                .ConfigureAwait(false);
            if (identity == null || identity.CertificateDigest != proof.SignerDigest ||
                identity.SignatureDigest != proof.SignatureDigest)
            {
                return false;
            }
            ProtectedTrustPolicySource.RequireOutside(pin.Path, [bundleRoot, Path.GetDirectoryName(artifactPath)!]);
            if (await files.DigestAsync(pin.Path, cancellationToken).ConfigureAwait(false) != pin.Digest)
            {
                throw new InvalidDataException("The protected NuGet verifier executable changed.");
            }
            string version = await runner.RunAsync(
                Path.GetDirectoryName(pin.Path)!, pin.Path, ["--version"], cancellationToken).ConfigureAwait(false);
            if (version.Trim() != pin.Version)
            {
                throw new InvalidDataException("The protected NuGet SDK version differs from its pin.");
            }
            try
            {
                string output = await runner.RunAsync(Path.GetDirectoryName(pin.Path)!, pin.Path,
                [
                    "nuget", "verify", Path.GetFullPath(artifactPath), "--all",
                    "--certificate-fingerprint", proof.SignerDigest[7..], "--verbosity", "detailed"
                ], cancellationToken, true).ConfigureAwait(false);
                return output.Contains(proof.SignerDigest[7..], StringComparison.OrdinalIgnoreCase) &&
                    output.Contains("Signature type: Author", StringComparison.Ordinal) &&
                    await files.DigestAsync(artifactPath, cancellationToken).ConfigureAwait(false) ==
                        proof.ArtifactDigest &&
                    await files.DigestAsync(pin.Path, cancellationToken).ConfigureAwait(false) == pin.Digest;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
