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

using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Export;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Bindings;
#if NET8_0_OR_GREATER
using Opc.Ua.WotCon.Bindings.OpcUa;
#endif
using Opc.Ua.WotCon.Server.Materialization;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotProjectedMethodRuntimeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [NonParallelizable]
        public async Task ContextualCallDetailsReachTheClientWithReceivingStringTableAndPermissions(bool diagnostics)
        {
            await VerifyCallDetailsAsync(diagnostics, native: false).ConfigureAwait(false);
        }

#if NET8_0_OR_GREATER
        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        [NonParallelizable]
        public async Task NativeCallDetailsReachTheClientWithReceivingStringTableAndPermissions(bool diagnostics)
        {
            await VerifyCallDetailsAsync(diagnostics, native: true).ConfigureAwait(false);
        }
#endif

        [TestCase(true, 0)]
        [TestCase(false, 0)]
        [TestCase(false, 4)]
        [Category("Integration")]
        [NonParallelizable]
        public async Task CompleteOperationDetailsSurviveFinalCallAssembly(bool successful, int innerDepth)
        {
            var inner = new ServiceResult(StatusCodes.BadTypeMismatch);
            for (int index = 0; index < innerDepth; index++)
            {
                inner = new ServiceResult(StatusCodes.Good, inner);
            }
            ServiceResult operation = successful
                ? new ServiceResult(StatusCodes.Good, new LocalizedText("en", "Command accepted"))
                : new ServiceResult(StatusCodes.Bad, inner);
            DiagnosticsMasks mask = successful
                ? DiagnosticsMasks.OperationLocalizedText : DiagnosticsMasks.OperationInnerStatusCode;
            if (innerDepth > 0)
            {
                mask |= DiagnosticsMasks.OperationInnerDiagnostics;
            }

            await VerifyCallDetailsAsync(true, false, operation, mask, innerDepth).ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [NonParallelizable]
        public async Task GoodInnerInformationBitsReachTheClientWithoutStrings()
        {
            var inner = new ServiceResult(StatusCodes.Good.SetSemanticsChanged(true));
            await VerifyCallDetailsAsync(
                true, false, new ServiceResult(StatusCodes.Bad, inner), DiagnosticsMasks.OperationInnerStatusCode)
                .ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [NonParallelizable]
        public async Task CallStatusInformationBitsReachTheClientWithoutDiagnostics()
        {
            await VerifyCallDetailsAsync(
                false, false, new ServiceResult(StatusCodes.Good.SetSemanticsChanged(true)))
                .ConfigureAwait(false);
        }

        private static async Task VerifyCallDetailsAsync(
            bool diagnostics,
            bool native,
            ServiceResult? operationOverride = null,
            DiagnosticsMasks requestedOverride = DiagnosticsMasks.None,
            int innerDepth = 0)
        {
            DiagnosticsMasks requested = operationOverride is not null
                ? requestedOverride
                : diagnostics ? DiagnosticsMasks.OperationAll : DiagnosticsMasks.None;
            var namespaces = new NamespaceTable();
            ushort sourceIndex = namespaces.GetIndexOrAppend("urn:call-source");
            var source = new Mock<ISession>();
            source.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            source.SetupGet(value => value.ServerUris).Returns(new StringTable());
            source.SetupGet(value => value.Factory).Returns(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()).Factory);
            source.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((header, calls, _) =>
                {
                    Assert.That(header?.ReturnDiagnostics ?? 0, Is.EqualTo((uint)requested));
                    Assert.That(calls.Count, Is.EqualTo(1));
                    Assert.That(calls[0].ObjectId, Is.EqualTo(new NodeId("Device", sourceIndex)));
                    Assert.That(calls[0].MethodId, Is.EqualTo(new NodeId("Run", sourceIndex)));
                    Assert.That(calls[0].InputArguments.Count, Is.EqualTo(2));
                    Assert.That(calls[0].InputArguments[0], Is.EqualTo(new Variant(1)));
                    Assert.That(calls[0].InputArguments[1], Is.EqualTo(new Variant(200)));
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            StringTable =
                            [
                                "unused-0", "unused-1", "unused-2", "unused-3",
                                "urn:device:validation", "CallRejected", "de", "Drehzahl zu hoch", "SpeedRejected"
                            ]
                        },
                        DiagnosticInfos = [new DiagnosticInfo { NamespaceUri = 4, SymbolicId = 5 }],
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = StatusCodes.BadInvalidArgument,
                                InputArgumentResults = [StatusCodes.Good, StatusCodes.BadOutOfRange],
                                InputArgumentDiagnosticInfos =
                                [
                                    null!,
                                    new DiagnosticInfo
                                    {
                                        NamespaceUri = 4, SymbolicId = 8, Locale = 6, LocalizedText = 7,
                                        AdditionalInfo = "limit: 100"
                                    }
                                ]
                            }
                        ]
                    });
                });
            var form = new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "1", "urn:test"), WotAffordanceKind.Action,
                "run", "/actions/run/forms/0", WoTBindingCapabilityEnum.InvokeAction, "invokeaction",
                new WotEndpointDescriptor("opc.tcp", "source.invalid", 4840, "opc.tcp://source.invalid:4840"),
                new WotAddressingDescriptor("nsu=urn:call-source;s=Run",
                    ImmutableDictionary<string, string>.Empty.Add("componentOf", "nsu=urn:call-source;s=Device")),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.InvokeAction, "invokeaction", "Call"),
                new WotPayloadDescriptor("application/octet-stream", "binary"), [], true);
            var contextual = new Mock<IWotContextualBindingChannel>();
            contextual.SetupGet(value => value.Form).Returns(form);
            contextual.Setup(value => value.InvokeAsync(
                It.IsAny<WotInvokeRequest>(), It.IsAny<CancellationToken>()))
                .Returns<WotInvokeRequest, CancellationToken>((request, _) =>
                {
                    Assert.That(request.DiagnosticsMask, Is.EqualTo(requested));
                    Assert.That(request.Inputs.Count, Is.EqualTo(2));
                    Assert.That(request.Inputs[0], Is.EqualTo(new Variant(1)));
                    Assert.That(request.Inputs[1], Is.EqualTo(new Variant(200)));
                    if (operationOverride is not null)
                    {
                        return new ValueTask<WotInvokeResult>(new WotInvokeResult(operationOverride.StatusCode)
                            .WithResultDetails(operationOverride, []));
                    }
                    var operation = new ServiceResult(
                        "urn:device:validation", new StatusCode(StatusCodes.BadInvalidArgument.Code, "CallRejected"),
                        LocalizedText.Null, null, innerResult: null);
                    var input = new ServiceResult(
                        "urn:device:validation", new StatusCode(StatusCodes.BadOutOfRange.Code, "SpeedRejected"),
                        new LocalizedText("de", "Drehzahl zu hoch"), "limit: 100", innerResult: null);
                    return new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.BadInvalidArgument)
                        .WithResultDetails(operation, [ServiceResult.Good, input]));
                });
            var channels = new FakeWotBindingChannelFactory();
            if (native)
            {
#if NET8_0_OR_GREATER
                var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    SessionFactory = (_, _) => new ValueTask<ISession>(source.Object),
                    DisposeSession = false
                });
                channels.SetOpener(form, token => executor.ActivateAsync(form, new WotExecutorContext(), token));
