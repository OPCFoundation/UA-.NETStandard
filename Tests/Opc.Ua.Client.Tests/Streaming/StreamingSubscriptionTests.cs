/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.Client.Tests.Streaming
{
    /// <summary>
    /// Deterministic null-guard tests for <see cref="StreamingSubscription"/>.
    /// </summary>
    /// <remarks>
    /// Full lazy <c>EnsureSubscriptionAsync</c> / monitored-item routing /
    /// dispose coverage requires a real <c>ISubscriptionManager</c> +
    /// <c>ISubscription</c> pipeline and lives in the integration test
    /// suite. Only the cheap argument-validation guards are exercised
    /// here.
    /// </remarks>
    [TestFixture]
    [Category("Client")]
    [Category("StreamingExtensions")]
    [Parallelizable]
    public sealed class StreamingSubscriptionTests
    {
        [Test]
        public void ConstructorWithNullSubscriptionManagerThrowsArgumentNullException()
        {
            Assert.That(
                () => new StreamingSubscription(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeDataChangesAsyncWithNullNodeIdThrowsArgumentNullException()
        {
            await using var sub = new StreamingSubscription(NewStubManager());

            // NodeId.Null is the canonical "null" value of the INullable
            // struct. The production guard checks nodeId.IsNull and throws
            // synchronously before any subscription-manager interaction.
            Assert.That(
                () => sub.SubscribeDataChangesAsync(NodeId.Null),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeDataChangesAsyncWithNullNodeIdsListThrowsArgumentNullException()
        {
            await using var sub = new StreamingSubscription(NewStubManager());

            Assert.That(
                () => sub.SubscribeDataChangesAsync((IReadOnlyList<NodeId>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeEventsAsyncWithNullNotifierIdThrowsArgumentNullException()
        {
            await using var sub = new StreamingSubscription(NewStubManager());

            Assert.That(
                () => sub.SubscribeEventsAsync(NodeId.Null, new EventFilter()),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeEventsAsyncWithNullFilterThrowsArgumentNullException()
        {
            await using var sub = new StreamingSubscription(NewStubManager());

            Assert.That(
                () => sub.SubscribeEventsAsync(ObjectIds.Server, null!),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// Stub subscription manager — its methods are never invoked
        /// because the argument guards trip synchronously before
        /// <c>EnsureSubscriptionAsync</c> is entered. Using a loose
        /// <see cref="Mock{T}"/> avoids hand-implementing every
        /// member of the <see cref="ISubscriptionManager"/> surface.
        /// </summary>
        private static ISubscriptionManager NewStubManager()
        {
            return new Mock<ISubscriptionManager>(MockBehavior.Loose).Object;
        }
    }
}
