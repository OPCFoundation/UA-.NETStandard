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
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Recycling.Model;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Covers the real independently hosted view and the injectable projection/control boundaries.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ProcessViewTests
    {
        /// <summary>
        /// A separate view auto-registers, projects absolute commits, and explicitly rejects unconfigured forwarding.
        /// </summary>
        [TestCase(RecyclingProcess.Collection)]
        [TestCase(RecyclingProcess.Sorting)]
        [TestCase(RecyclingProcess.Shredding)]
        [TestCase(RecyclingProcess.Washing)]
        [TestCase(RecyclingProcess.DensitySeparation)]
        [TestCase(RecyclingProcess.Drying)]
        [TestCase(RecyclingProcess.Extrusion)]
        [TestCase(RecyclingProcess.MeltFiltration)]
        [TestCase(RecyclingProcess.Pelletizing)]
        [TestCase(RecyclingProcess.Bagging)]
        public async Task IndependentViewProjectsCommittedStateAndDoesNotPretendControlsAreConfigured(
            RecyclingProcess process)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(timeout.Token)
                .ConfigureAwait(false);
            var adapter = new RecordingProcessAdapter();
            IHost host = await environment.StartViewAsync(
                process, adapter, timeout.Token).ConfigureAwait(false);
            RecyclingRuntime runtime = host.Services.GetRequiredService<RecyclingRuntime>();
            await runtime.AddressSpaceReady.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.Throws<InvalidOperationException>(() => _ = runtime.Coordinator);
            await using IAsyncEnumerator<CoordinatorNotification> commits =
                environment.Coordinator.WatchAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
            do
            {
                Assert.That(await commits.MoveNextAsync().ConfigureAwait(false), Is.True);
            }
            while (!commits.Current.Snapshot.Participants.Span.ToArray().Any(participant =>
                participant.Process == process && participant.Initialized));

            await environment.InitializeRolesAsync(timeout.Token).ConfigureAwait(false);
            await environment.Coordinator.TickAsync(timeout.Token).ConfigureAwait(false);
            CommittedSnapshot expected = environment.Coordinator.Current;
            CommittedSnapshot applied = await adapter.WaitForAsync(
                snapshot => snapshot.Revision >= expected.Revision, timeout.Token).ConfigureAwait(false);
            Assert.That(applied.Tick, Is.EqualTo(expected.Tick));
            Assert.That(applied.Parcels, Is.EqualTo(expected.Parcels));
            Assert.That(applied.SourceTimestampUtc, Is.EqualTo(expected.SourceTimestampUtc));

            string endpoint = host.Services.GetRequiredService<RecyclingHostOptions>().EndpointUrl;
            RecyclingCoordinatorClient connection = await environment.ConnectAsync("operator", timeout.Token, endpoint)
                .ConfigureAwait(false);
            ushort index = checked((ushort)connection.Session.NamespaceUris
                .GetIndex(RecyclingModelMapping.NamespaceUri));
            var view = new RecyclingProcessViewTypeClient(
                connection.Session, new NodeId(Model.Objects.ProcessView, index), environment.Telemetry);
            SnapshotDataType wire = await view.ReadSnapshotAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(wire.Tick, Is.EqualTo(expected.Tick));
            RecyclingCommand disable = CoordinatorTests.Command(
                environment.Coordinator, RecyclingCommandKind.DisableProcess, process);
            ServiceResultException unavailable = Assert.ThrowsAsync<ServiceResultException>(() =>
                view.ForwardCommandAsync(RecyclingModelMapping.ToWire(disable), timeout.Token).AsTask())!;
            Assert.That(unavailable.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(environment.Coordinator.Current.Stages.Span.ToArray()
                .Where(stage => stage.Process == process).All(static stage => stage.Enabled), Is.True);
        }

        /// <summary>
        /// The optional forwarding seam retains the real user identity and applies only coordinator commits.
        /// </summary>
        [Test]
        public async Task SuppliedForwarderUsesTheOriginatingUserCertificateAndRejectsServiceSubstitution()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(timeout.Token)
                .ConfigureAwait(false);
            RecyclingCoordinatorClient user = await environment.ConnectAsync("operator", timeout.Token)
                .ConfigureAwait(false);
            RecyclingCoordinatorClient service = await environment.ConnectAsync("view-Collection", timeout.Token)
                .ConfigureAwait(false);
            var principal = new RecyclingPrincipal(
                "operator", RecyclingAccess.Operator, RecyclingProcess.Collection,
                user.Session.SessionId.ToString(), user.CertificateSha256);
            var forwarder = new RecyclingUserCommandForwarder(
                (_, _) => new ValueTask<RecyclingCoordinatorClient>(user));
            RecyclingCommand command = CoordinatorTests.Command(
                environment.Coordinator, RecyclingCommandKind.DisableProcess, RecyclingProcess.Collection);
            RecyclingCommandOutcome outcome = await forwarder.ForwardAsync(principal, command, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(outcome.Status, Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(environment.Coordinator.Current.Stages[0].Enabled, Is.False);
            var substituted = new RecyclingUserCommandForwarder(
                (_, _) => new ValueTask<RecyclingCoordinatorClient>(service));
            ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(() =>
                substituted.ForwardAsync(principal, command, timeout.Token).AsTask())!;
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }
    }

    /// <summary>
    /// Observes the real adapter boundary using an actual signal rather than delays or a second simulator.
    /// </summary>
    internal sealed class RecordingProcessAdapter : IRecyclingProcessViewAdapter
    {
        /// <inheritdoc/>
        public bool CompanionBindingsComplete => false;

        /// <inheritdoc/>
        public ValueTask InitializeAsync(IMachineryBuildContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask ApplyAsync(CommittedSnapshot snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_updates.Writer.TryWrite(snapshot);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask MarkUnavailableAsync(DateTime sourceTimestampUtc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Awaits the actual absolute image delivered to the adapter.
        /// </summary>
        internal async Task<CommittedSnapshot> WaitForAsync(
            Predicate<CommittedSnapshot> predicate,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                CommittedSnapshot snapshot = await m_updates.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (predicate(snapshot))
                {
                    return snapshot;
                }
            }
        }

        private readonly Channel<CommittedSnapshot> m_updates =
            Channel.CreateBounded<CommittedSnapshot>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
    }
}
