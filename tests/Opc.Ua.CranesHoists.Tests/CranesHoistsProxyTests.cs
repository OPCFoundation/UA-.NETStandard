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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Verifies generated proxy inheritance and the qualified paths into referenced model contracts.
    /// </summary>
    [TestFixture]
    [Category("CranesHoists")]
    public sealed class CranesHoistsProxyTests
    {
        /// <summary>
        /// Each generated proxy shares the existing dependency proxy hierarchy
        /// and performs no service calls on creation.
        /// </summary>
        [Test]
        public void GeneratedProxiesReuseDependencyContracts()
        {
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            var nodeId = new NodeId("Crane", 1);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Robotics.MotionDeviceSystemTypeClient system =
                new CraneMotionDeviceSystemTypeClient(session.Object, nodeId, telemetry);
            Robotics.MotionDeviceTypeClient device = new CraneMotionDeviceTypeClient(session.Object, nodeId, telemetry);
            Robotics.AxisTypeClient axis = new CraneAxisTypeClient(session.Object, nodeId, telemetry);
            Robotics.MotorTypeClient motor = new CraneMotorTypeClient(session.Object, nodeId, telemetry);
            Di.ComponentTypeClient brake = new BrakeTypeClient(session.Object, nodeId, telemetry);
            var protection = new ProtectiveFunctionTypeClient(session.Object, nodeId, telemetry);

            Assert.Multiple(() =>
            {
                Assert.That(system, Is.TypeOf<CraneMotionDeviceSystemTypeClient>());
                Assert.That(device, Is.TypeOf<CraneMotionDeviceTypeClient>());
                Assert.That(axis, Is.TypeOf<CraneAxisTypeClient>());
                Assert.That(motor, Is.TypeOf<CraneMotorTypeClient>());
                Assert.That(brake, Is.TypeOf<BrakeTypeClient>());
                Assert.That(protection, Is.TypeOf<ProtectiveFunctionTypeClient>());
            });
            ObjectTypeClient[] proxies = [system, device, axis, motor, brake, protection];
            foreach (ObjectTypeClient proxy in proxies)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(proxy.ObjectId, Is.EqualTo(nodeId));
                    Assert.That(proxy.Session, Is.SameAs(session.Object));
                    Assert.That(proxy.Telemetry, Is.SameAs(telemetry));
                });
            }
            session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Identification resolves in the DI browse-name namespace and returns the existing Machinery proxy.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task GeneratedIdentificationProxyUsesQualifiedDependencyPath(bool reverseNamespaces)
        {
            SystemContext context = CranesHoistsTestModel.CreateContext(reverseNamespaces);
            var message = new Mock<IServiceMessageContext>(MockBehavior.Strict);
            message.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(message.Object);
            var nodeId = new NodeId("Crane", 1);
            var identificationId = new NodeId("Crane.Identification", 1);
            var expectedName = new QualifiedName("Identification",
                context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi));
            using var cancellation = new CancellationTokenSource();
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    It.Is<ArrayOf<BrowsePath>>(paths =>
                        paths.Count == 1 &&
                        paths[0].StartingNode == nodeId &&
                        paths[0].RelativePath.Elements.Count == 1 &&
                        paths[0].RelativePath.Elements[0].TargetName == expectedName &&
                        paths[0].RelativePath.Elements[0].ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HasComponent &&
                        paths[0].RelativePath.Elements[0].IncludeSubtypes &&
                        !paths[0].RelativePath.Elements[0].IsInverse),
                    cancellation.Token))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = NodeId.ToExpandedNodeId(identificationId, context.NamespaceUris),
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            }
                        ],
                        DiagnosticInfos = []
                    }));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var proxy = new CraneMotionDeviceSystemTypeClient(session.Object, nodeId, telemetry);

            Machinery.MachineIdentificationTypeClient? first =
                await proxy.GetIdentificationAsync(telemetry, cancellation.Token).ConfigureAwait(false);
            Machinery.MachineIdentificationTypeClient? second =
                await proxy.GetIdentificationAsync(telemetry, cancellation.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.TypeOf<Machinery.MachineIdentificationTypeClient>());
                Assert.That(first!.ObjectId, Is.EqualTo(identificationId));
                Assert.That(first.Session, Is.SameAs(session.Object));
                Assert.That(second, Is.SameAs(first));
            });
            session.Verify(value => value.TranslateBrowsePathsToNodeIdsAsync(
                null, It.IsAny<ArrayOf<BrowsePath>>(), cancellation.Token), Times.Once());
            session.VerifyGet(value => value.MessageContext, Times.AtLeastOnce());
            message.VerifyGet(value => value.NamespaceUris, Times.Exactly(2));
            session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A missing optional identification namespace remains absent and does not trigger a service request.
        /// </summary>
        [Test]
        public async Task OptionalIdentificationProxyRemainsAbsentWithoutItsNamespace()
        {
            ServiceMessageContext message = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            message.NamespaceUris.GetIndexOrAppend(CranesHoistsModel.NamespaceUri);
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(message);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var proxy = new BrakeTypeClient(session.Object, new NodeId("Brake", 1), telemetry);

            Machinery.MachineryComponentIdentificationTypeClient? identification =
                await proxy.GetIdentificationAsync(telemetry).ConfigureAwait(false);

            Assert.That(identification, Is.Null);
            session.VerifyGet(value => value.MessageContext, Times.Once());
            session.VerifyNoOtherCalls();
        }
    }
}
