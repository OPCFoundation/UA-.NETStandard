// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;

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
            var context = new ServiceMessageContext(null!, (IEncodeableFactory)null!);

            // Assert
            Assert.That(context.Factory, Is.Not.Null);
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
            var context = new ServiceMessageContext(null!, customFactory);

            // Assert
            Assert.That(context.Factory, Is.SameAs(customFactory));
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

            var context1 = new ServiceMessageContext(null!, factory1);
            var context2 = new ServiceMessageContext(null!, factory2);

            // Assert - the contexts should reference different factory instances
            Assert.That(context1.Factory, Is.SameAs(factory1));
            Assert.That(context2.Factory, Is.SameAs(factory2));
            Assert.That(context2.Factory, Is.Not.SameAs(context1.Factory));
        }

        /// <summary>
        /// Test that the default constructor creates a new factory.
        /// </summary>
        [Test]
        public void StaticCreateMethodCreatesNewFactory()
        {
            // Arrange & Act
            var context = ServiceMessageContext.Create(null);

            // Assert
            Assert.That(context.Factory, Is.Not.Null);
        }
    }
}
