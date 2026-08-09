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

using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.RobotIntent;
using Opc.Ua.Tests;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Covers Annex B links between OPC 40010 Robotics and Robot Intent.
    /// </summary>
    [TestFixture]
    public class IntentBuilderInteropTests
    {
        [Test]
        public void HasIntentControllerCreatesForwardAndInverseReferencesOnce()
        {
            SystemContext context = CreateSystemContext();
            var motionState = new MotionDeviceSystemState(null)
            {
                NodeId = new NodeId("motion", 2),
                BrowseName = new QualifiedName("Motion", 2)
            };
            var controllerState = new IntentControllerState(null)
            {
                NodeId = new NodeId("intent", 2),
                BrowseName = new QualifiedName("Intent", 2)
            };
            var buildContext = new Mock<IRoboticsBuildContext>(MockBehavior.Strict);
            buildContext.SetupGet(static c => c.Context).Returns(context);
            var motionBuilder = new Mock<IMotionDeviceSystemBuilder>(MockBehavior.Strict);
            motionBuilder.SetupGet(static b => b.BuildContext).Returns(buildContext.Object);
            motionBuilder.SetupGet(static b => b.State).Returns(motionState);
            var intentBuilder = new Mock<IIntentControllerBuilder>(MockBehavior.Strict);
            intentBuilder.SetupGet(static b => b.State).Returns(controllerState);
            NodeId referenceTypeId = NodeId.Create(
                global::Opc.Ua.RobotIntent.ReferenceTypes.HasIntentController,
                global::Opc.Ua.RobotIntent.Namespaces.RobotIntent,
                context.NamespaceUris);

            IMotionDeviceSystemBuilder returned = motionBuilder.Object
                .HasIntentController(intentBuilder.Object)
                .HasIntentController(intentBuilder.Object);

            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(motionBuilder.Object));
                Assert.That(motionState.ReferenceExists(referenceTypeId, false, controllerState.NodeId), Is.True);
                Assert.That(controllerState.ReferenceExists(referenceTypeId, true, motionState.NodeId), Is.True);
                Assert.That(CountReferences(motionState, referenceTypeId, false, controllerState.NodeId), Is.EqualTo(1));
                Assert.That(CountReferences(controllerState, referenceTypeId, true, motionState.NodeId), Is.EqualTo(1));
            });
        }

        [Test]
        public void HasIntentControllerGuardsNullArguments()
        {
            var motionBuilder = new Mock<IMotionDeviceSystemBuilder>(MockBehavior.Strict);
            var intentBuilder = new Mock<IIntentControllerBuilder>(MockBehavior.Strict);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => ((IMotionDeviceSystemBuilder)null!).HasIntentController(intentBuilder.Object),
                    Throws.TypeOf<System.ArgumentNullException>().With.Property("ParamName").EqualTo("motionDeviceSystem"));
                Assert.That(
                    () => motionBuilder.Object.HasIntentController(null!),
                    Throws.TypeOf<System.ArgumentNullException>().With.Property("ParamName").EqualTo("intentController"));
            });
        }

        private static SystemContext CreateSystemContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(global::Opc.Ua.RobotIntent.Namespaces.RobotIntent);
            return new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        private static int CountReferences(
            NodeState node,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId)
        {
            var references = new List<IReference>();
            node.GetReferences(null!, references);
            return references.Count(reference =>
                reference.ReferenceTypeId == referenceTypeId &&
                reference.IsInverse == isInverse &&
                reference.TargetId == targetId);
        }
    }
}
