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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Client;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// Unit tests for the shared <c>TranslateBrowsePaths</c> helper every
    /// WoT Connectivity client wrapper uses to resolve a named child. The
    /// interesting behaviour is what it does with the degenerate
    /// responses a non-conformant server can legitimately return.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class WotConBrowsePathResolverTests
    {
        [Test]
        public async Task ResolveChildReturnsTheTranslatedTargetNodeIdAsync()
        {
            BrowsePath? captured = null;
            var target = new NodeId("WoTRegistry", 3);
            Mock<ISession> session = CreateSession(
                path =>
                {
                    captured = path;
                    return new BrowsePathResult
                    {
                        StatusCode = StatusCodes.Good,
                        Targets = new[]
                        {
                            new BrowsePathTarget
                            {
                                TargetId = target,
                                RemainingPathIndex = uint.MaxValue
                            }
                        }.ToArrayOf()
                    };
                });

            NodeId resolved = await WotConBrowsePathResolver.ResolveChildAsync(
                session.Object,
                Ua.ObjectIds.Server,
                Ua.ReferenceTypeIds.HasComponent,
                3,
                "WoTRegistry",
                StatusCodes.BadNodeIdUnknown,
                "not found",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(resolved, Is.EqualTo(target));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.StartingNode, Is.EqualTo(Ua.ObjectIds.Server));
            Assert.That(captured.RelativePath.Elements, Has.Count.EqualTo(1));
            RelativePathElement element = captured.RelativePath.Elements[0];
            Assert.That(element.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.HasComponent));
            Assert.That(element.IsInverse, Is.False);
            Assert.That(element.IncludeSubtypes, Is.True);
            Assert.That(element.TargetName, Is.EqualTo(new QualifiedName("WoTRegistry", 3)));
        }

        [Test]
        public void ResolveChildThrowsTheSuppliedStatusWhenNoResultIsReturned()
        {
            Mock<ISession> session = CreateSession(_ => null);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ResolveAsync(session).ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(ex.Message, Does.Contain("missing"));
        }

        [Test]
        public void ResolveChildThrowsTheSuppliedStatusWhenNoTargetIsReturned()
        {
            Mock<ISession> session = CreateSession(
                _ => new BrowsePathResult { StatusCode = StatusCodes.Good, Targets = [] });

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ResolveAsync(session).ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
        }

        [Test]
        public void ResolveChildThrowsTheSuppliedStatusWhenTheServerReportsABadResult()
        {
            Mock<ISession> session = CreateSession(
                _ => new BrowsePathResult
                {
                    StatusCode = StatusCodes.BadNoMatch,
                    Targets = new[]
                    {
                        new BrowsePathTarget
                        {
                            TargetId = new NodeId("ignored", 3),
                            RemainingPathIndex = uint.MaxValue
                        }
                    }.ToArrayOf()
                });

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ResolveAsync(session).ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
        }

        [Test]
        public async Task ResolveLogicalResourceSelectsDefaultFromCollidingBrowseNames()
        {
            var telemetry = new Mock<ITelemetryContext>();
            var messageContext = ServiceMessageContext.Create(telemetry.Object);
            ushort wotNs = messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            ushort xRegistryNs = messageContext.NamespaceUris.GetIndexOrAppend(
                XRegistryWellKnown.XRegistryNamespaceUri);
            var group = new NodeId("group", wotNs);
            var nonDefault = new NodeId("non-default", wotNs);
            var expected = new NodeId("default", wotNs);
            var nonDefaultResourceId = new NodeId("non-default/resourceid", xRegistryNs);
            var nonDefaultFlag = new NodeId("non-default/isdefault", wotNs);
            var defaultResourceId = new NodeId("default/resourceid", xRegistryNs);
            var defaultFlag = new NodeId("default/isdefault", wotNs);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>,
                    CancellationToken>((_, _, _, descriptions, _) =>
                {
                    NodeId parent = descriptions[0].NodeId;
                    ReferenceDescription[] references = parent == group
                        ?
                        [
                            Child(nonDefault, "collision", wotNs),
                            Child(expected, "collision", wotNs)
                        ]
                        : parent == nonDefault
                            ?
                            [
                                Child(nonDefaultResourceId, "ResourceId", xRegistryNs),
                                Child(nonDefaultFlag, "IsDefault", wotNs)
                            ]
                            :
                            [
                                Child(defaultResourceId, "ResourceId", xRegistryNs),
                                Child(defaultFlag, "IsDefault", wotNs)
                            ];
                    return new ValueTask<BrowseResponse>(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = new[]
                        {
                            new BrowseResult
                            {
                                StatusCode = StatusCodes.Good,
                                References = references.ToArrayOf()
                            }
                        }.ToArrayOf(),
                        DiagnosticInfos = default
                    });
                });
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>,
                    CancellationToken>((_, _, _, reads, _) =>
                {
                    bool isDefault = reads[1].NodeId == defaultFlag;
                    return new ValueTask<ReadResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = new[]
                        {
                            new DataValue(new Variant("collision")),
                            new DataValue(new Variant(isDefault))
                        }.ToArrayOf(),
                        DiagnosticInfos = default
                    });
                });

            NodeId resolved = await WotConBrowsePathResolver.ResolveLogicalResourceAsync(
                session.Object,
                group,
                wotNs,
                "collision",
                StatusCodes.BadNoMatch,
                "missing",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(resolved, Is.EqualTo(expected));
        }

        private static ReferenceDescription Child(
            NodeId nodeId,
            string browseName,
            ushort namespaceIndex)
        {
            return new ReferenceDescription
            {
                ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                IsForward = true,
                NodeId = nodeId,
                BrowseName = new QualifiedName(browseName, namespaceIndex),
                DisplayName = new LocalizedText(browseName),
                NodeClass = NodeClass.Variable
            };
        }

        private static ValueTask<NodeId> ResolveAsync(Mock<ISession> session)
        {
            return WotConBrowsePathResolver.ResolveChildAsync(
                session.Object,
                Ua.ObjectIds.Server,
                Ua.ReferenceTypeIds.Organizes,
                3,
                "missing",
                StatusCodes.BadNoMatch,
                "Group 'missing' not found in the registry.",
                CancellationToken.None);
        }

        private static Mock<ISession> CreateSession(Func<BrowsePath, BrowsePathResult?> resolve)
        {
            var telemetry = new Mock<ITelemetryContext>();
            var messageContext = ServiceMessageContext.Create(telemetry.Object);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);
            session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) =>
                    {
                        BrowsePathResult? result = resolve(paths[0]);
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = result is null ? [] : new[] { result }.ToArrayOf(),
                                DiagnosticInfos = default
                            });
                    });
            return session;
        }
    }
}
