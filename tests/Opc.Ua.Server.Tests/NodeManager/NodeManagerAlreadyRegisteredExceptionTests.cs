/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
