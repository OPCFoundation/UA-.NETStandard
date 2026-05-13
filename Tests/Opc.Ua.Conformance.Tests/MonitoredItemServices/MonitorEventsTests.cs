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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Conformance.Tests.MonitoredItemServices
{
    /// <summary>
    /// compliance tests for Monitor Events conformance unit.
    /// Tests event monitoring with EventFilter on the Server object.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitorEvents")]
    public class MonitorEventsTests : TestFixture
    {
        [SetUp]
        public async Task SetUp()
        {
            m_subscriptionId = await CreateSetupSubscriptionAsync(
                publishingInterval: 1000, requestedLifetimeCount: 100,
                requestedMaxKeepAliveCount: 10).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDown()
        {
            if (m_subscriptionId > 0)
            {
                try
                {
                    await Session.DeleteSubscriptionsAsync(
                        null,
                        new uint[] { m_subscriptionId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Subscription may already be deleted
                }
                m_subscriptionId = 0;
            }
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Events")]
        [Property("Tag", "001")]
        public async Task MonitorServerEventsWithSeverityFilterAsync()
        {
            EventFilter eventFilter = CreateBasicEventFilter();
            MonitoredItemCreateRequest item = CreateEventItemRequest(eventFilter, 1);

            CreateMonitoredItemsResponse resp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(1));

            StatusCode sc = resp.Results[0].StatusCode;
            if (sc == StatusCodes.BadMonitoredItemFilterUnsupported ||
                sc == StatusCodes.BadFilterNotAllowed ||
                sc == StatusCodes.BadNodeIdUnknown ||
                sc == StatusCodes.BadAttributeIdInvalid)
            {
                Assert.Fail($"Server does not support event monitoring: {sc}");
            }

            Assert.That(StatusCode.IsGood(sc), Is.True,
                $"Event monitored item creation failed: {sc}");

            await Task.Delay(1000).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Events")]
        [Property("Tag", "002")]
        public async Task MonitorEventsWithSelectClauseDisplayNameAsync()
        {
            var eventFilter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.EventId)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.SourceName)],
                        AttributeId = Attributes.Value
                    }
                ],
                WhereClause = new ContentFilter()
            };

            MonitoredItemCreateRequest item = CreateEventItemRequest(eventFilter, 1);

            CreateMonitoredItemsResponse resp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(1));

            StatusCode sc = resp.Results[0].StatusCode;
            if (sc == StatusCodes.BadMonitoredItemFilterUnsupported ||
                sc == StatusCodes.BadFilterNotAllowed ||
                sc == StatusCodes.BadNodeIdUnknown ||
                sc == StatusCodes.BadAttributeIdInvalid)
            {
                Assert.Fail($"Server does not support event monitoring: {sc}");
            }

            Assert.That(StatusCode.IsGood(sc), Is.True,
                $"Event monitored item creation failed: {sc}");
        }

        [Test]
        [Property("ConformanceUnit", "Monitor Events")]
        [Property("Tag", "003")]
        public async Task MonitorEventsWithWhereClauseSeverityAsync()
        {
            var eventFilter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.EventId)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.Severity)],
                        AttributeId = Attributes.Value
                    }
                ],
                WhereClause = new ContentFilter
                {
                    Elements =
                    [
                        new ContentFilterElement
                        {
                            FilterOperator = FilterOperator.GreaterThanOrEqual,
                            FilterOperands = new ExtensionObject[]
                            {
                                new(new SimpleAttributeOperand
                                {
                                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                    BrowsePath = [new QualifiedName(BrowseNames.Severity)],
                                    AttributeId = Attributes.Value
                                }),
                                new(new LiteralOperand
                                {
                                    Value = Variant.From((ushort)1)
                                })
                            }.ToArrayOf()
                        }
                    ]
                }
            };

            MonitoredItemCreateRequest item = CreateEventItemRequest(eventFilter, 1);

            CreateMonitoredItemsResponse resp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(1));

            StatusCode sc = resp.Results[0].StatusCode;
            if (sc == StatusCodes.BadMonitoredItemFilterUnsupported ||
                sc == StatusCodes.BadFilterNotAllowed ||
                sc == StatusCodes.BadNodeIdUnknown ||
                sc == StatusCodes.BadAttributeIdInvalid)
            {
                Assert.Fail($"Server does not support event monitoring: {sc}");
            }

            Assert.That(StatusCode.IsGood(sc), Is.True,
                $"Event monitored item with where clause failed: {sc}");
        }

        private static EventFilter CreateBasicEventFilter()
        {
            return new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.SourceName)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.Message)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.Severity)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.ReceiveTime)],
                        AttributeId = Attributes.Value
                    }
                ],
                WhereClause = new ContentFilter()
            };
        }

        private static MonitoredItemCreateRequest CreateEventItemRequest(
            EventFilter eventFilter,
            uint clientHandle)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    SamplingInterval = 0,
                    Filter = new ExtensionObject(eventFilter),
                    QueueSize = 10,
                    DiscardOldest = true
                }
            };
        }

        private uint m_subscriptionId;
    }
}
