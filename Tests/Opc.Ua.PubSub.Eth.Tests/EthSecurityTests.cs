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
                    e.Level == LogLevel.Warning
                    && e.Message.Contains("SecurityMode=None", StringComparison.Ordinal)),
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
                    e.Level == LogLevel.Warning
                    && e.Message.Contains("SecurityMode=None", StringComparison.Ordinal)),
                Is.False);
        }

        private static async Task OpenAndCloseAsync(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry)
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            EthEndpoint endpoint = EthEndpointParser.Parse(connection.Address
                .TryGetValue(out NetworkAddressUrlDataType? address) && address is not null
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
