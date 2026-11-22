/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using NUnit.Framework;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Checks the NodeVersion bump scope during nested notifications and failures.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Category("NodeState")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerModelChangeScopeTests
    {
        /// <summary>
        /// Nested bumps suppress reentrant writes until the outer callback returns.
        /// </summary>
        [Test]
        public void NestedFrameworkBumpsRestoreOuterSuppression()
        {
            var outer = new BaseObjectState(null);
            var inner = new BaseObjectState(null);
            int notifications = 0;
            NodeState? notificationOwner = null;
            PropertyState<string> outerVersion = outer.EnableModelChangeTracking(
                1, (_, owner) =>
                {
                    notifications++;
                    notificationOwner = owner;
                });
            PropertyState<string> innerVersion = inner.EnableModelChangeTracking(1);
            int outerCallbacks = 0;
            int innerCallbacks = 0;
            bool outerBeforeNested = false;
            bool innerActive = false;
            bool outerAfterNested = false;
            bool reentrantWriteSucceeded = false;
            innerVersion.OnStateChanged = (_, _, _) =>
            {
                innerCallbacks++;
                innerActive = NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump;
            };
            outerVersion.OnStateChanged = (_, _, _) =>
            {
                outerCallbacks++;
                outerBeforeNested = NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump;
                inner.BumpNodeVersion(null!);
                outerAfterNested = NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump;
                reentrantWriteSucceeded = ServiceResult.IsGood(WriteNodeVersion(outerVersion, "2"));
            };

            Assert.That(NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump, Is.False);
            outer.BumpNodeVersion(null!);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(outerCallbacks, Is.EqualTo(1));
                Assert.That(innerCallbacks, Is.EqualTo(1));
                Assert.That(outerBeforeNested, Is.True);
                Assert.That(innerActive, Is.True);
                Assert.That(outerAfterNested, Is.True);
                Assert.That(reentrantWriteSucceeded, Is.True);
                Assert.That(notifications, Is.Zero);
                Assert.That(outerVersion.Value, Is.EqualTo("2"));
                Assert.That(innerVersion.Value, Is.EqualTo("2"));
                Assert.That(NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump, Is.False);
            }

            ServiceResult result = WriteNodeVersion(outerVersion, "4");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(notificationOwner, Is.SameAs(outer));
                Assert.That(outerVersion.Value, Is.EqualTo("4"));
            }
        }

        /// <summary>
        /// A failed nested bump restores both scopes before a later NodeVersion write.
        /// </summary>
        [Test]
        public void FailedNestedFrameworkBumpRestoresSuppression()
        {
            var outer = new BaseObjectState(null);
            var inner = new BaseObjectState(null);
            int notifications = 0;
            NodeState? notificationOwner = null;
            PropertyState<string> outerVersion = outer.EnableModelChangeTracking(
                1, (_, owner) =>
                {
                    notifications++;
                    notificationOwner = owner;
                });
            PropertyState<string> innerVersion = inner.EnableModelChangeTracking(1);
            var failure = new InvalidOperationException("Nested notification failed.");
            bool outerBeforeNested = false;
            bool innerActive = false;
            bool outerAfterNestedFailure = false;
            innerVersion.OnStateChanged = (_, _, _) =>
            {
                innerActive = NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump;
                throw failure;
            };
            outerVersion.OnStateChanged = (_, _, _) =>
            {
                outerBeforeNested = NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump;
                try
                {
                    inner.BumpNodeVersion(null!);
                }
                catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure))
                {
                    outerAfterNestedFailure = NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump;
                    throw;
                }
            };

            Assert.That(NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump, Is.False);
            InvalidOperationException? observed =
                Assert.Throws<InvalidOperationException>(() => outer.BumpNodeVersion(null!));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(observed, Is.SameAs(failure));
                Assert.That(outerBeforeNested, Is.True);
                Assert.That(innerActive, Is.True);
                Assert.That(outerAfterNestedFailure, Is.True);
                Assert.That(NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump, Is.False);
                Assert.That(notifications, Is.Zero);
            }

            ServiceResult result = WriteNodeVersion(outerVersion, "4");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(notificationOwner, Is.SameAs(outer));
                Assert.That(outerVersion.Value, Is.EqualTo("4"));
            }
        }

        /// <summary>
        /// Invokes the NodeVersion write handler with a string value.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static ServiceResult WriteNodeVersion(PropertyState<string> nodeVersion, string value)
        {
            var written = new Variant(value);
            StatusCode statusCode = StatusCodes.Good;
            DateTimeUtc timestamp = default;
            NodeValueEventHandler write = nodeVersion.OnWriteValue ??
                throw new InvalidOperationException("NodeVersion has no write handler.");
            return write(
                null!,
                nodeVersion,
                NumericRange.Null,
                QualifiedName.Null,
                ref written,
                ref statusCode,
                ref timestamp);
        }
    }
}
