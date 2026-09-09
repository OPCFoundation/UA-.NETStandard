// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class VerifiedNativeNuget
    {
        private VerifiedNativeNuget(
            string evidenceDigest, string producerEvidenceDigest, bool producerBaselineFailed, string[] bundleDigests)
        {
            EvidenceDigest = evidenceDigest;
            ProducerEvidenceDigest = producerEvidenceDigest;
            ProducerBaselineFailed = producerBaselineFailed;
            BundleDigests = bundleDigests;
        }

        public string EvidenceDigest { get; }

        public string ProducerEvidenceDigest { get; }

        public bool ProducerBaselineFailed { get; }

        public string[] BundleDigests { get; }

        public static async Task<VerifiedNativeNuget?> VerifyAsync(
            string repositoryRoot,
            string evidencePath,
            string artifactRoot,
            string bundleRoot,
            EvidenceEnvelope envelope,
            NativeNugetProof[] proofs,
            TrustedPolicySnapshot policy,
            EvidenceFiles files,
            IStatementSignatureVerifier signatures,
            CancellationToken cancellationToken)
        {
            if (envelope.Release.Group != "nuget" ||
                !proofs.Select(p => p.Role).Order(StringComparer.Ordinal)
                    .SequenceEqual(kRoles, StringComparer.Ordinal) ||
                !TrustedEvidenceVerifier.PinnedProducer(policy, envelope.Producer))
            {
                return null;
            }
            Dictionary<string, string> archives = await files.IndexArchivesAsync(artifactRoot, cancellationToken)
                .ConfigureAwait(false);
            string evidenceDigest = await files.DigestAsync(evidencePath, cancellationToken).ConfigureAwait(false);
            NativeNugetProof index = proofs.Single(p => p.Role == "index");
            string producerPath = index.IndexPath == null ? evidencePath :
                EvidenceFiles.Confined(bundleRoot, index.IndexPath);
            string producerDigest = await files.DigestAsync(producerPath, cancellationToken).ConfigureAwait(false);
            using JsonDocument producerJson = await files.ReadJsonAsync(producerPath, cancellationToken)
                .ConfigureAwait(false);
            VerificationSchemas schemas = await VerificationSchemas.LoadAsync(repositoryRoot, files, cancellationToken)
                .ConfigureAwait(false);
            schemas.Validate("release-evidence.schema.json", producerJson.RootElement);
            EvidenceEnvelope producer = producerJson.Deserialize(EvidenceJsonContext.Default.EvidenceEnvelope)!;
            if (producer.Source != envelope.Source ||
                !VerificationControls.SameProducer(producer.Producer, envelope.Producer) ||
                producer.Release != envelope.Release ||
                VerificationControls.ArtifactSetDigest(producer.Artifacts) !=
                    VerificationControls.ArtifactSetDigest(envelope.Artifacts) ||
                !producer.Assessment.UnmetControls.Order(StringComparer.Ordinal).SequenceEqual(
                    envelope.Assessment.UnmetControls.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                producer.Documents.Any(d => !envelope.Documents.Contains(d)) ||
                (producerDigest != evidenceDigest &&
                    !envelope.Documents.Any(d => d.Type == "producer-record" && d.Format == "json" &&
                        d.Version == "2" && d.Digest == producerDigest)))
            {
                return null;
            }
            foreach (NativeNugetProof proof in proofs)
            {
                string path = EvidenceFiles.Confined(bundleRoot, proof.BundlePath);
                string kind = proof.Role == "index" ? "native-nuget-index" : "native-nuget-pack";
                VerificationAuthority[] authorities = [.. policy.Authorities.Where(a =>
                    a.Id == proof.AuthorityId && a.RecordKinds.Contains(kind, StringComparer.Ordinal) &&
                    a.Repository == envelope.Source.Repository && a.Workflow == envelope.Producer.Workflow &&
                    a.DefinitionSha == envelope.Producer.DefinitionSha && a.Ref == envelope.Source.ActualRef)];
                if (authorities.Length != 1 || !VerificationControls.IsDigest(proof.Digest) ||
                    !File.Exists(path) ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != proof.Digest)
                {
                    return null;
                }
                ArtifactRecord[] subjects = proof.Role == "index" ? [] :
                    [.. envelope.Artifacts.Where(a => a.Configuration == proof.Role)];
                if (proof.Role != "index" && (subjects.Length == 0 ||
                    subjects.Any(a => !archives.ContainsKey(a.Digest))))
                {
                    return null;
                }
                string subjectPath = proof.Role == "index" ? producerPath : archives[subjects[0].Digest];
                string predicateType = proof.Role == "index" ? IndexPredicateType : PackPredicateType;
                using JsonDocument? statement = await signatures.VerifyAsync(
                    subjectPath, path, predicateType, authorities[0], policy, cancellationToken).ConfigureAwait(false);
                if (statement == null || statement.RootElement.GetProperty("_type").GetString() !=
                        "https://in-toto.io/Statement/v1" ||
                    statement.RootElement.GetProperty("predicateType").GetString() != predicateType)
                {
                    return null;
                }
                JsonElement signedSubjects = statement.RootElement.GetProperty("subject");
                if (proof.Role == "index")
                {
                    if (signedSubjects.GetArrayLength() != 1 ||
                        !MatchesSubject(signedSubjects[0], Path.GetFileName(producerPath), producerDigest))
                    {
                        return null;
                    }
                    EvaluationExpectation context = statement.RootElement.GetProperty("predicate")
                        .Deserialize(EvidenceJsonContext.Default.EvaluationExpectation)!;
                    if (context.Source != envelope.Source || !context.Source.TrackedClean ||
                        !VerificationControls.SameProducer(context.Producer, envelope.Producer) ||
                        context.Release != envelope.Release || context.PolicyDigest != producer.Policy.Digest ||
                        (context.Artifacts.Length != 0 &&
                            VerificationControls.ArtifactSetDigest(context.Artifacts) !=
                                VerificationControls.ArtifactSetDigest(envelope.Artifacts)))
                    {
                        return null;
                    }
                }
                else if (signedSubjects.GetArrayLength() != subjects.Length ||
                    subjects.Any(a => signedSubjects.EnumerateArray().Count(s =>
                        MatchesSubject(s, Path.GetFileName(archives[a.Digest]), a.Digest)) != 1) ||
                    !MatchesPack(statement.RootElement.GetProperty("predicate"), proof.Role, envelope))
                {
                    return null;
                }
                if (await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != proof.Digest)
                {
                    throw new InvalidDataException("Native proof bytes changed during verification.");
                }
            }
            if (await files.DigestAsync(evidencePath, cancellationToken).ConfigureAwait(false) != evidenceDigest)
            {
                throw new InvalidDataException("The native index changed during verification.");
            }
            bool producerFailed = producer.Assurance.Failed > 0 || producer.Assurance.Jobs.Any(j =>
                j.Status == "failed" || j.Counts?.Failed > 0 || j.Counts?.UnresolvedFindings > 0 ||
                (j.Selected && j.Counts?.Executed == 0));
            return new VerifiedNativeNuget(
                evidenceDigest, producerDigest, producerFailed, [.. proofs.Select(p => p.Digest)]);
        }

        private static bool MatchesSubject(JsonElement subject, string name, string digest)
        {
            return subject.GetProperty("name").GetString() == name &&
                subject.GetProperty("digest").EnumerateObject().Count() == 1 &&
                "sha256:" + subject.GetProperty("digest").GetProperty("sha256").GetString() == digest;
        }

        private static bool MatchesPack(JsonElement predicate, string configuration, EvidenceEnvelope envelope)
        {
            JsonElement definition = predicate.GetProperty("buildDefinition");
            JsonElement parameters = definition.GetProperty("externalParameters");
            JsonElement materials = definition.GetProperty("resolvedDependencies");
            JsonElement details = predicate.GetProperty("runDetails");
            string repository = "https://github.com/" + envelope.Source.Repository;
            return definition.GetProperty("buildType").GetString() == PackBuildType &&
                parameters.GetProperty("configuration").GetString() == configuration &&
                Versions.Equal(parameters.GetProperty("version").GetString()!, envelope.Release.Version) &&
                parameters.EnumerateObject().Count() == 2 &&
                !definition.GetProperty("internalParameters").EnumerateObject().Any() &&
                materials.GetArrayLength() == 1 &&
                materials[0].GetProperty("uri").GetString() == "git+" + repository + "@" + envelope.Source.ActualRef &&
                materials[0].GetProperty("digest").GetProperty("gitCommit").GetString() == envelope.Source.ActualSha &&
                details.GetProperty("builder").GetProperty("id").GetString() ==
                    repository + "/" + envelope.Producer.Workflow + "@" + envelope.Producer.DefinitionSha &&
                details.GetProperty("metadata").GetProperty("invocationId").GetString() ==
                    repository + "/actions/runs/" + envelope.Producer.RunId + "/attempts/" +
                        envelope.Producer.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal const string IndexPredicateType =
            "https://github.com/OPCFoundation/UA-.NETStandard/release-evidence/v1";
        internal const string PackPredicateType = "https://slsa.dev/provenance/v1";
        internal const string PackBuildType = "https://github.com/OPCFoundation/UA-.NETStandard/nuget-pack/v1";
        private static readonly string[] kRoles = ["Debug", "Release", "index"];
    }
}
