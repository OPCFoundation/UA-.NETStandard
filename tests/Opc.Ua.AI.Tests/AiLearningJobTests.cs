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
    [Category("AiModelManagement")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class AiLearningJobTests
    {
        [Test]
        public async Task LearningJobIsIndexedByItsOwnNodeIdAsync()
        {
            using AiNodeManager nm = await CreateAsync().ConfigureAwait(false);

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
            using AiNodeManager nm = await AiServerTestHarness
                .CreateAsync(
                    new InferenceBackends(new FakeInferenceBackend("primary")),
                    new AiOptions
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
            using AiNodeManager nm = await CreateAsync().ConfigureAwait(false);

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
            using AiNodeManager nm = await CreateAsync().ConfigureAwait(false);

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
            using AiNodeManager nm = await CreateAsync().ConfigureAwait(false);

            bool positive = await nm
                .RecordLearningSampleAsync("positive-1", AiLearningSampleKind.Positive)
                .ConfigureAwait(false);
            bool negative = await nm
                .RecordLearningSampleAsync("negative-1", AiLearningSampleKind.Negative)
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
            using AiNodeManager nm = await CreateAsync().ConfigureAwait(false);

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

        private static Task<AiNodeManager> CreateAsync()
        {
            return AiServerTestHarness.CreateAsync(
                new InferenceBackends(new FakeInferenceBackend("primary")),
                new AiOptions { EnableFallback = false });
        }
    }
}
