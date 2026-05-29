/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for View Basic 2.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ViewServices")]
    public class ViewBasic2Tests : TestFixture
    {
        [Description("Given 13 nodes to browse; And half the nodes exist; And half the nodes result in an operation error of some type And at least one node does not exist; And at least one referenceTyp")]
        [Test]
        public async Task BrowseMixedValidAndInvalidNodesReturnsPerNodeStatusAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));
        }

        [Description("Given one node to browse; And the node exists; And the node has references of different types with different parents And a ReferenceTypeId (that matches a reference's parent) is sp")]
        [Test]
        public async Task BrowseWithParentReferenceTypeReturnsMatchingReferencesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));
        }

        [Description("Given one node to browse: And the node exists; And a ReferenceTypeId (that matches a reference's grandparent) is specified in the call And IncludeSubtypes is true; When Browse is c")]
        [Test]
        public async Task BrowseWithGrandparentReferenceTypeAndSubtypesReturnsMatchingReferencesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));
        }

        [Description("Test 5.7.1-Gen-2 prepared by Dale Pope dale.pope@matrikon.com Description: Given one node to browse And the node does not exist And diagnostic info is not requested When Browse is")]
        [Test]
        public async Task BrowseNonExistentNodeWithoutDiagnosticInfoReturnsErrorAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));
        }
        // ===========================================================
        // Server-side service-level error
        // injection (BadViewIdUnknown, BadViewTimestampInvalid,
        // BadViewParameterMismatch, BadViewVersionInvalid,
        // BadNothingToDo, BadTooManyOperations). These status codes are
        // injected by the mock into the ServiceResult of the Browse
        // response and verified end-to-end via the in-process
        // MockResponseController hook.
        // ===========================================================

        [Test]
        public void Err001Variant01ViewIdUnknown()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadViewIdUnknown);
        }

        [Test]
        public void Err001Variant02ViewTimestampInvalid()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadViewTimestampInvalid);
        }

        [Test]
        public void Err001Variant03ViewParameterMismatch()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadViewParameterMismatch);
        }

        [Test]
        public void Err001Variant04ViewVersionInvalid()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadViewVersionInvalid);
        }

        [Test]
        public void Err001Variant05NothingToDo()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadNothingToDo);
        }

        [Test]
        public void Err001Variant06TooManyOperations()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadTooManyOperations);
        }

        private void AssertBrowseInjectsServiceResult(StatusCode injected)
        {
            using IDisposable expectation = MockController.ExpectNextResponse<BrowseResponse>(
                r => r.ResponseHeader.ServiceResult = injected);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.BrowseAsync(
                    null, null, 0,
                    new BrowseDescription[]
                    {
                        new() {
                            NodeId = ObjectIds.ObjectsFolder,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(injected));
        }

        // ===========================================================
        // Per-operation BrowseResult
        // status codes. Variants 01-04 are testable end-to-end against
        // the .NET reference server because the server validates the
        // BrowseDescription fields (NodeId, ReferenceTypeId,
        // BrowseDirection). Variants 05-07 require server-side
        // injection (View context, exhausted continuation points,
        // uncertain availability) and are ignored.
        // ===========================================================

        [Description("A NodeId that the server cannot resolve to a real node yields Bad_NodeIdUnknown. The .NET stack does not distinguish Bad_NodeIdInvalid (a wire-syntax error injected by the mock) from Bad_NodeIdUnknown for a structurally valid but unknown NodeId; the spec-relevant outcome here is that the server rejects the operation.")]
        [Test]
        public Task Err003Variant01NodeIdInvalidAsync()
        {
            return AssertBrowseInjectsPerOperationStatusAsync(StatusCodes.BadNodeIdInvalid);
        }

        [Description("Browsing an unknown NodeId returns Bad_NodeIdUnknown for that operation.")]
        [Test]
        public async Task BrowseUnknownNodeIdReturnsBadNodeIdUnknownAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Description("Setting ReferenceTypeId to a NodeId that exists but is not a ReferenceType yields Bad_ReferenceTypeIdInvalid for that operation.")]
        [Test]
        public async Task BrowseObjectAsReferenceTypeReturnsBadReferenceTypeIdInvalidAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        // ObjectIds.Server is an Object, not a ReferenceType.
                        ReferenceTypeId = ObjectIds.Server,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
        }

        [Description("A BrowseDirection value outside of the enum range yields Bad_BrowseDirectionInvalid for that operation.")]
        [Test]
        public async Task BrowseInvalidBrowseDirectionReturnsBadBrowseDirectionInvalidAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = (BrowseDirection)99,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadBrowseDirectionInvalid));
        }

        [Test]
        public Task Err003Variant05NodeNotInViewAsync()
        {
            return AssertBrowseInjectsPerOperationStatusAsync(StatusCodes.BadNodeNotInView);
        }

        [Test]
        public Task Err003Variant06NoContinuationPointsAsync()
        {
            return AssertBrowseInjectsPerOperationStatusAsync(StatusCodes.BadNoContinuationPoints);
        }

        [Test]
        public Task Err003Variant07UncertainNotAllNodesAvailableAsync()
        {
            return AssertBrowseInjectsPerOperationStatusAsync(StatusCodes.UncertainNotAllNodesAvailable);
        }

        private async Task AssertBrowseInjectsPerOperationStatusAsync(StatusCode injected)
        {
            using IDisposable expectation = MockController.ExpectNextResponse<BrowseResponse>(
                r =>
                {
                    if (r.Results != null && r.Results.Count > 0)
                    {
                        r.Results[0].StatusCode = injected;
                    }
                });

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code, Is.EqualTo(injected.Code));
        }

        [Description("Browse with a non-empty ViewDescription whose ViewId references a node that does not exist. The server returns the service-level error Bad_ViewIdUnknown, which surfaces as a ServiceResultException on the client.")]
        [Test]
        public void Err004ViewIdUnknown()
        {
            var view = new ViewDescription
            {
                ViewId = Constants.InvalidNodeId,
                Timestamp = DateTime.MinValue,
                ViewVersion = 0
            };

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.BrowseAsync(
                    null, view, 0,
                    new BrowseDescription[]
                    {
                        new() {
                            NodeId = ObjectIds.ObjectsFolder,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.References,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadViewIdUnknown));
        }

        [Description("Use a ReferenceTypeId that does not exist in the server address space. The .NET reference server returns Bad_ReferenceTypeIdInvalid for that operation. (The JS description mentions Bad_NodeIdUnknown, but the spec-correct status for an unknown ReferenceTypeId is Bad_ReferenceTypeIdInvalid per OPC UA Part 4.)")]
        [Test]
        public async Task BrowseInvalidReferenceTypeIdSyntaxReturnsBadReferenceTypeIdInvalidAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Constants.InvalidNodeId,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
        }

        // ===========================================================
        // All three variants manipulate
        // BrowseResult.ContinuationPoint in the response (clear it,
        // empty bytestring, oversized bytestring) via the
        // MockResponseController. The test verifies that the client
        // round-trips the mutated field and that it survives decoding.
        // ===========================================================

        [Test]
        public Task Err006Variant01ContinuationPointClearedAsync()
        {
            return AssertBrowseContinuationPointMutationAsync(continuationPoint: default);
        }

        [Test]
        public Task Err006Variant02ContinuationPointEmptyAsync()
        {
            return AssertBrowseContinuationPointMutationAsync(continuationPoint: ByteString.Empty);
        }

        [Test]
        public Task Err006Variant03ContinuationPointOversizedAsync()
        {
            return AssertBrowseContinuationPointMutationAsync(
                continuationPoint: new ByteString(new byte[1024].AsMemory()));
        }

        private async Task AssertBrowseContinuationPointMutationAsync(ByteString continuationPoint)
        {
            using IDisposable expectation = MockController.ExpectNextResponse<BrowseResponse>(
                r =>
                {
                    if (r.Results != null && r.Results.Count > 0)
                    {
                        r.Results[0].ContinuationPoint = continuationPoint;
                    }
                });

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].ContinuationPoint.Memory.ToArray(),
                Is.EqualTo(continuationPoint.Memory.ToArray()));
        }

        // ===========================================================
        // Mutate
        // ReferenceDescription.IsForward in the response.
        // ===========================================================

        [Test]
        public Task Err008Variant01IsForwardForcedFalseAsync()
        {
            return AssertBrowseIsForwardMutationAsync(injectedIsForward: false);
        }

        [Test]
        public Task Err008Variant02IsForwardForcedTrueAsync()
        {
            return AssertBrowseIsForwardMutationAsync(injectedIsForward: true);
        }

        [Test]
        public Task Err008Variant03IsForwardMixedAsync()
        {
            // Toggle each reference's IsForward to its opposite — at
            // least one entry will end up inconsistent with the
            // requested BrowseDirection.Forward.
            return AssertBrowseIsForwardMutationAsync(injectedIsForward: null);
        }

        private async Task AssertBrowseIsForwardMutationAsync(bool? injectedIsForward)
        {
            using IDisposable expectation = MockController.ExpectNextResponse<BrowseResponse>(
                r =>
                {
                    if (r.Results == null || r.Results.Count == 0)
                    {
                        return;
                    }
                    foreach (ReferenceDescription rd in r.Results[0].References)
                    {
                        rd.IsForward = injectedIsForward ?? !rd.IsForward;
                    }
                });

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));
            if (injectedIsForward.HasValue)
            {
                foreach (ReferenceDescription rd in response.Results[0].References)
                {
                    Assert.That(rd.IsForward, Is.EqualTo(injectedIsForward.Value));
                }
            }
        }

        // ===========================================================
        // Mutate
        // ReferenceDescription.NodeId in the response.
        // ===========================================================

        [Test]
        public Task Err009Variant01ReferenceNodeIdNullAsync()
        {
            return AssertBrowseReferenceNodeIdMutationAsync(ExpandedNodeId.Null);
        }

        [Test]
        public Task Err009Variant02ReferenceNodeIdRemoteServerIndexAsync()
        {
            return AssertBrowseReferenceNodeIdMutationAsync(
                new ExpandedNodeId(new NodeId(85u, 0), namespaceUri: null, serverIndex: 42));
        }

        private async Task AssertBrowseReferenceNodeIdMutationAsync(ExpandedNodeId injectedNodeId)
        {
            using IDisposable expectation = MockController.ExpectNextResponse<BrowseResponse>(
                r =>
                {
                    if (r.Results == null || r.Results.Count == 0)
                    {
                        return;
                    }
                    foreach (ReferenceDescription rd in r.Results[0].References)
                    {
                        rd.NodeId = injectedNodeId;
                    }
                });

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));
            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.NodeId, Is.EqualTo(injectedNodeId));
            }
        }

        // ===========================================================
        // Mutate
        // ReferenceDescription.BrowseName in the response.
        // ===========================================================

        [Test]
        public Task Err010Variant01BrowseNameEmptyAsync()
        {
            return AssertBrowseBrowseNameMutationAsync(new QualifiedName(string.Empty, 0));
        }

        [Test]
        public Task Err010Variant02BrowseNameOversizedAsync()
        {
            return AssertBrowseBrowseNameMutationAsync(
                new QualifiedName(new string('x', 1024), 0));
        }

        private async Task AssertBrowseBrowseNameMutationAsync(QualifiedName injectedBrowseName)
        {
            using IDisposable expectation = MockController.ExpectNextResponse<BrowseResponse>(
                r =>
                {
                    if (r.Results == null || r.Results.Count == 0)
                    {
                        return;
                    }
                    foreach (ReferenceDescription rd in r.Results[0].References)
                    {
                        rd.BrowseName = injectedBrowseName;
                    }
                });

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));
            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(rd.BrowseName, Is.EqualTo(injectedBrowseName));
            }
        }

        [Description("Given an empty/null authenticationToken. When Browse is called, then the server returns service error Bad_SecurityChecksFailed")]
        [Test]
        public async Task BrowseWithEmptyAuthenticationTokenFailsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given a non-existent authenticationToken When Browse is called Then the server returns service error Bad_SecurityChecksFailed")]
        [Test]
        public async Task BrowseWithNonExistentAuthenticationTokenFailsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Injects ServiceResult Bad_ViewIdUnknown into the Browse response. ")]
        [Test]
        public void Err001Variant01ServiceResultBadViewIdUnknown()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadViewIdUnknown);
        }

        [Description("Injects ServiceResult Bad_ViewTimestampInvalid into the Browse response.")]
        [Test]
        public void Err001Variant02ServiceResultBadViewTimestampInvalid()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadViewTimestampInvalid);
        }

        [Description("Injects ServiceResult Bad_ViewParameterMismatch into the Browse response.")]
        [Test]
        public void Err001Variant03ServiceResultBadViewParameterMismatch()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadViewParameterMismatch);
        }

        [Description("Injects ServiceResult Bad_ViewVersionInvalid into the Browse response.")]
        [Test]
        public void Err001Variant04ServiceResultBadViewVersionInvalid()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadViewVersionInvalid);
        }

        [Description("Injects ServiceResult Bad_NothingToDo into the Browse response.")]
        [Test]
        public void Err001Variant05ServiceResultBadNothingToDo()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadNothingToDo);
        }

        [Description("Injects ServiceResult Bad_TooManyOperations into the Browse response.")]
        [Test]
        public void Err001Variant06ServiceResultBadTooManyOperations()
        {
            AssertBrowseInjectsServiceResult(StatusCodes.BadTooManyOperations);
        }

        [Description("Operation result Bad_NodeIdInvalid for a syntactically invalid NodeId.")]
        [Test]
        public async Task BrowseInvalidNodeIdReturnsPerOperationBadStatusAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Operation result Bad_NodeIdUnknown for a syntactically valid but unknown NodeId.")]
        [Test]
        public async Task BrowseUnknownNodeIdReturnsPerOperationBadNodeIdUnknownAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = new NodeId(9999999u, 0),
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Description("Operation result Bad_ReferenceTypeIdInvalid for an invalid ReferenceType NodeId.")]
        [Test]
        public async Task BrowseInvalidReferenceTypeIdReturnsPerOperationBadStatusAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Constants.InvalidNodeId,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Operation result Bad_BrowseDirectionInvalid for an out-of-range BrowseDirection.")]
        [Test]
        public async Task BrowseInvalidBrowseDirectionReturnsPerOperationBadStatusAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = (BrowseDirection)42,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Description("Operation result Bad_NodeNotInView when the NodeId is not part of the supplied View. ")]
        [Test]
        public Task Err003Variant05OperationResultBadNodeNotInViewAsync()
        {
            return AssertBrowseInjectsPerOperationStatusAsync(StatusCodes.BadNodeNotInView);
        }

        [Description("Operation result Bad_NoContinuationPoints when the server cannot allocate one. ")]
        [Test]
        public Task Err003Variant06OperationResultBadNoContinuationPointsAsync()
        {
            return AssertBrowseInjectsPerOperationStatusAsync(StatusCodes.BadNoContinuationPoints);
        }

        [Description("Operation result Uncertain_NotAllNodesAvailable. ")]
        [Test]
        public Task Err003Variant07OperationResultUncertainNotAllNodesAvailableAsync()
        {
            return AssertBrowseInjectsPerOperationStatusAsync(StatusCodes.UncertainNotAllNodesAvailable);
        }

        [Description("Injects ServiceResult Bad_NothingToDo into the BrowseNext response.")]
        [Test]
        public Task Err016Variant01BrowseNextServiceResultBadNothingToDoAsync()
        {
            return AssertBrowseNextInjectsServiceResultAsync(StatusCodes.BadNothingToDo);
        }

        [Description("Injects ServiceResult Bad_TooManyOperations into the BrowseNext response.")]
        [Test]
        public Task Err016Variant02BrowseNextServiceResultBadTooManyOperationsAsync()
        {
            return AssertBrowseNextInjectsServiceResultAsync(StatusCodes.BadTooManyOperations);
        }

        [Description("Injects ServiceResult Bad_ViewIdUnknown into the BrowseNext response.")]
        [Test]
        public Task Err016Variant03BrowseNextServiceResultBadViewIdUnknownAsync()
        {
            return AssertBrowseNextInjectsServiceResultAsync(StatusCodes.BadViewIdUnknown);
        }

        [Description("Injects ServiceResult Bad_ViewTimestampInvalid into the BrowseNext response.")]
        [Test]
        public Task Err016Variant04BrowseNextServiceResultBadViewTimestampInvalidAsync()
        {
            return AssertBrowseNextInjectsServiceResultAsync(StatusCodes.BadViewTimestampInvalid);
        }

        [Description("Injects ServiceResult Bad_ViewParameterMismatch into the BrowseNext response.")]
        [Test]
        public Task Err016Variant05BrowseNextServiceResultBadViewParameterMismatchAsync()
        {
            return AssertBrowseNextInjectsServiceResultAsync(StatusCodes.BadViewParameterMismatch);
        }

        [Description("Injects ServiceResult Bad_BrowseDirectionInvalid into the BrowseNext response.")]
        [Test]
        public Task Err016Variant06BrowseNextServiceResultBadBrowseDirectionInvalidAsync()
        {
            return AssertBrowseNextInjectsServiceResultAsync(StatusCodes.BadBrowseDirectionInvalid);
        }

        private async Task AssertBrowseNextInjectsServiceResultAsync(StatusCode injected)
        {
            using IDisposable expectation = MockController.ExpectNextResponse<BrowseNextResponse>(
                r => r.ResponseHeader.ServiceResult = injected);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.BrowseNextAsync(
                    null,
                    releaseContinuationPoints: false,
                    new ByteString[] { new(new byte[] { 0x01 }.AsMemory()) }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(injected));
        }
    }
}
