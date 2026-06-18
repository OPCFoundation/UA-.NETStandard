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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
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
