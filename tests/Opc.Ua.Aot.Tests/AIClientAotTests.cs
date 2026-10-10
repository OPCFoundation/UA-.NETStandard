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

using Opc.Ua.AI.Client;
using Opc.Ua.Client;
using Ai = Opc.Ua.AI;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// NativeAOT regressions for the AI catalogue and generated client contracts
    /// used by UaLens. A real Session dispatches to a transport that never opens
    /// a socket; no inference provider or AI server implementation is required.
    /// </summary>
    public sealed class AIClientAotTests
    {
        [Test]
        public async Task ConstructionAndDiscoveryUseTheSessionNamespaceAsync()
        {
            using var harness = new AIClientHarness();
            AIClient client = harness.Client;
            var specializedModelId = new NodeId("model.specialized", kInstanceNamespaceIndex);
            var specializedTypeId = new NodeId("ModelSubtype", kInstanceNamespaceIndex);
            ReferenceDescription specialized = Reference(
                specializedModelId, "Specialized model", Ai.ObjectTypes.ModelType);
            specialized.TypeDefinition = new ExpandedNodeId(specializedTypeId);
            harness.Channel.AddBrowse(specializedTypeId, NodeId.Null,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(Ai.ObjectTypes.ModelType, kAiNamespace),
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IsForward = false,
                    NodeClass = NodeClass.ObjectType
                }
            ]);
            harness.Channel.AddBrowse(s_modelsFolderId, ReferenceTypeIds.HierarchicalReferences,
            [
                Reference(s_modelId, "Inspection model", Ai.ObjectTypes.ModelType),
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(new NodeId("untyped", kInstanceNamespaceIndex))
                },
                new ReferenceDescription
                {
                    TypeDefinition = new ExpandedNodeId(Ai.ObjectTypes.ModelType, kAiNamespace)
                },
                Reference(s_deploymentId, "Not a model", Ai.ObjectTypes.DeploymentType),
                specialized
            ]);
            harness.Channel.AddBrowse(s_deploymentsFolderId, ReferenceTypeIds.HierarchicalReferences,
                [Reference(s_deploymentId, "Local deployment", Ai.ObjectTypes.DeploymentType)]);

            ArrayOf<NodeId> models = await client.DiscoverModelsAsync().ConfigureAwait(false);
            ArrayOf<NodeId> deployments = await client.DiscoverDeploymentsAsync().ConfigureAwait(false);

            await Assert.That(Ai.Namespaces.AI).IsEqualTo(kAiNamespace);
            await Assert.That(client.Session).IsSameReferenceAs(harness.Session);
            await Assert.That(client.Telemetry).IsSameReferenceAs(harness.Session.MessageContext.Telemetry);
            await Assert.That(client.IsAINamespaceAvailable).IsTrue();
            await Assert.That(client.AIRootId).IsEqualTo(s_rootId);
            await Assert.That(client.ModelsFolderId)
                .IsEqualTo(new NodeId(Ai.Objects.AiRootType_Models, kAiNamespaceIndex));
            await Assert.That(client.DeploymentsFolderId)
                .IsEqualTo(new NodeId(Ai.Objects.AiRootType_Deployments, kAiNamespaceIndex));
            await Assert.That(models.Count).IsEqualTo(2);
            await Assert.That(models[0]).IsEqualTo(s_modelId);
            await Assert.That(models[1]).IsEqualTo(specializedModelId);
            await Assert.That(deployments.Count).IsEqualTo(1);
            await Assert.That(deployments[0]).IsEqualTo(s_deploymentId);
            await Assert.That(client.Model(models[0]).ModelNodeId).IsEqualTo(s_modelId);
            await Assert.That(client.Deployment(deployments[0]).DeploymentNodeId).IsEqualTo(s_deploymentId);
            await Assert.That(harness.Channel.Paths.Count).IsEqualTo(2);
            await AssertPathAsync(harness.Channel.Paths[0], s_rootId, Ai.BrowseNames.Models,
                ReferenceTypeIds.HierarchicalReferences).ConfigureAwait(false);
            await AssertPathAsync(harness.Channel.Paths[1], s_rootId, Ai.BrowseNames.Deployments,
                ReferenceTypeIds.HierarchicalReferences).ConfigureAwait(false);
            ArrayOf<BrowseDescription> browses = harness.Channel.Browses.Filter(
                browse => browse.NodeId == s_modelsFolderId || browse.NodeId == s_deploymentsFolderId);
            await Assert.That(browses.Count).IsEqualTo(2);
            await Assert.That(browses[0].NodeId).IsEqualTo(s_modelsFolderId);
            await Assert.That(browses[1].NodeId).IsEqualTo(s_deploymentsFolderId);
            for (int ii = 0; ii < browses.Count; ii++)
            {
                BrowseDescription browse = browses[ii];
                await Assert.That(browse.ReferenceTypeId).IsEqualTo(ReferenceTypeIds.HierarchicalReferences);
                await Assert.That(browse.BrowseDirection).IsEqualTo(BrowseDirection.Forward);
                await Assert.That(browse.IncludeSubtypes).IsTrue();
                await Assert.That(browse.NodeClassMask).IsEqualTo((uint)NodeClass.Object);
            }
            await Assert.That(harness.Channel.Browses.Count).IsEqualTo(4);
            await Assert.That(harness.Channel.RequestCount).IsEqualTo(6);
        }

        [Test]
        public async Task MissingNamespaceDoesNotBrowseLookalikeNamespaceAsync()
        {
            using var harness = new AIClientHarness(includeAiNamespace: false);

            ArrayOf<NodeId> models = await harness.Client.DiscoverModelsAsync().ConfigureAwait(false);
            ArrayOf<NodeId> deployments = await harness.Client.DiscoverDeploymentsAsync().ConfigureAwait(false);

            await Assert.That(harness.Client.IsAINamespaceAvailable).IsFalse();
            await Assert.That(harness.Client.AIRootId.IsNull).IsTrue();
            await Assert.That(harness.Client.ModelsFolderId.IsNull).IsTrue();
            await Assert.That(harness.Client.DeploymentsFolderId.IsNull).IsTrue();
            await Assert.That(models.Count).IsEqualTo(0);
            await Assert.That(deployments.Count).IsEqualTo(0);
            await Assert.That(harness.Session.NamespaceUris.GetIndex("http://opcfoundation.org/UA/AI"))
                .IsEqualTo(2);
            await Assert.That(harness.Channel.RequestCount).IsEqualTo(0);
        }

        [Test]
        public async Task MissingFolderAndBadBrowseDoNotProduceInstancesAsync()
        {
            using var harness = new AIClientHarness();
            harness.Channel.RemoveChild(s_rootId, Ai.BrowseNames.Models);
            harness.Channel.AddBrowse(s_deploymentsFolderId, ReferenceTypeIds.HierarchicalReferences,
                [Reference(s_deploymentId, "Must not escape bad status", Ai.ObjectTypes.DeploymentType)],
                StatusCodes.BadUserAccessDenied);

            ArrayOf<NodeId> models = await harness.Client.DiscoverModelsAsync().ConfigureAwait(false);
            ArrayOf<NodeId> deployments = await harness.Client.DiscoverDeploymentsAsync().ConfigureAwait(false);

            await Assert.That(models.Count).IsEqualTo(0);
            await Assert.That(deployments.Count).IsEqualTo(0);
            await Assert.That(harness.Channel.Paths.Count).IsEqualTo(2);
            await Assert.That(harness.Channel.Browses.Count).IsEqualTo(1);
            await Assert.That(harness.Channel.Browses[0].NodeId).IsEqualTo(s_deploymentsFolderId);
            await Assert.That(harness.Channel.RequestCount).IsEqualTo(3);
        }

        [Test]
        public async Task ModelReadPreservesSparseValuesAndDecodesTheGeneratedCardAsync()
        {
            using var harness = new AIClientHarness();
            var cardId = new NodeId("model.card", kInstanceNamespaceIndex);
            var publisherId = new NodeId("publisher", kInstanceNamespaceIndex);
            var sourceId = new NodeId("source", kInstanceNamespaceIndex);
            harness.Channel.AddValue(s_modelId, Ai.BrowseNames.ModelId, "inspection-v2");
            harness.Channel.AddValue(s_modelId, Ai.BrowseNames.Name, "Inspection");
            // Missing Version/Framework/License must not shift the later values.
            harness.Channel.AddValue(s_modelId, Ai.BrowseNames.Format, "ONNX");
            harness.Channel.AddValue(s_modelId, Ai.BrowseNames.Digest, ByteString.From([0xA1, 0x00, 0xFE]));
            harness.Channel.AddValue(s_modelId, Ai.BrowseNames.DigestAlgorithm, "SHA-256");
            harness.Channel.AddValue(s_modelId, Ai.BrowseNames.CreatedAt, new DateTimeUtc(2026, 1, 2));
            harness.Channel.AddValue(s_modelId, Ai.BrowseNames.LastModifiedAt, new DateTimeUtc(2026, 2, 3));
            harness.Channel.AddValue(s_modelId, Ai.BrowseNames.Publisher, publisherId);
            harness.Channel.AddChild(s_modelId, Ai.BrowseNames.Card, cardId, ReferenceTypeIds.HasComponent);
            harness.Channel.AddBrowse(s_modelId, new NodeId(Ai.ReferenceTypes.ImportedFrom, kAiNamespaceIndex),
                [Reference(sourceId, "Source", Ai.ObjectTypes.ModelSourceType)]);
            harness.Channel.AddValue(cardId, Ai.BrowseNames.IntendedUse, "Visual inspection");
            harness.Channel.AddValue(cardId, Ai.BrowseNames.DataJurisdiction, "EU");
            harness.Channel.AddValue(cardId, Ai.BrowseNames.SafetyAssessment,
                Variant.From(ArrayOf.Wrapped([SafetyAssessment()])));
            AIModelClient model = harness.Client.Model(s_modelId);

            AIModelSnapshot snapshot = await model.ReadAsync().ConfigureAwait(false);
            AIModelCardSnapshot card = await model.ReadCardAsync().ConfigureAwait(false);

            await Assert.That(snapshot.NodeId).IsEqualTo(s_modelId);
            await Assert.That(snapshot.ModelId).IsEqualTo("inspection-v2");
            await Assert.That(snapshot.Name).IsEqualTo("Inspection");
            await Assert.That(snapshot.Version).IsNull();
            await Assert.That(snapshot.Framework).IsNull();
            await Assert.That(snapshot.Format).IsEqualTo("ONNX");
            await Assert.That(snapshot.License).IsNull();
            await Assert.That(snapshot.Digest).IsEqualTo(ByteString.From([0xA1, 0x00, 0xFE]));
            await Assert.That(snapshot.DigestAlgorithm).IsEqualTo("SHA-256");
            await Assert.That(snapshot.CreatedAt).IsEqualTo(new DateTimeUtc(2026, 1, 2));
            await Assert.That(snapshot.LastModifiedAt).IsEqualTo(new DateTimeUtc(2026, 2, 3));
            await Assert.That(snapshot.PublisherId).IsEqualTo(publisherId);
            await Assert.That(snapshot.CardId).IsEqualTo(cardId);
            await Assert.That(snapshot.SourceId).IsEqualTo(sourceId);
            await Assert.That(card.NodeId).IsEqualTo(cardId);
            await Assert.That(card.IntendedUse).IsEqualTo("Visual inspection");
            await Assert.That(card.Limitations).IsNull();
            await Assert.That(card.DataJurisdiction).IsEqualTo("EU");
            await AssertSafetyAsync(card.SafetyAssessment).ConfigureAwait(false);
            await Assert.That(harness.Channel.Reads.Count).IsEqualTo(2);
            await AssertReadAsync(harness.Channel.Reads[0], s_modelId,
            [
                Ai.BrowseNames.ModelId, Ai.BrowseNames.Name, Ai.BrowseNames.Format,
                Ai.BrowseNames.Digest, Ai.BrowseNames.DigestAlgorithm, Ai.BrowseNames.CreatedAt,
                Ai.BrowseNames.LastModifiedAt, Ai.BrowseNames.Publisher
            ]).ConfigureAwait(false);
            await AssertReadAsync(harness.Channel.Reads[1], cardId,
            [
                Ai.BrowseNames.IntendedUse, Ai.BrowseNames.DataJurisdiction, Ai.BrowseNames.SafetyAssessment
            ]).ConfigureAwait(false);
            await AssertPathAsync(harness.Channel.Paths[11], s_modelId, Ai.BrowseNames.Card,
                ReferenceTypeIds.HasComponent).ConfigureAwait(false);
            await Assert.That(harness.Channel.Browses.Count).IsEqualTo(1);
            await Assert.That(harness.Channel.Browses[0].ReferenceTypeId)
                .IsEqualTo(new NodeId(Ai.ReferenceTypes.ImportedFrom, kAiNamespaceIndex));
        }

        [Test]
        public async Task MissingGeneratedCardDoesNotIssueAReadAsync()
        {
            using var harness = new AIClientHarness();

            AIModelCardSnapshot card = await harness.Client.Model(s_modelId).ReadCardAsync().ConfigureAwait(false);

            await Assert.That(card.NodeId.IsNull).IsTrue();
            await Assert.That(card.IntendedUse).IsNull();
            await Assert.That(card.SafetyAssessment.Count).IsEqualTo(0);
            await Assert.That(harness.Channel.Paths.Count).IsEqualTo(1);
            await AssertPathAsync(harness.Channel.Paths[0], s_modelId, Ai.BrowseNames.Card,
                ReferenceTypeIds.HasComponent).ConfigureAwait(false);
            await Assert.That(harness.Channel.Reads.Count).IsEqualTo(0);
            await Assert.That(harness.Channel.RequestCount).IsEqualTo(1);
        }

        [Test]
        [Arguments(false)]
        [Arguments(true)]
        public async Task DeploymentReadDecodesSignedAndUnsignedEnumsAsync(bool unsignedEnums)
        {
            using var harness = new AIClientHarness();
            var fallbackId = new NodeId("deployment.fallback", kInstanceNamespaceIndex);
            harness.Channel.AddValue(s_deploymentId, Ai.BrowseNames.DeploymentId, "local-inspection");
            harness.Channel.AddValue(s_deploymentId, Ai.BrowseNames.InferenceLocation,
                unsignedEnums ? Variant.From(3u) : Variant.From(3));
            harness.Channel.AddValue(s_deploymentId, Ai.BrowseNames.State,
                unsignedEnums ? Variant.From(4u) : Variant.From(4));
            harness.Channel.AddValue(s_deploymentId, Ai.BrowseNames.DataJurisdiction, "EU");
            harness.Channel.AddValue(s_deploymentId, Ai.BrowseNames.EgressPermitted, unsignedEnums);
            harness.Channel.AddValue(s_deploymentId, Ai.BrowseNames.MaxInlinePayloadSize, 8192UL);
            harness.Channel.AddValue(s_deploymentId, Ai.BrowseNames.EndpointUri, "urn:ai-aot:in-process");
            harness.Channel.AddBrowse(s_deploymentId, new NodeId(Ai.ReferenceTypes.UsesModel, kAiNamespaceIndex),
                [Reference(s_modelId, "Inspection model", Ai.ObjectTypes.ModelType)]);
            harness.Channel.AddBrowse(s_deploymentId, new NodeId(Ai.ReferenceTypes.FallsBackTo, kAiNamespaceIndex),
                [Reference(fallbackId, "Fallback", Ai.ObjectTypes.DeploymentType)]);

            AIDeploymentSnapshot snapshot = await harness.Client.Deployment(s_deploymentId)
                .ReadAsync().ConfigureAwait(false);

            await Assert.That(snapshot.NodeId).IsEqualTo(s_deploymentId);
            await Assert.That(snapshot.DeploymentId).IsEqualTo("local-inspection");
            await Assert.That(snapshot.InferenceLocation).IsEqualTo(Ai.InferenceLocationEnum.InSimulator);
            await Assert.That(snapshot.State).IsEqualTo(Ai.DeploymentStateEnum.Faulted);
            await Assert.That(snapshot.DataJurisdiction).IsEqualTo("EU");
            await Assert.That(snapshot.EgressPermitted).IsEqualTo(unsignedEnums);
            await Assert.That(snapshot.MaxInlinePayloadSize).IsEqualTo(8192UL);
            await Assert.That(snapshot.EndpointUri).IsEqualTo("urn:ai-aot:in-process");
            await Assert.That(snapshot.ModelId).IsEqualTo(s_modelId);
            await Assert.That(snapshot.FallbackDeploymentId).IsEqualTo(fallbackId);
            await Assert.That(harness.Channel.Reads.Count).IsEqualTo(1);
            await AssertReadAsync(harness.Channel.Reads[0], s_deploymentId,
            [
                Ai.BrowseNames.DeploymentId, Ai.BrowseNames.InferenceLocation, Ai.BrowseNames.State,
                Ai.BrowseNames.DataJurisdiction, Ai.BrowseNames.EgressPermitted,
                Ai.BrowseNames.MaxInlinePayloadSize, Ai.BrowseNames.EndpointUri
            ]).ConfigureAwait(false);
            await Assert.That(harness.Channel.Browses.Count).IsEqualTo(2);
            await Assert.That(harness.Channel.Browses[0].ReferenceTypeId)
                .IsEqualTo(new NodeId(Ai.ReferenceTypes.UsesModel, kAiNamespaceIndex));
            await Assert.That(harness.Channel.Browses[1].ReferenceTypeId)
                .IsEqualTo(new NodeId(Ai.ReferenceTypes.FallsBackTo, kAiNamespaceIndex));
        }

        [Test]
        public async Task GeneratedCapabilitiesCallDecodesBinaryStructureArrayAsync()
        {
            using var harness = new AIClientHarness();
            // Fixed OPC UA binary bodies, not instances of the generated classes:
            // the AIClient constructor must register the factory activators.
            ArrayOf<ExtensionObject> encoded =
            [
                BinaryStructure(5004, "040000006368617401"),
                BinaryStructure(5004, "06000000766973696F6E00")
            ];
            harness.Channel.EnqueueCall(StatusCodes.Good, [Variant.From(encoded)]);

            ArrayOf<Ai.CapabilityDataType> capabilities = await harness.Client.Deployment(s_deploymentId)
                .GetCapabilitiesAsync().ConfigureAwait(false);

            await Assert.That(harness.HadAiTypesBeforeConstruction).IsFalse();
            await Assert.That(capabilities.Count).IsEqualTo(2);
            await Assert.That(capabilities[0].Name).IsEqualTo("chat");
            await Assert.That(capabilities[0].Supported).IsTrue();
            await Assert.That(capabilities[1].Name).IsEqualTo("vision");
            await Assert.That(capabilities[1].Supported).IsFalse();
            await Assert.That(harness.Channel.Calls.Count).IsEqualTo(1);
            await Assert.That(harness.Channel.Calls[0].ObjectId).IsEqualTo(s_deploymentId);
            await Assert.That(harness.Channel.Calls[0].MethodId)
                .IsEqualTo(new NodeId(Ai.Methods.DeploymentType_GetCapabilities, kAiNamespaceIndex));
            await Assert.That(harness.Channel.Calls[0].InputArguments.Count).IsEqualTo(0);
            await Assert.That(harness.Channel.Paths.Count).IsEqualTo(0);
        }

        [Test]
        [Arguments(false)]
        [Arguments(true)]
        public async Task InvokeDecodesAllOutputsAndSupportsInstanceFallbackAsync(bool instanceFallback)
        {
            using var harness = new AIClientHarness();
            using var cancellation = new CancellationTokenSource();
            var instanceMethodId = new NodeId("deployment.invoke", kInstanceNamespaceIndex);
            if (instanceFallback)
            {
                // The generated HasComponent fallback cannot resolve this server's
                // method; AIClientOperations must resolve its hierarchical browse name.
                harness.Channel.AddChild(s_deploymentId, Ai.BrowseNames.Invoke, instanceMethodId);
                harness.Channel.EnqueueCall(StatusCodes.BadMethodInvalid, []);
            }
            harness.Channel.EnqueueCall(StatusCodes.Good, InvokeOutputs(unsignedFinishReason: instanceFallback));
            ArrayOf<KeyValuePair> parameters =
            [
                new KeyValuePair { Key = new QualifiedName("temperature"), Value = Variant.From(0.25) }
            ];

            AIInvokeResult result = await harness.Client.Deployment(s_deploymentId).InvokeAsync(
                ByteString.From([0x10, 0x20]),
                instanceFallback ? null : "application/json",
                parameters,
                12.5,
                instanceFallback ? null : "urn:ai-aot:request",
                cancellation.Token).ConfigureAwait(false);

            await Assert.That(result.ResponsePayload).IsEqualTo(ByteString.From([0x7B, 0x7D]));
            await Assert.That(result.ResponseContentType).IsEqualTo("application/json");
            await Assert.That(result.ModelUsed).IsEqualTo(s_modelId);
            await Assert.That(result.Usage.UnitKind).IsEqualTo("tokens");
            await Assert.That(result.Usage.InputUnits).IsEqualTo(11UL);
            await Assert.That(result.Usage.OutputUnits).IsEqualTo(7UL);
            // Metered total is intentionally not InputUnits + OutputUnits.
            await Assert.That(result.Usage.TotalUnits).IsEqualTo(15UL);
            await Assert.That(result.FinishReason).IsEqualTo(Ai.FinishReasonEnum.Filtered);
            await AssertSafetyAsync(result.SafetyAssessment).ConfigureAwait(false);
            await Assert.That(result.RetryAfter).IsEqualTo(0.75);
            await Assert.That(result.TransferRequired).IsTrue();
            await Assert.That(result.TransferId).IsEqualTo(new NodeId("transfer", kInstanceNamespaceIndex));
            await Assert.That(harness.Channel.Calls.Count).IsEqualTo(instanceFallback ? 2 : 1);
            await Assert.That(harness.Channel.Calls[0].MethodId)
                .IsEqualTo(new NodeId(Ai.Methods.DeploymentType_Invoke, kAiNamespaceIndex));
            ArrayOf<CallMethodRequest> calls = harness.Channel.Calls;
            for (int ii = 0; ii < calls.Count; ii++)
            {
                await AssertInvokeArgumentsAsync(calls[ii], harness.Session.MessageContext, instanceFallback)
                    .ConfigureAwait(false);
            }
            await Assert.That(harness.Channel.LastCancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(harness.Channel.Paths.Count).IsEqualTo(instanceFallback ? 2 : 0);
            if (instanceFallback)
            {
                await Assert.That(calls[1].MethodId).IsEqualTo(instanceMethodId);
                await AssertPathAsync(harness.Channel.Paths[0], s_deploymentId, Ai.BrowseNames.Invoke,
                    ReferenceTypeIds.HasComponent).ConfigureAwait(false);
                await AssertPathAsync(harness.Channel.Paths[1], s_deploymentId, Ai.BrowseNames.Invoke,
                    ReferenceTypeIds.HierarchicalReferences).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task BadCallStatusIsPreservedWithoutRetryAsync()
        {
            using var harness = new AIClientHarness();
            harness.Channel.EnqueueCall(StatusCodes.BadUserAccessDenied, InvokeOutputs());

            ServiceResultException exception = await Assert.That(async () =>
            {
                await harness.Client.Deployment(s_deploymentId).InvokeAsync(
                    ByteString.Empty, "application/json", [], 1).ConfigureAwait(false);
            }).Throws<ServiceResultException>();

            await Assert.That(exception.StatusCode).IsEqualTo(StatusCodes.BadUserAccessDenied);
            await Assert.That(harness.Channel.Calls.Count).IsEqualTo(1);
            await Assert.That(harness.Channel.Calls[0].ObjectId).IsEqualTo(s_deploymentId);
            await Assert.That(harness.Channel.Calls[0].MethodId)
                .IsEqualTo(new NodeId(Ai.Methods.DeploymentType_Invoke, kAiNamespaceIndex));
            await Assert.That(harness.Channel.Paths.Count).IsEqualTo(0);
        }

        [Test]
        public async Task MissingInstanceMethodPreservesBadMethodInvalidAsync()
        {
            using var harness = new AIClientHarness();
            harness.Channel.EnqueueCall(StatusCodes.BadMethodInvalid, []);

            ServiceResultException exception = await Assert.That(async () =>
            {
                await harness.Client.Deployment(s_deploymentId).InvokeAsync(
                    ByteString.Empty, "application/json", [], 1).ConfigureAwait(false);
            }).Throws<ServiceResultException>();

            await Assert.That(exception.StatusCode).IsEqualTo(StatusCodes.BadMethodInvalid);
            await Assert.That(harness.Channel.Calls.Count).IsEqualTo(1);
            await Assert.That(harness.Channel.Calls[0].ObjectId).IsEqualTo(s_deploymentId);
            await Assert.That(harness.Channel.Paths.Count).IsEqualTo(2);
            await AssertPathAsync(harness.Channel.Paths[0], s_deploymentId, Ai.BrowseNames.Invoke,
                ReferenceTypeIds.HasComponent).ConfigureAwait(false);
            await AssertPathAsync(harness.Channel.Paths[1], s_deploymentId, Ai.BrowseNames.Invoke,
                ReferenceTypeIds.HierarchicalReferences).ConfigureAwait(false);
        }

        [Test]
        public async Task BadReadServiceStatusDoesNotReturnAPartialModelAsync()
        {
            using var harness = new AIClientHarness();
            harness.Channel.AddValue(s_modelId, Ai.BrowseNames.ModelId, "must-not-be-returned");
            harness.Channel.ReadServiceStatus = StatusCodes.BadSessionClosed;

            ServiceResultException exception = await Assert.That(async () =>
            {
                await harness.Client.Model(s_modelId).ReadAsync().ConfigureAwait(false);
            }).Throws<ServiceResultException>();

            await Assert.That(exception.StatusCode).IsEqualTo(StatusCodes.BadSessionClosed);
            await Assert.That(harness.Channel.Reads.Count).IsEqualTo(1);
            await AssertReadAsync(harness.Channel.Reads[0], s_modelId, [Ai.BrowseNames.ModelId])
                .ConfigureAwait(false);
            await Assert.That(harness.Channel.Paths.Count).IsEqualTo(11);
            await Assert.That(harness.Channel.Browses.Count).IsEqualTo(0);
        }

        [Test]
        public async Task CancelledDiscoveryDoesNotBrowseAsync()
        {
            using var harness = new AIClientHarness();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            OperationCanceledException exception = await Assert.That(async () =>
            {
                await harness.Client.DiscoverModelsAsync(cancellation.Token).ConfigureAwait(false);
            }).Throws<OperationCanceledException>();

            await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(harness.Channel.Browses.Count).IsEqualTo(0);
            await Assert.That(harness.Channel.Reads.Count).IsEqualTo(0);
            await Assert.That(harness.Channel.Calls.Count).IsEqualTo(0);
        }

        [Test]
        public async Task CancellationDuringInvokePropagatesWithoutFallbackAsync()
        {
            using var harness = new AIClientHarness();
            using var cancellation = new CancellationTokenSource();
            harness.Channel.CancelOnCall = cancellation;

            OperationCanceledException exception = await Assert.That(async () =>
            {
                await harness.Client.Deployment(s_deploymentId).InvokeAsync(
                    ByteString.From([0x42]), "application/json", [], 5,
                    cancellationToken: cancellation.Token).ConfigureAwait(false);
            }).Throws<OperationCanceledException>();

            await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(cancellation.IsCancellationRequested).IsTrue();
            await Assert.That(harness.Channel.LastCancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(harness.Channel.Calls.Count).IsEqualTo(1);
            await Assert.That(harness.Channel.Calls[0].ObjectId).IsEqualTo(s_deploymentId);
            await Assert.That(harness.Channel.Calls[0].MethodId)
                .IsEqualTo(new NodeId(Ai.Methods.DeploymentType_Invoke, kAiNamespaceIndex));
            await Assert.That(harness.Channel.Paths.Count).IsEqualTo(0);
            await Assert.That(harness.Channel.RequestCount).IsEqualTo(1);
        }

        [Test]
        public async Task InvalidConstructionFailsBeforeAnyServiceRequestAsync()
        {
            using var harness = new AIClientHarness();

            ArgumentNullException sessionError = await Assert.That(
                () => new AIClient(null, harness.Client.Telemetry)).Throws<ArgumentNullException>();
            ArgumentNullException telemetryError = await Assert.That(
                () => new AIClient(harness.Session, null)).Throws<ArgumentNullException>();
            ArgumentException modelError = await Assert.That(
                () => harness.Client.Model(NodeId.Null)).Throws<ArgumentException>();
            ArgumentException deploymentError = await Assert.That(
                () => harness.Client.Deployment(NodeId.Null)).Throws<ArgumentException>();

            await Assert.That(sessionError.ParamName).IsEqualTo("session");
            await Assert.That(telemetryError.ParamName).IsEqualTo("telemetry");
            await Assert.That(modelError.ParamName).IsEqualTo("modelNodeId");
            await Assert.That(deploymentError.ParamName).IsEqualTo("deploymentNodeId");
            await Assert.That(harness.Channel.RequestCount).IsEqualTo(0);
        }

        private static async Task AssertReadAsync(ReadRequest request, NodeId parent, ArrayOf<string> names)
        {
            await Assert.That(request.MaxAge).IsEqualTo(0.0);
            await Assert.That(request.TimestampsToReturn).IsEqualTo(TimestampsToReturn.Both);
            await Assert.That(request.NodesToRead.Count).IsEqualTo(names.Count);
            for (int ii = 0; ii < names.Count; ii++)
            {
                ReadValueId read = request.NodesToRead[ii];
                await Assert.That(read.NodeId).IsEqualTo(ValueNodeId(parent, names[ii]));
                await Assert.That(read.AttributeId).IsEqualTo(Attributes.Value);
            }
        }

        private static async Task AssertSafetyAsync(ArrayOf<Ai.SafetyAssessmentDataType> safety)
        {
            await Assert.That(safety.Count).IsEqualTo(1);
            await Assert.That(safety[0].Category).IsEqualTo("policy");
            await Assert.That(safety[0].Severity).IsEqualTo(Ai.SafetySeverityEnum.High);
            await Assert.That(safety[0].Filtered).IsTrue();
            await Assert.That(safety[0].Detail).IsEqualTo("blocked");
        }

        private static async Task AssertInvokeArgumentsAsync(
            CallMethodRequest call,
            IServiceMessageContext messageContext,
            bool normalizedStrings)
        {
            await Assert.That(call.ObjectId).IsEqualTo(s_deploymentId);
            await Assert.That(call.InputArguments.Count).IsEqualTo(5);
            await Assert.That(call.InputArguments[0].TryGetValue(out ByteString payload)).IsTrue();
            await Assert.That(payload).IsEqualTo(ByteString.From([0x10, 0x20]));
            await Assert.That(call.InputArguments[1].TryGetValue(out string uri)).IsTrue();
            await Assert.That(uri).IsEqualTo(normalizedStrings ? string.Empty : "urn:ai-aot:request");
            await Assert.That(call.InputArguments[2].TryGetValue(out string contentType)).IsTrue();
            await Assert.That(contentType).IsEqualTo(normalizedStrings ? string.Empty : "application/json");
            await Assert.That(call.InputArguments[3].TryGetValue(
                out ArrayOf<KeyValuePair> parameters, messageContext)).IsTrue();
            await Assert.That(parameters.Count).IsEqualTo(1);
            await Assert.That(parameters[0].Key).IsEqualTo(new QualifiedName("temperature"));
            await Assert.That(parameters[0].Value.TryGetValue(out double temperature)).IsTrue();
            await Assert.That(temperature).IsEqualTo(0.25);
            await Assert.That(call.InputArguments[4].TryGetValue(out double timeout)).IsTrue();
            await Assert.That(timeout).IsEqualTo(12.5);
        }

        private static ArrayOf<Variant> InvokeOutputs(bool unsignedFinishReason = false)
        {
            return
            [
                Variant.From(ByteString.From([0x7B, 0x7D])),
                Variant.From("application/json"),
                Variant.From(s_modelId),
                Variant.From(BinaryStructure(5003,
                    "06000000746F6B656E730B0000000000000007000000000000000F00000000000000")),
                unsignedFinishReason ? Variant.From(3u) : Variant.From(3),
                Variant.From(ArrayOf.Wrapped([SafetyAssessment()])),
                Variant.From(0.75),
                Variant.From(true),
                Variant.From(new NodeId("transfer", kInstanceNamespaceIndex))
            ];
        }

        private static ExtensionObject SafetyAssessment()
        {
            return BinaryStructure(5005, "06000000706F6C696379030000000107000000626C6F636B6564");
        }

        private static ExtensionObject BinaryStructure(uint encodingId, string body)
        {
            return new ExtensionObject(
                new ExpandedNodeId(encodingId, kAiNamespace), ByteString.From(Convert.FromHexString(body)));
        }

        private static NodeId ValueNodeId(NodeId parent, string name)
        {
            return new NodeId($"{parent}/{name}", kInstanceNamespaceIndex);
        }

        private static async Task AssertPathAsync(
            BrowsePath path,
            NodeId parent,
            string browseName,
            NodeId referenceType)
        {
            await Assert.That(path.StartingNode).IsEqualTo(parent);
            await Assert.That(path.RelativePath.Elements.Count).IsEqualTo(1);
            RelativePathElement element = path.RelativePath.Elements[0];
            await Assert.That(element.TargetName).IsEqualTo(new QualifiedName(browseName, kAiNamespaceIndex));
            await Assert.That(element.ReferenceTypeId).IsEqualTo(referenceType);
            await Assert.That(element.IsInverse).IsFalse();
            await Assert.That(element.IncludeSubtypes).IsTrue();
        }

        private static ReferenceDescription Reference(NodeId nodeId, string name, uint typeId)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(nodeId),
                BrowseName = new QualifiedName(name, kInstanceNamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                NodeClass = NodeClass.Object,
                TypeDefinition = new ExpandedNodeId(typeId, kAiNamespace),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true
            };
        }

        private const string kAiNamespace = "http://opcfoundation.org/UA/AI/";
        private const ushort kAiNamespaceIndex = 4;
        private const ushort kInstanceNamespaceIndex = 3;
        private static readonly NodeId s_rootId = new(Ai.Objects.AiModelManagement, kAiNamespaceIndex);
        private static readonly NodeId s_modelsFolderId = new("catalogue", kInstanceNamespaceIndex);
        private static readonly NodeId s_deploymentsFolderId = new("deployments", kInstanceNamespaceIndex);
        private static readonly NodeId s_modelId = new("model.lens", kInstanceNamespaceIndex);
        private static readonly NodeId s_deploymentId = new("deployment.lens", kInstanceNamespaceIndex);

        private sealed class AIClientHarness : IDisposable
        {
            public AIClientHarness(bool includeAiNamespace = true)
            {
                ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
                Channel = new AITransportChannel(telemetry);
                NamespaceTable namespaces = Channel.MessageContext.NamespaceUris;
                namespaces.GetIndexOrAppend("urn:ai-aot:server");
                namespaces.GetIndexOrAppend("http://opcfoundation.org/UA/AI");
                namespaces.GetIndexOrAppend("urn:ai-aot:instances");
                if (includeAiNamespace)
                {
                    namespaces.GetIndexOrAppend(kAiNamespace);
                }
                Session = new Session(
                    Channel,
                    new ApplicationConfiguration(telemetry)
                    {
                        ClientConfiguration = new ClientConfiguration()
                    },
                    new ConfiguredEndpoint(null, Channel.EndpointDescription, Channel.EndpointConfiguration));
                HadAiTypesBeforeConstruction = Session.Factory.TryGetEncodeableType(
                    new ExpandedNodeId(5004, kAiNamespace), out _);
                Client = new AIClient(Session, telemetry);
                Channel.AddChild(s_rootId, Ai.BrowseNames.Models, s_modelsFolderId);
                Channel.AddChild(s_rootId, Ai.BrowseNames.Deployments, s_deploymentsFolderId);
            }

            public AITransportChannel Channel { get; }

            public Session Session { get; }

            public AIClient Client { get; }

            public bool HadAiTypesBeforeConstruction { get; }

            public void Dispose()
            {
                Session.Dispose();
            }
        }

        /// <summary>
        /// Only the transport boundary is faked. Session batching, managed browse,
        /// AI client operations and generated proxy decoding execute normally.
        /// Unscripted services fail instead of contacting an endpoint.
        /// </summary>
        private sealed class AITransportChannel(ITelemetryContext telemetry) : ITransportChannel
        {
            public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;

            public EndpointDescription EndpointDescription { get; } = new()
            {
                EndpointUrl = "opc.tcp://ai-aot.invalid:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            public EndpointConfiguration EndpointConfiguration { get; } = new();

            public byte[] ChannelThumbprint => [];

            public byte[] ClientChannelCertificate => [];

            public byte[] ServerChannelCertificate => [];

            public IServiceMessageContext MessageContext { get; } = ServiceMessageContext.Create(telemetry);

            public int OperationTimeout { get; set; } = 10000;

            public int RequestCount { get; private set; }

            public ArrayOf<BrowsePath> Paths => m_paths.ToArrayOf();

            public ArrayOf<BrowseDescription> Browses => m_browses.ToArrayOf();

            public ArrayOf<ReadRequest> Reads => m_reads.ToArrayOf();

            public ArrayOf<CallMethodRequest> Calls => m_calls.ToArrayOf();

            public StatusCode ReadServiceStatus { get; set; }

            public CancellationToken LastCancellationToken { get; private set; }

            public CancellationTokenSource CancelOnCall { get; set; }

            public void AddChild(NodeId parent, string browseName, NodeId child, NodeId referenceType = default)
            {
                m_children[(parent, new QualifiedName(browseName, kAiNamespaceIndex),
                    referenceType.IsNull ? ReferenceTypeIds.HierarchicalReferences : referenceType)] = child;
            }

            public void RemoveChild(NodeId parent, string browseName)
            {
                m_children.Remove((parent, new QualifiedName(browseName, kAiNamespaceIndex),
                    ReferenceTypeIds.HierarchicalReferences));
            }

            public void AddBrowse(
                NodeId parent,
                NodeId referenceType,
                ArrayOf<ReferenceDescription> references,
                StatusCode status = default)
            {
                m_references[(parent, referenceType)] = new BrowseResult
                {
                    StatusCode = status,
                    References = references
                };
            }

            public void AddValue(NodeId parent, string browseName, Variant value)
            {
                NodeId nodeId = ValueNodeId(parent, browseName);
                AddChild(parent, browseName, nodeId);
                m_values[nodeId] = new DataValue(value, StatusCodes.Good);
            }

            public void EnqueueCall(StatusCode status, ArrayOf<Variant> outputs)
            {
                m_callResults.Enqueue(new CallMethodResult
                {
                    StatusCode = status,
                    OutputArguments = outputs
                });
            }

            public ValueTask<IServiceResponse> SendRequestAsync(
                IServiceRequest request,
                CancellationToken ct = default)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                RequestCount++;
                LastCancellationToken = ct;
                ct.ThrowIfCancellationRequested();
                IServiceResponse response = request switch
                {
                    TranslateBrowsePathsToNodeIdsRequest translate => Translate(translate),
                    BrowseRequest browse => Browse(browse),
                    ReadRequest read => Read(read),
                    CallRequest call => Call(call, ct),
                    _ => throw new InvalidOperationException("Unscripted service in AI AOT test.")
                };
                response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
                return ValueTask.FromResult(response);
            }

            public ValueTask ReconnectAsync(
                ITransportWaitingConnection connection = null,
                CancellationToken ct = default)
            {
                throw new NotSupportedException("AI AOT tests never connect to a network.");
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Dispose()
            {
                m_disposed = true;
            }

            private TranslateBrowsePathsToNodeIdsResponse Translate(TranslateBrowsePathsToNodeIdsRequest request)
            {
                var results = new List<BrowsePathResult>();
                foreach (BrowsePath path in request.BrowsePaths)
                {
                    m_paths.Add(path);
                    RelativePathElement element = path.RelativePath.Elements[0];
                    bool found = m_children.TryGetValue(
                        (path.StartingNode, element.TargetName, element.ReferenceTypeId), out NodeId child);
                    results.Add(new BrowsePathResult
                    {
                        StatusCode = found ? StatusCodes.Good : StatusCodes.BadNoMatch,
                        Targets = found
                            ? [new BrowsePathTarget { TargetId = new ExpandedNodeId(child),
                                RemainingPathIndex = uint.MaxValue }]
                            : []
                    });
                }
                return new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = results.ToArrayOf()
                };
            }

            private BrowseResponse Browse(BrowseRequest request)
            {
                var results = new List<BrowseResult>();
                foreach (BrowseDescription browse in request.NodesToBrowse)
                {
                    m_browses.Add(browse);
                    results.Add(m_references.TryGetValue(
                        (browse.NodeId, browse.ReferenceTypeId), out BrowseResult result)
                        ? result
                        : new BrowseResult { StatusCode = StatusCodes.Good, References = [] });
                }
                return new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = results.ToArrayOf()
                };
            }

            private ReadResponse Read(ReadRequest request)
            {
                m_reads.Add(request);
                var results = new List<DataValue>();
                foreach (ReadValueId read in request.NodesToRead)
                {
                    results.Add(m_values.TryGetValue(read.NodeId, out DataValue value)
                        ? value
                        : new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown));
                }
                return new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = ReadServiceStatus },
                    Results = results.ToArrayOf()
                };
            }

            private CallResponse Call(CallRequest request, CancellationToken ct)
            {
                var results = new List<CallMethodResult>();
                foreach (CallMethodRequest call in request.MethodsToCall)
                {
                    m_calls.Add(call);
                    CancelOnCall?.Cancel();
                    ct.ThrowIfCancellationRequested();
                    results.Add(m_callResults.Dequeue());
                }
                return new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = results.ToArrayOf()
                };
            }

            private readonly Dictionary<(NodeId Parent, QualifiedName Name, NodeId ReferenceType), NodeId>
                m_children = [];
            private readonly Dictionary<(NodeId Parent, NodeId ReferenceType), BrowseResult> m_references = [];
            private readonly List<BrowsePath> m_paths = [];
            private readonly List<BrowseDescription> m_browses = [];
            private readonly Dictionary<NodeId, DataValue> m_values = [];
            private readonly List<ReadRequest> m_reads = [];
            private readonly List<CallMethodRequest> m_calls = [];
            private readonly Queue<CallMethodResult> m_callResults = [];
            private bool m_disposed;
        }
    }
}
