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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class NodeCacheContextTests
    {
        [Test]
        public async Task FetchValuesAsyncShouldReturnResultSetAsync()
        {
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            ArrayOf<DataValue> dataValues =
            [
                new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            ];
            var diagnosticInfos = new DiagnosticInfoCollection();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Once);

            // Act
            ResultSet<DataValue> result = await sut.FetchValuesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results, Is.EqualTo(dataValues));
            Assert.That(result.Errors.ToList(), Is.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public async Task FetchValueAsyncShouldReturnDataValueAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);

            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);
            var diagnosticInfos = new DiagnosticInfoCollection();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [dataValue],
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Once);

            // Act
            DataValue result = await sut.FetchValueAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(dataValue));

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchValuesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds = [];

            // Act
            ResultSet<DataValue> result = await sut.FetchValuesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results, Is.Empty);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public void FetchValueAsyncShouldThrowServiceResultExceptionForBadStatusCode()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Bad, DateTime.UtcNow);
            var diagnosticInfos = new DiagnosticInfoCollection();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [dataValue],
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<ServiceResultException>(async () => await sut.FetchValueAsync(
                null,
                nodeId,
                CancellationToken.None).ConfigureAwait(false));

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchValuesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            ArrayOf<DataValue> dataValues =
            [
                new DataValue(new Variant(123), StatusCodes.Bad, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            ];
            var diagnosticInfos = new DiagnosticInfoCollection();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Once);

            // Act
            ResultSet<DataValue> result = await sut.FetchValuesAsync(
                null,
                nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results, Is.EqualTo(dataValues));
            Assert.That(result.Errors[0].StatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(result.Errors[1].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void FetchValueAsyncShouldHandleCancellation()
        {
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await sut.FetchValueAsync(null, nodeId, cts.Token).ConfigureAwait(false));
        }

        [Test]
        public void FetchValuesAsyncShouldHandleCancellation()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            var cts = new CancellationTokenSource();
            cts.Cancel();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => _ = await sut.FetchValuesAsync(null, nodeIds, cts.Token).ConfigureAwait(false));
        }

        [Test]
        public async Task FetchValueAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);
            var diagnosticInfo = new DiagnosticInfo();
            var diagnosticInfos = new DiagnosticInfoCollection { diagnosticInfo };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [dataValue],
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Once);

            // Act
            DataValue result = await sut.FetchValueAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(dataValue));
            Assert.That(diagnosticInfos, Contains.Item(diagnosticInfo));

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchValuesAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            ArrayOf<DataValue> dataValues =
            [
                new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            ];
            var diagnosticInfo = new DiagnosticInfo();
            ArrayOf<DiagnosticInfo> diagnosticInfos = [diagnosticInfo, diagnosticInfo];

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Once);

            // Act
            ResultSet<DataValue> result = await sut.FetchValuesAsync(
                null,
                nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results, Is.EqualTo(dataValues));
            Assert.That(result.Errors.ToList(), Is.All.EqualTo(ServiceResult.Good));
            Assert.That(diagnosticInfos.ToList(), Contains.Item(diagnosticInfo));

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            VariableNode[] nodes =
            [
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = LocalizedText.From("TestDescription1"),
                    DisplayName = LocalizedText.From("TestDisplayName1"),
                    BrowseName = QualifiedName.From("TestBrowseName1"),
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = LocalizedText.From("TestDescription2"),
                    DisplayName = LocalizedText.From("TestDisplayName2"),
                    BrowseName = QualifiedName.From("TestBrowseName2"),
                    UserAccessLevel = 1
                }
            ];

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    ArrayOf<DataValue> results = request.NodesToRead
                        .ConvertAll(r =>
                        {
                            var value = new DataValue();
                            if (r.NodeId == nodeIds[0])
                            {
                                nodes[0].Read(null, r.AttributeId, value);
                            }
                            else
                            {
                                nodes[1].Read(null, r.AttributeId, value);
                            }
                            return value;
                        });
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = []
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            ResultSet<Node> result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(nodes[0], result.Results[0]), Is.True);
            Assert.That(Utils.IsEqual(nodes[1], result.Results[1]), Is.True);
            Assert.That(result.Errors.ToList(), Is.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnResultSetWhenOptionalAttributesMissingAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            VariableNode[] nodes =
            [
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = LocalizedText.From("TestDescription1"),
                    DisplayName = LocalizedText.From("TestDisplayName1"),
                    BrowseName = QualifiedName.From("TestBrowseName1"),
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = LocalizedText.From("TestDescription2"),
                    DisplayName = LocalizedText.From("TestDisplayName2"),
                    BrowseName = QualifiedName.From("TestBrowseName2"),
                    UserAccessLevel = 1
                }
            ];

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    ArrayOf<DataValue> results = request.NodesToRead
                        .ConvertAll(r =>
                        {
                            var value = new DataValue();
                            if (r.AttributeId == Attributes.MinimumSamplingInterval)
                            {
                                return new DataValue(StatusCodes.BadNotReadable);
                            }
                            if (r.AttributeId == Attributes.Description)
                            {
                                return new DataValue(StatusCodes.BadAttributeIdInvalid);
                            }
                            if (r.NodeId == nodeIds[0])
                            {
                                nodes[0].Read(null, r.AttributeId, value);
                            }
                            else
                            {
                                nodes[1].Read(null, r.AttributeId, value);
                            }
                            return value;
                        });
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = []
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            ResultSet<Node> result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(nodes[0], result.Results[0]), Is.False);
            Assert.That(Utils.IsEqual(nodes[1], result.Results[1]), Is.False);
            Assert.That(result.Errors.ToList(), Is.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public async Task FetchNodeAsyncShouldReturnNodeAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var node = new VariableNode
            {
                NodeId = nodeId,
                NodeClass = NodeClass.Variable,
                AccessLevel = 1,
                DataType = NodeId.Parse("ns=2;s=TestDataType"),
                Description = LocalizedText.From("TestDescription"),
                DisplayName = LocalizedText.From("TestDisplayName"),
                BrowseName = QualifiedName.From("TestBrowseName"),
                UserAccessLevel = 1
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    ArrayOf<DataValue> results = request.NodesToRead
                        .ConvertAll(r =>
                        {
                            var value = new DataValue();
                            node.Read(null, r.AttributeId, value);
                            return value;
                        });
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = []
                    });
                })
                .Verifiable(Times.Once);

            // Act
            Node result = await sut.FetchNodeAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            Assert.That(Utils.IsEqual(node, result), Is.True);

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds = [];

            // Act
            ResultSet<Node> result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results, Is.Empty);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public void FetchNodeAsyncShouldThrowServiceResultExceptionForBadStatusCode()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var node = new VariableNode
            {
                NodeId = nodeId,
                NodeClass = NodeClass.Variable,
                AccessLevel = 1,
                DataType = NodeId.Parse("ns=2;s=TestDataType"),
                Description = LocalizedText.From("TestDescription"),
                DisplayName = LocalizedText.From("TestDisplayName"),
                BrowseName = QualifiedName.From("TestBrowseName"),
                UserAccessLevel = 1
            };
            var diagnosticInfos = new DiagnosticInfoCollection();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                    new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = request.NodesToRead
                           .ConvertAll(r => new DataValue(StatusCodes.BadAlreadyExists)),
                        DiagnosticInfos = request.NodesToRead.ConvertAll(_ => new DiagnosticInfo())
                    }))
                .Verifiable(Times.Once);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchNodeAsync(null, nodeId,
                    NodeClass.Unspecified).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadAlreadyExists));

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            VariableNode[] nodes =
            [
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = LocalizedText.From("TestDescription1"),
                    DisplayName = LocalizedText.From("TestDisplayName1"),
                    BrowseName = QualifiedName.From("TestBrowseName1"),
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = LocalizedText.From("TestDescription2"),
                    DisplayName = LocalizedText.From("TestDisplayName2"),
                    BrowseName = QualifiedName.From("TestBrowseName2"),
                    UserAccessLevel = 1
                }
            ];

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    ArrayOf<DataValue> results = request.NodesToRead.ConvertAll(r =>
                    {
                        if (r.NodeId == nodeIds[0])
                        {
                            var value = new DataValue();
                            nodes[0].Read(null, r.AttributeId, value);
                            return value;
                        }
                        return new DataValue(StatusCodes.BadUnexpectedError);
                    });
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = [.. results.ConvertAll(r => new DiagnosticInfo())]
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            ResultSet<Node> result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(nodes[0], result.Results[0]), Is.True);
            Assert.That(Utils.IsEqual(nodes[1], result.Results[1]), Is.False);
            Assert.That(result.Errors.Count, Is.EqualTo(2));
            Assert.That(result.Errors[0], Is.EqualTo(ServiceResult.Good));
            Assert.That(result.Errors[1].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnErrorsForBadNodeClassTypeAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            VariableNode[] nodes =
            [
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = LocalizedText.From("TestDescription1"),
                    DisplayName = LocalizedText.From("TestDisplayName1"),
                    BrowseName = QualifiedName.From("TestBrowseName1"),
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = LocalizedText.From("TestDescription2"),
                    DisplayName = LocalizedText.From("TestDisplayName2"),
                    BrowseName = QualifiedName.From("TestBrowseName2"),
                    UserAccessLevel = 1
                }
            ];

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    ArrayOf<DataValue> results = request.NodesToRead
                        .ConvertAll(r =>
                        {
                            if (r.AttributeId == Attributes.NodeClass)
                            {
                                return new DataValue(new Variant("Badclass"));
                            }
                            var value = new DataValue();
                            if (r.NodeId == nodeIds[0])
                            {
                                nodes[0].Read(null, r.AttributeId, value);
                            }
                            else
                            {
                                nodes[1].Read(null, r.AttributeId, value);
                            }
                            return value;
                        });
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = results.ConvertAll(r => new DiagnosticInfo())
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            ResultSet<Node> result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(nodes[0], result.Results[0]), Is.False);
            Assert.That(Utils.IsEqual(nodes[1], result.Results[1]), Is.False);
            Assert.That(result.Errors.Count, Is.EqualTo(2));
            Assert.That(result.Errors[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(result.Errors[1].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public void FetchNodeAsyncShouldHandleCancellation()
        {
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await sut.FetchNodeAsync(null, nodeId, ct: cts.Token).ConfigureAwait(false));
        }

        [Test]
        public void FetchNodesAsyncShouldHandleCancellation()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            var cts = new CancellationTokenSource();
            cts.Cancel();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<OperationCanceledException>(async () => _ = await sut.FetchNodesAsync(
                null,
                nodeIds,
                NodeClass.Unspecified,
                ct: cts.Token).ConfigureAwait(false));
        }

        [Test]
        public async Task FetchNodeAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var node = new VariableNode
            {
                NodeId = nodeId,
                NodeClass = NodeClass.Variable,
                AccessLevel = 1,
                DataType = NodeId.Parse("ns=2;s=TestDataType"),
                Description = LocalizedText.From("TestDescription"),
                DisplayName = LocalizedText.From("TestDisplayName"),
                BrowseName = QualifiedName.From("TestBrowseName"),
                UserAccessLevel = 1
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    ArrayOf<DataValue> results = request.NodesToRead
                        .ConvertAll(r =>
                        {
                            var value = new DataValue();
                            node.Read(null, r.AttributeId, value);
                            return value;
                        });
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = results.ConvertAll(_ => new DiagnosticInfo())
                    });
                })
                .Verifiable(Times.Once);

            // Act
            Node result = await sut.FetchNodeAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            Assert.That(Utils.IsEqual(node, result), Is.True);

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            VariableNode[] nodes =
            [
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = LocalizedText.From("TestDescription1"),
                    DisplayName = LocalizedText.From("TestDisplayName1"),
                    BrowseName = QualifiedName.From("TestBrowseName1"),
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = LocalizedText.From("TestDescription2"),
                    DisplayName = LocalizedText.From("TestDisplayName2"),
                    BrowseName = QualifiedName.From("TestBrowseName2"),
                    UserAccessLevel = 1
                }
            ];

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    ArrayOf<DataValue> results = request.NodesToRead
                        .ConvertAll(r =>
                        {
                            var value = new DataValue();
                            if (r.NodeId == nodeIds[0])
                            {
                                nodes[0].Read(null, r.AttributeId, value);
                            }
                            else
                            {
                                nodes[1].Read(null, r.AttributeId, value);
                            }
                            return value;
                        });
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = results.ConvertAll(r => new DiagnosticInfo())
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            ResultSet<Node> result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(nodes[0], result.Results[0]), Is.True);
            Assert.That(Utils.IsEqual(nodes[1], result.Results[1]), Is.True);
            Assert.That(result.Errors.Count, Is.EqualTo(2));
            Assert.That(result.Errors.ToList(), Is.All.EqualTo(ServiceResult.Good));

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchReferencesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var references = new ReferenceDescriptionCollection
            {
                new ReferenceDescription
                {
                    NodeId = ExpandedNodeId.Parse("ns=2;s=TestNode1"),
                    BrowseName = QualifiedName.From("TestBrowseName1"),
                    DisplayName = LocalizedText.From("TestDisplayName1"),
                    NodeClass = NodeClass.Variable
                },
                new ReferenceDescription
                {
                    NodeId = ExpandedNodeId.Parse("ns=2;s=TestNode2"),
                    BrowseName = QualifiedName.From("TestBrowseName2"),
                    DisplayName = LocalizedText.From("TestDisplayName2"),
                    NodeClass = NodeClass.Variable
                }
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References = references
                        }
                    ],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            ReferenceDescriptionCollection result = await sut.FetchReferencesAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EquivalentTo(references));

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchReferencesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeIds = ArrayOf.Empty<NodeId>();

            // Act
            ResultSet<ArrayOf<ReferenceDescription>> result =
                await sut.FetchReferencesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results, Is.Empty);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public async Task FetchReferencesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            ArrayOf<ReferenceDescription> references =
            [
                new ReferenceDescription
                {
                    NodeId = ExpandedNodeId.Parse("ns=2;s=TestNode1"),
                    BrowseName = QualifiedName.From("TestBrowseName1"),
                    DisplayName = LocalizedText.From("TestDisplayName1"),
                    NodeClass = NodeClass.Variable
                },
                new ReferenceDescription
                {
                    NodeId = ExpandedNodeId.Parse("ns=2;s=TestNode2"),
                    BrowseName = QualifiedName.From("TestBrowseName2"),
                    DisplayName = LocalizedText.From("TestDisplayName2"),
                    NodeClass = NodeClass.Variable
                }
            ];

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References = references,
                            StatusCode = StatusCodes.Bad
                        },
                        new BrowseResult
                        {
                            References = references,
                            StatusCode = StatusCodes.Bad
                        }
                    ],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            ResultSet<ArrayOf<ReferenceDescription>> result = await sut.FetchReferencesAsync(null, nodeIds,
                CancellationToken.None).ConfigureAwait(false);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(result.Results[0], references), Is.True);
            Assert.That(Utils.IsEqual(result.Results[1], references), Is.True);
            Assert.That(result.Errors.Count, Is.EqualTo(2));
            Assert.That(result.Errors[0].StatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(result.Errors[1].StatusCode, Is.EqualTo(StatusCodes.Bad));
        }

        [Test]
        public void FetchReferencesAsyncShouldHandleCancellation()
        {
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            ArrayOf<NodeId> nodeIds =
            [
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            ];
            var cts = new CancellationTokenSource();
            cts.Cancel();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => _ = await sut.FetchReferencesAsync(null, nodeIds, cts.Token).ConfigureAwait(false));
        }

        [Test]
        public async Task FetchReferenceAsyncShouldReturnReferenceDescriptionAsync()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var reference = new ReferenceDescription
            {
                NodeId = ExpandedNodeId.Parse("ns=2;s=TestNode1"),
                BrowseName = QualifiedName.From("TestBrowseName1"),
                DisplayName = LocalizedText.From("TestDisplayName1"),
                NodeClass = NodeClass.Variable
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References = [reference]
                        }
                    ],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            ReferenceDescriptionCollection result = await sut.FetchReferencesAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(reference));

            session.Channel.Verify();
        }

        [Test]
        public void FetchReferenceAsyncShouldThrowServiceResultExceptionForBadStatusCode()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var reference = new ReferenceDescription
            {
                NodeId = ExpandedNodeId.Parse("ns=2;s=TestNode1"),
                BrowseName = QualifiedName.From("TestBrowseName1"),
                DisplayName = LocalizedText.From("TestDisplayName1"),
                NodeClass = NodeClass.Variable
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Bad },
                    Results =
                    [
                        new BrowseResult
                        {
                            References = [reference]
                        }
                    ],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchReferencesAsync(null, nodeId).ConfigureAwait(false));

            session.Channel.Verify();
        }

        [Test]
        public void FetchReferenceAsyncShouldHandleCancellation()
        {
            // Arrange
            var session = SessionMock.Create();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await sut.FetchReferencesAsync(null, nodeId, cts.Token).ConfigureAwait(false));
        }
    }
}
