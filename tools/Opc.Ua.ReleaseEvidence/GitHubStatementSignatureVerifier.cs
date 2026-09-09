// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class GitHubStatementSignatureVerifier(
        EvidenceFiles files, ProcessRunner runner) : IStatementSignatureVerifier
    {
        public async Task<JsonDocument?> VerifyAsync(
            string subjectPath, string bundlePath, string predicateType,
            VerificationAuthority authority, TrustedPolicySnapshot policy, CancellationToken cancellationToken)
        {
            string subjectDigest = await files.DigestAsync(subjectPath, cancellationToken).ConfigureAwait(false);
            string bundleDigest = await files.DigestAsync(bundlePath, cancellationToken).ConfigureAwait(false);
            if (await files.DigestAsync(policy.Tool.Path, cancellationToken).ConfigureAwait(false) != policy.Tool.Digest ||
                await files.DigestAsync(policy.TrustedRoot.Path, cancellationToken).ConfigureAwait(false) !=
                    policy.TrustedRoot.Digest)
            {
                throw new InvalidDataException("Protected verifier or independent Sigstore root changed.");
            }
            string root = Path.GetDirectoryName(policy.Tool.Path)!;
            string version = await runner.RunAsync(
                root, policy.Tool.Path, ["--version"], cancellationToken).ConfigureAwait(false);
            if (!version.StartsWith("gh version " + policy.Tool.Version + " ", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Verifier executable version differs from its protected pin.");
            }
            string output;
            try
            {
                output = await runner.RunAsync(root, policy.Tool.Path,
                [
                    "attestation", "verify", Path.GetFullPath(subjectPath), "--bundle", Path.GetFullPath(bundlePath),
                    "--repo", authority.Repository, "--cert-identity", authority.CertificateIdentity,
                    "--cert-oidc-issuer", authority.Issuer,
                    "--signer-workflow", authority.Repository + "/" + authority.Workflow,
                    "--signer-digest", authority.DefinitionSha, "--source-ref", authority.Ref,
                    "--custom-trusted-root", policy.TrustedRoot.Path,
                    "--predicate-type", predicateType, "--deny-self-hosted-runners", "--format", "json"
                ], cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
            if (await files.DigestAsync(policy.Tool.Path, cancellationToken).ConfigureAwait(false) != policy.Tool.Digest ||
                await files.DigestAsync(policy.TrustedRoot.Path, cancellationToken).ConfigureAwait(false) !=
                    policy.TrustedRoot.Digest ||
                await files.DigestAsync(subjectPath, cancellationToken).ConfigureAwait(false) != subjectDigest ||
                await files.DigestAsync(bundlePath, cancellationToken).ConfigureAwait(false) != bundleDigest)
            {
                throw new InvalidDataException("Verifier inputs changed during signature verification.");
            }
            using var result = JsonDocument.Parse(output);
            if (result.RootElement.ValueKind != JsonValueKind.Array || result.RootElement.GetArrayLength() != 1)
            {
                return null;
            }
            JsonElement verification = result.RootElement[0].GetProperty("verificationResult");
            JsonElement statement = verification.GetProperty("statement");
            if (verification.GetProperty("verifiedTimestamps").GetArrayLength() == 0 ||
                verification.GetProperty("signature").GetProperty("certificate").ValueKind != JsonValueKind.Object ||
                statement.GetProperty("_type").GetString() != "https://in-toto.io/Statement/v1" ||
                statement.GetProperty("predicateType").GetString() != predicateType)
            {
                return null;
            }
            bool found = false;
            foreach (JsonElement subject in statement.GetProperty("subject").EnumerateArray())
            {
                found |= subject.TryGetProperty("digest", out JsonElement digests) &&
                    digests.TryGetProperty("sha256", out JsonElement digest) &&
                    "sha256:" + digest.GetString() == subjectDigest;
            }
            return found ? JsonDocument.Parse(statement.GetRawText()) : null;
        }
    }
}
