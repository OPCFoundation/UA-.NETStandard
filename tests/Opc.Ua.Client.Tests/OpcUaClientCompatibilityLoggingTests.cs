// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            var sessionId = new NodeId(4711u);

            logger.ClientEventSubscriptionState(
                "Publishing",
                10,
                lastNotificationTime,
                3,
                1000,
                5,
                true,
                8,
                sessionId);
            logger.ClientEventNotification(42, "123");
            logger.ClientEventNotificationReceived(10, 11, sessionId);
            logger.ClientEventPublishStart(12, sessionId);
            logger.ClientEventPublishStop(12, sessionId);

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
            Assert.That(
                subscription.Properties["SessionId"]?.ToString(),
                Is.EqualTo(sessionId.ToString()));

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
            Assert.That(
                received.Properties["SessionId"]?.ToString(),
                Is.EqualTo(sessionId.ToString()));

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
