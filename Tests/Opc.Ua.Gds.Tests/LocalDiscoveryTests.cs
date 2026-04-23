/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
            var client = new LocalDiscoveryServerClient(appConfig);
            client.PreferredLocales = (ArrayOf<string>)s_frenchGermanLocales;
            Assert.That(client.PreferredLocales.ToList(), Does.Contain("fr-FR"));
            Assert.That(client.PreferredLocales.ToList(), Does.Contain("de-DE"));
        }
    }
}
