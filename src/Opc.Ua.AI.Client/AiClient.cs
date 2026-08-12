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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.AI.Client
{
    public sealed class AiClient
    {
        public AiClient(ISession session, ITelemetryContext telemetry)
        {
            m_operations = new AiClientOperations(session, telemetry);
        }

        public ISession Session => m_operations.Session;

        public ITelemetryContext Telemetry => m_operations.Telemetry;

        public bool IsAiNamespaceAvailable => m_operations.TryGetAiNamespaceIndex(out _);

        public NodeId AiRootId
        {
            get
            {
                if (!m_operations.TryGetAiNamespaceIndex(out ushort _))
                {
                    return NodeId.Null;
                }
                return NodeId.Create(Objects.AiModelManagement, Namespaces.AI, Session.NamespaceUris);
            }
        }

        public NodeId ModelsFolderId => CreateWellKnownNode(Objects.AiRootType_Models);

        public NodeId DatasetsFolderId => CreateWellKnownNode(Objects.AiRootType_Datasets);

        public NodeId DeploymentsFolderId => CreateWellKnownNode(Objects.AiRootType_Deployments);

        public NodeId LearningJobsFolderId => CreateWellKnownNode(Objects.AiRootType_LearningJobs);

        public ValueTask<NodeId> GetRegistriesFolderIdAsync(
            CancellationToken cancellationToken = default)
        {
            return ResolveOptionalRootChildAsync(BrowseNames.Registries, cancellationToken);
        }

        public ValueTask<NodeId> GetSourcesFolderIdAsync(
            CancellationToken cancellationToken = default)
        {
            return ResolveOptionalRootChildAsync(BrowseNames.Sources, cancellationToken);
        }

        public ValueTask<NodeId> GetEvaluationsFolderIdAsync(
            CancellationToken cancellationToken = default)
        {
            return ResolveOptionalRootChildAsync(BrowseNames.Evaluations, cancellationToken);
        }

        public ValueTask<NodeId> GetJobsFolderIdAsync(
            CancellationToken cancellationToken = default)
        {
            return ResolveOptionalRootChildAsync(BrowseNames.Jobs, cancellationToken);
        }

        public async ValueTask<ArrayOf<NodeId>> DiscoverModelsAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetRootFolderIdAsync(BrowseNames.Models, cancellationToken)
                .ConfigureAwait(false);
            return await DiscoverAsync(folder, ObjectTypes.ModelType, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<NodeId>> DiscoverDatasetsAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetRootFolderIdAsync(BrowseNames.Datasets, cancellationToken)
                .ConfigureAwait(false);
            return await DiscoverAsync(folder, ObjectTypes.DatasetType, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<NodeId>> DiscoverDeploymentsAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetRootFolderIdAsync(BrowseNames.Deployments, cancellationToken)
                .ConfigureAwait(false);
            return await DiscoverAsync(folder, ObjectTypes.DeploymentType, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<NodeId>> DiscoverLearningJobsAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetRootFolderIdAsync(BrowseNames.LearningJobs, cancellationToken)
                .ConfigureAwait(false);
            return await DiscoverAsync(folder, ObjectTypes.LearningJobType, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<NodeId>> DiscoverInferenceJobsAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetJobsFolderIdAsync(cancellationToken).ConfigureAwait(false);
            return await DiscoverAsync(folder, ObjectTypes.InferenceJobType, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<NodeId>> DiscoverEvaluationRunsAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetEvaluationsFolderIdAsync(cancellationToken).ConfigureAwait(false);
            return await DiscoverAsync(folder, ObjectTypes.EvaluationRunType, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ArrayOf<NodeId>> DiscoverSourcesAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetSourcesFolderIdAsync(cancellationToken).ConfigureAwait(false);
            return await DiscoverAsync(folder, ObjectTypes.ModelSourceType, cancellationToken)
                .ConfigureAwait(false);
        }

        public async IAsyncEnumerable<AiNodeEntry> EnumerateModelsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetRootFolderIdAsync(BrowseNames.Models, cancellationToken)
                .ConfigureAwait(false);
            await foreach (AiNodeEntry entry in EnumerateInstancesAsync(
                folder, ObjectTypes.ModelType, cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        public async IAsyncEnumerable<AiNodeEntry> EnumerateDatasetsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetRootFolderIdAsync(BrowseNames.Datasets, cancellationToken)
                .ConfigureAwait(false);
            await foreach (AiNodeEntry entry in EnumerateInstancesAsync(
                folder, ObjectTypes.DatasetType, cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        public async IAsyncEnumerable<AiNodeEntry> EnumerateDeploymentsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetRootFolderIdAsync(BrowseNames.Deployments, cancellationToken)
                .ConfigureAwait(false);
            await foreach (AiNodeEntry entry in EnumerateInstancesAsync(
                folder, ObjectTypes.DeploymentType, cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        public async IAsyncEnumerable<AiNodeEntry> EnumerateLearningJobsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetRootFolderIdAsync(BrowseNames.LearningJobs, cancellationToken)
                .ConfigureAwait(false);
            await foreach (AiNodeEntry entry in EnumerateInstancesAsync(
                folder, ObjectTypes.LearningJobType, cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        public async IAsyncEnumerable<AiNodeEntry> EnumerateInferenceJobsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetJobsFolderIdAsync(cancellationToken).ConfigureAwait(false);
            await foreach (AiNodeEntry entry in EnumerateInstancesAsync(
                folder, ObjectTypes.InferenceJobType, cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        public async IAsyncEnumerable<AiNodeEntry> EnumerateEvaluationRunsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetEvaluationsFolderIdAsync(cancellationToken).ConfigureAwait(false);
            await foreach (AiNodeEntry entry in EnumerateInstancesAsync(
                folder, ObjectTypes.EvaluationRunType, cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        public async IAsyncEnumerable<AiNodeEntry> EnumerateSourcesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId folder = await GetSourcesFolderIdAsync(cancellationToken).ConfigureAwait(false);
            await foreach (AiNodeEntry entry in EnumerateInstancesAsync(
                folder, ObjectTypes.ModelSourceType, cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        public AiModelClient Model(NodeId modelNodeId)
        {
            return new AiModelClient(m_operations, modelNodeId);
        }

        public AiDatasetClient Dataset(NodeId datasetNodeId)
        {
            return new AiDatasetClient(m_operations, datasetNodeId);
        }

        public AiDeploymentClient Deployment(NodeId deploymentNodeId)
        {
            return new AiDeploymentClient(m_operations, deploymentNodeId);
        }

        public AiModelSourceClient Source(NodeId sourceNodeId)
        {
            return new AiModelSourceClient(m_operations, sourceNodeId);
        }

        public AiInferenceJobClient InferenceJob(NodeId jobNodeId)
        {
            return new AiInferenceJobClient(m_operations, jobNodeId);
        }

        public AiLearningJobClient LearningJob(NodeId jobNodeId)
        {
            return new AiLearningJobClient(m_operations, jobNodeId);
        }

        public AiEvaluationRunClient EvaluationRun(NodeId runNodeId)
        {
            return new AiEvaluationRunClient(m_operations, runNodeId);
        }

        public AiInferenceTransferClient Transfer(NodeId transferNodeId)
        {
            return new AiInferenceTransferClient(m_operations, transferNodeId);
        }

        internal AiClientOperations Operations => m_operations;

        private NodeId CreateWellKnownNode(uint identifier)
        {
            return IsAiNamespaceAvailable
                ? NodeId.Create(identifier, Namespaces.AI, Session.NamespaceUris)
                : NodeId.Null;
        }

        private ValueTask<NodeId> ResolveOptionalRootChildAsync(
            string browseName,
            CancellationToken cancellationToken)
        {
            return GetRootFolderIdAsync(browseName, cancellationToken);
        }

        private async ValueTask<NodeId> GetRootFolderIdAsync(
            string browseName,
            CancellationToken cancellationToken)
        {
            NodeId root = AiRootId;
            if (root.IsNull)
            {
                return NodeId.Null;
            }
            AiRootTypeClient proxy = new(Session, root, Telemetry);
            return browseName switch
            {
                BrowseNames.Registries => (await proxy.GetRegistriesAsync(Telemetry, cancellationToken)
                    .ConfigureAwait(false))?.ObjectId ?? NodeId.Null,
                BrowseNames.Sources => (await proxy.GetSourcesAsync(Telemetry, cancellationToken)
                    .ConfigureAwait(false))?.ObjectId ?? NodeId.Null,
                BrowseNames.Evaluations => (await proxy.GetEvaluationsAsync(Telemetry, cancellationToken)
                    .ConfigureAwait(false))?.ObjectId ?? NodeId.Null,
                BrowseNames.Jobs => (await proxy.GetJobsAsync(Telemetry, cancellationToken)
                    .ConfigureAwait(false))?.ObjectId ?? NodeId.Null,
                _ => await m_operations.ResolveChildAsync(root, browseName, cancellationToken)
                    .ConfigureAwait(false)
            };
        }

        private ValueTask<ArrayOf<NodeId>> DiscoverAsync(
            NodeId folder,
            uint typeIdentifier,
            CancellationToken cancellationToken)
        {
            return m_operations.DiscoverInstancesAsync(
                folder,
                m_operations.AiNamespaceType(typeIdentifier),
                cancellationToken);
        }

        private async IAsyncEnumerable<AiNodeEntry> EnumerateInstancesAsync(
            NodeId root,
            uint typeIdentifier,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (root.IsNull)
            {
                yield break;
            }
            NodeId typeDefinition = m_operations.AiNamespaceType(typeIdentifier);
            if (typeDefinition.IsNull)
            {
                yield break;
            }
            ArrayOf<ReferenceDescription> references = await m_operations
                .BrowseHierarchicalObjectsAsync(root, cancellationToken).ConfigureAwait(false);
            var matches = new List<AiNodeEntry>();
            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];
                NodeId typeDef = ExpandedNodeId.ToNodeId(
                    reference.TypeDefinition, Session.NamespaceUris);
                NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (typeDef.IsNull || nodeId.IsNull)
                {
                    continue;
                }
                if (typeDef == typeDefinition ||
                    await Session.NodeCache.IsTypeOfAsync(
                        typeDef, typeDefinition, cancellationToken).ConfigureAwait(false))
                {
                    matches.Add(new AiNodeEntry(
                        nodeId, reference.BrowseName, reference.DisplayName, typeDef));
                }
            }
            for (int ii = 0; ii < matches.Count; ii++)
            {
                yield return matches[ii];
            }
        }

        private readonly AiClientOperations m_operations;
    }
}
