// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    [TestFixture]
    [Category("Client")]
    public sealed class RetryBudgetTests
    {
        [Test]
        public async Task BudgetExhaustsAfterMaxTimeAsync()
        {
            var timeProvider = new FakeTimeProvider();
            var budget = new RetryBudget(TimeSpan.FromSeconds(1), timeProvider);

            Assert.That(budget.TryConsume(out TimeSpan remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(TimeSpan.FromSeconds(1)));

            timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();

            Assert.That(budget.IsExhausted, Is.True);
            Assert.That(budget.TryConsume(out remaining), Is.False);
            Assert.That(remaining, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void TryConsumeReturnsRemainingTime()
        {
            var timeProvider = new FakeTimeProvider();
            var budget = new RetryBudget(TimeSpan.FromSeconds(10), timeProvider);

            Assert.That(budget.TryConsume(out TimeSpan remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(TimeSpan.FromSeconds(10)));

            timeProvider.Advance(TimeSpan.FromSeconds(4));

            Assert.That(budget.TryConsume(out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(TimeSpan.FromSeconds(6)));
        }

        [Test]
        public void ResetClearsElapsed()
        {
            var timeProvider = new FakeTimeProvider();
            var budget = new RetryBudget(TimeSpan.FromSeconds(10), timeProvider);

            Assert.That(budget.TryConsume(out _), Is.True);
            timeProvider.Advance(TimeSpan.FromSeconds(5));

            Assert.That(budget.ElapsedSinceFirstAttempt, Is.EqualTo(TimeSpan.FromSeconds(5)));

            budget.Reset();

            Assert.That(budget.ElapsedSinceFirstAttempt, Is.EqualTo(TimeSpan.Zero));
            Assert.That(budget.TryConsume(out TimeSpan remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(TimeSpan.FromSeconds(10)));
        }
    }
}
