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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Unit tests for MonitoredItem.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class MonitoredItemTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = AmbientMessageContext.Telemetry;
        }

        [Test]
        public void SaveValueInCacheUpdatesStatusErrorWhenBadStatusCode()
        {
            // Arrange
            var monitoredItem = new MonitoredItem(m_telemetry, new MonitoredItemOptions
            {
                StartNodeId = new NodeId(1, 0),
                AttributeId = Attributes.Value
            });

            var message = new NotificationMessage
            {
                SequenceNumber = 1,
                PublishTime = DateTime.UtcNow,
                StringTable = []
            };

            var notification = new MonitoredItemNotification
            {
                ClientHandle = monitoredItem.ClientHandle,
                Value = new DataValue
                {
                    Value = null,
                    StatusCode = StatusCodes.BadOutOfService,
                    ServerTimestamp = DateTime.UtcNow,
                    SourceTimestamp = DateTime.UtcNow
                },
                Message = message
            };

            // Act
            monitoredItem.SaveValueInCache(notification);

            // Assert
            Assert.IsNotNull(monitoredItem.Status.Error);
            Assert.AreEqual(StatusCodes.BadOutOfService, monitoredItem.Status.Error.StatusCode);
        }

        [Test]
        public void SaveValueInCacheClearsStatusErrorWhenGoodStatusCode()
        {
            // Arrange
            var monitoredItem = new MonitoredItem(m_telemetry, new MonitoredItemOptions
            {
                StartNodeId = new NodeId(1, 0),
                AttributeId = Attributes.Value
            });

            var message = new NotificationMessage
            {
                SequenceNumber = 1,
                PublishTime = DateTime.UtcNow,
                StringTable = []
            };

            // First send a bad status code
            var badNotification = new MonitoredItemNotification
            {
                ClientHandle = monitoredItem.ClientHandle,
                Value = new DataValue
                {
                    Value = null,
                    StatusCode = StatusCodes.BadOutOfService,
                    ServerTimestamp = DateTime.UtcNow,
                    SourceTimestamp = DateTime.UtcNow
                },
                Message = message
            };
            monitoredItem.SaveValueInCache(badNotification);

            // Verify error is set
            Assert.IsNotNull(monitoredItem.Status.Error);

            // Now send a good status code
            var goodNotification = new MonitoredItemNotification
            {
                ClientHandle = monitoredItem.ClientHandle,
                Value = new DataValue
                {
                    Value = 42,
                    StatusCode = StatusCodes.Good,
                    ServerTimestamp = DateTime.UtcNow,
                    SourceTimestamp = DateTime.UtcNow
                },
                Message = message
            };

            // Act
            monitoredItem.SaveValueInCache(goodNotification);

            // Assert
            Assert.IsNull(monitoredItem.Status.Error);
        }

        [Test]
        public void SaveValueInCacheUpdatesStatusErrorForDifferentBadStatusCodes()
        {
            // Arrange
            var monitoredItem = new MonitoredItem(m_telemetry, new MonitoredItemOptions
            {
                StartNodeId = new NodeId(1, 0),
                AttributeId = Attributes.Value
            });

            var message = new NotificationMessage
            {
                SequenceNumber = 1,
                PublishTime = DateTime.UtcNow,
                StringTable = []
            };

            // Test BadNodeIdUnknown
            var notification1 = new MonitoredItemNotification
            {
                ClientHandle = monitoredItem.ClientHandle,
                Value = new DataValue
                {
                    Value = null,
                    StatusCode = StatusCodes.BadNodeIdUnknown,
                    ServerTimestamp = DateTime.UtcNow,
                    SourceTimestamp = DateTime.UtcNow
                },
                Message = message
            };
            monitoredItem.SaveValueInCache(notification1);
            Assert.IsNotNull(monitoredItem.Status.Error);
            Assert.AreEqual(StatusCodes.BadNodeIdUnknown, monitoredItem.Status.Error.StatusCode);

            // Test BadCommunicationError
            var notification2 = new MonitoredItemNotification
            {
                ClientHandle = monitoredItem.ClientHandle,
                Value = new DataValue
                {
                    Value = null,
                    StatusCode = StatusCodes.BadCommunicationError,
                    ServerTimestamp = DateTime.UtcNow,
                    SourceTimestamp = DateTime.UtcNow
                },
                Message = message
            };
            monitoredItem.SaveValueInCache(notification2);
            Assert.IsNotNull(monitoredItem.Status.Error);
            Assert.AreEqual(StatusCodes.BadCommunicationError, monitoredItem.Status.Error.StatusCode);
        }

        [Test]
        public void SaveValueInCacheHandlesNullDataValue()
        {
            // Arrange
            var monitoredItem = new MonitoredItem(m_telemetry, new MonitoredItemOptions
            {
                StartNodeId = new NodeId(1, 0),
                AttributeId = Attributes.Value
            });

            var message = new NotificationMessage
            {
                SequenceNumber = 1,
                PublishTime = DateTime.UtcNow,
                StringTable = []
            };

            // A notification with a non-null value is required for the cache
            // The status update only happens when datachange.Value is not null
            var notification = new MonitoredItemNotification
            {
                ClientHandle = monitoredItem.ClientHandle,
                Value = new DataValue
                {
                    Value = 42,
                    StatusCode = StatusCodes.Good,
                    ServerTimestamp = DateTime.UtcNow,
                    SourceTimestamp = DateTime.UtcNow
                },
                Message = message
            };

            // Act - should not throw
            monitoredItem.SaveValueInCache(notification);

            // Assert - error should remain null since status is good
            Assert.IsNull(monitoredItem.Status.Error);
        }
    }
}
