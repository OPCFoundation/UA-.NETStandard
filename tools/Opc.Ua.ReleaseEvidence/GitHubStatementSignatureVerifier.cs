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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Authenticates preserved in-toto statements using pinned GitHub tooling, signer policy, and independent roots.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    /// <param name="runner">The service for bounded external tool execution.</param>
    internal sealed class GitHubStatementSignatureVerifier(
        EvidenceFiles files, ProcessRunner runner) : IStatementSignatureVerifier
    {
        /// <summary>
        /// Returns a verified statement bound to the supplied subject and predicate, or null when verification fails.
        /// </summary>
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
