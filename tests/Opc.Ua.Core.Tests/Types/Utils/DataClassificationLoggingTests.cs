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
using System.Linq;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using Opc.Ua.Redaction;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Pins how classified log parameters behave in the logging pipelines an
    /// application can configure.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [Parallelizable(ParallelScope.All)]
    public sealed class DataClassificationLoggingTests
    {
        private const string kRemoteEndpoint = "10.0.0.7:51234";
        private const string kUser = "jdoe";
        private static readonly Uri s_endpointUrl = new("opc.tcp://plc-17.factory.local:4840/UA/Server");
        private static readonly string s_unredacted =
            $"CONNECTED via {s_endpointUrl}: {kRemoteEndpoint}, User={kUser}, ChannelId=5";

        [Test]
        public void EnableOpcUaRedactionLogsClassifiedValuesUnchanged()
        {
            using ServiceProvider provider = BuildProvider(configureRedaction: null);

            FakeLogRecord record = LogOnce(provider);

            Assert.That(record.Message, Is.EqualTo(s_unredacted));
            Assert.That(record.GetStructuredStateValue("Url"), Is.EqualTo(s_endpointUrl.ToString()));
            Assert.That(record.GetStructuredStateValue("RemoteEndpoint"), Is.EqualTo(kRemoteEndpoint));
        }

        [Test]
        public void ErasingRedactorRemovesValuesFromMessageAndState()
        {
            using ServiceProvider provider = BuildProvider(redaction => redaction
                .SetRedactor<ErasingRedactor>(OpcUaDataClassifications.NetworkAddress)
                .SetRedactor<NullRedactor>(OpcUaDataClassifications.UserIdentity));

            FakeLogRecord record = LogOnce(provider);

            Assert.That(record.Message, Is.EqualTo($"CONNECTED via : , User={kUser}, ChannelId=5"));
            Assert.That(record.GetStructuredStateValue("Url"), Is.Empty);
            Assert.That(record.GetStructuredStateValue("RemoteEndpoint"), Is.Empty);
            Assert.That(record.GetStructuredStateValue("User"), Is.EqualTo(kUser));
            Assert.That(record.GetStructuredStateValue("ChannelId"), Is.EqualTo("5"));
        }

        [Test]
        public void ClassificationsWithoutRedactorAreErasedOnceRedactionIsConfigured()
        {
            using ServiceProvider provider = BuildProvider(redaction => redaction
                .SetRedactor<NullRedactor>(OpcUaDataClassifications.NetworkAddress));

            FakeLogRecord record = LogOnce(provider);

            Assert.That(record.GetStructuredStateValue("Url"), Is.EqualTo(s_endpointUrl.ToString()));
            Assert.That(record.GetStructuredStateValue("User"), Is.Empty);
        }

        [Test]
        public void HmacRedactorKeepsValuesCorrelatable()
        {
            using ServiceProvider provider = BuildProvider(redaction => redaction.SetHmacRedactor(
                options =>
                {
                    options.Key = Convert.ToBase64String(new byte[64]);
                    options.KeyId = 1;
                },
                OpcUaDataClassifications.NetworkAddress));

            ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
            logger.ClassifiedConnected(s_endpointUrl, "10.0.0.7:51234", kUser, 1u);
            logger.ClassifiedConnected(s_endpointUrl, "10.0.0.8:51234", kUser, 2u);

            IReadOnlyList<FakeLogRecord> records = provider.GetFakeLogCollector().GetSnapshot();
            string?[] urls = [.. records.Select(r => r.GetStructuredStateValue("Url"))];
            string?[] remotes = [.. records.Select(r => r.GetStructuredStateValue("RemoteEndpoint"))];

            Assert.That(urls[0], Is.Not.Empty.And.Not.Contain("plc-17"));
            Assert.That(urls[1], Is.EqualTo(urls[0]));
            Assert.That(remotes[1], Is.Not.EqualTo(remotes[0]));
        }

        [Test]
        public void PlainLoggerFactoryDropsClassifiedValues()
        {
            var fake = new FakeLoggerProvider();
            using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddProvider(fake));

            factory.CreateLogger("Test").ClassifiedConnected(s_endpointUrl, kRemoteEndpoint, kUser, 5u);

            Assert.That(
                fake.Collector.LatestRecord.Message,
                Is.EqualTo("CONNECTED via (null): (null), User=(null), ChannelId=5"));
        }

        [Test]
        public void DefaultTelemetryLogsClassifiedValues()
        {
            var fake = new FakeLoggerProvider();
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.AddProvider(fake));
            try
            {
                telemetry.CreateLogger("Test").ClassifiedConnected(s_endpointUrl, kRemoteEndpoint, kUser, 5u);
            }
            finally
            {
                (telemetry as IDisposable)?.Dispose();
            }

            Assert.That(fake.Collector.LatestRecord.Message, Is.EqualTo(s_unredacted));
        }

        [Test]
        public void AddOpcUaAddLoggingLogsClassifiedValues()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddLogging(builder => builder.AddFakeLogging());
            using ServiceProvider provider = services.BuildServiceProvider();

            ITelemetryContext telemetry = provider.GetRequiredService<ITelemetryContext>();
            telemetry.CreateLogger("Test").ClassifiedConnected(s_endpointUrl, kRemoteEndpoint, kUser, 5u);

            Assert.That(provider.GetFakeLogCollector().LatestRecord.Message, Is.EqualTo(s_unredacted));
        }

        private static ServiceProvider BuildProvider(Action<IRedactionBuilder>? configureRedaction)
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.EnableOpcUaRedaction().AddFakeLogging());
            if (configureRedaction != null)
            {
                services.AddRedaction(configureRedaction);
            }
            return services.BuildServiceProvider();
        }

        private static FakeLogRecord LogOnce(ServiceProvider provider)
        {
            ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
            logger.ClassifiedConnected(s_endpointUrl, kRemoteEndpoint, kUser, 5u);
            return provider.GetFakeLogCollector().LatestRecord;
        }
    }

    internal static partial class DataClassificationLoggingTestsLog
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information,
            Message = "CONNECTED via {Url}: {RemoteEndpoint}, User={User}, ChannelId={ChannelId}")]
        public static partial void ClassifiedConnected(
            this ILogger logger,
            [NetworkAddress] Uri url,
            [NetworkAddress] string remoteEndpoint,
            [UserIdentity] string user,
            uint channelId);
    }
}
