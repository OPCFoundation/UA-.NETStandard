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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AI;
using BrowseNames = Opc.Ua.AI.BrowseNames;

namespace Opc.Ua.AI.Server
{
    public sealed partial class AINodeManager
    {
        /// <summary>
        /// How many sample identifiers are retained for duplicate detection.
        /// </summary>
        /// <remarks>
        /// A retry arrives close behind the call it repeats, so a window of this
        /// size is far more than a duplicate needs while keeping the set bounded on
        /// a Server that never restarts.
        /// </remarks>
        private const int MaxRetainedLearningSampleIds = 4096;

        /// <summary>
        /// Records one ground-truth sample against the published learning job.
        /// </summary>
        /// <param name="sampleId">Stable caller-supplied identity of the sample.</param>
        /// <param name="sampleKind">
        /// Whether the sample carried an observation or was a negative example.
        /// Both kinds increment <c>SamplesCollected</c> exactly once.
        /// </param>
        /// <param name="cancellationToken">Cancels the accounting operation.</param>
        /// <returns>
        /// True when this call added a new sample; false when the sample was already
        /// recorded or no learning job is published.
        /// </returns>
        /// <remarks>
        /// Duplicate detection is per-process and covers the most recent
        /// <see cref="MaxRetainedLearningSampleIds"/> identifiers. Both the counter
        /// and the set start empty after a restart, so a sample replayed across one
        /// is counted again.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public ValueTask<bool> RecordLearningSampleAsync(
            string sampleId,
            AILearningSampleKind sampleKind = AILearningSampleKind.Positive,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sampleId);
            cancellationToken.ThrowIfCancellationRequested();

            if (sampleKind is not AILearningSampleKind.Positive and not AILearningSampleKind.Negative)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleKind));
            }

            lock (m_sync)
            {
                if (m_learningJob is null || !m_learningSampleIds.Add(sampleId))
                {
                    return new ValueTask<bool>(false);
                }

                // Bounded, because the identifiers arrive from callers and a Server
                // that ran for a year would otherwise retain one string per sample
                // for no benefit. Evicting the oldest keeps the guarantee where it
                // matters - a retry follows its original closely - and the window is
                // stated rather than implied. Idempotency is per-process either way:
                // the count and the set are both rebuilt empty on restart.
                m_learningSampleOrder.Enqueue(sampleId);
                while (m_learningSampleOrder.Count > MaxRetainedLearningSampleIds)
                {
                    m_learningSampleIds.Remove(m_learningSampleOrder.Dequeue());
                }

                PropertyState<ulong> samples =
                    Child<PropertyState<ulong>>(m_learningJob, BrowseNames.SamplesCollected);
                samples.Value++;
                m_learningJob.ClearChangeMasks(SystemContext, true);
                return new ValueTask<bool>(true);
            }
        }

        /// <summary>
        /// Publishes the learning job that receives ground-truth sample counts.
        /// </summary>
        private void BuildLearningJob()
        {
            m_learningJob = new LearningJobState(null);
            m_learningJob.Create(
                SystemContext,
                NodeId.Null,
                new QualifiedName("LearningSamples", NamespaceIndex),
                new LocalizedText("Learning samples"),
                true);

            Child<PropertyState<string>>(m_learningJob, BrowseNames.JobId).Value =
                "learning-samples";
            Child<PropertyState<LearningJobStateEnum>>(m_learningJob, BrowseNames.State).Value =
                LearningJobStateEnum.Collecting;
            Child<PropertyState<NodeId>>(m_learningJob, BrowseNames.BaseModel).Value =
                m_primaryModel?.NodeId ?? NodeId.Null;
            Child<PropertyState<ulong>>(m_learningJob, BrowseNames.SamplesCollected).Value = 0;

            m_learningJob.CurrentState!.Value = new LocalizedText("Running");
            m_learningJob.CurrentState.Id!.Value =
                global::Opc.Ua.ObjectIds.ProgramStateMachineType_Running;

            Child<FolderState>(m_root!, BrowseNames.LearningJobs).AddChild(m_learningJob);
        }
    }
}
