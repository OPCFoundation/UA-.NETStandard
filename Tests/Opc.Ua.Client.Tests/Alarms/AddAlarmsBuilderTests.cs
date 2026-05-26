// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#nullable enable

using System;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Alarms;

namespace Opc.Ua.Client.Tests.Alarms
{
    /// <summary>
    /// Tests for the fluent
    /// <see cref="OpcUaAlarmsBuilderExtensions.AddAlarms(IOpcUaBuilder)"/>
    /// extension that registers an <see cref="AlarmClientFactory"/>
    /// singleton on the DI container.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Alarms")]
    [Parallelizable]
    public sealed class AddAlarmsBuilderTests
    {
        [Test]
        public void AddAlarmsThrowsForNullBuilder()
        {
            Assert.That(
                () => OpcUaAlarmsBuilderExtensions.AddAlarms(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddAlarmsRegistersFactoryOnce()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddAlarms();
            int afterFirst = CountDescriptorsFor<AlarmClientFactory>(services);
            services.AddOpcUa().AddAlarms();
            int afterSecond = CountDescriptorsFor<AlarmClientFactory>(services);

            Assert.That(afterFirst, Is.EqualTo(1));
            Assert.That(afterSecond, Is.EqualTo(1));
        }

        [Test]
        public void AddAlarmsReturnsBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder result = builder.AddAlarms();

            Assert.That(result, Is.SameAs(builder));
            Assert.That(result.Services, Is.SameAs(services));
        }

        [Test]
        public void AddAlarmsResolvesFactory()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddAlarms();

            using ServiceProvider sp = services.BuildServiceProvider();
            var factory = sp.GetService<AlarmClientFactory>();

            Assert.That(factory, Is.Not.Null);
            Assert.That(factory!.Telemetry, Is.Not.Null);
            Assert.That(
                factory.Telemetry,
                Is.SameAs(sp.GetRequiredService<ITelemetryContext>()));
        }

        [Test]
        public void ResolvedFactoryCreateProducesAlarmClient()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddAlarms();
            using ServiceProvider sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<AlarmClientFactory>();
            var session = new Mock<ISessionClient>(MockBehavior.Loose).Object;

            AlarmClient client = factory.Create(session);

            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void ResolvedFactoryCreateRejectsNullSession()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddAlarms();
            using ServiceProvider sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<AlarmClientFactory>();

            Assert.That(
                () => factory.Create(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AlarmClientFactoryThrowsForNullTelemetry()
        {
            Assert.That(
                () => new AlarmClientFactory(null!),
                Throws.ArgumentNullException);
        }

        private static int CountDescriptorsFor<TService>(IServiceCollection services)
        {
            int count = 0;
            foreach (ServiceDescriptor d in services)
            {
                if (d.ServiceType == typeof(TService))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
