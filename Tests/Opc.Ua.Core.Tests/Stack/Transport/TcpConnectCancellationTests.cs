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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// A TCP connect that the network never answers must end when the
    /// caller cancels, not when the operating system gives up on the
    /// connect (issue #5068). CoreClientUtils.SelectEndpointAsync and
    /// DefaultSessionFactory.CreateAsync open the channel this way.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class TcpConnectCancellationTests
    {
        /// <summary>
        /// TEST-NET-1 (RFC 5737): never assigned, so a connect to it is
        /// normally dropped by the network and stays pending.
        /// </summary>
        private static readonly Uri s_unansweredUrl = new("opc.tcp://192.0.2.1:4840");

        private static readonly TimeSpan s_cancelAfter = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan s_maxCancelLatency = TimeSpan.FromSeconds(5);

        [Test]
        [CancelAfter(30000)]
        public void SocketConnectWithCancelledTokenThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var socket = new TcpMessageSocket(
                null,
                new BufferManager("test", 8192, telemetry),
                8192,
                telemetry);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await socket.ConnectAsync(s_unansweredUrl, cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(socket.RemoteEndpoint, Is.Null);
            socket.Close();
        }

        [Test]
        [CancelAfter(30000)]
        public async Task SocketConnectPendingIsCancelledAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var socket = new TcpMessageSocket(
                null,
                new BufferManager("test", 8192, telemetry),
                8192,
                telemetry);
            using var cts = new CancellationTokenSource(s_cancelAfter);

            var stopwatch = Stopwatch.StartNew();
            Exception error = await CaptureAsync(
                () => socket.ConnectAsync(s_unansweredUrl, cts.Token)).ConfigureAwait(false);
            stopwatch.Stop();

            AssertCancelledPromptly(error, stopwatch.Elapsed);
            Assert.That(error, Is.InstanceOf<OperationCanceledException>(), error.ToString());
            Assert.That(socket.RemoteEndpoint, Is.Null);
            socket.Close();
        }

        [Test]
        [CancelAfter(30000)]
        public async Task ChannelOpenPendingConnectIsCancelledAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var channel = new UaSCUaBinaryTransportChannel(
                new TcpMessageSocketFactory(telemetry),
                telemetry)
            {
                // far beyond the test timeout: only the token may end the open.
                OperationTimeout = 120000
            };
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 120000;
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = s_unansweredUrl.ToString(),
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    TransportProfileUri = Profiles.UaTcpTransport
                },
                Configuration = configuration,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create()
            };
            using var cts = new CancellationTokenSource(s_cancelAfter);

            var stopwatch = Stopwatch.StartNew();
            Exception error = await CaptureAsync(
                () => channel.OpenAsync(s_unansweredUrl, settings, cts.Token).AsTask()).ConfigureAwait(false);
            stopwatch.Stop();

            AssertCancelledPromptly(error, stopwatch.Elapsed);
            Assert.That(error, Is.InstanceOf<OperationCanceledException>(), error.ToString());
        }

        private static async Task<Exception> CaptureAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return e;
            }
            Assert.Fail("The connect to an unassigned address succeeded.");
            return null;
        }

        private static void AssertCancelledPromptly(Exception error, TimeSpan elapsed)
        {
            if (elapsed < s_cancelAfter && error is not OperationCanceledException)
            {
                // the network rejected the connect before the cancel: nothing to observe.
                Assert.Inconclusive(
                    $"The connect failed before it could be cancelled: {error.GetType().Name} {error.Message}");
            }

            Assert.That(
                elapsed,
                Is.LessThan(s_maxCancelLatency),
                "the pending connect ignored the cancellation token");
        }
    }
}
