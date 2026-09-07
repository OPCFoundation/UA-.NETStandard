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
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotProjectedMethodRuntimeTests
    {
        [Test]
        public async Task InvokesOnlyFirstExecutableAlternativeAndPreservesOrderedOutputs()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            MethodState method = h.AddMethod(
                "Start",
                [new Argument { Name = "Speed", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                [
                    new Argument
                    {
                        Name = "Accepted", DataType = Ua.DataTypeIds.Boolean, ValueRank = ValueRanks.Scalar
                    },
                    new Argument
                    {
                        Name = "ActualSpeed", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar
                    }
                ]);
            WotCompiledForm first = ActionForm("start", 0);
            WotCompiledForm second = ActionForm("start", 1);
            var channel = new FakeWotBindingChannel(first)
            {
                OnInvoke = (inputs, _) =>
                {
                    Assert.That(inputs, Has.Count.EqualTo(1));
                    Assert.That(inputs[0].TryGetValue(out int speed), Is.True);
                    Assert.That(speed, Is.EqualTo(17));
                    return new ValueTask<WotInvokeResult>(new WotInvokeResult(
                        StatusCodes.Uncertain, [new DataValue(new Variant(true)), new DataValue(new Variant(18))]));
                }
            };
            h.ChannelFactory.SetChannel(first, channel);
            WotBindingPlan plan = WotProjectionBindingRuntimeTestHarness.Plan(first, second)
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(
                        WotAffordanceKind.Action, "start", "/actions/start",
                        method.NodeId.ToString(), h.Root.NodeId.ToString())
                ]);
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            IAsyncDisposable? runtime = await factory.CreateAsync(h.Builder, [plan]).ConfigureAwait(false);
            Assert.That(h.ChannelFactory.OpenCount, Is.Zero);

            var outputs = new List<Variant>();
            ServiceResult result = await method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [new Variant(17)], [], outputs).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(outputs, Has.Count.EqualTo(2));
            Assert.That(outputs[0].TryGetValue(out bool accepted) && accepted, Is.True);
            Assert.That(outputs[1].TryGetValue(out int actual) && actual == 18, Is.True);
            Assert.That(h.ChannelFactory.OpenedForms, Is.EqualTo(new[] { first }));
            Assert.That(channel.InvokeCount, Is.EqualTo(1));
            Assert.That(runtime, Is.Not.Null);
            await runtime!.DisposeAsync().ConfigureAwait(false);
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void LocalIdentityIsNotTakenFromTheUpstreamForm()
        {
            const string document = """
                {
                  "uav:id": "nsu=urn:local;s=Device",
                  "actions": {
                    "start/one": {
                      "uav:id": "nsu=urn:local;s=Start",
                      "forms": [{
                        "href": "opc.tcp://source:4840",
                        "op": "invokeaction",
                        "uav:id": "nsu=urn:source;s=Start",
                        "uav:componentOf": "nsu=urn:source;s=Device"
                      }]
                    }
                  }
                }
                """;

            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "resource-one", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document));

            Assert.That(request.ProjectedAffordances.Count, Is.EqualTo(1));
            WotProjectedAffordance local = request.ProjectedAffordances[0];
            Assert.That(local.NodeId, Is.EqualTo("nsu=urn:local;s=Start"));
            Assert.That(local.OwnerNodeId, Is.EqualTo("nsu=urn:local;s=Device"));
            Assert.That(local.JsonPointer, Is.EqualTo("/actions/start~1one"));
            Assert.That(request.Forms[0].TryGetString("uav:id", out string target), Is.True);
            Assert.That(target, Is.EqualTo("nsu=urn:source;s=Start"));
        }

        [TestCaseSource(nameof(s_failureStatuses))]
        public async Task UpstreamFailureIsNotReplacedBySuccessOrDefaultOutputs(StatusCode status)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            MethodState method = h.AddMethod("Run", [],
                [new Argument { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }]);
            WotCompiledForm form = ActionForm("run");
            var channel = new FakeWotBindingChannel(form)
            {
                OnInvoke = (_, _) => new ValueTask<WotInvokeResult>(new WotInvokeResult(status))
            };
            h.ChannelFactory.SetChannel(form, channel);
            IAsyncDisposable runtime = await WireMethodAsync(h, method, form).ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            var outputs = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [], [], outputs).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(status));
            Assert.That(outputs, Is.Empty);
            Assert.That(channel.InvokeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task MissingOutputsFailRatherThanReturningInitializedDefaults()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            MethodState method = h.AddMethod("Run", [],
                [new Argument { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }]);
            WotCompiledForm form = ActionForm("run");
            h.ChannelFactory.SetChannel(form, new FakeWotBindingChannel(form)
            {
                OnInvoke = (_, _) => new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good))
            });
            IAsyncDisposable runtime = await WireMethodAsync(h, method, form).ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            var outputs = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [], [], outputs).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public async Task NamespaceBearingOutputsUseTheAggregateNamespaceIndexes()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            ushort local = h.Builder.Context.NamespaceUris.GetIndexOrAppend("urn:result");
            ServiceMessageContext source = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            ushort remote = source.NamespaceUris.GetIndexOrAppend("urn:result");
            Assert.That(local, Is.Not.EqualTo(remote));
            MethodState method = h.AddMethod("GetReference", [],
            [
                new Argument { Name = "Node", DataType = Ua.DataTypeIds.NodeId, ValueRank = ValueRanks.Scalar },
                new Argument { Name = "Name", DataType = Ua.DataTypeIds.QualifiedName, ValueRank = ValueRanks.Scalar }
            ]);
            WotCompiledForm form = ActionForm("getReference");
            h.ChannelFactory.SetChannel(form, new FakeWotBindingChannel(form)
            {
                OnInvoke = (_, _) => new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good,
                [
                    new DataValue(new Variant(new NodeId("Sensor", remote))),
                    new DataValue(new Variant(new QualifiedName("State", remote)))
                ]).WithContext(source))
            });
            IAsyncDisposable runtime = await WireMethodAsync(h, method, form).ConfigureAwait(false);
            await using var owner = runtime.ConfigureAwait(false);
            var outputs = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [], [], outputs).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(2));
            Assert.That(outputs[0].TryGetValue(out NodeId node), Is.True);
            Assert.That(node, Is.EqualTo(new NodeId("Sensor", local)));
            Assert.That(outputs[1].TryGetValue(out QualifiedName name), Is.True);
            Assert.That(name, Is.EqualTo(new QualifiedName("State", local)));
        }

        [Test]
        public async Task SameAffordanceNameInDifferentResourcesKeepsItsLocalMethodAndChannel()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            ArrayOf<Argument> signature =
            [
                new Argument { Name = "Result", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
            ];
            MethodState left = h.AddMethod("LeftRun", [], signature);
            MethodState right = h.AddMethod("RightRun", [], signature);
            WotCompiledForm leftForm = ActionForm("run");
            WotCompiledForm rightForm = ActionForm("run");
            var leftChannel = new FakeWotBindingChannel(leftForm)
            {
                OnInvoke = (_, _) => new ValueTask<WotInvokeResult>(
                    new WotInvokeResult(StatusCodes.Good, [new DataValue(new Variant(11))]))
            };
            var rightChannel = new FakeWotBindingChannel(rightForm)
            {
                OnInvoke = (_, _) => new ValueTask<WotInvokeResult>(
                    new WotInvokeResult(StatusCodes.Good, [new DataValue(new Variant(22))]))
            };
            h.ChannelFactory.SetChannel(leftForm, leftChannel);
            h.ChannelFactory.SetChannel(rightForm, rightChannel);
            WotBindingPlan leftPlan = new WotBindingPlan("left-resource", [], [leftForm], [], [])
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(WotAffordanceKind.Action, "run", "/actions/run",
                        left.NodeId.ToString(), h.Root.NodeId.ToString())
                ]);
            WotBindingPlan rightPlan = new WotBindingPlan("right-resource", [], [rightForm], [], [])
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(WotAffordanceKind.Action, "run", "/actions/run",
                        right.NodeId.ToString(), h.Root.NodeId.ToString())
                ]);
            IAsyncDisposable? runtime = await new WotProjectionBindingRuntimeFactory(h.ChannelFactory)
                .CreateAsync(h.Builder, [leftPlan, rightPlan]).ConfigureAwait(false);
            Assert.That(runtime, Is.Not.Null);
            await using var owner = runtime!.ConfigureAwait(false);
            var leftOutputs = new List<Variant>();
            var rightOutputs = new List<Variant>();

            ServiceResult leftResult = await left.CallAsync(
                h.Builder.Context, h.Root.NodeId, [], [], leftOutputs).ConfigureAwait(false);
            ServiceResult rightResult = await right.CallAsync(
                h.Builder.Context, h.Root.NodeId, [], [], rightOutputs).ConfigureAwait(false);

            Assert.That(leftResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(rightResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(leftOutputs, Has.Count.EqualTo(1));
            Assert.That(rightOutputs, Has.Count.EqualTo(1));
            Assert.That(leftOutputs[0].TryGetValue(out int leftValue) && leftValue == 11, Is.True);
            Assert.That(rightOutputs[0].TryGetValue(out int rightValue) && rightValue == 22, Is.True);
            Assert.That(leftChannel.InvokeCount, Is.EqualTo(1));
            Assert.That(rightChannel.InvokeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task InvalidArgumentsAreRejectedBeforeOpeningAChannel()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            MethodState method = h.AddMethod("Run",
                [new Argument { Name = "Value", DataType = Ua.DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }], []);
            WotCompiledForm form = ActionForm("run");
            IAsyncDisposable runtime = await WireMethodAsync(h, method, form).ConfigureAwait(false);
            await using var owner = runtime.ConfigureAwait(false);
            var errors = new List<ServiceResult>();
            var outputs = new List<Variant>();

            ServiceResult missing = await method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [], errors, outputs).ConfigureAwait(false);
            ServiceResult excess = await method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [new Variant(1), new Variant(2)], errors, outputs)
                .ConfigureAwait(false);
            ServiceResult wrongType = await method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [new Variant("not-an-integer")], errors, outputs)
                .ConfigureAwait(false);

            Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
            Assert.That(excess.StatusCode, Is.EqualTo(StatusCodes.BadTooManyArguments));
            // MethodState reports argument errors separately; the node manager
            // turns this combination into the service's BadInvalidArgument.
            Assert.That(wrongType.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(outputs, Is.Empty);
            Assert.That(h.ChannelFactory.OpenCount, Is.Zero);
        }

        [Test]
        public async Task RequiredActionWithoutExecutorFailsGenerationActivation()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            MethodState method = h.AddMethod("Run", [], []);
            WotCompiledForm form = ActionForm("run", executable: false);

            ServiceResultException? error = null;
            try
            {
                await WireMethodAsync(h, method, form).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                error = exception;
            }

            Assert.That(error, Is.Not.Null);
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(method.OnCallMethod2Async, Is.Null);
            Assert.That(h.ChannelFactory.OpenCount, Is.Zero);
        }

        [Test]
        public async Task CallerCancellationInterruptsOnlyItsSharedChannelWait()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            MethodState method = h.AddMethod("Run", [], []);
            WotCompiledForm form = ActionForm("run");
            var open = new TaskCompletionSource<IWotBindingChannel>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var channel = new FakeWotBindingChannel(form)
            {
                OnInvoke = (_, _) => new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good))
            };
            h.ChannelFactory.SetOpener(form, () => new ValueTask<IWotBindingChannel>(open.Task));
            IAsyncDisposable runtime = await WireMethodAsync(h, method, form).ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task<ServiceResult> first = method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [], [], [], cancellation.Token).AsTask();
            Task<ServiceResult> second = method.CallAsync(h.Builder.Context, h.Root.NodeId, [], [], []).AsTask();
            cancellation.Cancel();

            try
            {
                Assert.That((await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                Assert.That(second.IsCompleted, Is.False);
            }
            finally
            {
                open.TrySetResult(channel);
            }
            Assert.That((await second.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(h.ChannelFactory.OpenCount, Is.EqualTo(1));
            Assert.That(channel.InvokeCount, Is.EqualTo(1));
        }

        private static async ValueTask<IAsyncDisposable> WireMethodAsync(
            WotProjectionBindingRuntimeTestHarness h, MethodState method, WotCompiledForm form)
        {
            WotBindingPlan plan = WotProjectionBindingRuntimeTestHarness.Plan(form).WithProjectedAffordances(
            [
                new WotProjectedAffordance(WotAffordanceKind.Action, form.AffordanceName,
                    "/actions/" + form.AffordanceName, method.NodeId.ToString(), h.Root.NodeId.ToString())
            ]);
            return await new WotProjectionBindingRuntimeFactory(h.ChannelFactory)
                .CreateAsync(h.Builder, [plan]).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Expected an active binding runtime.");
        }

        private static readonly TestCaseData[] s_failureStatuses =
        [
            new TestCaseData(StatusCodes.BadUserAccessDenied),
            new TestCaseData(StatusCodes.BadNoCommunication),
            new TestCaseData(StatusCodes.BadInvalidArgument)
        ];

        internal static WotCompiledForm ActionForm(string name, int alternative = 0, bool executable = true)
        {
            return new WotCompiledForm(
                new WotBindingIdentity("test", "1.0", "urn:test"),
                WotAffordanceKind.Action,
                name,
                $"/actions/{name}/forms/{alternative}",
                WoTBindingCapabilityEnum.InvokeAction,
                "invokeaction",
                new WotEndpointDescriptor("test", null, -1, "test://source"),
                new WotAddressingDescriptor(name),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.InvokeAction, "invokeaction", "Call"),
                new WotPayloadDescriptor("application/json", "json"),
                [],
                executable);
        }
    }
}
