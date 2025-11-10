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
    public sealed class SessionTests
    {
        [Test]
        public async Task FetchOperationLimitsAsyncShouldFetchAllOperationLimitsAsync()
        {
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

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

            sut.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [new DataValue(new Variant(1000u))],
                    DiagnosticInfos = []
                }))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = []
                }));

            // Act
            await sut.FetchOperationLimitsAsync(ct).ConfigureAwait(false);

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
            Assert.That(sut.ServerCapabilities.MaxBrowseContinuationPoints, Is.EqualTo(13000));
            Assert.That(sut.ServerCapabilities.MaxHistoryContinuationPoints, Is.EqualTo(14000));
            Assert.That(sut.ServerCapabilities.MaxQueryContinuationPoints, Is.EqualTo(15000));
            Assert.That(sut.ServerCapabilities.MaxStringLength, Is.EqualTo(16000));
            Assert.That(sut.ServerCapabilities.MaxArrayLength, Is.EqualTo(17000));
            Assert.That(sut.ServerCapabilities.MaxByteStringLength, Is.EqualTo(18000));
            Assert.That(sut.ServerCapabilities.MinSupportedSampleRate, Is.EqualTo(19000.0));
            Assert.That(sut.ServerCapabilities.MaxSessions, Is.EqualTo(20000));
            Assert.That(sut.ServerCapabilities.MaxSubscriptions, Is.EqualTo(21000));
            Assert.That(sut.ServerCapabilities.MaxMonitoredItems, Is.EqualTo(22000));
            Assert.That(sut.ServerCapabilities.MaxMonitoredItemsPerSubscription, Is.EqualTo(23000));
            Assert.That(sut.ServerCapabilities.MaxMonitoredItemsQueueSize, Is.EqualTo(24000));
            Assert.That(sut.ServerCapabilities.MaxSubscriptionsPerSession, Is.EqualTo(25000));
            Assert.That(sut.ServerCapabilities.MaxWhereClauseParameters, Is.EqualTo(26000));
            Assert.That(sut.ServerCapabilities.MaxSelectClauseParameters, Is.EqualTo(27000));

            sut.Channel.Verify();
        }

        [Test]
        public void FetchOperationLimitsAsyncShouldHandleEmptyResponse()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var dataValues = new DataValueCollection();
            var diagnosticInfos = new DiagnosticInfoCollection();

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchOperationLimitsAsync(ct).ConfigureAwait(false));

            sut.Channel.Verify();
        }

        [Test]
        public void FetchOperationLimitsAsyncShouldHandlePartialData()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var dataValues = new DataValueCollection
            {
                new DataValue(new Variant(1000u)),
                new DataValue(new Variant(2000u)),
                new DataValue(new Variant(3000u))
            };

            sut.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [new DataValue(new Variant(1000u))],
                    DiagnosticInfos = []
                }))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = []
                }));

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchOperationLimitsAsync(ct).ConfigureAwait(false));

            sut.Channel.Verify();
        }

        [Test]
        public void FetchOperationLimitsAsyncShouldHandleErrors()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var dataValues = new DataValueCollection
            {
                new DataValue(StatusCodes.BadUnexpectedError)
            };

            var diagnosticInfos = new DiagnosticInfoCollection();

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Exactly(2));

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchOperationLimitsAsync(ct).ConfigureAwait(false));

            sut.Channel.Verify();
        }

        [Test]
        public void FetchOperationLimitsAsyncShouldThrowWhenInvalidDataTypes()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var dataValues = new DataValueCollection
            {
                new DataValue("InvalidDataType")
            };

            var diagnosticInfos = new DiagnosticInfoCollection();

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Exactly(2));

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchOperationLimitsAsync(ct).ConfigureAwait(false));

            sut.Channel.Verify();
        }

        [Test]
        public void FetchOperationLimitsAsyncShouldHandleTimeout()
        {
            var sut = SessionMock.Create();
            CancellationToken ct = new CancellationTokenSource(100).Token; // Set a short timeout

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException())
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<TaskCanceledException>(
                async () => await sut.FetchOperationLimitsAsync(ct).ConfigureAwait(false));

            sut.Channel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldFetchAndUpdateTablesAsync()
        {
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(new Variant(["http://server1", "http://server2"]));

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(sut.NamespaceUris.ToArray(), Is.EquivalentTo([Ua.Namespaces.OpcUa, "http://namespace2"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EquivalentTo(["http://server1", "http://server2"]));

            sut.Channel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldFetchAndUpdateTablesAndLogDifferences1Async()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var namespaceArray1 = new DataValue(new Variant([Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            var serverArray1 = new DataValue(new Variant(["http://server1", "http://server2"]));
            var namespaceArray2 = new DataValue(new Variant([Ua.Namespaces.OpcUa, "http://namespace3", "http://namespace2"]));
            var serverArray2 = new DataValue(new Variant(["http://server1", "http://server2", "http://server3"]));

            sut.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [namespaceArray1, serverArray1],
                    DiagnosticInfos = []
                }))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [namespaceArray2, serverArray2],
                    DiagnosticInfos = []
                }));

            // Act
            await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(sut.NamespaceUris.ToArray(), Is.EquivalentTo([Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EquivalentTo(["http://server1", "http://server2"]));

            // Act
            await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(sut.NamespaceUris.ToArray(), Is.EquivalentTo([Ua.Namespaces.OpcUa, "http://namespace3", "http://namespace2"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EquivalentTo(["http://server1", "http://server2", "http://server3"]));

            sut.Channel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldFetchAndUpdateTablesAndLogDifferences2Async()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var namespaceArray1 = new DataValue(new Variant([Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            var serverArray1 = new DataValue(new Variant(["http://server1", "http://server2"]));
            var namespaceArray2 = new DataValue(new Variant([Ua.Namespaces.OpcUa, "http://namespace3"]));
            var serverArray2 = new DataValue(new Variant(["http://server1"]));

            sut.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [namespaceArray1, serverArray1],
                    DiagnosticInfos = []
                }))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [namespaceArray2, serverArray2],
                    DiagnosticInfos = []
                }));

            // Act
            await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(sut.NamespaceUris.ToArray(), Is.EquivalentTo([Ua.Namespaces.OpcUa, "http://namespace2", "http://namespace3"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EquivalentTo(["http://server1", "http://server2"]));

            // Act
            await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(sut.NamespaceUris.ToArray(), Is.EquivalentTo([Ua.Namespaces.OpcUa, "http://namespace3"]));
            Assert.That(sut.ServerUris.ToArray(), Is.EquivalentTo(["http://server1"]));

            sut.Channel.Verify();
        }

        [Test]
        public async Task FetchNamespaceTablesAsyncShouldHandlePartialSuccessAsync()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(StatusCodes.BadUnexpectedError);

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Act
            await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(sut.NamespaceUris.ToArray(), Is.EquivalentTo([Ua.Namespaces.OpcUa, "http://namespace2"]));
            Assert.That(sut.ServerUris.ToArray(), Is.Empty);

            sut.Channel.Verify();
        }

        [Test]
        public void FetchNamespaceTablesAsyncShouldThrowInCaseOfBadResponse()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(new Variant(["http://server1", "http://server2"]));

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadUnexpectedError },
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));

            sut.Channel.Verify();
        }

        [Test]
        public void FetchNamespaceTablesAsyncShouldThrowWhenNamespaceArrayCouldNotBeRetrieved()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var namespaceArray = new DataValue(StatusCodes.BadUnexpectedError);
            var serverArray = new DataValue(StatusCodes.BadUnexpectedError);

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));

            sut.Channel.Verify();
        }

        [Test]
        public void FetchNamespaceTablesAsyncShoulThrowWhenEmptyResponse()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var dataValues = new DataValueCollection();
            var diagnosticInfos = new DiagnosticInfoCollection();

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = dataValues,
                    DiagnosticInfos = diagnosticInfos
                }))
                .Verifiable(Times.Once);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));

            sut.Channel.Verify();
        }

        [Test]
        public void FetchNamespaceTablesAsyncShouldThrowWhenInvalidDataTypesForNamespaceTable()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant(12345));
            var serverArray = new DataValue(new Variant(67890));

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));

            sut.Channel.Verify();
        }

        [Test]
        public void FetchNamespaceTablesAsyncShouldThrowWhenInvalidDataTypeForServerUrls()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            var namespaceArray = new DataValue(new Variant([Ua.Namespaces.OpcUa, "http://namespace2"]));
            var serverArray = new DataValue(new Variant(67890));

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ReadRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [namespaceArray, serverArray],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.FetchNamespaceTablesAsync(ct).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));

            sut.Channel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldCloseSessionAndChannelSuccessfullyAsync()
        {
            // Arrange
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);

            // Act
            StatusCode result = await sut.CloseAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            sut.Channel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldHandleAlreadyClosedSessionAsync()
        {
            // Arrange
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            sut.Dispose();

            // Act
            StatusCode result = await sut.CloseAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task CloseAsyncShouldHandleErrorsDuringCloseAsync()
        {
            // Arrange
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);

            // Act
            StatusCode result = await sut.CloseAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(StatusCodes.BadUnexpectedError));
            sut.Channel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldCloseSessionWithoutDeletingSubscriptionsAsync()
        {
            // Arrange
            var sut = SessionMock.Create();
            sut.SetConnected();
            sut.DeleteSubscriptionsOnClose = false;
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);

            // Act
            StatusCode result = await sut.CloseAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            sut.Channel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldHandleChannelErrorsAsync()
        {
            // Arrange
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);

            StatusCode result = await sut.CloseAsync(ct).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good)); // Eats the error from channel close
            sut.Channel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldCloseSessionSuccessfullyAsync()
        {
            // Arrange
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);

            // Act
            StatusCode result = await sut.CloseAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            sut.Channel.Verify();
        }

        [Test]
        public async Task CloseAsyncShouldHandleErrorsDuringCloseSessionAsync()
        {
            // Arrange
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);
            sut.Channel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);

            // Act
            StatusCode result = await sut.CloseAsync(ct).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.EqualTo(StatusCodes.BadUnexpectedError));
            sut.Channel.Verify();
        }

        [Test]
        public async Task ReconnectAsyncShouldReconnectSuccessfullyAsync()
        {
            // Arrange
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            byte[] serverNonce = [1, 2, 3, 4];

            sut.Channel
                .Setup(c => c.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);
            sut.Channel
            .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ActivateSessionResponse
                {
                    ServerNonce = serverNonce,
                    Results = [],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            await sut.ReconnectAsync(ct).ConfigureAwait(false);

            Assert.That(sut.ServerNonce, Is.EquivalentTo(serverNonce));
            sut.Channel.Verify();
        }

        [Test]
        public void ReconnectAsyncShouldHandleReconnectFailure()
        {
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Once);
            sut.Channel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.ReconnectAsync(ct).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));

            sut.Channel.Verify();
        }

        [Test]
        [Explicit("Requires proper channel creation mocking")]
        public void ReconnectAsyncShouldHandleNoSupportedFeatures()
        {
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.None);

            // TODO: Should properly mock channel creation

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.ReconnectAsync(ct).ConfigureAwait(false));

            sut.Channel.Verify();
        }

        [Test]
        public void ReconnectAsyncShouldHandleCancellation()
        {
            var sut = SessionMock.Create();
            sut.SetConnected();

            sut.Channel
                .Setup(c => c.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException())
                .Verifiable(Times.Once);
            sut.Channel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            Assert.ThrowsAsync<TaskCanceledException>(
                async () => await sut.ReconnectAsync(default).ConfigureAwait(false));

            sut.Channel.Verify();
        }

        [Test]
        public async Task ReconnectAsyncShouldHandleServerResponseWithNullNonceAsync()
        {
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);
            sut.Channel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ActivateSessionResponse
                {
                    ServerNonce = null,
                    Results = [],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            await sut.ReconnectAsync(ct).ConfigureAwait(false);

            Assert.That(sut.ServerNonce, Is.Null);
            sut.Channel.Verify();
        }

        [Test]
        public void ReconnectAsyncShouldThrowWithIncompatibleIdentity()
        {
            var ep = new EndpointDescription
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
            };
            var sut = SessionMock.Create(ep);
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.ReconnectAsync(ct).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));

            sut.Channel.Verify();
        }

        [Test]
        public void ReconnectAsyncShouldThrowWithBadActivationResponse()
        {
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);
            sut.Channel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionNotActivated },
                    Results = [],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.ReconnectAsync(ct).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated));

            sut.Channel.Verify();
        }

        [Test]
        public void ReconnectAsyncShouldThrowWhenTimingOut()
        {
            var sut = SessionMock.Create();
            sut.SetConnected();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);
            sut.Channel
                .Setup(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<ActivateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException())
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await sut.ReconnectAsync(ct).ConfigureAwait(false));

            sut.Channel.Verify();
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
            var sut = SessionMock.Create(ep);
            CancellationToken ct = CancellationToken.None;
            byte[] serverNonce = [1, 2, 3, 4];
            var authToken = NodeId.Parse("s=cookie");

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CreateSessionResponse
                {
                    ServerNonce = serverNonce,
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ActivateSessionResponse
                {
                    ServerNonce = serverNonce,
                    Results = [],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Read limit and also first keep alive timers
            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 1),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results =
                    [
                        new (new Variant(0u))
                    ],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.AtLeastOnce);

            // Operation limits
            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 27),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [.. Enumerable
                        .Range(0, 27)
                        .Select(_ => new DataValue(Variant.Null))],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            // Namespaces
            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ReadRequest>(r => r.NodesToRead.Count == 2),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results =
                    [
                        new (new[] { Ua.Namespaces.OpcUa }),
                        new(Array.Empty<string>())
                    ],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            await sut.OpenAsync("test", new UserIdentity(), ct).ConfigureAwait(false);

            Assert.That(sut.ServerNonce, Is.EquivalentTo(new byte[] { 1, 2, 3, 4 }));
            sut.Channel.Verify();
        }

        [Test]
        public void OpenAsyncShouldHandleCreateSessionSuccessButActivationError()
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
            var sut = SessionMock.Create(ep);
            CancellationToken ct = CancellationToken.None;
            byte[] serverNonce = [1, 2, 3, 4];
            var authToken = NodeId.Parse("s=cookie");

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CreateSessionResponse
                {
                    ServerNonce = serverNonce,
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionNotActivated },
                    ServerNonce = serverNonce,
                    Results = [],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask())
                .Verifiable(Times.Once);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.OpenAsync("test", new UserIdentity(), default).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated));
            sut.Channel.Verify();
        }

        [Test]
        public void OpenAsyncShouldHandleCreateSessionSuccessButActivationErrorAndThenCloseAlsoFails()
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
            var sut = SessionMock.Create(ep);
            CancellationToken ct = CancellationToken.None;
            byte[] serverNonce = [1, 2, 3, 4];
            var authToken = NodeId.Parse("s=cookie");

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CreateSessionResponse
                {
                    ServerNonce = serverNonce,
                    SessionId = NodeId.Parse("s=connected"),
                    AuthenticationToken = authToken,
                    ServerEndpoints = [ep]
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<ActivateSessionRequest>(r => r.RequestHeader.AuthenticationToken == authToken),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionNotActivated },
                    ServerNonce = serverNonce,
                    Results = [],
                    DiagnosticInfos = []
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CloseSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadNotConnected }
                }))
                .Verifiable(Times.Once);

            sut.Channel
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadNotConnected))
                .Verifiable(Times.Once);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.OpenAsync(
                    "test",
                    60000,
                    new UserIdentity(),
                    null,
                    true,
                    closeChannel: true,
                    default).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated));

            sut.Channel.Verify();
        }

        [Test]
        public void OpenAsyncShouldHandleSessionOpeningFailure()
        {
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
            var sut = SessionMock.Create(ep);

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUnexpectedError))
                .Verifiable(Times.Exactly(2));

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                  async () => await sut.OpenAsync("test", new UserIdentity(), default).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));

            sut.Channel.Verify();
        }

