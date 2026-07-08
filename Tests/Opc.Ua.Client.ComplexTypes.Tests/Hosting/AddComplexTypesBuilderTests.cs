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
                () => OpcUaComplexTypesBuilderExtensions.AddComplexTypes((IOpcUaBuilder)null!),
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
