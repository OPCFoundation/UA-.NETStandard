/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GranulateDrying.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.GranulateDrying.Tests
{
    /// <summary>
    /// Strict, port-free service boundary with deliberately shifted model namespace indexes.
    /// </summary>
    internal sealed class GranulateDryingClientPeer
    {
        internal GranulateDryingClientPeer(
            bool includeGdd = true,
            bool includeCommon = true,
            bool includeMachinery = true,
            CancellationToken token = default)
        {
            Token = token;
            Telemetry = NUnitTelemetryContext.Create();
            Context = ServiceMessageContext.Create(Telemetry);
            Context.NamespaceUris.GetIndexOrAppend("urn:gdd-tests:instances");
            Context.NamespaceUris.GetIndexOrAppend("urn:gdd-tests:vendor");
            if (includeCommon)
            {
                Context.NamespaceUris.GetIndexOrAppend(PlasticsRubber.Namespaces.PlasticsRubber);
            }
            Context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            if (includeGdd)
            {
                Context.NamespaceUris.GetIndexOrAppend(Namespaces.GranulateDrying);
            }
            if (includeMachinery)
            {
                Context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Namespaces.Machinery);
            }
            Session.SetupGet(value => value.MessageContext).Returns(Context);
            Session.SetupGet(value => value.NamespaceUris).Returns(Context.NamespaceUris);
            Session.SetupGet(value => value.Factory).Returns(Context.Factory);
            Session.SetupGet(value => value.NodeCache).Returns(Cache.Object);
            Session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits { MaxNodesPerBrowse = 16 });
            Session.SetupGet(value => value.ServerCapabilities).Returns(new ServerCapabilities());
            Session.SetupGet(value => value.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Balanced);
            Client = new GranulateDryingClient(Session.Object, Telemetry);
        }

        internal ITelemetryContext Telemetry { get; }
        internal ServiceMessageContext Context { get; }
        internal Mock<ISession> Session { get; } = new(MockBehavior.Strict);
        internal Mock<INodeCache> Cache { get; } = new(MockBehavior.Strict);
        internal GranulateDryingClient Client { get; }
        internal CancellationToken Token { get; }
        internal static NodeId DeviceId => new("dryer-27", 1);
        internal static DateTimeUtc SourceTime => (DateTimeUtc)new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        internal static DateTimeUtc ServerTime => (DateTimeUtc)new DateTime(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc);

        internal QualifiedName Gdd(string name) => Name(Namespaces.GranulateDrying, name);
        internal QualifiedName Common(string name) => Name(PlasticsRubber.Namespaces.PlasticsRubber, name);
        internal QualifiedName Di(string name) => Name(Opc.Ua.Di.Namespaces.OpcUaDi, name);
        internal QualifiedName Machinery(string name) => Name(Opc.Ua.Machinery.Namespaces.Machinery, name);

        internal QualifiedName Name(string uri, string name) =>
            new(name, checked((ushort)Context.NamespaceUris.GetIndex(uri)));

        internal NodeId NextNode() => new(++m_nextNode, 1);

        internal static DataValue Sample(Variant payload, StatusCode status = default) =>
            new(payload, status, SourceTime, ServerTime);

        internal NodeId ExpectPath(NodeId owner, params QualifiedName[] names)
        {
            return ExpectPath(owner, 1, names);
        }

        internal NodeId ExpectPath(NodeId owner, int count, params QualifiedName[] names)
        {
            NodeId target = NextNode();
            ExpectTranslation(owner, [Target(target)], false, count, names);
            return target;
        }

        internal NodeId ExpectChild(NodeId owner, QualifiedName name, bool present = true, int count = 1)
        {
            NodeId target = present ? NextNode() : NodeId.Null;
            ExpectTranslation(owner, [present ? Target(target) : new BrowsePathResult
            {
                StatusCode = StatusCodes.BadNoMatch
            }], true, count, name);
            return target;
        }

        internal static BrowsePathResult Target(NodeId target) => new()
        {
            Targets = [new BrowsePathTarget { TargetId = target, RemainingPathIndex = uint.MaxValue }]
        };

        internal void ExpectTranslation(
            NodeId owner, ArrayOf<BrowsePathResult> results, bool generated, params QualifiedName[] names)
        {
            ExpectTranslation(owner, results, generated, 1, names);
        }

        private void ExpectTranslation(
            NodeId owner, ArrayOf<BrowsePathResult> results, bool generated, int count, params QualifiedName[] names)
        {
            Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                null, It.Is<ArrayOf<BrowsePath>>(paths => Matches(paths, owner, names)), Token))
                .Returns((RequestHeader _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                {
                    foreach (RelativePathElement element in paths[0].RelativePath.Elements)
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(element.ReferenceTypeId, Is.EqualTo(generated
                                ? Opc.Ua.ReferenceTypeIds.HasComponent
                                : Opc.Ua.ReferenceTypeIds.HierarchicalReferences));
                            Assert.That(element.IncludeSubtypes, Is.True);
                            Assert.That(element.IsInverse, Is.False);
                        });
                    }
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                        new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results
                        });
                });
            m_verify.Add(() => Session.Verify(session => session.TranslateBrowsePathsToNodeIdsAsync(
                null, It.Is<ArrayOf<BrowsePath>>(paths => Matches(paths, owner, names)), Token), Times.Exactly(count)));
        }

        internal void ExpectRead(NodeId node, params DataValue[] values)
        {
            Session.Setup(session => session.ReadAsync(null, 0, TimestampsToReturn.Both,
                It.Is<ArrayOf<ReadValueId>>(items => items.Count == 1 &&
                    items[0].NodeId == node && items[0].AttributeId == Attributes.Value), Token))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = values
                }));
            m_verify.Add(() => Session.Verify(session => session.ReadAsync(null, 0, TimestampsToReturn.Both,
                It.Is<ArrayOf<ReadValueId>>(items => items.Count == 1 &&
                    items[0].NodeId == node && items[0].AttributeId == Attributes.Value), Token), Times.Once));
        }

        internal void ExpectWrite(NodeId node, Action<Variant> check, params StatusCode[] results)
        {
            Session.Setup(session => session.WriteAsync(null,
                It.Is<ArrayOf<WriteValue>>(items => items.Count == 1 &&
                    items[0].NodeId == node && items[0].AttributeId == Attributes.Value), Token))
                .Returns((RequestHeader _, ArrayOf<WriteValue> items, CancellationToken _) =>
                {
                    check(items[0].Value.WrappedValue);
                    return new ValueTask<WriteResponse>(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results
                    });
                });
            m_verify.Add(() => Session.Verify(session => session.WriteAsync(null,
                It.Is<ArrayOf<WriteValue>>(items => items.Count == 1 &&
                    items[0].NodeId == node && items[0].AttributeId == Attributes.Value), Token), Times.Once));
        }

        internal NodeId Type(ExpandedNodeId type) => ExpandedNodeId.ToNodeId(type, Context.NamespaceUris);

        internal static ReferenceDescription Reference(ExpandedNodeId node, ExpandedNodeId type,
            string name = "Member") =>
            new()
            {
                NodeId = node,
                TypeDefinition = type,
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName(name, 1),
                DisplayName = new LocalizedText("en", name),
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                IsForward = true
            };

        internal void ExpectBrowse(NodeId node, params ReferenceDescription[] references)
        {
            ExpectBrowseResults(node, [new BrowseResult { References = references }]);
        }

        internal void ExpectBrowseResults(NodeId node, ArrayOf<BrowseResult> results, Action? beforeReturn = null)
        {
            Session.Setup(session => session.BrowseAsync(null, null, 0,
                It.Is<ArrayOf<BrowseDescription>>(items => items.Count == 1 && items[0].NodeId == node), Token))
                .Returns((RequestHeader _, ViewDescription _, uint _, ArrayOf<BrowseDescription> items,
                    CancellationToken token) =>
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(items[0].BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
                        Assert.That(items[0].ReferenceTypeId,
                            Is.EqualTo(Opc.Ua.ReferenceTypeIds.HierarchicalReferences));
                        Assert.That(items[0].IncludeSubtypes, Is.True);
                        Assert.That(items[0].NodeClassMask, Is.EqualTo((uint)NodeClass.Object));
                        Assert.That(items[0].ResultMask, Is.EqualTo((uint)BrowseResultMask.All));
                    });
                    beforeReturn?.Invoke();
                    token.ThrowIfCancellationRequested();
                    return new ValueTask<BrowseResponse>(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results
                    });
                });
            m_verify.Add(() => Session.Verify(session => session.BrowseAsync(null, null, 0,
                It.Is<ArrayOf<BrowseDescription>>(items => items.Count == 1 && items[0].NodeId == node), Token),
                    Times.Once));
        }

        internal void ExpectType(NodeId actual, NodeId declared, bool result, int count = 1,
            Action? beforeReturn = null)
        {
            Cache.Setup(cache => cache.IsTypeOfAsync(actual, declared, Token))
                .Returns((NodeId _, NodeId _, CancellationToken token) =>
                {
                    beforeReturn?.Invoke();
                    token.ThrowIfCancellationRequested();
                    return new ValueTask<bool>(result);
                });
            m_verify.Add(() => Cache.Verify(cache => cache.IsTypeOfAsync(actual, declared, Token),
                Times.Exactly(count)));
        }

        internal void Verify()
        {
            foreach (Action verify in m_verify)
            {
                verify();
            }
            Assert.That(Session.Invocations.Any(call =>
                call.Method.Name.StartsWith("Close", StringComparison.Ordinal) ||
                call.Method.Name.StartsWith("Dispose", StringComparison.Ordinal)), Is.False);
        }

        internal void VerifyNoServices()
        {
            Assert.That(Session.Invocations.All(call =>
                call.Method.Name.StartsWith("get_", StringComparison.Ordinal)), Is.True);
        }

        internal static void AssertReading<T>(
            GranulateDryingReading<T> reading, NodeId node, T expected, StatusCode status = default)
        {
            Assert.Multiple(() =>
            {
                Assert.That(reading.NodeId, Is.EqualTo(node));
                Assert.That(reading.IsPresent, Is.True);
                Assert.That(reading.HasValue, Is.True);
                Assert.That(reading.Value, Is.EqualTo(expected));
                Assert.That(reading.DataValue.StatusCode, Is.EqualTo(status));
                Assert.That(reading.DataValue.SourceTimestamp, Is.EqualTo(SourceTime));
                Assert.That(reading.DataValue.ServerTimestamp, Is.EqualTo(ServerTime));
            });
        }

        internal static void AssertMissing<T>(GranulateDryingReading<T> reading)
        {
            Assert.Multiple(() =>
            {
                Assert.That(reading.NodeId, Is.EqualTo(NodeId.Null));
                Assert.That(reading.IsPresent, Is.False);
                Assert.That(reading.HasValue, Is.False);
                Assert.That(reading.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            });
        }

        internal static async Task AssertFailure(Func<Task> action, StatusCode status)
        {
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await action().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(status));
        }

        private static bool Matches(ArrayOf<BrowsePath> paths, NodeId owner, QualifiedName[] names) =>
            paths.Count == 1 && paths[0].StartingNode == owner &&
            paths[0].RelativePath.Elements.ToArray()!.Select(element => element.TargetName).SequenceEqual(names);

        private readonly List<Action> m_verify = [];
        private uint m_nextNode = 8000;
    }
}
