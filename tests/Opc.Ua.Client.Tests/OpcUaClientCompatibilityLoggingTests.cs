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
using System.Linq;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Logging")]
    [Parallelizable]
    public sealed class OpcUaClientCompatibilityLoggingTests
    {
        [Test]
        public void ClientEventsRetainLegacyContracts()
        {
            using var provider = new RecordingLoggerProvider();
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddProvider(provider));
            ILogger logger = telemetry.CreateLogger(ClientEventIds.LegacyCategoryName);
            var lastNotificationTime = new DateTime(2026, 7, 19, 8, 30, 0, DateTimeKind.Utc);

            logger.ClientEventSubscriptionState(
                "Publishing",
                10,
                lastNotificationTime,
                3,
                1000,
                5,
                true,
                8);
            logger.ClientEventNotification(42, "123");
            logger.ClientEventNotificationReceived(10, 11);
            logger.ClientEventPublishStart(12);
            logger.ClientEventPublishStop(12);

            RecordedLogRecord subscription = AssertRecord(
                provider,
                ClientEventIds.LegacySubscriptionStateId,
                "SubscriptionState");
            Assert.That(subscription.Properties["Context"], Is.EqualTo("Publishing"));
            Assert.That(subscription.Properties["Id"], Is.EqualTo(10u));
            Assert.That(subscription.Properties, Contains.Key("LastNotificationTime"));
            Assert.That(subscription.Message, Does.Contain("08:30:00"));
            Assert.That(subscription.Properties["CurrentPublishingInterval"], Is.EqualTo(1000d));
            Assert.That(subscription.Properties["CurrentKeepAliveCount"], Is.EqualTo(5u));
            Assert.That(subscription.Properties["CurrentPublishingEnabled"], Is.True);

            RecordedLogRecord notification = AssertRecord(
                provider,
                ClientEventIds.LegacyNotificationId,
                "Notification");
            Assert.That(notification.Properties["ClientHandle"], Is.EqualTo(42));
            Assert.That(notification.Properties["Value"], Is.EqualTo("123"));

            RecordedLogRecord received = AssertRecord(
                provider,
                ClientEventIds.LegacyNotificationReceivedId,
                "NotificationReceived");
            Assert.That(received.Properties["SubscriptionId"], Is.EqualTo(10));
            Assert.That(received.Properties["SequenceNumber"], Is.EqualTo(11));

            AssertRecord(provider, ClientEventIds.LegacyPublishStartId, "PublishStart");
            AssertRecord(provider, ClientEventIds.LegacyPublishStopId, "PublishStop");
        }

        private static RecordedLogRecord AssertRecord(
            RecordingLoggerProvider provider,
            int eventId,
            string eventName)
        {
            RecordedLogRecord record = provider.Records.Single(candidate =>
                candidate.CategoryName == ClientEventIds.LegacyCategoryName &&
                candidate.EventId.Id == eventId);
            Assert.That(record.EventId.Name, Is.EqualTo(eventName));
            Assert.That(record.LogLevel, Is.EqualTo(LogLevel.Trace));
            return record;
        }
    }
}
