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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies complete method results across native asynchronous and adapted legacy node managers.
    /// </summary>
    public partial class AsyncCustomNodeManagerTests
    {
        /// <summary>
        /// Verifies that ordinary callbacks retain their null-result success convention.
        /// </summary>
        /// <param name="asynchronousLegacyCall">Whether the legacy manager opts into asynchronous method calls.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OrdinaryMethodCallbacksRetainNullAsSuccessfulCompletion(bool asynchronousLegacyCall)
        {
            using ITestNodeManager manager = CreateManager(asynchronousLegacyCall);
            ServerSystemContext context = manager.SystemContext;
            ushort ns = manager.NamespaceIndexes[0];
            var owner = new BaseObjectState(null)
            {
                NodeId = new NodeId("LegacyOwner", ns),
                BrowseName = new QualifiedName("LegacyOwner", ns)
            };
            var method = new MethodState(owner)
            {
                NodeId = new NodeId("LegacyCall", ns),
                BrowseName = new QualifiedName("LegacyCall", ns)
            };
            int calls = 0;
            method.OnCallMethod2 = (_, _, _, _, _) =>
            {
                calls++;
                return null!;
            };
            owner.AddChild(method);
            await manager.AddNodeAsync(context, NodeId.Null, owner).ConfigureAwait(false);
            using var operation = new OperationContext(Mock.Of<ISession>(), DiagnosticsMasks.None);
            var results = new List<CallMethodResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await manager.CallAsync(
                operation,
                [new CallMethodRequest { ObjectId = owner.NodeId, MethodId = method.NodeId }],
                results, errors).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].InputArgumentResults.IsEmpty, Is.True);
            Assert.That(results[0].OutputArguments.IsEmpty, Is.True);
        }

        /// <summary>
        /// Verifies ordered input results and permission-gated diagnostic reencoding into the receiving string table.
        /// </summary>
        /// <param name="diagnostics">Whether operation diagnostics are requested.</param>
        /// <param name="authorized">Whether additional diagnostic information is authorized.</param>
        /// <param name="successfulInputDetail">Whether a Good input result has diagnostic text.</param>
        /// <param name="asynchronousLegacyCall">Whether the legacy manager opts into asynchronous method calls.</param>
        [TestCase(false, false, false, false)]
        [TestCase(false, true, false, false)]
        [TestCase(true, false, false, false)]
        [TestCase(true, true, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(false, true, true, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, true, false)]
        [TestCase(true, true, true, true)]
        public async Task CallResultRetainsInputPositionsAndEncodesDiagnosticsInTheResponseTable(
            bool diagnostics, bool authorized, bool successfulInputDetail, bool asynchronousLegacyCall)
        {
            using ITestNodeManager manager = CreateManager(asynchronousLegacyCall);
            ServerSystemContext context = manager.SystemContext;
            ushort ns = manager.NamespaceIndexes[0];
            var parent = new BaseObjectState(null);
            parent.CreateAsPredefinedNode(context);
            parent.NodeId = new NodeId("ResultOwner", ns);
            parent.BrowseName = new QualifiedName("ResultOwner", ns);
            var method = new MethodState(parent)
            {
                NodeId = new NodeId("CheckedCall", ns),
                BrowseName = new QualifiedName("CheckedCall", ns)
            };
            method.InputArguments =
                new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(method)
                {
                    Value =
                    [
                        new Argument { Name = "Mode", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar },
                        new Argument { Name = "Speed", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
                    ]
                };
            var operation = new ServiceResult(
                "urn:upstream", new StatusCode(StatusCodes.BadInvalidArgument.Code, "CallRejected"),
                new LocalizedText("en", "Call rejected"), "operation detail", innerResult: null);
            var inputError = new ServiceResult(
                "urn:upstream", new StatusCode(StatusCodes.BadOutOfRange.Code, "SpeedRejected"),
                new LocalizedText("de", "Drehzahl zu hoch"), "limit: 100", innerResult: null);
            ServiceResult accepted = successfulInputDetail
                ? new ServiceResult(
                    "urn:upstream", new StatusCode(StatusCodes.Good.Code, "AcceptedMode"),
                    new LocalizedText("en", "Mode is valid"), null, innerResult: null)
                : ServiceResult.Good;
            int calls = 0;
            method.OnCallMethodWithResultAsync = (_, _, _, _, _) =>
            {
                calls++;
                return new ValueTask<MethodInvocationResult>(new MethodInvocationResult(
                    operation, inputArgumentResults: [accepted, inputError]));
            };
            parent.AddChild(method);
            await manager.AddNodeAsync(context, default, parent).ConfigureAwait(false);
            DiagnosticsMasks mask = diagnostics ? DiagnosticsMasks.OperationAll : 0;
            if (authorized)
            {
                mask |= DiagnosticsMasks.UserPermissionAdditionalInfo;
            }
            using var requestContext = new OperationContext(Mock.Of<ISession>(), mask);
            requestContext.StringTable.Append("existing-local-symbol");
            requestContext.StringTable.Append("existing-local-text");
            var results = new List<CallMethodResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await manager.CallAsync(
                requestContext,
                [new CallMethodRequest
                {
                    ObjectId = parent.NodeId,
                    MethodId = method.NodeId,
                    InputArguments = [new Variant(1), new Variant(200)]
                }],
                results, errors).ConfigureAwait(false);

            if (m_managerType == AsyncCustomNodeManagerType.CustomNodeManager2ViaAdapter && !asynchronousLegacyCall)
            {
                Assert.That(calls, Is.Zero, "A synchronous-only manager must not block on the new async handler.");
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(results[0].InputArgumentResults.IsEmpty, Is.True);
                Assert.That(results[0].OutputArguments.IsEmpty, Is.True);
                return;
            }
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(errors[0].SymbolicId, Is.EqualTo("CallRejected"));
            Assert.That(results[0].InputArgumentResults.Count, Is.EqualTo(2));
            Assert.That(results[0].InputArgumentResults[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].InputArgumentResults[1], Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(results[0].OutputArguments.IsEmpty, Is.True);
            if (diagnostics)
            {
                Assert.That(results[0].InputArgumentDiagnosticInfos.Count, Is.EqualTo(2));
                DiagnosticInfo actual = results[0].InputArgumentDiagnosticInfos[1];
                var table = requestContext.StringTable.ToArrayOf();
                if (successfulInputDetail)
                {
                    DiagnosticInfo first = results[0].InputArgumentDiagnosticInfos[0];
                    Assert.That(first, Is.Not.Null);
                    Assert.That(table[first.SymbolicId], Is.EqualTo("AcceptedMode"));
                    Assert.That(table[first.LocalizedText], Is.EqualTo("Mode is valid"));
                }
                else
                {
                    Assert.That(results[0].InputArgumentDiagnosticInfos[0], Is.Null);
                }
                Assert.That(actual.SymbolicId, Is.GreaterThan(1));
                Assert.That(table[actual.SymbolicId], Is.EqualTo("SpeedRejected"));
                Assert.That(table[actual.NamespaceUri], Is.EqualTo("urn:upstream"));
                Assert.That(table[actual.Locale], Is.EqualTo("de"));
                Assert.That(table[actual.LocalizedText], Is.EqualTo("Drehzahl zu hoch"));
                Assert.That(actual.AdditionalInfo, Is.EqualTo(authorized ? "limit: 100" : null),
                    "Forwarding must retain the existing permission gate for additional diagnostic information.");
                Assert.That(table[0], Is.EqualTo("existing-local-symbol"));
            }
            else
            {
                Assert.That(results[0].InputArgumentDiagnosticInfos.IsEmpty, Is.True);
                Assert.That(requestContext.StringTable.Count, Is.EqualTo(2));
            }
        }

        /// <summary>
        /// Verifies final Call assembly preserves requested Good-operation details through the public dispatcher.
        /// </summary>
        /// <param name="diagnostics">Whether operation diagnostics are requested.</param>
        /// <param name="innerStatusOnly">Whether the operation has only an inner status and no diagnostic strings.</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task CallDispatcherPreservesRequestedGoodOperationDiagnostics(
            bool diagnostics, bool innerStatusOnly)
        {
            m_mockServer.Setup(server => server.Telemetry).Returns(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(m_configuration);
            m_mockServer.Setup(server => server.ResourceManager).Returns(resources);
            ITestNodeManager manager = CreateManager(asynchronousLegacyCall: true);
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(Mock.Of<IConfigurationNodeManager>());
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(Mock.Of<ICoreNodeManager>());
            m_mockServer.Setup(server => server.MainNodeManagerFactory).Returns(factory.Object);
            await using var dispatcher = new MasterNodeManager(m_mockServer.Object, m_configuration, null, manager);
            m_mockServer.Setup(server => server.NodeManager).Returns(dispatcher);
            ushort ns = manager.NamespaceIndexes[0];
            var owner = new BaseObjectState(null)
            {
                NodeId = new NodeId("DiagnosticOwner", ns),
                BrowseName = new QualifiedName("DiagnosticOwner", ns)
            };
            var method = new MethodState(owner)
            {
                NodeId = new NodeId("DiagnosticCall", ns),
                BrowseName = new QualifiedName("DiagnosticCall", ns)
            };
            ServiceResult operation = innerStatusOnly
                ? new ServiceResult(StatusCodes.Good, new ServiceResult(StatusCodes.BadTypeMismatch))
                : new ServiceResult(
                    "urn:dispatcher", new StatusCode(StatusCodes.Good.Code, "Completed"),
                    new LocalizedText("en", "Done"), null, innerResult: null);
            int calls = 0;
            method.OnCallMethodWithResultAsync = (_, _, _, _, _) =>
            {
                calls++;
                return new ValueTask<MethodInvocationResult>(new MethodInvocationResult(operation));
            };
            owner.AddChild(method);
            await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, owner).ConfigureAwait(false);
            DiagnosticsMasks mask = !diagnostics
                ? DiagnosticsMasks.None
                : innerStatusOnly
                    ? DiagnosticsMasks.OperationInnerStatusCode
                    : DiagnosticsMasks.OperationSymbolicId | DiagnosticsMasks.OperationLocalizedText;
            using var context = new OperationContext(m_mockSession.Object, mask);

            (ArrayOf<CallMethodResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos) =
                await dispatcher.CallAsync(
                    context,
                    [new CallMethodRequest { ObjectId = owner.NodeId, MethodId = method.NodeId }])
                .ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].InputArgumentResults.IsEmpty, Is.True);
            Assert.That(results[0].OutputArguments.IsEmpty, Is.True);
            if (!diagnostics)
            {
                Assert.That(diagnosticInfos.IsEmpty, Is.True);
                Assert.That(context.StringTable.Count, Is.Zero);
                return;
            }
            Assert.That(diagnosticInfos.Count, Is.EqualTo(1));
            DiagnosticInfo actual = diagnosticInfos[0];
            Assert.That(actual, Is.Not.Null);
            if (innerStatusOnly)
            {
                Assert.That(context.StringTable.Count, Is.Zero);
                Assert.That(actual.InnerStatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(actual.IsNullDiagnosticInfo, Is.False);
            }
            else
            {
                Assert.That(context.StringTable.GetString((uint)actual.SymbolicId), Is.EqualTo("Completed"));
                Assert.That(context.StringTable.GetString((uint)actual.NamespaceUri), Is.EqualTo("urn:dispatcher"));
                Assert.That(context.StringTable.GetString((uint)actual.LocalizedText), Is.EqualTo("Done"));
                Assert.That(context.StringTable.GetString((uint)actual.Locale), Is.EqualTo("en"));
            }
        }

        /// <summary>
        /// Enables the legacy manager's existing asynchronous method-dispatch capability for these tests.
        /// </summary>
        private sealed class AsyncCallTestNodeManager : TestableCustomNodeManager2, ICallAsyncNodeManager
        {
            /// <summary>
            /// Initializes the legacy asynchronous method-dispatch test manager.
            /// </summary>
            /// <param name="server">The test server.</param>
            /// <param name="configuration">The application configuration.</param>
            /// <param name="useSamplingGroups">Whether the test uses sampling groups.</param>
            /// <param name="logger">The contextual logger.</param>
            /// <param name="namespaceUri">The manager's namespace URI.</param>
            public AsyncCallTestNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                bool useSamplingGroups,
                ILogger logger,
                string namespaceUri)
                : base(server, configuration, useSamplingGroups, logger, namespaceUri)
            {
            }
        }
    }
}
