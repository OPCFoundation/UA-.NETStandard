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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SessionClient")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public sealed class SessionClientBatchTests
    {
        [DatapointSource]
        public static readonly RequestHeader[] RequestHeaders =
        [
            null,
            new RequestHeader(),
            new RequestHeader { AuditEntryId = "audit-entry-id" }
        ];

        [Theory]
        public async Task ActivateSessionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var clientSignature = new SignatureData();
            var localeIds = new StringCollection();
            var userIdentityToken = new ExtensionObject();
            var userTokenSignature = new SignatureData();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ActivateSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ActivateSessionResponse())
                .Verifiable(Times.Once);

            ActivateSessionResponse response = await sessionMock.ActivateSessionAsync(
                requestHeader,
                clientSignature,
                default,
                localeIds,
                userIdentityToken,
                userTokenSignature,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void ActivateSessionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var clientSignature = new SignatureData();
            var localeIds = new StringCollection();
            var userIdentityToken = new ExtensionObject();
            var userTokenSignature = new SignatureData();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.ActivateSessionAsync(
                    requestHeader,
                    clientSignature,
                    default,
                    localeIds,
                    userIdentityToken,
                    userTokenSignature,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void ActivateSessionAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var clientSignature = new SignatureData();
            var localeIds = new StringCollection();
            var userIdentityToken = new ExtensionObject();
            var userTokenSignature = new SignatureData();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ActivateSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.ActivateSessionAsync(
                    requestHeader,
                    clientSignature,
                    default,
                    localeIds,
                    userIdentityToken,
                    userTokenSignature,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task AddNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var nodesToAdd = new AddNodesItemCollection(
                [.. Enumerable.Repeat(new AddNodesItem(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerNodeManagement = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = new AddNodesResultCollection(
                    [.. Enumerable.Repeat(new AddNodesResult(), 10)])
                })
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = new AddNodesResultCollection(
                    [.. Enumerable.Repeat(new AddNodesResult(), 5)])
                });

            AddNodesResponse response = await sessionMock.AddNodesAsync(
                requestHeader,
                nodesToAdd,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void AddNodesAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var nodesToAdd = new AddNodesItemCollection(
                [.. Enumerable.Repeat(new AddNodesItem(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerNodeManagement = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = new AddNodesResultCollection(
                        [.. Enumerable.Repeat(new AddNodesResult(), 10)])
                })
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = new AddNodesResultCollection(
                        [.. Enumerable.Repeat(new AddNodesResult(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.AddNodesAsync(
                    requestHeader,
                    nodesToAdd,
                    ct).ConfigureAwait(false));
            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task AddNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var nodesToAdd = new AddNodesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddNodesResponse())
                .Verifiable(Times.Once);

            AddNodesResponse response = await sessionMock.AddNodesAsync(
                requestHeader,
                nodesToAdd,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void AddNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var nodesToAdd = new AddNodesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.AddNodesAsync(
                    requestHeader,
                    nodesToAdd,
                    ct).ConfigureAwait(false));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void AddNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var nodesToAdd = new AddNodesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.AddNodesAsync(requestHeader,
                nodesToAdd, ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task AddNodesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var nodesToAdd = new AddNodesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddNodesResponse
                {
                    Results = [new AddNodesResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            AddNodesResponse response = await sessionMock.AddNodesAsync(
                requestHeader,
                nodesToAdd,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task AddReferencesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var referencesToAdd = new AddReferencesItemCollection(
                [.. Enumerable.Repeat(new AddReferencesItem(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerNodeManagement = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = new StatusCodeCollection(
                    [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = new StatusCodeCollection(
                    [.. Enumerable.Repeat(StatusCodes.Good, 5)])
                });

            AddReferencesResponse response = await sessionMock.AddReferencesAsync(
                requestHeader,
                referencesToAdd,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void AddReferencesAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var referencesToAdd = new AddReferencesItemCollection(
                [.. Enumerable.Repeat(new AddReferencesItem(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerNodeManagement = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Bad, 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.AddReferencesAsync(
                    requestHeader,
                    referencesToAdd,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task AddReferencesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var referencesToAdd = new AddReferencesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddReferencesResponse())
                .Verifiable(Times.Once);

            AddReferencesResponse response = await sessionMock.AddReferencesAsync(requestHeader,
                referencesToAdd, ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void AddReferencesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var referencesToAdd = new AddReferencesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.AddReferencesAsync(
                    requestHeader,
                    referencesToAdd,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void AddReferencesAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var referencesToAdd = new AddReferencesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.AddReferencesAsync(
                    requestHeader,
                    referencesToAdd,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task AddReferencesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var referencesToAdd = new AddReferencesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is AddReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new AddReferencesResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            AddReferencesResponse response = await sessionMock.AddReferencesAsync(
                requestHeader,
                referencesToAdd,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task BrowseAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection(
                [.. Enumerable.Repeat(new BrowseDescription(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                    [.. Enumerable.Repeat(new BrowseResult(), 10)])
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                    [.. Enumerable.Repeat(new BrowseResult(), 5)])
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseAsyncShouldContainTraceContextInRequestHeaderAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                [.. Enumerable.Repeat(new BrowseDescription(), 5)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;

            ActivitySource activitySource = sessionMock.MessageContext.Telemetry.GetActivitySource();
            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = a => a.Name == activitySource.Name,
                Sample = (ref _) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref _) => ActivitySamplingResult.AllData
            });
            Assert.That(activitySource.HasListeners(), Is.True);
            sessionMock.ActivityTraceFlags = ClientTraceFlags.Traces;
            using Activity activity = activitySource.StartActivity("TestActivity");

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>((r, ct) =>
                {
                    requestHeader = r.RequestHeader;
                    return new ValueTask<IServiceResponse>(new BrowseResponse
                    {
                        Results = new BrowseResultCollection(
                            [.. Enumerable.Repeat(new BrowseResult(), 5)])
                    });
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                0,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            if (requestHeader != null)
            {
                Assert.That(requestHeader.AdditionalHeader, Is.Not.Null);
                var additionalParameters = requestHeader.AdditionalHeader.Body as AdditionalParametersType;
                Assert.That(additionalParameters, Is.Not.Null);
                Assert.That(additionalParameters.Parameters.Any(k => k.Key == "SpanContext"), Is.True);
            }

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Theory]
        public async Task BrowseAsyncShouldContainTraceContextInRequestHeaderWhenBatchedAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                [.. Enumerable.Repeat(new BrowseDescription(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;
            ActivitySource activitySource = sessionMock.MessageContext.Telemetry.GetActivitySource();
            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = a => a.Name == activitySource.Name,
                Sample = (ref _) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref _) => ActivitySamplingResult.AllData
            });
            Assert.That(activitySource.HasListeners(), Is.True);
            sessionMock.ActivityTraceFlags = ClientTraceFlags.Traces;

            using Activity activity = activitySource.StartActivity("TestActivity");

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 10)])
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 5)])
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                0,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            if (requestHeader != null)
            {
                Assert.That(requestHeader.AdditionalHeader, Is.Not.Null);
                var additionalParameters = requestHeader.AdditionalHeader.Body as AdditionalParametersType;
                Assert.That(additionalParameters, Is.Not.Null);
                Assert.That(additionalParameters.Parameters.Any(k => k.Key == "SpanContext"), Is.True);
            }

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void BrowseAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection(
                [.. Enumerable.Repeat(new BrowseDescription(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;
            sessionMock.ActivityTraceFlags = ClientTraceFlags.Log;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 10)])
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.BrowseAsync(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowse,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseAsyncShouldHandleDiagnosticInfosCorrectlyAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                [.. Enumerable.Repeat(new BrowseDescription(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;

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

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 10)]),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        [.. Enumerable.Repeat(diagnosticInfo1, 10)])
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 5)]),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        [.. Enumerable.Repeat(diagnosticInfo2, 5)])
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                0,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[10].SymbolicId, Is.EqualTo(5));
            Assert.That(response.DiagnosticInfos[10].InnerDiagnosticInfo, Is.Not.Null);
            Assert.That(response.DiagnosticInfos[10].InnerDiagnosticInfo.SymbolicId, Is.EqualTo(1));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseAsyncShouldHandleEmptyDiagnosticInfosCorrectlyAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                [.. Enumerable.Repeat(new BrowseDescription(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 10)]),
                    DiagnosticInfos = []
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 5)]),
                    DiagnosticInfos = []
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                0,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(0));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseAsyncShouldHandleEmptyStringTablesInDiagnosticInfosAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                [.. Enumerable.Repeat(new BrowseDescription(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;

            var diagnosticInfo1 = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 10)]),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        [.. Enumerable.Repeat(diagnosticInfo1, 10)]),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = []
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 5)]),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        [.. Enumerable.Repeat(diagnosticInfo1, 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = []
                    }
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                0,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.ResponseHeader.StringTable.Count, Is.EqualTo(0));

            // Verify that the indexes in the diagnostic infos are correctly updated
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[0].NamespaceUri, Is.EqualTo(2));
            Assert.That(response.DiagnosticInfos[0].Locale, Is.EqualTo(3));
            Assert.That(response.DiagnosticInfos[0].LocalizedText, Is.EqualTo(4));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseAsyncShouldHandleMixedDiagnosticInfosCorrectlyAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                [.. Enumerable.Repeat(new BrowseDescription(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;

            var diagnosticInfo1 = new DiagnosticInfo
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 10)]),
                    DiagnosticInfos = new DiagnosticInfoCollection(
                        [.. Enumerable.Repeat(diagnosticInfo1, 10)])
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 5)]),
                    DiagnosticInfos = []
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                0,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseAsyncShouldRecombineStringTablesInDiagnosticInfos1Async(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Range(0, 15).Select(_ => new BrowseDescription()));
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;

            static DiagnosticInfo DiagnosticInfo() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = [.. Enumerable.Range(0, 10).Select(_ => new BrowseResult())],
                    DiagnosticInfos = [.. Enumerable.Range(0, 10).Select(_ => DiagnosticInfo())],
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2", "String3", "String4"]
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = [.. Enumerable.Range(0, 5).Select(_ => new BrowseResult())],
                    DiagnosticInfos = [.. Enumerable.Range(0, 5).Select(_ => DiagnosticInfo())],
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String5", "String6", "String7", "String8"]
                    }
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                0,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.ResponseHeader.StringTable.Count, Is.EqualTo(8));
            Assert.That(response.ResponseHeader.StringTable,
                Is.EquivalentTo(["String1", "String2", "String3", "String4", "String5", "String6", "String7", "String8"]));

            // Verify that the indexes in the diagnostic infos are correctly updated
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[0].NamespaceUri, Is.EqualTo(2));
            Assert.That(response.DiagnosticInfos[0].Locale, Is.EqualTo(3));
            Assert.That(response.DiagnosticInfos[0].LocalizedText, Is.EqualTo(4));

            Assert.That(response.DiagnosticInfos[10].SymbolicId, Is.EqualTo(5));
            Assert.That(response.DiagnosticInfos[10].NamespaceUri, Is.EqualTo(6));
            Assert.That(response.DiagnosticInfos[10].Locale, Is.EqualTo(7));
            Assert.That(response.DiagnosticInfos[10].LocalizedText, Is.EqualTo(8));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseAsyncShouldRecombineStringTablesInDiagnosticInfos2Async(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Range(0, 15).Select(_ => new BrowseDescription()));
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;

            DiagnosticInfo DiagnosticInfo1() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            DiagnosticInfo DiagnosticInfo2() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4
            };

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = [.. Enumerable.Range(0, 10).Select(_ => new BrowseResult())],
                    DiagnosticInfos = [.. Enumerable.Range(0, 10).Select(_ => DiagnosticInfo1())],
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2", "String3", "String4"]
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = [.. Enumerable.Range(0, 5).Select(_ => new BrowseResult())],
                    DiagnosticInfos = [.. Enumerable.Range(0, 5).Select(_ => DiagnosticInfo2())],
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String5", "String6", "String7", "String8"]
                    }
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                0,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.ResponseHeader.StringTable.Count, Is.EqualTo(8));
            Assert.That(response.ResponseHeader.StringTable,
                Is.EquivalentTo(["String1", "String2", "String3", "String4", "String5", "String6", "String7", "String8"]));

            // Verify that the indexes in the diagnostic infos are correctly updated
            Assert.That(response.DiagnosticInfos[0].SymbolicId, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos[0].NamespaceUri, Is.EqualTo(2));
            Assert.That(response.DiagnosticInfos[0].Locale, Is.EqualTo(3));
            Assert.That(response.DiagnosticInfos[0].LocalizedText, Is.EqualTo(4));

            Assert.That(response.DiagnosticInfos[10].SymbolicId, Is.EqualTo(5));
            Assert.That(response.DiagnosticInfos[10].NamespaceUri, Is.EqualTo(6));
            Assert.That(response.DiagnosticInfos[10].Locale, Is.EqualTo(7));
            Assert.That(response.DiagnosticInfos[10].LocalizedText, Is.EqualTo(8));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseAsyncShouldRecombineStringTablesInDiagnosticInfos3Async(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodesToBrowse = new BrowseDescriptionCollection(
                Enumerable.Range(0, 15).Select(_ => new BrowseDescription()));
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerBrowse = 10;

            DiagnosticInfo DiagnosticInfo1() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 1,
                LocalizedText = 2
            };

            DiagnosticInfo DiagnosticInfo2() => new()
            {
                SymbolicId = 1,
                NamespaceUri = 2,
                Locale = 3,
                LocalizedText = 4,
                InnerDiagnosticInfo = DiagnosticInfo1()
            };

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = [.. Enumerable.Range(0, 10).Select(_ => new BrowseResult())],
                    DiagnosticInfos = [.. Enumerable.Range(0, 10).Select(_ => DiagnosticInfo1())],
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2"]
                    }
                })
                .ReturnsAsync(new BrowseResponse
                {
                    Results = [.. Enumerable.Range(0, 5).Select(_ => new BrowseResult())],
                    DiagnosticInfos = [.. Enumerable.Range(0, 5).Select(_ => DiagnosticInfo2())],
                    ResponseHeader = new ResponseHeader
                    {
                        StringTable = ["String1", "String2", "String3", "String4"]
                    }
                });

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                0,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(15));
            Assert.That(response.ResponseHeader.StringTable.Count, Is.EqualTo(6));
            Assert.That(response.ResponseHeader.StringTable,
                Is.EquivalentTo(["String1", "String2", "String1", "String2", "String3", "String4"]));

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

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseResponse())
                .Verifiable(Times.Once);

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void BrowseAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.BrowseAsync(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowse,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void BrowseAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.BrowseAsync(
                    requestHeader,
                    view,
                    requestedMaxReferencesPerNode,
                    nodesToBrowse,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task BrowseAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            const uint requestedMaxReferencesPerNode = 10u;
            var nodesToBrowse = new BrowseDescriptionCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseResponse
                {
                    Results = [new BrowseResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            BrowseResponse response = await sessionMock.BrowseAsync(
                requestHeader,
                view,
                requestedMaxReferencesPerNode,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task BrowseNextAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            const bool releaseContinuationPoints = true;

            var continuationPoints = Enumerable
                .Repeat(ByteString.Empty, 15)
                .ToArrayOf();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.ServerCapabilities.MaxBrowseContinuationPoints = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 10)])
                })
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 5)])
                });

            BrowseNextResponse response = await sessionMock.BrowseNextAsync(
                requestHeader,
                releaseContinuationPoints,
                continuationPoints,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void BrowseNextAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            const bool releaseContinuationPoints = true;

            var continuationPoints = Enumerable
                .Repeat(ByteString.Empty, 15)
                .ToArrayOf();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.ServerCapabilities.MaxBrowseContinuationPoints = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 10)])
                })
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = new BrowseResultCollection(
                        [.. Enumerable.Repeat(new BrowseResult(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.BrowseNextAsync(
                    requestHeader,
                    releaseContinuationPoints,
                    continuationPoints,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task BrowseNextAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const bool releaseContinuationPoints = true;

            var continuationPoints = new ByteStringCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseNextResponse())
                .Verifiable(Times.Once);

            BrowseNextResponse response = await sessionMock.BrowseNextAsync(requestHeader,
                releaseContinuationPoints, continuationPoints, ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void BrowseNextAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const bool releaseContinuationPoints = true;

            var continuationPoints = new ByteStringCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.BrowseNextAsync(
                    requestHeader,
                    releaseContinuationPoints,
                    continuationPoints,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void BrowseNextAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const bool releaseContinuationPoints = true;

            var continuationPoints = new ByteStringCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.BrowseNextAsync(
                    requestHeader,
                    releaseContinuationPoints,
                    continuationPoints,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task BrowseNextAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            const bool releaseContinuationPoints = true;

            var continuationPoints = new ByteStringCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is BrowseNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results = [new BrowseResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            BrowseNextResponse response = await sessionMock.BrowseNextAsync(
                requestHeader,
                releaseContinuationPoints,
                continuationPoints,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task CallAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var methodsToCall = new CallMethodRequestCollection(
                [.. Enumerable.Repeat(new CallMethodRequest(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerMethodCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResultCollection(
                    [.. Enumerable.Repeat(new CallMethodResult(), 10)])
                })
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResultCollection(
                    [.. Enumerable.Repeat(new CallMethodResult(), 5)])
                });

            CallResponse response = await sessionMock.CallAsync(
                requestHeader,
                methodsToCall,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void CallAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var methodsToCall = new CallMethodRequestCollection(
                [.. Enumerable.Repeat(new CallMethodRequest(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerMethodCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResultCollection(
                        [.. Enumerable.Repeat(new CallMethodResult(), 10)])
                })
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResultCollection(
                        [.. Enumerable.Repeat(new CallMethodResult(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.CallAsync(
                    requestHeader,
                    methodsToCall,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task CallAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var methodsToCall = new CallMethodRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CallResponse())
                .Verifiable(Times.Once);

            CallResponse response = await sessionMock.CallAsync(requestHeader,
                methodsToCall, ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CallAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var methodsToCall = new CallMethodRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.CallAsync(
                    requestHeader,
                    methodsToCall,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CallAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var methodsToCall = new CallMethodRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.CallAsync(
                    requestHeader,
                    methodsToCall,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task CallAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var methodsToCall = new CallMethodRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CallRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CallResponse
                {
                    Results = [new CallMethodResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            CallResponse response = await sessionMock.CallAsync(
                requestHeader,
                methodsToCall,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task CancelAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const uint requestHandle = 1u;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CancelRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CancelResponse())
                .Verifiable(Times.Once);

            CancelResponse response = await sessionMock.CancelAsync(requestHeader,
                requestHandle, ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CancelAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const uint requestHandle = 1u;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.CancelAsync(
                    requestHeader,
                    requestHandle,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CancelAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const uint requestHandle = 1u;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CancelRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.CancelAsync(
                    requestHeader,
                    requestHandle,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task CloseSessionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const bool deleteSubscriptions = true;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CloseSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CloseSessionResponse())
                .Verifiable(Times.Once);

            CloseSessionResponse response = await sessionMock.CloseSessionAsync(requestHeader,
                deleteSubscriptions, ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CloseSessionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const bool deleteSubscriptions = true;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.CloseSessionAsync(
                    requestHeader,
                    deleteSubscriptions,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CloseSessionAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const bool deleteSubscriptions = true;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CloseSessionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.CloseSessionAsync(
                    requestHeader,
                    deleteSubscriptions,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task CreateMonitoredItemsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection(
                [.. Enumerable.Repeat(new MonitoredItemCreateRequest(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = new MonitoredItemCreateResultCollection(
                        [.. Enumerable.Repeat(new MonitoredItemCreateResult(), 10)])
                })
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = new MonitoredItemCreateResultCollection(
                        [.. Enumerable.Repeat(new MonitoredItemCreateResult(), 5)])
                });

            CreateMonitoredItemsResponse response = await sessionMock.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToCreate,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void CreateMonitoredItemsAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;

            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection(
                [.. Enumerable.Repeat(new MonitoredItemCreateRequest(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = new MonitoredItemCreateResultCollection(
                        [.. Enumerable.Repeat(new MonitoredItemCreateResult(), 10)])
                })
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = new MonitoredItemCreateResultCollection(
                        [.. Enumerable.Repeat(new MonitoredItemCreateResult(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.CreateMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task CreateMonitoredItemsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateMonitoredItemsResponse())
                .Verifiable(Times.Once);

            CreateMonitoredItemsResponse response = await sessionMock.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToCreate,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CreateMonitoredItemsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.CreateMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CreateMonitoredItemsAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.CreateMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task CreateMonitoredItemsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToCreate = new MonitoredItemCreateRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = [new MonitoredItemCreateResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            CreateMonitoredItemsResponse response = await sessionMock.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToCreate,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task CreateSubscriptionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const bool publishingEnabled = true;
            const byte priority = 1;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateSubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new CreateSubscriptionResponse())
                .Verifiable(Times.Once);

            CreateSubscriptionResponse response = await sessionMock.CreateSubscriptionAsync(
                requestHeader,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                publishingEnabled,
                priority,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CreateSubscriptionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const bool publishingEnabled = true;
            const byte priority = 1;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.CreateSubscriptionAsync(
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void CreateSubscriptionAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const bool publishingEnabled = true;
            const byte priority = 1;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is CreateSubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.CreateSubscriptionAsync(
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task DeleteMonitoredItemsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection([.. Enumerable.Repeat(1u, 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 5)])
                });

            DeleteMonitoredItemsResponse response = await sessionMock.DeleteMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                monitoredItemIds,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void DeleteMonitoredItemsAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection([.. Enumerable.Repeat(1u, 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Bad, 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.DeleteMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    monitoredItemIds,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task DeleteMonitoredItemsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteMonitoredItemsResponse())
                .Verifiable(Times.Once);

            DeleteMonitoredItemsResponse response = await sessionMock.DeleteMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                monitoredItemIds,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void DeleteMonitoredItemsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.DeleteMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    monitoredItemIds,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void DeleteMonitoredItemsAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            var monitoredItemIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.DeleteMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    monitoredItemIds,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task DeleteNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var nodesToDelete = new DeleteNodesItemCollection(
                [.. Enumerable.Repeat(new DeleteNodesItem(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerNodeManagement = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 5)])
                });

            DeleteNodesResponse response = await sessionMock.DeleteNodesAsync(
                requestHeader,
                nodesToDelete,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void DeleteNodesAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var nodesToDelete = new DeleteNodesItemCollection(
                [.. Enumerable.Repeat(new DeleteNodesItem(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerNodeManagement = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Bad, 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.DeleteNodesAsync(
                    requestHeader,
                    nodesToDelete,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task DeleteNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var nodesToDelete = new DeleteNodesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteNodesResponse())
                .Verifiable(Times.Once);

            DeleteNodesResponse response = await sessionMock.DeleteNodesAsync(
                requestHeader,
                nodesToDelete,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void DeleteNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var nodesToDelete = new DeleteNodesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.DeleteNodesAsync(
                    requestHeader,
                    nodesToDelete,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void DeleteNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrows
            (RequestHeader requestHeader)
        {
            var nodesToDelete = new DeleteNodesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.DeleteNodesAsync(
                    requestHeader,
                    nodesToDelete,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task DeleteNodesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var nodesToDelete = new DeleteNodesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteNodesResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            DeleteNodesResponse response = await sessionMock.DeleteNodesAsync(
                requestHeader,
                nodesToDelete,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task DeleteReferencesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var referencesToDelete = new DeleteReferencesItemCollection(
                [.. Enumerable.Repeat(new DeleteReferencesItem(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerNodeManagement = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 5)])
                });

            DeleteReferencesResponse response = await sessionMock.DeleteReferencesAsync(
                requestHeader,
                referencesToDelete,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void DeleteReferencesAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var referencesToDelete = new DeleteReferencesItemCollection(
                [.. Enumerable.Repeat(new DeleteReferencesItem(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerNodeManagement = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Bad, 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.DeleteReferencesAsync(
                    requestHeader,
                    referencesToDelete,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task DeleteReferencesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var referencesToDelete = new DeleteReferencesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteReferencesResponse())
                .Verifiable(Times.Once);

            DeleteReferencesResponse response = await sessionMock.DeleteReferencesAsync(
                requestHeader,
                referencesToDelete,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void DeleteReferencesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var referencesToDelete = new DeleteReferencesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.DeleteReferencesAsync(
                    requestHeader,
                    referencesToDelete,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void DeleteReferencesAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var referencesToDelete = new DeleteReferencesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.DeleteReferencesAsync(
                    requestHeader,
                    referencesToDelete,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task DeleteReferencesAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var referencesToDelete = new DeleteReferencesItemCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteReferencesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteReferencesResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            DeleteReferencesResponse response = await sessionMock.DeleteReferencesAsync(
                requestHeader,
                referencesToDelete,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task DeleteSubscriptionsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var subscriptionIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new DeleteSubscriptionsResponse())
                .Verifiable(Times.Once);

            DeleteSubscriptionsResponse response = await sessionMock.DeleteSubscriptionsAsync(
                requestHeader,
                subscriptionIds,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void DeleteSubscriptionsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var subscriptionIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.DeleteSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void DeleteSubscriptionsAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var subscriptionIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is DeleteSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.DeleteSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task HistoryReadAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection(
                [.. Enumerable.Repeat(new HistoryReadValueId(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerHistoryReadData = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = new HistoryReadResultCollection(
                    [.. Enumerable.Repeat(new HistoryReadResult(), 10)])
                })
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = new HistoryReadResultCollection(
                    [.. Enumerable.Repeat(new HistoryReadResult(), 5)])
                });

            HistoryReadResponse response = await sessionMock.HistoryReadAsync(
                requestHeader,
                historyReadDetails,
                timestampsToReturn,
                releaseContinuationPoints,
                nodesToRead,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void HistoryReadAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var historyReadDetails = new ExtensionObject(new ReadEventDetails());
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection(
                [.. Enumerable.Repeat(new HistoryReadValueId(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerHistoryReadEvents = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = new HistoryReadResultCollection(
                        [.. Enumerable.Repeat(new HistoryReadResult(), 10)])
                })
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = new HistoryReadResultCollection(
                        [.. Enumerable.Repeat(new HistoryReadResult(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.HistoryReadAsync(
                    requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task HistoryReadAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryReadResponse())
                .Verifiable(Times.Once);

            HistoryReadResponse response = await sessionMock.HistoryReadAsync(requestHeader,
                historyReadDetails, timestampsToReturn, releaseContinuationPoints,
                nodesToRead, ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void HistoryReadAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.HistoryReadAsync(
                    requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void HistoryReadAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.HistoryReadAsync(
                    requestHeader,
                    historyReadDetails,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodesToRead,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task HistoryReadAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var historyReadDetails = new ExtensionObject();
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            const bool releaseContinuationPoints = true;
            var nodesToRead = new HistoryReadValueIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryReadResponse
                {
                    Results = [new HistoryReadResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            HistoryReadResponse response = await sessionMock.HistoryReadAsync(requestHeader, historyReadDetails,
                timestampsToReturn, releaseContinuationPoints, nodesToRead, ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task HistoryUpdateAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var historyUpdateDetails = new ExtensionObjectCollection(
                [.. Enumerable.Repeat(new ExtensionObject(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerHistoryUpdateData = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = new HistoryUpdateResultCollection(
                        [.. Enumerable.Repeat(new HistoryUpdateResult(), 10)])
                })
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = new HistoryUpdateResultCollection(
                        [.. Enumerable.Repeat(new HistoryUpdateResult(), 5)])
                });

            HistoryUpdateResponse response = await sessionMock.HistoryUpdateAsync(
                requestHeader,
                historyUpdateDetails,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void HistoryUpdateAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var historyUpdateDetails = new ExtensionObjectCollection(
                [.. Enumerable.Repeat(new ExtensionObject(new UpdateEventDetails()), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerHistoryUpdateEvents = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = new HistoryUpdateResultCollection([.. Enumerable.Repeat(new HistoryUpdateResult(), 10)])
                })
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = new HistoryUpdateResultCollection([.. Enumerable.Repeat(new HistoryUpdateResult(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.HistoryUpdateAsync(
                    requestHeader,
                    historyUpdateDetails,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task HistoryUpdateAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var historyUpdateDetails = new ExtensionObjectCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryUpdateResponse())
                .Verifiable(Times.Once);

            HistoryUpdateResponse response = await sessionMock.HistoryUpdateAsync(
                requestHeader,
                historyUpdateDetails,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void HistoryUpdateAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var historyUpdateDetails = new ExtensionObjectCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.HistoryUpdateAsync(
                    requestHeader,
                    historyUpdateDetails,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void HistoryUpdateAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var historyUpdateDetails = new ExtensionObjectCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.HistoryUpdateAsync(
                    requestHeader,
                    historyUpdateDetails,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task HistoryUpdateAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var historyUpdateDetails = new ExtensionObjectCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is HistoryUpdateRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new HistoryUpdateResponse
                {
                    Results = [new HistoryUpdateResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            HistoryUpdateResponse response = await sessionMock.HistoryUpdateAsync(
                requestHeader,
                historyUpdateDetails,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task ModifyMonitoredItemsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection(
                [.. Enumerable.Repeat(new MonitoredItemModifyRequest(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = new MonitoredItemModifyResultCollection(
                        [.. Enumerable.Repeat(new MonitoredItemModifyResult(), 10)])
                })
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = new MonitoredItemModifyResultCollection(
                        [.. Enumerable.Repeat(new MonitoredItemModifyResult(), 5)])
                });

            ModifyMonitoredItemsResponse response = await sessionMock.ModifyMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToModify,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void ModifyMonitoredItemsAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection(
                [.. Enumerable.Repeat(new MonitoredItemModifyRequest(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = new MonitoredItemModifyResultCollection(
                        [.. Enumerable.Repeat(new MonitoredItemModifyResult(), 10)])
                })
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = new MonitoredItemModifyResultCollection(
                        [.. Enumerable.Repeat(new MonitoredItemModifyResult(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.ModifyMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task ModifyMonitoredItemsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifyMonitoredItemsResponse())
                .Verifiable(Times.Once);

            ModifyMonitoredItemsResponse response = await sessionMock.ModifyMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToModify,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void ModifyMonitoredItemsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.ModifyMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void ModifyMonitoredItemsAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.ModifyMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task ModifyMonitoredItemsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var itemsToModify = new MonitoredItemModifyRequestCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifyMonitoredItemsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results = [new MonitoredItemModifyResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            ModifyMonitoredItemsResponse response = await sessionMock.ModifyMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                timestampsToReturn,
                itemsToModify,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task ModifySubscriptionAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const byte priority = 1;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifySubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ModifySubscriptionResponse())
                .Verifiable(Times.Once);

            ModifySubscriptionResponse response = await sessionMock.ModifySubscriptionAsync(
                requestHeader,
                subscriptionId,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void ModifySubscriptionAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const byte priority = 1;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(async () => await sessionMock.ModifySubscriptionAsync(
                requestHeader,
                subscriptionId,
                requestedPublishingInterval,
                requestedLifetimeCount,
                requestedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void ModifySubscriptionAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const double requestedPublishingInterval = 1000.0;
            const uint requestedLifetimeCount = 10u;
            const uint requestedMaxKeepAliveCount = 5u;
            const uint maxNotificationsPerPublish = 100u;
            const byte priority = 1;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ModifySubscriptionRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.ModifySubscriptionAsync(
                    requestHeader,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task PublishAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var subscriptionAcknowledgements = new SubscriptionAcknowledgementCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is PublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new PublishResponse())
                .Verifiable(Times.Once);

            PublishResponse response = await sessionMock.PublishAsync(
                requestHeader,
                subscriptionAcknowledgements,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void PublishAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var subscriptionAcknowledgements = new SubscriptionAcknowledgementCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.PublishAsync(
                    requestHeader,
                    subscriptionAcknowledgements,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void PublishAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var subscriptionAcknowledgements = new SubscriptionAcknowledgementCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is PublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.PublishAsync(
                    requestHeader,
                    subscriptionAcknowledgements,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task QueryFirstAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodeTypes = new NodeTypeDescriptionCollection();
            var filter = new ContentFilter();
            const uint maxDataSetsToReturn = 10u;
            const uint maxReferencesToReturn = 10u;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryFirstRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new QueryFirstResponse())
                .Verifiable(Times.Once);

            QueryFirstResponse response = await sessionMock.QueryFirstAsync(
                requestHeader,
                view,
                nodeTypes,
                filter,
                maxDataSetsToReturn,
                maxReferencesToReturn,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void QueryFirstAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodeTypes = new NodeTypeDescriptionCollection();
            var filter = new ContentFilter();
            const uint maxDataSetsToReturn = 10u;
            const uint maxReferencesToReturn = 10u;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.QueryFirstAsync(
                    requestHeader,
                    view,
                    nodeTypes,
                    filter,
                    maxDataSetsToReturn,
                    maxReferencesToReturn,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void QueryFirstAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var view = new ViewDescription();
            var nodeTypes = new NodeTypeDescriptionCollection();
            var filter = new ContentFilter();
            const uint maxDataSetsToReturn = 10u;
            const uint maxReferencesToReturn = 10u;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryFirstRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.QueryFirstAsync(
                    requestHeader,
                    view,
                    nodeTypes,
                    filter,
                    maxDataSetsToReturn,
                    maxReferencesToReturn,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task QueryNextAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const bool releaseContinuationPoint = true;
            ByteString continuationPoint = [];
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new QueryNextResponse())
                .Verifiable(Times.Once);

            QueryNextResponse response = await sessionMock.QueryNextAsync(
                requestHeader,
                releaseContinuationPoint,
                continuationPoint,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void QueryNextAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const bool releaseContinuationPoint = true;
            ByteString continuationPoint = [];
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.QueryNextAsync(
                    requestHeader,
                    releaseContinuationPoint,
                    continuationPoint,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void QueryNextAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const bool releaseContinuationPoint = true;
            ByteString continuationPoint = [];
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is QueryNextRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.QueryNextAsync(
                    requestHeader,
                    releaseContinuationPoint,
                    continuationPoint,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task ReadAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection(
                [.. Enumerable.Repeat(new ReadValueId(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerRead = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(
                    [.. Enumerable.Repeat(new DataValue(), 10)])
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(
                    [.. Enumerable.Repeat(new DataValue(), 5)])
                });

            ReadResponse response = await sessionMock.ReadAsync(
                requestHeader,
                maxAge,
                timestampsToReturn,
                nodesToRead,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void ReadAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection(
                [.. Enumerable.Repeat(new ReadValueId(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerRead = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(
                        [.. Enumerable.Repeat(new DataValue(), 10)])
                })
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValueCollection(
                        [.. Enumerable.Repeat(new DataValue(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(async () => await sessionMock.ReadAsync(
                requestHeader,
                maxAge,
                timestampsToReturn,
                nodesToRead,
                ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task ReadAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ReadResponse())
                .Verifiable(Times.Once);

            ReadResponse response = await sessionMock.ReadAsync(
                requestHeader,
                maxAge,
                timestampsToReturn,
                nodesToRead,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void ReadAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.ReadAsync(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    nodesToRead,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void ReadAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.ReadAsync(
                    requestHeader,
                    maxAge,
                    timestampsToReturn,
                    nodesToRead,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task ReadAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            const double maxAge = 1000.0;
            const TimestampsToReturn timestampsToReturn = TimestampsToReturn.Both;
            var nodesToRead = new ReadValueIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [new DataValue { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            ReadResponse response = await sessionMock.ReadAsync(
                requestHeader,
                maxAge,
                timestampsToReturn,
                nodesToRead,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task RegisterNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var nodesToRegister = new NodeIdCollection([.. Enumerable.Repeat(new NodeId(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerRegisterNodes = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = new NodeIdCollection([.. Enumerable.Repeat(new NodeId(), 10)])
                })
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = new NodeIdCollection([.. Enumerable.Repeat(new NodeId(), 5)])
                });

            RegisterNodesResponse response = await sessionMock.RegisterNodesAsync(
                requestHeader,
                nodesToRegister,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void RegisterNodesAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var nodesToRegister = new NodeIdCollection([.. Enumerable.Repeat(new NodeId(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerRegisterNodes = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = new NodeIdCollection([.. Enumerable.Repeat(new NodeId(), 10)])
                })
                .ReturnsAsync(new RegisterNodesResponse
                {
                    RegisteredNodeIds = new NodeIdCollection([.. Enumerable.Repeat(new NodeId(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.RegisterNodesAsync(
                    requestHeader,
                    nodesToRegister,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task RegisterNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var nodesToRegister = new NodeIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new RegisterNodesResponse())
                .Verifiable(Times.Once);

            RegisterNodesResponse response = await sessionMock.RegisterNodesAsync(
                requestHeader, nodesToRegister, ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void RegisterNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var nodesToRegister = new NodeIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.RegisterNodesAsync(
                    requestHeader,
                    nodesToRegister,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void RegisterNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var nodesToRegister = new NodeIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RegisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.RegisterNodesAsync(
                    requestHeader,
                    nodesToRegister,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task RepublishAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const uint retransmitSequenceNumber = 1u;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RepublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new RepublishResponse())
                .Verifiable(Times.Once);

            RepublishResponse response = await sessionMock.RepublishAsync(
                requestHeader,
                subscriptionId,
                retransmitSequenceNumber,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void RepublishAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const uint retransmitSequenceNumber = 1u;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.RepublishAsync(
                    requestHeader,
                    subscriptionId,
                    retransmitSequenceNumber,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void RepublishAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const uint retransmitSequenceNumber = 1u;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is RepublishRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.RepublishAsync(
                    requestHeader,
                    subscriptionId,
                    retransmitSequenceNumber,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task SetMonitoringModeAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection([.. Enumerable.Repeat(1u, 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = new StatusCodeCollection(
                    [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = new StatusCodeCollection(
                    [.. Enumerable.Repeat(StatusCodes.Good, 5)])
                });

            SetMonitoringModeResponse response = await sessionMock.SetMonitoringModeAsync(
                requestHeader,
                subscriptionId,
                monitoringMode,
                monitoredItemIds,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void SetMonitoringModeAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection([.. Enumerable.Repeat(1u, 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Bad, 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.SetMonitoringModeAsync(
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task SetMonitoringModeAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetMonitoringModeResponse())
                .Verifiable(Times.Once);

            SetMonitoringModeResponse response = await sessionMock.SetMonitoringModeAsync(
                requestHeader,
                subscriptionId,
                monitoringMode,
                monitoredItemIds,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void SetMonitoringModeAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.SetMonitoringModeAsync(
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void SetMonitoringModeAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.SetMonitoringModeAsync(
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task SetMonitoringModeAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const MonitoringMode monitoringMode = MonitoringMode.Reporting;
            var monitoredItemIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetMonitoringModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            SetMonitoringModeResponse response = await sessionMock.SetMonitoringModeAsync(
                requestHeader,
                subscriptionId,
                monitoringMode,
                monitoredItemIds,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task SetPublishingModeAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const bool publishingEnabled = true;
            var subscriptionIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetPublishingModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetPublishingModeResponse())
                .Verifiable(Times.Once);

            SetPublishingModeResponse response = await sessionMock.SetPublishingModeAsync(
                requestHeader,
                publishingEnabled,
                subscriptionIds,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void SetPublishingModeAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const bool publishingEnabled = true;
            var subscriptionIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.SetPublishingModeAsync(
                    requestHeader,
                    publishingEnabled,
                    subscriptionIds,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void SetPublishingModeAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const bool publishingEnabled = true;
            var subscriptionIds = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetPublishingModeRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.SetPublishingModeAsync(
                    requestHeader,
                    publishingEnabled,
                    subscriptionIds,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task SetTriggeringAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection([.. Enumerable.Repeat(1u, 15)]);
            var linksToRemove = new UInt32Collection([.. Enumerable.Repeat(1u, 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 5)]),
                    RemoveResults = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 5)])
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    RemoveResults = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                });

            SetTriggeringResponse response = await sessionMock.SetTriggeringAsync(
                requestHeader,
                subscriptionId,
                triggeringItemId,
                linksToAdd,
                linksToRemove,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.AddResults.Count, Is.EqualTo(15));
            Assert.That(response.RemoveResults.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(3));
        }

        [Theory]
        public void SetTriggeringAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection([.. Enumerable.Repeat(1u, 15)]);
            var linksToRemove = new UInt32Collection([.. Enumerable.Repeat(1u, 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.SetTriggeringAsync(
                    requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task SetTriggeringAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection();
            var linksToRemove = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new SetTriggeringResponse())
                .Verifiable(Times.Once);

            SetTriggeringResponse response = await sessionMock.SetTriggeringAsync(
                requestHeader,
                subscriptionId,
                triggeringItemId,
                linksToAdd,
                linksToRemove,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void SetTriggeringAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection();
            var linksToRemove = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.SetTriggeringAsync(
                    requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void SetTriggeringAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection();
            var linksToRemove = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.SetTriggeringAsync(
                    requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task SetTriggeringAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;
            var linksToAdd = new UInt32Collection();
            var linksToRemove = new UInt32Collection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            SetTriggeringResponse response = await sessionMock.SetTriggeringAsync(
                requestHeader,
                subscriptionId,
                triggeringItemId,
                linksToAdd,
                linksToRemove,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.AddResults.Count, Is.EqualTo(1));
            Assert.That(response.AddDiagnosticInfos.Count, Is.EqualTo(1));
            Assert.That(response.RemoveResults.Count, Is.EqualTo(1));
            Assert.That(response.RemoveDiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task TransferSubscriptionsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var subscriptionIds = new UInt32Collection();
            const bool sendInitialValues = true;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TransferSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TransferSubscriptionsResponse())
                .Verifiable(Times.Once);

            TransferSubscriptionsResponse response = await sessionMock.TransferSubscriptionsAsync(
                requestHeader,
                subscriptionIds,
                sendInitialValues,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void TransferSubscriptionsAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var subscriptionIds = new UInt32Collection();
            const bool sendInitialValues = true;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.TransferSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    sendInitialValues,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void TransferSubscriptionsAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var subscriptionIds = new UInt32Collection();
            const bool sendInitialValues = true;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TransferSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.TransferSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    sendInitialValues,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task TransferSubscriptionsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var subscriptionIds = new UInt32Collection();
            const bool sendInitialValues = true;
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TransferSubscriptionsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TransferSubscriptionsResponse
                {
                    Results = [new TransferResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            TransferSubscriptionsResponse response = await sessionMock.TransferSubscriptionsAsync(
                requestHeader,
                subscriptionIds,
                sendInitialValues,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var browsePaths = new BrowsePathCollection([.. Enumerable.Repeat(new BrowsePath(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResultCollection(
                        [.. Enumerable.Repeat(new BrowsePathResult(), 10)])
                })
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResultCollection(
                        [.. Enumerable.Repeat(new BrowsePathResult(), 5)])
                });

            TranslateBrowsePathsToNodeIdsResponse response = await sessionMock.TranslateBrowsePathsToNodeIdsAsync(
                requestHeader,
                browsePaths,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));
            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void TranslateBrowsePathsToNodeIdsAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var browsePaths = new BrowsePathCollection([.. Enumerable.Repeat(new BrowsePath(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResultCollection(
                        [.. Enumerable.Repeat(new BrowsePathResult(), 10)])
                })
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = new BrowsePathResultCollection(
                        [.. Enumerable.Repeat(new BrowsePathResult(), 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.TranslateBrowsePathsToNodeIdsAsync(
                    requestHeader,
                    browsePaths,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var browsePaths = new BrowsePathCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse())
                .Verifiable(Times.Once);

            TranslateBrowsePathsToNodeIdsResponse response = await sessionMock.TranslateBrowsePathsToNodeIdsAsync(
                requestHeader,
                browsePaths,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
        }

        [Theory]
        public async Task TranslateBrowsePathsToNodeIdsAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var browsePaths = new BrowsePathCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is TranslateBrowsePathsToNodeIdsRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results = [new BrowsePathResult { StatusCode = StatusCodes.Good }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            TranslateBrowsePathsToNodeIdsResponse response = await sessionMock.TranslateBrowsePathsToNodeIdsAsync(
                requestHeader,
                browsePaths,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }

        [Theory]
        public async Task UnregisterNodesAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var nodesToUnregister = new NodeIdCollection([.. Enumerable.Repeat(new NodeId(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerRegisterNodes = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UnregisterNodesResponse())
                .ReturnsAsync(new UnregisterNodesResponse());

            UnregisterNodesResponse response = await sessionMock.UnregisterNodesAsync(
                requestHeader,
                nodesToUnregister,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void UnregisterNodesAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var nodesToUnregister = new NodeIdCollection([.. Enumerable.Repeat(new NodeId(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerRegisterNodes = 10;

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.UnregisterNodesAsync(
                    requestHeader,
                    nodesToUnregister,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task UnregisterNodesAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var nodesToUnregister = new NodeIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new UnregisterNodesResponse())
                .Verifiable(Times.Once);

            UnregisterNodesResponse response = await sessionMock.UnregisterNodesAsync(
                requestHeader,
                nodesToUnregister,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void UnregisterNodesAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var nodesToUnregister = new NodeIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.UnregisterNodesAsync(
                    requestHeader,
                    nodesToUnregister,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void UnregisterNodesAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var nodesToUnregister = new NodeIdCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is UnregisterNodesRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.UnregisterNodesAsync(
                    requestHeader,
                    nodesToUnregister,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task WriteAsyncShouldBatchRequestsWhenExceedingOperationLimitsAsync(
            RequestHeader requestHeader)
        {
            var nodesToWrite = new WriteValueCollection([.. Enumerable.Repeat(new WriteValue(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerWrite = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse
                {
                    Results = new StatusCodeCollection(
                    [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new WriteResponse
                {
                    Results = new StatusCodeCollection(
                    [.. Enumerable.Repeat(StatusCodes.Good, 5)])
                });

            WriteResponse response = await sessionMock.WriteAsync(
                requestHeader,
                nodesToWrite,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(15));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public void WriteAsyncShouldHandleBatchingWhenSecondOperationFails(
            RequestHeader requestHeader)
        {
            var nodesToWrite = new WriteValueCollection(
                [.. Enumerable.Repeat(new WriteValue(), 15)]);
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.OperationLimits.MaxNodesPerWrite = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WriteResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Good, 10)])
                })
                .ReturnsAsync(new WriteResponse
                {
                    Results = new StatusCodeCollection(
                        [.. Enumerable.Repeat(StatusCodes.Bad, 5)]),
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    }
                });

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.WriteAsync(
                    requestHeader,
                    nodesToWrite,
                    ct).ConfigureAwait(false));

            sessionMock.Channel
                .Verify(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
        }

        [Theory]
        public async Task WriteAsyncShouldSimplyCallBaseMethodWhenNoLimitsSetAsync(
            RequestHeader requestHeader)
        {
            var nodesToWrite = new WriteValueCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new WriteResponse())
                .Verifiable(Times.Once);

            WriteResponse response = await sessionMock.WriteAsync(requestHeader,
                nodesToWrite, ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(requestHeader?.RequestHandle ?? 1, Is.Not.EqualTo(0));
            Assert.That(
                requestHeader?.Timestamp ?? DateTime.UtcNow,
                Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromSeconds(1)));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public void WriteAsyncShouldThrowExceptionWhenResponseContainsBadStatusCode(
            RequestHeader requestHeader)
        {
            var nodesToWrite = new WriteValueCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
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

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await sessionMock.WriteAsync(
                    requestHeader,
                    nodesToWrite,
                    ct).ConfigureAwait(false));

            sessionMock.Channel.Verify();
        }

        [Theory]
        public void WriteAsyncShouldThrowExceptionWhenSendRequestAsyncThrows(
            RequestHeader requestHeader)
        {
            var nodesToWrite = new WriteValueCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ThrowsAsync(new IOException("Test exception"))
                .Verifiable(Times.Once);

            IOException ioex = Assert.ThrowsAsync<IOException>(
                async () => await sessionMock.WriteAsync(
                    requestHeader,
                    nodesToWrite,
                    ct).ConfigureAwait(false));

            Assert.That(ioex.Message, Is.EqualTo("Test exception"));
            sessionMock.Channel.Verify();
        }

        [Theory]
        public async Task WriteAsyncShouldValidateResponseAndHandleDiagnosticInfoAsync(
            RequestHeader requestHeader)
        {
            var nodesToWrite = new WriteValueCollection();
            CancellationToken ct = CancellationToken.None;
            var sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is WriteRequest),
                    It.Is<CancellationToken>(t => t == ct)))
                .ReturnsAsync(new WriteResponse
                {
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = [new DiagnosticInfo()]
                })
                .Verifiable(Times.Once);

            WriteResponse response = await sessionMock.WriteAsync(
                requestHeader,
                nodesToWrite,
                ct).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
        }
    }
}
