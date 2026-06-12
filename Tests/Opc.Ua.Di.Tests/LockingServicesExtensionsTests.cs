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

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server.Locking;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Argument-validation tests for
    /// <see cref="LockingServicesExtensions.BindToLockService"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Locking")]
    public sealed class LockingServicesExtensionsTests
    {
        private static readonly NodeId s_elementId = new("element-1", 2);

        [Test]
        public void BindToLockServiceThrowsOnNullLockState()
        {
            ILockService service = new Mock<ILockService>().Object;

            Assert.Throws<ArgumentNullException>(
                () => LockingServicesExtensions.BindToLockService(
                    lockState: null!,
                    s_elementId,
                    service));
        }

        [Test]
        public void BindToLockServiceThrowsOnNullElementId()
        {
            var lockState = new LockingServicesState(parent: null);
            ILockService service = new Mock<ILockService>().Object;

            Assert.Throws<ArgumentNullException>(
                () => lockState.BindToLockService(NodeId.Null, service));
        }

        [Test]
        public void BindToLockServiceThrowsOnNullService()
        {
            var lockState = new LockingServicesState(parent: null);

            Assert.Throws<ArgumentNullException>(
                () => lockState.BindToLockService(s_elementId, service: null!));
        }

        [Test]
        public void BindToLockServiceIsNoOpWhenAllMethodChildrenAreNull()
        {
            // A freshly-constructed LockingServicesState has all four
            // method children (InitLock/RenewLock/ExitLock/BreakLock) as
            // null until the predefined-node pipeline materialises them.
            // Binding should silently skip each missing method.
            var lockState = new LockingServicesState(parent: null);
            ILockService service = new Mock<ILockService>().Object;

            Assert.That(lockState.InitLock, Is.Null);
            Assert.That(lockState.RenewLock, Is.Null);
            Assert.That(lockState.ExitLock, Is.Null);
            Assert.That(lockState.BreakLock, Is.Null);

            Assert.DoesNotThrow(() => lockState.BindToLockService(s_elementId, service));
        }

        // -----------------------------------------------------------------
        // Behavioural tests — verify that the OnCall delegate installed by
        // BindToLockService routes through to the supplied ILockService and
        // propagates the integer status via the ref parameter.
        // -----------------------------------------------------------------

        [Test]
        public void BindToLockServiceRoutesInitLockToService()
        {
            ISystemContext ctx = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(s => s.InitLock(ctx, s_elementId, "tag")).Returns(42);

            var lockState = new LockingServicesState(parent: null)
            {
                InitLock = new InitLockMethodState(parent: null),
            };

            lockState.BindToLockService(s_elementId, service.Object);

            int status = 0;
            ServiceResult result = lockState.InitLock!.OnCall!(
                ctx, lockState.InitLock, NodeId.Null, "tag", ref status);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(status, Is.EqualTo(42));
            service.Verify(s => s.InitLock(ctx, s_elementId, "tag"), Times.Once);
        }

        [Test]
        public void BindToLockServiceRoutesRenewLockToService()
        {
            ISystemContext ctx = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(s => s.RenewLock(ctx, s_elementId)).Returns(7);

            var lockState = new LockingServicesState(parent: null)
            {
                RenewLock = new RenewLockMethodState(parent: null),
            };

            lockState.BindToLockService(s_elementId, service.Object);

            int status = 0;
            ServiceResult result = lockState.RenewLock!.OnCall!(
                ctx, lockState.RenewLock, NodeId.Null, ref status);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(status, Is.EqualTo(7));
            service.Verify(s => s.RenewLock(ctx, s_elementId), Times.Once);
        }

        [Test]
        public void BindToLockServiceRoutesExitLockToService()
        {
            ISystemContext ctx = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(s => s.ExitLock(ctx, s_elementId)).Returns(13);

            var lockState = new LockingServicesState(parent: null)
            {
                ExitLock = new ExitLockMethodState(parent: null),
            };

            lockState.BindToLockService(s_elementId, service.Object);

            int status = 0;
            ServiceResult result = lockState.ExitLock!.OnCall!(
                ctx, lockState.ExitLock, NodeId.Null, ref status);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(status, Is.EqualTo(13));
            service.Verify(s => s.ExitLock(ctx, s_elementId), Times.Once);
        }

        [Test]
        public void BindToLockServiceRoutesBreakLockToService()
        {
            ISystemContext ctx = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>(MockBehavior.Strict);
            service.Setup(s => s.BreakLock(ctx, s_elementId)).Returns(99);

            var lockState = new LockingServicesState(parent: null)
            {
                BreakLock = new BreakLockMethodState(parent: null),
            };

            lockState.BindToLockService(s_elementId, service.Object);

            int status = 0;
            ServiceResult result = lockState.BreakLock!.OnCall!(
                ctx, lockState.BreakLock, NodeId.Null, ref status);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(status, Is.EqualTo(99));
            service.Verify(s => s.BreakLock(ctx, s_elementId), Times.Once);
        }

        [Test]
        public void BindToLockServiceBindsOnlyMethodsThatExistOnLockState()
        {
            // Mixed scenario: only InitLock + ExitLock are attached.
            // The other two must remain null after binding (i.e. no
            // method-state is fabricated by BindToLockService).
            ISystemContext ctx = Mock.Of<ISystemContext>();
            var service = new Mock<ILockService>();
            service.Setup(s => s.InitLock(It.IsAny<ISystemContext>(),
                It.IsAny<NodeId>(), It.IsAny<string>())).Returns(1);
            service.Setup(s => s.ExitLock(It.IsAny<ISystemContext>(),
                It.IsAny<NodeId>())).Returns(2);

            var lockState = new LockingServicesState(parent: null)
            {
                InitLock = new InitLockMethodState(parent: null),
                ExitLock = new ExitLockMethodState(parent: null),
            };

            lockState.BindToLockService(s_elementId, service.Object);

            Assert.That(lockState.InitLock!.OnCall, Is.Not.Null);
            Assert.That(lockState.ExitLock!.OnCall, Is.Not.Null);
            Assert.That(lockState.RenewLock, Is.Null);
            Assert.That(lockState.BreakLock, Is.Null);
        }
    }
}
