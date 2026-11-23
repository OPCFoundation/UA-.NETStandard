// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Verifies the learning job that accounts for submitted ground-truth samples.
    /// </summary>
    [TestFixture]
    [Category("AIModelManagement")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class AILearningJobTests
    {
        [Test]
        public async Task LearningJobIsIndexedByItsOwnNodeIdAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            NodeState? node = nm.IndexedNode(nm.LearningJobId);

            Assert.Multiple(() =>
            {
                Assert.That(nm.LearningJobId, Is.Not.EqualTo(NodeId.Null));
                Assert.That(node, Is.InstanceOf<LearningJobState>());
            });
        }

        [Test]
        public async Task DisabledLearningLoopOmitsTheLearningJobAsync()
        {
            using AINodeManager nm = await AIServerTestHarness
                .CreateAsync(
                    new InferenceBackends(new FakeInferenceBackend("primary")),
                    new AIOptions
                    {
                        EnableFallback = false,
                        EnableLearningLoop = false
                    })
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(nm.LearningJobId, Is.EqualTo(NodeId.Null));
                Assert.That(nm.CountIndexed<LearningJobState>(), Is.Zero);
            });
        }

        [Test]
        public async Task SamplesCollectedStartsAtZeroAndIncrementsAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            LearningJobState job = nm.FindPredefinedNode<LearningJobState>(nm.LearningJobId);

            Assert.That(job.SamplesCollected!.Value, Is.Zero);

            bool added = await nm.RecordLearningSampleAsync("sample-1").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.True);
                Assert.That(job.SamplesCollected.Value, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DuplicateSampleIdIncrementsOnlyOnceAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            bool first = await nm.RecordLearningSampleAsync("sample-1").ConfigureAwait(false);
            bool second = await nm.RecordLearningSampleAsync("sample-1").ConfigureAwait(false);
            LearningJobState job = nm.FindPredefinedNode<LearningJobState>(nm.LearningJobId);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.True);
                Assert.That(second, Is.False);
                Assert.That(job.SamplesCollected!.Value, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task NegativeExampleCountsExactlyLikePositiveExampleAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            bool positive = await nm
                .RecordLearningSampleAsync("positive-1", AILearningSampleKind.Positive)
                .ConfigureAwait(false);
            bool negative = await nm
                .RecordLearningSampleAsync("negative-1", AILearningSampleKind.Negative)
                .ConfigureAwait(false);
            LearningJobState job = nm.FindPredefinedNode<LearningJobState>(nm.LearningJobId);

            Assert.Multiple(() =>
            {
                Assert.That(positive, Is.True);
                Assert.That(negative, Is.True);
                Assert.That(job.SamplesCollected!.Value, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task ConcurrentSampleIncrementsDoNotLoseCountsAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            Task<bool>[] tasks = Enumerable
                .Range(0, 250)
                .Select(index => nm.RecordLearningSampleAsync($"sample-{index}").AsTask())
                .ToArray();

            bool[] added = await Task.WhenAll(tasks).ConfigureAwait(false);
            LearningJobState job = nm.FindPredefinedNode<LearningJobState>(nm.LearningJobId);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.All.True);
                Assert.That(job.SamplesCollected!.Value, Is.EqualTo(250));
            });
        }

        private static Task<AINodeManager> CreateAsync()
        {
            return AIServerTestHarness.CreateAsync(
                new InferenceBackends(new FakeInferenceBackend("primary")),
                new AIOptions { EnableFallback = false });
        }
    }
}
