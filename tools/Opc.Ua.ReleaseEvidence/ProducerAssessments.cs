// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed record ProducerAssessment(
        int SchemaVersion, string Kind, SourceRecord Source, ProducerRecord Producer, ReleaseRecord Release,
        ArtifactRecord[] Artifacts, string[] UnmetControls, string[] PendingControls, string[] ObservedControls);

    internal static class ProducerAssessments
    {
        public static async Task<EvidenceEnvelope> AttachAsync(
            EvidenceEnvelope envelope, string root, EvidenceFiles files, CancellationToken cancellationToken)
        {
            if (envelope.Assessment.PendingControls == null || envelope.Assessment.ObservedControls == null)
            {
                return envelope;
            }
            var record = new ProducerAssessment(
                1, "producer-assessment", envelope.Source, envelope.Producer, envelope.Release, envelope.Artifacts,
                envelope.Assessment.UnmetControls, envelope.Assessment.PendingControls,
                envelope.Assessment.ObservedControls);
            if (!Validate(record, envelope))
            {
                throw new InvalidDataException("Producer classifications refer to a different scope.");
            }
            const string path = "producer-assessment.json";
            await files.WriteModelAsync(
                EvidenceFiles.Confined(root, path), record,
                VerificationJsonContext.Default.ProducerAssessment, cancellationToken).ConfigureAwait(false);
            string digest = await files.DigestAsync(EvidenceFiles.Confined(root, path), cancellationToken)
                .ConfigureAwait(false);
            return envelope with
            {
                Documents =
                [
                    .. envelope.Documents,
                    new DocumentRecord("producer-record", "json", "1", path, digest,
                        new SubjectRecord("artifact-set", envelope.Release.Group,
                            VerificationControls.ArtifactSetDigest(envelope.Artifacts)))
                ]
            };
        }

        public static async Task<AssessmentRecord> ReadAsync(
            EvidenceEnvelope envelope, string root, EvidenceFiles files, CancellationToken cancellationToken)
        {
            ProducerAssessment? found = null;
            foreach (DocumentRecord document in envelope.Documents.Where(d =>
                d.Type == "producer-record" && d.Format == "json" && d.Version == "1"))
            {
                string path = EvidenceFiles.Confined(root, document.Path);
                if (!File.Exists(path) ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != document.Digest)
                {
                    continue;
                }
                using JsonDocument json = await files.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
                if (!json.RootElement.TryGetProperty("kind", out JsonElement kind) ||
                    kind.GetString() != "producer-assessment")
                {
                    continue;
                }
                ProducerAssessment candidate = json.Deserialize(VerificationJsonContext.Default.ProducerAssessment)!;
                if (!Validate(candidate, envelope))
                {
                    continue;
                }
                if (found != null)
                {
                    throw new InvalidDataException("Producer classifications must be unique.");
                }
                found = candidate;
            }
            return found == null
                ? new AssessmentRecord(envelope.Assessment.Status, envelope.Assessment.UnmetControls)
                : envelope.Assessment with
                {
                    PendingControls = found.PendingControls, ObservedControls = found.ObservedControls
                };
        }

        private static bool Validate(ProducerAssessment record, EvidenceEnvelope envelope)
        {
            if (record.SchemaVersion != 1 || record.Kind != "producer-assessment" ||
                record.PendingControls.Intersect(record.ObservedControls, StringComparer.Ordinal).Any() ||
                !record.PendingControls.Concat(record.ObservedControls).Order(StringComparer.Ordinal)
                    .SequenceEqual(record.UnmetControls.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw new InvalidDataException("Invalid producer control classification partition.");
            }
            return record.Source == envelope.Source &&
                VerificationControls.SameProducer(record.Producer, envelope.Producer) &&
                record.Release == envelope.Release &&
                VerificationControls.ArtifactSetDigest(record.Artifacts) ==
                    VerificationControls.ArtifactSetDigest(envelope.Artifacts) &&
                record.UnmetControls.Order(StringComparer.Ordinal).SequenceEqual(
                    envelope.Assessment.UnmetControls.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        }
    }
}
