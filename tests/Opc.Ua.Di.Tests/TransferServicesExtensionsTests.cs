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
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server.Transfer;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Category("DI")]
    [Category("Transfer")]
    public sealed class TransferServicesExtensionsTests
    {
        [Test]
        public void BindToTransferServiceRejectsInvalidArguments()
        {
            ITransferService service = Mock.Of<ITransferService>();
            BaseObjectState transferServices = CreateTransferServices().Object;

            Assert.That(
                () => TransferServicesExtensions.BindToTransferService(
                    null!,
                    new NodeId("Device", 2),
                    service),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("transferServices"));
            Assert.That(
                () => transferServices.BindToTransferService(NodeId.Null, service),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("elementId"));
            Assert.That(
                () => transferServices.BindToTransferService(new NodeId("Device", 2), null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("service"));
        }

        [Test]
        public void BindToTransferServiceRequiresAllMethodChildren()
        {
            var transferServices = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("TransferServices", 2)
            };
            transferServices.AddChild(CreateMethod(
                transferServices,
                BrowseNames.TransferToDevice));

            Assert.That(
                () => transferServices.BindToTransferService(
                    new NodeId("Device", 2),
                    Mock.Of<ITransferService>()),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void BoundTransferMethodsRouteToService()
        {
            NodeId elementId = new("Device", 2);
            var service = new Mock<ITransferService>(MockBehavior.Strict);
            service
                .Setup(s => s.TransferToDeviceAsync(
                    It.IsAny<ISystemContext>(),
                    elementId,
                    It.Is<ParameterSet>(set => set.ElementId == elementId),
                    default))
                .ReturnsAsync(11);
            service
                .Setup(s => s.TransferFromDeviceAsync(
                    It.IsAny<ISystemContext>(),
                    elementId,
                    default))
                .ReturnsAsync(12);
            (BaseObjectState Object, MethodState To, MethodState From, _) =
                CreateTransferServices();
            Object.BindToTransferService(elementId, service.Object);
            var toOutputs = new List<Variant>();
            var fromOutputs = new List<Variant>();
            var context = new SystemContext(telemetry: null!);

            ServiceResult toResult = To.OnCallMethod2!(
                context,
                To,
                Object.NodeId,
                [],
                toOutputs);
            ServiceResult fromResult = From.OnCallMethod2!(
                context,
                From,
                Object.NodeId,
                [],
                fromOutputs);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(toResult), Is.True);
                Assert.That(toOutputs[0].GetInt32(), Is.EqualTo(11));
                Assert.That(toOutputs[1].GetInt32(), Is.EqualTo((int)(uint)StatusCodes.Good));
                Assert.That(ServiceResult.IsGood(fromResult), Is.True);
                Assert.That(fromOutputs[0].GetInt32(), Is.EqualTo(12));
                Assert.That(fromOutputs[1].GetInt32(), Is.EqualTo((int)(uint)StatusCodes.Good));
            });
        }

        [Test]
        public void BoundFetchValidatesAndRoutesArguments()
        {
            var service = new Mock<ITransferService>(MockBehavior.Strict);
            service
                .SetupSequence(s => s.FetchAsync(
                    It.IsAny<ISystemContext>(),
                    7,
                    3,
                    25,
                    true,
                    default))
                .ReturnsAsync(new FetchResult(3, true, [], StatusCodes.Good))
                .ReturnsAsync(new FetchResult(3, true, [], StatusCodes.BadInternalError));
            (BaseObjectState Object, _, _, MethodState Fetch) = CreateTransferServices();
            Object.BindToTransferService(new NodeId("Device", 2), service.Object);
            var context = new SystemContext(telemetry: null!);
            ArrayOf<Variant> inputs =
            [
                new Variant(7),
                new Variant(3),
                new Variant(25),
                new Variant(true)
            ];
            var outputs = new List<Variant>();

            ServiceResult missing = Fetch.OnCallMethod2!(
                context,
                Fetch,
                Object.NodeId,
                [new Variant(7)],
                []);
            ServiceResult success = Fetch.OnCallMethod2!(
                context,
                Fetch,
                Object.NodeId,
                inputs,
                outputs);
            ServiceResult failure = Fetch.OnCallMethod2!(
                context,
                Fetch,
                Object.NodeId,
                inputs,
                []);

            Assert.Multiple(() =>
            {
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
                Assert.That(ServiceResult.IsGood(success), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(1));
                Assert.That(outputs[0].IsNull, Is.True);
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            });
        }

        private static (
            BaseObjectState Object,
            MethodState To,
            MethodState From,
            MethodState Fetch) CreateTransferServices()
        {
            var transferServices = new BaseObjectState(null)
            {
                NodeId = new NodeId("TransferServices", 2),
                BrowseName = new QualifiedName("TransferServices", 2)
            };
            MethodState transferTo = CreateMethod(
                transferServices,
                BrowseNames.TransferToDevice);
            MethodState transferFrom = CreateMethod(
                transferServices,
                BrowseNames.TransferFromDevice);
            MethodState fetch = CreateMethod(
                transferServices,
                BrowseNames.FetchTransferResultData);
            transferServices.AddChild(transferTo);
            transferServices.AddChild(transferFrom);
            transferServices.AddChild(fetch);
            return (transferServices, transferTo, transferFrom, fetch);
        }

        private static MethodState CreateMethod(BaseObjectState parent, string browseName)
        {
            return new MethodState(parent)
            {
                NodeId = new NodeId(browseName, 2),
                BrowseName = new QualifiedName(browseName, 2)
            };
        }
    }
}
