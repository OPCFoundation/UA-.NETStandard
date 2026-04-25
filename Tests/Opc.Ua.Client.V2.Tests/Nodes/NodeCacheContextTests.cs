// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Opc.Ua.Client.Sessions;
using Opc.Ua.Client.Subscriptions;
using NUnit.Framework;

#nullable enable

namespace Opc.Ua.Client.Nodes
{
    [TestFixture]
    public sealed class NodeCacheContextTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockChannel = new Mock<ITransportChannel>();
            m_mockObservability = new Mock<ITelemetryContext>();
            m_mockMeterFactory = new Mock<IMeterFactory>();
            m_mockObservability.Setup(o => o.MeterFactory)
                .Returns(m_mockMeterFactory.Object);
            m_mockMeterFactory.Setup(o => o.Create(It.IsAny<MeterOptions>()))
                .Returns(new Meter("TestMeter"));
            m_mockLogger = new Mock<ILogger<SessionBase>>();
            m_mockObservability.Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(m_mockLogger.Object);
            m_mockTimeProvider = new Mock<TimeProvider>();
            m_mockTimer = new Mock<ITimer>();
            m_mockTimeProvider
                .Setup(t => t.CreateTimer(
                    It.IsAny<TimerCallback>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan>()))
                .Returns(m_mockTimer.Object);
            m_mockObservability
                .Setup(o => o.TimeProvider).Returns(m_mockTimeProvider.Object);
            m_options = new SessionCreateOptions
            {
                SessionName = "TestSession",
                Channel = m_mockChannel.Object
            };
            m_configuration = new ApplicationConfiguration
            {
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 10000
                }
            };
        }

        [Test]
        public async Task FetchValuesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            ArrayOf<DataValue> dataValues =
            [
                new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            ];

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValuesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results, Is.EqualTo(dataValues));
            Assert.That(result.Errors, Has.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public async Task FetchValueAsyncShouldReturnDataValueAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [dataValue],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValueAsync(null, nodeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.EqualTo(dataValue));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchValuesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId>();

            // Act
            var result = await sut.FetchValuesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results, Is.Empty);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public async Task FetchValueAsyncShouldThrowServiceResultExceptionForBadStatusCodeAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Bad, DateTime.UtcNow);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [dataValue],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchValueAsync(null, nodeId, CancellationToken.None);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchValuesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId> { NodeId.Parse("ns=2;s=TestNode1"), NodeId.Parse("ns=2;s=TestNode2") };
            ArrayOf<DataValue> dataValues =
            [
                new DataValue(new Variant(123), StatusCodes.Bad, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            ];

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValuesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results, Is.EqualTo(dataValues));
            Assert.That(result.Errors[0].StatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(result.Errors[1].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task FetchValueAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchValueAsync(null, nodeId, cts.Token);

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () => await act());
        }

        [Test]
        public async Task FetchValuesAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchValuesAsync(null, nodeIds, cts.Token);

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () => await act());
        }

        [Test]
        public async Task FetchValueAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var dataValue = new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow);
            var diagnosticInfo = new DiagnosticInfo();
            ArrayOf<DiagnosticInfo> diagnosticInfos = [diagnosticInfo];

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [dataValue],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValueAsync(null, nodeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.EqualTo(dataValue));
            Assert.That(diagnosticInfos, Does.Contain(diagnosticInfo));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchValuesAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            ArrayOf<DataValue> dataValues =
            [
                new DataValue(new Variant(123), StatusCodes.Good, DateTime.UtcNow),
                new DataValue(new Variant(456), StatusCodes.Good, DateTime.UtcNow)
            ];
            var diagnosticInfo = new DiagnosticInfo();
            ArrayOf<DiagnosticInfo> diagnosticInfos = [diagnosticInfo, diagnosticInfo];

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.FetchValuesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results, Is.EqualTo(dataValues));
            Assert.That(result.Errors, Has.All.EqualTo(ServiceResult.Good));
            Assert.That(diagnosticInfos, Does.Contain(diagnosticInfo));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
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
            };

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = request.NodesToRead
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
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(nodes[0], result.Results[0]), Is.True);
            Assert.That(Utils.IsEqual(nodes[1], result.Results[1]), Is.True);
            Assert.That(result.Errors, Has.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnResultSetWhenOptionalAttributesMissingAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
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
            };

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = request.NodesToRead
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
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(nodes[0], result.Results[0]), Is.False);
            Assert.That(Utils.IsEqual(nodes[1], result.Results[1]), Is.False);
            Assert.That(result.Errors, Has.All.EqualTo(ServiceResult.Good));
        }

        [Test]
        public async Task FetchNodeAsyncShouldReturnNodeAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
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

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = request.NodesToRead
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
            var result = await sut.FetchNodeAsync(null, nodeId, CancellationToken.None);

            // Assert
            Assert.That(Utils.IsEqual(node, result), Is.True);

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId>();

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results, Is.Empty);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public async Task FetchNodeAsyncShouldThrowServiceResultExceptionForBadStatusCodeAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
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

            m_mockChannel
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

            // Act
            Func<Task> act = async () => await sut.FetchNodeAsync(null, nodeId,
                CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadAlreadyExists));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
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
            };

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = request.NodesToRead
                        .ConvertAll(r =>
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
                        DiagnosticInfos = results.ConvertAll(r => new DiagnosticInfo())
                    });
                })
                .Verifiable(Times.Exactly(2));

            // Act
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

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
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
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
            };

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = request.NodesToRead
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
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(nodes[0], result.Results[0]), Is.False);
            Assert.That(Utils.IsEqual(nodes[1], result.Results[1]), Is.False);
            Assert.That(result.Errors.Count, Is.EqualTo(2));
            Assert.That(result.Errors[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(result.Errors[1].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public async Task FetchNodeAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNodeAsync(null, nodeId, cts.Token);

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () => await act());
        }

        [Test]
        public async Task FetchNodesAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNodesAsync(null, nodeIds, cts.Token);

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () => await act());
        }

        [Test]
        public async Task FetchNodeAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
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

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = request.NodesToRead
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
            var result = await sut.FetchNodeAsync(null, nodeId, CancellationToken.None);

            // Assert
            Assert.That(Utils.IsEqual(node, result), Is.True);

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNodesAsyncShouldProcessDiagnosticInfoAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
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
            };

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ReadRequest, CancellationToken>((request, ct) =>
                {
                    var results = request.NodesToRead
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
            var result = await sut.FetchNodesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(nodes[0], result.Results[0]), Is.True);
            Assert.That(Utils.IsEqual(nodes[1], result.Results[1]), Is.True);
            Assert.That(result.Errors.Count, Is.EqualTo(2));
            Assert.That(result.Errors, Has.All.EqualTo(ServiceResult.Good));

            m_mockChannel.Verify();
        }
        [Test]
        public async Task FetchReferencesAsyncShouldReturnResultSetAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
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

            m_mockChannel
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
            var result = await sut.FetchReferencesAsync(null, nodeId, CancellationToken.None);

            // Assert
            Assert.That(result, Is.EqualTo(references));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchReferencesAsyncShouldReturnEmptyResultSetForEmptyNodeIdsAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId>();

            // Act
            var result = await sut.FetchReferencesAsync(null, nodeIds, CancellationToken.None);

            // Assert
            Assert.That(result.Results, Is.Empty);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public async Task FetchReferencesAsyncShouldReturnErrorsForBadStatusCodesAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
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

            m_mockChannel
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
            var result = await sut.FetchReferencesAsync(null, nodeIds,
                CancellationToken.None);

            // Assert
            Assert.That(result.Results.Count, Is.EqualTo(2));
            Assert.That(Utils.IsEqual(result.Results[0], references), Is.True);
            Assert.That(Utils.IsEqual(result.Results[1], references), Is.True);
            Assert.That(result.Errors.Count, Is.EqualTo(2));
            Assert.That(result.Errors[0].StatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(result.Errors[1].StatusCode, Is.EqualTo(StatusCodes.Bad));
        }

        [Test]
        public async Task FetchReferencesAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeIds = new List<NodeId>
            {
                NodeId.Parse("ns=2;s=TestNode1"),
                NodeId.Parse("ns=2;s=TestNode2")
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchReferencesAsync(null,
                nodeIds, cts.Token);

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () => await act());
        }

        [Test]
        public async Task FetchReferenceAsyncShouldReturnReferenceDescriptionAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var reference = new ReferenceDescription
            {
                NodeId = ExpandedNodeId.Parse("ns=2;s=TestNode1"),
                BrowseName = QualifiedName.From("TestBrowseName1"),
                DisplayName = LocalizedText.From("TestDisplayName1"),
                NodeClass = NodeClass.Variable
            };

            m_mockChannel
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
            var result = await sut.FetchReferencesAsync(null, nodeId, CancellationToken.None);

            // Assert
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(reference));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchReferenceAsyncShouldThrowServiceResultExceptionForBadStatusCodeAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var reference = new ReferenceDescription
            {
                NodeId = ExpandedNodeId.Parse("ns=2;s=TestNode1"),
                BrowseName = QualifiedName.From("TestBrowseName1"),
                DisplayName = LocalizedText.From("TestDisplayName1"),
                NodeClass = NodeClass.Variable
            };

            m_mockChannel
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
            Func<Task> act = async () => await sut.FetchReferencesAsync(null, nodeId,
                CancellationToken.None);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchReferenceAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            var sut = new TestCacheContext(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var nodeId = NodeId.Parse("ns=2;s=TestNode");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<BrowseRequest>(),
                    It.Is<CancellationToken>(ct => ct.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchReferencesAsync(null, nodeId, cts.Token);

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () => await act());
        }

        private sealed class TestCacheContext : SessionBase
        {
            public TestCacheContext(ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint, SessionCreateOptions m_options,
                ITelemetryContext telemetry, ReverseConnectManager? reverseConnect)
                : base(configuration, endpoint, m_options, telemetry, reverseConnect)
            {
                if (m_options.Channel != null)
                {
                    AttachChannel(m_options.Channel);
                }
            }

            protected override IManagedSubscription CreateSubscription(ISubscriptionNotificationHandler handler,
                IOptionsMonitor<SubscriptionOptions> m_options, IMessageAckQueue queue,
                ITelemetryContext telemetry)
            {
                throw new NotImplementedException();
            }
        }

        private Mock<ITransportChannel> m_mockChannel;
        private Mock<ITelemetryContext> m_mockObservability;
        private Mock<ILogger<SessionBase>> m_mockLogger;
        private Mock<IMeterFactory> m_mockMeterFactory;
        private Mock<TimeProvider> m_mockTimeProvider;
        private Mock<ITimer> m_mockTimer;
        private SessionCreateOptions m_options;
        private ApplicationConfiguration m_configuration;
    }
}
