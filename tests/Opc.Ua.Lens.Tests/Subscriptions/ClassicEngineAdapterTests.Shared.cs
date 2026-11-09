/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Diagnostics;
using UaLens.Subscriptions;
using UaLens.Telemetry;

namespace UaLens.Tests.Subscriptions;

public sealed partial class ClassicEngineAdapterTests
{

    internal sealed class ClassicProtocol : IDisposable
    {
        public ClassicProtocol()
        {
            var channel = new Mock<ITransportChannel>();
            ServiceMessageContext messages = ServiceMessageContext.Create(Telemetry);
            channel.SetupGet(value => value.MessageContext).Returns(messages);
            channel.Setup(value => value.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns((IServiceRequest request, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Requests.Add(request);
                    IServiceResponse response = request switch
                    {
                        CreateSubscriptionRequest => new CreateSubscriptionResponse
                        {
                            SubscriptionId = 500,
                            RevisedPublishingInterval = 20,
                            RevisedMaxKeepAliveCount = 5,
                            RevisedLifetimeCount = 1000
                        },
                        SetPublishingModeRequest => new SetPublishingModeResponse { Results = [StatusCodes.Good] },
                        CreateMonitoredItemsRequest => new CreateMonitoredItemsResponse
                        {
                            Results = [new MonitoredItemCreateResult
                            {
                                StatusCode = StatusCodes.Good, MonitoredItemId = 700,
                                RevisedSamplingInterval = 9, RevisedQueueSize = 8
                            }]
                        },
                        ModifyMonitoredItemsRequest => new ModifyMonitoredItemsResponse
                        {
                            Results = [new MonitoredItemModifyResult
                            {
                                StatusCode = StatusCodes.Good, RevisedSamplingInterval = 13, RevisedQueueSize = 15
                            }]
                        },
                        SetMonitoringModeRequest => new SetMonitoringModeResponse { Results = [StatusCodes.Good] },
                        DeleteMonitoredItemsRequest => new DeleteMonitoredItemsResponse
                        {
                            Results = [StatusCodes.Good]
                        },
                        DeleteSubscriptionsRequest => new DeleteSubscriptionsResponse { Results = [StatusCodes.Good] },
                        _ => throw new AssertionException("Unexpected classic request: " + request.GetType().Name)
                    };
                    response.ResponseHeader.ServiceResult = StatusCodes.Good;
                    response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
                    return ValueTask.FromResult(response);
                });
            var configuration = new ApplicationConfiguration(Telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://classic.example.test:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            }, new EndpointConfiguration());
            Session = new Session(channel.Object, configuration, endpoint,
                engineFactory: ClassicSubscriptionEngineFactory.Instance);
        }

        public AppTelemetryContext Telemetry { get; } = new(new LogRingBuffer(32));
        public Session Session { get; }
        public List<IServiceRequest> Requests { get; } = [];
        public void Dispose() => Session.Dispose();
    }
}
