/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Reflection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.RobotIntent;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Tests;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies Robot Intent capability redeclaration honesty.
    /// </summary>
    [TestFixture]
    public class IntentBuilderRedeclarationTests
    {
        [Test]
        public void AcceptsAllowsIdenticalRedeclarationAsIdempotent()
        {
            IntentControllerBuilder builder = CreateBuilder();

            builder.Accepts<JointMoveIntentDataType>(
                cancelSupported: true,
                pauseSupported: true,
                retrySupported: false,
                supportedBufferModes: new[] { BufferModeEnum.Buffered, BufferModeEnum.Aborting }.ToArrayOf(),
                supportedBlockingModes: new[] { BlockingModeEnum.None, BlockingModeEnum.Hard }.ToArrayOf());
            builder.Accepts<JointMoveIntentDataType>(
                cancelSupported: true,
                pauseSupported: true,
                retrySupported: false,
                supportedBufferModes: new[] { BufferModeEnum.Aborting, BufferModeEnum.Buffered }.ToArrayOf(),
                supportedBlockingModes: new[] { BlockingModeEnum.Hard, BlockingModeEnum.None }.ToArrayOf());

            List<IntentCapabilityDataType> capabilities = GetDeclaredCapabilities(builder);

            Assert.Multiple(() =>
            {
                Assert.That(capabilities, Has.Count.EqualTo(1));
                Assert.That(capabilities[0].PauseSupported, Is.True);
                Assert.That(capabilities[0].SupportedBufferModes.Contains(BufferModeEnum.Aborting), Is.True);
            });
        }

        [Test]
        public void AcceptsThrowsOnConflictingRedeclaration()
        {
            IntentControllerBuilder builder = CreateBuilder();

            builder.Accepts<JointMoveIntentDataType>(cancelSupported: true);
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => builder.Accepts<JointMoveIntentDataType>(cancelSupported: false))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(exception.Message, Does.Contain(nameof(JointMoveIntentDataType)));
                Assert.That(GetDeclaredCapabilities(builder), Has.Count.EqualTo(1));
            });
        }

        private static List<IntentCapabilityDataType> GetDeclaredCapabilities(IntentControllerBuilder builder)
        {
            FieldInfo field = typeof(IntentControllerBuilder).GetField(
                "m_capabilities",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (List<IntentCapabilityDataType>)field.GetValue(builder)!;
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
