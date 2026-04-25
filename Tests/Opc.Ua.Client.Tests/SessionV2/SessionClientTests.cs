#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Sessions
{
    public class RequestHeaderData : IEnumerable
    {
        public IEnumerator GetEnumerator()
        {
            yield return new TestCaseData(new EncodeableTestData<RequestHeader>(null));
            yield return new TestCaseData(new EncodeableTestData<RequestHeader>(new RequestHeader()));
            yield return new TestCaseData(new EncodeableTestData<RequestHeader>(new RequestHeader { AuditEntryId = "audit-entry-id" }));
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [TestFixture]
    public sealed class SessionClientTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockObservability = new Mock<IV2TelemetryContext>();
            m_mockChannel = new Mock<ITransportChannel>();
            m_mockLogger = new Mock<ILogger<SessionClient>>();

            m_mockObservability.Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(m_mockLogger.Object);

            m_sessionServices = new TestSessionServices(m_mockObservability.Object,
                m_mockChannel.Object);
        }

        [TearDown]
        public void TearDown()
        {
            m_sessionServices.Dispose();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ActivateSessionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange
            var requestHeader = header.Value;
            var clientSignature = new SignatureData();
            var clientSoftwareCertificates = ArrayOf<SignedSoftwareCertificate>.Empty;
            var localeIds = ArrayOf<string>.Empty;
            var userIdentityToken = new ExtensionObject();
            var userTokenSignature = new SignatureData();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ActivateSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ActivateSessionResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.ActivateSessionAsync(requestHeader,
                clientSignature, clientSoftwareCertificates, localeIds,
                userIdentityToken, userTokenSignature, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ActivateSessionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var clientSignature = new SignatureData();
            var clientSoftwareCertificates = ArrayOf<SignedSoftwareCertificate>.Empty;
            var localeIds = ArrayOf<string>.Empty;
            var userIdentityToken = new ExtensionObject();
            var userTokenSignature = new SignatureData();
            var ct = CancellationToken.None;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.ActivateSessionAsync(requestHeader,
                clientSignature, clientSoftwareCertificates, localeIds,
                userIdentityToken, userTokenSignature, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ActivateSessionAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var clientSignature = new SignatureData();
            var clientSoftwareCertificates = ArrayOf<SignedSoftwareCertificate>.Empty;
            var localeIds = ArrayOf<string>.Empty;
            var userIdentityToken = new ExtensionObject();
            var userTokenSignature = new SignatureData();
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ActivateSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.ActivateSessionAsync(requestHeader,
                clientSignature, clientSoftwareCertificates, localeIds,
                userIdentityToken, userTokenSignature, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = Enumerable.Repeat(new AddNodesItem(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = Enumerable.Repeat(new AddNodesResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = Enumerable.Repeat(new AddNodesResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.AddNodesAsync(requestHeader, nodesToAdd, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = Enumerable.Repeat(new AddNodesItem(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = Enumerable.Repeat(new AddNodesResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = Enumerable.Repeat(new AddNodesResult(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.AddNodesAsync(requestHeader, nodesToAdd, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = ArrayOf<AddNodesItem>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddNodesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.AddNodesAsync(requestHeader,
                nodesToAdd, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = ArrayOf<AddNodesItem>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.AddNodesAsync(requestHeader,
                nodesToAdd, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = ArrayOf<AddNodesItem>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.AddNodesAsync(requestHeader,
                nodesToAdd, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddNodesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var nodesToAdd = ArrayOf<AddNodesItem>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            var response = await m_sessionServices.AddNodesAsync(requestHeader, nodesToAdd, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = Enumerable.Repeat(new AddReferencesItem(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.AddReferencesAsync(requestHeader, referencesToAdd, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = Enumerable.Repeat(new AddReferencesItem(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.AddReferencesAsync(requestHeader, referencesToAdd, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = ArrayOf<AddReferencesItem>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddReferencesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.AddReferencesAsync(requestHeader,
                referencesToAdd, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = ArrayOf<AddReferencesItem>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.AddReferencesAsync(requestHeader,
                referencesToAdd, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = ArrayOf<AddReferencesItem>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.AddReferencesAsync(requestHeader,
                referencesToAdd, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task AddReferencesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var referencesToAdd = ArrayOf<AddReferencesItem>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            var response = await m_sessionServices.AddReferencesAsync(requestHeader, referencesToAdd, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = Enumerable.Repeat(new BrowseDescription(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader, view,
                requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldContainTraceContextInRequestHeaderAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = Enumerable.Repeat(new BrowseDescription(), 5).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = activitySource => activitySource.Name == "TestActivitySource",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllData
            });
            var activitySource = new ActivitySource("TestActivitySource");
            Assert.That(activitySource.HasListeners(), Is.True);
            m_mockObservability.Setup(o => o.ActivitySource).Returns(activitySource);

            using var activity = activitySource.StartActivity("TestActivity");

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>((r, ct) =>
                {
                    requestHeader = r.RequestHeader;
                    return new ValueTask<IServiceResponse>(new BrowseResponse
                    {
                        Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf()
                    });
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null)
            {
                Assert.That(requestHeader.AdditionalHeader, Is.Not.Null);
                Assert.That(requestHeader.AdditionalHeader.TryGetEncodeable<AdditionalParametersType>(out var additionalParameters), Is.True);
                Assert.That(additionalParameters, Is.Not.Null);
                Assert.That(additionalParameters!.Parameters.Find(p => p.Key == "traceparent"), Is.Not.Null);
            }
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldContainTraceContextInRequestHeaderWhenBatchedAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = Enumerable.Repeat(new BrowseDescription(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = activitySource => activitySource.Name == "TestActivitySource",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllData
            });
            var activitySource = new ActivitySource("TestActivitySource");
            Assert.That(activitySource.HasListeners(), Is.True);
            m_mockObservability.Setup(o => o.ActivitySource).Returns(activitySource);
            m_mockObservability.Setup(o => o.ActivitySource).Returns(activitySource);

            using var activity = activitySource.StartActivity("TestActivity");

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null)
            {
                Assert.That(requestHeader.AdditionalHeader, Is.Not.Null);
                Assert.That(requestHeader.AdditionalHeader.TryGetEncodeable<AdditionalParametersType>(out var additionalParameters), Is.True);
                Assert.That(additionalParameters, Is.Not.Null);
                Assert.That(additionalParameters!.Parameters.Find(p => p.Key == "traceparent"), Is.Not.Null);
            }
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = Enumerable.Repeat(new BrowseDescription(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;
            m_sessionServices.TraceActivityUsingLogger = true;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.BrowseAsync(requestHeader,
                view, requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleDiagnosticInfosCorrectlyAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = Enumerable.Repeat(new BrowseDescription(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

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

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 10).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Repeat(diagnosticInfo1, 10).ToArrayOf()
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Repeat(diagnosticInfo2, 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader,
                view, 0, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[10].SymbolicId, Is.EqualTo(5));
            Assert.That(response.DiagnosticInfos[10].InnerDiagnosticInfo, Is.Not.Null);
            Assert.That(response.DiagnosticInfos[10].InnerDiagnosticInfo.SymbolicId, Is.EqualTo(1));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleEmptyDiagnosticInfosCorrectlyAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = Enumerable.Repeat(new BrowseDescription(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 10).ToArrayOf(),
                    DiagnosticInfos = []
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf(),
                    DiagnosticInfos = []
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader,
                view, 0, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(0));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleEmptyStringTablesInDiagnosticInfosAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = Enumerable.Repeat(new BrowseDescription(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            var diagnosticInfo1 = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 10).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Repeat(diagnosticInfo1, 10).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = []
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Repeat(diagnosticInfo1, 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = []
                    }
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.ResponseHeader.StringTable.Count, Is.EqualTo(0));

            // Verify that the indexes in the diagnostic infos are correctly updated
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[0].NamespaceUri, Is.EqualTo(2));
            Assert.That(response.DiagnosticInfos[0].Locale, Is.EqualTo(3));
            Assert.That(response.DiagnosticInfos[0].LocalizedText, Is.EqualTo(4));

            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldHandleMixedDiagnosticInfosCorrectlyAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = Enumerable.Repeat(new BrowseDescription(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            var diagnosticInfo1 = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 10).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Repeat(diagnosticInfo1, 10).ToArrayOf()
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf(),
                    DiagnosticInfos = []
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldRecombineStringTablesInDiagnosticInfos1Async(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = Enumerable.Range(0, 15).Select(_ => new BrowseDescription()).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

            static DiagnosticInfo diagnosticInfo() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Range(0, 10).Select(_ => new BrowseResult()).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Range(0, 10).Select(_ => diagnosticInfo()).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2", "String3", "String4"]
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Range(0, 5).Select(_ => new BrowseResult()).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Range(0, 5).Select(_ => diagnosticInfo()).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String5", "String6", "String7", "String8"]
                    }
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.ResponseHeader.StringTable.Count, Is.EqualTo(8));
            Assert.That(response.ResponseHeader.StringTable, Is.EqualTo(new[] { "String1", "String2", "String3", "String4", "String5", "String6", "String7", "String8" }));

            // Verify that the indexes in the diagnostic infos are correctly updated
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[0].NamespaceUri, Is.EqualTo(2));
            Assert.That(response.DiagnosticInfos[0].Locale, Is.EqualTo(3));
            Assert.That(response.DiagnosticInfos[0].LocalizedText, Is.EqualTo(4));

            Assert.That(response.DiagnosticInfos[10].SymbolicId, Is.EqualTo(5));
            Assert.That(response.DiagnosticInfos[10].NamespaceUri, Is.EqualTo(6));
            Assert.That(response.DiagnosticInfos[10].Locale, Is.EqualTo(7));
            Assert.That(response.DiagnosticInfos[10].LocalizedText, Is.EqualTo(8));

            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldRecombineStringTablesInDiagnosticInfos2Async(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = Enumerable.Range(0, 15).Select(_ => new BrowseDescription()).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

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

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Range(0, 10).Select(_ => new BrowseResult()).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Range(0, 10).Select(_ => diagnosticInfo1()).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2", "String3", "String4"]
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Range(0, 5).Select(_ => new BrowseResult()).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Range(0, 5).Select(_ => diagnosticInfo2()).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String5", "String6", "String7", "String8"]
                    }
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.ResponseHeader.StringTable.Count, Is.EqualTo(8));
            Assert.That(response.ResponseHeader.StringTable, Is.EqualTo(new[] { "String1", "String2", "String3", "String4", "String5", "String6", "String7", "String8" }));

            // Verify that the indexes in the diagnostic infos are correctly updated
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[0].NamespaceUri, Is.EqualTo(2));
            Assert.That(response.DiagnosticInfos[0].Locale, Is.EqualTo(3));
            Assert.That(response.DiagnosticInfos[0].LocalizedText, Is.EqualTo(4));

            Assert.That(response.DiagnosticInfos[10].SymbolicId, Is.EqualTo(5));
            Assert.That(response.DiagnosticInfos[10].NamespaceUri, Is.EqualTo(6));
            Assert.That(response.DiagnosticInfos[10].Locale, Is.EqualTo(7));
            Assert.That(response.DiagnosticInfos[10].LocalizedText, Is.EqualTo(8));

            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldRecombineStringTablesInDiagnosticInfos3Async(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            var nodesToBrowse = Enumerable.Range(0, 15).Select(_ => new BrowseDescription()).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerBrowse = 10;

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

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Range(0, 10).Select(_ => new BrowseResult()).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Range(0, 10).Select(_ => diagnosticInfo1()).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2"]
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = Enumerable.Range(0, 5).Select(_ => new BrowseResult()).ToArrayOf(),
                    DiagnosticInfos = Enumerable.Range(0, 5).Select(_ => diagnosticInfo2()).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2", "String3", "String4"]
                    }
                });

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader, view, 0, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.ResponseHeader.StringTable.Count, Is.EqualTo(6));
            Assert.That(response.ResponseHeader.StringTable, Is.EqualTo(new[] { "String1", "String2", "String1", "String2", "String3", "String4" }));

            // Verify that the indexes in the diagnostic infos are correctly updated
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[0].NamespaceUri, Is.EqualTo(2));
            Assert.That(response.DiagnosticInfos[0].Locale, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[0].LocalizedText, Is.EqualTo(2));

            Assert.That(response.DiagnosticInfos[10].SymbolicId, Is.EqualTo(3));
            Assert.That(response.DiagnosticInfos[10].NamespaceUri, Is.EqualTo(4));
            Assert.That(response.DiagnosticInfos[10].Locale, Is.EqualTo(5));
            Assert.That(response.DiagnosticInfos[10].LocalizedText, Is.EqualTo(6));
            Assert.That(response.DiagnosticInfos[10].InnerDiagnosticInfo, Is.Not.Null);
            Assert.That(response.DiagnosticInfos[10].InnerDiagnosticInfo.SymbolicId, Is.EqualTo(3));
            Assert.That(response.DiagnosticInfos[10].InnerDiagnosticInfo.NamespaceUri, Is.EqualTo(4));
            Assert.That(response.DiagnosticInfos[10].InnerDiagnosticInfo.Locale, Is.EqualTo(3));
            Assert.That(response.DiagnosticInfos[10].InnerDiagnosticInfo.LocalizedText, Is.EqualTo(4));

            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = ArrayOf<BrowseDescription>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.BrowseAsync(requestHeader, view,
                requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = ArrayOf<BrowseDescription>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.BrowseAsync(requestHeader, view,
                requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = ArrayOf<BrowseDescription>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.BrowseAsync(requestHeader, view,
                requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = ArrayOf<BrowseDescription>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            var response = await m_sessionServices.BrowseAsync(requestHeader, view, requestedMaxReferencesPerNode, nodesToBrowse, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = Enumerable.Repeat(ByteString.Empty, 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxBrowseContinuationPoints = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = Enumerable.Repeat(ByteString.Empty, 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxBrowseContinuationPoints = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = Enumerable.Repeat(new BrowseResult(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = ArrayOf<ByteString>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseNextResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = ArrayOf<ByteString>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = ArrayOf<ByteString>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task BrowseNextAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoints = true;
            var requestHeader = header.Value;
            var continuationPoints = ArrayOf<ByteString>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            var response = await m_sessionServices.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = Enumerable.Repeat(new CallMethodRequest(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerMethodCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = Enumerable.Repeat(new CallMethodResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new CallResponse
                {
                    Results = Enumerable.Repeat(new CallMethodResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.CallAsync(requestHeader, methodsToCall, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = Enumerable.Repeat(new CallMethodRequest(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxNodesPerMethodCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = Enumerable.Repeat(new CallMethodResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new CallResponse
                {
                    Results = Enumerable.Repeat(new CallMethodResult(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.CallAsync(requestHeader,
                methodsToCall, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = ArrayOf<CallMethodRequest>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CallResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.CallAsync(requestHeader,
                methodsToCall, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = ArrayOf<CallMethodRequest>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.CallAsync(requestHeader,
                methodsToCall, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = ArrayOf<CallMethodRequest>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.CallAsync(requestHeader,
                methodsToCall, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CallAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            var methodsToCall = ArrayOf<CallMethodRequest>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            var response = await m_sessionServices.CallAsync(requestHeader, methodsToCall, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CancelAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint requestHandle = 1u;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CancelRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CancelResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.CancelAsync(requestHeader,
                requestHandle, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CancelAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint requestHandle = 1u;
            var ct = CancellationToken.None;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.CancelAsync(requestHeader,
                requestHandle, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CancelAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint requestHandle = 1u;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CancelRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.CancelAsync(requestHeader,
                requestHandle, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CloseSessionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const bool deleteSubscriptions = true;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CloseSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CloseSessionResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.CloseSessionAsync(requestHeader,
                deleteSubscriptions, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CloseSessionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const bool deleteSubscriptions = true;
            var ct = CancellationToken.None;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.CloseSessionAsync(requestHeader,
                deleteSubscriptions, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CloseSessionAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const bool deleteSubscriptions = true;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CloseSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.CloseSessionAsync(requestHeader,
                deleteSubscriptions, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = Enumerable.Repeat(new MonitoredItemCreateRequest(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat(new MonitoredItemCreateResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat(new MonitoredItemCreateResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var requestHeader = header.Value;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = Enumerable.Repeat(new MonitoredItemCreateRequest(), 15).ToArrayOf();
            var ct = CancellationToken.None;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat(new MonitoredItemCreateResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat(new MonitoredItemCreateResult(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = ArrayOf<MonitoredItemCreateRequest>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateMonitoredItemsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = ArrayOf<MonitoredItemCreateRequest>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = ArrayOf<MonitoredItemCreateRequest>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.CreateMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToCreate, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CreateMonitoredItemsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var requestHeader = header.Value;
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = ArrayOf<MonitoredItemCreateRequest>.Empty;
            var ct = CancellationToken.None;

            m_mockChannel
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
            var response = await m_sessionServices.CreateMonitoredItemsAsync(requestHeader, subscriptionId,
                timestampsToReturn, itemsToCreate, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CreateSubscriptionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
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

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateSubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateSubscriptionResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.CreateSubscriptionAsync(requestHeader,
                requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount,
                maxNotificationsPerPublish, publishingEnabled, priority, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CreateSubscriptionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
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

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.CreateSubscriptionAsync(requestHeader,
                requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount,
                maxNotificationsPerPublish, publishingEnabled, priority, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task CreateSubscriptionAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
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

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateSubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.CreateSubscriptionAsync(requestHeader,
                requestedPublishingInterval, requestedLifetimeCount, requestedMaxKeepAliveCount,
                maxNotificationsPerPublish, publishingEnabled, priority, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = Enumerable.Repeat(1u, 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.DeleteMonitoredItemsAsync(requestHeader,
                subscriptionId, monitoredItemIds, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = Enumerable.Repeat(1u, 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.DeleteMonitoredItemsAsync(requestHeader,
                subscriptionId, monitoredItemIds, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteMonitoredItemsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.DeleteMonitoredItemsAsync(
                requestHeader, subscriptionId, monitoredItemIds, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.DeleteMonitoredItemsAsync(requestHeader,
                subscriptionId, monitoredItemIds, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteMonitoredItemsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            var monitoredItemIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.DeleteMonitoredItemsAsync(requestHeader,
                subscriptionId, monitoredItemIds, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = Enumerable.Repeat(new DeleteNodesItem(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.DeleteNodesAsync(requestHeader, nodesToDelete, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = Enumerable.Repeat(new DeleteNodesItem(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.DeleteNodesAsync(requestHeader, nodesToDelete, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = ArrayOf<DeleteNodesItem>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteNodesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.DeleteNodesAsync(requestHeader,
                nodesToDelete, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = ArrayOf<DeleteNodesItem>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.DeleteNodesAsync(requestHeader,
                nodesToDelete, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = ArrayOf<DeleteNodesItem>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.DeleteNodesAsync(requestHeader,
                nodesToDelete, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteNodesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToDelete = ArrayOf<DeleteNodesItem>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.DeleteNodesAsync(requestHeader, nodesToDelete, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = Enumerable.Repeat(new DeleteReferencesItem(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.DeleteReferencesAsync(requestHeader, referencesToDelete, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = Enumerable.Repeat(new DeleteReferencesItem(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerNodeManagement = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.DeleteReferencesAsync(requestHeader, referencesToDelete, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = ArrayOf<DeleteReferencesItem>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteReferencesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.DeleteReferencesAsync(requestHeader,
                referencesToDelete, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = ArrayOf<DeleteReferencesItem>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.DeleteReferencesAsync(requestHeader,
                referencesToDelete, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = ArrayOf<DeleteReferencesItem>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.DeleteReferencesAsync(requestHeader,
                referencesToDelete, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteReferencesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var referencesToDelete = ArrayOf<DeleteReferencesItem>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.DeleteReferencesAsync(requestHeader, referencesToDelete, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteSubscriptionsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteSubscriptionsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.DeleteSubscriptionsAsync(
                requestHeader, subscriptionIds, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteSubscriptionsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.DeleteSubscriptionsAsync(requestHeader,
                subscriptionIds, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task DeleteSubscriptionsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.DeleteSubscriptionsAsync(requestHeader,
                subscriptionIds, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = Enumerable.Repeat(new HistoryReadValueId(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerHistoryReadData = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = Enumerable.Repeat(new HistoryReadResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = Enumerable.Repeat(new HistoryReadResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject(new ReadEventDetails());
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = Enumerable.Repeat(new HistoryReadValueId(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerHistoryReadEvents = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = Enumerable.Repeat(new HistoryReadResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = Enumerable.Repeat(new HistoryReadResult(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = ArrayOf<HistoryReadValueId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryReadResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints,
                nodesToRead, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = ArrayOf<HistoryReadValueId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints,
                nodesToRead, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = ArrayOf<HistoryReadValueId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints,
                nodesToRead, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryReadAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = ArrayOf<HistoryReadValueId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.HistoryReadAsync(requestHeader, historyReadDetails,
                timestampsToReturn, releaseContinuationPoints, nodesToRead, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = Enumerable.Repeat(new ExtensionObject(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerHistoryUpdateData = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = Enumerable.Repeat(new HistoryUpdateResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = Enumerable.Repeat(new HistoryUpdateResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = Enumerable.Repeat(new ExtensionObject(new UpdateEventDetails()), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerHistoryUpdateEvents = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = Enumerable.Repeat(new HistoryUpdateResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = Enumerable.Repeat(new HistoryUpdateResult(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = ArrayOf<ExtensionObject>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryUpdateResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.HistoryUpdateAsync(requestHeader,
                historyUpdateDetails, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = ArrayOf<ExtensionObject>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.HistoryUpdateAsync(requestHeader,
                historyUpdateDetails, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = ArrayOf<ExtensionObject>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.HistoryUpdateAsync(requestHeader,
                historyUpdateDetails, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task HistoryUpdateAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var historyUpdateDetails = ArrayOf<ExtensionObject>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.HistoryUpdateAsync(requestHeader, historyUpdateDetails, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = Enumerable.Repeat(new MonitoredItemModifyRequest(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat(new MonitoredItemModifyResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat(new MonitoredItemModifyResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.ModifyMonitoredItemsAsync(requestHeader, subscriptionId,
                timestampsToReturn, itemsToModify, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = Enumerable.Repeat(new MonitoredItemModifyRequest(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat(new MonitoredItemModifyResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = Enumerable.Repeat(new MonitoredItemModifyResult(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.ModifyMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToModify, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = ArrayOf<MonitoredItemModifyRequest>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifyMonitoredItemsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.ModifyMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToModify, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = ArrayOf<MonitoredItemModifyRequest>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.ModifyMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToModify, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = ArrayOf<MonitoredItemModifyRequest>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.ModifyMonitoredItemsAsync(requestHeader,
                subscriptionId, timestampsToReturn, itemsToModify, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ModifyMonitoredItemsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = ArrayOf<MonitoredItemModifyRequest>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.ModifyMonitoredItemsAsync(requestHeader, subscriptionId,
                timestampsToReturn, itemsToModify, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ModifySubscriptionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
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

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifySubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifySubscriptionResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.ModifySubscriptionAsync(requestHeader,
                subscriptionId, requestedPublishingInterval, requestedLifetimeCount,
                requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ModifySubscriptionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
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

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.ModifySubscriptionAsync(requestHeader,
                subscriptionId, requestedPublishingInterval, requestedLifetimeCount,
                requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ModifySubscriptionAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
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

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifySubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.ModifySubscriptionAsync(requestHeader,
                subscriptionId, requestedPublishingInterval, requestedLifetimeCount,
                requestedMaxKeepAliveCount, maxNotificationsPerPublish, priority, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task PublishAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionAcknowledgements = ArrayOf<SubscriptionAcknowledgement>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is PublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new PublishResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.PublishAsync(requestHeader,
                subscriptionAcknowledgements, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task PublishAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionAcknowledgements = ArrayOf<SubscriptionAcknowledgement>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.PublishAsync(requestHeader,
                subscriptionAcknowledgements, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task PublishAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionAcknowledgements = ArrayOf<SubscriptionAcknowledgement>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is PublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.PublishAsync(requestHeader,
                subscriptionAcknowledgements, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task QueryFirstAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var view = new ViewDescription();
            var nodeTypes = ArrayOf<NodeTypeDescription>.Empty;
            var filter = new ContentFilter();
            const uint maxDataSetsToReturn = 10u;
            const uint maxReferencesToReturn = 10u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryFirstRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new QueryFirstResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.QueryFirstAsync(requestHeader,
                view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task QueryFirstAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var view = new ViewDescription();
            var nodeTypes = ArrayOf<NodeTypeDescription>.Empty;
            var filter = new ContentFilter();
            const uint maxDataSetsToReturn = 10u;
            const uint maxReferencesToReturn = 10u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.QueryFirstAsync(requestHeader,
                view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task QueryFirstAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var view = new ViewDescription();
            var nodeTypes = ArrayOf<NodeTypeDescription>.Empty;
            var filter = new ContentFilter();
            const uint maxDataSetsToReturn = 10u;
            const uint maxReferencesToReturn = 10u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryFirstRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.QueryFirstAsync(requestHeader,
                view, nodeTypes, filter, maxDataSetsToReturn, maxReferencesToReturn, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task QueryNextAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoint = true;
            var continuationPoint = ByteString.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new QueryNextResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.QueryNextAsync(requestHeader,
                releaseContinuationPoint, continuationPoint, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task QueryNextAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoint = true;
            var continuationPoint = ByteString.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.QueryNextAsync(requestHeader,
                releaseContinuationPoint, continuationPoint, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task QueryNextAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool releaseContinuationPoint = true;
            var continuationPoint = ByteString.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.QueryNextAsync(requestHeader,
                releaseContinuationPoint, continuationPoint, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = Enumerable.Repeat(new ReadValueId(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerRead = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = Enumerable.Repeat(new DataValue(), 10).ToArrayOf()
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = Enumerable.Repeat(new DataValue(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.ReadAsync(requestHeader, maxAge,
                timestampsToReturn, nodesToRead, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = Enumerable.Repeat(new ReadValueId(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerRead = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = Enumerable.Repeat(new DataValue(), 10).ToArrayOf()
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = Enumerable.Repeat(new DataValue(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.ReadAsync(requestHeader, maxAge,
                timestampsToReturn, nodesToRead, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = ArrayOf<ReadValueId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ReadResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.ReadAsync(requestHeader,
                maxAge, timestampsToReturn, nodesToRead, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = ArrayOf<ReadValueId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.ReadAsync(requestHeader,
                maxAge, timestampsToReturn, nodesToRead, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = ArrayOf<ReadValueId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.ReadAsync(requestHeader,
                maxAge, timestampsToReturn, nodesToRead, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task ReadAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = ArrayOf<ReadValueId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.ReadAsync(requestHeader, maxAge,
                timestampsToReturn, nodesToRead, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = Enumerable.Repeat(default(NodeId), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerRegisterNodes = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = Enumerable.Repeat(default(NodeId), 10).ToArrayOf()
                })
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = Enumerable.Repeat(default(NodeId), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.RegisterNodesAsync(requestHeader, nodesToRegister, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = Enumerable.Repeat(default(NodeId), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerRegisterNodes = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = Enumerable.Repeat(default(NodeId), 10).ToArrayOf()
                })
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = Enumerable.Repeat(default(NodeId), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.RegisterNodesAsync(requestHeader, nodesToRegister, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = ArrayOf<NodeId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new RegisterNodesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.RegisterNodesAsync(
                requestHeader, nodesToRegister, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = ArrayOf<NodeId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.RegisterNodesAsync(requestHeader,
                nodesToRegister, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task RegisterNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToRegister = ArrayOf<NodeId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.RegisterNodesAsync(requestHeader,
                nodesToRegister, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task RepublishAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint retransmitSequenceNumber = 1u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RepublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new RepublishResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.RepublishAsync(requestHeader,
                subscriptionId, retransmitSequenceNumber, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task RepublishAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint retransmitSequenceNumber = 1u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.RepublishAsync(requestHeader,
                subscriptionId, retransmitSequenceNumber, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task RepublishAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint retransmitSequenceNumber = 1u;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RepublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.RepublishAsync(requestHeader,
                subscriptionId, retransmitSequenceNumber, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = Enumerable.Repeat(1u, 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.SetMonitoringModeAsync(requestHeader, subscriptionId,
                monitoringMode, monitoredItemIds, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = Enumerable.Repeat(1u, 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetMonitoringModeResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetMonitoringModeAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.SetMonitoringModeAsync(requestHeader,
                subscriptionId, monitoringMode, monitoredItemIds, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetPublishingModeAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool publishingEnabled = true;
            var subscriptionIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetPublishingModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetPublishingModeResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.SetPublishingModeAsync(requestHeader,
                publishingEnabled, subscriptionIds, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetPublishingModeAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool publishingEnabled = true;
            var subscriptionIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.SetPublishingModeAsync(requestHeader,
                publishingEnabled, subscriptionIds, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetPublishingModeAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const bool publishingEnabled = true;
            var subscriptionIds = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetPublishingModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.SetPublishingModeAsync(requestHeader,
                publishingEnabled, subscriptionIds, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = Enumerable.Repeat(1u, 15).ToArrayOf();
            var linksToRemove = Enumerable.Repeat(1u, 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToArrayOf(),
                    RemoveResults = Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToArrayOf()
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    RemoveResults = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.AddResults.Count, Is.EqualTo(15));
            Assert.That(response.RemoveResults.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = Enumerable.Repeat(1u, 15).ToArrayOf();
            var linksToRemove = Enumerable.Repeat(1u, 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxMonitoredItemsPerCall = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = ArrayOf<uint>.Empty;
            var linksToRemove = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetTriggeringResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = ArrayOf<uint>.Empty;
            var linksToRemove = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = ArrayOf<uint>.Empty;
            var linksToRemove = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.SetTriggeringAsync(requestHeader,
                subscriptionId, triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task SetTriggeringAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = ArrayOf<uint>.Empty;
            var linksToRemove = ArrayOf<uint>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.SetTriggeringAsync(requestHeader, subscriptionId,
                triggeringItemId, linksToAdd, linksToRemove, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.AddResults, Has.Count.EqualTo(1));
            Assert.That(response.AddDiagnosticInfos, Has.Count.EqualTo(1));
            Assert.That(response.RemoveResults, Has.Count.EqualTo(1));
            Assert.That(response.RemoveDiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task TransferSubscriptionsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = ArrayOf<uint>.Empty;
            const bool sendInitialValues = true;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TransferSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TransferSubscriptionsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.TransferSubscriptionsAsync(
                requestHeader, subscriptionIds, sendInitialValues, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task TransferSubscriptionsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = ArrayOf<uint>.Empty;
            const bool sendInitialValues = true;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.TransferSubscriptionsAsync(
                requestHeader, subscriptionIds, sendInitialValues, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task TransferSubscriptionsAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = ArrayOf<uint>.Empty;
            const bool sendInitialValues = true;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TransferSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.TransferSubscriptionsAsync(
                requestHeader, subscriptionIds, sendInitialValues, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task TransferSubscriptionsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var subscriptionIds = ArrayOf<uint>.Empty;
            const bool sendInitialValues = true;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.TransferSubscriptionsAsync(requestHeader,
                subscriptionIds, sendInitialValues, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var browsePaths = Enumerable.Repeat(new BrowsePath(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = Enumerable.Repeat(new BrowsePathResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = Enumerable.Repeat(new BrowsePathResult(), 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var browsePaths = Enumerable.Repeat(new BrowsePath(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = Enumerable.Repeat(new BrowsePathResult(), 10).ToArrayOf()
                })
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = Enumerable.Repeat(new BrowsePathResult(), 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var browsePaths = ArrayOf<BrowsePath>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.TranslateBrowsePathsToNodeIdsAsync(
                requestHeader, browsePaths, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var browsePaths = ArrayOf<BrowsePath>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = Enumerable.Repeat(default(NodeId), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerRegisterNodes = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UnregisterNodesResponse())
                .ReturnsAsync(new UnregisterNodesResponse());

            // Act
            var response = await m_sessionServices.UnregisterNodesAsync(requestHeader, nodesToUnregister, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = Enumerable.Repeat(default(NodeId), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerRegisterNodes = 10;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.UnregisterNodesAsync(requestHeader, nodesToUnregister, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = ArrayOf<NodeId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new UnregisterNodesResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.UnregisterNodesAsync(
                requestHeader, nodesToUnregister, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = ArrayOf<NodeId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.UnregisterNodesAsync(requestHeader,
                nodesToUnregister, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task UnregisterNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToUnregister = ArrayOf<NodeId>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.UnregisterNodesAsync(requestHeader,
                nodesToUnregister, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = Enumerable.Repeat(new WriteValue(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerWrite = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new WriteResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 5).ToArrayOf()
                });

            // Act
            var response = await m_sessionServices.WriteAsync(requestHeader, nodesToWrite, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldHandleBatchingWhenSecondOperationFailsAsync(EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = Enumerable.Repeat(new WriteValue(), 15).ToArrayOf();
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_sessionServices.OperationLimits.MaxNodesPerWrite = 10;

            m_mockChannel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new WriteResponse
                {
                    Results = Enumerable.Repeat((StatusCode)StatusCodes.Bad, 5).ToArrayOf(),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            // Act
            Func<Task> act = async () => await m_sessionServices.WriteAsync(requestHeader, nodesToWrite, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = ArrayOf<WriteValue>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new WriteResponse())
                .Verifiable(Times.Once);

            // Act
            var response = await m_sessionServices.WriteAsync(requestHeader,
                nodesToWrite, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            if (requestHeader != null) { Assert.That(requestHeader.RequestHandle, Is.Not.EqualTo(0u)); }
            Assert.That(requestHeader?.Timestamp, Is.EqualTo(DateTimeUtc.Now).Within(TimeSpan.FromSeconds(1)));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldThrowExceptionWhenResponseContainsBadStatusCodeAsync(EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = ArrayOf<WriteValue>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            Func<Task> act = async () => await m_sessionServices.WriteAsync(requestHeader,
                nodesToWrite, ct);

            // Assert
            Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldThrowExceptionWhenSendRequestAsyncThrowsAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = ArrayOf<WriteValue>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            // Act
            Func<Task> act = async () => await m_sessionServices.WriteAsync(requestHeader,
                nodesToWrite, ct);

            // Assert
            var ex = Assert.ThrowsAsync<IOException>(async () => await act());
            Assert.That(ex.Message, Does.Match("Test exception"));
            m_mockChannel.Verify();
        }

        
        [TestCaseSource(typeof(RequestHeaderData))]
        public async Task WriteAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            EncodeableTestData<RequestHeader> header)
        {
            // Arrange

            var nodesToWrite = ArrayOf<WriteValue>.Empty;
            var ct = CancellationToken.None;
            var requestHeader = header.Value;

            m_mockChannel
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
            var response = await m_sessionServices.WriteAsync(requestHeader, nodesToWrite, ct);

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.DiagnosticInfos, Has.Count.EqualTo(1));
        }

        private sealed class TestSessionServices : Opc.Ua.Client.Sessions.SessionClient
        {
            public TestSessionServices(IV2TelemetryContext telemetry, ITransportChannel channel)
                : base(telemetry, channel) => AttachChannel(channel);
        }

        private Mock<ITransportChannel> m_mockChannel;
        private Mock<ILogger<SessionClient>> m_mockLogger;
        private Mock<IV2TelemetryContext> m_mockObservability;
        private TestSessionServices m_sessionServices;
    }
}
#endif
