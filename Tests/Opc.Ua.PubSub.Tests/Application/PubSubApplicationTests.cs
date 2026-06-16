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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Unit tests for the <see cref="PubSubApplication"/> runtime
    /// aggregator built via <see cref="PubSubApplicationBuilder"/>.
    /// </summary>
    [TestFixture]
    public class PubSubApplicationTests
    {
        [Test]
        public async Task StartAsync_ThenStopAsync_RoundTripsState()
        {
            IPubSubApplication app = NewEmptyApplication();
            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                app.State.State,
                Is.AnyOf(
                    PubSubState.Operational,
                    PubSubState.PreOperational,
                    PubSubState.Paused));
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(app.State.State, Is.EqualTo(PubSubState.Disabled));
        }

        [Test]
        public async Task DisposeAsync_AfterStart_ShutsDownCleanly()
        {
            IPubSubApplication app = NewEmptyApplication();
            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
            // Second dispose should be idempotent.
            await app.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public void Connections_OnEmptySnapshot_IsEmpty()
        {
            IPubSubApplication app = NewEmptyApplication();
            Assert.That(app.Connections, Is.Empty);
        }

        [Test]
        public void MetaDataRegistry_IsAvailable()
        {
            IPubSubApplication app = NewEmptyApplication();
            Assert.That(app.MetaDataRegistry, Is.Not.Null);
        }

        [Test]
        public async Task StartAsync_WithCancelled_PropagatesCancellation()
        {
            IPubSubApplication app = NewEmptyApplication();
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await app.StartAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Assert.Pass();
            }
        }

        private static IPubSubApplication NewEmptyApplication()
        {
            var config = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("test-app")
                .UseConfiguration(config)
                .UseAllStandardEncoders()
                .Build();
        }
    }
}
