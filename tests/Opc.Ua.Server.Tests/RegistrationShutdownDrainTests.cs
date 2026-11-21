// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that failed cancellation callbacks do not release an active discovery-registration owner.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class RegistrationShutdownDrainTests
    {
        /// <summary>
        /// Shutdown drains the active attempt before reporting cancellation failures, preserving both failures.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationFailureStillDrainsRegistrationBeforeReportingErrorsAsync(bool failRegistration)
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource();
            var callbackFailure = new InvalidOperationException("The registration cancellation callback failed.");
            var registrationFailure = new InvalidOperationException("The active registration failed.");
            using CancellationTokenRegistration callback = cancellation.Token.Register(() => throw callbackFailure);
            var registration = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            FieldInfo cancellationField = typeof(DiscoveryRegistration).GetField(
                "m_cts", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new AssertionException("The registration cancellation owner was not found.");
            FieldInfo registrationField = typeof(DiscoveryRegistration).GetField(
                "m_task", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new AssertionException("The active registration task was not found.");
            cancellationField.SetValue(server.DiscoveryRegistration, cancellation);
            registrationField.SetValue(server.DiscoveryRegistration, registration.Task);
            ValueTask stopping = server.DiscoveryRegistration.StopAsync();
            Task stop = stopping.AsTask();
            bool completedBeforeRegistration = stop.IsCompleted;
            if (failRegistration)
            {
                registration.TrySetException(registrationFailure);
            }
            else
            {
                registration.TrySetResult(true);
            }
            AggregateException? observed;
            try
            {
                observed = await Assert.ThrowsAsync<AggregateException>(
                    () => stop.WaitAsync(timeout.Token)).ConfigureAwait(false);
                if (failRegistration)
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => registration.Task).ConfigureAwait(false);
                }
            }
            finally
            {
                registrationField.SetValue(server.DiscoveryRegistration, Task.CompletedTask);
            }
            Assert.That(completedBeforeRegistration, Is.False,
                "A failed cancellation callback must not bypass the active registration drain.");
            Assert.That(observed, Is.Not.Null);
            AggregateException failures = observed!.Flatten();
            Assert.That(failures.InnerExceptions, Has.Count.EqualTo(failRegistration ? 2 : 1));
            Assert.That(failures.InnerExceptions, Has.Exactly(1).SameAs(callbackFailure));
            if (failRegistration)
            {
                Assert.That(failures.InnerExceptions, Has.Exactly(1).SameAs(registrationFailure));
            }
            Assert.That(cancellationField.GetValue(server.DiscoveryRegistration), Is.Null);
        }
    }
}
