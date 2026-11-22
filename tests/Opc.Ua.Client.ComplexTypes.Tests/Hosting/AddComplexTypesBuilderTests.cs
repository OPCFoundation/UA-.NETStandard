// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Opc.Ua.Client.ComplexTypes.Tests.Hosting
{
    /// <summary>
    /// Tests for the fluent
    /// <see cref="OpcUaComplexTypesBuilderExtensions.AddComplexTypes(IOpcUaBuilder)"/>
    /// extension that registers a <see cref="ComplexTypeSystemFactory"/>
    /// singleton on the DI container.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ComplexTypes")]
    [Parallelizable]
    public sealed class AddComplexTypesBuilderTests
    {
        [Test]
        public void AddComplexTypesThrowsForNullBuilder()
        {
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddComplexTypes(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddComplexTypesRegistersFactoryOnce()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddComplexTypes();
            int afterFirst = CountDescriptorsFor<ComplexTypeSystemFactory>(services);
            services.AddOpcUa().AddComplexTypes();
            int afterSecond = CountDescriptorsFor<ComplexTypeSystemFactory>(services);

            Assert.That(afterFirst, Is.EqualTo(1));
            Assert.That(afterSecond, Is.EqualTo(1));
        }

        [Test]
        public void AddComplexTypesReturnsBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            IOpcUaBuilder result = builder.AddComplexTypes();

            Assert.That(result, Is.SameAs(builder));
            Assert.That(result.Services, Is.SameAs(services));
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
