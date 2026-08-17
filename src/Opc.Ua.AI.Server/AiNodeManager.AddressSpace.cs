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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AI.Inference;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.AI;
using AIRefs = Opc.Ua.AI.ReferenceTypeIds;
using BrowseNames = Opc.Ua.AI.BrowseNames;
using ObjectIds = Opc.Ua.ObjectIds;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace Opc.Ua.AI.Server
{
    public sealed partial class AINodeManager
    {
        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(externalReferences);

            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            lock (m_sync)
            {
                try
                {
                    CreateAIAddressSpace(externalReferences);
                }
                catch (Exception ex)
                {
                    // The Server reports a start-up failure as a bare status code, so
                    // without this the one piece of information needed to fix it -
                    // where it happened - is discarded before anyone sees it.
                    m_logger.LogError(ex, "Failed to build the AI address space.");
                    throw;
                }
            }
        }

        private void CreateAIAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            // The model already declares the entry point - ns=2;i=7001, parented to
            // the Server Object - so it is adopted rather than built again. Creating
            // a second one leaves two Objects named AiModelManagement under the
            // Server, one populated and one empty, and which of them a client finds
            // depends on browse order. The empty one makes the Server look as though
            // it publishes no models at all.
            m_root = FindPredefinedNode<AiRootState>(
                new NodeId(Opc.Ua.AI.Objects.AiModelManagement, NamespaceIndex));

            Child<PropertyState<string>>(m_root, BrowseNames.SpecificationVersion)
                .Value = SpecificationVersion;

            // Materialised now, before anything is indexed. Jobs is optional, so it
            // does not exist until something asks for it - and the first thing to
            // ask is BeginTransfer, long after registration, which would leave the
            // folder visible in a Browse of the root and unresolvable when browsed.
            Child<FolderState>(m_root, BrowseNames.Jobs);

            BuildModels();
            BuildDeployments();

            if (m_options.EnableLearningLoop)
            {
                BuildLearningJob();
            }

            if (m_options.EnableCatalogue)
            {
                BuildCatalogue();
            }

            // One call: AddPredefinedNode walks the children, so the tree has to be
            // finished before it is registered, not registered as it is built.
            AddPredefinedNodeSynchronously(m_root);
        }

        /// <summary>
        /// The release of the companion specification this Server implements.
        /// </summary>
        public const string SpecificationVersion = "0.2.0";

        private static void AddExternalReference(
            NodeId sourceId,
            NodeId referenceType,
            bool isInverse,
            NodeId targetId,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (!externalReferences.TryGetValue(sourceId, out IList<IReference>? references))
            {
                references = new List<IReference>();
                externalReferences[sourceId] = references;
            }

            references.Add(new NodeStateReference(referenceType, isInverse, targetId));
        }

        /// <summary>
        /// Publishes the models this Server can run.
        /// </summary>
        /// <remarks>
        /// A model carries the identity and the digest at which a provenance walk
        /// terminates. The nameplate answers which artefact this is; whether it
        /// ought to be running here is a different question, asked by different
        /// people, and answered by the card.
        /// </remarks>
        private void BuildModels()
        {
            IList<BackendModel> declared = m_backendOptions.Models;

            BackendModel primary = declared.Count > 0 ? declared[0] : DefaultModel;
            m_primaryModel = CreateModel(primary, "PrimaryModel");

            if (!m_options.EnableFallback)
            {
                return;
            }

            BackendModel secondary = declared.Count > 1
                ? declared[1]
                : primary with
                {
                    Name = primary.Name + "-compact",
                    // A synthesised stand-in is a DIFFERENT artefact, so it cannot
                    // inherit the primary's digest. A provenance walk from a fallback
                    // answer would otherwise terminate at the primary's digest and
                    // attribute the answer to weights that never ran - which is
                    // precisely the comparison a digest exists to support.
                    Digest = default,
                    DigestAlgorithm = string.Empty,
                    Version = primary.Version,
                    Quantization = "unspecified"
                };

            m_fallbackModel = CreateModel(secondary, "FallbackModel");
        }

        private static BackendModel DefaultModel { get; } = new()
        {
            Publisher = "sample",
            Name = "primary-model",
            Version = "1.0.0",
            TaskKind = "chat",
            Framework = "rest-chat-completions"
        };

        private ModelState CreateModel(BackendModel source, string browseName)
        {
            // Constructed without a parent on purpose. AddChild assigns the parent
            // AND the reference type that makes the child browsable, but it only
            // does so when the parent actually changes - so a node handed its parent
            // in the constructor is indexed by the Server and invisible to a client,
            // which is a great deal harder to notice than an outright failure.
            var model = new ModelState(null);
            model.Create(
                SystemContext,
                NodeId.Null,
                new QualifiedName(browseName, NamespaceIndex),
                new LocalizedText(source.Name),
                true);

            Child<PropertyState<string>>(model, BrowseNames.ModelId).Value =
                FormattableString.Invariant(
                    $"{source.Publisher}/{source.Name}:{source.Version}");
            Child<PropertyState<LocalizedText>>(model, BrowseNames.Name).Value =
                new LocalizedText(source.Name);
            Child<PropertyState<string>>(model, BrowseNames.Version).Value = source.Version;
            Child<PropertyState<string>>(model, BrowseNames.Publisher).Value = source.Publisher;
            Child<PropertyState<string>>(model, BrowseNames.Framework).Value = source.Framework;
            Child<PropertyState<string>>(model, BrowseNames.TaskKind).Value = source.TaskKind;

            if (!string.IsNullOrEmpty(source.Quantization))
            {
                Child<PropertyState<string>>(model, BrowseNames.Quantization).Value =
                    source.Quantization;
            }

            // Digest and DigestAlgorithm are Mandatory because a provenance walk
            // terminates at them. This sample never holds the artefact, so it
            // publishes what the backend declares; where a backend declares nothing,
            // an empty digest is the honest answer and a fabricated one is not.
            Child<PropertyState<ByteString>>(model, BrowseNames.Digest).Value =
                source.Digest.Length > 0
                    ? new ByteString(source.Digest.ToArray())
                    : ByteString.Empty;
            Child<PropertyState<string>>(model, BrowseNames.DigestAlgorithm).Value =
                source.DigestAlgorithm;

            Child<FolderState>(m_root!, BrowseNames.Models).AddChild(model);
            return model;
        }

        /// <summary>
        /// Publishes the deployments that run those models.
        /// </summary>
        /// <remarks>
        /// The primary runs wherever the backend is configured to reach. The
        /// fallback runs on the Server, because a fallback that needed the same
        /// network as the primary would not be one. What matters in the fallback
        /// case is not that an answer arrives but that <c>ModelUsed</c> says which
        /// model produced it.
        /// </remarks>
        private void BuildDeployments()
        {
            m_primary = CreateDeployment(
                m_options.PrimaryDeploymentId,
                "PrimaryDeployment",
                m_primaryModel!,
                m_backendOptions);

            if (!m_options.EnableFallback || m_fallbackModel is null)
            {
                return;
            }

            // From the FALLBACK's own configuration. Publishing the primary's site,
            // jurisdiction and egress here would describe a deployment that does not
            // exist: an operator who points the fallback at a cloud endpoint would
            // get a Server routing payloads off the machine while telling every
            // client InferenceLocation=OnServer and EgressPermitted=false.
            m_fallback = CreateDeployment(
                m_options.FallbackDeploymentId,
                "FallbackDeployment",
                m_fallbackModel,
                m_fallbackBackendOptions);

            Child<PropertyState<FallbackPolicyEnum>>(m_primary, BrowseNames.FallbackPolicy)
                .Value = FallbackPolicyEnum.FallBackTo;
            m_primary.AddReference(
                RefType(AIRefs.FallsBackTo), false, m_fallback.NodeId);
            m_fallback.AddReference(
                RefType(AIRefs.FallsBackTo), true, m_primary.NodeId);
        }

        /// <summary>
        /// Resolves a reference type this model declares to a NodeId in this Server.
        /// </summary>
        /// <remarks>
        /// The generated identifiers are ExpandedNodeIds carrying a namespace URI,
        /// because a model does not know what index a Server will give it. The
        /// translation has to happen against the Server's own namespace table.
        /// </remarks>
        private NodeId RefType(ExpandedNodeId referenceTypeId)
        {
            return ExpandedNodeId.ToNodeId(referenceTypeId, Server.NamespaceUris);
        }

        private DeploymentState CreateDeployment(
            string deploymentId,
            string browseName,
            ModelState model,
            InferenceBackendOptions backend)
        {
            InferenceLocationEnum site = MapSite(backend.Site);
            var deployment = new DeploymentState(null);
            deployment.Create(
                SystemContext,
                NodeId.Null,
                new QualifiedName(browseName, NamespaceIndex),
                new LocalizedText(deploymentId),
                true);

            Child<PropertyState<string>>(deployment, BrowseNames.DeploymentId).Value =
                deploymentId;
            Child<PropertyState<InferenceLocationEnum>>(
                deployment, BrowseNames.InferenceLocation).Value = site;
            Child<PropertyState<DeploymentStateEnum>>(deployment, BrowseNames.State).Value =
                DeploymentStateEnum.Ready;
            Child<PropertyState<VersionBindingEnum>>(
                deployment, BrowseNames.VersionBinding).Value = VersionBindingEnum.Pinned;

            // Fail is the safe default: a caller told that nothing happened can
            // decide what to do, and deciding is frequently its job.
            Child<PropertyState<FallbackPolicyEnum>>(
                deployment, BrowseNames.FallbackPolicy).Value = FallbackPolicyEnum.Fail;

            // Where the data goes. Egress is not made false by encryption, which
            // answers who can read data in flight and not where the data went.
            Child<PropertyState<string>>(deployment, BrowseNames.DataJurisdiction).Value =
                backend.DataJurisdiction;
            Child<PropertyState<bool>>(deployment, BrowseNames.EgressPermitted).Value =
                backend.EgressPermitted;
            Child<PropertyState<bool>>(deployment, BrowseNames.RetainsInput).Value =
                backend.RetainsInput;

            // Published before a client calls rather than discovered from a
            // rejection: the real bound is the smallest of several limits, and a
            // client can see none of them.
            Child<PropertyState<uint>>(deployment, BrowseNames.MaxInlinePayloadSize).Value =
                backend.MaxInlinePayloadSize;

            Child<PropertyState<ReachabilityEnum>>(deployment, BrowseNames.Reachability)
                .Value = ReachabilityEnum.Unknown;
            Child<PropertyState<uint>>(deployment, BrowseNames.ConsecutiveFailures).Value = 0;

            if (site != InferenceLocationEnum.OnServer &&
                !string.IsNullOrEmpty(backend.EndpointUri))
            {
                // The endpoint, never the credential. The address space says where
                // the Server goes; what it presents on arrival stays out of it.
                Child<PropertyState<string>>(deployment, BrowseNames.EndpointUri).Value =
                    backend.EndpointUri;
            }

            // Exactly one UsesModel reference: the only defined path from a running
            // deployment to the artefact its answers depend on.
            deployment.AddReference(RefType(AIRefs.UsesModel), false, model.NodeId);
            model.AddReference(RefType(AIRefs.UsesModel), true, deployment.NodeId);

            Child<FolderState>(m_root!, BrowseNames.Deployments).AddChild(deployment);
            WireDeploymentMethods(deployment);
            return deployment;
        }

        private static InferenceLocationEnum MapSite(InferenceSite site)
        {
            return site switch
            {
                InferenceSite.Cloud => InferenceLocationEnum.Cloud,
                InferenceSite.EdgeOffServer => InferenceLocationEnum.EdgeOffServer,
                _ => InferenceLocationEnum.OnServer
            };
        }
    }
}
