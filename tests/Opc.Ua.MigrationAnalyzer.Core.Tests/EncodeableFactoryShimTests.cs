/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.MigrationAnalyzer.Core.Tests
{
    /// <summary>
    /// Runtime tests for <see cref="EncodeableFactoryShim"/>.
    /// </summary>
    [TestFixture]
    [Category("Shim")]
    public class EncodeableFactoryShimTests
    {
        /// <summary>
        /// The static <c>GlobalFactory</c> shim must return the same
        /// instance as <c>ServiceMessageContext.GlobalContext.Factory</c>.
        /// </summary>
        [Test]
        public Task GlobalFactoryReturnsServiceMessageContextGlobalFactoryAsync()
        {
#pragma warning disable CS0618 // EncodeableFactory.GlobalFactory is an intentional shim call.
            IEncodeableFactory shimFactory = EncodeableFactory.GlobalFactory;
            Assert.That(shimFactory, Is.Not.Null);
            Assert.That(shimFactory, Is.SameAs(ServiceMessageContext.GlobalContext.Factory));
#pragma warning restore CS0618
            return Task.CompletedTask;
        }
    }
}
