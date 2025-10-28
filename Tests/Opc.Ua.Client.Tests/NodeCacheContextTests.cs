/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Tests;
using System.IO;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("NodeCacheAsync")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class NodeCacheContextTests
    {
        [Test]
        public async Task FetchValuesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var dataValues = new DataValueCollection
            {
                new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            };
            var diagnosticInfos = new DiagnosticInfoCollection();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValuesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Should().BeEquivalentTo(dataValues);
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);
        }

        [Test]
        public async Task FetchValueAsyncShouldReturnDataValueAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);

            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);
            var diagnosticInfos = new DiagnosticInfoCollection();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [dataValue],
                    DiagnosticInfos = diagnosticInfos
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValueAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            result.Should().BeEquivalentTo(dataValue);

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchValuesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>();

            // Act
            var result = await sut.FetchValuesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Should().BeEmpty();
            result.Errors.Should().BeEmpty();
        }

        [Test]
        public async Task FetchValueAsyncShouldThrowServiceResultExceptionForBadStatusCodeAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Bad, DateTime.UtcNow);
            var diagnosticInfos = new DiagnosticInfoCollection();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [dataValue],
                    DiagnosticInfos = diagnosticInfos
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchValueAsync(
                null,
                nodeId,
                CancellationToken.None).ConfigureAwait(false);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchValuesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId> {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2") };
            var dataValues = new DataValueCollection
            {
                new DataValue(new Variant(123), StatusCodes.Bad, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            };
            var diagnosticInfos = new DiagnosticInfoCollection();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValuesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Should().BeEquivalentTo(dataValues);
            result.Errors[0].StatusCode.Should().Be(StatusCodes.Bad);
            result.Errors[1].StatusCode.Should().Be(StatusCodes.Good);
        }

        [Test]
        public async Task FetchValueAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
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

            // Act
            Func<Task> act = async () => await sut.FetchValueAsync(null, nodeId, cts.Token).ConfigureAwait(false);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task FetchValuesAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchValuesAsync(null, nodeIds, cts.Token).ConfigureAwait(false);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task FetchValueAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);
            var diagnosticInfo = new DiagnosticInfo();
            var diagnosticInfos = new DiagnosticInfoCollection { diagnosticInfo };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [dataValue],
                    DiagnosticInfos = diagnosticInfos
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValueAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            result.Should().BeEquivalentTo(dataValue);
            diagnosticInfos.Should().Contain(diagnosticInfo);

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchValuesAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var dataValues = new DataValueCollection
            {
                new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            };
            var diagnosticInfo = new DiagnosticInfo();
            var diagnosticInfos = new DiagnosticInfoCollection { diagnosticInfo, diagnosticInfo };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValuesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Should().BeEquivalentTo(dataValues);
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);
            diagnosticInfos.Should().Contain(diagnosticInfo);

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var nodes = new[]
            {
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = "TestDescription1",
                    DisplayName = "TestDisplayName1",
                    BrowseName = "TestBrowseName1",
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = "TestDescription2",
                    DisplayName = "TestDisplayName2",
                    BrowseName = "TestBrowseName2",
                    UserAccessLevel = 1
                }
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = new DataValueCollection(request.NodesToRead
                        .Select(r =>
                        {
                            var value = new DataValue();
                            if (r.NodeId == nodeIds[0])
                            {
                                nodes[0].Read(null!, r.AttributeId, value);
                            }
                            else
                            {
                                nodes[1].Read(null!, r.AttributeId, value);
                            }
                            return value;
                        }));
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = []
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeTrue();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeTrue();
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnResultSetWhenOptionalAttributesMissingAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var nodes = new[]
            {
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = "TestDescription1",
                    DisplayName = "TestDisplayName1",
                    BrowseName = "TestBrowseName1",
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = "TestDescription2",
                    DisplayName = "TestDisplayName2",
                    BrowseName = "TestBrowseName2",
                    UserAccessLevel = 1
                }
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = new DataValueCollection(request.NodesToRead
                        .Select(r =>
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
                                nodes[0].Read(null!, r.AttributeId, value);
                            }
                            else
                            {
                                nodes[1].Read(null!, r.AttributeId, value);
                            }
                            return value;
                        }));
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = []
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeFalse();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeFalse();
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);
        }

        [Test]
        public async Task FetchNodeAsyncShouldReturnNodeAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var node = new VariableNode
            {
                NodeId = nodeId,
                NodeClass = NodeClass.Variable,
                AccessLevel = 1,
                DataType = NodeId.Parse("ns=2;s=TestDataType"),
                Description = "TestDescription",
                DisplayName = "TestDisplayName",
                BrowseName = "TestBrowseName",
                UserAccessLevel = 1
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = new DataValueCollection(request.NodesToRead
                        .Select(r =>
                        {
                            var value = new DataValue();
                            node.Read(null!, r.AttributeId, value);
                            return value;
                        }));
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = []
                    });
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchNodeAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            Utils.IsEqual(node, result).Should().BeTrue();

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>();

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Should().BeEmpty();
            result.Errors.Should().BeEmpty();
        }

        [Test]
        public async Task FetchNodeAsyncShouldThrowServiceResultExceptionForBadStatusCodeAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var node = new VariableNode
            {
                NodeId = nodeId,
                NodeClass = NodeClass.Variable,
                AccessLevel = 1,
                DataType = NodeId.Parse("ns=2;s=TestDataType"),
                Description = "TestDescription",
                DisplayName = "TestDisplayName",
                BrowseName = "TestBrowseName",
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
                        Results = new DataValueCollection(request.NodesToRead
                                .Select(r => new DataValue(StatusCodes.BadAlreadyExists))),
                        DiagnosticInfos = new DiagnosticInfoCollection(
                                request.NodesToRead.Select(_ => new DiagnosticInfo()))
                    }))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNodeAsync(null, nodeId,
                NodeClass.Unspecified).ConfigureAwait(false);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadAlreadyExists);

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var nodes = new[]
            {
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = "TestDescription1",
                    DisplayName = "TestDisplayName1",
                    BrowseName = "TestBrowseName1",
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = "TestDescription2",
                    DisplayName = "TestDisplayName2",
                    BrowseName = "TestBrowseName2",
                    UserAccessLevel = 1
                }
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = new DataValueCollection(request.NodesToRead
                        .Select(r =>
                        {
                            if (r.NodeId == nodeIds[0])
                            {
                                var value = new DataValue();
                                nodes[0].Read(null!, r.AttributeId, value);
                                return value;
                            }
                            return new DataValue(StatusCodes.BadUnexpectedError);
                        }));
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = new DiagnosticInfoCollection(
                            results.Select(r => new DiagnosticInfo()))
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeTrue();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeFalse();
            result.Errors.Count.Should().Be(2);
            result.Errors[0].Should().Be(ServiceResult.Good);
            result.Errors[1].StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnErrorsForBadNodeClassTypeAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var nodes = new[]
            {
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = "TestDescription1",
                    DisplayName = "TestDisplayName1",
                    BrowseName = "TestBrowseName1",
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = "TestDescription2",
                    DisplayName = "TestDisplayName2",
                    BrowseName = "TestBrowseName2",
                    UserAccessLevel = 1
                }
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = new DataValueCollection(request.NodesToRead
                        .Select(r =>
                        {
                            if (r.AttributeId == Attributes.NodeClass)
                            {
                                return new DataValue(new Variant("Badclass"));
                            }
                            var value = new DataValue();
                            if (r.NodeId == nodeIds[0])
                            {
                                nodes[0].Read(null!, r.AttributeId, value);
                            }
                            else
                            {
                                nodes[1].Read(null!, r.AttributeId, value);
                            }
                            return value;
                        }));
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = new DiagnosticInfoCollection(
                            results.Select(r => new DiagnosticInfo()))
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeFalse();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeFalse();
            result.Errors.Count.Should().Be(2);
            result.Errors[0].StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
            result.Errors[1].StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
        }

        [Test]
        public async Task FetchNodeAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
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

            // Act
            Func<Task> act = async () =>
            {
                await sut.FetchNodeAsync(null, nodeId, ct: cts.Token).ConfigureAwait(false);
            };

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task FetchNodesAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNodesAsync(
                null,
                nodeIds,
                NodeClass.Unspecified,
                ct: cts.Token).ConfigureAwait(false);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task FetchNodeAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var node = new VariableNode
            {
                NodeId = nodeId,
                NodeClass = NodeClass.Variable,
                AccessLevel = 1,
                DataType = NodeId.Parse("ns=2;s=TestDataType"),
                Description = "TestDescription",
                DisplayName = "TestDisplayName",
                BrowseName = "TestBrowseName",
                UserAccessLevel = 1
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = new DataValueCollection(request.NodesToRead
                        .Select(r =>
                        {
                            var value = new DataValue();
                            node.Read(null!, r.AttributeId, value);
                            return value;
                        }));
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = new DiagnosticInfoCollection(
                            results.Select(_ => new DiagnosticInfo()))
                    });
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchNodeAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            Utils.IsEqual(node, result).Should().BeTrue();

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var nodes = new[]
            {
                new VariableNode
                {
                    NodeId = nodeIds[0],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType1"),
                    Description = "TestDescription1",
                    DisplayName = "TestDisplayName1",
                    BrowseName = "TestBrowseName1",
                    UserAccessLevel = 1
                },
                new VariableNode
                {
                    NodeId = nodeIds[1],
                    NodeClass = NodeClass.Variable,
                    AccessLevel = 1,
                    DataType = NodeId.Parse("ns=2;s=TestDataType2"),
                    Description = "TestDescription2",
                    DisplayName = "TestDisplayName2",
                    BrowseName = "TestBrowseName2",
                    UserAccessLevel = 1
                }
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = new DataValueCollection(request.NodesToRead
                        .Select(r =>
                        {
                            var value = new DataValue();
                            if (r.NodeId == nodeIds[0])
                            {
                                nodes[0].Read(null!, r.AttributeId, value);
                            }
                            else
                            {
                                nodes[1].Read(null!, r.AttributeId, value);
                            }
                            return value;
                        }));
                    return new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = new DiagnosticInfoCollection(
                            results.Select(r => new DiagnosticInfo()))
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeTrue();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeTrue();
            result.Errors.Count.Should().Be(2);
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);

            session.Channel.Verify();
        }
        [Test]
        public async Task FetchReferencesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var references = new ReferenceDescriptionCollection
            {
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId("ns=2;s=TestNode1"),
                    BrowseName = "TestBrowseName1",
                    DisplayName = "TestDisplayName1",
                    NodeClass = NodeClass.Variable
                },
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId("ns=2;s=TestNode2"),
                    BrowseName = "TestBrowseName2",
                    DisplayName = "TestDisplayName2",
                    NodeClass = NodeClass.Variable
                }
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                new BrowseResult
                {
                    References = references
                }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchReferencesAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            result.Should().BeEquivalentTo(references);

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchReferencesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>();

            // Act
            var result = await sut.FetchReferencesAsync(null, nodeIds).ConfigureAwait(false);

            // Assert
            result.Results.Should().BeEmpty();
            result.Errors.Should().BeEmpty();
        }

        [Test]
        public async Task FetchReferencesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var references = new ReferenceDescriptionCollection
            {
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId("ns=2;s=TestNode1"),
                    BrowseName = "TestBrowseName1",
                    DisplayName = "TestDisplayName1",
                    NodeClass = NodeClass.Variable
                },
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId("ns=2;s=TestNode2"),
                    BrowseName = "TestBrowseName2",
                    DisplayName = "TestDisplayName2",
                    NodeClass = NodeClass.Variable
                }
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
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
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchReferencesAsync(null, nodeIds,
                CancellationToken.None).ConfigureAwait(false);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(result.Results[0], references).Should().BeTrue();
            Utils.IsEqual(result.Results[1], references).Should().BeTrue();
            result.Errors.Count.Should().Be(2);
            result.Errors[0].StatusCode.Should().Be(StatusCodes.Bad);
            result.Errors[1].StatusCode.Should().Be(StatusCodes.Bad);
        }

        [Test]
        public async Task FetchReferencesAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchReferencesAsync(null,
                nodeIds, cts.Token).ConfigureAwait(false);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task FetchReferenceAsyncShouldReturnReferenceDescriptionAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var reference = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId("ns=2;s=TestNode1"),
                BrowseName = "TestBrowseName1",
                DisplayName = "TestDisplayName1",
                NodeClass = NodeClass.Variable
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                new BrowseResult
                {
                    References = [reference]
                }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchReferencesAsync(null, nodeId).ConfigureAwait(false);

            // Assert
            result.Count.Should().Be(1);
            result[0].Should().BeEquivalentTo(reference);

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchReferenceAsyncShouldThrowServiceResultExceptionForBadStatusCodeAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
            var sut = new NodeCacheContext(session);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var reference = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId("ns=2;s=TestNode1"),
                BrowseName = "TestBrowseName1",
                DisplayName = "TestDisplayName1",
                NodeClass = NodeClass.Variable
            };

            session.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
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
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchReferencesAsync(null, nodeId,
                CancellationToken.None).ConfigureAwait(false);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();

            session.Channel.Verify();
        }

        [Test]
        public async Task FetchReferenceAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var session = new SessionChannelWrapper();
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

            // Act
            Func<Task> act = async () => await sut.FetchReferencesAsync(null, nodeId, cts.Token).ConfigureAwait(false);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        private sealed class SessionChannelWrapper : ISession
        {
            private readonly SessionClient m_client;

            public Mock<ISession> Session { get; }
            public Mock<ITransportChannel> Channel { get; }

            private ISession m_session => Session.Object;

            public SessionChannelWrapper()
            {
                Session = new Mock<ISession>();
                Channel = new Mock<ITransportChannel>();
                m_client = new SessionClient(Channel.Object);
            }

            public ISessionFactory SessionFactory => m_session.SessionFactory;

            public ConfiguredEndpoint ConfiguredEndpoint => m_session.ConfiguredEndpoint;

            public string SessionName => m_session.SessionName;

            public double SessionTimeout => m_session.SessionTimeout;

            public object Handle => m_session.Handle;

            public IUserIdentity Identity => m_session.Identity;

            public IEnumerable<IUserIdentity> IdentityHistory => m_session.IdentityHistory;

            public NamespaceTable NamespaceUris => m_session.NamespaceUris;

            public StringTable ServerUris => m_session.ServerUris;

            public ISystemContext SystemContext => m_session.SystemContext;

            public IEncodeableFactory Factory => m_session.Factory;

            public ITypeTable TypeTree => m_session.TypeTree;

            public INodeCache NodeCache => m_session.NodeCache;

            public FilterContext FilterContext => m_session.FilterContext;

            public StringCollection PreferredLocales => m_session.PreferredLocales;

            public IEnumerable<Subscription> Subscriptions => m_session.Subscriptions;

            public int SubscriptionCount => m_session.SubscriptionCount;

            public bool DeleteSubscriptionsOnClose
            {
                get => m_session.DeleteSubscriptionsOnClose;
                set => m_session.DeleteSubscriptionsOnClose = value;
            }

            public int PublishRequestCancelDelayOnCloseSession
            {
                get
                => m_session.PublishRequestCancelDelayOnCloseSession; set
                => m_session.PublishRequestCancelDelayOnCloseSession = value;
            }
            public Subscription DefaultSubscription
            {
                get => m_session.DefaultSubscription; set
                => m_session.DefaultSubscription = value;
            }
            public int KeepAliveInterval
            {
                get => m_session.KeepAliveInterval; set
                => m_session.KeepAliveInterval = value;
            }

            public bool KeepAliveStopped => m_session.KeepAliveStopped;

            public DateTime LastKeepAliveTime => m_session.LastKeepAliveTime;

            public int LastKeepAliveTickCount => m_session.LastKeepAliveTickCount;

            public int OutstandingRequestCount => m_session.OutstandingRequestCount;

            public int DefunctRequestCount => m_session.DefunctRequestCount;

            public int GoodPublishRequestCount => m_session.GoodPublishRequestCount;

            public int MinPublishRequestCount
            {
                get => m_session.MinPublishRequestCount; set
                => m_session.MinPublishRequestCount = value;
            }
            public int MaxPublishRequestCount
            {
                get => m_session.MaxPublishRequestCount; set
                => m_session.MaxPublishRequestCount = value;
            }

            public bool Reconnecting => m_session.Reconnecting;

            public OperationLimits OperationLimits => m_session.OperationLimits;

            public uint ServerMaxContinuationPointsPerBrowse
                => m_session.ServerMaxContinuationPointsPerBrowse;

            public uint ServerMaxByteStringLength => m_session.ServerMaxByteStringLength;

            public bool TransferSubscriptionsOnReconnect
            {
                get
                => m_session.TransferSubscriptionsOnReconnect; set
                => m_session.TransferSubscriptionsOnReconnect = value;
            }

            public bool CheckDomain => m_session.CheckDomain;

            public ContinuationPointPolicy ContinuationPointPolicy
            {
                get
                => m_session.ContinuationPointPolicy; set
                => m_session.ContinuationPointPolicy = value;
            }

            public NodeId SessionId => m_client.SessionId;

            public bool Connected => m_client.Connected;

            public EndpointDescription Endpoint => m_client.Endpoint;

            public EndpointConfiguration EndpointConfiguration => m_client.EndpointConfiguration;

            public IServiceMessageContext MessageContext => m_client.MessageContext;

            public ITransportChannel NullableTransportChannel => m_client.NullableTransportChannel;

            public ITransportChannel TransportChannel => m_client.TransportChannel;

            public DiagnosticsMasks ReturnDiagnostics
            {
                get => m_client.ReturnDiagnostics; set
                => m_client.ReturnDiagnostics = value;
            }
            public int OperationTimeout
            {
                get => m_client.OperationTimeout; set
                => m_client.OperationTimeout = value;
            }
            public int DefaultTimeoutHint
            {
                get => m_client.DefaultTimeoutHint; set
                => m_client.DefaultTimeoutHint = value;
            }

            public bool Disposed => m_client.Disposed;

            public event KeepAliveEventHandler KeepAlive
            {
                add
                {
                    m_session.KeepAlive += value;
                }

                remove
                {
                    m_session.KeepAlive -= value;
                }
            }

            public event NotificationEventHandler Notification
            {
                add
                {
                    m_session.Notification += value;
                }

                remove
                {
                    m_session.Notification -= value;
                }
            }

            public event PublishErrorEventHandler PublishError
            {
                add
                {
                    m_session.PublishError += value;
                }

                remove
                {
                    m_session.PublishError -= value;
                }
            }

            public event PublishSequenceNumbersToAcknowledgeEventHandler PublishSequenceNumbersToAcknowledge
            {
                add
                {
                    m_session.PublishSequenceNumbersToAcknowledge += value;
                }

                remove
                {
                    m_session.PublishSequenceNumbersToAcknowledge -= value;
                }
            }

            public event EventHandler SubscriptionsChanged
            {
                add
                {
                    m_session.SubscriptionsChanged += value;
                }

                remove
                {
                    m_session.SubscriptionsChanged -= value;
                }
            }

            public event EventHandler SessionClosing
            {
                add
                {
                    m_session.SessionClosing += value;
                }

                remove
                {
                    m_session.SessionClosing -= value;
                }
            }

            public event EventHandler SessionConfigurationChanged
            {
                add
                {
                    m_session.SessionConfigurationChanged += value;
                }

                remove
                {
                    m_session.SessionConfigurationChanged -= value;
                }
            }

            public event RenewUserIdentityEventHandler RenewUserIdentity
            {
                add
                {
                    m_session.RenewUserIdentity += value;
                }

                remove
                {
                    m_session.RenewUserIdentity -= value;
                }
            }

            [Obsolete]
            public ResponseHeader ActivateSession(
                RequestHeader requestHeader,
                SignatureData clientSignature,
                SignedSoftwareCertificateCollection clientSoftwareCertificates,
                StringCollection localeIds,
                ExtensionObject userIdentityToken,
                SignatureData userTokenSignature,
                out byte[] serverNonce,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.ActivateSession(
                    requestHeader,
                    clientSignature,
                    clientSoftwareCertificates,
                    localeIds,
                    userIdentityToken,
                    userTokenSignature,
                    out serverNonce,
                    out results,
                    out diagnosticInfos);
            }

            public Task<ActivateSessionResponse> ActivateSessionAsync(
                RequestHeader requestHeader,
                SignatureData clientSignature,
                SignedSoftwareCertificateCollection clientSoftwareCertificates,
                StringCollection localeIds,
                ExtensionObject userIdentityToken,
                SignatureData userTokenSignature,
                CancellationToken ct)
            {
                return m_client.ActivateSessionAsync(
                    requestHeader,
                    clientSignature,
                    clientSoftwareCertificates,
                    localeIds,
                    userIdentityToken,
                    userTokenSignature,
                    ct);
            }

            [Obsolete]
            public ResponseHeader AddNodes(
                RequestHeader requestHeader,
                AddNodesItemCollection nodesToAdd,
                out AddNodesResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.AddNodes(
                    requestHeader,
                    nodesToAdd,
                    out results,
                    out diagnosticInfos);
            }

            public Task<AddNodesResponse> AddNodesAsync(
                RequestHeader requestHeader,
                AddNodesItemCollection nodesToAdd,
                CancellationToken ct)
            {
                return m_client.AddNodesAsync(requestHeader, nodesToAdd, ct);
            }

            [Obsolete]
            public ResponseHeader AddReferences(
                RequestHeader requestHeader,
                AddReferencesItemCollection referencesToAdd,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.AddReferences(
                    requestHeader,
                    referencesToAdd,
                    out results,
                    out diagnosticInfos);
            }

            public Task<AddReferencesResponse> AddReferencesAsync(
                RequestHeader requestHeader,
                AddReferencesItemCollection referencesToAdd,
                CancellationToken ct)
            {
                return m_client.AddReferencesAsync(requestHeader, referencesToAdd, ct);
            }

            public bool AddSubscription(Subscription subscription)
            {
                return m_session.AddSubscription(subscription);
            }

            public bool ApplySessionConfiguration(SessionConfiguration sessionConfiguration)
            {
                return m_session.ApplySessionConfiguration(sessionConfiguration);
            }

            public void AttachChannel(ITransportChannel channel)
            {
                m_client.AttachChannel(channel);
            }

            [Obsolete]
            public IAsyncResult BeginActivateSession(
                RequestHeader requestHeader,
                SignatureData clientSignature,
                SignedSoftwareCertificateCollection clientSoftwareCertificates,
                StringCollection localeIds,
                ExtensionObject userIdentityToken,
                SignatureData userTokenSignature,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginActivateSession(
                    requestHeader,
                    clientSignature,
                    clientSoftwareCertificates,
                    localeIds,
                    userIdentityToken,
                    userTokenSignature,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginAddNodes(
                RequestHeader requestHeader,
                AddNodesItemCollection nodesToAdd,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginAddNodes(requestHeader, nodesToAdd, callback, asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginAddReferences(
                RequestHeader requestHeader,
                AddReferencesItemCollection referencesToAdd,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginAddReferences(
                    requestHeader,
                    referencesToAdd,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginBrowse(
                RequestHeader requestHeader,
                ViewDescription view,
                uint requestedMaxReferencesPerNode,
                BrowseDescriptionCollection nodesToBrowse,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginBrowse(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowse,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginBrowseNext(
                RequestHeader requestHeader,
                bool releaseContinuationPoints,
                ByteStringCollection continuationPoints,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginBrowseNext(
                    requestHeader,
                    releaseContinuationPoints,
                    continuationPoints,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginCall(
                RequestHeader requestHeader,
                CallMethodRequestCollection methodsToCall,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginCall(requestHeader, methodsToCall, callback, asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginCancel(
                RequestHeader requestHeader,
                uint requestHandle,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginCancel(requestHeader, requestHandle, callback, asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginCloseSession(
                RequestHeader requestHeader,
                bool deleteSubscriptions,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginCloseSession(
                    requestHeader,
                    deleteSubscriptions,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginCreateMonitoredItems(
                RequestHeader requestHeader,
                uint subscriptionId,
                TimestampsToReturn timestampsToReturn,
                MonitoredItemCreateRequestCollection itemsToCreate,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginCreateMonitoredItems(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginCreateSession(
                RequestHeader requestHeader,
                ApplicationDescription clientDescription,
                string serverUri,
                string endpointUrl,
                string sessionName,
                byte[] clientNonce,
                byte[] clientCertificate,
                double requestedSessionTimeout,
                uint maxResponseMessageSize,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginCreateSession(
                    requestHeader,
                    clientDescription,
                    serverUri,
                    endpointUrl,
                    sessionName,
                    clientNonce,
                    clientCertificate,
                    requestedSessionTimeout,
                    maxResponseMessageSize,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginCreateSubscription(
                RequestHeader requestHeader,
                double requestedPublishingInterval,
                uint requestedLifetimeCount,
                uint requestedMaxKeepAliveCount,
                uint maxNotificationsPerPublish,
                bool publishingEnabled,
                byte priority,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginCreateSubscription(
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginDeleteMonitoredItems(
                RequestHeader requestHeader,
                uint subscriptionId,
                UInt32Collection monitoredItemIds,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginDeleteMonitoredItems(
                    requestHeader,
                    subscriptionId,
                    monitoredItemIds,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginDeleteNodes(
                RequestHeader requestHeader,
                DeleteNodesItemCollection nodesToDelete,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginDeleteNodes(
                    requestHeader,
                    nodesToDelete,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginDeleteReferences(
                RequestHeader requestHeader,
                DeleteReferencesItemCollection referencesToDelete,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginDeleteReferences(
                    requestHeader,
                    referencesToDelete,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginDeleteSubscriptions(
                RequestHeader requestHeader,
                UInt32Collection subscriptionIds,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginDeleteSubscriptions(
                    requestHeader,
                    subscriptionIds,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginHistoryRead(
                RequestHeader requestHeader,
                ExtensionObject historyReadDetails,
                TimestampsToReturn timestampsToReturn,
                bool releaseContinuationPoints,
                HistoryReadValueIdCollection nodesToRead,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginHistoryRead(
                    requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginHistoryUpdate(
                RequestHeader requestHeader,
                ExtensionObjectCollection historyUpdateDetails,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginHistoryUpdate(
                    requestHeader,
                    historyUpdateDetails,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginModifyMonitoredItems(
                RequestHeader requestHeader,
                uint subscriptionId,
                TimestampsToReturn timestampsToReturn,
                MonitoredItemModifyRequestCollection itemsToModify,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginModifyMonitoredItems(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginModifySubscription(
                RequestHeader requestHeader,
                uint subscriptionId,
                double requestedPublishingInterval,
                uint requestedLifetimeCount,
                uint requestedMaxKeepAliveCount,
                uint maxNotificationsPerPublish,
                byte priority,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginModifySubscription(
                    requestHeader,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    callback,
                    asyncState);
            }

            public bool BeginPublish(int timeout)
            {
                return m_session.BeginPublish(timeout);
            }

            [Obsolete]
            public IAsyncResult BeginPublish(
                RequestHeader requestHeader,
                SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginPublish(
                    requestHeader,
                    subscriptionAcknowledgements,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginQueryFirst(
                RequestHeader requestHeader,
                ViewDescription view,
                NodeTypeDescriptionCollection nodeTypes,
                ContentFilter filter,
                uint maxDataSetsToReturn,
                uint maxReferencesToReturn,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginQueryFirst(
                    requestHeader,
                    view,
                    nodeTypes,
                    filter,
                    maxDataSetsToReturn,
                    maxReferencesToReturn,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginQueryNext(
                RequestHeader requestHeader,
                bool releaseContinuationPoint,
                byte[] continuationPoint,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginQueryNext(
                    requestHeader,
                    releaseContinuationPoint,
                    continuationPoint,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginRead(
                RequestHeader requestHeader,
                double maxAge,
                TimestampsToReturn timestampsToReturn,
                ReadValueIdCollection nodesToRead,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginRead(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    nodesToRead,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginRegisterNodes(
                RequestHeader requestHeader,
                NodeIdCollection nodesToRegister,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginRegisterNodes(
                    requestHeader,
                    nodesToRegister,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginRepublish(
                RequestHeader requestHeader,
                uint subscriptionId,
                uint retransmitSequenceNumber,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginRepublish(
                    requestHeader,
                    subscriptionId,
                    retransmitSequenceNumber,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginSetMonitoringMode(
                RequestHeader requestHeader,
                uint subscriptionId,
                MonitoringMode monitoringMode,
                UInt32Collection monitoredItemIds,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginSetMonitoringMode(
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginSetPublishingMode(
                RequestHeader requestHeader,
                bool publishingEnabled,
                UInt32Collection subscriptionIds,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginSetPublishingMode(
                    requestHeader,
                    publishingEnabled,
                    subscriptionIds,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginSetTriggering(
                RequestHeader requestHeader,
                uint subscriptionId,
                uint triggeringItemId,
                UInt32Collection linksToAdd,
                UInt32Collection linksToRemove,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginSetTriggering(
                    requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginTransferSubscriptions(
                RequestHeader requestHeader,
                UInt32Collection subscriptionIds,
                bool sendInitialValues,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginTransferSubscriptions(
                    requestHeader,
                    subscriptionIds,
                    sendInitialValues,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginTranslateBrowsePathsToNodeIds(
                RequestHeader requestHeader,
                BrowsePathCollection browsePaths,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginTranslateBrowsePathsToNodeIds(
                    requestHeader,
                    browsePaths,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginUnregisterNodes(
                RequestHeader requestHeader,
                NodeIdCollection nodesToUnregister,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginUnregisterNodes(
                    requestHeader,
                    nodesToUnregister,
                    callback,
                    asyncState);
            }

            [Obsolete]
            public IAsyncResult BeginWrite(
                RequestHeader requestHeader,
                WriteValueCollection nodesToWrite,
                AsyncCallback callback,
                object asyncState)
            {
                return m_client.BeginWrite(requestHeader, nodesToWrite, callback, asyncState);
            }

            [Obsolete]
            public ResponseHeader Browse(
                RequestHeader requestHeader,
                ViewDescription view,
                uint requestedMaxReferencesPerNode,
                BrowseDescriptionCollection nodesToBrowse,
                out BrowseResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.Browse(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowse,
                    out results,
                    out diagnosticInfos);
            }

            public Task<BrowseResponse> BrowseAsync(
                RequestHeader requestHeader,
                ViewDescription view,
                uint requestedMaxReferencesPerNode,
                BrowseDescriptionCollection nodesToBrowse,
                CancellationToken ct)
            {
                return m_client.BrowseAsync(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowse,
                    ct);
            }

            [Obsolete]
            public ResponseHeader BrowseNext(
                RequestHeader requestHeader,
                bool releaseContinuationPoints,
                ByteStringCollection continuationPoints,
                out BrowseResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.BrowseNext(
                    requestHeader,
                    releaseContinuationPoints,
                    continuationPoints,
                    out results,
                    out diagnosticInfos);
            }

            public Task<BrowseNextResponse> BrowseNextAsync(
                RequestHeader requestHeader,
                bool releaseContinuationPoints,
                ByteStringCollection continuationPoints,
                CancellationToken ct)
            {
                return m_client.BrowseNextAsync(
                    requestHeader,
                    releaseContinuationPoints,
                    continuationPoints,
                    ct);
            }

            [Obsolete]
            public ResponseHeader Call(
                RequestHeader requestHeader,
                CallMethodRequestCollection methodsToCall,
                out CallMethodResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.Call(
                    requestHeader,
                    methodsToCall,
                    out results,
                    out diagnosticInfos);
            }

            public Task<CallResponse> CallAsync(
                RequestHeader requestHeader,
                CallMethodRequestCollection methodsToCall,
                CancellationToken ct)
            {
                return m_client.CallAsync(requestHeader, methodsToCall, ct);
            }

            [Obsolete]
            public ResponseHeader Cancel(
                RequestHeader requestHeader,
                uint requestHandle,
                out uint cancelCount)
            {
                return m_client.Cancel(requestHeader, requestHandle, out cancelCount);
            }

            public Task<CancelResponse> CancelAsync(
                RequestHeader requestHeader,
                uint requestHandle,
                CancellationToken ct)
            {
                return m_client.CancelAsync(requestHeader, requestHandle, ct);
            }

            public Task ChangePreferredLocalesAsync(
                StringCollection preferredLocales,
                CancellationToken ct = default)
            {
                return m_session.ChangePreferredLocalesAsync(preferredLocales, ct);
            }

            public Task<StatusCode> CloseAsync(bool closeChannel, CancellationToken ct = default)
            {
                return m_session.CloseAsync(closeChannel, ct);
            }

            public Task<StatusCode> CloseAsync(int timeout, CancellationToken ct = default)
            {
                return m_session.CloseAsync(timeout, ct);
            }

            public Task<StatusCode> CloseAsync(
                int timeout,
                bool closeChannel,
                CancellationToken ct = default)
            {
                return m_session.CloseAsync(timeout, closeChannel, ct);
            }

            public Task<StatusCode> CloseAsync(CancellationToken ct = default)
            {
                return m_client.CloseAsync(ct);
            }

            [Obsolete]
            public ResponseHeader CloseSession(
                RequestHeader requestHeader,
                bool deleteSubscriptions)
            {
                return m_client.CloseSession(requestHeader, deleteSubscriptions);
            }

            public Task<CloseSessionResponse> CloseSessionAsync(
                RequestHeader requestHeader,
                bool deleteSubscriptions,
                CancellationToken ct)
            {
                return m_client.CloseSessionAsync(requestHeader, deleteSubscriptions, ct);
            }

            [Obsolete]
            public ResponseHeader CreateMonitoredItems(
                RequestHeader requestHeader,
                uint subscriptionId,
                TimestampsToReturn timestampsToReturn,
                MonitoredItemCreateRequestCollection itemsToCreate,
                out MonitoredItemCreateResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.CreateMonitoredItems(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    out results,
                    out diagnosticInfos);
            }

            public Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
                RequestHeader requestHeader,
                uint subscriptionId,
                TimestampsToReturn timestampsToReturn,
                MonitoredItemCreateRequestCollection itemsToCreate,
                CancellationToken ct)
            {
                return m_client.CreateMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    ct);
            }

            [Obsolete]
            public ResponseHeader CreateSession(
                RequestHeader requestHeader,
                ApplicationDescription clientDescription,
                string serverUri,
                string endpointUrl,
                string sessionName,
                byte[] clientNonce,
                byte[] clientCertificate,
                double requestedSessionTimeout,
                uint maxResponseMessageSize,
                out NodeId sessionId,
                out NodeId authenticationToken,
                out double revisedSessionTimeout,
                out byte[] serverNonce,
                out byte[] serverCertificate,
                out EndpointDescriptionCollection serverEndpoints,
                out SignedSoftwareCertificateCollection serverSoftwareCertificates,
                out SignatureData serverSignature,
                out uint maxRequestMessageSize)
            {
                return m_client.CreateSession(
                    requestHeader,
                    clientDescription,
                    serverUri,
                    endpointUrl,
                    sessionName,
                    clientNonce,
                    clientCertificate,
                    requestedSessionTimeout,
                    maxResponseMessageSize,
                    out sessionId,
                    out authenticationToken,
                    out revisedSessionTimeout,
                    out serverNonce,
                    out serverCertificate,
                    out serverEndpoints,
                    out serverSoftwareCertificates,
                    out serverSignature,
                    out maxRequestMessageSize);
            }

            public Task<CreateSessionResponse> CreateSessionAsync(
                RequestHeader requestHeader,
                ApplicationDescription clientDescription,
                string serverUri,
                string endpointUrl,
                string sessionName,
                byte[] clientNonce,
                byte[] clientCertificate,
                double requestedSessionTimeout,
                uint maxResponseMessageSize,
                CancellationToken ct)
            {
                return m_client.CreateSessionAsync(
                    requestHeader,
                    clientDescription,
                    serverUri,
                    endpointUrl,
                    sessionName,
                    clientNonce,
                    clientCertificate,
                    requestedSessionTimeout,
                    maxResponseMessageSize,
                    ct);
            }

            [Obsolete]
            public ResponseHeader CreateSubscription(
                RequestHeader requestHeader,
                double requestedPublishingInterval,
                uint requestedLifetimeCount,
                uint requestedMaxKeepAliveCount,
                uint maxNotificationsPerPublish,
                bool publishingEnabled,
                byte priority,
                out uint subscriptionId,
                out double revisedPublishingInterval,
                out uint revisedLifetimeCount,
                out uint revisedMaxKeepAliveCount)
            {
                return m_client.CreateSubscription(
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    out subscriptionId,
                    out revisedPublishingInterval,
                    out revisedLifetimeCount,
                    out revisedMaxKeepAliveCount);
            }

            public Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
                RequestHeader requestHeader,
                double requestedPublishingInterval,
                uint requestedLifetimeCount,
                uint requestedMaxKeepAliveCount,
                uint maxNotificationsPerPublish,
                bool publishingEnabled,
                byte priority,
                CancellationToken ct)
            {
                return m_client.CreateSubscriptionAsync(
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    ct);
            }

            [Obsolete]
            public ResponseHeader DeleteMonitoredItems(
                RequestHeader requestHeader,
                uint subscriptionId,
                UInt32Collection monitoredItemIds,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.DeleteMonitoredItems(
                    requestHeader,
                    subscriptionId,
                    monitoredItemIds,
                    out results,
                    out diagnosticInfos);
            }

            public Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
                RequestHeader requestHeader,
                uint subscriptionId,
                UInt32Collection monitoredItemIds,
                CancellationToken ct)
            {
                return m_client.DeleteMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    monitoredItemIds,
                    ct);
            }

            [Obsolete]
            public ResponseHeader DeleteNodes(
                RequestHeader requestHeader,
                DeleteNodesItemCollection nodesToDelete,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.DeleteNodes(
                    requestHeader,
                    nodesToDelete,
                    out results,
                    out diagnosticInfos);
            }

            public Task<DeleteNodesResponse> DeleteNodesAsync(
                RequestHeader requestHeader,
                DeleteNodesItemCollection nodesToDelete,
                CancellationToken ct)
            {
                return m_client.DeleteNodesAsync(requestHeader, nodesToDelete, ct);
            }

            [Obsolete]
            public ResponseHeader DeleteReferences(
                RequestHeader requestHeader,
                DeleteReferencesItemCollection referencesToDelete,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.DeleteReferences(
                    requestHeader,
                    referencesToDelete,
                    out results,
                    out diagnosticInfos);
            }

            public Task<DeleteReferencesResponse> DeleteReferencesAsync(
                RequestHeader requestHeader,
                DeleteReferencesItemCollection referencesToDelete,
                CancellationToken ct)
            {
                return m_client.DeleteReferencesAsync(requestHeader, referencesToDelete, ct);
            }

            [Obsolete]
            public ResponseHeader DeleteSubscriptions(
                RequestHeader requestHeader,
                UInt32Collection subscriptionIds,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.DeleteSubscriptions(
                    requestHeader,
                    subscriptionIds,
                    out results,
                    out diagnosticInfos);
            }

            public Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                RequestHeader requestHeader,
                UInt32Collection subscriptionIds,
                CancellationToken ct)
            {
                return m_client.DeleteSubscriptionsAsync(requestHeader, subscriptionIds, ct);
            }

            public void DetachChannel()
            {
                m_client.DetachChannel();
            }

            public void Dispose()
            {
                m_client.Dispose();
            }

            [Obsolete]
            public ResponseHeader EndActivateSession(
                IAsyncResult result,
                out byte[] serverNonce,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndActivateSession(
                    result,
                    out serverNonce,
                    out results,
                    out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndAddNodes(
                IAsyncResult result,
                out AddNodesResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndAddNodes(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndAddReferences(
                IAsyncResult result,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndAddReferences(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndBrowse(
                IAsyncResult result,
                out BrowseResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndBrowse(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndBrowseNext(
                IAsyncResult result,
                out BrowseResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndBrowseNext(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndCall(
                IAsyncResult result,
                out CallMethodResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndCall(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndCancel(IAsyncResult result, out uint cancelCount)
            {
                return m_client.EndCancel(result, out cancelCount);
            }

            [Obsolete]
            public ResponseHeader EndCloseSession(IAsyncResult result)
            {
                return m_client.EndCloseSession(result);
            }

            [Obsolete]
            public ResponseHeader EndCreateMonitoredItems(
                IAsyncResult result,
                out MonitoredItemCreateResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndCreateMonitoredItems(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndCreateSession(
                IAsyncResult result,
                out NodeId sessionId,
                out NodeId authenticationToken,
                out double revisedSessionTimeout,
                out byte[] serverNonce,
                out byte[] serverCertificate,
                out EndpointDescriptionCollection serverEndpoints,
                out SignedSoftwareCertificateCollection serverSoftwareCertificates,
                out SignatureData serverSignature,
                out uint maxRequestMessageSize)
            {
                return m_client.EndCreateSession(
                    result,
                    out sessionId,
                    out authenticationToken,
                    out revisedSessionTimeout,
                    out serverNonce,
                    out serverCertificate,
                    out serverEndpoints,
                    out serverSoftwareCertificates,
                    out serverSignature,
                    out maxRequestMessageSize);
            }

            [Obsolete]
            public ResponseHeader EndCreateSubscription(
                IAsyncResult result,
                out uint subscriptionId,
                out double revisedPublishingInterval,
                out uint revisedLifetimeCount,
                out uint revisedMaxKeepAliveCount)
            {
                return m_client.EndCreateSubscription(
                    result,
                    out subscriptionId,
                    out revisedPublishingInterval,
                    out revisedLifetimeCount,
                    out revisedMaxKeepAliveCount);
            }

            [Obsolete]
            public ResponseHeader EndDeleteMonitoredItems(
                IAsyncResult result,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndDeleteMonitoredItems(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndDeleteNodes(
                IAsyncResult result,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndDeleteNodes(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndDeleteReferences(
                IAsyncResult result,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndDeleteReferences(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndDeleteSubscriptions(
                IAsyncResult result,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndDeleteSubscriptions(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndHistoryRead(
                IAsyncResult result,
                out HistoryReadResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndHistoryRead(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndHistoryUpdate(
                IAsyncResult result,
                out HistoryUpdateResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndHistoryUpdate(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndModifyMonitoredItems(
                IAsyncResult result,
                out MonitoredItemModifyResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndModifyMonitoredItems(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndModifySubscription(
                IAsyncResult result,
                out double revisedPublishingInterval,
                out uint revisedLifetimeCount,
                out uint revisedMaxKeepAliveCount)
            {
                return m_client.EndModifySubscription(
                    result,
                    out revisedPublishingInterval,
                    out revisedLifetimeCount,
                    out revisedMaxKeepAliveCount);
            }

            [Obsolete]
            public ResponseHeader EndPublish(
                IAsyncResult result,
                out uint subscriptionId,
                out UInt32Collection availableSequenceNumbers,
                out bool moreNotifications,
                out NotificationMessage notificationMessage,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndPublish(
                    result,
                    out subscriptionId,
                    out availableSequenceNumbers,
                    out moreNotifications,
                    out notificationMessage,
                    out results,
                    out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndQueryFirst(
                IAsyncResult result,
                out QueryDataSetCollection queryDataSets,
                out byte[] continuationPoint,
                out ParsingResultCollection parsingResults,
                out DiagnosticInfoCollection diagnosticInfos,
                out ContentFilterResult filterResult)
            {
                return m_client.EndQueryFirst(
                    result,
                    out queryDataSets,
                    out continuationPoint,
                    out parsingResults,
                    out diagnosticInfos,
                    out filterResult);
            }

            [Obsolete]
            public ResponseHeader EndQueryNext(
                IAsyncResult result,
                out QueryDataSetCollection queryDataSets,
                out byte[] revisedContinuationPoint)
            {
                return m_client.EndQueryNext(
                    result,
                    out queryDataSets,
                    out revisedContinuationPoint);
            }

            [Obsolete]
            public ResponseHeader EndRead(
                IAsyncResult result,
                out DataValueCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndRead(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndRegisterNodes(
                IAsyncResult result,
                out NodeIdCollection registeredNodeIds)
            {
                return m_client.EndRegisterNodes(result, out registeredNodeIds);
            }

            [Obsolete]
            public ResponseHeader EndRepublish(
                IAsyncResult result,
                out NotificationMessage notificationMessage)
            {
                return m_client.EndRepublish(result, out notificationMessage);
            }

            [Obsolete]
            public ResponseHeader EndSetMonitoringMode(
                IAsyncResult result,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndSetMonitoringMode(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndSetPublishingMode(
                IAsyncResult result,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndSetPublishingMode(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndSetTriggering(
                IAsyncResult result,
                out StatusCodeCollection addResults,
                out DiagnosticInfoCollection addDiagnosticInfos,
                out StatusCodeCollection removeResults,
                out DiagnosticInfoCollection removeDiagnosticInfos)
            {
                return m_client.EndSetTriggering(
                    result,
                    out addResults,
                    out addDiagnosticInfos,
                    out removeResults,
                    out removeDiagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndTransferSubscriptions(
                IAsyncResult result,
                out TransferResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndTransferSubscriptions(result, out results, out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndTranslateBrowsePathsToNodeIds(
                IAsyncResult result,
                out BrowsePathResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndTranslateBrowsePathsToNodeIds(
                    result,
                    out results,
                    out diagnosticInfos);
            }

            [Obsolete]
            public ResponseHeader EndUnregisterNodes(IAsyncResult result)
            {
                return m_client.EndUnregisterNodes(result);
            }

            [Obsolete]
            public ResponseHeader EndWrite(
                IAsyncResult result,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.EndWrite(result, out results, out diagnosticInfos);
            }

            public Task FetchNamespaceTablesAsync(CancellationToken ct = default)
            {
                return m_session.FetchNamespaceTablesAsync(ct);
            }

            public Task FetchTypeTreeAsync(ExpandedNodeId typeId, CancellationToken ct = default)
            {
                return m_session.FetchTypeTreeAsync(typeId, ct);
            }

            public Task FetchTypeTreeAsync(
                ExpandedNodeIdCollection typeIds,
                CancellationToken ct = default)
            {
                return m_session.FetchTypeTreeAsync(typeIds, ct);
            }

            [Obsolete]
            public ResponseHeader HistoryRead(
                RequestHeader requestHeader,
                ExtensionObject historyReadDetails,
                TimestampsToReturn timestampsToReturn,
                bool releaseContinuationPoints,
                HistoryReadValueIdCollection nodesToRead,
                out HistoryReadResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.HistoryRead(
                    requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);
            }

            public Task<HistoryReadResponse> HistoryReadAsync(
                RequestHeader requestHeader,
                ExtensionObject historyReadDetails,
                TimestampsToReturn timestampsToReturn,
                bool releaseContinuationPoints,
                HistoryReadValueIdCollection nodesToRead,
                CancellationToken ct)
            {
                return m_client.HistoryReadAsync(
                    requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    ct);
            }

            [Obsolete]
            public ResponseHeader HistoryUpdate(
                RequestHeader requestHeader,
                ExtensionObjectCollection historyUpdateDetails,
                out HistoryUpdateResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.HistoryUpdate(
                    requestHeader,
                    historyUpdateDetails,
                    out results,
                    out diagnosticInfos);
            }

            public Task<HistoryUpdateResponse> HistoryUpdateAsync(
                RequestHeader requestHeader,
                ExtensionObjectCollection historyUpdateDetails,
                CancellationToken ct)
            {
                return m_client.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct);
            }

            public IEnumerable<Subscription> Load(
                Stream stream,
                bool transferSubscriptions = false,
                IEnumerable<Type> knownTypes = null)
            {
                return m_session.Load(stream, transferSubscriptions, knownTypes);
            }

            [Obsolete]
            public ResponseHeader ModifyMonitoredItems(
                RequestHeader requestHeader,
                uint subscriptionId,
                TimestampsToReturn timestampsToReturn,
                MonitoredItemModifyRequestCollection itemsToModify,
                out MonitoredItemModifyResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.ModifyMonitoredItems(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    out results,
                    out diagnosticInfos);
            }

            public Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
                RequestHeader requestHeader,
                uint subscriptionId,
                TimestampsToReturn timestampsToReturn,
                MonitoredItemModifyRequestCollection itemsToModify,
                CancellationToken ct)
            {
                return m_client.ModifyMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    ct);
            }

            [Obsolete]
            public ResponseHeader ModifySubscription(
                RequestHeader requestHeader,
                uint subscriptionId,
                double requestedPublishingInterval,
                uint requestedLifetimeCount,
                uint requestedMaxKeepAliveCount,
                uint maxNotificationsPerPublish,
                byte priority,
                out double revisedPublishingInterval,
                out uint revisedLifetimeCount,
                out uint revisedMaxKeepAliveCount)
            {
                return m_client.ModifySubscription(
                    requestHeader,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    out revisedPublishingInterval,
                    out revisedLifetimeCount,
                    out revisedMaxKeepAliveCount);
            }

            public Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
                RequestHeader requestHeader,
                uint subscriptionId,
                double requestedPublishingInterval,
                uint requestedLifetimeCount,
                uint requestedMaxKeepAliveCount,
                uint maxNotificationsPerPublish,
                byte priority,
                CancellationToken ct)
            {
                return m_client.ModifySubscriptionAsync(
                    requestHeader,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    ct);
            }

            public uint NewRequestHandle()
            {
                return m_client.NewRequestHandle();
            }

            public Task OpenAsync(
                string sessionName,
                IUserIdentity identity,
                CancellationToken ct = default)
            {
                return m_session.OpenAsync(sessionName, identity, ct);
            }

            public Task OpenAsync(
                string sessionName,
                uint sessionTimeout,
                IUserIdentity identity,
                IList<string> preferredLocales,
                CancellationToken ct = default)
            {
                return m_session.OpenAsync(
                    sessionName,
                    sessionTimeout,
                    identity,
                    preferredLocales,
                    ct);
            }

            public Task OpenAsync(
                string sessionName,
                uint sessionTimeout,
                IUserIdentity identity,
                IList<string> preferredLocales,
                bool checkDomain,
                CancellationToken ct = default)
            {
                return m_session.OpenAsync(
                    sessionName,
                    sessionTimeout,
                    identity,
                    preferredLocales,
                    checkDomain,
                    ct);
            }

            public Task OpenAsync(
                string sessionName,
                uint sessionTimeout,
                IUserIdentity identity,
                IList<string> preferredLocales,
                bool checkDomain,
                bool closeChannel,
                CancellationToken ct = default)
            {
                return m_session.OpenAsync(
                    sessionName,
                    sessionTimeout,
                    identity,
                    preferredLocales,
                    checkDomain,
                    closeChannel,
                    ct);
            }

            [Obsolete]
            public ResponseHeader Publish(
                RequestHeader requestHeader,
                SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
                out uint subscriptionId,
                out UInt32Collection availableSequenceNumbers,
                out bool moreNotifications,
                out NotificationMessage notificationMessage,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.Publish(
                    requestHeader,
                    subscriptionAcknowledgements,
                    out subscriptionId,
                    out availableSequenceNumbers,
                    out moreNotifications,
                    out notificationMessage,
                    out results,
                    out diagnosticInfos);
            }

            public Task<PublishResponse> PublishAsync(
                RequestHeader requestHeader,
                SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
                CancellationToken ct)
            {
                return m_client.PublishAsync(requestHeader, subscriptionAcknowledgements, ct);
            }

            [Obsolete]
            public ResponseHeader QueryFirst(
                RequestHeader requestHeader,
                ViewDescription view,
                NodeTypeDescriptionCollection nodeTypes,
                ContentFilter filter,
                uint maxDataSetsToReturn,
                uint maxReferencesToReturn,
                out QueryDataSetCollection queryDataSets,
                out byte[] continuationPoint,
                out ParsingResultCollection parsingResults,
                out DiagnosticInfoCollection diagnosticInfos,
                out ContentFilterResult filterResult)
            {
                return m_client.QueryFirst(
                    requestHeader,
                    view,
                    nodeTypes,
                    filter,
                    maxDataSetsToReturn,
                    maxReferencesToReturn,
                    out queryDataSets,
                    out continuationPoint,
                    out parsingResults,
                    out diagnosticInfos,
                    out filterResult);
            }

            public Task<QueryFirstResponse> QueryFirstAsync(
                RequestHeader requestHeader,
                ViewDescription view,
                NodeTypeDescriptionCollection nodeTypes,
                ContentFilter filter,
                uint maxDataSetsToReturn,
                uint maxReferencesToReturn,
                CancellationToken ct)
            {
                return m_client.QueryFirstAsync(
                    requestHeader,
                    view,
                    nodeTypes,
                    filter,
                    maxDataSetsToReturn,
                    maxReferencesToReturn,
                    ct);
            }

            [Obsolete]
            public ResponseHeader QueryNext(
                RequestHeader requestHeader,
                bool releaseContinuationPoint,
                byte[] continuationPoint,
                out QueryDataSetCollection queryDataSets,
                out byte[] revisedContinuationPoint)
            {
                return m_client.QueryNext(
                    requestHeader,
                    releaseContinuationPoint,
                    continuationPoint,
                    out queryDataSets,
                    out revisedContinuationPoint);
            }

            public Task<QueryNextResponse> QueryNextAsync(
                RequestHeader requestHeader,
                bool releaseContinuationPoint,
                byte[] continuationPoint,
                CancellationToken ct)
            {
                return m_client.QueryNextAsync(
                    requestHeader,
                    releaseContinuationPoint,
                    continuationPoint,
                    ct);
            }

            public Task<bool> ReactivateSubscriptionsAsync(
                SubscriptionCollection subscriptions,
                bool sendInitialValues,
                CancellationToken ct = default)
            {
                return m_session.ReactivateSubscriptionsAsync(subscriptions, sendInitialValues, ct);
            }

            [Obsolete]
            public ResponseHeader Read(
                RequestHeader requestHeader,
                double maxAge,
                TimestampsToReturn timestampsToReturn,
                ReadValueIdCollection nodesToRead,
                out DataValueCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.Read(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);
            }

            public Task<ReadResponse> ReadAsync(
                RequestHeader requestHeader,
                double maxAge,
                TimestampsToReturn timestampsToReturn,
                ReadValueIdCollection nodesToRead,
                CancellationToken ct)
            {
                return m_client.ReadAsync(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    nodesToRead,
                    ct);
            }

            public Task ReconnectAsync(CancellationToken ct = default)
            {
                return m_session.ReconnectAsync(ct);
            }

            public Task ReconnectAsync(
                ITransportWaitingConnection connection,
                CancellationToken ct = default)
            {
                return m_session.ReconnectAsync(connection, ct);
            }

            public Task ReconnectAsync(ITransportChannel channel, CancellationToken ct = default)
            {
                return m_session.ReconnectAsync(channel, ct);
            }

            [Obsolete]
            public ResponseHeader RegisterNodes(
                RequestHeader requestHeader,
                NodeIdCollection nodesToRegister,
                out NodeIdCollection registeredNodeIds)
            {
                return m_client.RegisterNodes(
                    requestHeader,
                    nodesToRegister,
                    out registeredNodeIds);
            }

            public Task<RegisterNodesResponse> RegisterNodesAsync(
                RequestHeader requestHeader,
                NodeIdCollection nodesToRegister,
                CancellationToken ct)
            {
                return m_client.RegisterNodesAsync(requestHeader, nodesToRegister, ct);
            }

            public Task ReloadInstanceCertificateAsync(CancellationToken ct = default)
            {
                return m_session.ReloadInstanceCertificateAsync(ct);
            }

            public Task<bool> RemoveSubscriptionAsync(
                Subscription subscription,
                CancellationToken ct = default)
            {
                return m_session.RemoveSubscriptionAsync(subscription, ct);
            }

            public Task<bool> RemoveSubscriptionsAsync(
                IEnumerable<Subscription> subscriptions,
                CancellationToken ct = default)
            {
                return m_session.RemoveSubscriptionsAsync(subscriptions, ct);
            }

            public bool RemoveTransferredSubscription(Subscription subscription)
            {
                return m_session.RemoveTransferredSubscription(subscription);
            }

            [Obsolete]
            public ResponseHeader Republish(
                RequestHeader requestHeader,
                uint subscriptionId,
                uint retransmitSequenceNumber,
                out NotificationMessage notificationMessage)
            {
                return m_client.Republish(
                    requestHeader,
                    subscriptionId,
                    retransmitSequenceNumber,
                    out notificationMessage);
            }

            public Task<(bool, ServiceResult)> RepublishAsync(
                uint subscriptionId,
                uint sequenceNumber,
                CancellationToken ct = default)
            {
                return m_session.RepublishAsync(subscriptionId, sequenceNumber, ct);
            }

            public Task<RepublishResponse> RepublishAsync(
                RequestHeader requestHeader,
                uint subscriptionId,
                uint retransmitSequenceNumber,
                CancellationToken ct)
            {
                return m_client.RepublishAsync(
                    requestHeader,
                    subscriptionId,
                    retransmitSequenceNumber,
                    ct);
            }

            public void Save(
                Stream stream,
                IEnumerable<Subscription> subscriptions,
                IEnumerable<Type> knownTypes = null)
            {
                m_session.Save(stream, subscriptions, knownTypes);
            }

            public SessionConfiguration SaveSessionConfiguration(Stream stream = null)
            {
                return m_session.SaveSessionConfiguration(stream);
            }

            [Obsolete]
            public ResponseHeader SetMonitoringMode(
                RequestHeader requestHeader,
                uint subscriptionId,
                MonitoringMode monitoringMode,
                UInt32Collection monitoredItemIds,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.SetMonitoringMode(
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    out results,
                    out diagnosticInfos);
            }

            public Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
                RequestHeader requestHeader,
                uint subscriptionId,
                MonitoringMode monitoringMode,
                UInt32Collection monitoredItemIds,
                CancellationToken ct)
            {
                return m_client.SetMonitoringModeAsync(
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    ct);
            }

            [Obsolete]
            public ResponseHeader SetPublishingMode(
                RequestHeader requestHeader,
                bool publishingEnabled,
                UInt32Collection subscriptionIds,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.SetPublishingMode(
                    requestHeader,
                    publishingEnabled,
                    subscriptionIds,
                    out results,
                    out diagnosticInfos);
            }

            public Task<SetPublishingModeResponse> SetPublishingModeAsync(
                RequestHeader requestHeader,
                bool publishingEnabled,
                UInt32Collection subscriptionIds,
                CancellationToken ct)
            {
                return m_client.SetPublishingModeAsync(
                    requestHeader,
                    publishingEnabled,
                    subscriptionIds,
                    ct);
            }

            [Obsolete]
            public ResponseHeader SetTriggering(
                RequestHeader requestHeader,
                uint subscriptionId,
                uint triggeringItemId,
                UInt32Collection linksToAdd,
                UInt32Collection linksToRemove,
                out StatusCodeCollection addResults,
                out DiagnosticInfoCollection addDiagnosticInfos,
                out StatusCodeCollection removeResults,
                out DiagnosticInfoCollection removeDiagnosticInfos)
            {
                return m_client.SetTriggering(
                    requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    out addResults,
                    out addDiagnosticInfos,
                    out removeResults,
                    out removeDiagnosticInfos);
            }

            public Task<SetTriggeringResponse> SetTriggeringAsync(
                RequestHeader requestHeader,
                uint subscriptionId,
                uint triggeringItemId,
                UInt32Collection linksToAdd,
                UInt32Collection linksToRemove,
                CancellationToken ct)
            {
                return m_client.SetTriggeringAsync(
                    requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    ct);
            }

            public void StartPublishing(int timeout, bool fullQueue)
            {
                m_session.StartPublishing(timeout, fullQueue);
            }

            [Obsolete]
            public ResponseHeader TransferSubscriptions(
                RequestHeader requestHeader,
                UInt32Collection subscriptionIds,
                bool sendInitialValues,
                out TransferResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.TransferSubscriptions(
                    requestHeader,
                    subscriptionIds,
                    sendInitialValues,
                    out results,
                    out diagnosticInfos);
            }

            public Task<bool> TransferSubscriptionsAsync(
                SubscriptionCollection subscriptions,
                bool sendInitialValues,
                CancellationToken ct = default)
            {
                return m_session.TransferSubscriptionsAsync(subscriptions, sendInitialValues, ct);
            }

            public Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
                RequestHeader requestHeader,
                UInt32Collection subscriptionIds,
                bool sendInitialValues,
                CancellationToken ct)
            {
                return m_client.TransferSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    sendInitialValues,
                    ct);
            }

            [Obsolete]
            public ResponseHeader TranslateBrowsePathsToNodeIds(
                RequestHeader requestHeader,
                BrowsePathCollection browsePaths,
                out BrowsePathResultCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.TranslateBrowsePathsToNodeIds(
                    requestHeader,
                    browsePaths,
                    out results,
                    out diagnosticInfos);
            }

            public Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
                RequestHeader requestHeader,
                BrowsePathCollection browsePaths,
                CancellationToken ct)
            {
                return m_client.TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct);
            }

            [Obsolete]
            public ResponseHeader UnregisterNodes(
                RequestHeader requestHeader,
                NodeIdCollection nodesToUnregister)
            {
                return m_client.UnregisterNodes(requestHeader, nodesToUnregister);
            }

            public Task<UnregisterNodesResponse> UnregisterNodesAsync(
                RequestHeader requestHeader,
                NodeIdCollection nodesToUnregister,
                CancellationToken ct)
            {
                return m_client.UnregisterNodesAsync(requestHeader, nodesToUnregister, ct);
            }

            public Task UpdateSessionAsync(
                IUserIdentity identity,
                StringCollection preferredLocales,
                CancellationToken ct = default)
            {
                return m_session.UpdateSessionAsync(identity, preferredLocales, ct);
            }

            [Obsolete]
            public ResponseHeader Write(
                RequestHeader requestHeader,
                WriteValueCollection nodesToWrite,
                out StatusCodeCollection results,
                out DiagnosticInfoCollection diagnosticInfos)
            {
                return m_client.Write(
                    requestHeader,
                    nodesToWrite,
                    out results,
                    out diagnosticInfos);
            }

            public Task<WriteResponse> WriteAsync(
                RequestHeader requestHeader,
                WriteValueCollection nodesToWrite,
                CancellationToken ct)
            {
                return m_client.WriteAsync(requestHeader, nodesToWrite, ct);
            }
        }
    }
}
