/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class ServerFixtureTelemetryTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ActivityListenerSamplesOnlyResolvedSource(bool disableActivityLogging)
        {
            using var unrelatedSource = new ActivitySource(nameof(ServerFixtureTelemetryTests));
            var fixture = new ServerFixture<StandardServer>(static telemetry => new StandardServer(telemetry));

            try
            {
                fixture.StartActivityListenerInternal(disableActivityLogging);
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                ActivitySource source = telemetry.GetActivitySource(typeof(ServerFixture<StandardServer>).Assembly);

                Assert.That(fixture.ActivityListener.ShouldListenTo!(source), Is.True);
                Assert.That(fixture.ActivityListener.ShouldListenTo!(unrelatedSource), Is.False);

                using Activity activity = source.StartActivity(nameof(ActivityListenerSamplesOnlyResolvedSource))!;

                Assert.That(activity, Is.Not.Null);
                Assert.That(activity!.Source, Is.SameAs(source));
                Assert.That(activity.IsAllDataRequested, Is.True);
                Assert.That(activity.Recorded, Is.True);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
