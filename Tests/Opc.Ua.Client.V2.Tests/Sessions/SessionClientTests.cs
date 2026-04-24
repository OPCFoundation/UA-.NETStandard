// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Xunit;

    public class RequestHeaderData : TheoryData<XunitSerializableEncodeable<RequestHeader>>
    {
        public RequestHeaderData()
        {
            Add(new XunitSerializableEncodeable<RequestHeader>(null));
            Add(new XunitSerializableEncodeable<RequestHeader>(new RequestHeader()));
            Add(new XunitSerializableEncodeable<RequestHeader>(new RequestHeader { AuditEntryId = "audit-entry-id" }));
        }
    }

    public sealed class SessionClientTests : IDisposable
    {
        public SessionClientTests()
        {
            _mockObservability = new Mock<ITelemetryContext>();
            _mockChannel = new Mock<ITransportChannel>();
            _mockLogger = new Mock<ILogger<SessionClient>>();

            _mockObservability.Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(_mockLogger.Object);

            _sessionServices = new TestSessionServices(_mockObservability.Object,
                _mockChannel.Object);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _sessionServices.Dispose();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ActivateSessionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange
            var requestHeader = header.Value;
            var clientSignature = new SignatureData();
            var clientSoftwareCertificates = new SignedSoftwareCertificateCollection();
            var localeIds = new StringCollection();
            var userIdentityToken = new ExtensionObject();
            var userTokenSignature = new SignatureData();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ActivateSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ActivateSessionResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.ActivateSessionAsync(requestHeader,
                clientSignature, clientSoftwareCertificates, localeIds,
                userIdentityToken, userTokenSignature, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ActivateSessionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var clientSignature = new SignatureData();
            var clientSoftwareCertificates = new SignedSoftwareCertificateCollection();
            var localeIds = new StringCollection();
            var userIdentityToken = new ExtensionObject();
            var userTokenSignature = new SignatureData();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ActivateSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ActivateSessionResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.ActivateSessionAsync(requestHeader,
                clientSignature, clientSoftwareCertificates, localeIds,
                userIdentityToken, userTokenSignature, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ActivateSessionAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var clientSignature = new SignatureData();
            var clientSoftwareCertificates = new SignedSoftwareCertificateCollection();
            var localeIds = new StringCollection();
            var userIdentityToken = new ExtensionObject();
            var userTokenSignature = new SignatureData();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ActivateSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.ActivateSessionAsync(requestHeader,
                clientSignature, clientSoftwareCertificates, localeIds,
                userIdentityToken, userTokenSignature, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = new AddNodesItemCollection(
                Enumerable.Repeat(new AddNodesItem(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = new AddNodesResultCollection(
                    Enumerable.Repeat(new AddNodesResult(), 10).ToList())
                })
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = new AddNodesResultCollection(
                    Enumerable.Repeat(new AddNodesResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.AddNodesAsync(requestHeader, nodesToAdd, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = new AddNodesItemCollection(Enumerable.Repeat(new AddNodesItem(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = new AddNodesResultCollection(Enumerable.Repeat(new AddNodesResult(), 10).ToList())
                })
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = new AddNodesResultCollection(Enumerable.Repeat(new AddNodesResult(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.AddNodesAsync(requestHeader, nodesToAdd, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = new AddNodesItemCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddNodesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.AddNodesAsync(requestHeader,
                nodesToAdd, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = new AddNodesItemCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddNodesResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.AddNodesAsync(requestHeader,
                nodesToAdd, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = new AddNodesItemCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.AddNodesAsync(requestHeader,
                nodesToAdd, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = new AddNodesItemCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = [new AddNodesResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.AddNodesAsync(requestHeader, nodesToAdd, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = new AddReferencesItemCollection(
                Enumerable.Repeat(new AddReferencesItem(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = new StatusCodeCollection(
                    Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = new StatusCodeCollection(
                    Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToList())
                });

            // Act
            var response = await _sessionServices.AddReferencesAsync(requestHeader, referencesToAdd, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = new AddReferencesItemCollection(Enumerable.Repeat(new AddReferencesItem(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.AddReferencesAsync(requestHeader, referencesToAdd, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = new AddReferencesItemCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddReferencesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.AddReferencesAsync(requestHeader,
                referencesToAdd, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = new AddReferencesItemCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddReferencesResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.AddReferencesAsync(requestHeader,
                referencesToAdd, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = new AddReferencesItemCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.AddReferencesAsync(requestHeader,
                referencesToAdd, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = new AddReferencesItemCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.AddReferencesAsync(requestHeader, referencesToAdd, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Repeat(new BrowseDescription(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                    Enumerable.Repeat(new BrowseResult(), 10).ToList())
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                    Enumerable.Repeat(new BrowseResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view,
                requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldContainTraceContextInRequestHeaderAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Repeat(new BrowseDescription(), 5).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = activitySource => activitySource.Name == "TestActivitySource",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllData
            });
            var activitySource = new ActivitySource("TestActivitySource");
            activitySource.HasListeners().Should().BeTrue();
            _mockObservability.Setup(o => o.ActivitySource).Returns(activitySource);

            using var activity = activitySource.StartActivity("TestActivity");

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>((r, ct) =>
                {
                    requestHeader = r.RequestHeader;
                    return Task.FromResult<IServiceResponse>(new BrowseResponse
                    {
                        Results = new BrowseResultCollection(
                            Enumerable.Repeat(new BrowseResult(), 5).ToList())
                    });
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            if (requestHeader != null)
            {
                requestHeader.AdditionalHeader.Should().NotBeNull();
                var additionalParameters = requestHeader.AdditionalHeader.Body as AdditionalParametersType;
                additionalParameters.Should().NotBeNull();
                Assert.NotNull(additionalParameters);
                additionalParameters.Parameters.Should().Contain(p => p.Key == "traceparent");
            }
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldContainTraceContextInRequestHeaderWhenBatchedAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Repeat(new BrowseDescription(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = activitySource => activitySource.Name == "TestActivitySource",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllData
            });
            var activitySource = new ActivitySource("TestActivitySource");
            activitySource.HasListeners().Should().BeTrue();
            _mockObservability.Setup(o => o.ActivitySource).Returns(activitySource);
            _mockObservability.Setup(o => o.ActivitySource).Returns(activitySource);

            using var activity = activitySource.StartActivity("TestActivity");

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 10).ToList())
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            if (requestHeader != null)
            {
                requestHeader.AdditionalHeader.Should().NotBeNull();
                var additionalParameters = requestHeader.AdditionalHeader.Body as AdditionalParametersType;
                additionalParameters.Should().NotBeNull();
                Assert.NotNull(additionalParameters);
                additionalParameters.Parameters.Should().Contain(p => p.Key == "traceparent");
            }
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection(Enumerable.Repeat(new BrowseDescription(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;
            _sessionServices.TraceActivityUsingLogger = true;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 10).ToList())
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.BrowseAsync(requestHeader,
                view, requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleDiagnosticInfosCorrectlyAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Repeat(new BrowseDescription(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            var diagnosticInfo1 = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            var diagnosticInfo2 = new DiagnosticInfo
            {
                SymbolicId = 5,
                NamespaceUri = 6,
                Locale = 7,
                LocalizedText = 8,
                InnerDiagnosticInfo = diagnosticInfo1
            };

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 10).ToList()),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Repeat(diagnosticInfo1, 10).ToList())
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 5).ToList()),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Repeat(diagnosticInfo2, 5).ToList())
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader,
                view, 0, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            response.DiagnosticInfos.Count.Should().Be(15);
            response.DiagnosticInfos[0].SymbolicId.Should().Be(1);
            response.DiagnosticInfos[10].SymbolicId.Should().Be(5);
            response.DiagnosticInfos[10].InnerDiagnosticInfo.Should().NotBeNull();
            response.DiagnosticInfos[10].InnerDiagnosticInfo.SymbolicId.Should().Be(1);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleEmptyDiagnosticInfosCorrectlyAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Repeat(new BrowseDescription(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 10).ToList()),
                    DiagnosticInfos = []
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 5).ToList()),
                    DiagnosticInfos = []
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader,
                view, 0, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            response.DiagnosticInfos.Count.Should().Be(0);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleEmptyStringTablesInDiagnosticInfosAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Repeat(new BrowseDescription(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            var diagnosticInfo1 = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 10).ToList()),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Repeat(diagnosticInfo1, 10).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = []
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 5).ToList()),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Repeat(diagnosticInfo1, 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = []
                    }
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            response.DiagnosticInfos.Count.Should().Be(15);
            response.ResponseHeader.StringTable.Count.Should().Be(0);

            // Verify that the indexes in the diagnostic infos are correctly updated
            response.DiagnosticInfos[0].SymbolicId.Should().Be(1);
            response.DiagnosticInfos[0].NamespaceUri.Should().Be(2);
            response.DiagnosticInfos[0].Locale.Should().Be(3);
            response.DiagnosticInfos[0].LocalizedText.Should().Be(4);

            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleMixedDiagnosticInfosCorrectlyAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Repeat(new BrowseDescription(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            var diagnosticInfo1 = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 10).ToList()),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Repeat(diagnosticInfo1, 10).ToList())
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 5).ToList()),
                    DiagnosticInfos = []
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            response.DiagnosticInfos.Count.Should().Be(15);
            response.DiagnosticInfos[0].SymbolicId.Should().Be(1);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldRecombineStringTablesInDiagnosticInfos1Async(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Range(0, 15).Select(_ => new BrowseDescription()));
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            static DiagnosticInfo diagnosticInfo() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Range(0, 10).Select(_ => new BrowseResult())),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Range(0, 10).Select(_ => diagnosticInfo())),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2", "String3", "String4"]
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Range(0, 5).Select(_ => new BrowseResult())),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Range(0, 5).Select(_ => diagnosticInfo())),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String5", "String6", "String7", "String8"]
                    }
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            response.DiagnosticInfos.Count.Should().Be(15);
            response.ResponseHeader.StringTable.Count.Should().Be(8);
            response.ResponseHeader.StringTable.Should()
                .ContainInOrder("String1", "String2", "String3", "String4", "String5", "String6", "String7", "String8");

            // Verify that the indexes in the diagnostic infos are correctly updated
            response.DiagnosticInfos[0].SymbolicId.Should().Be(1);
            response.DiagnosticInfos[0].NamespaceUri.Should().Be(2);
            response.DiagnosticInfos[0].Locale.Should().Be(3);
            response.DiagnosticInfos[0].LocalizedText.Should().Be(4);

            response.DiagnosticInfos[10].SymbolicId.Should().Be(5);
            response.DiagnosticInfos[10].NamespaceUri.Should().Be(6);
            response.DiagnosticInfos[10].Locale.Should().Be(7);
            response.DiagnosticInfos[10].LocalizedText.Should().Be(8);

            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldRecombineStringTablesInDiagnosticInfos2Async(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Range(0, 15).Select(_ => new BrowseDescription()));
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            DiagnosticInfo diagnosticInfo1() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            DiagnosticInfo diagnosticInfo2() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Range(0, 10).Select(_ => new BrowseResult())),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Range(0, 10).Select(_ => diagnosticInfo1())),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2", "String3", "String4"]
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Range(0, 5).Select(_ => new BrowseResult())),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Range(0, 5).Select(_ => diagnosticInfo2())),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String5", "String6", "String7", "String8"]
                    }
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            response.DiagnosticInfos.Count.Should().Be(15);
            response.ResponseHeader.StringTable.Count.Should().Be(8);
            response.ResponseHeader.StringTable.Should()
                .ContainInOrder("String1", "String2", "String3", "String4", "String5", "String6", "String7", "String8");

            // Verify that the indexes in the diagnostic infos are correctly updated
            response.DiagnosticInfos[0].SymbolicId.Should().Be(1);
            response.DiagnosticInfos[0].NamespaceUri.Should().Be(2);
            response.DiagnosticInfos[0].Locale.Should().Be(3);
            response.DiagnosticInfos[0].LocalizedText.Should().Be(4);

            response.DiagnosticInfos[10].SymbolicId.Should().Be(5);
            response.DiagnosticInfos[10].NamespaceUri.Should().Be(6);
            response.DiagnosticInfos[10].Locale.Should().Be(7);
            response.DiagnosticInfos[10].LocalizedText.Should().Be(8);

            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldRecombineStringTablesInDiagnosticInfos3Async(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Range(0, 15).Select(_ => new BrowseDescription()));
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            DiagnosticInfo diagnosticInfo1() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 1,
                LocalizedText = 2
            };

            DiagnosticInfo diagnosticInfo2() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4,
                InnerDiagnosticInfo = diagnosticInfo1()
            };

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Range(0, 10).Select(_ => new BrowseResult())),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Range(0, 10).Select(_ => diagnosticInfo1())),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2"]
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Range(0, 5).Select(_ => new BrowseResult())),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        Enumerable.Range(0, 5).Select(_ => diagnosticInfo2())),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2", "String3", "String4"]
                    }
                });

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            response.DiagnosticInfos.Count.Should().Be(15);
            response.ResponseHeader.StringTable.Count.Should().Be(6);
            response.ResponseHeader.StringTable.Should().ContainInOrder("String1", "String2", "String1", "String2", "String3", "String4");

            // Verify that the indexes in the diagnostic infos are correctly updated
            response.DiagnosticInfos[0].SymbolicId.Should().Be(1);
            response.DiagnosticInfos[0].NamespaceUri.Should().Be(2);
            response.DiagnosticInfos[0].Locale.Should().Be(1);
            response.DiagnosticInfos[0].LocalizedText.Should().Be(2);

            response.DiagnosticInfos[10].SymbolicId.Should().Be(3);
            response.DiagnosticInfos[10].NamespaceUri.Should().Be(4);
            response.DiagnosticInfos[10].Locale.Should().Be(5);
            response.DiagnosticInfos[10].LocalizedText.Should().Be(6);
            response.DiagnosticInfos[10].InnerDiagnosticInfo.Should().NotBeNull();
            response.DiagnosticInfos[10].InnerDiagnosticInfo.SymbolicId.Should().Be(3);
            response.DiagnosticInfos[10].InnerDiagnosticInfo.NamespaceUri.Should().Be(4);
            response.DiagnosticInfos[10].InnerDiagnosticInfo.Locale.Should().Be(3);
            response.DiagnosticInfos[10].InnerDiagnosticInfo.LocalizedText.Should().Be(4);

            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view,
                requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.BrowseAsync(requestHeader, view,
                requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.BrowseAsync(requestHeader, view,
                requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = [new BrowseResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.BrowseAsync(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = new ByteStringCollection(
                Enumerable.Repeat(Array.Empty<byte>(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxBrowseContinuationPoints = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 10).ToList())
                })
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = new BrowseResultCollection(
                        Enumerable.Repeat(new BrowseResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = new ByteStringCollection(Enumerable.Repeat(Array.Empty<byte>(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxBrowseContinuationPoints = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = new BrowseResultCollection(Enumerable.Repeat(new BrowseResult(), 10).ToList())
                })
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = new BrowseResultCollection(Enumerable.Repeat(new BrowseResult(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = new ByteStringCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseNextResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = new ByteStringCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = new ByteStringCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = new ByteStringCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = [new BrowseResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = new CallMethodRequestCollection(
                Enumerable.Repeat(new CallMethodRequest(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerMethodCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResultCollection(
                    Enumerable.Repeat(new CallMethodResult(), 10).ToList())
                })
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResultCollection(
                    Enumerable.Repeat(new CallMethodResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.CallAsync(requestHeader, methodsToCall, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = new CallMethodRequestCollection(Enumerable.Repeat(new CallMethodRequest(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxNodesPerMethodCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResultCollection(Enumerable.Repeat(new CallMethodResult(), 10).ToList())
                })
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResultCollection(Enumerable.Repeat(new CallMethodResult(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.CallAsync(requestHeader,
                methodsToCall, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = new CallMethodRequestCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CallResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.CallAsync(requestHeader,
                methodsToCall, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = new CallMethodRequestCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CallAsync(requestHeader,
                methodsToCall, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = new CallMethodRequestCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CallAsync(requestHeader,
                methodsToCall, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = new CallMethodRequestCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CallResponse
                {
                    Results = [new CallMethodResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.CallAsync(requestHeader, methodsToCall, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CancelAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint requestHandle = 1u;
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CancelRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CancelResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.CancelAsync(requestHeader,
                requestHandle, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CancelAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint requestHandle = 1u;
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CancelRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CancelResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CancelAsync(requestHeader,
                requestHandle, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CancelAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint requestHandle = 1u;
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CancelRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CancelAsync(requestHeader,
                requestHandle, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CloseSessionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const bool deleteSubscriptions = true;
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CloseSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CloseSessionResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.CloseSessionAsync(requestHeader,
                deleteSubscriptions, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CloseSessionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const bool deleteSubscriptions = true;
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CloseSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CloseSessionAsync(requestHeader,
                deleteSubscriptions, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CloseSessionAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const bool deleteSubscriptions = true;
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CloseSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CloseSessionAsync(requestHeader,
                deleteSubscriptions, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection(
                Enumerable.Repeat(new MonitoredItemCreateRequest(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = new MonitoredItemCreateResultCollection(
                        Enumerable.Repeat(new MonitoredItemCreateResult(), 10).ToList())
                })
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = new MonitoredItemCreateResultCollection(
                        Enumerable.Repeat(new MonitoredItemCreateResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var requestHeader = header.Value;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection(Enumerable.Repeat(new MonitoredItemCreateRequest(), 15).ToList());
            var ct = CancellationToken.None;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = new MonitoredItemCreateResultCollection(Enumerable.Repeat(new MonitoredItemCreateResult(), 10).ToList())
                })
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = new MonitoredItemCreateResultCollection(Enumerable.Repeat(new MonitoredItemCreateResult(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateMonitoredItemsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection();
            var ct = CancellationToken.None;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = [new MonitoredItemCreateResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.CreateMonitoredItemsAsync(requestHeader, subscriptionId,
                timestampsToReturn, itemsToCreate, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CreateSubscriptionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const bool publishingEnabled = true;
            const byte priority = 1;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateSubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateSubscriptionResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.CreateSubscriptionAsync(requestHeader,
                requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount,
                maxNotificationsPerPublish, publishingEnabled, priority, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CreateSubscriptionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const bool publishingEnabled = true;
            const byte priority = 1;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateSubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CreateSubscriptionAsync(requestHeader,
                requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount,
                maxNotificationsPerPublish, publishingEnabled, priority, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task CreateSubscriptionAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const bool publishingEnabled = true;
            const byte priority = 1;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateSubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.CreateSubscriptionAsync(requestHeader,
                requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount,
                maxNotificationsPerPublish, publishingEnabled, priority, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection(Enumerable.Repeat(1u, 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = new StatusCodeCollection(
                    Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = new StatusCodeCollection(
                    Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToList())
                });

            // Act
            var response = await _sessionServices.DeleteMonitoredItemsAsync(requestHeader,
                subscriptionId, monitoredItemIds, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection(Enumerable.Repeat(1u, 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteMonitoredItemsAsync(requestHeader,
                subscriptionId, monitoredItemIds, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteMonitoredItemsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.DeleteMonitoredItemsAsync(
                requestHeader, subscriptionId, monitoredItemIds, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteMonitoredItemsAsync(requestHeader,
                subscriptionId, monitoredItemIds, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteMonitoredItemsAsync(requestHeader,
                subscriptionId, monitoredItemIds, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = new DeleteNodesItemCollection(
                Enumerable.Repeat(new DeleteNodesItem(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = new StatusCodeCollection(
                        Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = new StatusCodeCollection(
                        Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToList())
                });

            // Act
            var response = await _sessionServices.DeleteNodesAsync(requestHeader, nodesToDelete, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = new DeleteNodesItemCollection(Enumerable.Repeat(new DeleteNodesItem(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteNodesAsync(requestHeader, nodesToDelete, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = new DeleteNodesItemCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteNodesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.DeleteNodesAsync(requestHeader,
                nodesToDelete, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = new DeleteNodesItemCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteNodesResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteNodesAsync(requestHeader,
                nodesToDelete, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = new DeleteNodesItemCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteNodesAsync(requestHeader,
                nodesToDelete, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = new DeleteNodesItemCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.DeleteNodesAsync(requestHeader, nodesToDelete, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = new DeleteReferencesItemCollection(
                Enumerable.Repeat(new DeleteReferencesItem(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = new StatusCodeCollection(
                        Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = new StatusCodeCollection(
                        Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToList())
                });

            // Act
            var response = await _sessionServices.DeleteReferencesAsync(requestHeader, referencesToDelete, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = new DeleteReferencesItemCollection(Enumerable.Repeat(new DeleteReferencesItem(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteReferencesAsync(requestHeader, referencesToDelete, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = new DeleteReferencesItemCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteReferencesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.DeleteReferencesAsync(requestHeader,
                referencesToDelete, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = new DeleteReferencesItemCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteReferencesAsync(requestHeader,
                referencesToDelete, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = new DeleteReferencesItemCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteReferencesAsync(requestHeader,
                referencesToDelete, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = new DeleteReferencesItemCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.DeleteReferencesAsync(requestHeader, referencesToDelete, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteSubscriptionsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteSubscriptionsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.DeleteSubscriptionsAsync(
                requestHeader, subscriptionIds, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteSubscriptionsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteSubscriptionsAsync(requestHeader,
                subscriptionIds, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task DeleteSubscriptionsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.DeleteSubscriptionsAsync(requestHeader,
                subscriptionIds, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection(
                Enumerable.Repeat(new HistoryReadValueId(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerHistoryReadData = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = new HistoryReadResultCollection(
                    Enumerable.Repeat(new HistoryReadResult(), 10).ToList())
                })
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = new HistoryReadResultCollection(
                    Enumerable.Repeat(new HistoryReadResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject(new ReadEventDetails());
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection(Enumerable.Repeat(new HistoryReadValueId(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerHistoryReadEvents = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = new HistoryReadResultCollection(Enumerable.Repeat(new HistoryReadResult(), 10).ToList())
                })
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = new HistoryReadResultCollection(Enumerable.Repeat(new HistoryReadResult(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryReadResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints,
                nodesToRead, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryReadResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints,
                nodesToRead, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints,
                nodesToRead, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = [new HistoryReadResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.HistoryReadAsync(requestHeader, historyReadDetails,
                timestampsToReturn, releaseContinuationPoints, nodesToRead, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = new ExtensionObjectCollection(Enumerable.Repeat(new ExtensionObject(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerHistoryUpdateData = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = new HistoryUpdateResultCollection(Enumerable.Repeat(new HistoryUpdateResult(), 10).ToList())
                })
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = new HistoryUpdateResultCollection(Enumerable.Repeat(new HistoryUpdateResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = new ExtensionObjectCollection(
                Enumerable.Repeat(new ExtensionObject(new UpdateEventDetails()), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerHistoryUpdateEvents = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = new HistoryUpdateResultCollection(Enumerable.Repeat(new HistoryUpdateResult(), 10).ToList())
                })
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = new HistoryUpdateResultCollection(Enumerable.Repeat(new HistoryUpdateResult(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = new ExtensionObjectCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryUpdateResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.HistoryUpdateAsync(requestHeader,
                historyUpdateDetails, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = new ExtensionObjectCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.HistoryUpdateAsync(requestHeader,
                historyUpdateDetails, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = new ExtensionObjectCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.HistoryUpdateAsync(requestHeader,
                historyUpdateDetails, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = new ExtensionObjectCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = [new HistoryUpdateResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection(
                Enumerable.Repeat(new MonitoredItemModifyRequest(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = new MonitoredItemModifyResultCollection(
                        Enumerable.Repeat(new MonitoredItemModifyResult(), 10).ToList())
                })
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = new MonitoredItemModifyResultCollection(
                        Enumerable.Repeat(new MonitoredItemModifyResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.ModifyMonitoredItemsAsync(requestHeader, subscriptionId,
                timestampsToReturn, itemsToModify, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection(Enumerable.Repeat(new MonitoredItemModifyRequest(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = new MonitoredItemModifyResultCollection(Enumerable.Repeat(new MonitoredItemModifyResult(), 10).ToList())
                })
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = new MonitoredItemModifyResultCollection(Enumerable.Repeat(new MonitoredItemModifyResult(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.ModifyMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToModify, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifyMonitoredItemsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.ModifyMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToModify, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.ModifyMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToModify, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.ModifyMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToModify, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = [new MonitoredItemModifyResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.ModifyMonitoredItemsAsync(requestHeader, subscriptionId,
                timestampsToReturn, itemsToModify, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ModifySubscriptionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const byte priority = 1;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifySubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifySubscriptionResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.ModifySubscriptionAsync(requestHeader,
                subscriptionId, requestedPublishingInterval, requestedLifetimeCount,
                requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ModifySubscriptionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const byte priority = 1;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifySubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifySubscriptionResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.ModifySubscriptionAsync(requestHeader,
                subscriptionId, requestedPublishingInterval, requestedLifetimeCount,
                requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ModifySubscriptionAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const byte priority = 1;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifySubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.ModifySubscriptionAsync(requestHeader,
                subscriptionId, requestedPublishingInterval, requestedLifetimeCount,
                requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task PublishAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionAcknowledgements = new SubscriptionAcknowledgementCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is PublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new PublishResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.PublishAsync(requestHeader,
                subscriptionAcknowledgements, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task PublishAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionAcknowledgements = new SubscriptionAcknowledgementCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is PublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new PublishResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.PublishAsync(requestHeader,
                subscriptionAcknowledgements, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task PublishAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionAcknowledgements = new SubscriptionAcknowledgementCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is PublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.PublishAsync(requestHeader,
                subscriptionAcknowledgements, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task QueryFirstAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var view = new ViewDescription();
            var nodeTypes = new NodeTypeDescriptionCollection();
            var filter = new ContentFilter();
            const uint maxDataSetsToReturn = 10u;
            const uint maxReferencesToReturn = 10u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryFirstRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new QueryFirstResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.QueryFirstAsync(requestHeader,
                view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task QueryFirstAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var view = new ViewDescription();
            var nodeTypes = new NodeTypeDescriptionCollection();
            var filter = new ContentFilter();
            const uint maxDataSetsToReturn = 10u;
            const uint maxReferencesToReturn = 10u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryFirstRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new QueryFirstResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.QueryFirstAsync(requestHeader,
                view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task QueryFirstAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var view = new ViewDescription();
            var nodeTypes = new NodeTypeDescriptionCollection();
            var filter = new ContentFilter();
            const uint maxDataSetsToReturn = 10u;
            const uint maxReferencesToReturn = 10u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryFirstRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.QueryFirstAsync(requestHeader,
                view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task QueryNextAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoint = true;
            var continuationPoint = Array.Empty<byte>();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new QueryNextResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.QueryNextAsync(requestHeader,
                releaseContinuationPoint, continuationPoint, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task QueryNextAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoint = true;
            var continuationPoint = Array.Empty<byte>();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new QueryNextResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.QueryNextAsync(requestHeader,
                releaseContinuationPoint, continuationPoint, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task QueryNextAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoint = true;
            var continuationPoint = Array.Empty<byte>();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.QueryNextAsync(requestHeader,
                releaseContinuationPoint, continuationPoint, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection(
                Enumerable.Repeat(new ReadValueId(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerRead = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(
                    Enumerable.Repeat(new DataValue(), 10).ToList())
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(
                    Enumerable.Repeat(new DataValue(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.ReadAsync(requestHeader, maxAge,
                timestampsToReturn, nodesToRead, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection(Enumerable.Repeat(new ReadValueId(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerRead = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(Enumerable.Repeat(new DataValue(), 10).ToList())
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(Enumerable.Repeat(new DataValue(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.ReadAsync(requestHeader, maxAge,
                timestampsToReturn, nodesToRead, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ReadResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.ReadAsync(requestHeader,
                maxAge, timestampsToReturn, nodesToRead, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.ReadAsync(requestHeader,
                maxAge, timestampsToReturn, nodesToRead, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.ReadAsync(requestHeader,
                maxAge, timestampsToReturn, nodesToRead, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [new DataValue { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.ReadAsync(requestHeader, maxAge,
                timestampsToReturn, nodesToRead, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = new NodeIdCollection(Enumerable.Repeat(new NodeId(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerRegisterNodes = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = new NodeIdCollection(Enumerable.Repeat(new NodeId(), 10).ToList())
                })
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = new NodeIdCollection(Enumerable.Repeat(new NodeId(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.RegisterNodesAsync(requestHeader, nodesToRegister, ct);

            // Assert
            response.Should().NotBeNull();
            response.RegisteredNodeIds.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = new NodeIdCollection(Enumerable.Repeat(new NodeId(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerRegisterNodes = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = new NodeIdCollection(Enumerable.Repeat(new NodeId(), 10).ToList())
                })
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = new NodeIdCollection(Enumerable.Repeat(new NodeId(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.RegisterNodesAsync(requestHeader, nodesToRegister, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = new NodeIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new RegisterNodesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.RegisterNodesAsync(
                requestHeader, nodesToRegister, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = new NodeIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new RegisterNodesResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.RegisterNodesAsync(requestHeader,
                nodesToRegister, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = new NodeIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.RegisterNodesAsync(requestHeader,
                nodesToRegister, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task RepublishAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint retransmitSequenceNumber = 1u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RepublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new RepublishResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.RepublishAsync(requestHeader,
                subscriptionId, retransmitSequenceNumber, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task RepublishAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint retransmitSequenceNumber = 1u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RepublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new RepublishResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.RepublishAsync(requestHeader,
                subscriptionId, retransmitSequenceNumber, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task RepublishAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint retransmitSequenceNumber = 1u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RepublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.RepublishAsync(requestHeader,
                subscriptionId, retransmitSequenceNumber, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection(Enumerable.Repeat(1u, 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = new StatusCodeCollection(
                    Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = new StatusCodeCollection(
                    Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToList())
                });

            // Act
            var response = await _sessionServices.SetMonitoringModeAsync(requestHeader, subscriptionId,
                monitoringMode, monitoredItemIds, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection(Enumerable.Repeat(1u, 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetMonitoringModeResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetPublishingModeAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool publishingEnabled = true;
            var subscriptionIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetPublishingModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetPublishingModeResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.SetPublishingModeAsync(requestHeader,
                publishingEnabled, subscriptionIds, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetPublishingModeAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool publishingEnabled = true;
            var subscriptionIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetPublishingModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetPublishingModeResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.SetPublishingModeAsync(requestHeader,
                publishingEnabled, subscriptionIds, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetPublishingModeAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const bool publishingEnabled = true;
            var subscriptionIds = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetPublishingModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.SetPublishingModeAsync(requestHeader,
                publishingEnabled, subscriptionIds, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection(Enumerable.Repeat(1u, 15).ToList());
            var linksToRemove = new UInt32Collection(Enumerable.Repeat(1u, 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = new StatusCodeCollection(
                        Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = new StatusCodeCollection(
                        Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToList()),
                    RemoveResults = new StatusCodeCollection(
                        Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToList())
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    RemoveResults = new StatusCodeCollection(
                        Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                });

            // Act
            var response = await _sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            response.Should().NotBeNull();
            response.AddResults.Count.Should().Be(15);
            response.RemoveResults.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection(Enumerable.Repeat(1u, 15).ToList());
            var linksToRemove = new UInt32Collection(Enumerable.Repeat(1u, 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = new StatusCodeCollection(
                        Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection();
            var linksToRemove = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetTriggeringResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection();
            var linksToRemove = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection();
            var linksToRemove = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection();
            var linksToRemove = new UInt32Collection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = [StatusCodes.Good],
                    AddDiagnosticInfos = [new DiagnosticInfo()],
                    RemoveResults = [StatusCodes.Good],
                    RemoveDiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.SetTriggeringAsync(requestHeader, subscriptionId,
                triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            response.Should().NotBeNull();
            response.AddResults.Should().HaveCount(1);
            response.AddDiagnosticInfos.Should().HaveCount(1);
            response.RemoveResults.Should().HaveCount(1);
            response.RemoveDiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task TransferSubscriptionsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = new UInt32Collection();
            const bool sendInitialValues = true;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TransferSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TransferSubscriptionsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.TransferSubscriptionsAsync(
                requestHeader, subscriptionIds, sendInitialValues, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task TransferSubscriptionsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = new UInt32Collection();
            const bool sendInitialValues = true;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TransferSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TransferSubscriptionsResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.TransferSubscriptionsAsync(
                requestHeader, subscriptionIds, sendInitialValues, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task TransferSubscriptionsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = new UInt32Collection();
            const bool sendInitialValues = true;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TransferSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.TransferSubscriptionsAsync(
                requestHeader, subscriptionIds, sendInitialValues, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task TransferSubscriptionsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = new UInt32Collection();
            const bool sendInitialValues = true;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TransferSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TransferSubscriptionsResponse
                {
                    Results = [new TransferResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.TransferSubscriptionsAsync(requestHeader,
                subscriptionIds, sendInitialValues, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var browsePaths = new BrowsePathCollection(Enumerable.Repeat(new BrowsePath(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResultCollection(Enumerable.Repeat(new BrowsePathResult(), 10).ToList())
                })
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResultCollection(Enumerable.Repeat(new BrowsePathResult(), 5).ToList())
                });

            // Act
            var response = await _sessionServices.TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var browsePaths = new BrowsePathCollection(Enumerable.Repeat(new BrowsePath(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResultCollection(Enumerable.Repeat(new BrowsePathResult(), 10).ToList())
                })
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResultCollection(Enumerable.Repeat(new BrowsePathResult(), 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var browsePaths = new BrowsePathCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.TranslateBrowsePathsToNodeIdsAsync(
                requestHeader, browsePaths, ct);

            // Assert
            response.Should().NotBeNull();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var browsePaths = new BrowsePathCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = [new BrowsePathResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = new NodeIdCollection(Enumerable.Repeat(new NodeId(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerRegisterNodes = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UnregisterNodesResponse())
                .ReturnsAsync(new UnregisterNodesResponse());

            // Act
            var response = await _sessionServices.UnregisterNodesAsync(requestHeader, nodesToUnregister, ct);

            // Assert
            response.Should().NotBeNull();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = new NodeIdCollection(Enumerable.Repeat(new NodeId(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerRegisterNodes = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UnregisterNodesResponse())
                .ReturnsAsync(new UnregisterNodesResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.UnregisterNodesAsync(requestHeader, nodesToUnregister, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = new NodeIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new UnregisterNodesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.UnregisterNodesAsync(
                requestHeader, nodesToUnregister, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = new NodeIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new UnregisterNodesResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.UnregisterNodesAsync(requestHeader,
                nodesToUnregister, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = new NodeIdCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.UnregisterNodesAsync(requestHeader,
                nodesToUnregister, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = new WriteValueCollection(Enumerable.Repeat(new WriteValue(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerWrite = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse
                {
                    Results = new StatusCodeCollection(
                    Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new WriteResponse
                {
                    Results = new StatusCodeCollection(
                    Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToList())
                });

            // Act
            var response = await _sessionServices.WriteAsync(requestHeader, nodesToWrite, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Count.Should().Be(15);
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = new WriteValueCollection(Enumerable.Repeat(new WriteValue(), 15).ToList());
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _sessionServices.OperationLimits.MaxNodesPerWrite = 10;

            _mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToList())
                })
                .ReturnsAsync(new WriteResponse
                {
                    Results = new StatusCodeCollection(Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToList()),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await _sessionServices.WriteAsync(requestHeader, nodesToWrite, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = new WriteValueCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new WriteResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.WriteAsync(requestHeader,
                nodesToWrite, ct);

            // Assert
            response.Should().NotBeNull();
            requestHeader?.RequestHandle.Should().NotBe(0);
            requestHeader?.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = new WriteValueCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                })
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.WriteAsync(requestHeader,
                nodesToWrite, ct);

            // Assert
            await act.Should().ThrowAsync<ServiceResultException>();
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = new WriteValueCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await _sessionServices.WriteAsync(requestHeader,
                nodesToWrite, ct);

            // Assert
            await act.Should().ThrowAsync<IOException>().WithMessage("Test exception");
            _mockChannel.Verify();
        }

        [Theory]
        [ClassData(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            XunitSerializableEncodeable<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = new WriteValueCollection();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            _mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new WriteResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            // Act
            var response = await _sessionServices.WriteAsync(requestHeader, nodesToWrite, ct);

            // Assert
            response.Should().NotBeNull();
            response.Results.Should().HaveCount(1);
            response.DiagnosticInfos.Should().HaveCount(1);
        }

        private sealed class TestSessionServices : Opc.Ua.Client.Sessions.SessionClient
        {
            public TestSessionServices(ITelemetryContext telemetry, ITransportChannel channel)
                : base(telemetry, channel) => AttachChannel(channel);
        }

        private readonly Mock<ITransportChannel> _mockChannel;
        private readonly Mock<ILogger<SessionClient>> _mockLogger;
        private readonly Mock<ITelemetryContext> _mockObservability;
        private readonly TestSessionServices _sessionServices;
    }
}
