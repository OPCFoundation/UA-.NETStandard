// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class LocalDiscoveryTests
    {
        private static readonly string[] s_frenchGermanLocales = ["fr-FR", "de-DE"];

        [Test]
        public void ConstructorSetsApplicationConfiguration()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.ApplicationConfiguration, Is.SameAs(appConfig));
        }

        [Test]
        public void ConstructorSetsDefaultDiagnosticsMasksToNone()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.DiagnosticsMasks, Is.EqualTo(DiagnosticsMasks.None));
        }

        [Test]
        public void ConstructorWithCustomDiagnosticsMasks()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig, DiagnosticsMasks.All);
            Assert.That(client.DiagnosticsMasks, Is.EqualTo(DiagnosticsMasks.All));
        }

        [Test]
        public void ConstructorCreatesMessageContext()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.MessageContext, Is.Not.Null);
        }

        [Test]
        public void ConstructorSetsPreferredLocalesIncludingEnUs()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.PreferredLocales.IsEmpty, Is.False);
            Assert.That(client.PreferredLocales.ToList(), Does.Contain("en-US"));
        }

        [Test]
        public void PreferredLocalesContainsCurrentCulture()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            string currentUiCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;
            Assert.That(client.PreferredLocales.ToList(), Does.Contain(currentUiCulture));
        }

        [Test]
        public void DefaultOperationTimeoutDefaultsToZero()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig);
            Assert.That(client.DefaultOperationTimeout, Is.Zero);
        }

        [Test]
        public void DefaultOperationTimeoutRoundTrip()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig)
            {
                DefaultOperationTimeout = 30000
            };
            Assert.That(client.DefaultOperationTimeout, Is.EqualTo(30000));
        }

        [Test]
        public void PreferredLocalesCanBeReplaced()
        {
            var appConfig = new ApplicationConfiguration();
            var client = new LocalDiscoveryServerClient(appConfig)
            {
                PreferredLocales = (ArrayOf<string>)s_frenchGermanLocales
            };
            Assert.That(client.PreferredLocales.ToList(), Does.Contain("fr-FR"));
            Assert.That(client.PreferredLocales.ToList(), Does.Contain("de-DE"));
        }
    }
}
