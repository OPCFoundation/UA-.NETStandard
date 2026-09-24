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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that a server bounds the chunks of incomplete messages with one
    /// budget across all its transport listeners.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class ChunkReassemblyBudgetServerTests
    {
        [Test]
        public async Task ServerSizesOneBudgetForItsListenersFromItsQuotasAsync()
        {
            var fixture = new ServerFixture<BudgetCaptureServer>(t => new BudgetCaptureServer(t));
            BudgetCaptureServer server = await fixture.StartAsync().ConfigureAwait(false);

            try
            {
                await AddListenerAsync(server, fixture.Config).ConfigureAwait(false);
                Assert.That(server.ListenerBudgets, Has.Count.GreaterThan(1));
                ChunkReassemblyBudget budget = server.ListenerBudgets[0];
                Assert.That(budget, Is.Not.Null);
                Assert.That(server.ListenerBudgets, Has.All.SameAs(budget));
                Assert.That(
                    budget.MaxBytes,
                    Is.EqualTo(ChunkReassemblyBudget.GetDefaultMaxBytes(
                        fixture.Config.TransportQuotas.MaxMessageSize)));
                Assert.That(server.ChunkReassemblyBudget, Is.Null, "the server must not claim a budget it sized.");
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ServerHandsItsListenersTheConfiguredBudgetAsync()
        {
            var configured = new ChunkReassemblyBudget(32L * 1024 * 1024);
            var fixture = new ServerFixture<BudgetCaptureServer>(
                t => new BudgetCaptureServer(t) { ChunkReassemblyBudget = configured });
            BudgetCaptureServer server = await fixture.StartAsync().ConfigureAwait(false);

            try
            {
                await AddListenerAsync(server, fixture.Config).ConfigureAwait(false);
                Assert.That(server.ListenerBudgets, Has.Count.GreaterThan(1));
                Assert.That(server.ListenerBudgets, Has.All.SameAs(configured));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static async Task AddListenerAsync(BudgetCaptureServer server, ApplicationConfiguration configuration)
        {
            var listener = new Mock<ITransportListener>();
            await server.CreateServiceHostEndpointAsync(
                new Uri("opc.tcp://localhost:4840/additional"),
                [],
                EndpointConfiguration.Create(configuration),
                listener.Object,
                Mock.Of<ICertificateValidatorEx>()).ConfigureAwait(false);

            listener.Verify(value => value.OpenAsync(
                It.IsAny<Uri>(),
                It.Is<TransportListenerSettings>(settings =>
                    settings.ChunkReassemblyBudget == server.ListenerBudgets[0]),
                It.IsAny<ITransportListenerCallback>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Records the budget each transport listener of the server is opened with.
        /// </summary>
        private sealed class BudgetCaptureServer : StandardServer
        {
            public BudgetCaptureServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public List<ChunkReassemblyBudget> ListenerBudgets { get; } = [];

            protected override void ConfigureTransportListenerSettings(
                TransportListenerSettings settings,
                Uri endpointUri)
            {
                base.ConfigureTransportListenerSettings(settings, endpointUri);
                ListenerBudgets.Add(settings.ChunkReassemblyBudget);
            }
        }
    }
}
