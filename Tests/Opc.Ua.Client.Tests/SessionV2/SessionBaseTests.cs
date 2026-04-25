#if OPCUA_CLIENT_V2
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
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Opc.Ua.Client.Services;
using Opc.Ua.Client.Subscriptions;
using NUnit.Framework;

namespace Opc.Ua.Client.Sessions
{
    [TestFixture]
    public sealed class SessionBaseTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockChannel = new Mock<ITransportChannel>();
            m_mockObservability = new Mock<ITelemetryContext>();
            m_mockObservability.Setup(o => o.CreateMeter())
                .Returns(new Meter("TestMeter"));
            m_mockLogger = new Mock<ILogger<SessionBase>>();
            m_mockObservability.Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(m_mockLogger.Object);
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
        public async Task FetchOperationLimitsAsyncShouldFetchAllOperationLimitsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            ArrayOf<DataValue> dataValues =
            [
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
            ];

            m_mockChannel
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
            Assert.That(sut.OperationLimits.MaxNodesPerRead, Is.EqualTo(1000));
            Assert.That(sut.OperationLimits.MaxNodesPerHistoryReadData, Is.EqualTo(2000));
            Assert.That(sut.OperationLimits.MaxNodesPerHistoryReadEvents, Is.EqualTo(3000));
            Assert.That(sut.OperationLimits.MaxNodesPerWrite, Is.EqualTo(4000));
            Assert.That(sut.OperationLimits.MaxNodesPerHistoryUpdateData, Is.EqualTo(5000));
            Assert.That(sut.OperationLimits.MaxNodesPerHistoryUpdateEvents, Is.EqualTo(6000));
            Assert.That(sut.OperationLimits.MaxNodesPerMethodCall, Is.EqualTo(7000));
            Assert.That(sut.OperationLimits.MaxNodesPerBrowse, Is.EqualTo(8000));
            Assert.That(sut.OperationLimits.MaxNodesPerRegisterNodes, Is.EqualTo(9000));
            Assert.That(sut.OperationLimits.MaxNodesPerNodeManagement, Is.EqualTo(10000));
            Assert.That(sut.OperationLimits.MaxMonitoredItemsPerCall, Is.EqualTo(11000));
            Assert.That(sut.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds, Is.EqualTo(12000));
            Assert.That(sut.OperationLimits.MaxBrowseContinuationPoints, Is.EqualTo(13000));
            Assert.That(sut.OperationLimits.MaxHistoryContinuationPoints, Is.EqualTo(14000));
            Assert.That(sut.OperationLimits.MaxQueryContinuationPoints, Is.EqualTo(15000));
            Assert.That(sut.OperationLimits.MaxStringLength, Is.EqualTo(16000));
            Assert.That(sut.OperationLimits.MaxArrayLength, Is.EqualTo(17000));
            Assert.That(sut.OperationLimits.MaxByteStringLength, Is.EqualTo(18000));
            Assert.That(sut.OperationLimits.MinSupportedSampleRate, Is.EqualTo(19000.0));
            Assert.That(sut.OperationLimits.MaxSessions, Is.EqualTo(20000));
            Assert.That(sut.OperationLimits.MaxSubscriptions, Is.EqualTo(21000));
            Assert.That(sut.OperationLimits.MaxMonitoredItems, Is.EqualTo(22000));
            Assert.That(sut.OperationLimits.MaxMonitoredItemsPerSubscription, Is.EqualTo(23000));
            Assert.That(sut.OperationLimits.MaxMonitoredItemsQueueSize, Is.EqualTo(24000));
            Assert.That(sut.OperationLimits.MaxSubscriptionsPerSession, Is.EqualTo(25000));
            Assert.That(sut.OperationLimits.MaxWhereClauseParameters, Is.EqualTo(26000));
            Assert.That(sut.OperationLimits.MaxSelectClauseParameters, Is.EqualTo(27000));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchOperationLimitsAsyncShouldHandleEmptyResponseAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            ArrayOf<DataValue> dataValues = ArrayOf<DataValue>.Empty;
            ArrayOf<DiagnosticInfo> diagnosticInfos = ArrayOf<DiagnosticInfo>.Empty;

            m_mockChannel
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
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchOperationLimitsAsyncShouldHandlePartialDataAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            ArrayOf<DataValue> dataValues =
            [
                new DataValue(new Variant(1000u)),
                new DataValue(new Variant(2000u)),
                new DataValue(new Variant(3000u))
            ];

