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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.PlasticsRubber.Client;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    /// <summary>
    /// Verifies real diagnostics methods and generated records rather than synthetic event-shaped values.
    /// </summary>
    [TestFixture]
    public sealed class DiagnosticsBindingTests
    {
        /// <summary>
        /// Actual completion produces the standard typed step and end events with the interface as their source.
        /// </summary>
        [Test]
        public async Task CompletionPublishesGeneratedEventsOverUaAsync()
        {
            await using var fixture = new CommonRuntimeServerFixture();
            var provider = new DiagnosticsProvider();
            DiagnosticsBinding? binding = null;
            await fixture.StartAsync(async server =>
            {
                DiagnosticsState state = server.Manager.SystemContext.CreateInstanceOfDiagnosticsType(
                    server.Root, new QualifiedName("Diagnostics", server.Root.NodeId.NamespaceIndex));
                binding = await DiagnosticsBinding.CreateAsync(
                    server.Manager, state, server.Root, provider, AllowAccess()).ConfigureAwait(false);
                await server.RegisterAsync(state, binding).ConfigureAwait(false);
            }).ConfigureAwait(false);
            var common = new PlasticsRubberClient(fixture.Session, fixture.Session.MessageContext.Telemetry);
            DiagnosticsTypeClient commands = common.Diagnostics(binding!.Node.NodeId);
            await commands.RunDiagnosticsAsync().ConfigureAwait(false);
            Assert.That(binding.Node.Status!.Value, Is.EqualTo(DiagnosticsStatusEnumeration.ACTIVE_OK));

            Assert.That(fixture.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            await using var streaming = new StreamingSubscription(manager!, new SubscriptionOptions
            {
                PublishingInterval = TimeSpan.FromMilliseconds(50)
            });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            IAsyncEnumerator<DiagnosisStepEndEventTypeRecord> steps = common.ObserveDiagnosticsStepsAsync(
                fixture.Root.NodeId, streaming, cancellationToken: deadline.Token).GetAsyncEnumerator(deadline.Token);
            IAsyncEnumerator<DiagnosisEndEventTypeRecord> ends = common.ObserveDiagnosticsCompletionsAsync(
                fixture.Root.NodeId, streaming, cancellationToken: deadline.Token).GetAsyncEnumerator(deadline.Token);
            try
            {
                Task<bool> step = steps.MoveNextAsync().AsTask();
                Task<bool> end = ends.MoveNextAsync().AsTask();
                while (manager!.Items.Sum(subscription => subscription.MonitoredItems.Items.Count(
                    item => item.Created && ServiceResult.IsGood(item.Error))) < 2)
                {
                    await Task.Delay(10, deadline.Token).ConfigureAwait(false);
                }
                var completed = new DiagnosticsSnapshot(
                    2, DiagnosticsStatusEnumeration.COMPLETE_ERROR_DETECTED, Time(2),
                    [new DiagnosticsStep(fixture.Root.NodeId, false, new LocalizedText("Sensor failed."))],
                    new LocalizedText("One diagnosis step failed."));
                await binding.PublishAsync(completed, deadline.Token).ConfigureAwait(false);
                await Task.WhenAll(step, end).WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(steps.Current.Result, Is.False);
                    Assert.That(steps.Current.InputNode, Is.EqualTo(fixture.Root.NodeId));
                    Assert.That(steps.Current.SourceNode, Is.EqualTo(fixture.Root.NodeId));
                    Assert.That(steps.Current.Time, Is.EqualTo(completed.SourceTimestamp));
                    Assert.That(ends.Current.Status, Is.EqualTo(DiagnosticsStatusEnumeration.COMPLETE_ERROR_DETECTED));
                    Assert.That(ends.Current.SourceNode, Is.EqualTo(fixture.Root.NodeId));
                    Assert.That(
                        fixture.Manager.FindPredefinedNode<BaseObjectTypeState>(ends.Current.EventType)!.IsAbstract,
                        Is.False);
                });
                await binding.PublishAsync(completed, deadline.Token).ConfigureAwait(false);
                Assert.That(binding.Node.Status.Value,
                    Is.EqualTo(DiagnosticsStatusEnumeration.COMPLETE_ERROR_DETECTED));
                await commands.StopDiagnosticsAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(binding.Node.Status.Value, Is.EqualTo(DiagnosticsStatusEnumeration.OFF));
            }
            finally
            {
                deadline.Cancel();
                await steps.DisposeAsync().ConfigureAwait(false);
                await ends.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A provider cannot acknowledge Run while reporting an inactive diagnostic state.
        /// </summary>
        [Test]
        public async Task InactiveRunResultIsRejectedAsync()
        {
            await using var fixture = new CommonRuntimeServerFixture();
            var provider = new Mock<IDiagnosticsProvider>(MockBehavior.Strict);
            provider.Setup(value => value.RunAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DiagnosticsSnapshot>(
                    new DiagnosticsSnapshot(1, DiagnosticsStatusEnumeration.OFF, Time(1), [], LocalizedText.Null)));
            DiagnosticsBinding? binding = null;
            await fixture.StartAsync(async server =>
            {
                DiagnosticsState state = server.Manager.SystemContext.CreateInstanceOfDiagnosticsType(
                    server.Root, new QualifiedName("Diagnostics", server.Root.NodeId.NamespaceIndex));
                binding = await DiagnosticsBinding.CreateAsync(
                    server.Manager, state, server.Root, provider.Object, AllowAccess()).ConfigureAwait(false);
                await server.RegisterAsync(state, binding).ConfigureAwait(false);
            }).ConfigureAwait(false);
            var client = new DiagnosticsTypeClient(
                fixture.Session, binding!.Node.NodeId, fixture.Session.MessageContext.Telemetry);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RunDiagnosticsAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(binding.Node.Status!.Value, Is.EqualTo(DiagnosticsStatusEnumeration.OFF));
        }

        /// <summary>
        /// Returns a test-only policy that explicitly permits operations.
        /// </summary>
        private static IPlasticsRubberAccessPolicy AllowAccess()
        {
            var policy = new Mock<IPlasticsRubberAccessPolicy>();
            policy.Setup(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            return policy.Object;
        }

        /// <summary>
        /// Returns deterministic source times.
        /// </summary>
        private static DateTimeUtc Time(int second)
        {
            return (DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, second, DateTimeKind.Utc);
        }

        /// <summary>
        /// Accepts explicit start/stop requests without running a background simulation.
        /// </summary>
        private sealed class DiagnosticsProvider : IDiagnosticsProvider
        {
            /// <inheritdoc/>
            public ValueTask<DiagnosticsSnapshot> RunAsync(
                PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<DiagnosticsSnapshot>(new DiagnosticsSnapshot(
                    1, DiagnosticsStatusEnumeration.ACTIVE_OK, Time(1), [], new LocalizedText("Running.")));
            }

            /// <inheritdoc/>
            public ValueTask<DiagnosticsSnapshot> StopAsync(
                PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<DiagnosticsSnapshot>(new DiagnosticsSnapshot(
                    3, DiagnosticsStatusEnumeration.OFF, Time(3), [], new LocalizedText("Stopped.")));
            }
        }
    }
}
