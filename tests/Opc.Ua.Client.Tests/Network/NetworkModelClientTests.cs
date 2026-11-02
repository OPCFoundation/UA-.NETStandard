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
using Opc.Ua.Client.Network;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Network
{
    /// <summary>
    /// Tests for Part 22 client construction and session ownership.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("NetworkModel")]
    [Parallelizable]
    public sealed class NetworkModelClientTests
    {
        /// <summary>
        /// Requires a session and telemetry before constructing the client.
        /// </summary>
        [Test]
        public void ConstructorRejectsMissingSessionAndTelemetry()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var session = new Mock<ISessionClient>().Object;

            Assert.That(
                () => new NetworkModelClient(null!, telemetry),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("session"));
            Assert.That(
                () => new NetworkModelClient(session, null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("telemetry"));
        }

        /// <summary>
        /// Retains the caller's session and telemetry without transferring ownership.
        /// </summary>
        [Test]
        public void FactoryCreatesClientBoundToCallerOwnedSession()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var session = new Mock<ISessionClient>().Object;
            var factory = new NetworkModelClientFactory(telemetry);

            NetworkModelClient client = factory.Create(session);

            Assert.That(client.Session, Is.SameAs(session));
            Assert.That(client.Telemetry, Is.SameAs(telemetry));
        }

        /// <summary>
        /// Applies the same input guards to factory and direct construction.
        /// </summary>
        [Test]
        public void FactoryAndDirectConstructionRejectMissingInputs()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var session = new Mock<ISessionClient>().Object;

            Assert.That(
                () => new NetworkModelClientFactory(null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("telemetry"));
            Assert.That(
                () => NetworkModelClientExtensions.GetNetworkModelClient(null!, telemetry),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("session"));
            Assert.That(
                () => NetworkModelClientExtensions.GetNetworkModelClient(session, null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("telemetry"));
        }

        /// <summary>
        /// Rejects unspecified targets for each generated typed proxy.
        /// </summary>
        [Test]
        public void TypedProxyGettersRejectNullNodeIds()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var client = new NetworkModelClient(new Mock<ISessionClient>().Object, telemetry);

            Assert.That(() => client.GetInterface(NodeId.Null), Throws.ArgumentException);
            Assert.That(() => client.GetMappingTable(NodeId.Null), Throws.ArgumentException);
            Assert.That(() => client.GetLldp(NodeId.Null), Throws.ArgumentException);
        }

        /// <summary>
        /// Borrows the subscription and forwards its stream, options and cancellation unchanged.
        /// </summary>
        [Test]
        public void ObservationBorrowsSubscriptionAndForwardsRequestedOptions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var client = new NetworkModelClient(new Mock<ISessionClient>().Object, telemetry);
            var subscription = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            var changes = new Mock<IAsyncEnumerable<DataValueChange>>().Object;
            NodeId variable = new("speed", 2);
            var options = new MonitoredItemOptions { SamplingInterval = TimeSpan.FromMilliseconds(100) };
            using var cancellation = new CancellationTokenSource();
            subscription.Setup(value => value.SubscribeDataChangesAsync(variable, options, cancellation.Token))
                .Returns(changes);

            IAsyncEnumerable<DataValueChange> observed = client.ObserveAsync(
                variable, subscription.Object, options, cancellation.Token);

            Assert.That(observed, Is.SameAs(changes));
            subscription.Verify(value => value.SubscribeDataChangesAsync(variable, options, cancellation.Token),
                Times.Once);
            subscription.Verify(value => value.DisposeAsync(), Times.Never);
            subscription.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Rejects missing observation dependencies and mapping keys.
        /// </summary>
        [Test]
        public async Task ObservationAndMappingMethodsRejectMissingInputs()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var client = new NetworkModelClient(new Mock<ISessionClient>().Object, telemetry);

            Assert.That(
                () => client.ObserveAsync(NodeId.Null, null!),
                Throws.ArgumentException);
            Assert.That(
                () => client.ObserveAsync(ObjectIds.NetworkInterfaces, null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("subscription"));
            await Assert.ThatAsync(
                async () => await client.AddPriorityMappingEntryAsync(
                    ObjectIds.MappingTables, null!, "label").ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("mappingUri")).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await client.DeletePriorityMappingEntryAsync(
                    ObjectIds.MappingTables, "urn:test:priority", null!).ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("priorityLabel")).ConfigureAwait(false);
        }
    }
}
