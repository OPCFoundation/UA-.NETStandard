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

using Moq;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Tests;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies capability redeclarations compare all mode values, not only option flags.
    /// </summary>
    [TestFixture]
    public class IntentBuilderCapabilityModeEquivalenceTests
    {
        [Test]
        public void AcceptsAllowsModeSetsInDifferentOrder()
        {
            IntentControllerBuilder builder = CreateBuilder();

            builder.Accepts<JointMoveIntentDataType>(
                supportedBufferModes: new[] { BufferModeEnum.Buffered, BufferModeEnum.Aborting }.ToArrayOf(),
                supportedBlockingModes: new[] { BlockingModeEnum.None, BlockingModeEnum.Hard }.ToArrayOf());

            Assert.That(
                () => builder.Accepts<JointMoveIntentDataType>(
                    supportedBufferModes: new[] { BufferModeEnum.Aborting, BufferModeEnum.Buffered }.ToArrayOf(),
                    supportedBlockingModes: new[] { BlockingModeEnum.Hard, BlockingModeEnum.None }.ToArrayOf()),
                Throws.Nothing);
        }

        [Test]
        public void AcceptsRejectsRedeclarationWithFewerBufferModes()
        {
            IntentControllerBuilder builder = CreateBuilder();

            builder.Accepts<JointMoveIntentDataType>(
                supportedBufferModes: new[] { BufferModeEnum.Aborting, BufferModeEnum.Buffered }.ToArrayOf());
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => builder.Accepts<JointMoveIntentDataType>(
                    supportedBufferModes: new[] { BufferModeEnum.Aborting }.ToArrayOf()))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(exception.Message, Does.Contain(nameof(JointMoveIntentDataType)));
            });
        }

        [Test]
        public void AcceptsRejectsRedeclarationWithDifferentBlockingModeValue()
        {
            IntentControllerBuilder builder = CreateBuilder();

            builder.Accepts<JointMoveIntentDataType>(
                supportedBlockingModes: new[] { BlockingModeEnum.None, BlockingModeEnum.Hard }.ToArrayOf());
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => builder.Accepts<JointMoveIntentDataType>(
                    supportedBlockingModes: new[] { BlockingModeEnum.None, BlockingModeEnum.Single }.ToArrayOf()))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(exception.Message, Does.Contain(nameof(JointMoveIntentDataType)));
            });
        }

        private static IntentControllerBuilder CreateBuilder()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            RobotIntentRootState root = OpcUaRobotIntentExtensions.CreateInstanceOfRobotIntentRootType(
                systemContext,
                null!,
                new QualifiedName("RobotIntent", 1));
            root.CreateOrReplaceControllers(systemContext, null);
            var context = new Mock<IRobotIntentBuildContext>(MockBehavior.Strict);
            context.SetupGet(static build => build.Context).Returns(systemContext);
            context.SetupGet(static build => build.Root).Returns(root);
            context.SetupGet(static build => build.InstanceNamespaceIndex).Returns((ushort)1);
            context.SetupGet(static build => build.Nodes).Returns(Mock.Of<INodeManagerBuilder>());
            return new IntentControllerBuilder(context.Object, "Controller");
        }
    }
}