            m_mockChannel
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
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchOperationLimitsAsyncShouldHandleErrorsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            ArrayOf<DataValue> dataValues =
            [
                new DataValue(StatusCodes.BadUnexpectedError)
            ];

            ArrayOf<DiagnosticInfo> diagnosticInfos = ArrayOf<DiagnosticInfo>.Empty;

            m_mockChannel
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
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchOperationLimitsAsyncShouldThrowWhenInvalidDataTypesAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            ArrayOf<DataValue> dataValues =
            [
                new DataValue("InvalidDataType")
            ];

            ArrayOf<DiagnosticInfo> diagnosticInfos = ArrayOf<DiagnosticInfo>.Empty;

            m_mockChannel
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
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchOperationLimitsAsyncShouldHandleTimeoutAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = new CancellationTokenSource(100).Token; // Set a short timeout

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.FetchOperationLimitsAsync(ct);

            // Assert
            Assert.ThrowsAsync<TaskCanceledException>(async () => await act());

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldFetchAndUpdateTablesAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(new Variant(["http://server1", "http://server2"]));

            m_mockChannel
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
            Assert.That(sut.NamespaceUris.ToArray(), Is.EqualTo([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EqualTo(["http://server1", "http://server2"]));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldFetchAndUpdateTablesAndLogDifferences1Async()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray1 = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            var serverArray1 = new DataValue(new Variant(["http://server1", "http://server2"]));
            var namespaceArray2 = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace3", "http://namespace2"]));
            var serverArray2 = new DataValue(new Variant(["http://server1", "http://server2", "http://server3"]));

            m_mockChannel
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
            Assert.That(sut.NamespaceUris.ToArray(), Is.EqualTo([Opc.Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EqualTo(["http://server1", "http://server2"]));

            // Act
            await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            Assert.That(sut.NamespaceUris.ToArray(), Is.EqualTo([Opc.Ua.Namespaces.OpcUa, "http://namespace3", "http://namespace2"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EqualTo(["http://server1", "http://server2", "http://server3"]));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldFetchAndUpdateTablesAndLogDifferences2Async()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray1 = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            var serverArray1 = new DataValue(new Variant(["http://server1", "http://server2"]));
            var namespaceArray2 = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace3"]));
            var serverArray2 = new DataValue(new Variant(["http://server1"]));

            m_mockChannel
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
            Assert.That(sut.NamespaceUris.ToArray(), Is.EqualTo([Opc.Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EqualTo(["http://server1", "http://server2"]));

            // Act
            await sut.FetchNamespaceTablesAsync(ct);

            // Assert
            Assert.That(sut.NamespaceUris.ToArray(), Is.EqualTo([Opc.Ua.Namespaces.OpcUa, "http://namespace3"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EqualTo(["http://server1"]));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldHandlePartialSuccessAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(StatusCodes.BadUnexpectedError);

            m_mockChannel
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
            Assert.That(sut.NamespaceUris.ToArray(), Is.EqualTo([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            Assert.That(sut.ServerUris.ToArray(), Is.Empty);

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldThrowInCaseOfBadResponseAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(new Variant(["http://server1", "http://server2"]));

            m_mockChannel
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
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldThrowWhenNamespaceArrayCouldNotBeRetrievedAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(StatusCodes.BadUnexpectedError);
            var serverArray = new DataValue(StatusCodes.BadUnexpectedError);

            m_mockChannel
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
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShoulThrowWhenEmptyResponseAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            ArrayOf<DataValue> dataValues = ArrayOf<DataValue>.Empty;
            ArrayOf<DiagnosticInfo> diagnosticInfos = ArrayOf<DiagnosticInfo>.Empty;

            m_mockChannel
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
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldThrowWhenInvalidDataTypesForNamespaceTableAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant(12345));
            var serverArray = new DataValue(new Variant(67890));

            m_mockChannel
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
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldThrowWhenInvalidDataTypeForServerUrlsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Opc.Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(new Variant(67890));

            m_mockChannel
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
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));

            m_mockChannel.Verify();
        }

        [Test]
        public async Task PingServerAsyncShouldReturnTrueOnSuccessfulPingAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var serverState = new DataValue(Variant.From(ServerState.Running));

            m_mockChannel
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
            Assert.That(result, Is.True);
            m_mockChannel.Verify();
        }

        [Test]
        public async Task PingServerAsyncShouldReturnFalseInCaseOfInvalidServerStateAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var serverState = new DataValue(new Variant("InvalidState"));

            m_mockChannel
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
            Assert.That(result, Is.False);
            m_mockChannel.Verify();
        }

        [Test]
        public async Task PingServerAsyncShouldThrowWhenCancellationTokenCancelledAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    cts.Token))
                .ThrowsAsync(new TaskCanceledException()) // no matter what we throw
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.PingServerAsync(cts.Token);

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () => await act());
            m_mockChannel.Verify();
        }

        [Test]
        public async Task PingServerAsyncShouldNotThrowWhenCancellationTokenNotCancelledAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    ct))
                .ThrowsAsync(new TaskCanceledException()) // no matter what we throw
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            Assert.That(result, Is.False);
            m_mockChannel.Verify();
        }

