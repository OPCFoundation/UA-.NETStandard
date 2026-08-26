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
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using ObjectIds = Opc.Ua.ObjectIds;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Verifies the asynchronous path and the model source.
    /// </summary>
    [TestFixture]
    [Category("AIModelManagement")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class JobAndSourceTests
    {
        [Test]
        public async Task AnAsynchronousInferenceReturnsAJobThatLaterCarriesTheResultAsync()
        {
            using AINodeManager nm = await CreateAsync().ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            InvokeAsyncMethodStateResult started =
                await deployment.InvokeAsync!.OnCallAsync!(
                    nm.SystemContext,
                    deployment.InvokeAsync,
                    nm.PrimaryDeploymentId,
                    ByteString.From(Encoding.UTF8.GetBytes("{}")),
                    string.Empty,
                    "application/json",
                    ArrayOf<Opc.Ua.KeyValuePair>.Empty,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(started.Job, Is.Not.EqualTo(NodeId.Null));

            var job = nm.FindPredefinedNode<InferenceJobState>(started.Job);

            // Running before Halted: the caller has a NodeId to watch, and the
            // result belongs to the job rather than to the call that asked for it.
            Assert.That(
                job.CurrentState!.Id!.Value,
                Is.EqualTo(Opc.Ua.ObjectIds.ProgramStateMachineType_Running));

            await WaitForHaltedAsync(job).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(job.ResponsePayload!.Value.Length, Is.GreaterThan(0));
                Assert.That(job.ModelUsed!.Value, Is.Not.EqualTo(NodeId.Null));
                Assert.That(job.Progress!.Value, Is.EqualTo(100));
                Assert.That(job.FinishedAt, Is.Not.Null);
            });
        }

        [Test]
        public async Task AFailedJobHaltsAndRecordsWhyAsync()
        {
            var primary = new FakeInferenceBackend("primary") { Healthy = false };

            using AINodeManager nm = await AIServerTestHarness
                .CreateAsync(
                    new InferenceBackends(primary),
                    new AIOptions
                    {
                        EnableFallback = false,
                        AsyncInferenceDelay = TimeSpan.Zero
                    })
                .ConfigureAwait(false);

            var deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            InvokeAsyncMethodStateResult started =
                await deployment.InvokeAsync!.OnCallAsync!(
                    nm.SystemContext,
                    deployment.InvokeAsync,
                    nm.PrimaryDeploymentId,
                    ByteString.From(Encoding.UTF8.GetBytes("{}")),
                    string.Empty,
                    "application/json",
                    ArrayOf<Opc.Ua.KeyValuePair>.Empty,
                    CancellationToken.None).ConfigureAwait(false);

            var job = nm.FindPredefinedNode<InferenceJobState>(started.Job);
            await WaitForHaltedAsync(job).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                // Halted either way. Whether the inference succeeded is answered by
                // whether ResponsePayload or LastError is set, not by the state the
                // program ended in.
                Assert.That(job.LastError!.Value.Text, Is.Not.Empty);

                // The member is published either way, so a client can browse to it
                // and subscribe before the job finishes. On failure it carries a
                // null ByteString - absent rather than empty, because an empty
                // answer and no answer are different things and a client deciding
                // whether to retry needs to tell them apart.
                Assert.That(job.ResponsePayload, Is.Not.Null);
                Assert.That(job.ResponsePayload!.Value.IsNull, Is.True);
            });
        }

        [Test]
        public async Task TheSourceReportsWhatItCanReachAsync()
        {
            var primary = new FakeInferenceBackend("primary");
            primary.Models.Add(new BackendModel
            {
                Publisher = "contoso",
                Name = "weld-inspect",
                Version = "2.1.0"
            });

            using AINodeManager nm = await AIServerTestHarness
                .CreateAsync(
                    new InferenceBackends(primary),
                    new AIOptions { EnableFallback = false })
                .ConfigureAwait(false);

            ModelSourceState source = FindSource(nm);

            TestConnectionMethodStateResult probe = await source.TestConnection!.OnCallAsync!(
                nm.SystemContext,
                source.TestConnection,
                source.NodeId,
                CancellationToken.None).ConfigureAwait(false);

            ListModelsMethodStateResult listed = await source.ListModels!.OnCallAsync!(
                nm.SystemContext,
                source.ListModels,
                source.NodeId,
                string.Empty,
                0,
                ByteString.Empty,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(probe.Reachable, Is.True);
                Assert.That(source.Reachability!.Value, Is.EqualTo(ReachabilityEnum.Reachable));

                // Answered from the source, not from what this Server has already
                // imported: the question is what COULD be deployed.
                Assert.That(listed.Models.Count, Is.EqualTo(1));
                Assert.That(listed.Models[0].Name, Is.EqualTo("weld-inspect"));
            });
        }

        [Test]
        public async Task AnUnreachableSourceSaysSoAsync()
        {
            var primary = new FakeInferenceBackend("primary") { Reachable = false };

            using AINodeManager nm = await AIServerTestHarness
                .CreateAsync(
                    new InferenceBackends(primary),
                    new AIOptions { EnableFallback = false })
                .ConfigureAwait(false);

            ModelSourceState source = FindSource(nm);

            TestConnectionMethodStateResult probe = await source.TestConnection!.OnCallAsync!(
                nm.SystemContext,
                source.TestConnection,
                source.NodeId,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(probe.Reachable, Is.False);
                Assert.That(
                    source.Reachability!.Value,
                    Is.EqualTo(ReachabilityEnum.Unreachable));
                Assert.That(source.ConsecutiveFailures!.Value, Is.EqualTo(1u));
            });
        }

        private static Task<AINodeManager> CreateAsync()
        {
            return AIServerTestHarness.CreateAsync(
                new InferenceBackends(new FakeInferenceBackend("primary")),
                new AIOptions
                {
                    EnableFallback = false,
                    AsyncInferenceDelay = TimeSpan.Zero
                });
        }

        private static ModelSourceState FindSource(AINodeManager nm)
        {
            var root = nm.FindPredefinedNode<AiRootState>(nm.RootId);
            var children = new System.Collections.Generic.List<BaseInstanceState>();
            root.Sources!.GetChildren(nm.SystemContext, children);

            foreach (BaseInstanceState child in children)
            {
                if (child is ModelSourceState source)
                {
                    return source;
                }
            }

            throw new InvalidOperationException("No model source was published.");
        }

        /// <summary>
        /// Waits for the job to leave Running.
        /// </summary>
        /// <remarks>
        /// Polls rather than sleeping a fixed interval, so the test is neither
        /// flaky on a loaded machine nor slower than it needs to be on an idle one.
        /// </remarks>
        private static async Task WaitForHaltedAsync(InferenceJobState job)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (job.CurrentState!.Id!.Value == Opc.Ua.ObjectIds.ProgramStateMachineType_Halted)
                {
                    return;
                }

                await Task.Delay(20).ConfigureAwait(false);
            }

            Assert.Fail("The job did not reach Halted.");
        }
    }
}
