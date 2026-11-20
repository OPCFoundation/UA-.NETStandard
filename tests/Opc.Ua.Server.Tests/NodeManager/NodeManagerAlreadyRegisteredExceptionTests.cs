// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Covers the publicly catchable duplicate-registration exception contract.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerAlreadyRegisteredExceptionTests
    {
        [Test]
        public void ExceptionTypeIsVisibleToExternalCallers()
        {
            Assert.That(typeof(NodeManagerAlreadyRegisteredException).IsVisible, Is.True);
        }

        [Test]
        public void DefaultConstructorPreservesMessageAndBaseType()
        {
            var exception = new NodeManagerAlreadyRegisteredException();

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.InstanceOf<InvalidOperationException>());
                Assert.That(exception.Message, Is.EqualTo("The NodeManager is already registered."));
                Assert.That(exception.InnerException, Is.Null);
            });
        }

        [Test]
        public void MessageConstructorPreservesCustomMessage()
        {
            const string message = "The factory returned its existing NodeManager.";
            var exception = new NodeManagerAlreadyRegisteredException(message);

            Assert.That(exception.Message, Is.EqualTo(message));
        }

        [Test]
        public void InnerExceptionConstructorPreservesMessageAndCause()
        {
            const string message = "The factory returned its existing NodeManager.";
            var cause = new InvalidOperationException("An existing generation still owns the manager.");
            var exception = new NodeManagerAlreadyRegisteredException(message, cause);

            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Is.EqualTo(message));
                Assert.That(exception.InnerException, Is.SameAs(cause));
            });
        }
    }
}
