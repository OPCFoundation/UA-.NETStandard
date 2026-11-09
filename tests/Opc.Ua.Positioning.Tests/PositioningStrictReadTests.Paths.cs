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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Positioning.Client;

namespace Opc.Ua.Positioning.Tests
{
    public sealed partial class PositioningStrictReadTests
    {
        [Test]
        public async Task OnlyGenuineOptionalBadNoMatchIsAbsentAndRequiredChildStillFailsAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, _ => new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch });
            NodeId optional = await ResolveAsync(Operations(session)).ConfigureAwait(false);
            Assert.That(optional.IsNull, Is.True);
            await AssertStatusAsync(
                async () => await Operations(session).ResolveRequiredChildAsync(
                    s_frame,
                    ReferenceTypeIds.HasComponent,
                    new QualifiedName("Base", 1),
                    CancellationToken.None)
                    .ConfigureAwait(false),
                StatusCodes.BadNoMatch).ConfigureAwait(false);
            VerifyNoRead(session);
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task NonGoodPathStatusIsNotReplacedWithAnAbsentChildAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, _ => new BrowsePathResult { StatusCode = status });
            await AssertStatusAsync(async () => await ResolveAsync(Operations(session)).ConfigureAwait(false), status)
                .ConfigureAwait(false);
            await AssertStatusAsync(
                async () => await Relative(session).ReadFrameAsync(s_frame).ConfigureAwait(false),
                status).ConfigureAwait(false);
            VerifyNoRead(session);
        }

        [Test]
        public async Task BadNoMatchWithTargetsIsNotTreatedAsAbsenceAsync()
        {
            Mock<ISession> session = CreateSession();
            BrowsePathResult path = Resolved(s_base);
            path.StatusCode = StatusCodes.BadNoMatch;
            SetupTranslate(session, _ => path);
            await AssertStatusAsync(
                async () => await ResolveAsync(Operations(session)).ConfigureAwait(false),
                StatusCodes.BadNoMatch).ConfigureAwait(false);
        }

        [TestCase("empty")]
        [TestCase("ambiguous")]
        [TestCase("partial")]
        [TestCase("null")]
        public async Task StrictPathsRequireExactlyOneCompleteTargetAsync(string shape)
        {
            Mock<ISession> session = CreateSession();
            BrowsePathResult result = Resolved(s_base);
            result.Targets = shape switch
            {
                "empty" => [],
                "ambiguous" => [result.Targets[0], result.Targets[0]],
                "partial" => [new BrowsePathTarget { TargetId = s_base, RemainingPathIndex = 0 }],
                _ => [null!]
            };
            SetupTranslate(session, _ => result);
            await AssertStatusAsync(
                async () => await Relative(session).ReadFrameAsync(s_frame).ConfigureAwait(false),
                StatusCodes.BadDecodingError).ConfigureAwait(false);
            VerifyNoRead(session);
        }

        [TestCase("remote")]
        [TestCase("uri")]
        [TestCase("index")]
        [TestCase("null")]
        public async Task PathTargetsMustBeLocalAndMappedWithoutNamespaceMutationAsync(string targetKind)
        {
            Mock<ISession> session = CreateSession();
            int namespaceCount = session.Object.NamespaceUris.Count;
            ExpandedNodeId target = targetKind switch
            {
                "remote" => new ExpandedNodeId("base", 0, kInstanceNamespace, 1),
                "uri" => new ExpandedNodeId("base", "urn:not:in:server"),
                "index" => new ExpandedNodeId(new NodeId("base", 99)),
                _ => ExpandedNodeId.Null
            };
            SetupTranslate(
                session,
                _ => new BrowsePathResult
                {
                    Targets = [new BrowsePathTarget { TargetId = target, RemainingPathIndex = uint.MaxValue }]
                });
            await AssertStatusAsync(
                async () => await ResolveAsync(Operations(session)).ConfigureAwait(false),
                targetKind == "remote" ? StatusCodes.BadNotSupported : StatusCodes.BadNodeIdUnknown)
                .ConfigureAwait(false);
            Assert.That(session.Object.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            VerifyNoRead(session);
        }

        [Test]
        public async Task AbsoluteLocalPathResolvesUsingTheExistingNamespaceIndexAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(
                session,
                _ => new BrowsePathResult
                {
                    Targets =
                    [
                        new BrowsePathTarget
                        {
                            TargetId = new ExpandedNodeId("base", kInstanceNamespace),
                            RemainingPathIndex = uint.MaxValue
                        }
                    ]
                });
            NodeId result = await ResolveAsync(Operations(session)).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(s_base));
        }

        [TestCase("header")]
        [TestCase("empty")]
        [TestCase("extra")]
        [TestCase("null")]
        [TestCase("diagnostics")]
        public async Task TranslateRejectsMalformedResponseShapesAsync(string shape)
        {
            Mock<ISession> session = CreateSession();
            session.Setup(
                s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            ServiceResult = shape == "header" ? StatusCodes.BadDecodingError : StatusCodes.Good
                        },
                        Results = shape switch
                        {
                            "empty" => [],
                            "extra" => [Resolved(s_base), Resolved(s_base)],
                            "null" => [null!],
                            _ => [Resolved(s_base)]
                        },
                        DiagnosticInfos = shape == "diagnostics" ? [new DiagnosticInfo(), new DiagnosticInfo()] : []
                    });
            await AssertStatusAsync(
                async () => await ResolveAsync(Operations(session)).ConfigureAwait(false),
                StatusCodes.BadDecodingError).ConfigureAwait(false);
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task TranslateRequiresGoodHeaderEvenForAnOptionalMissingPathAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            session.Setup(
                s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = status },
                        Results = [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }]
                    });
            await AssertStatusAsync(async () => await ResolveAsync(Operations(session)).ConfigureAwait(false), status)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task MissingModelNamespacesAreRejectedByBothClientsWithoutAppendingOrReadingAsync()
        {
            Mock<ISession> session = CreateSession(includeModels: false);
            int count = session.Object.NamespaceUris.Count;
            await AssertStatusAsync(
                async () => await Relative(session).ReadFrameAsync(s_frame).ConfigureAwait(false),
                StatusCodes.BadNodeIdUnknown).ConfigureAwait(false);
            await AssertStatusAsync(
                async () =>
                    await Global(session).ReadZoneTransformAsync(s_zone).ConfigureAwait(false),
                StatusCodes.BadNodeIdUnknown).ConfigureAwait(false);
            await AssertStatusAsync(
                async () =>
                    await CollectAsync(Relative(session).EnumerateSpatialObjectListsAsync()).ConfigureAwait(false),
                StatusCodes.BadNodeIdUnknown).ConfigureAwait(false);
            await AssertStatusAsync(
                async () =>
                    await CollectAsync(Global(session).EnumerateZonesAsync()).ConfigureAwait(false),
                StatusCodes.BadNodeIdUnknown).ConfigureAwait(false);
            Assert.That(session.Object.NamespaceUris.Count, Is.EqualTo(count));
            VerifyNoRead(session);
            VerifyBorrowed(session);
        }

        [TestCase("remote")]
        [TestCase("uri")]
        [TestCase("index")]
        [TestCase("null")]
        public async Task DiscoveryRejectsNonlocalReferencesBeforeTypeLookupAsync(string kind)
        {
            Mock<ISession> session = CreateSession();
            ReferenceDescription reference = Reference();
            reference.NodeId = kind switch
            {
                "remote" => new ExpandedNodeId("frame", 0, kInstanceNamespace, 1),
                "uri" => new ExpandedNodeId("frame", "urn:not:in:server"),
                "index" => new NodeId("frame", 99),
                _ => ExpandedNodeId.Null
            };
            SetupBrowse(session, () => new BrowseResult { References = [reference] });
            await AssertStatusAsync(
                async () =>
                    await CollectAsync(Relative(session).EnumerateFramesAsync(s_frame)).ConfigureAwait(false),
                kind == "remote" ? StatusCodes.BadNotSupported : StatusCodes.BadNodeIdUnknown).ConfigureAwait(false);
            Mock.Get(session.Object.NodeCache).Verify(
                c => c.IsTypeOfAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task DiscoveryCountsNonmatchingReferencesAndRejectsNullReferencesAsync()
        {
            Mock<ISession> session = CreateSession();
            ReferenceDescription other = Reference("other");
            other.TypeDefinition = new ExpandedNodeId(VariableTypeIds.BaseDataVariableType);
            SetupBrowse(session, () => new BrowseResult { References = [other, Reference()] });
            await AssertStatusAsync(
                async () => await CollectAsync(
                    Relative(
                        session,
                        new PositioningClientReadOptions
                        {
                            MaximumBrowseReferences = 1
                        }).EnumerateFramesAsync(s_frame)).ConfigureAwait(false),
                StatusCodes.BadEncodingLimitsExceeded)
                .ConfigureAwait(false);
            SetupBrowse(session, () => new BrowseResult { References = [null!] });
            await AssertStatusAsync(
                async () => await BrowseAsync(session).ConfigureAwait(false),
                StatusCodes.BadDecodingError).ConfigureAwait(false);
        }

        [Test]
        public async Task LegacyResolutionStillChoosesFirstPartialTargetAndTreatsFailedStatusAsAbsentAsync()
        {
            Mock<ISession> session = CreateSession();
            var operations = new PositioningClientOperations(session.Object, Mock.Of<ITelemetryContext>());
            SetupTranslate(
                session,
                _ => new BrowsePathResult
                {
                    Targets =
                    [
                        new BrowsePathTarget { TargetId = s_base, RemainingPathIndex = 0 },
                        new BrowsePathTarget { TargetId = s_frame, RemainingPathIndex = uint.MaxValue }
                    ]
                });
            Assert.That(await ResolveAsync(operations).ConfigureAwait(false), Is.EqualTo(s_base));
            SetupTranslate(session, _ => new BrowsePathResult { StatusCode = StatusCodes.BadUserAccessDenied });
            NodeId missing = await ResolveAsync(operations).ConfigureAwait(false);
            Assert.That(missing.IsNull, Is.True);
        }

        [Test]
        public void LegacyNamespaceLookupStillAppendsMissingModels()
        {
            Mock<ISession> session = CreateSession(includeModels: false);
            int count = session.Object.NamespaceUris.Count;
            var operations = new PositioningClientOperations(session.Object, Mock.Of<ITelemetryContext>());
            ushort rsl = operations.GetNamespaceIndex(Rsl.Namespaces.RSL);
            ushort gpos = operations.GetNamespaceIndex(Gpos.Namespaces.GPOS);
            Assert.Multiple(
                () =>
                {
                    Assert.That(rsl, Is.EqualTo(count));
                    Assert.That(gpos, Is.EqualTo(count + 1));
                    Assert.That(session.Object.NamespaceUris.Count, Is.EqualTo(count + 2));
                });
        }

        private static ValueTask<NodeId> ResolveAsync(PositioningClientOperations operations)
        {
            return operations.ResolveChildAsync(
                s_frame,
                ReferenceTypeIds.HasComponent,
                new QualifiedName("Base", 1),
                CancellationToken.None);
        }
    }
}
