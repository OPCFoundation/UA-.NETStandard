// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Security-behaviour tests for the Ethernet transport: the unsecured
    /// (<c>SecurityMode=None</c>) connection warning.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.3", Summary = "Ethernet transport security warning")]
    public sealed class EthSecurityTests
    {
        [Test]
        public async Task OpenWithUnsecuredGroupLogsWarning()
        {
            var provider = new CapturingLoggerProvider();
            ITelemetryContext telemetry = DefaultTelemetry.Create(b => b.AddProvider(provider));
            PubSubConnectionDataType connection =
                EthTestHelpers.NewConnection("opc.eth://01-00-5E-00-00-01", "Unsecured");
            connection.WriterGroups =
                [new WriterGroupDataType { SecurityMode = MessageSecurityMode.None }];

            await OpenAndCloseAsync(connection, telemetry).ConfigureAwait(false);

            Assert.That(
                provider.Entries.Any(e =>
                    e.Level == LogLevel.Warning &&
                    e.Message.Contains("SecurityMode=None", StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public async Task OpenWithSecuredGroupDoesNotWarn()
        {
            var provider = new CapturingLoggerProvider();
            ITelemetryContext telemetry = DefaultTelemetry.Create(b => b.AddProvider(provider));
            PubSubConnectionDataType connection =
                EthTestHelpers.NewConnection("opc.eth://01-00-5E-00-00-01", "Secured");
            connection.WriterGroups =
                [new WriterGroupDataType { SecurityMode = MessageSecurityMode.SignAndEncrypt }];

            await OpenAndCloseAsync(connection, telemetry).ConfigureAwait(false);

            Assert.That(
                provider.Entries.Any(e =>
                    e.Level == LogLevel.Warning &&
                    e.Message.Contains("SecurityMode=None", StringComparison.Ordinal)),
                Is.False);
        }

        private static async Task OpenAndCloseAsync(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry)
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            EthEndpoint endpoint = EthEndpointParser.Parse(connection.Address
                .TryGetValue(out NetworkAddressUrlDataType? address) &&
                address is not null
                    ? address.Url!
                    : "opc.eth://01-00-5E-00-00-01");
            IEthernetFrameChannel channel = factory.Create(
                EthTestHelpers.LoopbackParameters(), telemetry, TimeProvider.System);
            await using var transport = new EthernetDatagramTransport(
                connection,
                endpoint,
                PubSubTransportDirection.Send,
                channel,
                EthTestHelpers.LoopbackOptions(),
                telemetry,
                TimeProvider.System);

            await transport.OpenAsync().ConfigureAwait(false);
            await transport.CloseAsync().ConfigureAwait(false);
        }

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = [];

            public ILogger CreateLogger(string categoryName)
            {
                return new CapturingLogger(Entries);
            }

            public void Dispose()
            {
            }

            private sealed class CapturingLogger : ILogger
            {
                private readonly List<(LogLevel Level, string Message)> m_entries;

                public CapturingLogger(List<(LogLevel Level, string Message)> entries)
                {
                    m_entries = entries;
                }

                public IDisposable BeginScope<TState>(TState state)
                    where TState : notnull
                {
                    return NullScope.Instance;
                }

                public bool IsEnabled(LogLevel logLevel)
                {
                    return true;
                }

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    lock (m_entries)
                    {
                        m_entries.Add((logLevel, formatter(state, exception)));
                    }
                }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
