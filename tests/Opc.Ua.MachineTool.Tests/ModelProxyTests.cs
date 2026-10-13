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
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises generated model proxies without a client or server runtime project.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    public sealed class ModelProxyTests
    {
        /// <summary>
        /// The abstract Number argument retains its concrete numeric type and precision.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MeasurementProxyPreservesAbstractNumberArguments(bool floatingPoint)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(MachineToolParts.Wmtp);
            Mock<ISessionClient> session = CreateSession(context, []);
            var objectId = new NodeId("measurement", 1);
            var proxy = new WMTPMeasurementTypeClient(session.Object, objectId, NUnitTelemetryContext.Create());
            Variant value = floatingPoint ? new Variant(0.125) : new Variant(9007199254740993UL);

            await proxy.SetDeltaConditionAsync(value).ConfigureAwait(false);

            var methodId = NodeId.Create(7030, WMTP.Namespaces.WMTP, context.NamespaceUris);
            session.Verify(client => client.CallAsync(
                null,
                It.Is<ArrayOf<CallMethodRequest>>(requests =>
                    requests.Count == 1 &&
                    requests[0].ObjectId == objectId &&
                    requests[0].MethodId == methodId &&
                    requests[0].InputArguments.Count == 1 &&
                    requests[0].InputArguments[0].TypeInfo == value.TypeInfo &&
                    requests[0].InputArguments[0] == value),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// The published Any-rank work-cycle report preserves both scalar and array structure representations.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WorkCycleProxyPreservesAnyRankStructureReports(bool array)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(MachineToolParts.Wmtp);
            var first = new ExtensionObject(new WMTPOutputDataType { ActualValue = 12.5, Index = 17 });
            var second = new ExtensionObject(new WMTPOutputDataType { ActualValue = 13.75, Index = 18 });
            ArrayOf<ExtensionObject> records = [first, second];
            Variant expected = array ? new Variant(records) : new Variant(first);
            Mock<ISessionClient> session = CreateSession(context, [expected]);
            var objectId = new NodeId("work-cycle", 1);
            var proxy = new WMTPWorkCycleDataTypeClient(session.Object, objectId, NUnitTelemetryContext.Create());

            Variant actual = await proxy.CombinedReportAllAsync().ConfigureAwait(false);

            Assert.That(actual, Is.EqualTo(expected));
            if (array)
            {
                Assert.That(actual.TryGetValue(out ArrayOf<ExtensionObject> values), Is.True);
                Assert.That(values.Count, Is.EqualTo(2));
                Assert.That(values[1].TryGetValue(out WMTPOutputDataType? value), Is.True);
                Assert.That(value!.Index, Is.EqualTo(18));
            }
            else
            {
                Assert.That(actual.TryGetValue<WMTPOutputDataType>(out WMTPOutputDataType? value, context), Is.True);
                Assert.That(value!.Index, Is.EqualTo(17));
                Assert.That(value.ActualValue, Is.EqualTo(12.5));
            }
            var methodId = NodeId.Create(7011, WMTP.Namespaces.WMTP, context.NamespaceUris);
            session.Verify(client => client.CallAsync(
                null,
                It.Is<ArrayOf<CallMethodRequest>>(requests =>
                    requests.Count == 1 &&
                    requests[0].ObjectId == objectId &&
                    requests[0].MethodId == methodId &&
                    requests[0].InputArguments.Count == 0),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Missing report output is an explicit bad response, not a successful empty record set.
        /// </summary>
        [Test]
        public void WorkCycleProxyRejectsMissingReportOutput()
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(MachineToolParts.Wmtp);
            Mock<ISessionClient> session = CreateSession(context, []);
            var proxy = new WMTPWorkCycleDataTypeClient(
                session.Object, new NodeId("work-cycle", 1), NUnitTelemetryContext.Create());
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await proxy.CombinedReportAllAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        private static Mock<ISessionClient> CreateSession(
            ServiceMessageContext context,
            ArrayOf<Variant> outputArguments)
        {
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            session.SetupGet(client => client.MessageContext).Returns(context);
            session.Setup(client => client.CallAsync(
                null,
                It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>())).Returns(
                    new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments = outputArguments
                            }
                        ],
                        DiagnosticInfos = []
                    }));
            return session;
        }
    }
}