#else
                throw new NotSupportedException("The built-in OPC UA executor is available on net8.0 and later.");
#endif
            }
            else
            {
                channels.SetChannel(form, contextual.Object);
            }
            string directory = Path.Combine(Path.GetTempPath(), "w-call-" + Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            ReferenceServer? server = null;
            try
            {
                server = await fixture.StartAsync(directory).ConfigureAwait(false);
                var host = new LifecycleWotProjectionHost(
                    server.NodeManagerLifecycle, new WotProjectionBindingRuntimeFactory(channels));
                WotProjectionHandle handle = await host.AddAsync(CreateCallProjection(form)).ConfigureAwait(false);
                try
                {
                    using var client = new ClientFixture(NUnitTelemetryContext.Create());
                    await client.LoadClientConfigurationAsync(directory).ConfigureAwait(false);
                    using ISession session = await client.ConnectAsync(
                        new UriBuilder(Utils.UriSchemeOpcTcp, "localhost", fixture.Port).Uri, SecurityPolicies.None)
                        .ConfigureAwait(false);
                    session.ReturnDiagnostics = requested;
                    try
                    {
                        NodeId owner = ExpandedNodeId.Parse("nsu=urn:wot-call;s=Owner", session.NamespaceUris);
                        NodeId method = ExpandedNodeId.Parse("nsu=urn:wot-call;s=Run", session.NamespaceUris);
                        CallResponse response = await session.CallAsync(
                            new RequestHeader { ReturnDiagnostics = (uint)requested },
                            [new CallMethodRequest
                            {
                                ObjectId = owner, MethodId = method,
                                InputArguments = [new Variant(1), new Variant(200)]
                            }], CancellationToken.None).ConfigureAwait(false);

                        Assert.That(response.Results.Count, Is.EqualTo(1));
                        CallMethodResult result = response.Results[0];
                        Assert.That(result.StatusCode.Code,
                            Is.EqualTo((operationOverride?.StatusCode ?? StatusCodes.BadInvalidArgument).Code));
                        Assert.That(result.InputArgumentResults.Count, Is.EqualTo(operationOverride is null ? 2 : 0));
                        if (operationOverride is null)
                        {
                            Assert.That(result.InputArgumentResults[0], Is.EqualTo(StatusCodes.Good));
                            Assert.That(result.InputArgumentResults[1], Is.EqualTo(StatusCodes.BadOutOfRange));
                        }
                        Assert.That(result.OutputArguments.IsEmpty, Is.True);
                        if (operationOverride is not null && requested == DiagnosticsMasks.None)
                        {
                            Assert.That(response.DiagnosticInfos.IsEmpty, Is.True);
                            Assert.That(result.InputArgumentDiagnosticInfos.IsEmpty, Is.True);
                            Assert.That(response.ResponseHeader.StringTable.IsEmpty, Is.True);
                        }
                        else if (operationOverride is not null)
                        {
                            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
                            Assert.That(result.InputArgumentDiagnosticInfos.IsEmpty, Is.True);
                            DiagnosticInfo detail = response.DiagnosticInfos[0];
                            if (requested == DiagnosticsMasks.OperationLocalizedText)
                            {
                                Assert.That(response.ResponseHeader.StringTable[detail.LocalizedText],
                                    Is.EqualTo("Command accepted"));
                            }
                            else
                            {
                                Assert.That(response.ResponseHeader.StringTable.IsEmpty, Is.True);
                                ServiceResult expected = operationOverride;
                                for (int index = 0; index < innerDepth; index++)
                                {
                                    Assert.That(detail.InnerDiagnosticInfo, Is.Not.Null);
                                    detail = detail.InnerDiagnosticInfo!;
                                    expected = expected.InnerResult!;
                                }
                                Assert.That(detail.InnerStatusCode.Code, Is.EqualTo(expected.InnerResult!.StatusCode.Code));
                            }
                        }
                        else if (diagnostics)
                        {
                            ArrayOf<string> table = response.ResponseHeader.StringTable;
                            Assert.That(result.InputArgumentDiagnosticInfos.Count, Is.EqualTo(2));
                            Assert.That(result.InputArgumentDiagnosticInfos[0], Is.Null);
                            DiagnosticInfo detail = result.InputArgumentDiagnosticInfos[1];
                            Assert.That(table[detail.NamespaceUri], Is.EqualTo("urn:device:validation"));
                            Assert.That(table[detail.SymbolicId], Is.EqualTo("SpeedRejected"));
                            Assert.That(table[detail.Locale], Is.EqualTo("de"));
                            Assert.That(table[detail.LocalizedText], Is.EqualTo("Drehzahl zu hoch"));
                            Assert.That(detail.AdditionalInfo, Is.Null);
                            Assert.That(response.DiagnosticInfos.Count, Is.EqualTo(1));
                            Assert.That(table[response.DiagnosticInfos[0].SymbolicId], Is.EqualTo("CallRejected"));
                        }
                        else
                        {
                            Assert.That(result.InputArgumentDiagnosticInfos.IsEmpty, Is.True);
                            Assert.That(response.DiagnosticInfos.IsEmpty, Is.True);
                            Assert.That(response.ResponseHeader.StringTable.IsEmpty, Is.True);
                        }
                        Assert.That(channels.OpenCount, Is.EqualTo(1));
                        source.Verify(value => value.CallAsync(
                            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                            It.IsAny<CancellationToken>()), native ? Times.Once() : Times.Never());
                        contextual.Verify(value => value.InvokeAsync(
                            It.IsAny<WotInvokeRequest>(), It.IsAny<CancellationToken>()),
                            native ? Times.Never() : Times.Once());
                    }
                    finally
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await host.RemoveAsync(handle).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static WotProjectionDocument CreateCallProjection(WotCompiledForm form)
        {
            var namespaces = new NamespaceTable();
            ushort index = namespaces.GetIndexOrAppend("urn:wot-call");
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = namespaces,
                ServerUris = new StringTable(),
                TypeTable = new TypeTable(namespaces),
                EncodeableFactory = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()).Factory
            };
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("Owner", index),
                BrowseName = new QualifiedName("Owner", index),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
            };
            root.AddReference(Ua.ReferenceTypeIds.Organizes, true, Ua.ObjectIds.ObjectsFolder);
            var method = new MethodState(root)
            {
                NodeId = new NodeId("Run", index),
                BrowseName = new QualifiedName("Run", index),
                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
            };
            PropertyState<ArrayOf<Argument>> inputs = method.CreateOrReplaceInputArguments(
                context, null, assignInstanceNodeIds: false);
            inputs.Create(
                context, new NodeId("Run.InputArguments", index),
                QualifiedName.From(Ua.BrowseNames.InputArguments), LocalizedText.Null, assignNodeIds: false);
            inputs.DataType = Ua.DataTypeIds.Argument;
            inputs.Value =
            [
                new Argument { Name = "Mode", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar },
                new Argument { Name = "Speed", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
            ];
            root.AddChild(method);
            var nodeSet = new UANodeSet
            {
                Models = [new ModelTableEntry { ModelUri = "urn:wot-call", Version = "1.0.0" }]
            };
            nodeSet.Export(context, root);
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            WotBindingPlan plan = new WotBindingPlan("call-result", [], [form], [], []).WithProjectedAffordances(
            [
                new WotProjectedAffordance(
                    WotAffordanceKind.Action, "run", "/actions/run",
                    "nsu=urn:wot-call;s=Run", "nsu=urn:wot-call;s=Owner")
            ]);
            return new WotProjectionDocument(
                "call-result", [new WotProjectionSource("call-result", ["urn:wot-call"], stream.ToArray())], [plan]);
        }
    }
}
