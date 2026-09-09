// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed partial class VerifiedPromotion
    {
        public static Task<VerifiedPromotion> VerifyAsync(
            PromotionVerificationInput input, CancellationToken cancellationToken)
        {
            var files = new EvidenceFiles();
            return VerifyAsync(input, files, new TrustedEvidenceVerifier(files), null, cancellationToken);
        }

        internal static async Task<VerifiedPromotion> VerifyAsync(
            PromotionVerificationInput input,
            EvidenceFiles files,
            TrustedEvidenceVerifier verifier,
            IArtifactSignatureVerifier? artifactVerifier,
            CancellationToken cancellationToken)
        {
            EvidenceFiles.RejectLinks(input.Work);
            ProtectedTrustPolicySource.RequireOutside(
                Path.GetFullPath(input.Work), [input.CandidateRoot]);
            EvidenceEnvelope envelope = await files.ReadModelAsync(
                input.Evidence, EvidenceJsonContext.Default.EvidenceEnvelope, cancellationToken).ConfigureAwait(false);
            PromotionRequest request = await files.ReadModelAsync(
                input.Request, PromotionJsonContext.Default.PromotionRequest, cancellationToken).ConfigureAwait(false);
            string requestDigest = await files.DigestAsync(input.Request, cancellationToken).ConfigureAwait(false);
            await PromotionRequestValidator.ValidateAsync(request, input.CandidateRoot, files, cancellationToken)
                .ConfigureAwait(false);
            string assessmentPath = Path.Combine(input.Work, Guid.NewGuid().ToString("N") + ".assessment.json");
            int exit = await new EvidenceEvaluator(files, verifier, artifactVerifier).EvaluateAsync(
                input.RepositoryRoot, input.Evidence, input.Expected, assessmentPath,
                envelope.Release.Group == "nuget" ? input.CandidateRoot : null, cancellationToken,
                input.VerificationBundle, input.TrustPolicy).ConfigureAwait(false);
            EvaluationReport assessment = await files.ReadModelAsync(
                assessmentPath, EvidenceJsonContext.Default.EvaluationReport, cancellationToken).ConfigureAwait(false);
            VerifiedClaims claims = await verifier.VerifyAsync(
                input.RepositoryRoot, input.Evidence, envelope, input.VerificationBundle,
                input.TrustPolicy, input.CandidateRoot, cancellationToken).ConfigureAwait(false);
            string[] mandatory =
            [
                "EVIDENCE_SCHEMA", "SOURCE_IDENTITY", "PRODUCER_IDENTITY", "POLICY_IDENTITY",
                "RELEASE_INTENT", "ARTIFACT_MEMBERSHIP", "ARTIFACT_INTEGRITY", "EVIDENCE_FRESHNESS",
                "SIGNATURE_VERIFIED", "PUBLISHER_BOUNDARY"
            ];
            if (exit != 0 || assessment.BaselineFailed ||
                assessment.UnmetControls.Any(c => mandatory.Contains(c, StringComparer.Ordinal)) ||
                claims.Findings.Any(f => mandatory.Contains(f.Code, StringComparer.Ordinal)) ||
                claims.Policy == null || !claims.Has("release-intent") || !claims.Has("publication-boundary") ||
                envelope.Source.ActualRef.StartsWith("refs/pull/", StringComparison.Ordinal) ||
                envelope.Source.PullRequestHeadSha != null || envelope.Source.SyntheticMergeSha != null ||
                request.Group != envelope.Release.Group || request.SourceSha != envelope.Source.ActualSha ||
                request.RunId != envelope.Producer.RunId || request.Attempt != envelope.Producer.Attempt ||
                request.PolicyDigest != claims.Policy.PolicyDigest ||
                request.IntentDigest != claims.Policy.ExpectedIntentDigest ||
                claims.Records["publication-boundary"].PromotionRequestDigest != requestDigest ||
                request.EvidenceDigest !=
                    await files.DigestAsync(input.Evidence, cancellationToken).ConfigureAwait(false) ||
                request.CandidateDigest != VerificationControls.ArtifactSetDigest(envelope.Artifacts) ||
                !request.Members.Select(m => m.Content.Digest).Order(StringComparer.Ordinal).SequenceEqual(
                    envelope.Artifacts.Select(a => a.Digest).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !claims.Records["release-intent"].Scope.Destinations.Contains(
                    request.Destination, StringComparer.Ordinal) ||
                !claims.Records["publication-boundary"].Scope.Destinations.Contains(
                    request.Destination, StringComparer.Ordinal) ||
                request.Members.Any(m => m.Destination != request.Destination ||
                    envelope.Artifacts.Count(a => MatchesMember(m, a)) != 1) ||
                await files.DigestAsync(input.Request, cancellationToken).ConfigureAwait(false) != requestDigest)
            {
                throw new PromotionRejectedException(
                    "Current cryptographic authority and exact candidate membership are required.");
            }
            await VerifyClosureAsync(input, envelope, claims, request, files, cancellationToken).ConfigureAwait(false);
            await PromotionRequestValidator.ValidateAsync(request, input.CandidateRoot, files, cancellationToken)
                .ConfigureAwait(false);
            return new VerifiedPromotion(request, requestDigest, input.CandidateRoot, true);
        }

        private static async Task VerifyClosureAsync(
            PromotionVerificationInput input,
            EvidenceEnvelope envelope,
            VerifiedClaims claims,
            PromotionRequest request,
            EvidenceFiles files,
            CancellationToken cancellationToken)
        {
            var common = envelope.Documents.Select(d => d.Digest).ToHashSet(StringComparer.Ordinal);
            common.Add(request.EvidenceDigest);
            if (claims.NativeNuget != null)
            {
                common.UnionWith(claims.NativeNuget.BundleDigests);
            }
            OciImageClosure[] closures = [];
            if (envelope.Release.Group != "nuget")
            {
                VerificationBundle bundle = await files.ReadModelAsync(
                    input.VerificationBundle, VerificationJsonContext.Default.VerificationBundle, cancellationToken)
                    .ConfigureAwait(false);
                if (claims.BundleRoot == null || bundle.OciRequestPath == null)
                {
                    throw new PromotionRejectedException("Authenticated OCI layout and referrers are required.");
                }
                closures = await new OciClosureReader(files).ReadAuthenticatedAsync(
                    EvidenceFiles.Confined(claims.BundleRoot, bundle.OciRequestPath),
                    input.Evidence, envelope, claims, cancellationToken).ConfigureAwait(false);
            }
            foreach (PromotionMember member in request.Members)
            {
                var required = new HashSet<string>(common, StringComparer.Ordinal);
                PromotionAlias[] aliases = PromotionRequestValidator.GetAliases(member);
                if (envelope.Release.Group == "nuget")
                {
                    if (aliases.Length != 0)
                    {
                        throw new PromotionRejectedException("NuGet delivery does not authorize registry tag aliases.");
                    }
                }
                else
                {
                    OciImageClosure closure = closures.Single(c => c.Image == member.Id);
                    required.UnionWith(closure.Blobs.Select(b => b.Digest));
                    bool isRoot = member.Content.Digest == closure.RootDigest;
                    if ((!isRoot && aliases.Length != 0) ||
                        (isRoot && !aliases.Any(a => a.Immutable &&
                            a.Name == Versions.Parse(member.Version).ToNormalizedString())))
                    {
                        throw new PromotionRejectedException(
                            "Each OCI root needs its immutable version alias; runnable children cannot retag it.");
                    }
                }
                required.Remove(member.Content.Digest);
                if (!required.SetEquals(member.Evidence.Select(e => e.Digest)))
                {
                    throw new PromotionRejectedException(
                        "The request must preserve the complete authenticated document and native/referrer closure.");
                }
            }
        }

        private static bool MatchesMember(PromotionMember member, ArtifactRecord artifact)
        {
            return member.Id == artifact.Id && member.Kind == artifact.Kind &&
                member.Content.Digest == artifact.Digest && Versions.Equal(member.Version, artifact.Version) &&
                (member.Platform == null
                    ? artifact.Scopes.Platforms.Length == 0
                    : artifact.Scopes.Platforms.SequenceEqual([member.Platform], StringComparer.Ordinal));
        }
    }
}
