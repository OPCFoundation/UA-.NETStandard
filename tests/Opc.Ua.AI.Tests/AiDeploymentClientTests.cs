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
using Moq;
using NUnit.Framework;
using Opc.Ua.AI.Client;

namespace Opc.Ua.AI.Tests
{
    [TestFixture]
    [Category("AI")]
    [Category("Client")]
    public sealed class AIDeploymentClientTests
    {
        [Test]
        public void ConstructorWithNullClientThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AIDeploymentClient((AIClient)null!, new NodeId(1u)));
        }

        [Test]
        public void ConstructorWithNullNodeIdThrowsArgumentException()
        {
            var harness = new AISessionHarness();
            Assert.Throws<ArgumentException>(
                () => new AIDeploymentClient(harness.Client, NodeId.Null));
        }

        [Test]
        public void DeploymentNodeIdIsExposed()
        {
            var harness = new AISessionHarness();
            AIDeploymentClient client = harness.Client.Deployment(harness.DeploymentNodeId);

            Assert.That(client.DeploymentNodeId, Is.EqualTo(harness.DeploymentNodeId));
        }

        [Test]
        public async Task ReadAsyncReturnsDeploymentSnapshotAsync()
        {
            var harness = new AISessionHarness();
            NodeId deploymentId = harness.DeploymentNodeId;
            harness.AddValueChild(deploymentId, BrowseNames.DeploymentId, new NodeId(3001u, 3), "dep-1");
            harness.AddValueChild(
                deploymentId,
                BrowseNames.InferenceLocation,
                new NodeId(3002u, 3),
                (int)InferenceLocationEnum.Cloud);
            harness.AddValueChild(
                deploymentId,
                BrowseNames.State,
                new NodeId(3003u, 3),
                (int)DeploymentStateEnum.Degraded);
            harness.AddValueChild(deploymentId, BrowseNames.DataJurisdiction, new NodeId(3004u, 3), "EU");
            harness.AddValueChild(deploymentId, BrowseNames.EgressPermitted, new NodeId(3005u, 3), true);
            harness.AddValueChild(deploymentId, BrowseNames.MaxInlinePayloadSize, new NodeId(3006u, 3), (ulong)1024);
            harness.AddValueChild(deploymentId, BrowseNames.EndpointUri, new NodeId(3007u, 3), "https://example.com");

            AIDeploymentSnapshot snapshot = await harness.Client.Deployment(deploymentId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.NodeId, Is.EqualTo(deploymentId));
                Assert.That(snapshot.DeploymentId, Is.EqualTo("dep-1"));
                Assert.That(snapshot.InferenceLocation, Is.EqualTo(InferenceLocationEnum.Cloud));
                Assert.That(snapshot.State, Is.EqualTo(DeploymentStateEnum.Degraded));
                Assert.That(snapshot.DataJurisdiction, Is.EqualTo("EU"));
                Assert.That(snapshot.EgressPermitted, Is.True);
                Assert.That(snapshot.EndpointUri, Is.EqualTo("https://example.com"));
            });
        }

        [Test]
        public async Task ReadAsyncWithMissingChildrenReturnsDefaultsAsync()
        {
            var harness = new AISessionHarness();

            AIDeploymentSnapshot snapshot = await harness.Client
                .Deployment(harness.DeploymentNodeId).ReadAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.DeploymentId, Is.Null);
                Assert.That(snapshot.EndpointUri, Is.Null);
                Assert.That(snapshot.EgressPermitted, Is.False);
                Assert.That(snapshot.ModelId, Is.EqualTo(NodeId.Null));
                Assert.That(snapshot.FallbackDeploymentId, Is.EqualTo(NodeId.Null));
            });
        }

        [Test]
        public async Task OpenModelAsyncReturnsNullWhenNoModelReferenceAsync()
        {
            var harness = new AISessionHarness();
            AIDeploymentClient client = harness.Client.Deployment(harness.DeploymentNodeId);

            AIModelClient? model = await client.OpenModelAsync().ConfigureAwait(false);

            Assert.That(model, Is.Null);
        }

        [Test]
        public async Task OpenFallbackAsyncReturnsNullWhenNoFallbackAsync()
        {
            var harness = new AISessionHarness();
            AIDeploymentClient client = harness.Client.Deployment(harness.DeploymentNodeId);

            AIDeploymentClient? fallback = await client.OpenFallbackAsync().ConfigureAwait(false);

            Assert.That(fallback, Is.Null);
        }

        [Test]
        public async Task InvokeAsyncWhenTypeMethodSucceedsReturnsAllOutputsAsync()
        {
            var harness = new AISessionHarness();
            NodeId modelId = new(4001u, 3);
            NodeId transferId = new(4002u, 3);
            var usage = new UsageDataType();
            var safety = new SafetyAssessmentDataType();
            SetupCallResults(
                harness,
                CallResult(
                    StatusCodes.Good,
                    [
                        Variant.From(ByteString.From([1, 2, 3])),
                        Variant.From("application/json"),
                        Variant.From(modelId),
                        Variant.FromStructure(usage),
                        Variant.From((int)FinishReasonEnum.Filtered),
                        Variant.FromStructure(ArrayOf.Wrapped([safety])),
                        Variant.From(0.25),
                        Variant.From(true),
                        Variant.From(transferId)
                    ]));

            AIInvokeResult result = await harness.Client.Deployment(harness.DeploymentNodeId)
                .InvokeAsync(
                    ByteString.From([9]),
                    "application/json",
                    [],
                    5.0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ResponsePayload.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(result.ResponseContentType, Is.EqualTo("application/json"));
                Assert.That(result.ModelUsed, Is.EqualTo(modelId));
                Assert.That(result.Usage, Is.Not.Null);
                Assert.That(result.FinishReason, Is.EqualTo(FinishReasonEnum.Filtered));
                Assert.That(result.SafetyAssessment, Has.Count.EqualTo(1));
                Assert.That(result.RetryAfter, Is.EqualTo(0.25));
                Assert.That(result.TransferRequired, Is.True);
                Assert.That(result.TransferId, Is.EqualTo(transferId));
            });
        }

        [Test]
        public async Task InvokeAsyncWhenTypeMethodIsRejectedUsesResolvedInstanceMethodAsync()
        {
            var harness = new AISessionHarness();
            NodeId instanceMethodId = new(4100u, 3);
            NodeId modelId = new(4101u, 3);
            NodeId transferId = new(4102u, 3);
            var usage = new UsageDataType();
            var safety = new SafetyAssessmentDataType();
            SetupFirstResolutionMissingThenPresent(
                harness,
                instanceMethodId,
                harness.DeploymentNodeId,
                BrowseNames.Invoke,
                harness.AINamespaceIndex);
            List<CallMethodRequest> calls = SetupCallResults(
                harness,
                CallResult(StatusCodes.BadMethodInvalid),
                CallResult(
                    StatusCodes.Good,
                    [
                        Variant.From(ByteString.From([4, 5])),
                        Variant.From("application/octet-stream"),
                        Variant.From(modelId),
                        Variant.FromStructure(usage),
                        Variant.From((uint)FinishReasonEnum.ToolCall),
                        Variant.FromStructure(ArrayOf.Wrapped([safety])),
                        Variant.From(1.5),
                        Variant.From(true),
                        Variant.From(transferId)
                    ]));

            AIInvokeResult result = await harness.Client.Deployment(harness.DeploymentNodeId)
                .InvokeAsync(
                    ByteString.From([7]),
                    null!,
                    [],
                    10.0,
                    null!)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Has.Count.EqualTo(2));
                Assert.That(calls[1].MethodId, Is.EqualTo(instanceMethodId));
                Assert.That(calls[1].InputArguments[1].GetString(), Is.EqualTo(string.Empty));
                Assert.That(calls[1].InputArguments[2].GetString(), Is.EqualTo(string.Empty));
                Assert.That(result.ResponsePayload.Span.ToArray(), Is.EqualTo(new byte[] { 4, 5 }));
                Assert.That(result.ResponseContentType, Is.EqualTo("application/octet-stream"));
                Assert.That(result.ModelUsed, Is.EqualTo(modelId));
                Assert.That(result.Usage, Is.Not.Null);
                Assert.That(result.FinishReason, Is.EqualTo(FinishReasonEnum.ToolCall));
                Assert.That(result.SafetyAssessment, Has.Count.EqualTo(1));
                Assert.That(result.RetryAfter, Is.EqualTo(1.5));
                Assert.That(result.TransferRequired, Is.True);
                Assert.That(result.TransferId, Is.EqualTo(transferId));
            });
        }

        [Test]
        public async Task InvokeAsyncWhenFallbackReturnsNoOutputsUsesDefaultsAsync()
        {
            var harness = new AISessionHarness();
            SetupFirstResolutionMissingThenPresent(
                harness,
                new NodeId(4200u, 3),
                harness.DeploymentNodeId,
                BrowseNames.Invoke,
                harness.AINamespaceIndex);
            List<CallMethodRequest> calls = SetupCallResults(
                harness,
                CallResult(StatusCodes.BadMethodInvalid),
                CallResult(StatusCodes.Good));

            AIInvokeResult result = await harness.Client.Deployment(harness.DeploymentNodeId)
                .InvokeAsync(
                    ByteString.Empty,
                    string.Empty,
                    [],
                    0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ResponsePayload, Is.EqualTo(ByteString.Empty));
                Assert.That(result.ResponseContentType, Is.Null);
                Assert.That(result.ModelUsed, Is.EqualTo(NodeId.Null));
                Assert.That(result.Usage, Is.Null);
                Assert.That(result.SafetyAssessment, Is.Empty);
                Assert.That(result.RetryAfter, Is.Zero);
                Assert.That(result.TransferRequired, Is.False);
                Assert.That(result.TransferId, Is.EqualTo(NodeId.Null));
                Assert.That(calls, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public async Task GetCapabilitiesAsyncWhenTypeMethodIsRejectedUsesResolvedInstanceMethodAsync()
        {
            var harness = new AISessionHarness();
            NodeId instanceMethodId = new(4300u, 3);
            ArrayOf<CapabilityDataType> expected = [new CapabilityDataType()];
            SetupFirstResolutionMissingThenPresent(
                harness,
                instanceMethodId,
                harness.DeploymentNodeId,
                BrowseNames.GetCapabilities,
                harness.AINamespaceIndex);
            List<CallMethodRequest> calls = SetupCallResults(
                harness,
                CallResult(StatusCodes.BadMethodInvalid),
                CallResult(StatusCodes.Good, [Variant.FromStructure(expected)]));

            ArrayOf<CapabilityDataType> result = await harness.Client
                .Deployment(harness.DeploymentNodeId)
                .GetCapabilitiesAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Has.Count.EqualTo(2));
                Assert.That(calls[1].MethodId, Is.EqualTo(instanceMethodId));
                Assert.That(result, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task GetCapabilitiesAsyncWhenFallbackReturnsNoOutputsReturnsEmptyAsync()
        {
            var harness = new AISessionHarness();
            SetupFirstResolutionMissingThenPresent(
                harness,
                new NodeId(4400u, 3),
                harness.DeploymentNodeId,
                BrowseNames.GetCapabilities,
                harness.AINamespaceIndex);
            List<CallMethodRequest> calls = SetupCallResults(
                harness,
                CallResult(StatusCodes.BadMethodInvalid),
                CallResult(StatusCodes.Good));

            ArrayOf<CapabilityDataType> result = await harness.Client
                .Deployment(harness.DeploymentNodeId)
                .GetCapabilitiesAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Empty);
                Assert.That(calls, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public async Task BeginTransferAsyncWhenTypeMethodIsRejectedUsesResolvedInstanceMethodAsync()
        {
            var harness = new AISessionHarness();
            NodeId instanceMethodId = new(4500u, 3);
            NodeId transferId = new(4501u, 3);
            SetupFirstResolutionMissingThenPresent(
                harness,
                instanceMethodId,
                harness.DeploymentNodeId,
                BrowseNames.BeginTransfer,
                harness.AINamespaceIndex);
            List<CallMethodRequest> calls = SetupCallResults(
                harness,
                CallResult(StatusCodes.BadMethodInvalid),
                CallResult(
                    StatusCodes.Good,
                    [Variant.From(transferId), Variant.From(true)]));

            AIBeginTransferResult result = await harness.Client
                .Deployment(harness.DeploymentNodeId)
                .BeginTransferAsync(null!, 123)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Has.Count.EqualTo(2));
                Assert.That(calls[1].MethodId, Is.EqualTo(instanceMethodId));
                Assert.That(calls[1].InputArguments[0].GetString(), Is.EqualTo(string.Empty));
                Assert.That(result.TransferId, Is.EqualTo(transferId));
                Assert.That(result.Accepted, Is.True);
            });
        }

        [Test]
        public async Task InvokeAsyncAsyncWhenTypeMethodIsRejectedUsesResolvedInstanceMethodAsync()
        {
            var harness = new AISessionHarness();
            NodeId instanceMethodId = new(4600u, 3);
            NodeId jobId = new(4601u, 3);
            SetupFirstResolutionMissingThenPresent(
                harness,
                instanceMethodId,
                harness.DeploymentNodeId,
                BrowseNames.InvokeAsync,
                harness.AINamespaceIndex);
            List<CallMethodRequest> calls = SetupCallResults(
                harness,
                CallResult(StatusCodes.BadMethodInvalid),
                CallResult(StatusCodes.Good, [Variant.From(jobId)]));

            NodeId result = await harness.Client.Deployment(harness.DeploymentNodeId)
                .InvokeAsyncAsync(
                    ByteString.Empty,
                    null!,
                    [],
                    null!)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Has.Count.EqualTo(2));
                Assert.That(calls[1].MethodId, Is.EqualTo(instanceMethodId));
                Assert.That(calls[1].InputArguments[1].GetString(), Is.EqualTo(string.Empty));
                Assert.That(calls[1].InputArguments[2].GetString(), Is.EqualTo(string.Empty));
                Assert.That(result, Is.EqualTo(jobId));
            });
        }

        private static List<CallMethodRequest> SetupCallResults(
            AISessionHarness harness,
            params CallMethodResult[] results)
        {
            int index = 0;
            var calls = new List<CallMethodRequest>();
            harness.Session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        calls.Add(requests[0]);
                        CallMethodResult result = results[Math.Min(index++, results.Length - 1)];
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [result],
                            DiagnosticInfos = []
                        });
                    });
            return calls;
        }

        private static void SetupFirstResolutionMissingThenPresent(
            AISessionHarness harness,
            NodeId instanceMethodId,
            NodeId startingNode,
            string browseName,
            ushort namespaceIndex)
        {
            int callCount = 0;
            harness.Session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) =>
                    {
                        Assert.That(paths, Has.Count.EqualTo(1));
                        BrowsePath path = paths[0];
                        Assert.That(path.StartingNode, Is.EqualTo(startingNode));
                        Assert.That(path.RelativePath.Elements, Has.Count.EqualTo(1));
                        RelativePathElement element = path.RelativePath.Elements[0];
                        Assert.That(element.TargetName, Is.EqualTo(
                            new QualifiedName(browseName, namespaceIndex)));
                        Assert.That(
                            element.ReferenceTypeId,
                            Is.EqualTo(callCount == 0
                                ? global::Opc.Ua.ReferenceTypeIds.HasComponent
                                : global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences));
                        bool found = callCount++ > 0;
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results =
                                [
                                    new BrowsePathResult
                                    {
                                        StatusCode = found
                                            ? StatusCodes.Good
                                            : StatusCodes.BadNoMatch,
                                        Targets = found
                                            ? [new BrowsePathTarget
                                            {
                                                TargetId = new ExpandedNodeId(instanceMethodId)
                                            }]
                                            : []
                                    }
                                ],
                                DiagnosticInfos = []
                            });
                    });
        }

        private static CallMethodResult CallResult(
            StatusCode statusCode,
            ArrayOf<Variant> outputs = default)
        {
            return new CallMethodResult
            {
                StatusCode = statusCode,
                OutputArguments = outputs
            };
        }
    }
}
