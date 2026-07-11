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

#nullable enable

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
                () => ((IOpcUaBuilder)null!).AddAlarms(),
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
            AlarmClientFactory? factory = sp.GetService<AlarmClientFactory>();

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
            AlarmClientFactory factory = sp.GetRequiredService<AlarmClientFactory>();
            ISessionClient session = new Mock<ISessionClient>(MockBehavior.Loose).Object;

            AlarmClient client = factory.Create(session);

            Assert.That(client, Is.Not.Null);
        }

        [Test]
        public void ResolvedFactoryCreateRejectsNullSession()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddAlarms();
            using ServiceProvider sp = services.BuildServiceProvider();
            AlarmClientFactory factory = sp.GetRequiredService<AlarmClientFactory>();

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
