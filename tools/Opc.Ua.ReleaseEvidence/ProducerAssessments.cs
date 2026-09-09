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

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Preserves a producer's partition of unmet controls into observed failures and pending verification.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Kind">The producer-assessment discriminator identifying the control-classification record.</param>
    /// <param name="Source">The actual checkout identity associated with the evidence.</param>
    /// <param name="Producer">The workflow, run, job, and tool identity that produced the evidence.</param>
    /// <param name="Release">The artifact group, release version, and delivery channel covered by this record.</param>
    /// <param name="Artifacts">The exact artifacts and target scopes covered by this record.</param>
    /// <param name="UnmetControls">The controls not yet satisfied by the available evidence.</param>
    /// <param name="PendingControls">
    /// The unmet controls that still require independent verification rather than describing observed failures.
    /// </param>
    /// <param name="ObservedControls">The unmet controls classified as observed failures by the producer.</param>
    internal sealed record ProducerAssessment(
        int SchemaVersion, string Kind, SourceRecord Source, ProducerRecord Producer, ReleaseRecord Release,
        ArtifactRecord[] Artifacts, string[] UnmetControls, string[] PendingControls, string[] ObservedControls);

    /// <summary>
    /// Persists and retrieves producer control classifications bound to the evidence source and artifact set.
    /// </summary>
    internal static class ProducerAssessments
    {
        /// <summary>
        /// Writes available producer control classifications and attaches their content-addressed document to the
        /// envelope.
        /// </summary>
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

        /// <summary>
        /// Reads a unique matching producer assessment or retains the envelope's unpartitioned unmet controls.
        /// </summary>
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
