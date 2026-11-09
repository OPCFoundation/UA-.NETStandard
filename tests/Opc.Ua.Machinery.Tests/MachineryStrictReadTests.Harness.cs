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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Machinery.Tests
{
    public sealed partial class MachineryStrictReadTests
    {
        /// <summary>
        /// Serves deterministic protocol responses without a network or physical methods.
        /// </summary>
        private sealed class ReadHarness
        {
            /// <summary>
            /// Creates the borrowed session and its published namespace table.
            /// </summary>
            public ReadHarness()
            {
                foreach (string uri in new[]
                {
                    Ua.Machinery.Namespaces.Machinery,
                    Ua.Di.Namespaces.OpcUaDi,
                    ProcessValues.Namespaces.MachineryProcessValues,
                    PADIM.Namespaces.PADIM,
                    ECM.Namespaces.ECM,
                    IA.Namespaces.IA,
                    Energy.Namespaces.MachineryEnergy,
                    Jobs.Namespaces.MachineryJobs,
                    ISA95.JobControl.V2.Namespaces.ISA95JobControlV2,
                    Result.Namespaces.MachineryResult,
                    "urn:machinery:strict-tests"
                })
                {
                    Namespaces.Append(uri);
                }
                Context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
                Context.NamespaceUris = Namespaces;
                Session.SetupGet(s => s.NamespaceUris).Returns(() => Namespaces);
                Session.SetupGet(s => s.MessageContext).Returns(Context);
                Session.SetupGet(s => s.SessionId).Returns(() => SessionId);
                Session.SetupGet(s => s.OperationLimits).Returns(new OperationLimits());
                Session.SetupGet(s => s.ServerCapabilities).Returns(new ServerCapabilities());
                Session.SetupGet(s => s.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
                var typeTree = new TypeTable(Namespaces);
                typeTree.AddSubtype(Ua.DataTypeIds.BaseDataType, NodeId.Null);
                typeTree.AddSubtype(Ua.DataTypeIds.Structure, Ua.DataTypeIds.BaseDataType);
                typeTree.AddSubtype(new NodeId(ECM.DataTypes.AcPeDataType, Index(ECM.Namespaces.ECM)),
                    Ua.DataTypeIds.Structure);
                typeTree.AddSubtype(new NodeId(ECM.DataTypes.AcPpDataType, Index(ECM.Namespaces.ECM)),
                    Ua.DataTypeIds.Structure);
                Session.SetupGet(s => s.TypeTree).Returns(typeTree);
                var cache = new Mock<INodeCache>();
                cache.Setup(c => c.IsTypeOfAsync(It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<bool>(true));
                cache.Setup(c => c.IsTypeOfAsync(
                    It.IsAny<NodeId>(), VariableTypeIds.PropertyType, It.IsAny<CancellationToken>()))
                    .Returns((NodeId type, NodeId _, CancellationToken _) =>
                        new ValueTask<bool>(type == VariableTypeIds.PropertyType));
                Session.SetupGet(s => s.NodeCache).Returns(cache.Object);

                Translate = TranslatePaths;
                Read = (nodes, _) => Task.FromResult(ReadNodes(nodes));
                Browse = _ => new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                };
                Next = (_, _) => Task.FromResult(
                    new BrowseNextResponse { ResponseHeader = new ResponseHeader(), Results = [new BrowseResult()] });
                Release = (_, _) => Task.FromResult(
                    new BrowseNextResponse { ResponseHeader = new ResponseHeader(), Results = [new BrowseResult()] });
                Session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                        new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(Translate(paths)));
                Session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader? _, double _, TimestampsToReturn timestamps,
                        ArrayOf<ReadValueId> nodes, CancellationToken token) =>
                    {
                        ReadTimestamps.Enqueue(timestamps);
                        return new ValueTask<ReadResponse>(Read(nodes, token));
                    });
                Session.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader? _, ViewDescription? _, uint limit,
                        ArrayOf<BrowseDescription> nodes, CancellationToken _) =>
                    {
                        BrowseLimits.Enqueue(limit);
                        return new ValueTask<BrowseResponse>(Browse(nodes));
                    });
                Session.Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader? _, bool release, ArrayOf<ByteString> points, CancellationToken token) =>
                    {
                        if (release)
                        {
                            Releases.Enqueue((points[0], token.IsCancellationRequested));
                            return new ValueTask<BrowseNextResponse>(Release(points, token));
                        }
                        NextRequests.Enqueue(points[0]);
                        return new ValueTask<BrowseNextResponse>(Next(points, token));
                    });
            }

            /// <summary>
            /// Gets the session whose unexpected actions fail the test.
            /// </summary>
            public Mock<ISession> Session { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets or changes the borrowed session identity to exercise in-place replacement.
            /// </summary>
            public NodeId SessionId { get; set; } = new("strict-read-session", 1);

            /// <summary>
            /// Gets or replaces the table to test legitimately absent model namespaces.
            /// </summary>
            public NamespaceTable Namespaces { get; set; } = new();

            /// <summary>
            /// Gets the context used for binary structures.
            /// </summary>
            public ServiceMessageContext Context { get; }

            /// <summary>
            /// Gets the selected machine.
            /// </summary>
            public NodeId Machine => new(100u, Index("urn:machinery:strict-tests"));

            /// <summary>
            /// Gets the stored source values.
            /// </summary>
            public Dictionary<NodeId, DataValue> Values { get; } = [];

            /// <summary>
            /// Gets explicit wire metadata for typed measurement validation.
            /// </summary>
            public Dictionary<(NodeId Node, uint Attribute), DataValue> AttributeValues { get; } = [];

            /// <summary>
            /// Gets or sets translation responses.
            /// </summary>
            public Func<ArrayOf<BrowsePath>, TranslateBrowsePathsToNodeIdsResponse> Translate { get; set; }

            /// <summary>
            /// Gets or sets read responses.
            /// </summary>
            public Func<ArrayOf<ReadValueId>, CancellationToken, Task<ReadResponse>> Read { get; set; }

            /// <summary>
            /// Gets or sets initial browse responses.
            /// </summary>
            public Func<ArrayOf<BrowseDescription>, BrowseResponse> Browse { get; set; }

            /// <summary>
            /// Gets or sets continuation responses.
            /// </summary>
            public Func<ArrayOf<ByteString>, CancellationToken, Task<BrowseNextResponse>> Next { get; set; }

            /// <summary>
            /// Gets or sets cleanup responses.
            /// </summary>
            public Func<ArrayOf<ByteString>, CancellationToken, Task<BrowseNextResponse>> Release { get; set; }

            /// <summary>
            /// Gets independently captured timestamp requests.
            /// </summary>
            public ConcurrentQueue<TimestampsToReturn> ReadTimestamps { get; } = new();

            /// <summary>
            /// Gets the advertised page-size limits.
            /// </summary>
            public ConcurrentQueue<uint> BrowseLimits { get; } = new();

            /// <summary>
            /// Gets continuation requests, excluding cleanup.
            /// </summary>
            public ConcurrentQueue<ByteString> NextRequests { get; } = new();

            /// <summary>
            /// Gets released tokens and whether cleanup incorrectly reused cancellation.
            /// </summary>
            public ConcurrentQueue<(ByteString Token, bool Cancelled)> Releases { get; } = new();

            /// <summary>
            /// Creates a configured or legacy client.
            /// </summary>
            public MachineryClient Client(MachineryClientOptions? options = null, bool strict = true)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                return strict
                    ? new MachineryClient(Session.Object, telemetry, options ?? new MachineryClientOptions())
                    : new MachineryClient(Session.Object, telemetry);
            }

            /// <summary>
            /// Looks up a known namespace without appending it.
            /// </summary>
            public ushort Index(string uri)
            {
                return (ushort)Namespaces.GetIndex(uri);
            }

            /// <summary>
            /// Creates a qualified name in a published namespace.
            /// </summary>
            public QualifiedName Name(string name, string uri)
            {
                return new QualifiedName(name, Index(uri));
            }

            /// <summary>
            /// Registers one exact path.
            /// </summary>
            public NodeId Add(NodeId parent, ArrayOf<QualifiedName> path)
            {
                var nodeId = new NodeId(m_nextId++, Machine.NamespaceIndex);
                m_paths[Key(parent, path)] = nodeId;
                return nodeId;
            }

            /// <summary>
            /// Registers one exact path and its unmodified source value.
            /// </summary>
            public NodeId Add(NodeId parent, ArrayOf<QualifiedName> path, in DataValue value)
            {
                NodeId source = Add(parent, path);
                Values[source] = value;
                return source;
            }

            /// <summary>
            /// Translates registered paths and returns genuine optional BadNoMatch otherwise.
            /// </summary>
            public TranslateBrowsePathsToNodeIdsResponse TranslatePaths(ArrayOf<BrowsePath> paths)
            {
                var results = new BrowsePathResult[paths.Count];
                for (int ii = 0; ii < paths.Count; ii++)
                {
                    QualifiedName[] names = [.. paths[ii].RelativePath.Elements.ToArray()!.Select(e => e.TargetName)];
                    results[ii] = m_paths.TryGetValue(Key(paths[ii].StartingNode, names.ToArrayOf()), out NodeId source)
                        ? Target(source)
                        : new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
                }
                return new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = results.ToArrayOf()
                };
            }

            /// <summary>
            /// Reads registered source values without changing quality or timestamps.
            /// </summary>
            public ReadResponse ReadNodes(ArrayOf<ReadValueId> nodes)
            {
                var values = new DataValue[nodes.Count];
                for (int ii = 0; ii < nodes.Count; ii++)
                {
                    ReadValueId node = nodes[ii];
                    if (AttributeValues.TryGetValue((node.NodeId, node.AttributeId), out DataValue attribute))
                    {
                        values[ii] = attribute;
                    }
                    else if (node.AttributeId == Attributes.Value)
                    {
                        values[ii] = Values.TryGetValue(node.NodeId, out DataValue value)
                            ? value
                            : DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
                    }
                    else
                    {
                        values[ii] = node.AttributeId switch
                        {
                            Attributes.DataType => new DataValue(Variant.From(Ua.DataTypeIds.Double)),
                            Attributes.ValueRank => new DataValue(Variant.From(ValueRanks.Scalar)),
                            _ => DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)
                        };
                    }
                }
                return new ReadResponse { ResponseHeader = new ResponseHeader(), Results = values.ToArrayOf() };
            }

            /// <summary>
            /// Sets a measurement's declared DataType and ValueRank independently of its value.
            /// </summary>
            public void Declare(NodeId node, NodeId dataType, int valueRank = ValueRanks.Scalar)
            {
                AttributeValues[(node, Attributes.DataType)] = new DataValue(Variant.From(dataType));
                AttributeValues[(node, Attributes.ValueRank)] = new DataValue(Variant.From(valueRank));
            }

            /// <summary>
            /// Formats an exact path, including each namespace index.
            /// </summary>
            private static string Key(NodeId parent, ArrayOf<QualifiedName> path)
            {
                return $"{parent}|{string.Join("/", path.ToArray()!.Select(n => n.ToString()))}";
            }

            private readonly Dictionary<string, NodeId> m_paths = new(StringComparer.Ordinal);
            private uint m_nextId = 200;
        }

        private static BrowsePathResult Target(NodeId source)
        {
            return new BrowsePathResult
            {
                Targets = [new BrowsePathTarget { TargetId = source, RemainingPathIndex = uint.MaxValue }]
            };
        }

        private static DataValue TimedValue(Variant value, StatusCode status)
        {
            return new DataValue(
                value, status,
                new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 10, 12, 0, 1, DateTimeKind.Utc),
                sourcePicoseconds: 12, serverPicoseconds: 34);
        }

        private static QualifiedName Di(ReadHarness harness, string name)
        {
            return harness.Name(name, Ua.Di.Namespaces.OpcUaDi);
        }

        private static QualifiedName Machinery(ReadHarness harness, string name)
        {
            return harness.Name(name, Namespaces.Machinery);
        }

        private static MachineryReadValue Field(ArrayOf<MachineryReadValue> values, QualifiedName name)
        {
            return values.ToArray()!.Single(v => v.BrowsePath.Count != 0 && v.BrowsePath[^1] == name);
        }

        private static StatusCode TestStatus(string name)
        {
            return name switch
            {
                nameof(StatusCodes.Uncertain) => StatusCodes.Uncertain,
                nameof(StatusCodes.BadUserAccessDenied) => StatusCodes.BadUserAccessDenied,
                nameof(StatusCodes.BadNodeIdUnknown) => StatusCodes.BadNodeIdUnknown,
                nameof(StatusCodes.BadNotReadable) => StatusCodes.BadNotReadable,
                nameof(StatusCodes.BadNoMatch) => StatusCodes.BadNoMatch,
                nameof(StatusCodes.BadDecodingError) => StatusCodes.BadDecodingError,
                nameof(StatusCodes.BadNotSupported) => StatusCodes.BadNotSupported,
                nameof(StatusCodes.BadTypeMismatch) => StatusCodes.BadTypeMismatch,
                nameof(StatusCodes.BadEncodingLimitsExceeded) => StatusCodes.BadEncodingLimitsExceeded,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
        }
    }
}
