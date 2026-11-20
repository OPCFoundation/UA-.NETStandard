/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
