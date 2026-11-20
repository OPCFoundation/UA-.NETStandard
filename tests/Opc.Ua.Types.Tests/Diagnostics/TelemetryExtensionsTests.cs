/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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

            using Meter meter = telemetry.CreateMeter();

            Assert.That(meter.Name, Is.EqualTo(typeof(TelemetryExtensionsTests).Assembly.FullName));
        }

        [Test]
        public void ActivitySourceUsesCallingAssembly()
        {
            var telemetry = new TestTelemetryContext();

            ActivitySource source = telemetry.ActivitySource;

            Assert.That(source.Name, Is.EqualTo(typeof(TelemetryExtensionsTests).Assembly.FullName));
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

            using Activity activity = telemetry.StartActivity()!;

            Assert.That(activity, Is.Not.Null);
            Assert.That(activity!.Source.Name, Is.EqualTo(expectedSourceName));
        }

        [Test]
        public void ContextUsesAssemblyAwareSources()
        {
            using var telemetry = new AssemblyTelemetryContext();

            using Meter meter = TelemetryExtensions.CreateMeter(telemetry);
            ActivitySource source = telemetry.GetActivitySource();

            Assert.That(meter.Name, Is.EqualTo(typeof(TelemetryExtensionsTests).Assembly.FullName));
            Assert.That(source.Name, Is.EqualTo(typeof(TelemetryExtensionsTests).Assembly.FullName));
        }

        [Test]
        public void NullResultsUseDefaultSources()
        {
            var telemetry = new NullTelemetryContext();
            string expectedSourceName = typeof(TelemetryExtensionsTests).Assembly.FullName!;
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == expectedSourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllData
            };
            ActivitySource.AddActivityListener(listener);

            using Meter meter = TelemetryExtensions.CreateMeter(telemetry);
            ActivitySource source = telemetry.GetActivitySource();
            using Activity activity = telemetry.StartActivity()!;

            Assert.That(meter.Name, Is.EqualTo(expectedSourceName));
            Assert.That(source.Name, Is.EqualTo(expectedSourceName));
            Assert.That(activity, Is.Not.Null);
            Assert.That(activity!.Source.Name, Is.EqualTo(expectedSourceName));
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }

        private sealed class AssemblyTelemetryContext : ITelemetryContext, System.IDisposable
        {
            public Microsoft.Extensions.Logging.ILoggerFactory LoggerFactory =>
                NullLoggerFactory.Instance;

            public ActivitySource ActivitySource { get; } = new("Default");

            public Meter CreateMeter()
            {
                return new Meter("Default");
            }

            public Meter CreateMeter(System.Reflection.Assembly assembly)
            {
                return new Meter(assembly.FullName!);
            }

            public ActivitySource GetActivitySource(System.Reflection.Assembly assembly)
            {
                return m_activitySource ??= new ActivitySource(assembly.FullName!);
            }

            public void Dispose()
            {
                ActivitySource.Dispose();
                m_activitySource?.Dispose();
            }

            private ActivitySource m_activitySource = null!;
        }

        private sealed class NullTelemetryContext : ITelemetryContext
        {
            public Microsoft.Extensions.Logging.ILoggerFactory LoggerFactory =>
                NullLoggerFactory.Instance;

            public ActivitySource ActivitySource => null!;

            public Meter CreateMeter()
            {
                return null!;
            }

            public Meter CreateMeter(System.Reflection.Assembly assembly)
            {
                return null!;
            }

            public ActivitySource GetActivitySource(System.Reflection.Assembly assembly)
            {
                return null!;
            }
        }
    }
}
