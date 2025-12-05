/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
 * http://opcfoundation.org/License/MIT/1.00
 * ======================================================================*/

using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.ServiceMessageContextTests
{
    /// <summary>
    /// Tests for <see cref="ServiceMessageContext"/>.
    /// </summary>
    [TestFixture]
    [Category("ServiceMessageContext")]
    [Parallelizable]
    public class ServiceMessageContextTests
    {
        /// <summary>
        /// Test that creating a ServiceMessageContext with a null factory creates a new factory.
        /// </summary>
        [Test]
        public void ConstructorWithNullFactoryCreatesNewFactory()
        {
            // Arrange & Act
            var context = new ServiceMessageContext((ITelemetryContext)null, (IEncodeableFactory)null);

            // Assert
            Assert.IsNotNull(context.Factory);
        }

        /// <summary>
        /// Test that creating a ServiceMessageContext with a custom factory uses that factory.
        /// </summary>
        [Test]
        public void ConstructorWithCustomFactoryUsesProvidedFactory()
        {
            // Arrange
            IEncodeableFactory customFactory = EncodeableFactory.Create();

            // Act
            var context = new ServiceMessageContext(null, customFactory);

            // Assert
            Assert.AreSame(customFactory, context.Factory);
        }

        /// <summary>
        /// Test that two ServiceMessageContexts with different factories have separate factories.
        /// </summary>
        [Test]
        public void TwoContextsWithDifferentFactoriesAreSeparate()
        {
            // Arrange
            IEncodeableFactory factory1 = EncodeableFactory.Create();
            IEncodeableFactory factory2 = EncodeableFactory.Create();

            var context1 = new ServiceMessageContext(null, factory1);
            var context2 = new ServiceMessageContext(null, factory2);

            // Assert - the contexts should reference different factory instances
            Assert.AreSame(factory1, context1.Factory);
            Assert.AreSame(factory2, context2.Factory);
            Assert.AreNotSame(context1.Factory, context2.Factory);
        }

        /// <summary>
        /// Test that the default constructor creates a new factory.
        /// </summary>
        [Test]
        public void DefaultConstructorCreatesNewFactory()
        {
            // Arrange & Act
            var context = new ServiceMessageContext(null);

            // Assert
            Assert.IsNotNull(context.Factory);
        }

        /// <summary>
        /// Test that setting the Factory property works correctly.
        /// </summary>
        [Test]
        public void SettingFactoryPropertyWorks()
        {
            // Arrange
            var context = new ServiceMessageContext(null);
            IEncodeableFactory customFactory = EncodeableFactory.Create();

            // Act
            context.Factory = customFactory;

            // Assert
            Assert.AreSame(customFactory, context.Factory);
        }

        /// <summary>
        /// Test that setting the Factory property to null creates a new factory.
        /// </summary>
        [Test]
        public void SettingFactoryPropertyToNullCreatesNewFactory()
        {
            // Arrange
            var context = new ServiceMessageContext(null);
            var originalFactory = context.Factory;

            // Act
            context.Factory = null;

            // Assert
            Assert.IsNotNull(context.Factory);
            Assert.AreNotSame(originalFactory, context.Factory);
        }
    }
}
