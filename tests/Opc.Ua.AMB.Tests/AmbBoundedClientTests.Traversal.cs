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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Tests;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.AMB.Tests
{
    public sealed partial class AmbBoundedClientTests
    {
        [TestCase(16, true, true)]
        [TestCase(4, true, false)]
        [TestCase(16, false, false)]
        public async Task RemoteRelationsRequireVerifiedAssetMembershipAndShareBrowseBudgetAsync(
            int maximumReferences,
            bool hasResolver,
            bool succeeds)
        {
            Mock<ISession> local = CreateSession();
            Mock<ISession> remote = CreateSession();
            var localServers = new StringTable(["urn:test:local", "urn:test:remote"]);
            local.SetupGet(s => s.ServerUris).Returns(localServers);
            remote.SetupGet(s => s.ServerUris).Returns(new StringTable(["urn:test:remote"]));
            var target = new NodeId("confirmed", 4);
            var alias = new NodeId("alias", 4);
            NodeId category = ExpandedNodeId.ToNodeId(
                ObjectIds.AssetsByProductInstanceUri, remote.Object.NamespaceUris);
            ReferenceDescription Relation(string name)
            {
                ReferenceDescription reference = Reference(
                    new ExpandedNodeId(new NodeId(name, 4), InstanceNamespace, 1));
                reference.ReferenceTypeId = Ua.ReferenceTypeIds.Utilizes;
                reference.IsForward = true;
                return reference;
            }
            SetupBrowse(local, request => request.NodeId == s_asset
                ? new BrowseResult
                {
                    References = [Relation("group"), Relation("confirmed"), Relation("confirmed")]
                }
                : new BrowseResult());
            SetupBrowse(remote, request => request.NodeId == category
                ? new BrowseResult { References = [Reference(alias)] }
                : request.NodeId == alias
                    ? new BrowseResult { References = [Reference(target)] }
                    : new BrowseResult());
            var resolver = new Mock<IAmbSessionResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.ResolveAsync("urn:test:remote", It.IsAny<CancellationToken>()))
                .ReturnsAsync(remote.Object);
            var client = new AmbClient(local.Object, NUnitTelemetryContext.Create(), new AmbClientOptions
            {
                SessionResolver = hasResolver ? resolver.Object : null,
                MaximumBrowseReferences = maximumReferences
            });
            if (succeeds)
            {
                ArrayOf<AssetRelationReference> relations = await client.ReadRelationReferencesAsync(s_asset)
                    .ConfigureAwait(false);
                Assert.That(relations.Count, Is.EqualTo(1),
                    "neither grouping Objects nor duplicate relations are assets");
                Assert.That(relations[0], Is.EqualTo(new AssetRelationReference(
                    Ua.ReferenceTypeIds.Utilizes,
                    false,
                    new AssetNodeReference("urn:test:remote", new ExpandedNodeId(target, InstanceNamespace)))));
                ArrayOf<AssetRelation> legacy = await client.ReadRelationsAsync(s_asset).ConfigureAwait(false);
                Assert.That(legacy.Count, Is.EqualTo(3), "published remote references remain unverified");
                Assert.That(legacy[1].Target, Is.EqualTo(new ExpandedNodeId(target, InstanceNamespace, 1)));
            }
            else
            {
                await AssertStatusAsync(
                    () => client.ReadRelationReferencesAsync(s_asset).AsTask(),
                    hasResolver ? StatusCodes.BadEncodingLimitsExceeded : StatusCodes.BadNotSupported)
                    .ConfigureAwait(false);
            }
            Assert.That(localServers.Count, Is.EqualTo(2));
            VerifyNoMutationOrDisposal(local);
            VerifyNoMutationOrDisposal(remote);
        }

        [Test]
        public async Task RemoteSubAssetCandidateWithoutResolverIsNotReportedAsAnAssetAsync()
        {
            Mock<ISession> session = CreateSession();
            var candidate = new ExpandedNodeId(new NodeId("group", 4), null, 1);
            SetupBrowse(session, request => request.NodeId == s_asset
                ? new BrowseResult { References = [Reference(candidate)] }
                : new BrowseResult());
            AmbClient client = CreateClient(session);
            var found = new List<AssetNodeReference>();

            ServiceResultException failure = await FailureAsync<ServiceResultException>(async () =>
            {
                await foreach (AssetNodeReference sub in client.EnumerateSubAssetReferencesAsync(s_asset)
                    .ConfigureAwait(false))
                {
                    found.Add(sub);
                }
            }).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(failure.Message, Does.Contain(candidate.ToString()));
            Assert.That(found, Is.Empty, "a grouping Object has no evidence establishing asset identity");
            VerifyNoMutationOrDisposal(session);
        }

        [TestCase("missing")]
        [TestCase("wrong-server")]
        [TestCase("unknown-server")]
        [TestCase("denied")]
        [TestCase("namespace")]
        [TestCase("browse")]
        public async Task RemoteSubAssetResolutionPropagatesFailuresWithoutReturningCandidatesAsync(
            string scenario)
        {
            StatusCode expected = scenario switch
            {
                "missing" => StatusCodes.BadNotConnected,
                "denied" or "browse" => StatusCodes.BadUserAccessDenied,
                _ => StatusCodes.BadNodeIdInvalid
            };
            Mock<ISession> local = CreateSession();
            Mock<ISession> remote = CreateSession();
            local.SetupGet(s => s.ServerUris).Returns(new StringTable(["urn:test:local", "urn:test:remote"]));
            remote.SetupGet(s => s.ServerUris).Returns(new StringTable(
                [scenario == "wrong-server" ? "urn:test:wrong" : "urn:test:remote"]));
            var candidate = new ExpandedNodeId(
                new NodeId("group", 4),
                scenario == "namespace" ? "urn:test:unknown" : InstanceNamespace,
                scenario == "unknown-server" ? 9u : 1u);
            SetupBrowse(local, request => request.NodeId == s_asset
                ? new BrowseResult { References = [Reference(candidate)] }
                : new BrowseResult());
            SetupBrowse(remote, _ => new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied });
            var resolver = new Mock<IAmbSessionResolver>(MockBehavior.Strict);
            if (scenario == "denied")
            {
                resolver.Setup(r => r.ResolveAsync("urn:test:remote", It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new ServiceResultException(StatusCodes.BadUserAccessDenied));
            }
            else
            {
                resolver.Setup(r => r.ResolveAsync("urn:test:remote", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(scenario == "missing" ? null : remote.Object);
            }
            var client = new AmbClient(local.Object, NUnitTelemetryContext.Create(), new AmbClientOptions
            {
                SessionResolver = resolver.Object
            });
            var found = new List<AssetNodeReference>();

            await AssertStatusAsync(async () =>
            {
                await foreach (AssetNodeReference sub in client.EnumerateSubAssetReferencesAsync(s_asset)
                    .ConfigureAwait(false))
                {
                    found.Add(sub);
                }
            }, expected).ConfigureAwait(false);

            Assert.That(found, Is.Empty);
            VerifyNoMutationOrDisposal(local);
            VerifyNoMutationOrDisposal(remote);
        }

        [Test]
        public async Task RemoteSubAssetResolutionHonorsCancellationAndDoesNotOwnSessionsAsync()
        {
            Mock<ISession> local = CreateSession();
            local.SetupGet(s => s.ServerUris).Returns(new StringTable(["urn:test:local", "urn:test:remote"]));
            SetupBrowse(local, request => request.NodeId == s_asset
                ? new BrowseResult
                {
                    References = [Reference(new ExpandedNodeId(new NodeId("group", 4), InstanceNamespace, 1))]
                }
                : new BrowseResult());
            using var cancellation = new CancellationTokenSource();
            var resolver = new Mock<IAmbSessionResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.ResolveAsync("urn:test:remote", cancellation.Token))
                .Returns((string _, CancellationToken token) =>
                {
                    cancellation.Cancel();
                    return new ValueTask<ISession?>(Task.FromCanceled<ISession?>(token));
                });
            var client = new AmbClient(local.Object, NUnitTelemetryContext.Create(), new AmbClientOptions
            {
                SessionResolver = resolver.Object
            });

            await Assert.ThatAsync(async () =>
            {
                await foreach (AssetNodeReference _ in client
                    .EnumerateSubAssetReferencesAsync(s_asset, cancellation.Token)
                    .ConfigureAwait(false))
                {
                    Assert.Fail("an unresolved candidate must not be yielded");
                }
            }, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            VerifyNoMutationOrDisposal(local);
        }

        [TestCase(32, true)]
        [TestCase(5, false)]
        public async Task RemoteSubAssetsTraverseGroupingObjectsAndNormalizeCyclesWithOneBudgetAsync(
            int maximumReferences,
            bool succeeds)
        {
            Mock<ISession> local = CreateSession();
            Mock<ISession> remote = CreateSession();
            Mock<ISession> third = CreateSession();
            var localServers = new StringTable(["urn:test:local", "urn:test:remote"]);
            var remoteServers = new StringTable(
                ["urn:test:remote", "urn:test:local", "urn:test:unused", "urn:test:third"]);
            local.SetupGet(s => s.ServerUris).Returns(localServers);
            remote.SetupGet(s => s.ServerUris).Returns(remoteServers);
            third.SetupGet(s => s.ServerUris).Returns(new StringTable(["urn:test:third"]));
            remote.Object.NamespaceUris.GetIndexOrAppend("urn:test:other");
            ushort remoteNs = remote.Object.NamespaceUris.GetIndexOrAppend("urn:test:remote:assets");
            var group = new NodeId("group", remoteNs);
            var first = new NodeId("first", remoteNs);
            ushort thirdNs = third.Object.NamespaceUris.GetIndexOrAppend("urn:test:third:assets");
            var nested = new NodeId("nested", thirdNs);
            var localGroup = new NodeId("local-group", 4);
            var alias = new NodeId("remote-alias", remoteNs);
            NodeId category = ExpandedNodeId.ToNodeId(
                ObjectIds.AssetsByProductInstanceUri, remote.Object.NamespaceUris);
            var foreignGroup = new ExpandedNodeId(group, "urn:test:remote:assets", 1);
            SetupBrowse(local, request =>
            {
                if (request.NodeId == s_asset)
                {
                    return new BrowseResult { References = [Reference(localGroup)] };
                }
                return request.NodeId == localGroup
                    ? new BrowseResult { References = [Reference(foreignGroup), Reference(foreignGroup)] }
                    : new BrowseResult();
            });
            var remoteBrowses = new List<NodeId>();
            SetupBrowse(remote, request =>
            {
                remoteBrowses.Add(request.NodeId);
                if (request.NodeId == category)
                {
                    return new BrowseResult { References = [Reference(alias)] };
                }
                if (request.NodeId == alias)
                {
                    return new BrowseResult { References = [Reference(first)] };
                }
                if (request.NodeId == group)
                {
                    return new BrowseResult { References = [Reference(first)] };
                }
                if (request.NodeId == first)
                {
                    return new BrowseResult
                    {
                        References =
                        [
                            Reference(new ExpandedNodeId(nested, "urn:test:third:assets", 3)),
                            Reference(new ExpandedNodeId(s_asset, InstanceNamespace, 1))
                        ]
                    };
                }
                return new BrowseResult();
            });
            NodeId thirdCategory = ExpandedNodeId.ToNodeId(
                ObjectIds.AssetsByProductInstanceUri, third.Object.NamespaceUris);
            var thirdAlias = new NodeId("third-alias", thirdNs);
            SetupBrowse(third, request =>
            {
                if (request.NodeId == thirdCategory)
                {
                    return new BrowseResult { References = [Reference(thirdAlias)] };
                }
                return request.NodeId == thirdAlias
                    ? new BrowseResult { References = [Reference(nested)] }
                    : new BrowseResult();
            });
            var resolver = new Mock<IAmbSessionResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.ResolveAsync("urn:test:remote", It.IsAny<CancellationToken>()))
                .ReturnsAsync(remote.Object);
            resolver.Setup(r => r.ResolveAsync("urn:test:third", It.IsAny<CancellationToken>()))
                .ReturnsAsync(third.Object);
            var client = new AmbClient(local.Object, NUnitTelemetryContext.Create(), new AmbClientOptions
            {
                SessionResolver = resolver.Object,
                MaximumBrowseReferences = maximumReferences
            });
            var found = new List<AssetNodeReference>();
            async Task TraverseAsync()
            {
                await foreach (AssetNodeReference sub in client.EnumerateSubAssetReferencesAsync(s_asset)
                    .ConfigureAwait(false))
                {
                    found.Add(sub);
                }
            }
            if (succeeds)
            {
                await TraverseAsync().ConfigureAwait(false);
                Assert.That(found, Is.EqualTo(new[]
                {
                    new AssetNodeReference("urn:test:remote", new ExpandedNodeId(first, "urn:test:remote:assets")),
                    new AssetNodeReference("urn:test:third", new ExpandedNodeId(nested, "urn:test:third:assets"))
                }));
                Assert.That(remoteBrowses.Count(node => node == group), Is.EqualTo(1));
                Assert.That(localServers.Count, Is.EqualTo(2), "traversal must not synthesize ServerArray entries");
                localServers.Update(["urn:test:local", "urn:test:remote", "urn:test:new-server"]);
                Assert.That(found[1].ServerUri, Is.EqualTo("urn:test:third"),
                    "a server append/refresh must not reinterpret a returned identity as the new server");
                Assert.That(localServers.GetString(2), Is.EqualTo("urn:test:new-server"));
                var legacy = new List<ExpandedNodeId>();
                await foreach (ExpandedNodeId sub in client.EnumerateSubAssetsAsync(s_asset).ConfigureAwait(false))
                {
                    legacy.Add(sub);
                }
                Assert.That(legacy, Does.Contain(new ExpandedNodeId(group, "urn:test:remote:assets", 1)));
                Assert.That(legacy.All(node => node.ServerIndex != 2), Is.True,
                    "reference reporting does not traverse to the nested server");
                Assert.That(localServers.Count, Is.EqualTo(3),
                    "legacy traversal must also leave ServerArray unchanged");
            }
            else
            {
                await AssertStatusAsync(TraverseAsync, StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
                Assert.That(found, Is.Empty);
            }
            resolver.Verify(r => r.ResolveAsync("urn:test:remote", It.IsAny<CancellationToken>()),
                Times.Once);
            VerifyNoMutationOrDisposal(local);
            VerifyNoMutationOrDisposal(remote);
            VerifyNoMutationOrDisposal(third);
        }

        [TestCase(16, true)]
        [TestCase(17, false)]
        public async Task RemoteGroupingTraversalSharesTheOriginatingDepthLimitAsync(int levels, bool succeeds)
        {
            Mock<ISession> local = CreateSession();
            Mock<ISession> remote = CreateSession();
            local.SetupGet(s => s.ServerUris).Returns(new StringTable(["urn:test:local", "urn:test:remote"]));
            remote.SetupGet(s => s.ServerUris).Returns(new StringTable(["urn:test:remote"]));
            SetupBrowse(local, request => request.NodeId == s_asset
                ? new BrowseResult
                {
                    References = [Reference(new ExpandedNodeId(new NodeId(1u, 4), InstanceNamespace, 1))]
                }
                : new BrowseResult());
            int deepest = 0;
            SetupBrowse(remote, request =>
            {
                if (request.NodeId.NamespaceIndex == 4 && request.NodeId.TryGetValue(out uint level))
                {
                    deepest = Math.Max(deepest, (int)level);
                    if (level < levels)
                    {
                        return new BrowseResult { References = [Reference(new NodeId(level + 1, 4))] };
                    }
                }
                return new BrowseResult();
            });
            var resolver = new Mock<IAmbSessionResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.ResolveAsync("urn:test:remote", It.IsAny<CancellationToken>()))
                .ReturnsAsync(remote.Object);
            var client = new AmbClient(local.Object, NUnitTelemetryContext.Create(), new AmbClientOptions
            {
                SessionResolver = resolver.Object
            });
            var found = new List<AssetNodeReference>();
            async Task TraverseAsync()
            {
                await foreach (AssetNodeReference sub in client.EnumerateSubAssetReferencesAsync(s_asset)
                    .ConfigureAwait(false))
                {
                    found.Add(sub);
                }
            }
            if (succeeds)
            {
                await TraverseAsync().ConfigureAwait(false);
            }
            else
            {
                await AssertStatusAsync(TraverseAsync, StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            }
            Assert.That(deepest, Is.EqualTo(16));
            Assert.That(found, Is.Empty, "traversed grouping Objects are not confirmed assets");
            VerifyNoMutationOrDisposal(local);
            VerifyNoMutationOrDisposal(remote);
        }

        [Test]
        public async Task ResolutionRejectsUnmappedNamespaceIndexAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, _ => [Resolved(new NodeId("Health", 100))]);

            await AssertStatusAsync(
                () => CreateClient(session).ReadDeviceHealthValueAsync(s_asset).AsTask(),
                StatusCodes.BadNodeIdInvalid).ConfigureAwait(false);

            VerifyNoRead(session);
        }

        [Test]
        public async Task BrowsedVariablesWithNullIdentityAreNotOptionalAbsenceAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupTranslate(session, _ => [Resolved(s_addIn)]);
            SetupBrowse(session, _ => new BrowseResult { References = [Reference(ExpandedNodeId.Null)] });

            await AssertStatusAsync(
                () => CreateClient(session).ReadDocumentationLinksAsync(s_asset).AsTask(),
                StatusCodes.BadDecodingError).ConfigureAwait(false);

            VerifyNoRead(session);
        }

        [Test]
        public async Task AReadReturningBadNoMatchIsNotAnAbsentPathAsync()
        {
            Mock<ISession> session = CreateSession();
            var source = new NodeId("Health", 4);
            SetupTranslate(session, _ => [Resolved(source)]);
            SetupRead(session, (_, _) => DataValue.FromStatusCode(StatusCodes.BadNoMatch));

            AmbValueReadException failure = await FailureAsync<AmbValueReadException>(
                () => CreateClient(session).ReadDeviceHealthValueAsync(s_asset).AsTask()).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(failure.ReadValue!.IsAbsent, Is.False);
            Assert.That(failure.ReadValue.SourceNodeId, Is.EqualTo(source));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationWhileAwaitingNextReleasesTheSubmittedContinuationAsync(bool emptyRelease)
        {
            Mock<ISession> session = CreateSession();
            using var cancellation = new CancellationTokenSource();
            ByteString point = ByteString.From(71);
            SetupBrowse(session, _ => new BrowseResult { ContinuationPoint = point });
            session.Setup(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), false, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, bool _, ArrayOf<ByteString> points, CancellationToken token) =>
                {
                    Assert.That(points[0], Is.EqualTo(point));
                    Assert.That(token, Is.EqualTo(cancellation.Token));
                    cancellation.Cancel();
                    return new ValueTask<BrowseNextResponse>(Task.FromCanceled<BrowseNextResponse>(token));
                });
            var released = new List<ByteString>();
            SetupRelease(session, released, emptyRelease);

            await Assert.ThatAsync(async () => await CreateClient(session)
                .ReadLocationsOfAssetAsync(s_asset, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(released, Is.EqualTo(new[] { point }));
        }

        [TestCaseSource(nameof(NonGoodStatuses))]
        public async Task FailedBrowseServiceReleasesReturnedContinuationAsync(StatusCode status)
        {
            Mock<ISession> session = CreateSession();
            ByteString point = ByteString.From(72);
            session.Setup(s => s.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = status },
                    Results = [new BrowseResult { ContinuationPoint = point }]
                });
            var released = new List<ByteString>();
            SetupRelease(session, released);

            await AssertStatusAsync(
                () => CreateClient(session).ReadLocationsOfAssetAsync(s_asset).AsTask(), status).ConfigureAwait(false);

            Assert.That(released, Is.EqualTo(new[] { point }));
        }

        [TestCase("status")]
        [TestCase("shape")]
        [TestCase("data")]
        [TestCase("references")]
        [TestCase("empty-diagnostics")]
        [TestCase("single-diagnostics")]
        [TestCase("service")]
        public async Task InvalidCleanupResponsesPreserveThePrimaryFailureAsync(string shape)
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, _ => new BrowseResult
            {
                References = [Reference("one"), Reference("two")],
                ContinuationPoint = ByteString.From(73)
            });
            var result = new BrowseResult();
            if (shape == "status")
            {
                result.StatusCode = StatusCodes.Uncertain;
            }
            else if (shape == "data")
            {
                result.ContinuationPoint = ByteString.From(74);
            }
            else if (shape == "references")
            {
                result.References = [Reference("unexpected")];
            }
            session.Setup(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = shape == "service" ? StatusCodes.Uncertain : StatusCodes.Good
                    },
                    Results = shape switch
                    {
                        "shape" => [result, result],
                        "empty-diagnostics" or "service" => [],
                        _ => [result]
                    },
                    DiagnosticInfos = shape switch
                    {
                        "empty-diagnostics" => [new DiagnosticInfo()],
                        "single-diagnostics" => [new DiagnosticInfo(), new DiagnosticInfo()],
                        _ => []
                    }
                });

            AggregateException failure = await FailureAsync<AggregateException>(
                () => CreateClient(session, 1).ReadLocationsOfAssetAsync(s_asset).AsTask()).ConfigureAwait(false);

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(failure.InnerExceptions[1], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(shape is "status" or "service" ? StatusCodes.Uncertain : StatusCodes.BadDecodingError));
        }

        [Test]
        public async Task InvalidatedContinuationCleanupDoesNotMaskThePrimaryFailureAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, _ => new BrowseResult
            {
                References = [Reference("one"), Reference("two")],
                ContinuationPoint = ByteString.From(75)
            });
            SetupNext(session, (release, _, _) =>
            {
                Assert.That(release, Is.True);
                return new BrowseResult { StatusCode = StatusCodes.BadContinuationPointInvalid };
            });

            await AssertStatusAsync(
                () => CreateClient(session, 1).ReadLocationsOfAssetAsync(s_asset).AsTask(),
                StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
        }

        [Test]
        public async Task LocationQueuesDeduplicateAndSkipNullsAndCyclesAsync()
        {
            Mock<ISession> session = CreateSession();
            var root = new NodeId("root", 4);
            var child = new NodeId("child", 4);
            var browsed = new List<NodeId>();
            SetupBrowse(session, request =>
            {
                browsed.Add(request.NodeId);
                if (request.ReferenceTypeId == Ua.ReferenceTypeIds.Organizes)
                {
                    return new BrowseResult
                    {
                        References = [Reference(root), Reference(root), Reference(ExpandedNodeId.Null)]
                    };
                }
                if (request.ReferenceTypeId == Ua.ReferenceTypeIds.HasComponent)
                {
                    return request.NodeId == root
                        ? new BrowseResult { References = [Reference(child), Reference(child), Reference(root)] }
                        : new BrowseResult { References = [Reference(root)] };
                }
                return new BrowseResult();
            });

            ArrayOf<AssetLocationNode> locations = await CreateClient(session, 7)
                .BrowseLocationsAsync(AssetLocationKind.Hierarchical).ConfigureAwait(false);

            Assert.That(locations.Count, Is.EqualTo(2));
            Assert.That(locations[0].NodeId, Is.EqualTo(root));
            Assert.That(locations[1].NodeId, Is.EqualTo(child));
            Assert.That(browsed.Count(node => node == root), Is.EqualTo(2));
            Assert.That(browsed.Count(node => node == child), Is.EqualTo(2));
            Assert.That(browsed, Does.Not.Contain(NodeId.Null));
        }

        [TestCase(16, true)]
        [TestCase(17, false)]
        public async Task SubAssetTraversalRespectsExactDepthAndRejectsOneBeyondAsync(int depth, bool succeeds)
        {
            Mock<ISession> session = CreateSession();
            SetupBrowse(session, request =>
            {
                if (request.ReferenceTypeId != Ua.ReferenceTypeIds.HierarchicalReferences)
                {
                    return new BrowseResult();
                }
                uint level = request.NodeId.TryGetValue(out uint number) ? number : 0;
                return level < depth
                    ? new BrowseResult { References = [Reference(new NodeId(level + 1, 4))] }
                    : new BrowseResult();
            });
            int found = 0;
            async Task TraverseAsync()
            {
                await foreach (ExpandedNodeId _ in CreateClient(session, 64).EnumerateSubAssetsAsync(s_asset)
                    .ConfigureAwait(false))
                {
                    found++;
                }
            }
            if (succeeds)
            {
                await TraverseAsync().ConfigureAwait(false);
                Assert.That(found, Is.Zero, "hierarchical objects without an AMB alias are not invented as assets");
            }
            else
            {
                await AssertStatusAsync(TraverseAsync, StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ObservationFallsBackWithoutCachingIncompleteClassesAsync(bool maintenance, bool limit)
        {
            Mock<ISession> session = CreateSession();
            var subtype = new NodeId("custom-maintenance-class", 4);
            int rootsBrowsed = 0;
            SetupBrowse(session, request =>
            {
                if (request.NodeId == Ua.ObjectTypeIds.MaintenanceConditionClassType)
                {
                    rootsBrowsed++;
                    return new BrowseResult { References = [Reference(subtype)] };
                }
                Assert.That(request.NodeId, Is.EqualTo(subtype));
                return limit
                    ? new BrowseResult { References = [Reference("over-budget")] }
                    : new BrowseResult { StatusCode = StatusCodes.BadUserAccessDenied };
            });
            var fields = new Variant[AmbConditionFields.Count];
            fields[AmbConditionFields.ConditionId] = Variant.From(s_alarm);
            fields[AmbConditionFields.SourceNode] = Variant.From(s_asset);
            fields[AmbConditionFields.ConditionName] = Variant.From("Observed");
            var streaming = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            EventFilter? observedFilter = null;
            streaming.Setup(subscription => subscription.SubscribeEventsAsync(
                Ua.ObjectIds.Server, It.IsAny<EventFilter>(), null, It.IsAny<CancellationToken>()))
                .Returns((NodeId _, EventFilter filter, MonitoringOptions? _, CancellationToken _) =>
                {
                    observedFilter = filter;
                    return ObserveSingleEventAsync(fields.ToArrayOf());
                });
            AmbClient client = CreateClient(session, 1);
            int received = 0;
            if (maintenance)
            {
                await foreach (MaintenanceActivityRecord activity in client.ObserveMaintenanceAsync(
                    streaming: streaming.Object).ConfigureAwait(false))
                {
                    received++;
                    Assert.That(activity.ConditionId, Is.EqualTo(s_alarm));
                    Assert.That(activity.ConditionName, Is.EqualTo("Observed"));
                    Assert.That(activity.SourceNode, Is.EqualTo(s_asset));
                    Assert.That(activity.Values.Count, Is.Zero);
                }
            }
            else
            {
                await foreach (AssetAlarmRecord alarm in client.ObserveHealthAlarmsAsync(
                    streaming: streaming.Object).ConfigureAwait(false))
                {
                    received++;
                    Assert.That(alarm.ConditionId, Is.EqualTo(s_alarm));
                    Assert.That(alarm.ConditionName, Is.EqualTo("Observed"));
                    Assert.That(alarm.SourceNode, Is.EqualTo(s_asset));
                    Assert.That(alarm.Values.Count, Is.Zero);
                }
            }
            Assert.That(received, Is.EqualTo(1));
            Assert.That(observedFilter, Is.Not.Null);
            HashSet<NodeId> expectedClasses = AmbConditionFields.KnownMaintenanceClasses(session.Object.NamespaceUris);
            expectedClasses.Add(subtype);
            Assert.That(observedFilter, Is.EqualTo(
                AmbConditionFields.Filter(session.Object.NamespaceUris, expectedClasses, maintenance)));

            await AssertStatusAsync(
                () => maintenance
                    ? client.ReadMaintenanceActivitiesAsync(s_asset).AsTask()
                    : client.ReadHealthAlarmsAsync(s_asset).AsTask(),
                limit ? StatusCodes.BadEncodingLimitsExceeded : StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            Assert.That(rootsBrowsed, Is.EqualTo(2), "observation must not cache a partial class set");
            streaming.Verify(subscription => subscription.SubscribeEventsAsync(
                Ua.ObjectIds.Server, It.IsAny<EventFilter>(), null, It.IsAny<CancellationToken>()), Times.Once);
            VerifyNoMutationOrDisposal(session);
        }

        private static async IAsyncEnumerable<EventNotification> ObserveSingleEventAsync(ArrayOf<Variant> fields)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new EventNotification(null, fields);
        }
    }
}
