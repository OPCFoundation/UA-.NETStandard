/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    [TestFixture]
    [Category("Fluent")]
    [Parallelizable]
    public class TypedAccessorExtensionsTests
    {
        [Test]
        public void ComponentsReturnsAccessorWithOriginalBuilder()
        {
            var builder = new Mock<INodeBuilder<BaseObjectState>>();

            IComponentAccessor<BaseObjectState> accessor = builder.Object.Components();

            Assert.That(accessor.Builder, Is.SameAs(builder.Object));
        }

        [Test]
        public void PropertiesReturnsAccessorWithOriginalBuilder()
        {
            var builder = new Mock<INodeBuilder<BaseObjectState>>();

            IPropertyAccessor<BaseObjectState> accessor = builder.Object.Properties();

            Assert.That(accessor.Builder, Is.SameAs(builder.Object));
        }

        [Test]
        public void ComponentsAndPropertiesRejectNullBuilder()
        {
            INodeBuilder<BaseObjectState> builder = null!;

            Assert.That(() => builder.Components(), Throws.TypeOf<System.ArgumentNullException>());
            Assert.That(() => builder.Properties(), Throws.TypeOf<System.ArgumentNullException>());
        }
    }
}
