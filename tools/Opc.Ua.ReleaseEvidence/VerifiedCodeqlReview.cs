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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Json.Schema;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Represents an independent CodeQL review accepted by schema, authority, freshness, and signature checks.
    /// </summary>
    internal sealed class VerifiedCodeqlReview
    {
        private VerifiedCodeqlReview(CodeqlReviewRecord record, string digest)
        {
            Record = record;
            Digest = digest;
        }

        /// <summary>
        /// Gets the independently authenticated CodeQL review record.
        /// </summary>
        public CodeqlReviewRecord Record { get; }

        /// <summary>
        /// Gets the digest of the exact review-record file accepted during verification.
        /// </summary>
        public string Digest { get; }

        /// <summary>
        /// Authenticates a current, nonrevoked CodeQL review under pinned policy and rejects input changes during
        /// verification.
        /// </summary>
        public static async Task<VerifiedCodeqlReview?> VerifyAsync(
            string repositoryRoot,
            string bundleRoot,
            VerificationProof proof,
            TrustedPolicySnapshot policy,
            EvidenceFiles files,
            IRecordSignatureVerifier signatures,
            TimeProvider clock,
            CancellationToken cancellationToken)
        {
            const string schemaRelative = ".azurepipelines/codeql-review.schema.json";
            FrozenFile[] pins = [.. policy.ContractFiles.Where(f => f.Path == schemaRelative)];
            string schemaPath = EvidenceFiles.Confined(repositoryRoot, schemaRelative);
            if (pins.Length != 1 || !File.Exists(schemaPath) ||
                new FileInfo(schemaPath).Length != pins[0].Size ||
                await files.DigestAsync(schemaPath, cancellationToken).ConfigureAwait(false) != pins[0].Digest)
            {
                throw new InvalidDataException("The independent review schema is not protected by the current policy.");
            }
            string recordPath = EvidenceFiles.Confined(bundleRoot, proof.RecordPath);
            string bundlePath = EvidenceFiles.Confined(bundleRoot, proof.BundlePath);
            if (!File.Exists(recordPath) || !File.Exists(bundlePath))
            {
                return null;
            }
            using JsonDocument schemaDocument = await files.ReadJsonAsync(schemaPath, cancellationToken)
                .ConfigureAwait(false);
            JsonSchema schema = JsonSchema.FromText(schemaDocument.RootElement.GetRawText(), new BuildOptions
            {
                SchemaRegistry = new SchemaRegistry
                {
                    Fetch = (_, _) => throw new InvalidDataException("Unregistered offline review schema.")
                }
            });
            using JsonDocument document = await files.ReadJsonAsync(recordPath, cancellationToken).ConfigureAwait(false);
            if (!schema.Evaluate(document.RootElement, new EvaluationOptions
            {
                RequireFormatValidation = true
            }).IsValid)
            {
                throw new InvalidDataException("The independent review does not satisfy its closed schema.");
            }
            CodeqlReviewRecord record = document.Deserialize(VerificationJsonContext.Default.CodeqlReviewRecord)!;
            string digest = EvidenceFiles.Digest(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetRawText()));
            string fileDigest = await files.DigestAsync(recordPath, cancellationToken).ConfigureAwait(false);
            VerificationAuthority[] authorities = [.. policy.Authorities.Where(a =>
                a.Id == proof.AuthorityId && a.RecordKinds.Contains("codeql-disposition", StringComparer.Ordinal))];
            DateTimeOffset now = clock.GetUtcNow();
            if (authorities.Length != 1 || record.Kind != "codeql-disposition" ||
                record.Repository != authorities[0].Repository ||
                record.PolicyDigest != policy.PolicyDigest || record.CheckpointSequence != policy.CheckpointSequence ||
                policy.SchemaVersion != 1 || policy.CheckpointSequence < 1 ||
                policy.IssuedAt > now || policy.ExpiresAt <= now ||
                record.IssuedAt < policy.IssuedAt || record.IssuedAt > now ||
                record.ExpiresAt <= now || record.ExpiresAt > policy.ExpiresAt ||
                record.ExpiresAt <= record.IssuedAt ||
                policy.RevokedRecordIds.Contains(record.Id, StringComparer.Ordinal) ||
                record.Review.RunId != record.Producer.RunId || record.Review.Attempt != record.Producer.Attempt ||
                record.Review.ReviewedAlerts > record.Review.ReviewedOccurrences ||
                record.Review.UnresolvedFindings > record.Review.ReviewedAlerts ||
                !TrustedEvidenceVerifier.PinnedProducer(policy, record.Producer) ||
                !await signatures.VerifyAsync(
                    recordPath, bundlePath, authorities[0], policy, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
            using JsonDocument after = await files.ReadJsonAsync(recordPath, cancellationToken).ConfigureAwait(false);
            if (await files.DigestAsync(recordPath, cancellationToken).ConfigureAwait(false) != fileDigest ||
                EvidenceFiles.Digest(System.Text.Encoding.UTF8.GetBytes(after.RootElement.GetRawText())) != digest)
            {
                throw new InvalidDataException("The review changed during cryptographic verification.");
            }
            return new VerifiedCodeqlReview(record, fileDigest);
        }
    }
}
