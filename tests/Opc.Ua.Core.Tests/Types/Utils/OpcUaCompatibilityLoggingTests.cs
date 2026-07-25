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
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("Logging")]
    [Parallelizable]
    public sealed class OpcUaCompatibilityLoggingTests
    {
        [Test]
        public void CoreEventsRetainLegacyContracts()
        {
            using var provider = new RecordingLoggerProvider();
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddProvider(provider));
            ILogger logger = telemetry.CreateLogger(CoreEventIds.CoreCompatibilityCategory);

            logger.CoreServiceCallStart("Read", 11, 2);
            logger.CoreServiceCallStop("Read", 11, 1);
            logger.CoreServiceCallBadStop("Write", 12, 3, 0x1234);
            logger.CoreSendResponse(7, 99);

            RecordedLogRecord start = AssertRecord(
                provider,
                CoreEventIds.CoreCompatibilityCategory,
                CoreEventIds.CoreServiceCallStart,
                "ServiceCallStart",
                LogLevel.Trace);
            Assert.That(start.Properties["ServiceName"], Is.EqualTo("Read"));
            Assert.That(start.Properties["RequestHandle"], Is.EqualTo(11));
            Assert.That(start.Properties["PendingRequestCount"], Is.EqualTo(2));

            RecordedLogRecord stop = AssertRecord(
                provider,
                CoreEventIds.CoreCompatibilityCategory,
                CoreEventIds.CoreServiceCallStop,
                "ServiceCallStop",
                LogLevel.Trace);
            Assert.That(stop.Properties["PendingRequestCount"], Is.EqualTo(1));

            RecordedLogRecord badStop = AssertRecord(
                provider,
                CoreEventIds.CoreCompatibilityCategory,
                CoreEventIds.CoreServiceCallBadStop,
                "ServiceCallBadStop",
                LogLevel.Warning);
            Assert.That(badStop.Properties["PendingRequestCount"], Is.EqualTo(3));
            Assert.That(badStop.Properties["StatusCode"], Is.EqualTo(0x1234));

            RecordedLogRecord response = AssertRecord(
                provider,
                CoreEventIds.CoreCompatibilityCategory,
                CoreEventIds.CoreSendResponse,
                "SendResponse",
                LogLevel.Trace);
            Assert.That(response.Properties["ChannelId"], Is.EqualTo(7));
            Assert.That(response.Properties["RequestId"], Is.EqualTo(99));
        }

        [Test]
        public void ChannelManagerEventsRetainLegacyContracts()
        {
            using var provider = new RecordingLoggerProvider();
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddProvider(provider));
            ILogger logger = telemetry.CreateLogger(CoreEventIds.ChannelManagerCompatibilityCategory);

            logger.ChannelManagerChannelOpened("opc.tcp://server", true, 1, 2);
            logger.ChannelManagerChannelClosed("opc.tcp://server", "faulted", 0, 0);
            logger.ChannelManagerStateChanged(
                "opc.tcp://server",
                "Ready",
                "Reconnecting",
                3,
                "BadTimeout",
                "Timed out");
            logger.ChannelManagerReconnectStarted("opc.tcp://server", 0);
            logger.ChannelManagerReconnectCompleted("opc.tcp://server", 2, "success");
            logger.ChannelManagerReconnectFailed(
                "opc.tcp://server",
                4,
                "policy-exhausted",
                "BadTimeout",
                "Timed out");
            logger.ChannelManagerParticipantAttached("opc.tcp://server", "Session-1", 1, 1);
            logger.ChannelManagerParticipantDetached("opc.tcp://server", "Session-1", 0, 0);

            AssertRecord(
                provider,
                CoreEventIds.ChannelManagerCompatibilityCategory,
                CoreEventIds.ChannelManagerChannelOpened,
                "ChannelOpened",
                LogLevel.Information);
            AssertRecord(
                provider,
                CoreEventIds.ChannelManagerCompatibilityCategory,
                CoreEventIds.ChannelManagerChannelClosed,
                "ChannelClosed",
                LogLevel.Information);
            RecordedLogRecord stateChanged = AssertRecord(
                provider,
                CoreEventIds.ChannelManagerCompatibilityCategory,
                CoreEventIds.ChannelManagerStateChanged,
                "StateChanged",
                LogLevel.Information);
            Assert.That(stateChanged.Properties["StatusCode"], Is.EqualTo("BadTimeout"));
            Assert.That(stateChanged.Properties["ErrorMessage"], Is.EqualTo("Timed out"));
            AssertRecord(
                provider,
                CoreEventIds.ChannelManagerCompatibilityCategory,
                CoreEventIds.ChannelManagerReconnectStarted,
                "ReconnectStarted",
                LogLevel.Information);
            AssertRecord(
                provider,
                CoreEventIds.ChannelManagerCompatibilityCategory,
                CoreEventIds.ChannelManagerReconnectCompleted,
                "ReconnectCompleted",
                LogLevel.Information);
            RecordedLogRecord reconnectFailed = AssertRecord(
                provider,
                CoreEventIds.ChannelManagerCompatibilityCategory,
                CoreEventIds.ChannelManagerReconnectFailed,
                "ReconnectFailed",
                LogLevel.Warning);
            Assert.That(reconnectFailed.Properties["StatusCode"], Is.EqualTo("BadTimeout"));
            Assert.That(reconnectFailed.Properties["ErrorMessage"], Is.EqualTo("Timed out"));
            AssertRecord(
                provider,
                CoreEventIds.ChannelManagerCompatibilityCategory,
                CoreEventIds.ChannelManagerParticipantAttached,
                "ParticipantAttached",
                LogLevel.Information);
            AssertRecord(
                provider,
                CoreEventIds.ChannelManagerCompatibilityCategory,
                CoreEventIds.ChannelManagerParticipantDetached,
                "ParticipantDetached",
                LogLevel.Information);
        }

        private static RecordedLogRecord AssertRecord(
            RecordingLoggerProvider provider,
            string categoryName,
            int eventId,
            string eventName,
            LogLevel logLevel)
        {
            RecordedLogRecord record = provider.Records.Single(candidate =>
                candidate.CategoryName == categoryName &&
                candidate.EventId.Id == eventId);
            Assert.That(record.EventId.Name, Is.EqualTo(eventName));
            Assert.That(record.LogLevel, Is.EqualTo(logLevel));
            return record;
        }
    }
}
