// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Moq;
    using Opc.Ua.Client.Sessions;
    using Opc.Ua.Client.Subscriptions;
    using Xunit;

    public sealed class NodeCacheContextTests
    {
        public NodeCacheContextTests()
        {
            _mockChannel = new Mock<ITransportChannel>();
            _mockObservability = new Mock<ITelemetryContext>();
            _mockMeterFactory = new Mock<IMeterFactory>();
            _mockObservability.Setup(o => o.MeterFactory)
                .Returns(_mockMeterFactory.Object);
            _mockMeterFactory.Setup(o => o.Create(It.IsAny<MeterOptions>()))
                .Returns(new Meter("TestMeter"));
            _mockLogger = new Mock<ILogger<SessionBase>>();
            _mockObservability.Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(_mockLogger.Object);
            _mockTimeProvider = new Mock<TimeProvider>();
            _mockTimer = new Mock<ITimer>();
            _mockTimeProvider
                .Setup(t => t.CreateTimer(
                    It.IsAny<TimerCallback>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan>()))
                .Returns(_mockTimer.Object);
            _mockObservability
                .Setup(o => o.TimeProvider).Returns(_mockTimeProvider.Object);
            _options = new SessionCreateOptions
            {
                SessionName = "TestSession",
                Channel = _mockChannel.Object
            };
            _configuration = new ApplicationConfiguration
            {
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 10000
                }
            };
        }

        [Fact]
        public async Task FetchValuesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
            var result = await sut.FetchValuesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Should().BeEquivalentTo(dataValues);
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);
        }

        [Fact]
        public async Task FetchValueAsyncShouldReturnDataValueAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);
            var diagnosticInfos = new DiagnosticInfoCollection();

            _mockChannel
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
            var result = await sut.FetchValueAsync(null, nodeId, CancellationToken.None);

            // Assert
            result.Should().BeEquivalentTo(dataValue);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchValuesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeIds = new List<NodeId>();

            // Act
            var result = await sut.FetchValuesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Should().BeEmpty();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task FetchValueAsyncShouldThrowServiceResultExceptionForBadStatusCodeAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Bad, DateTime.UtcNow);
            var diagnosticInfos = new DiagnosticInfoCollection();

            _mockChannel
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
            Func<Task> act = async () => await sut.FetchValueAsync(null, nodeId, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchValuesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeIds = new List<NodeId> { NodeId.Parse("ns=2;s=TestNode1"), NodeId.Parse("ns=2;s=TestNode2") };
            var dataValues = new DataValueCollection
            {
                new DataValue(new Variant(123), StatusCodes.Bad, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            };
            var diagnosticInfos = new DiagnosticInfoCollection();

            _mockChannel
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
            var result = await sut.FetchValuesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Should().BeEquivalentTo(dataValues);
            result.Errors[0].StatusCode.Should().Be(StatusCodes.Bad);
            result.Errors[1].StatusCode.Should().Be(StatusCodes.Good);
        }

        [Fact]
        public async Task FetchValueAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchValueAsync(null, nodeId, cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task FetchValuesAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchValuesAsync(null, nodeIds, cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task FetchValueAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);
            var diagnosticInfo = new DiagnosticInfo();
            var diagnosticInfos = new DiagnosticInfoCollection { diagnosticInfo };

            _mockChannel
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
            var result = await sut.FetchValueAsync(null, nodeId, CancellationToken.None);

            // Assert
            result.Should().BeEquivalentTo(dataValue);
            diagnosticInfos.Should().Contain(diagnosticInfo);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchValuesAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
            var result = await sut.FetchValuesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Should().BeEquivalentTo(dataValues);
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);
            diagnosticInfos.Should().Contain(diagnosticInfo);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNodesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
                    return Task.FromResult<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = []
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeTrue();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeTrue();
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);
        }

        [Fact]
        public async Task FetchNodesAsyncShouldReturnResultSetWhenOptionalAttributesMissingAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
                    return Task.FromResult<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = []
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeFalse();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeFalse();
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);
        }

        [Fact]
        public async Task FetchNodeAsyncShouldReturnNodeAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
                    return Task.FromResult<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = []
                    });
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchNodeAsync(null, nodeId, CancellationToken.None);

            // Assert
            Utils.IsEqual(node, result).Should().BeTrue();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNodesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeIds = new List<NodeId>();

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Should().BeEmpty();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task FetchNodeAsyncShouldThrowServiceResultExceptionForBadStatusCodeAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                    Task.FromResult<IServiceResponse>(new ReadResponse
                    {
                        Results = new DataValueCollection(request.NodesToRead
                                .Select(r => new DataValue(StatusCodes.BadAlreadyExists))),
                        DiagnosticInfos = new DiagnosticInfoCollection(
                                request.NodesToRead.Select(_ => new DiagnosticInfo()))
                    }))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNodeAsync(null, nodeId,
                CancellationToken.None);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadAlreadyExists);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNodesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
                    return Task.FromResult<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = new DiagnosticInfoCollection(
                            results.Select(r => new DiagnosticInfo()))
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeTrue();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeFalse();
            result.Errors.Count.Should().Be(2);
            result.Errors[0].Should().Be(ServiceResult.Good);
            result.Errors[1].StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
        }

        [Fact]
        public async Task FetchNodesAsyncShouldReturnErrorsForBadNodeClassTypeAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
                    return Task.FromResult<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = new DiagnosticInfoCollection(
                            results.Select(r => new DiagnosticInfo()))
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeFalse();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeFalse();
            result.Errors.Count.Should().Be(2);
            result.Errors[0].StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
            result.Errors[1].StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
        }

        [Fact]
        public async Task FetchNodeAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNodeAsync(null, nodeId, cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task FetchNodesAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNodesAsync(null, nodeIds, cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task FetchNodeAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
                    return Task.FromResult<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = new DiagnosticInfoCollection(
                            results.Select(_ => new DiagnosticInfo()))
                    });
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchNodeAsync(null, nodeId, CancellationToken.None);

            // Assert
            Utils.IsEqual(node, result).Should().BeTrue();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNodesAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
                    return Task.FromResult<IServiceResponse>(new ReadResponse
                    {
                        Results = results,
                        DiagnosticInfos = new DiagnosticInfoCollection(
                            results.Select(r => new DiagnosticInfo()))
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(nodes[0], result.Results[0]).Should().BeTrue();
            Utils.IsEqual(nodes[1], result.Results[1]).Should().BeTrue();
            result.Errors.Count.Should().Be(2);
            result.Errors.Should().AllBeEquivalentTo(ServiceResult.Good);

            _mockChannel.Verify();
        }
        [Fact]
        public async Task FetchReferencesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
            var result = await sut.FetchReferencesAsync(null, nodeId, CancellationToken.None);

            // Assert
            result.Should().BeEquivalentTo(references);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchReferencesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeIds = new List<NodeId>();

            // Act
            var result = await sut.FetchReferencesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            result.Results.Should().BeEmpty();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task FetchReferencesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
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

            _mockChannel
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
                CancellationToken.None);

            // Assert
            result.Results.Count.Should().Be(2);
            Utils.IsEqual(result.Results[0], references).Should().BeTrue();
            Utils.IsEqual(result.Results[1], references).Should().BeTrue();
            result.Errors.Count.Should().Be(2);
            result.Errors[0].StatusCode.Should().Be(StatusCodes.Bad);
            result.Errors[1].StatusCode.Should().Be(StatusCodes.Bad);
        }

        [Fact]
        public async Task FetchReferencesAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchReferencesAsync(null,
                nodeIds, cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task FetchReferenceAsyncShouldReturnReferenceDescriptionAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var reference = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId("ns=2;s=TestNode1"),
                BrowseName = "TestBrowseName1",
                DisplayName = "TestDisplayName1",
                NodeClass = NodeClass.Variable
            };

            _mockChannel
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
            var result = await sut.FetchReferencesAsync(null, nodeId, CancellationToken.None);

            // Assert
            result.Count.Should().Be(1);
            result[0].Should().BeEquivalentTo(reference);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchReferenceAsyncShouldThrowServiceResultExceptionForBadStatusCodeAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var reference = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId("ns=2;s=TestNode1"),
                BrowseName = "TestBrowseName1",
                DisplayName = "TestDisplayName1",
                NodeClass = NodeClass.Variable
            };

            _mockChannel
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
                CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchReferenceAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchReferencesAsync(null, nodeId, cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        private sealed class TestCacheContext : SessionBase
        {
            public TestCacheContext(ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint, SessionCreateOptions _options,
                ITelemetryContext telemetry, ReverseConnectManager? reverseConnect)
                : base(configuration, endpoint, _options, telemetry, reverseConnect)
            {
                if (_options.Channel != null)
                {
                    AttachChannel(_options.Channel);
                }
            }

            protected override IManagedSubscription CreateSubscription(ISubscriptionNotificationHandler handler,
                IOptionsMonitor<SubscriptionOptions> _options, IMessageAckQueue queue,
                ITelemetryContext telemetry)
            {
                throw new NotImplementedException();
            }
        }

        private readonly Mock<ITransportChannel> _mockChannel;
        private readonly Mock<ITelemetryContext> _mockObservability;
        private readonly Mock<ILogger<SessionBase>> _mockLogger;
        private readonly Mock<IMeterFactory> _mockMeterFactory;
        private readonly Mock<TimeProvider> _mockTimeProvider;
        private readonly Mock<ITimer> _mockTimer;
        private readonly SessionCreateOptions _options;
        private readonly ApplicationConfiguration _configuration;
    }
}