#if FALSE // TODO: Enable when moving certificate loading into OpenAsync
        [Test]
        public void OpenAsyncShouldHandleBadSecurityPolicy()
        {
            var ep = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = "Bad",
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens =
                [
                    new UserTokenPolicy()
                ]
            };
            var sut = SessionMock.Create(ep);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                  async () => await sut.OpenAsync("test", new UserIdentity(), default).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));

            sut.Channel.Verify();
        }
#endif

        [Test]
        public void OpenAsyncShouldHandleBadIdentityTokenPolicy()
        {
            var ep = new EndpointDescription
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
            };
            var sut = SessionMock.Create(ep);

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                  async () => await sut.OpenAsync("test", new UserIdentity(), default).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            sut.Channel.Verify();
        }

        [Test]
        public void OpenAsyncShouldHandleCancellation()
        {
            var sut = SessionMock.Create();

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TaskCanceledException())
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<TaskCanceledException>(
                async () => await sut.OpenAsync("test", new UserIdentity(), default).ConfigureAwait(false));

            sut.Channel.Verify();
        }

        [Test]
        public void OpenAsyncShouldHandleInvalidServerResponse()
        {
            var sut = SessionMock.Create();
            CancellationToken ct = CancellationToken.None;

            sut.Channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<CreateSessionRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(new CreateSessionResponse
                {
                    ServerNonce = null,
                    SessionId = NodeId.Parse("s=connected")
                }))
                .Verifiable(Times.Once);

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sut.OpenAsync("test", new UserIdentity(), ct).ConfigureAwait(false));

            sut.Channel.Verify();
        }
    }
}
