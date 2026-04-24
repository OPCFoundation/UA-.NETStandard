// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
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
    using Opc.Ua.Client.Services;
    using Opc.Ua.Client.Subscriptions;
    using Xunit;

    public sealed class SessionBaseTests
    {
        public SessionBaseTests()
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
        public async Task FetchOperationLimitsAsyncShouldFetchAllOperationLimitsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var dataValues = new DataValueCollection
            {
                new DataValue(new Variant(2000u)),
                new DataValue(new Variant(3000u)),
                new DataValue(new Variant(4000u)),
                new DataValue(new Variant(1000u)),
                new DataValue(new Variant(5000u)),
                new DataValue(new Variant(6000u)),
                new DataValue(new Variant(7000u)),
                new DataValue(new Variant(8000u)),
                new DataValue(new Variant(9000u)),
                new DataValue(new Variant(10000u)),
                new DataValue(new Variant(11000u)),
                new DataValue(new Variant(12000u)),
                new DataValue(new Variant((ushort)13000)),
                new DataValue(new Variant((ushort)14000u)),
                new DataValue(new Variant((ushort)15000u)),
                new DataValue(new Variant(16000u)),
                new DataValue(new Variant(17000u)),
                new DataValue(new Variant(18000u)),
                new DataValue(new Variant((double)19000.0)),
                new DataValue(new Variant(20000u)),
                new DataValue(new Variant(21000u)),
                new DataValue(new Variant(22000u)),
                new DataValue(new Variant(23000u)),
                new DataValue(new Variant(24000u)),
                new DataValue(new Variant(25000u)),
                new DataValue(new Variant(26000u)),
                new DataValue(new Variant(27000u))
            };

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [new DataValue(new Variant(1000u))],
                    DiagnosticInfos = []
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = []
                });

            // Act
            await sut.FetchOperationLimitsAsync(ct);

            // Assert
            sut.OperationLimits.MaxNodesPerRead.Should().Be(1000);
            sut.OperationLimits.MaxNodesPerHistoryReadData.Should().Be(2000);
            sut.OperationLimits.MaxNodesPerHistoryReadEvents.Should().Be(3000);
            sut.OperationLimits.MaxNodesPerWrite.Should().Be(4000);
            sut.OperationLimits.MaxNodesPerHistoryUpdateData.Should().Be(5000);
            sut.OperationLimits.MaxNodesPerHistoryUpdateEvents.Should().Be(6000);
            sut.OperationLimits.MaxNodesPerMethodCall.Should().Be(7000);
            sut.OperationLimits.MaxNodesPerBrowse.Should().Be(8000);
            sut.OperationLimits.MaxNodesPerRegisterNodes.Should().Be(9000);
            sut.OperationLimits.MaxNodesPerNodeManagement.Should().Be(10000);
            sut.OperationLimits.MaxMonitoredItemsPerCall.Should().Be(11000);
            sut.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds.Should().Be(12000);
            sut.OperationLimits.MaxBrowseContinuationPoints.Should().Be(13000);
            sut.OperationLimits.MaxHistoryContinuationPoints.Should().Be(14000);
            sut.OperationLimits.MaxQueryContinuationPoints.Should().Be(15000);
            sut.OperationLimits.MaxStringLength.Should().Be(16000);
            sut.OperationLimits.MaxArrayLength.Should().Be(17000);
            sut.OperationLimits.MaxByteStringLength.Should().Be(18000);
            sut.OperationLimits.MinSupportedSampleRate.Should().Be(19000.0);
            sut.OperationLimits.MaxSessions.Should().Be(20000);
            sut.OperationLimits.MaxSubscriptions.Should().Be(21000);
            sut.OperationLimits.MaxMonitoredItems.Should().Be(22000);
            sut.OperationLimits.MaxMonitoredItemsPerSubscription.Should().Be(23000);
            sut.OperationLimits.MaxMonitoredItemsQueueSize.Should().Be(24000);
            sut.OperationLimits.MaxSubscriptionsPerSession.Should().Be(25000);
            sut.OperationLimits.MaxWhereClauseParameters.Should().Be(26000);
            sut.OperationLimits.MaxSelectClauseParameters.Should().Be(27000);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchOperationLimitsAsyncShouldHandleEmptyResponseAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var dataValues = new DataValueCollection();
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
            Func<Task> act = async () => await sut.FetchOperationLimitsAsync(ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchOperationLimitsAsyncShouldHandlePartialDataAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var dataValues = new DataValueCollection
            {
                new DataValue(new Variant(1000u)),
                new DataValue(new Variant(2000u)),
                new DataValue(new Variant(3000u))
            };

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [new DataValue(new Variant(1000u))],
                    DiagnosticInfos = []
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = []
                });

            // Act
            Func<Task> act = async () => await sut.FetchOperationLimitsAsync(ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchOperationLimitsAsyncShouldHandleErrorsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var dataValues = new DataValueCollection
            {
                new DataValue(StatusCodes.BadUnexpectedError)
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
                .Verifiable(Times.Exactly(2));

            // Act
            Func<Task> act = async () => await sut.FetchOperationLimitsAsync(ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchOperationLimitsAsyncShouldThrowWhenInvalidDataTypesAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var dataValues = new DataValueCollection
            {
                new DataValue("InvalidDataType")
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
                .Verifiable(Times.Exactly(2));

            // Act
            Func<Task> act = async () => await sut.FetchOperationLimitsAsync(ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchOperationLimitsAsyncShouldHandleTimeoutAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = new CancellationTokenSource(100).Token; // Set a short timeout

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchOperationLimitsAsync(ct);

            // Assert
            await act.Should().ThrowAsync<TaskCanceledException>();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNamespaceTablesAsyncShouldFetchAndUpdateTablesAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(new Variant(["http://server1", "http://server2"]));

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            sut.NamespaceUris.ToArray().Should().BeEquivalentTo([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]);
            sut.ServerUris.ToArray().Should().BeEquivalentTo(["http://server1", "http://server2"]);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNamespaceTablesAsyncShouldFetchAndUpdateTablesAndLogDifferences1Async()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray1 = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            var serverArray1 = new DataValue(new Variant(["http://server1", "http://server2"]));
            var namespaceArray2 = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace3", "http://namespace2"]));
            var serverArray2 = new DataValue(new Variant(["http://server1", "http://server2", "http://server3"]));

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [namespaceArray1, serverArray1],
                    DiagnosticInfos = []
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = [namespaceArray2, serverArray2],
                    DiagnosticInfos = []
                });

            // Act
            await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            sut.NamespaceUris.ToArray().Should().BeEquivalentTo([Opc.Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]);
            sut.ServerUris.ToArray().Should().BeEquivalentTo(["http://server1", "http://server2"]);

            // Act
            await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            sut.NamespaceUris.ToArray().Should().BeEquivalentTo([Opc.Ua.Namespaces.OpcUa, "http://namespace3", "http://namespace2"]);
            sut.ServerUris.ToArray().Should().BeEquivalentTo(["http://server1", "http://server2", "http://server3"]);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNamespaceTablesAsyncShouldFetchAndUpdateTablesAndLogDifferences2Async()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray1 = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            var serverArray1 = new DataValue(new Variant(["http://server1", "http://server2"]));
            var namespaceArray2 = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace3"]));
            var serverArray2 = new DataValue(new Variant(["http://server1"]));

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [namespaceArray1, serverArray1],
                    DiagnosticInfos = []
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = [namespaceArray2, serverArray2],
                    DiagnosticInfos = []
                });

            // Act
            await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            sut.NamespaceUris.ToArray().Should().BeEquivalentTo([Opc.Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]);
            sut.ServerUris.ToArray().Should().BeEquivalentTo(["http://server1", "http://server2"]);

            // Act
            await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            sut.NamespaceUris.ToArray().Should().BeEquivalentTo([Opc.Ua.Namespaces.OpcUa, "http://namespace3"]);
            sut.ServerUris.ToArray().Should().BeEquivalentTo(["http://server1"]);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNamespaceTablesAsyncShouldHandlePartialSuccessAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(StatusCodes.BadUnexpectedError);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            sut.NamespaceUris.ToArray().Should().BeEquivalentTo([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]);
            sut.ServerUris.ToArray().Should().BeEmpty();

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNamespaceTablesAsyncShouldThrowInCaseOfBadResponseAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(new Variant(["http://server1", "http://server2"]));

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadUnexpectedError },
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadUnexpectedError);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNamespaceTablesAsyncShouldThrowWhenNamespaceArrayCouldNotBeRetrievedAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(StatusCodes.BadUnexpectedError);
            var serverArray = new DataValue(StatusCodes.BadUnexpectedError);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadUnexpectedError);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNamespaceTablesAsyncShoulThrowWhenEmptyResponseAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var dataValues = new DataValueCollection();
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
            Func<Task> act = async () => await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadUnexpectedError);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNamespaceTablesAsyncShouldThrowWhenInvalidDataTypesForNamespaceTableAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant(12345));
            var serverArray = new DataValue(new Variant(67890));

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadTypeMismatch);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task FetchNamespaceTablesAsyncShouldThrowWhenInvalidDataTypeForServerUrlsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(new Variant(67890));

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadTypeMismatch);

            _mockChannel.Verify();
        }

        [Fact]
        public async Task PingServerAsyncShouldReturnTrueOnSuccessfulPingAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var serverState = new DataValue(new Variant(ServerState.Running));

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [serverState],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            result.Should().BeTrue();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task PingServerAsyncShouldReturnFalseInCaseOfInvalidServerStateAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var serverState = new DataValue(new Variant("InvalidState"));

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [serverState],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            result.Should().BeFalse();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task PingServerAsyncShouldThrowWhenCancellationTokenCancelledAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    cts.Token))
                .ThrowsAsync(new TaskCanceledException()) // no matter what we throw
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.PingServerAsync(cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task PingServerAsyncShouldNotThrowWhenCancellationTokenNotCancelledAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    ct))
                .ThrowsAsync(new TaskCanceledException()) // no matter what we throw
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            result.Should().BeFalse();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task PingServerAsyncShouldHandleNoCommunicationButInGuardBandAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNoCommunication))
                .Verifiable(Times.Once);
            _mockTimeProvider
                .Setup(t => t.TimestampFrequency)
                .Returns(1000);
            _mockTimeProvider
                .Setup(t => t.GetTimestamp())
                .Returns(-10000000L);

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            result.Should().BeTrue();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task PingServerAsyncShouldHandleNoCommunicationOutsideGuardBandAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNoCommunication))
                .Verifiable(Times.Once);
            _mockTimeProvider
                .Setup(t => t.TimestampFrequency)
                .Returns(1000);
            _mockTimeProvider
                .Setup(t => t.GetTimestamp())
                .Returns(10000000L);

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            result.Should().BeFalse();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task PingServerAsyncShouldHandleOtherErrorsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            result.Should().BeFalse();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task CloseAsyncShouldCloseSessionAndChannelSuccessfullyAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(true, true, ct);

            // Assert
            result.Should().Be(ServiceResult.Good);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task CloseAsyncShouldHandleAlreadyClosedSessionAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            sut.Dispose();

            // Act
            var result = await sut.CloseAsync(true, true, ct);

            // Assert
            result.Should().Be(ServiceResult.Good);
        }

        [Fact]
        public async Task CloseAsyncShouldHandleErrorsDuringCloseAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(true, true, ct);

            // Assert
            result.StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task CloseAsyncShouldCloseSessionWithoutDeletingSubscriptionsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(true, false, ct);

            // Assert
            result.Should().Be(ServiceResult.Good);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task CloseAsyncShouldHandleChannelErrorsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(true, true, ct);

            // Assert
            result.Should().Be(ServiceResult.Good);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task CloseAsyncShouldCloseSessionSuccessfullyAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(ct);

            // Assert
            result.Should().Be(StatusCodes.Good);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task CloseAsyncShouldHandleErrorsDuringCloseSessionAsync()
        {
            // Arrange
            var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(ct);

            // Assert
            result.Should().Be(StatusCodes.BadUnexpectedError);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task ReconnectAsyncShouldReconnectSuccessfullyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            var serverNonce = new byte[] { 1, 2, 3, 4 };

            _mockChannel
                .Setup(c => c.Reconnect(It.IsAny<ITransportWaitingConnection>()))
                .Verifiable(Times.Once);
            _mockChannel
            .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ServerNonce = serverNonce,
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.ReconnectAsync(ct);

            // Assert
            sut._serverNonce.Should().BeEquivalentTo(serverNonce);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task ReconnectAsyncShouldHandleReconnectFailureAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.Reconnect(It.IsAny<ITransportWaitingConnection>()))
                .Throws(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>()
                .WithMessage("*BadUnexpectedError*");
            _mockChannel.Verify();
        }

        [Fact]
        public async Task ReconnectAsyncShouldHandleNoSupportedFeaturesAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://notreallytherehostorelsetestfails:1234",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.None);

            // TODO: Should properly mock channel creation

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task ReconnectAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);
            sut.SetConnected();

            _mockChannel
                .Setup(c => c.Reconnect(It.IsAny<ITransportWaitingConnection>()))
                .Throws(new TaskCanceledException())
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(default);

            // Assert
            await act.Should().ThrowAsync<TaskCanceledException>();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task ReconnectAsyncShouldHandleServerResponseWithNullNonceAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.Reconnect(It.IsAny<ITransportWaitingConnection>()))
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ServerNonce = null,
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.ReconnectAsync(ct);

            // Assert
            sut._serverNonce.Should().NotBeNull().And.BeEmpty();
            _mockChannel.Verify();
        }
        [Fact]
        public async Task ReconnectAsyncShouldThrowWithIncompatibleIdentityAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "T",
                            TokenType = UserTokenType.Certificate
                        }
                    ]
                }),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(ct);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadUserAccessDenied);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task ReconnectAsyncShouldThrowWithBadActivationResponseAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.Reconnect(It.IsAny<ITransportWaitingConnection>()))
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionNotActivated },
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(ct);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadSessionNotActivated);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task ReconnectAsyncShouldThrowWhenTimingOutAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.Reconnect(It.IsAny<ITransportWaitingConnection>()))
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(ct);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadTimeout);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task OpenAsyncShouldOpenSessionSuccessfullyAsync()
        {
            // Arrange
            var ep = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens =
                [
                    new UserTokenPolicy()
                ]
            };
            _options = _options with { EnableComplexTypePreloading = false };
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, ep),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;
            var serverNonce = new byte[] { 1, 2, 3, 4 };
            var authToken = NodeId.Parse("s=cookie");

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = serverNonce,
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ServerNonce = serverNonce,
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Read limit
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 1),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new (new Variant(0u))
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Operation limits
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 27),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(Enumerable
                        .Range(0, 27)
                        .Select(_ => new DataValue(Variant.Null))),
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Namespaces
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 2),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new (new[] { Opc.Ua.Namespaces.OpcUa }),
                        new(Array.Empty<string>())
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.OpenAsync(ct);

            // Assert
            sut._serverNonce.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
            _mockChannel.Verify();
        }

        [Fact]
        public async Task OpenAsyncShouldHandleCreateSessionSuccessButActivationErrorAsync()
        {
            // Arrange
            var ep = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens =
                [
                    new UserTokenPolicy()
                ]
            };
            _options = _options with { EnableComplexTypePreloading = false };
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, ep),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;
            var serverNonce = new byte[] { 1, 2, 3, 4 };
            var authToken = NodeId.Parse("s=cookie");

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = serverNonce,
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionNotActivated },
                    ServerNonce = serverNonce,
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(CancellationToken.None);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadSessionNotActivated);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task OpenAsyncShouldHandleCreateSessionSuccessButActivationErrorAndThenCloseAlsoFailsAsync()
        {
            // Arrange
            var ep = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens =
                [
                    new UserTokenPolicy()
                ]
            };
            _options = _options with { EnableComplexTypePreloading = false };
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, ep),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;
            var serverNonce = new byte[] { 1, 2, 3, 4 };
            var authToken = NodeId.Parse("s=cookie");

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = serverNonce,
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionNotActivated },
                    ServerNonce = serverNonce,
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadNotConnected }
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadNotConnected))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(CancellationToken.None);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadSessionNotActivated);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task OpenAsyncShouldHandleSessionOpeningFailureAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Exactly(2));

            // Act
            Func<Task> act = async () => await sut.OpenAsync(CancellationToken.None);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task OpenAsyncShouldHandleBadSecurityPolicyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = "Bad",
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(default);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadSecurityPolicyRejected);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task OpenAsyncShouldHandleBadIdentityTokenPolicyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "PolicyId",
                            TokenType = UserTokenType.IssuedToken
                        }
                    ]
                }),
                _options, _mockObservability.Object, null);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(default);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadUserAccessDenied);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task OpenAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(default);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task OpenAsyncShouldHandleInvalidServerResponseAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                    [
                new UserTokenPolicy()
                    ]
                }),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = null,
                    SessionId = NodeId.Parse("s=connected")
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Fact]
        public async Task OpenAsyncShouldHandleComplexTypeLoadingDisabledAsync()
        {
            // Arrange
            var ep = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens =
                [
                    new UserTokenPolicy()
                ]
            };
            _options = _options with { EnableComplexTypePreloading = false };
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, ep),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;
            var serverNonce = new byte[] { 1, 2, 3, 4 };
            var authToken = NodeId.Parse("s=cookie");

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = serverNonce,
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                })
                .Verifiable(Times.Once);

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ServerNonce = serverNonce,
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Read limit
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 1),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                new (new Variant(0u))
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Operation limits
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 27),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(Enumerable
                        .Range(0, 27)
                        .Select(_ => new DataValue(Variant.Null))),
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Namespaces
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 2),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                new (new[] { Opc.Ua.Namespaces.OpcUa }),
                new(Array.Empty<string>())
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.OpenAsync(ct);

            // Assert
            sut._serverNonce.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
            _mockChannel.Verify();
        }

        [Fact]
        public async Task BrowseAsyncShouldHandleWhenBrowseNextResultCollectionIsEmptyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                new NodeId("ns=1;s=ChildNode1"),
                new NodeId("ns=1;s=ChildNode2"),
                new NodeId("ns=1;s=ChildNode3")
            };

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    ct))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new ()
                        {
                            References =
                            [
                                new () { NodeId = nodeIds[0] }
                            ],
                            ContinuationPoint = [1, 2, 3, 4]
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    ct))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References = [],
                            ContinuationPoint = [1, 2, 3, 4]
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            var results = new List<BrowseDescriptionResult>();
            await foreach (var result in sut.BrowseAsync(null, null,
                [
                    new ()
                    {
                        NodeId = NodeId.Parse("ns=1;s=TestNode"),
                        BrowseDirection = BrowseDirection.Both,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], ct))
            {
                results.Add(result);
            }

            // Assert
            results.Should().HaveCount(2);
            results[0].Result.References.Should().ContainSingle(r => r.NodeId == nodeIds[0]);
            results[1].Result.References.Should().BeEmpty();
            results[1].Result.StatusCode.Should().Be(StatusCodes.BadNoData);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task BrowseAsyncShouldHandleContinuationPointsSuccessfullyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                new NodeId("ns=1;s=ChildNode1"),
                new NodeId("ns=1;s=ChildNode2"),
                new NodeId("ns=1;s=ChildNode3")
            };

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    ct))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new ()
                        {
                            References =
                            [
                                new () { NodeId = nodeIds[0] }
                            ],
                            ContinuationPoint = [1, 2, 3, 4]
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);
            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    ct))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References =
                            [
                                new () { NodeId = nodeIds[1] }
                            ],
                            ContinuationPoint = [1, 2, 3, 4]
                        }
                    ],
                    DiagnosticInfos = []
                })
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References =
                            [
                                new () { NodeId = nodeIds[2] }
                            ],
                            ContinuationPoint = []
                        }
                    ],
                    DiagnosticInfos = []
                });

            // Act
            var results = new List<BrowseDescriptionResult>();
            await foreach (var result in sut.BrowseAsync(null, null,
                [
                    new ()
                    {
                        NodeId = NodeId.Parse("ns=1;s=TestNode"),
                        BrowseDirection = BrowseDirection.Both,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], ct))
            {
                results.Add(result);
            }

            // Assert
            results.Should().HaveCount(3);
            results[0].Result.References.Should().ContainSingle(r => r.NodeId == nodeIds[0]);
            results[1].Result.References.Should().ContainSingle(r => r.NodeId == nodeIds[1]);
            results[2].Result.References.Should().ContainSingle(r => r.NodeId == nodeIds[2]);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task BrowseAsyncShouldHandleNullContinuationPointsSuccessfullyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                new NodeId("ns=1;s=ChildNode1"),
                new NodeId("ns=1;s=ChildNode2"),
                new NodeId("ns=1;s=ChildNode3")
            };

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    ct))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new ()
                        {
                            References =
                            [
                                new () { NodeId = nodeIds[0] }
                            ],
                            ContinuationPoint = [1, 2, 3, 4]
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    ct))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References =
                            [
                                new () { NodeId = nodeIds[1] },
                                new () { NodeId = nodeIds[2] }
                            ],
                            ContinuationPoint = null
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            var results = new List<BrowseDescriptionResult>();
            await foreach (var result in sut.BrowseAsync(null, null,
                [
                    new ()
                    {
                        NodeId = NodeId.Parse("ns=1;s=TestNode"),
                        BrowseDirection = BrowseDirection.Both,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], ct))
            {
                results.Add(result);
            }

            // Assert
            results.Should().HaveCount(2);
            results[0].Result.References.Should().ContainSingle(r => r.NodeId == nodeIds[0]);
            results[1].Result.References.Count.Should().Be(2);
            results[1].Result.References[0].NodeId.Should().Be(nodeIds[1]);
            results[1].Result.References[1].NodeId.Should().Be(nodeIds[2]);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task BrowseAsyncShouldHandleWhenBrowseResultCollectionIsEmptyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                new NodeId("ns=1;s=ChildNode1"),
                new NodeId("ns=1;s=ChildNode2"),
                new NodeId("ns=1;s=ChildNode3")
            };

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    ct))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new ()
                        {
                            References = [],
                            ContinuationPoint = [1, 2, 3, 4]
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            var results = new List<BrowseDescriptionResult>();
            await foreach (var result in sut.BrowseAsync(null, null,
                [
                    new ()
                    {
                        NodeId = NodeId.Parse("ns=1;s=TestNode"),
                        BrowseDirection = BrowseDirection.Both,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], ct))
            {
                results.Add(result);
            }

            // Assert
            results.Should().HaveCount(1);
            results[0].Result.References.Should().BeEmpty();
            results[0].Result.StatusCode.Should().Be(StatusCodes.BadNoData);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task BrowseAsyncShouldHandleBrowseNextCancelledAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                new NodeId("ns=1;s=ChildNode1"),
                new NodeId("ns=1;s=ChildNode2"),
                new NodeId("ns=1;s=ChildNode3")
            };

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    ct))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new ()
                        {
                            References =
                            [
                                new () { NodeId = nodeIds[0] }
                            ],
                            ContinuationPoint = [1, 2, 3, 4]
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest && !((BrowseNextRequest)r).ReleaseContinuationPoints),
                    ct))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest && ((BrowseNextRequest)r).ReleaseContinuationPoints),
                    ct))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNotConnected))
                .Verifiable(Times.Once);

            // Act
            var results = new List<BrowseDescriptionResult>();
            Func<Task> act = async () =>
            {
                await foreach (var result in sut.BrowseAsync(null, null,
                    [
                        new ()
                        {
                            NodeId = NodeId.Parse("ns=1;s=TestNode"),
                            BrowseDirection = BrowseDirection.Both,
                            ReferenceTypeId = ReferenceTypeIds.References,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], ct))
                {
                    results.Add(result);
                }
            };

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            results.Should().HaveCount(1);
            results[0].Result.References.Should().ContainSingle(r => r.NodeId == nodeIds[0]);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task BrowseAsyncShouldHandleBrowseNextWithBadResponseAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                new NodeId("ns=1;s=ChildNode1"),
                new NodeId("ns=1;s=ChildNode2"),
                new NodeId("ns=1;s=ChildNode3")
            };

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    ct))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new ()
                        {
                            References =
                            [
                                new () { NodeId = nodeIds[0] }
                            ],
                            ContinuationPoint = [1, 2, 3, 4]
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest && !((BrowseNextRequest)r).ReleaseContinuationPoints),
                    ct))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.BadUnexpectedError
                    }
                })
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest && ((BrowseNextRequest)r).ReleaseContinuationPoints),
                    ct))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNotConnected))
                .Verifiable(Times.Once);

            // Act
            var results = new List<BrowseDescriptionResult>();
            Func<Task> act = async () =>
            {
                await foreach (var result in sut.BrowseAsync(null, null,
                    [
                        new ()
                        {
                            NodeId = NodeId.Parse("ns=1;s=TestNode"),
                            BrowseDirection = BrowseDirection.Both,
                            ReferenceTypeId = ReferenceTypeIds.References,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], ct))
                {
                    results.Add(result);
                }
            };

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
            results.Should().HaveCount(1);
            results[0].Result.References.Should().ContainSingle(r => r.NodeId == nodeIds[0]);
            _mockChannel.Verify();
        }

        [Fact]
        public async Task BrowseAsyncShouldHandleContinuationPointsWithErrorsAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                _options, _mockObservability.Object, null);

            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                new NodeId("ns=1;s=ChildNode1"),
                new NodeId("ns=1;s=ChildNode2"),
                new NodeId("ns=1;s=ChildNode3")
            };

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    ct))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new ()
                        {
                            References =
                            [
                                new () { NodeId = nodeIds[0] }
                            ],
                            ContinuationPoint = [1, 2, 3, 4]
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);
            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    ct))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results =
                    [
                        new BrowseResult()
                        {
                            StatusCode = StatusCodes.BadUnexpectedError
                        }
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            var results = new List<BrowseDescriptionResult>();
            await foreach (var result in sut.BrowseAsync(null, null,
                [
                    new ()
                    {
                        NodeId = NodeId.Parse("ns=1;s=TestNode"),
                        BrowseDirection = BrowseDirection.Both,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], ct))
            {
                results.Add(result);
            }
            // Assert
            results.Should().HaveCount(2);
            results[0].Result.References.Should().ContainSingle(r => r.NodeId == new NodeId("ns=1;s=ChildNode1"));
            results[1].Result.References.Should().BeEmpty();
            results[1].Result.StatusCode.Should().Be(StatusCodes.BadUnexpectedError);
            _mockChannel.Verify();
        }

        private sealed class TestSessionBase : SessionBase
        {
            public TestSessionBase(ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint, SessionCreateOptions options,
                ITelemetryContext telemetry, ReverseConnectManager? reverseConnect)
                : base(configuration, endpoint, options, telemetry, reverseConnect)
            {
                if (options.Channel != null)
                {
                    AttachChannel(options.Channel);
                }
            }

            public void SetConnected()
            {
                base.SessionCreated(NodeId.Parse("s=connected"), NodeId.Parse("s=cookie"));
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
        private readonly ApplicationConfiguration _configuration;
        private SessionCreateOptions _options;
    }
}
