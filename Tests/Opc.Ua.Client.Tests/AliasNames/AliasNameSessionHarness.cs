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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Tests;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests.AliasNames
{
    /// <summary>
    /// Scriptable in-memory mock of an OPC UA <see cref="ISession"/> for
    /// <c>AliasNameClient</c> tests. Captures every <c>Call</c> /
    /// <c>Read</c> / <c>Browse</c> request and lets the test supply a
    /// per-method response handler.
    /// </summary>
    internal sealed class AliasNameSessionHarness
    {
        public Mock<ISession> SessionMock { get; }
        public ISession Session => SessionMock.Object;
        public IServiceMessageContext MessageContext { get; }
        public List<CallMethodRequest> CallRequests { get; } = [];
        public List<ReadValueId> ReadRequests { get; } = [];
        public Func<CallMethodRequest, CallMethodResult> CallHandler { get; set; }
        public Func<ReadValueId, DataValue> ReadHandler { get; set; }

        public Func<BrowsePath, BrowsePathResult> BrowsePathHandler { get; set; }

        private AliasNameSessionHarness(
            Mock<ISession> mock,
            IServiceMessageContext messageContext)
        {
            SessionMock = mock;
            MessageContext = messageContext;
        }

        public static AliasNameSessionHarness Create()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);

            var sessionMock = new Mock<ISession>(MockBehavior.Loose);
            sessionMock.SetupGet(s => s.MessageContext).Returns(messageContext);
            sessionMock.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);

            var harness = new AliasNameSessionHarness(sessionMock, messageContext);

            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) =>
                    {
                        var results = new BrowsePathResult[paths.Count];
                        for (int i = 0; i < paths.Count; i++)
                        {
                            results[i] = harness.BrowsePathHandler != null
                                ? harness.BrowsePathHandler(paths[i])
                                : DefaultBrowsePathResult(paths[i]);
                        }
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = results.ToArrayOf(),
                                DiagnosticInfos = default
                            });
                    });

            sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, methodsToCall, _) =>
                    {
                        var results = new CallMethodResult[methodsToCall.Count];
                        for (int i = 0; i < methodsToCall.Count; i++)
                        {
                            CallMethodRequest req = methodsToCall[i];
                            harness.CallRequests.Add(req);
                            results[i] = harness.CallHandler != null
                                ? harness.CallHandler(req)
                                : DefaultCallResult();
                        }
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });

            sessionMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>,
                    CancellationToken>(
                    (_, _, _, nodes, _) =>
                    {
                        var results = new DataValue[nodes.Count];
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            ReadValueId r = nodes[i];
                            harness.ReadRequests.Add(r);
                            results[i] = harness.ReadHandler != null
                                ? harness.ReadHandler(r)
                                : new DataValue
                                {
                                    StatusCode = StatusCodes.BadNotFound
                                };
                        }
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });

            sessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>,
                    CancellationToken>(
                    (_, _, _, descriptions, _) =>
                    {
                        var results = new BrowseResult[descriptions.Count];
                        for (int i = 0; i < descriptions.Count; i++)
                        {
                            results[i] = new BrowseResult
                            {
                                StatusCode = StatusCodes.Good,
                                References = System.Array
                                    .Empty<ReferenceDescription>()
                                    .ToArrayOf()
                            };
                        }
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });

            return harness;
        }

        private static CallMethodResult DefaultCallResult()
        {
            return new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = System.Array.Empty<Variant>().ToArrayOf()
            };
        }

        /// <summary>
        /// Default browse-path resolver — returns a synthetic NodeId
        /// (browseName-named string identifier in ns=0) so that
        /// <c>AliasNameClient.ResolveMethodIdAsync</c> tests have
        /// something to cache. Tests that care about the precise NodeId
        /// should supply their own <see cref="BrowsePathHandler"/>.
        /// </summary>
        private static BrowsePathResult DefaultBrowsePathResult(BrowsePath path)
        {
            string targetName = path.RelativePath.Elements[0].TargetName.Name;
            return new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = new[]
                {
                    new BrowsePathTarget
                    {
                        TargetId = new ExpandedNodeId(targetName, 0),
                        RemainingPathIndex = uint.MaxValue
                    }
                }.ToArrayOf()
            };
        }
    }
}
