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
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AI.Client;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Tests for the lightweight AI sub-client types that each wrap one OPC UA
    /// type instance: <see cref="AIDatasetClient"/>, <see cref="AIInferenceJobClient"/>,
    /// <see cref="AIEvaluationRunClient"/>, <see cref="AIModelSourceClient"/>,
    /// <see cref="AILearningJobClient"/>, and <see cref="AIInferenceTransferClient"/>.
    /// </summary>
    [TestFixture]
    [Category("AI")]
    [Category("Client")]
    public sealed class AISubClientTests
    {
        private static readonly NodeId s_nodeId = new(5000u, 3);

        [Test]
        public void DatasetConstructorWithNullClientThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AIDatasetClient((AIClient)null!, s_nodeId));
        }

        [Test]
        public void DatasetConstructorWithNullNodeIdThrows()
        {
            var h = new AISessionHarness();
            Assert.Throws<ArgumentException>(
                () => new AIDatasetClient(h.Client, NodeId.Null));
        }

        [Test]
        public async Task DatasetReadAsyncReturnsSnapshotAsync()
        {
            var h = new AISessionHarness();
            h.AddValueChild(s_nodeId, BrowseNames.DatasetId, new NodeId(5001u, 3), "ds-1");
            h.AddValueChild(s_nodeId, BrowseNames.Name, new NodeId(5002u, 3), "training");
            h.AddValueChild(
                s_nodeId,
                BrowseNames.SourceKind,
                new NodeId(5003u, 3),
                (int)DatasetSourceEnum.Synthetic);
            h.AddValueChild(s_nodeId, BrowseNames.ArtifactUri, new NodeId(5004u, 3), "https://blob/ds1");
            h.AddValueChild(s_nodeId, BrowseNames.ContentType, new NodeId(5005u, 3), "application/json");
            h.AddValueChild(s_nodeId, BrowseNames.SizeBytes, new NodeId(5006u, 3), (ulong)4096);
            h.AddValueChild(s_nodeId, BrowseNames.SampleCount, new NodeId(5007u, 3), (uint)100);

            AIDatasetSnapshot snapshot = await h.Client.Dataset(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.DatasetId, Is.EqualTo("ds-1"));
                Assert.That(snapshot.Name, Is.EqualTo("training"));
                Assert.That(snapshot.SourceKind, Is.EqualTo(DatasetSourceEnum.Synthetic));
                Assert.That(snapshot.ArtifactUri, Is.EqualTo("https://blob/ds1"));
                Assert.That(snapshot.ContentType, Is.EqualTo("application/json"));
                Assert.That(snapshot.SizeBytes, Is.EqualTo(4096));
                Assert.That(snapshot.SampleCount, Is.EqualTo(100));
            });
        }

        [Test]
        public async Task DatasetReadAsyncWithMissingChildrenReturnsDefaultsAsync()
        {
            var h = new AISessionHarness();

            AIDatasetSnapshot snapshot = await h.Client.Dataset(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.DatasetId, Is.Null);
                Assert.That(snapshot.Name, Is.Null);
                Assert.That(snapshot.SizeBytes, Is.Zero);
                Assert.That(snapshot.SampleCount, Is.Zero);
            });
        }

        [Test]
        public void InferenceJobConstructorWithNullClientThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AIInferenceJobClient((AIClient)null!, s_nodeId));
        }

        [Test]
        public void InferenceJobConstructorWithNullNodeIdThrows()
        {
            var h = new AISessionHarness();
            Assert.Throws<ArgumentException>(
                () => new AIInferenceJobClient(h.Client, NodeId.Null));
        }

        [Test]
        public async Task InferenceJobReadAsyncReturnsSnapshotAsync()
        {
            var h = new AISessionHarness();
            h.AddValueChild(s_nodeId, BrowseNames.JobId, new NodeId(5010u, 3), "job-42");
            h.AddValueChild(s_nodeId, BrowseNames.Deployment, new NodeId(5011u, 3), new NodeId(100u, 3));
            h.AddValueChild(s_nodeId, BrowseNames.ResponsePayload, new NodeId(5012u, 3), ByteString.From(new byte[] { 0xCA, 0xFE }));
            h.AddValueChild(s_nodeId, BrowseNames.ResponseContentType, new NodeId(5013u, 3), "text/plain");
            h.AddValueChild(s_nodeId, BrowseNames.ModelUsed, new NodeId(5014u, 3), new NodeId(200u, 3));
            h.AddValueChild(
                s_nodeId,
                BrowseNames.FinishReason,
                new NodeId(5015u, 3),
                (int)FinishReasonEnum.Error);

            AIInferenceJobSnapshot snapshot = await h.Client.InferenceJob(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.JobId, Is.EqualTo("job-42"));
                Assert.That(snapshot.ResponseContentType, Is.EqualTo("text/plain"));
                Assert.That(snapshot.ResponsePayload.Length, Is.EqualTo(2));
                Assert.That(snapshot.FinishReason, Is.EqualTo(FinishReasonEnum.Error));
            });
        }

        [Test]
        public async Task InferenceJobReadAsyncMissingChildrenReturnsDefaultsAsync()
        {
            var h = new AISessionHarness();

            AIInferenceJobSnapshot snapshot = await h.Client.InferenceJob(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.JobId, Is.Null);
                Assert.That(snapshot.DeploymentId, Is.EqualTo(NodeId.Null));
                Assert.That(snapshot.ModelUsed, Is.EqualTo(NodeId.Null));
            });
        }

        [Test]
        public void EvaluationRunConstructorWithNullClientThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AIEvaluationRunClient((AIClient)null!, s_nodeId));
        }

        [Test]
        public void EvaluationRunConstructorWithNullNodeIdThrows()
        {
            var h = new AISessionHarness();
            Assert.Throws<ArgumentException>(
                () => new AIEvaluationRunClient(h.Client, NodeId.Null));
        }

        [Test]
        public async Task EvaluationRunReadAsyncReturnsSnapshotAsync()
        {
            var h = new AISessionHarness();
            h.AddValueChild(s_nodeId, BrowseNames.RunId, new NodeId(5020u, 3), "run-7");
            h.AddValueChild(s_nodeId, BrowseNames.EvaluatedModel, new NodeId(5021u, 3), new NodeId(300u, 3));
            h.AddValueChild(s_nodeId, BrowseNames.Passed, new NodeId(5022u, 3), true);
            h.AddValueChild(s_nodeId, BrowseNames.ReportUri, new NodeId(5024u, 3), "https://reports/7");

            AIEvaluationRunSnapshot snapshot = await h.Client.EvaluationRun(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.RunId, Is.EqualTo("run-7"));
                Assert.That(snapshot.Passed, Is.True);
                Assert.That(snapshot.ReportUri, Is.EqualTo("https://reports/7"));
            });
        }

        [Test]
        public async Task EvaluationRunReadAsyncMissingChildrenReturnsDefaultsAsync()
        {
            var h = new AISessionHarness();

            AIEvaluationRunSnapshot snapshot = await h.Client.EvaluationRun(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.RunId, Is.Null);
                Assert.That(snapshot.Passed, Is.False);
                Assert.That(snapshot.EvaluatedModelId, Is.EqualTo(NodeId.Null));
            });
        }

        [Test]
        public void LearningJobConstructorWithNullClientThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AILearningJobClient((AIClient)null!, s_nodeId));
        }

        [Test]
        public void LearningJobConstructorWithNullNodeIdThrows()
        {
            var h = new AISessionHarness();
            Assert.Throws<ArgumentException>(
                () => new AILearningJobClient(h.Client, NodeId.Null));
        }

        [Test]
        public async Task LearningJobReadAsyncReturnsSnapshotAsync()
        {
            var h = new AISessionHarness();
            h.AddValueChild(s_nodeId, BrowseNames.JobId, new NodeId(5030u, 3), "learn-3");
            h.AddValueChild(
                s_nodeId,
                BrowseNames.State,
                new NodeId(5031u, 3),
                (int)LearningJobStateEnum.Training);
            h.AddValueChild(s_nodeId, BrowseNames.Progress, new NodeId(5032u, 3), 0.75);
            h.AddValueChild(s_nodeId, BrowseNames.CandidateModel, new NodeId(5033u, 3), new NodeId(400u, 3));
            h.AddValueChild(s_nodeId, BrowseNames.TargetDeployment, new NodeId(5034u, 3), new NodeId(500u, 3));

            AILearningJobSnapshot snapshot = await h.Client.LearningJob(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.JobId, Is.EqualTo("learn-3"));
                Assert.That(snapshot.State, Is.EqualTo(LearningJobStateEnum.Training));
                Assert.That(snapshot.Progress, Is.EqualTo(0.75));
            });
        }

        [Test]
        public async Task LearningJobReadAsyncMissingChildrenReturnsDefaultsAsync()
        {
            var h = new AISessionHarness();

            AILearningJobSnapshot snapshot = await h.Client.LearningJob(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.JobId, Is.Null);
                Assert.That(snapshot.Progress, Is.Zero);
                Assert.That(snapshot.CandidateModelId, Is.EqualTo(NodeId.Null));
                Assert.That(snapshot.TargetDeploymentId, Is.EqualTo(NodeId.Null));
            });
        }

        [Test]
        public void ModelSourceConstructorWithNullClientThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AIModelSourceClient((AIClient)null!, s_nodeId));
        }

        [Test]
        public void ModelSourceConstructorWithNullNodeIdThrows()
        {
            var h = new AISessionHarness();
            Assert.Throws<ArgumentException>(
                () => new AIModelSourceClient(h.Client, NodeId.Null));
        }

        [Test]
        public async Task ModelSourceReadAsyncReturnsSnapshotAsync()
        {
            var h = new AISessionHarness();
            h.AddValueChild(s_nodeId, BrowseNames.SourceId, new NodeId(5040u, 3), "src-1");
            h.AddValueChild(s_nodeId, BrowseNames.EndpointUri, new NodeId(5041u, 3), "https://model-store.example.com");
            h.AddValueChild(
                s_nodeId,
                BrowseNames.ApiDialect,
                new NodeId(5042u, 3),
                (int)ApiDialectEnum.OpenInferenceProtocol);
            h.AddValueChild(
                s_nodeId,
                BrowseNames.AuthenticationKind,
                new NodeId(5043u, 3),
                (int)AuthenticationKindEnum.BearerToken);
            h.AddValueChild(s_nodeId, BrowseNames.CredentialReference, new NodeId(5044u, 3), "vault://key1");

            AIModelSourceSnapshot snapshot = await h.Client.Source(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.SourceId, Is.EqualTo("src-1"));
                Assert.That(snapshot.EndpointUri, Is.EqualTo("https://model-store.example.com"));
                Assert.That(snapshot.ApiDialect, Is.EqualTo(ApiDialectEnum.OpenInferenceProtocol));
                Assert.That(snapshot.AuthenticationKind, Is.EqualTo(AuthenticationKindEnum.BearerToken));
                Assert.That(snapshot.CredentialReference, Is.EqualTo("vault://key1"));
            });
        }

        [Test]
        public async Task ModelSourceReadAsyncMissingChildrenReturnsDefaultsAsync()
        {
            var h = new AISessionHarness();

            AIModelSourceSnapshot snapshot = await h.Client.Source(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.SourceId, Is.Null);
                Assert.That(snapshot.EndpointUri, Is.Null);
            });
        }

        [Test]
        public void InferenceTransferConstructorWithNullClientThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AIInferenceTransferClient((AIClient)null!, s_nodeId));
        }

        [Test]
        public void InferenceTransferConstructorWithNullNodeIdThrows()
        {
            var h = new AISessionHarness();
            Assert.Throws<ArgumentException>(
                () => new AIInferenceTransferClient(h.Client, NodeId.Null));
        }

        [Test]
        public async Task InferenceTransferReadAsyncReturnsSnapshotAsync()
        {
            var h = new AISessionHarness();
            h.AddValueChild(s_nodeId, BrowseNames.TransferId, new NodeId(5050u, 3), "xfer-1");
            h.AddValueChild(
                s_nodeId,
                BrowseNames.State,
                new NodeId(5051u, 3),
                (int)TransferStateEnum.Completed);
            h.AddValueChild(s_nodeId, BrowseNames.BytesTransferred, new NodeId(5052u, 3), (ulong)2048);
            h.AddValueChild(s_nodeId, BrowseNames.ModelUsed, new NodeId(5053u, 3), new NodeId(600u, 3));
            h.AddValueChild(s_nodeId, BrowseNames.ResponseContentType, new NodeId(5054u, 3), "application/octet-stream");

            AITransferSnapshot snapshot = await h.Client.Transfer(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.TransferId, Is.EqualTo("xfer-1"));
                Assert.That(snapshot.State, Is.EqualTo(TransferStateEnum.Completed));
                Assert.That(snapshot.BytesTransferred, Is.EqualTo(2048));
                Assert.That(snapshot.ResponseContentType, Is.EqualTo("application/octet-stream"));
            });
        }

        [Test]
        public async Task InferenceTransferReadAsyncMissingChildrenReturnsDefaultsAsync()
        {
            var h = new AISessionHarness();

            AITransferSnapshot snapshot = await h.Client.Transfer(s_nodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.TransferId, Is.Null);
                Assert.That(snapshot.BytesTransferred, Is.Zero);
                Assert.That(snapshot.ModelUsed, Is.EqualTo(NodeId.Null));
            });
        }

        [Test]
        public void ClientDatasetFactoryCreatesClient()
        {
            var h = new AISessionHarness();
            AIDatasetClient ds = h.Client.Dataset(s_nodeId);
            Assert.That(ds.DatasetNodeId, Is.EqualTo(s_nodeId));
        }

        [Test]
        public void ClientInferenceJobFactoryCreatesClient()
        {
            var h = new AISessionHarness();
            AIInferenceJobClient j = h.Client.InferenceJob(s_nodeId);
            Assert.That(j.JobNodeId, Is.EqualTo(s_nodeId));
        }

        [Test]
        public void ClientLearningJobFactoryCreatesClient()
        {
            var h = new AISessionHarness();
            AILearningJobClient lj = h.Client.LearningJob(s_nodeId);
            Assert.That(lj.JobNodeId, Is.EqualTo(s_nodeId));
        }

        [Test]
        public void ClientEvaluationRunFactoryCreatesClient()
        {
            var h = new AISessionHarness();
            AIEvaluationRunClient er = h.Client.EvaluationRun(s_nodeId);
            Assert.That(er.RunNodeId, Is.EqualTo(s_nodeId));
        }

        [Test]
        public void ClientSourceFactoryCreatesClient()
        {
            var h = new AISessionHarness();
            AIModelSourceClient src = h.Client.Source(s_nodeId);
            Assert.That(src.SourceNodeId, Is.EqualTo(s_nodeId));
        }

        [Test]
        public void ClientTransferFactoryCreatesClient()
        {
            var h = new AISessionHarness();
            AIInferenceTransferClient t = h.Client.Transfer(s_nodeId);
            Assert.That(t.TransferNodeId, Is.EqualTo(s_nodeId));
        }

        [Test]
        public async Task DiscoverDatasetsReturnsNodesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Datasets, new NodeId(6000u, 3));
            h.AddBrowse(new NodeId(6000u, 3), [h.Ref(s_nodeId, "Dataset1", ObjectTypes.DatasetType)]);

            ArrayOf<NodeId> nodes = await h.Client.DiscoverDatasetsAsync().ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(1));
            Assert.That(nodes[0], Is.EqualTo(s_nodeId));
        }

        [Test]
        public async Task DiscoverLearningJobsReturnsNodesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.LearningJobs, new NodeId(6100u, 3));
            h.AddBrowse(new NodeId(6100u, 3), [h.Ref(s_nodeId, "Job1", ObjectTypes.LearningJobType)]);

            ArrayOf<NodeId> nodes = await h.Client.DiscoverLearningJobsAsync().ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task DiscoverInferenceJobsReturnsNodesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Jobs, new NodeId(6200u, 3));
            h.AddBrowse(new NodeId(6200u, 3), [h.Ref(s_nodeId, "InfJob1", ObjectTypes.InferenceJobType)]);

            ArrayOf<NodeId> nodes = await h.Client.DiscoverInferenceJobsAsync().ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task DiscoverEvaluationRunsReturnsNodesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Evaluations, new NodeId(6300u, 3));
            h.AddBrowse(new NodeId(6300u, 3), [h.Ref(s_nodeId, "Run1", ObjectTypes.EvaluationRunType)]);

            ArrayOf<NodeId> nodes = await h.Client.DiscoverEvaluationRunsAsync().ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task DiscoverSourcesReturnsNodesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Sources, new NodeId(6400u, 3));
            h.AddBrowse(new NodeId(6400u, 3), [h.Ref(s_nodeId, "Src1", ObjectTypes.ModelSourceType)]);

            ArrayOf<NodeId> nodes = await h.Client.DiscoverSourcesAsync().ConfigureAwait(false);

            Assert.That(nodes.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task EnumerateDatasetsYieldsEntriesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Datasets, new NodeId(7000u, 3));
            h.AddBrowse(new NodeId(7000u, 3), [h.Ref(s_nodeId, "Ds1", ObjectTypes.DatasetType)]);

            var entries = new List<AINodeEntry>();
            await foreach (AINodeEntry entry in h.Client.EnumerateDatasetsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].BrowseName.Name, Is.EqualTo("Ds1"));
        }

        [Test]
        public async Task EnumerateLearningJobsYieldsEntriesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.LearningJobs, new NodeId(7100u, 3));
            h.AddBrowse(new NodeId(7100u, 3), [h.Ref(s_nodeId, "LJ1", ObjectTypes.LearningJobType)]);

            var entries = new List<AINodeEntry>();
            await foreach (AINodeEntry entry in h.Client.EnumerateLearningJobsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task EnumerateInferenceJobsYieldsEntriesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Jobs, new NodeId(7200u, 3));
            h.AddBrowse(new NodeId(7200u, 3), [h.Ref(s_nodeId, "IJ1", ObjectTypes.InferenceJobType)]);

            var entries = new List<AINodeEntry>();
            await foreach (AINodeEntry entry in h.Client.EnumerateInferenceJobsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task EnumerateEvaluationRunsYieldsEntriesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Evaluations, new NodeId(7300u, 3));
            h.AddBrowse(new NodeId(7300u, 3), [h.Ref(s_nodeId, "ER1", ObjectTypes.EvaluationRunType)]);

            var entries = new List<AINodeEntry>();
            await foreach (AINodeEntry entry in h.Client.EnumerateEvaluationRunsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task EnumerateSourcesYieldsEntriesAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Sources, new NodeId(7400u, 3));
            h.AddBrowse(new NodeId(7400u, 3), [h.Ref(s_nodeId, "S1", ObjectTypes.ModelSourceType)]);

            var entries = new List<AINodeEntry>();
            await foreach (AINodeEntry entry in h.Client.EnumerateSourcesAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(1));
        }

        [Test]
        public void DatasetsFolderIdUsesWellKnownNode()
        {
            var h = new AISessionHarness();
            Assert.That(h.Client.DatasetsFolderId, Is.Not.EqualTo(NodeId.Null));
        }

        [Test]
        public void LearningJobsFolderIdUsesWellKnownNode()
        {
            var h = new AISessionHarness();
            Assert.That(h.Client.LearningJobsFolderId, Is.Not.EqualTo(NodeId.Null));
        }

        [Test]
        public async Task GetJobsFolderIdAsyncResolvesChildAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Jobs, new NodeId(8000u, 3));

            NodeId folderId = await h.Client.GetJobsFolderIdAsync().ConfigureAwait(false);

            Assert.That(folderId, Is.EqualTo(new NodeId(8000u, 3)));
        }

        [Test]
        public async Task GetSourcesFolderIdAsyncResolvesChildAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Sources, new NodeId(8100u, 3));

            NodeId folderId = await h.Client.GetSourcesFolderIdAsync().ConfigureAwait(false);

            Assert.That(folderId, Is.EqualTo(new NodeId(8100u, 3)));
        }

        [Test]
        public async Task GetEvaluationsFolderIdAsyncResolvesChildAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Evaluations, new NodeId(8200u, 3));

            NodeId folderId = await h.Client.GetEvaluationsFolderIdAsync().ConfigureAwait(false);

            Assert.That(folderId, Is.EqualTo(new NodeId(8200u, 3)));
        }

        [Test]
        public async Task GetRegistriesFolderIdAsyncResolvesChildAsync()
        {
            var h = new AISessionHarness();
            h.AddChild(h.AIRootId, BrowseNames.Registries, new NodeId(8300u, 3));

            NodeId folderId = await h.Client.GetRegistriesFolderIdAsync().ConfigureAwait(false);

            Assert.That(folderId, Is.EqualTo(new NodeId(8300u, 3)));
        }

        [Test]
        public async Task ModelEnumerateResourcesYieldsMatchingObjectsAsync()
        {
            var h = new AISessionHarness();
            var resourceId = new NodeId(9000u, 3);
            h.AddBrowse(h.ModelNodeId,
                [h.Ref(resourceId, "Weights", ObjectTypes.ModelResourceType)]);

            var entries = new List<AINodeEntry>();
            await foreach (AINodeEntry entry in h.Client.Model(h.ModelNodeId).EnumerateResourcesAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].BrowseName.Name, Is.EqualTo("Weights"));
        }

        [Test]
        public async Task ModelReadResourceAsyncReturnsSnapshotAsync()
        {
            var h = new AISessionHarness();
            var resId = new NodeId(9100u, 3);
            h.AddValueChild(resId, BrowseNames.ArtifactUri, new NodeId(9101u, 3), "https://models/weights.bin");
            h.AddValueChild(resId, BrowseNames.ContentType, new NodeId(9102u, 3), "application/octet-stream");
            h.AddValueChild(resId, BrowseNames.SizeBytes, new NodeId(9103u, 3), (ulong)1048576);
            h.AddValueChild(resId, BrowseNames.Digest, new NodeId(9104u, 3), ByteString.From([0xAB, 0xCD]));
            h.AddValueChild(resId, BrowseNames.DigestAlgorithm, new NodeId(9105u, 3), "SHA-256");

            AIModelResourceSnapshot snapshot = await h.Client.Model(h.ModelNodeId)
                .ReadResourceAsync(resId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ArtifactUri, Is.EqualTo("https://models/weights.bin"));
                Assert.That(snapshot.ContentType, Is.EqualTo("application/octet-stream"));
                Assert.That(snapshot.SizeBytes, Is.EqualTo(1048576));
                Assert.That(snapshot.DigestAlgorithm, Is.EqualTo("SHA-256"));
            });
        }

        [Test]
        public async Task ModelOpenSourceAsyncReturnsNullWhenNoSourceReferenceAsync()
        {
            var h = new AISessionHarness();

            AIModelSourceClient? source = await h.Client.Model(h.ModelNodeId)
                .OpenSourceAsync().ConfigureAwait(false);

            Assert.That(source, Is.Null);
        }

        [Test]
        public async Task ModelReadResourceAsyncWithMissingChildrenReturnsDefaultsAsync()
        {
            var h = new AISessionHarness();
            var resId = new NodeId(9200u, 3);

            AIModelResourceSnapshot snapshot = await h.Client.Model(h.ModelNodeId)
                .ReadResourceAsync(resId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ArtifactUri, Is.Null);
                Assert.That(snapshot.ContentType, Is.Null);
                Assert.That(snapshot.SizeBytes, Is.Zero);
            });
        }

        [Test]
        public void ModelReadResourceAsyncWithNullNodeIdThrows()
        {
            var h = new AISessionHarness();

            Assert.ThrowsAsync<ArgumentException>(
                async () => await h.Client.Model(h.ModelNodeId)
                    .ReadResourceAsync(NodeId.Null).ConfigureAwait(false));
        }

        [Test]
        public async Task ModelReadWithAllFieldsPopulatedReturnsCompleteSnapshotAsync()
        {
            var h = new AISessionHarness();
            NodeId modelId = h.ModelNodeId;
            h.AddValueChild(modelId, BrowseNames.ModelId, new NodeId(9300u, 3), "m-full");
            h.AddValueChild(modelId, BrowseNames.Name, new NodeId(9301u, 3), "FullModel");
            h.AddValueChild(modelId, BrowseNames.Version, new NodeId(9302u, 3), "2.0");
            h.AddValueChild(modelId, BrowseNames.Framework, new NodeId(9303u, 3), "ONNX");
            h.AddValueChild(modelId, BrowseNames.Format, new NodeId(9304u, 3), "onnx");
            h.AddValueChild(modelId, BrowseNames.License, new NodeId(9305u, 3), "MIT");
            h.AddValueChild(modelId, BrowseNames.Digest, new NodeId(9306u, 3), ByteString.From([1]));
            h.AddValueChild(modelId, BrowseNames.DigestAlgorithm, new NodeId(9307u, 3), "SHA-256");
            h.AddValueChild(modelId, BrowseNames.Publisher, new NodeId(9310u, 3), new NodeId(700u, 3));

            AIModelSnapshot snapshot = await h.Client.Model(modelId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ModelId, Is.EqualTo("m-full"));
                Assert.That(snapshot.Framework, Is.EqualTo("ONNX"));
                Assert.That(snapshot.Format, Is.EqualTo("onnx"));
                Assert.That(snapshot.License, Is.EqualTo("MIT"));
                Assert.That(snapshot.DigestAlgorithm, Is.EqualTo("SHA-256"));
            });
        }

        [Test]
        public async Task DeploymentReadWithAllFieldsPopulatedReturnsCompleteSnapshotAsync()
        {
            var h = new AISessionHarness();
            NodeId depId = h.DeploymentNodeId;
            h.AddValueChild(depId, BrowseNames.DeploymentId, new NodeId(9400u, 3), "dep-full");
            h.AddValueChild(
                depId,
                BrowseNames.InferenceLocation,
                new NodeId(9401u, 3),
                (int)InferenceLocationEnum.EdgeOffServer);
            h.AddValueChild(
                depId,
                BrowseNames.State,
                new NodeId(9402u, 3),
                (int)DeploymentStateEnum.Active);
            h.AddValueChild(depId, BrowseNames.DataJurisdiction, new NodeId(9403u, 3), "US");
            h.AddValueChild(depId, BrowseNames.EgressPermitted, new NodeId(9404u, 3), false);
            h.AddValueChild(depId, BrowseNames.MaxInlinePayloadSize, new NodeId(9405u, 3), (ulong)8192);
            h.AddValueChild(depId, BrowseNames.EndpointUri, new NodeId(9406u, 3), "https://ai.example.com/deploy");

            AIDeploymentSnapshot snapshot = await h.Client.Deployment(depId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.DeploymentId, Is.EqualTo("dep-full"));
                Assert.That(snapshot.InferenceLocation, Is.EqualTo(InferenceLocationEnum.EdgeOffServer));
                Assert.That(snapshot.State, Is.EqualTo(DeploymentStateEnum.Active));
                Assert.That(snapshot.DataJurisdiction, Is.EqualTo("US"));
                Assert.That(snapshot.EgressPermitted, Is.False);
                Assert.That(snapshot.MaxInlinePayloadSize, Is.EqualTo(8192));
                Assert.That(snapshot.EndpointUri, Is.EqualTo("https://ai.example.com/deploy"));
            });
        }
    }
}