        [Test]
        public async Task PingServerAsyncShouldHandleNoCommunicationButInGuardBandAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNoCommunication))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            Assert.That(result, Is.True);
            m_mockChannel.Verify();
        }

        [Test]
        public async Task PingServerAsyncShouldHandleNoCommunicationOutsideGuardBandAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNoCommunication))
                .Verifiable(Times.Once);
            // Set timestamp to 0 so that elapsed time is very large
            sut.LastKeepAliveTimestamp = 0;

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            Assert.That(result, Is.False);
            m_mockChannel.Verify();
        }

        [Test]
        public async Task PingServerAsyncShouldHandleOtherErrorsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.PingServerAsync(ct);

            // Assert
            Assert.That(result, Is.False);
            m_mockChannel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldCloseSessionAndChannelSuccessfullyAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(true, true, ct);

            // Assert
            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldHandleAlreadyClosedSessionAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            sut.Dispose();

            // Act
            var result = await sut.CloseAsync(true, true, ct);

            // Assert
            Assert.That(result, Is.EqualTo(ServiceResult.Good));
        }

        [Test]
        public async Task CloseAsyncShouldHandleErrorsDuringCloseAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(true, true, ct);

            // Assert
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldCloseSessionWithoutDeletingSubscriptionsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(true, false, ct);

            // Assert
            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldHandleChannelErrorsAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(true, true, ct);

            // Assert
            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldCloseSessionSuccessfullyAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(ct);

            // Assert
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldHandleErrorsDuringCloseSessionAsync()
        {
            // Arrange
            var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);
            m_mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);

            // Act
            var result = await sut.CloseAsync(ct);

            // Assert
            Assert.That(result, Is.EqualTo(StatusCodes.BadUnexpectedError));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task ReconnectAsyncShouldReconnectSuccessfullyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            var serverNonce = new byte[] { 1, 2, 3, 4 };

            m_mockChannel
                .Setup(c => c.ReconnectAsync(It.IsAny<ITransportWaitingConnection>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);
            m_mockChannel
            .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ServerNonce = new ByteString(serverNonce),
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.ReconnectAsync(ct);

            // Assert
            Assert.That(sut._serverNonce, Is.EqualTo(serverNonce));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task ReconnectAsyncShouldHandleReconnectFailureAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.ReconnectAsync(It.IsAny<ITransportWaitingConnection>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);
            m_mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(ct);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.Message, Does.Match("*BadUnexpectedError*"));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task ReconnectAsyncShouldHandleNoSupportedFeaturesAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.None);

            // TODO: Should properly mock channel creation

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        [Test]
        public async Task ReconnectAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();

            m_mockChannel
                .Setup(c => c.ReconnectAsync(It.IsAny<ITransportWaitingConnection>(), It.IsAny<CancellationToken>()))
                .Throws(new TaskCanceledException())
                .Verifiable(Times.Once);
            m_mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(default);

            // Assert
            Assert.ThrowsAsync<TaskCanceledException>(async () => await act());
            m_mockChannel.Verify();
        }

        [Test]
        public async Task ReconnectAsyncShouldHandleServerResponseWithNullNonceAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.ReconnectAsync(It.IsAny<ITransportWaitingConnection>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);
            m_mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ServerNonce = default,
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.ReconnectAsync(ct);

            // Assert
            Assert.That(sut._serverNonce, Is.EqualTo(ByteString.Empty));
            m_mockChannel.Verify();
        }
        [Test]
        public async Task ReconnectAsyncShouldThrowWithIncompatibleIdentityAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(ct);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task ReconnectAsyncShouldThrowWithBadActivationResponseAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.ReconnectAsync(It.IsAny<ITransportWaitingConnection>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);
            m_mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            m_mockChannel
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
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task ReconnectAsyncShouldThrowWhenTimingOutAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);
            sut.SetConnected();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.ReconnectAsync(It.IsAny<ITransportWaitingConnection>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);
            m_mockChannel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.ReconnectAsync(ct);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            m_mockChannel.Verify();
        }

        [Test]
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
            m_options = m_options with { EnableComplexTypePreloading = false };
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, ep),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;
            var serverNonce = new byte[] { 1, 2, 3, 4 };
            var authToken = NodeId.Parse("s=cookie");

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = new ByteString(serverNonce),
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ServerNonce = new ByteString(serverNonce),
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Read limit
            m_mockChannel
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
            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 27),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = (ArrayOf<DataValue>)[..Enumerable
                        .Range(0, 27)
                        .Select(_ => new DataValue(Variant.Null))],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Namespaces
            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 2),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(Variant.From(new[] { Opc.Ua.Namespaces.OpcUa })),
                        new DataValue(Variant.From(Array.Empty<string>()))
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.OpenAsync(ct);

            // Assert
            Assert.That(sut._serverNonce, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            m_mockChannel.Verify();
        }

        [Test]
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
            m_options = m_options with { EnableComplexTypePreloading = false };
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, ep),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;
            var serverNonce = new byte[] { 1, 2, 3, 4 };
            var authToken = NodeId.Parse("s=cookie");

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = new ByteString(serverNonce),
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionNotActivated },
                    ServerNonce = new ByteString(serverNonce),
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated));
            m_mockChannel.Verify();
        }

        [Test]
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
            m_options = m_options with { EnableComplexTypePreloading = false };
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, ep),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;
            var serverNonce = new byte[] { 1, 2, 3, 4 };
            var authToken = NodeId.Parse("s=cookie");

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = new ByteString(serverNonce),
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionNotActivated },
                    ServerNonce = new ByteString(serverNonce),
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadNotConnected }
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadNotConnected))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task OpenAsyncShouldHandleSessionOpeningFailureAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Exactly(2));

            // Act
            Func<Task> act = async () => await sut.OpenAsync(CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task OpenAsyncShouldHandleBadSecurityPolicyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(default);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task OpenAsyncShouldHandleBadIdentityTokenPolicyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(default);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task OpenAsyncShouldHandleCancellationAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException())
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(default);

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () => await act());
            m_mockChannel.Verify();
        }

        [Test]
        public async Task OpenAsyncShouldHandleInvalidServerResponseAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
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
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = default,
                    SessionId = NodeId.Parse("s=connected")
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await sut.OpenAsync(ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        [Test]
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
            m_options = m_options with { EnableComplexTypePreloading = false };
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, ep),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;
            var serverNonce = new byte[] { 1, 2, 3, 4 };
            var authToken = NodeId.Parse("s=cookie");

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSessionResponse
                {
                    ServerNonce = new ByteString(serverNonce),
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                })
                .Verifiable(Times.Once);

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ServerNonce = new ByteString(serverNonce),
                    Results = [],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Read limit
            m_mockChannel
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
            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 27),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = (ArrayOf<DataValue>)[..Enumerable
                        .Range(0, 27)
                        .Select(_ => new DataValue(Variant.Null))],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Namespaces
            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 2),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                new DataValue(Variant.From(new[] { Opc.Ua.Namespaces.OpcUa })),
                new DataValue(Variant.From(Array.Empty<string>()))
                    ],
                    DiagnosticInfos = []
                })
                .Verifiable(Times.Once);

            // Act
            await sut.OpenAsync(ct);

            // Assert
            Assert.That(sut._serverNonce, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task BrowseAsyncShouldHandleWhenBrowseNextResultCollectionIsEmptyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                NodeId.Parse("ns=1;s=ChildNode1"),
                NodeId.Parse("ns=1;s=ChildNode2"),
                NodeId.Parse("ns=1;s=ChildNode3")
            };

            m_mockChannel
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
            m_mockChannel
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
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].Result.References, Has.Exactly(1).Matches<ReferenceDescription>(r => r.NodeId == nodeIds[0]));
            Assert.That(results[1].Result.References, Is.Empty);
            Assert.That(results[1].Result.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task BrowseAsyncShouldHandleContinuationPointsSuccessfullyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                NodeId.Parse("ns=1;s=ChildNode1"),
                NodeId.Parse("ns=1;s=ChildNode2"),
                NodeId.Parse("ns=1;s=ChildNode3")
            };

            m_mockChannel
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
            m_mockChannel
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
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results[0].Result.References, Has.Exactly(1).Matches<ReferenceDescription>(r => r.NodeId == nodeIds[0]));
            Assert.That(results[1].Result.References, Has.Exactly(1).Matches<ReferenceDescription>(r => r.NodeId == nodeIds[1]));
            Assert.That(results[2].Result.References, Has.Exactly(1).Matches<ReferenceDescription>(r => r.NodeId == nodeIds[2]));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task BrowseAsyncShouldHandleNullContinuationPointsSuccessfullyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                NodeId.Parse("ns=1;s=ChildNode1"),
                NodeId.Parse("ns=1;s=ChildNode2"),
                NodeId.Parse("ns=1;s=ChildNode3")
            };

            m_mockChannel
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
            m_mockChannel
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
                            ContinuationPoint = default
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
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].Result.References, Has.Exactly(1).Matches<ReferenceDescription>(r => r.NodeId == nodeIds[0]));
            Assert.That(results[1].Result.References.Count, Is.EqualTo(2));
            Assert.That(results[1].Result.References[0].NodeId, Is.EqualTo(nodeIds[1]));
            Assert.That(results[1].Result.References[1].NodeId, Is.EqualTo(nodeIds[2]));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task BrowseAsyncShouldHandleWhenBrowseResultCollectionIsEmptyAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                NodeId.Parse("ns=1;s=ChildNode1"),
                NodeId.Parse("ns=1;s=ChildNode2"),
                NodeId.Parse("ns=1;s=ChildNode3")
            };

            m_mockChannel
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
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Result.References, Is.Empty);
            Assert.That(results[0].Result.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task BrowseAsyncShouldHandleBrowseNextCancelledAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                NodeId.Parse("ns=1;s=ChildNode1"),
                NodeId.Parse("ns=1;s=ChildNode2"),
                NodeId.Parse("ns=1;s=ChildNode3")
            };

            m_mockChannel
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
            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest && !((BrowseNextRequest)r).ReleaseContinuationPoints),
                    ct))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);
            m_mockChannel
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
            Assert.ThrowsAsync<OperationCanceledException>(async () => await act());
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Result.References, Has.Exactly(1).Matches<ReferenceDescription>(r => r.NodeId == nodeIds[0]));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task BrowseAsyncShouldHandleBrowseNextWithBadResponseAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);
            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                NodeId.Parse("ns=1;s=ChildNode1"),
                NodeId.Parse("ns=1;s=ChildNode2"),
                NodeId.Parse("ns=1;s=ChildNode3")
            };

            m_mockChannel
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
            m_mockChannel
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
            m_mockChannel
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
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Result.References, Has.Exactly(1).Matches<ReferenceDescription>(r => r.NodeId == nodeIds[0]));
            m_mockChannel.Verify();
        }

        [Test]
        public async Task BrowseAsyncShouldHandleContinuationPointsWithErrorsAsync()
        {
            // Arrange
            await using var sut = new TestSessionBase(m_configuration,
                new ConfiguredEndpoint(null, new EndpointDescription()),
                m_options, m_mockObservability.Object, null);

            var ct = CancellationToken.None;

            var nodeIds = new[]
            {
                NodeId.Parse("ns=1;s=ChildNode1"),
                NodeId.Parse("ns=1;s=ChildNode2"),
                NodeId.Parse("ns=1;s=ChildNode3")
            };

            m_mockChannel
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
            m_mockChannel
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
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].Result.References, Has.Exactly(1).Matches<ReferenceDescription>(r => r.NodeId == NodeId.Parse("ns=1;s=ChildNode1")));
            Assert.That(results[1].Result.References, Is.Empty);
            Assert.That(results[1].Result.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            m_mockChannel.Verify();
        }

        private sealed class TestSessionBase : SessionBase
        {
            public TestSessionBase(ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint, SessionCreateOptions options,
                ITelemetryContext telemetry, ReverseConnectManager reverseConnect)
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
                IOptionsMonitor<Subscriptions.SubscriptionOptions> m_options, IMessageAckQueue queue,
                ITelemetryContext telemetry)
            {
                throw new NotImplementedException();
            }
        }

        private Mock<ITransportChannel> m_mockChannel;
        private Mock<ITelemetryContext> m_mockObservability;
        private Mock<ILogger<SessionBase>> m_mockLogger;
        private ApplicationConfiguration m_configuration;
        private SessionCreateOptions m_options;
    }
}
#endif
