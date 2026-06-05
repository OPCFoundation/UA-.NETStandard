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

#nullable enable

// CA2000: integration-test disposables are released by helper cleanup paths.
// CA2007: NUnit test lifecycle methods are invoked by the framework.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2000, CA2007, CA2016

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Channels.Stress.Tests.Helpers;
using Opc.Ua.Client;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Channels.Stress.Tests.Integration
{
    /// <summary>
    /// L2 failover lease-swap tests for managed channel sharing.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class FailoverLeaseSwapTests : IntegrationTestBase
    {
        [Test]
        [CancelAfter(180_000)]
        public async Task FailoverWithKeyChangeSwapsLeaseRefcountsAsync(CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager();
            using var metrics = new MetricsCollector();
            ConfiguredEndpoint endpointA = await GetEndpointAsync(SecurityPolicies.None)
                .ConfigureAwait(false);
            ConfiguredEndpoint endpointB = await GetEndpointAsync(SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            var sessions = new ManagedSessionType?[5];

            try
            {
                for (int ii = 0; ii < sessions.Length; ii++)
                {
                    sessions[ii] = await ConnectManagedSessionAsync(
                        endpointA,
                        manager,
                        nameof(FailoverWithKeyChangeSwapsLeaseRefcountsAsync) + ii,
                        ct).ConfigureAwait(false);
                }

                ManagedChannelKey keyA = GetManagedChannel(sessions[0]!).Key;
                foreach (ManagedSessionType? session in sessions)
                {
                    Assert.That(GetManagedChannel(session!).Key, Is.EqualTo(keyA));
                }

                await AssertRefcountAsync(manager, keyA, 5, ct).ConfigureAwait(false);
                double openCountBeforeFailover = CountChannelOpenMeasurements(
                    metrics,
                    endpointA.Description.EndpointUrl!);

                await RecreateInPlaceAsync(sessions[0]!, endpointB, ct).ConfigureAwait(false);
                await RecreateInPlaceAsync(sessions[1]!, endpointB, ct).ConfigureAwait(false);

                ManagedChannelKey keyB = GetManagedChannel(sessions[0]!).Key;
                Assert.That(keyB, Is.Not.EqualTo(keyA));
                Assert.That(GetManagedChannel(sessions[1]!).Key, Is.EqualTo(keyB));

                await AssertRefcountAsync(manager, keyA, 3, ct).ConfigureAwait(false);
                await AssertRefcountAsync(manager, keyB, 2, ct).ConfigureAwait(false);

                for (int ii = 0; ii < sessions.Length; ii++)
                {
                    await AssertReadServerStatusAsync(sessions[ii]!, ct).ConfigureAwait(false);
                }

                Assert.That(
                    await WaitForAsync(
                        () => CountChannelOpenMeasurements(metrics, endpointB.Description.EndpointUrl!) ==
                            openCountBeforeFailover + 1,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Failover to endpoint B must open exactly one additional shared channel.");
            }
            finally
            {
                for (int ii = sessions.Length - 1; ii >= 0; ii--)
                {
                    await CloseAndDisposeAsync(sessions[ii]).ConfigureAwait(false);
                }
            }
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task FailoverToEndpointWithExistingSessionReusesLeaseAsync(CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager();
            ConfiguredEndpoint endpointA = await GetEndpointAsync(SecurityPolicies.None)
                .ConfigureAwait(false);
            ConfiguredEndpoint endpointB = await GetEndpointAsync(SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            var endpointASessions = new ManagedSessionType?[3];
            ManagedSessionType? endpointBSession = null;

            try
            {
                for (int ii = 0; ii < endpointASessions.Length; ii++)
                {
                    endpointASessions[ii] = await ConnectManagedSessionAsync(
                        endpointA,
                        manager,
                        nameof(FailoverToEndpointWithExistingSessionReusesLeaseAsync) + "A" + ii,
                        ct).ConfigureAwait(false);
                }

                endpointBSession = await ConnectManagedSessionAsync(
                    endpointB,
                    manager,
                    nameof(FailoverToEndpointWithExistingSessionReusesLeaseAsync) + "B",
                    ct).ConfigureAwait(false);

                ManagedChannelKey keyA = GetManagedChannel(endpointASessions[0]!).Key;
                ManagedChannelKey keyB = GetManagedChannel(endpointBSession).Key;
                Assert.That(keyB, Is.Not.EqualTo(keyA));
                await AssertRefcountAsync(manager, keyA, 3, ct).ConfigureAwait(false);
                await AssertRefcountAsync(manager, keyB, 1, ct).ConfigureAwait(false);

                await RecreateInPlaceAsync(endpointASessions[0]!, endpointB, ct).ConfigureAwait(false);
                await RecreateInPlaceAsync(endpointASessions[1]!, endpointB, ct).ConfigureAwait(false);

                Assert.That(GetManagedChannel(endpointASessions[0]!).Key, Is.EqualTo(keyB));
                Assert.That(GetManagedChannel(endpointASessions[1]!).Key, Is.EqualTo(keyB));
                Assert.That(GetManagedChannel(endpointASessions[2]!).Key, Is.EqualTo(keyA));

                await AssertRefcountAsync(manager, keyA, 1, ct).ConfigureAwait(false);
                await AssertRefcountAsync(manager, keyB, 3, ct).ConfigureAwait(false);

                foreach (ManagedSessionType? session in endpointASessions)
                {
                    await AssertReadServerStatusAsync(session!, ct).ConfigureAwait(false);
                }
                await AssertReadServerStatusAsync(endpointBSession, ct).ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAsync(endpointBSession).ConfigureAwait(false);
                for (int ii = endpointASessions.Length - 1; ii >= 0; ii--)
                {
                    await CloseAndDisposeAsync(endpointASessions[ii]).ConfigureAwait(false);
                }
            }
        }

        private static async Task AssertRefcountAsync(
            ClientChannelManager manager,
            ManagedChannelKey key,
            int expectedRefcount,
            CancellationToken ct)
        {
            Assert.That(
                await WaitForQuiescence.EntryRefcountReachesAsync(
                    manager,
                    key,
                    expectedRefcount,
                    DefaultWait,
                    ct).ConfigureAwait(false),
                Is.True,
                $"Expected refcount {expectedRefcount} for {key.EndpointUrl}.");
            ManagedChannelDiagnostic diagnostic = GetDiagnostic(manager, key);
            Assert.That(diagnostic.Refcount, Is.EqualTo(expectedRefcount));
            Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
        }

        private static double CountChannelOpenMeasurements(
            MetricsCollector metrics,
            string endpointUrl)
        {
            return metrics.Measurements
                .Where(measurement => measurement.Name == "opcua.channel.open" &&
                    HasTag(measurement.Tags, "endpoint", endpointUrl) &&
                    HasTag(measurement.Tags, "reverse", false))
                .Sum(measurement => measurement.Value);
        }

        private static bool HasTag(
            TagList tags,
            string key,
            string value)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == key && string.Equals(tag.Value as string, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTag(
            TagList tags,
            string key,
            bool value)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == key && tag.Value is bool tagValue && tagValue == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task RecreateInPlaceAsync(
            ManagedSessionType session,
            ConfiguredEndpoint endpoint,
            CancellationToken ct)
        {
            Session innerSession = GetInnerSession(session);
            object? result = s_recreateInPlaceMethod.Invoke(
                innerSession,
                [endpoint, null, null, ct]);
            Assert.That(
                result,
                Is.AssignableTo<Task>(),
                "Session.RecreateInPlaceAsync reflection call must return a Task.");
            await ((Task)result!).ConfigureAwait(false);
        }

        private static readonly MethodInfo s_recreateInPlaceMethod =
            typeof(Session).GetMethod(
                "RecreateInPlaceAsync",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                [
                    typeof(ConfiguredEndpoint),
                    typeof(ITransportWaitingConnection),
                    typeof(ITransportChannel),
                    typeof(CancellationToken)
                ]) ??
            throw new InvalidOperationException("Session.RecreateInPlaceAsync reflection hook was not found.");
    }
}
