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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Behavioural tests for <see cref="DiLockClient"/>. Verifies that
    /// each helper invokes <see cref="ISession.CallAsync"/> with the
    /// expected method NodeId and translates the response into the
    /// documented status / exception contract.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("ClientHelpers")]
    public sealed class DiLockClientTests
    {
        [Test]
        public async Task InitLockAsyncCallsInitLockMethodWithContext()
        {
            var nsTable = CreateNamespaceTable();
            var sessionMock = CreateSessionMock(nsTable);
            CallMethodRequest? captured = null;
            SetupCallReturns(sessionMock, new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = new Variant[] { new Variant((int)0) }
            }, r => captured = r);

            var client = new DiLockClient(
                sessionMock.Object, new NodeId("lock-1", 2), NullTelemetry());

            int status = await client.InitLockAsync("tag").ConfigureAwait(false);

            Assert.That(status, Is.Zero);
            Assert.That(captured, Is.Not.Null);
            Assert.That(
                captured!.MethodId,
                Is.EqualTo(NodeId.Create(
                    global::Opc.Ua.Di.Methods.LockingServicesType_InitLock,
                    global::Opc.Ua.Di.Namespaces.OpcUaDi,
                    nsTable)));
            Assert.That(captured.InputArguments, Has.Count.EqualTo(1));
            Assert.That(
                captured.InputArguments[0].TryGetValue(out string ctx),
                Is.True);
            Assert.That(ctx, Is.EqualTo("tag"));
        }

        [Test]
        public async Task RenewLockAsyncCallsRenewLockMethodWithoutInputs()
        {
            var nsTable = CreateNamespaceTable();
            var sessionMock = CreateSessionMock(nsTable);
            CallMethodRequest? captured = null;
            SetupCallReturns(sessionMock, new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = new Variant[] { new Variant((int)1) }
            }, r => captured = r);

            var client = new DiLockClient(
                sessionMock.Object, new NodeId("lock-1", 2), NullTelemetry());

            int status = await client.RenewLockAsync().ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(1));
            Assert.That(captured, Is.Not.Null);
            Assert.That(
                captured!.MethodId,
                Is.EqualTo(NodeId.Create(
                    global::Opc.Ua.Di.Methods.LockingServicesType_RenewLock,
                    global::Opc.Ua.Di.Namespaces.OpcUaDi,
                    nsTable)));
            Assert.That(captured.InputArguments, Is.Empty);
        }

        [Test]
        public async Task ExitLockAsyncCallsExitLockMethod()
        {
            var nsTable = CreateNamespaceTable();
            var sessionMock = CreateSessionMock(nsTable);
            CallMethodRequest? captured = null;
            SetupCallReturns(sessionMock, new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = new Variant[] { new Variant((int)0) }
            }, r => captured = r);

            var client = new DiLockClient(
                sessionMock.Object, new NodeId("lock-1", 2), NullTelemetry());

            int status = await client.ExitLockAsync().ConfigureAwait(false);

            Assert.That(status, Is.Zero);
            Assert.That(
                captured!.MethodId,
                Is.EqualTo(NodeId.Create(
                    global::Opc.Ua.Di.Methods.LockingServicesType_ExitLock,
                    global::Opc.Ua.Di.Namespaces.OpcUaDi,
                    nsTable)));
            Assert.That(captured.InputArguments, Is.Empty);
        }

        [Test]
        public async Task BreakLockAsyncCallsBreakLockMethod()
        {
            var nsTable = CreateNamespaceTable();
            var sessionMock = CreateSessionMock(nsTable);
            CallMethodRequest? captured = null;
            SetupCallReturns(sessionMock, new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = new Variant[] { new Variant((int)0) }
            }, r => captured = r);

            var client = new DiLockClient(
                sessionMock.Object, new NodeId("lock-1", 2), NullTelemetry());

            int status = await client.BreakLockAsync().ConfigureAwait(false);

            Assert.That(status, Is.Zero);
            Assert.That(
                captured!.MethodId,
                Is.EqualTo(NodeId.Create(
                    global::Opc.Ua.Di.Methods.LockingServicesType_BreakLock,
                    global::Opc.Ua.Di.Namespaces.OpcUaDi,
                    nsTable)));
        }

        [Test]
        public void CallThrowsServiceResultExceptionWhenStatusBad()
        {
            var sessionMock = CreateSessionMock();
            SetupCallReturns(sessionMock, new CallMethodResult
            {
                StatusCode = StatusCodes.BadUserAccessDenied,
                OutputArguments = new Variant[] { new Variant((int)0) }
            });

            var client = new DiLockClient(
                sessionMock.Object, new NodeId("lock-1", 2), NullTelemetry());

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.InitLockAsync("tag").ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void CallThrowsBadUnexpectedErrorWhenOutputEmpty()
        {
            var sessionMock = CreateSessionMock();
            SetupCallReturns(sessionMock, new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = global::Opc.Ua.ArrayOf.Empty<Variant>()
            });

            var client = new DiLockClient(
                sessionMock.Object, new NodeId("lock-1", 2), NullTelemetry());

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RenewLockAsync().ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadUnexpectedError));
        }

        [Test]
        public void CallThrowsBadUnexpectedErrorWhenOutputIsNotInt32()
        {
            var sessionMock = CreateSessionMock();
            SetupCallReturns(sessionMock, new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = new Variant[] { new Variant("not-an-int") }
            });

            var client = new DiLockClient(
                sessionMock.Object, new NodeId("lock-1", 2), NullTelemetry());

            // The generated LockingServicesTypeClient proxy treats a
            // non-Int32 status output as a generic BadUnexpectedError
            // when the typed unwrap fails; the original hand-rolled
            // helper raised BadTypeMismatch. The proxy's wire-form
            // contract is now canonical for this client.
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ExitLockAsync().ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadUnexpectedError));
        }

        private static NamespaceTable CreateNamespaceTable()
        {
            var nsTable = new NamespaceTable();
            nsTable.GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);
            return nsTable;
        }

        private static Mock<ISession> CreateSessionMock()
        {
            return CreateSessionMock(CreateNamespaceTable());
        }

        private static Mock<ISession> CreateSessionMock(NamespaceTable nsTable)
        {
            var mock = new Mock<ISession>();
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
            // The source-generated LockingServicesTypeClient proxy reads
            // Session.MessageContext.NamespaceUris (not Session.NamespaceUris)
            // to resolve the method's ExpandedNodeId — supply a stub
            // context backed by the same namespace table.
            var ctxMock = new Mock<IServiceMessageContext>();
            ctxMock.SetupGet(c => c.NamespaceUris).Returns(nsTable);
            mock.SetupGet(s => s.MessageContext).Returns(ctxMock.Object);
            return mock;
        }

        private static void SetupCallReturns(
            Mock<ISession> sessionMock,
            CallMethodResult result,
            System.Action<CallMethodRequest>? capture = null)
        {
            sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, methods, _) =>
                    {
                        if (capture != null && methods.Count > 0)
                        {
                            capture(methods[0]);
                        }
                    })
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResult[] { result }
                });
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }
    }
}
