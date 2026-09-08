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
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class OpcUaWotPropertyContextTests
    {
        [Test]
        public async Task ReadTranslatesRequestedDataEncodingIntoSourceNamespace()
        {
            ServiceMessageContext source = CreateContext();
            ushort sourceEncoding = source.NamespaceUris.GetIndexOrAppend("urn:encoding");
            ServiceMessageContext caller = CreateContext();
            caller.NamespaceUris.Append("urn:aggregate-only");
            ushort callerEncoding = caller.NamespaceUris.GetIndexOrAppend("urn:encoding");
            Mock<ISession> session = CreateSession(source);
            ReadValueId? captured = null;
            session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> requests,
                    CancellationToken _) =>
                {
                    captured = requests[0];
                    return new ValueTask<ReadResponse>(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new DataValue(new Variant(42))]
                    });
                });
            await using IWotPropertyBindingChannel channel = await OpenChannelAsync(session).ConfigureAwait(false);

            WotReadResult result = await channel.ReadAsync(new WotReadRequest(
                caller, NumericRange.Parse("1"), new QualifiedName("Custom", callerEncoding))).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.NodeId, Is.EqualTo(new NodeId("Value", 1)));
            Assert.That(captured.IndexRange, Is.EqualTo("1"));
            Assert.That(captured.DataEncoding, Is.EqualTo(new QualifiedName("Custom", sourceEncoding)));
            Assert.That(source.NamespaceUris.Count, Is.EqualTo(3));
            Assert.That(caller.NamespaceUris.Count, Is.EqualTo(4));
        }

        [Test]
        public async Task WriteRejectsMissingSourceNamespaceBeforeSendingARequest()
        {
            ServiceMessageContext source = CreateContext();
            ServiceMessageContext caller = CreateContext();
            ushort missing = caller.NamespaceUris.GetIndexOrAppend("urn:absent-remotely");
            Mock<ISession> session = CreateSession(source);
            await using IWotPropertyBindingChannel channel = await OpenChannelAsync(session).ConfigureAwait(false);

            WotWriteResult result = await channel.WriteAsync(new WotWriteRequest(
                new DataValue(new Variant(new NodeId("Sensor", missing))), caller)).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(result.Error, Is.Not.Empty);
            Assert.That(source.NamespaceUris.Count, Is.EqualTo(2));
            session.Verify(s => s.WriteAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ReadRejectsUnmappedEncodingBeforeSendingARequest()
        {
            ServiceMessageContext source = CreateContext();
            ServiceMessageContext caller = CreateContext();
            ushort missing = caller.NamespaceUris.GetIndexOrAppend("urn:absent-remotely");
            Mock<ISession> session = CreateSession(source);
            await using IWotPropertyBindingChannel channel = await OpenChannelAsync(session).ConfigureAwait(false);

            WotReadResult result = await channel.ReadAsync(new WotReadRequest(
                caller, dataEncoding: new QualifiedName("Custom", missing))).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(result.Value.WrappedValue.IsNull, Is.True);
            Assert.That(result.Error, Is.Not.Empty);
            Assert.That(source.NamespaceUris.Count, Is.EqualTo(2));
            session.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static ServiceMessageContext CreateContext()
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            context.NamespaceUris.Append("urn:source");
            return context;
        }

        private static Mock<ISession> CreateSession(ServiceMessageContext context)
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(s => s.ServerUris).Returns(context.ServerUris);
            session.SetupGet(s => s.Factory).Returns(context.Factory);
            return session;
        }

        private static async ValueTask<IWotPropertyBindingChannel> OpenChannelAsync(Mock<ISession> session)
        {
            var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
            {
                SessionFactory = (_, _) => new ValueTask<ISession>(session.Object),
                DisposeSession = false
            });
            var form = new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "10101", OpcUaBindingPlanner.BindingUri),
                WotAffordanceKind.Property,
                "value",
                "/properties/value/forms/0",
                WoTBindingCapabilityEnum.ReadProperty,
                "readproperty",
                new WotEndpointDescriptor("opc.tcp", "localhost", 4840, "opc.tcp://localhost:4840"),
                new WotAddressingDescriptor("nsu=urn:source;s=Value"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "Read"),
                new WotPayloadDescriptor("application/octet-stream", "binary"),
                [],
                isExecutable: true);
            IWotBindingChannel channel = await executor.ActivateAsync(form, new WotExecutorContext())
                .ConfigureAwait(false);
            Assert.That(channel, Is.InstanceOf<IWotPropertyBindingChannel>());
            return (IWotPropertyBindingChannel)channel;
        }
    }
}
