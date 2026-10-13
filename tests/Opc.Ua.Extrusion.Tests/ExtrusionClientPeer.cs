/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Extrusion.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// Strict, port-free services with deliberately nonstandard namespace indexes.
    /// </summary>
    internal sealed class ExtrusionClientPeer
    {
        internal ExtrusionClientPeer(
            bool includeExtrusion = true,
            bool includeRoots = true,
            bool includeFilter = true,
            CancellationToken token = default)
        {
            Token = token;
            Telemetry = NUnitTelemetryContext.Create();
            Context = ServiceMessageContext.Create(Telemetry);
            Context.NamespaceUris.GetIndexOrAppend("urn:extrusion-tests:instances");
            Context.NamespaceUris.GetIndexOrAppend("urn:extrusion-tests:vendor");
            Context.NamespaceUris.GetIndexOrAppend(PlasticsRubber.Namespaces.PlasticsRubber);
            if (includeExtrusion)
            {
                Context.NamespaceUris.GetIndexOrAppend(Namespaces.Extrusion);
            }
            if (includeFilter)
            {
                Context.NamespaceUris.GetIndexOrAppend(Filter.Namespaces.ExtrusionFilter);
            }
            Context.NamespaceUris.GetIndexOrAppend(Extruder.Namespaces.ExtrusionExtruder);
            Context.NamespaceUris.GetIndexOrAppend(Line.Namespaces.ExtrusionLine);
            Context.NamespaceUris.GetIndexOrAppend(Cutter.Namespaces.ExtrusionCutter);
            if (includeRoots)
            {
                Context.NamespaceUris.GetIndexOrAppend(Machinery.Namespaces.Machinery);
                Context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            }
            Session.SetupGet(value => value.MessageContext).Returns(Context);
            Session.SetupGet(value => value.NamespaceUris).Returns(Context.NamespaceUris);
            Session.SetupGet(value => value.Factory).Returns(Context.Factory);
            Session.SetupGet(value => value.NodeCache).Returns(Cache.Object);
            Session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits { MaxNodesPerBrowse = 16 });
            Session.SetupGet(value => value.ServerCapabilities).Returns(new ServerCapabilities());
            Session.SetupGet(value => value.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Balanced);
            Client = new ExtrusionClient(Session.Object, Telemetry);
        }

        internal CancellationToken Token { get; }
        internal ITelemetryContext Telemetry { get; }
        internal ServiceMessageContext Context { get; }
        internal Mock<ISession> Session { get; } = new(MockBehavior.Strict);
        internal Mock<INodeCache> Cache { get; } = new(MockBehavior.Strict);
        internal ExtrusionClient Client { get; }
        internal static NodeId DeviceId => new("extruder-27", 1);
        internal static NodeId FilterId => new("filter-43", 1);
        internal static DateTimeUtc SourceTime => (DateTimeUtc)new DateTime(2024, 3, 6, 9, 12, 15, DateTimeKind.Utc);
        internal static DateTimeUtc ServerTime => (DateTimeUtc)new DateTime(2024, 3, 6, 9, 12, 17, DateTimeKind.Utc);

        internal QualifiedName Name(string uri, string name) =>
            new(name, checked((ushort)Context.NamespaceUris.GetIndex(uri)));

        internal QualifiedName General(string name) => Name(Namespaces.Extrusion, name);
        internal QualifiedName FilterName(string name) => Name(Filter.Namespaces.ExtrusionFilter, name);
        internal NodeId Type(ExpandedNodeId type) => ExpandedNodeId.ToNodeId(type, Context.NamespaceUris);
        internal NodeId NextNode() => new(++m_nextNode, 1);

        internal static DataValue Sample(Variant payload, StatusCode status = default) =>
            new(payload, status, SourceTime, ServerTime);

        internal static ReferenceDescription Reference(
            ExpandedNodeId node, ExpandedNodeId type, QualifiedName name) => new()
        {
            NodeId = node,
            TypeDefinition = type,
            BrowseName = name,
            DisplayName = new LocalizedText("en", "Display " + name.Name),
            NodeClass = NodeClass.Object,
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
            IsForward = true
        };

        internal static ReferenceDescription Variable(NodeId node, QualifiedName name) => new()
        {
            NodeId = node,
            BrowseName = name,
            NodeClass = NodeClass.Variable,
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
            IsForward = true
        };

        internal void ExpectBrowse(NodeId node, uint classes, params ReferenceDescription[] references) =>
            ExpectBrowseResults(node, classes, [new BrowseResult { References = references }]);

        internal void ExpectBrowseResults(
            NodeId node, uint classes, ArrayOf<BrowseResult> results, int count = 1, Action? beforeReturn = null)
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
                        Assert.That(items[0].NodeClassMask, Is.EqualTo(classes));
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
                It.Is<ArrayOf<BrowseDescription>>(items => items.Count == 1 && items[0].NodeId == node),
                Token), Times.Exactly(count)));
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

        internal void ExpectWrite(
            NodeId node, Variant expected, ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnostics = default)
        {
            Session.Setup(session => session.WriteAsync(null,
                It.Is<ArrayOf<WriteValue>>(items => items.Count == 1 &&
                    items[0].NodeId == node && items[0].AttributeId == Attributes.Value), Token))
                .Returns((RequestHeader _, ArrayOf<WriteValue> items, CancellationToken _) =>
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(items[0].Value.WrappedValue, Is.EqualTo(expected));
                        Assert.That(items[0].Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(items[0].IndexRange, Is.Null.Or.Empty);
                    });
                    return new ValueTask<WriteResponse>(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results,
                        DiagnosticInfos = diagnostics
                    });
                });
            m_verify.Add(() => Session.Verify(session => session.WriteAsync(null,
                It.Is<ArrayOf<WriteValue>>(items => items.Count == 1 &&
                    items[0].NodeId == node && items[0].AttributeId == Attributes.Value), Token), Times.Once));
        }

        internal void ExpectType(
            NodeId actual, NodeId expected, bool result, int count = 1, Action? beforeReturn = null)
        {
            Cache.Setup(cache => cache.IsTypeOfAsync(actual, expected, Token))
                .Returns((NodeId _, NodeId _, CancellationToken token) =>
                {
                    beforeReturn?.Invoke();
                    token.ThrowIfCancellationRequested();
                    return new ValueTask<bool>(result);
                });
            m_verify.Add(() => Cache.Verify(cache => cache.IsTypeOfAsync(actual, expected, Token),
                Times.Exactly(count)));
        }

        internal void ExpectChild(
            NodeId owner, QualifiedName name, NodeId target, StatusCode status = default, Exception? failure = null)
        {
            Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(null,
                It.Is<ArrayOf<BrowsePath>>(paths => paths.Count == 1 && paths[0].StartingNode == owner &&
                    paths[0].RelativePath.Elements.Count == 1
                        && paths[0].RelativePath.Elements[0].TargetName == name), Token))
                .Returns((RequestHeader _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                {
                    RelativePathElement element = paths[0].RelativePath.Elements[0];
                    Assert.Multiple(() =>
                    {
                        Assert.That(element.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
                        Assert.That(element.IsInverse, Is.False);
                        Assert.That(element.IncludeSubtypes, Is.True);
                    });
                    if (failure != null)
                    {
                        throw failure;
                    }
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new
                        TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new BrowsePathResult
                            {
                                StatusCode = status,
                                Targets = target.IsNull ? [] :
                                    [new BrowsePathTarget { TargetId = target, RemainingPathIndex = uint.MaxValue }]
                            }
                        ]
                    });
                });
            m_verify.Add(() => Session.Verify(session => session.TranslateBrowsePathsToNodeIdsAsync(null,
                It.Is<ArrayOf<BrowsePath>>(paths => paths.Count == 1 && paths[0].StartingNode == owner &&
                    paths[0].RelativePath.Elements.Count == 1 && paths[0].RelativePath.Elements[0].TargetName == name),
                Token), Times.Once));
        }

        internal void ExpectCall(NodeId owner, NodeId method, ArrayOf<Variant> arguments, StatusCode status = default)
        {
            Session.Setup(session => session.CallAsync(null, It.Is<ArrayOf<CallMethodRequest>>(calls =>
                calls.Count == 1 && calls[0].ObjectId == owner && calls[0].MethodId == method), Token))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> calls, CancellationToken _) =>
                {
                    Assert.That(calls[0].InputArguments, Is.EqualTo(arguments));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = status, OutputArguments = [] }]
                    });
                });
            m_verify.Add(() => Session.Verify(session => session.CallAsync(null,
                It.Is<ArrayOf<CallMethodRequest>>(calls =>
                calls.Count == 1 && calls[0].ObjectId == owner && calls[0].MethodId == method), Token), Times.Once));
        }

        internal void ExpectSuperType(NodeId actual, NodeId parent, Exception? failure = null)
        {
            Cache.Setup(cache => cache.FindSuperTypeAsync(actual, Token))
                .Returns((NodeId _, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (failure != null)
                    {
                        throw failure;
                    }
                    return new ValueTask<NodeId>(parent);
                });
            m_verify.Add(() => Cache.Verify(cache => cache.FindSuperTypeAsync(actual, Token), Times.Once));
        }

        internal void Verify()
        {
            foreach (Action verify in m_verify)
            {
                verify();
            }
            Cache.VerifyNoOtherCalls();
            Assert.That(Session.Invocations.Any(call =>
                call.Method.Name.StartsWith("Close", StringComparison.Ordinal) ||
                call.Method.Name.StartsWith("Dispose", StringComparison.Ordinal)), Is.False);
        }

        internal void VerifyNoServices() =>
            Assert.That(Session.Invocations.All(call => call.Method.Name.StartsWith("get_", StringComparison.Ordinal)),
                Is.True);

        internal static void AssertReading<T>(ExtrusionRead<T> reading, T expected, bool hasValue, StatusCode status)
        {
            Assert.Multiple(() =>
            {
                Assert.That(reading.Value, Is.EqualTo(expected));
                Assert.That(reading.HasValue, Is.EqualTo(hasValue));
                Assert.That(reading.DataValue.StatusCode, Is.EqualTo(status));
                Assert.That(reading.DataValue.SourceTimestamp, Is.EqualTo(SourceTime));
                Assert.That(reading.DataValue.ServerTimestamp, Is.EqualTo(ServerTime));
            });
        }

        internal static void AssertFailure(Func<Task> action, StatusCode expected)
        {
            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await action().ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(expected));
        }

        internal static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
        {
            var values = new List<T>();
            await foreach (T value in source.ConfigureAwait(false))
            {
                values.Add(value);
            }
            return values;
        }

        internal static async IAsyncEnumerable<T> Notifications<T>(
            T[] values, Action produced, Action disposed, Exception? failure,
            [EnumeratorCancellation] CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await Task.CompletedTask.ConfigureAwait(false);
                foreach (T value in values)
                {
                    token.ThrowIfCancellationRequested();
                    produced();
                    yield return value;
                    if (failure != null)
                    {
                        throw failure;
                    }
                }
            }
            finally
            {
                disposed();
            }
        }

        private readonly List<Action> m_verify = [];
        private uint m_nextNode = 9100;
    }
}
