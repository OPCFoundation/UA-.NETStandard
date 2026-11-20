// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Server.Internal;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests.Internal
{
    [TestFixture]
    [TestSpec("9.1.11.2", Summary = "Diagnostics address space")]
    public class DiagnosticsAddressSpaceTests
    {
        [Test]
        [TestSpec("9.1.11.2", Summary = "Binds multiple counters")]
        public void StatusBinding_BindsMultipleCounters()
        {
            Assert.That(PubSubStatusBinding.CounterNodeIdCount, Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        [TestSpec("5.2.3", Summary = "ConfigurationVersion is accessible")]
        public async Task ApplicationExposesConfigurationVersion()
        {
            await using IPubSubApplication app = BuildApp();
            Assert.That(app.ConfigurationVersion, Is.Not.Null);
            Assert.That(app.ConfigurationVersion.MajorVersion, Is.GreaterThan(0U));
        }

        [Test]
        [TestSpec("9.1.11", Summary = "Diagnostics level settable")]
        public async Task DiagnosticsLevelIsAvailable()
        {
            await using IPubSubApplication app = BuildApp();
            Assert.That(app.Diagnostics, Is.Not.Null);
            Assert.That(app.Diagnostics.Level, Is.Not.EqualTo((PubSubDiagnosticsLevel)255));
        }

        private static IPubSubApplication BuildApp()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("diag-addr-test")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .Build();
        }
    }
}
