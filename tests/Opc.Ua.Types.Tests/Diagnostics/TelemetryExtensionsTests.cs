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

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Diagnostics
{
    [TestFixture]
    public class TelemetryExtensionsTests
    {
        [Test]
        public void CreateMeterUsesCallingAssembly()
        {
            var telemetry = new TestTelemetryContext();

            using Meter meter = TelemetryExtensions.CreateMeter(telemetry);

            Assert.That(meter.Name, Is.EqualTo(typeof(TelemetryExtensionsTests).Assembly.FullName));
        }

        [Test]
        public void GetActivitySourceUsesCallingAssembly()
        {
            var telemetry = new TestTelemetryContext();

            ActivitySource source = telemetry.GetActivitySource();

            Assert.That(source.Name, Is.EqualTo(typeof(TelemetryExtensionsTests).Assembly.FullName));
        }

        [Test]
        public void StartActivityUsesCallingAssembly()
        {
            var telemetry = new TestTelemetryContext();
            string expectedSourceName = typeof(TelemetryExtensionsTests).Assembly.FullName!;
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == expectedSourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllData
            };
            ActivitySource.AddActivityListener(listener);

            using Activity activity = telemetry.StartActivity();

            Assert.That(activity, Is.Not.Null);
            Assert.That(activity!.Source.Name, Is.EqualTo(expectedSourceName));
        }

        [Test]
        public void LegacyContextUsesExistingSources()
        {
            using var telemetry = new LegacyTelemetryContext();

            using Meter meter = TelemetryExtensions.CreateMeter(telemetry);
            ActivitySource source = telemetry.GetActivitySource();

            Assert.That(meter.Name, Is.EqualTo("Legacy"));
            Assert.That(source, Is.SameAs(telemetry.ActivitySource));
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }

        private sealed class LegacyTelemetryContext : ITelemetryContext, System.IDisposable
        {
            public Microsoft.Extensions.Logging.ILoggerFactory LoggerFactory =>
                NullLoggerFactory.Instance;

            public ActivitySource ActivitySource { get; } = new("Legacy");

            public Meter CreateMeter()
            {
                return new Meter("Legacy");
            }

            public void Dispose()
            {
                ActivitySource.Dispose();
            }
        }
    }
}
