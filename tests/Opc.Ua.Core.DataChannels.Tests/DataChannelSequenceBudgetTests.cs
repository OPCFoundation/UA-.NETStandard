/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    public sealed class SequenceNumberBudgetTests
    {
        [Test]
        public void ObservedServiceChunksContributeToRenewalAndStallBudget()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var budget = new SequenceNumberBudget(clock, capacity: 5);

            budget.ObserveConsumed(4);
            clock.Advance(TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(budget.Consumed, Is.EqualTo(4));
                Assert.That(budget.Remaining, Is.EqualTo(1));
                Assert.That(budget.ShouldRenew, Is.True);
                Assert.That(budget.MustStall, Is.False);
            });

            Assert.That(budget.TryConsume(), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(budget.Remaining, Is.Zero);
                Assert.That(budget.MustStall, Is.True);
                Assert.That(budget.TryConsume(), Is.False);
            });
        }

        [Test]
        public void TokenActivationResetsObservedAndConsumedBudget()
        {
            var budget = new SequenceNumberBudget(capacity: 3);

            budget.ObserveSequenceNumber(2);
            Assert.That(budget.MustStall, Is.True);

            budget.OnTokenActivated();

            Assert.Multiple(() =>
            {
                Assert.That(budget.Consumed, Is.Zero);
                Assert.That(budget.Remaining, Is.EqualTo(3));
                Assert.That(budget.TryConsume(), Is.True);
            });
        }
    }
}
