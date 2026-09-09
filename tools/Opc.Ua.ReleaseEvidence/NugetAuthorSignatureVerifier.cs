// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class NugetAuthorSignatureVerifier(
        EvidenceFiles files, ProcessRunner runner) : IArtifactSignatureVerifier
    {
        public async Task<bool> VerifyAsync(
            string artifactPath,
            ArtifactSignatureProof proof,
            string bundleRoot,
            TrustedPolicySnapshot policy,
            CancellationToken cancellationToken)
        {
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
