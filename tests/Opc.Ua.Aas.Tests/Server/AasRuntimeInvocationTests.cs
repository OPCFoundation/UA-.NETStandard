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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Aas.Tests.Server
{
    /// <summary>
    /// Tests the AAS runtime callbacks wired onto materialized nodes.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasRuntimeInvocationTests
    {
        [Test]
        public async Task ValueReadAndWriteAreServedThroughTheValueProviderAsync()
        {
            var valueProvider = new Mock<IAasValueProvider>(MockBehavior.Strict);
            valueProvider
                .Setup(p => p.ReadValueAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AasValueReadResult(ServiceResult.Good, Variant.From("before"),
                    StatusCodes.Good, DateTime.UtcNow));
            valueProvider
                .Setup(p => p.WriteValueAsync(It.IsAny<NodeId>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);

            RuntimeCallbacks callbacks = await CreateCallbacksAsync(
                valueProvider.Object,
                new DefaultAasOperationHandler()).ConfigureAwait(false);
            NodeId valueNodeId = AasServerTestData.MemberNodeId(
                AasServerTestData.ElementNodeId(AasServerTestData.PropertyName),
                "Value");

            AttributeReadResult read = await callbacks.Reads[valueNodeId](
                null!,
                null!,
                NumericRange.Null,
                QualifiedName.Null,
                CancellationToken.None).ConfigureAwait(false);
            AttributeWriteResult write = await callbacks.Writes[valueNodeId](
                null!,
                null!,
                NumericRange.Null,
                Variant.From("after"),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(read.Result), Is.True);
                Assert.That(GetString(read.Value), Is.EqualTo("before"));
                Assert.That(ServiceResult.IsGood(write.Result), Is.True);
            });
            valueProvider.Verify(
                p => p.ReadValueAsync(valueNodeId, It.IsAny<CancellationToken>()),
                Times.Once);
            valueProvider.Verify(
                p => p.WriteValueAsync(valueNodeId, It.IsAny<Variant>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task InvokeWithCorrectArityReturnsOperationOutputsAsync()
        {
            var handler = new Mock<IAasOperationHandler>(MockBehavior.Strict);
            handler
                .Setup(h => h.InvokeAsync(It.IsAny<AasOperationInvokeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AasOperationInvokeResult(
                    new ArrayOf<Variant>(new[] { Variant.From("done") }),
                    new ArrayOf<Variant>(new[] { Variant.From("updated") }),
                    true,
                    string.Empty));
            RuntimeCallbacks callbacks = await CreateCallbacksAsync(
                new DocumentAasValueProvider(),
                handler.Object).ConfigureAwait(false);

            List<Variant> outputArguments = [];
            ServiceResult result = await callbacks.Calls[InvokeNodeId()](
                null!,
                null!,
                AasServerTestData.ElementNodeId(AasServerTestData.OperationName),
                InvokeArguments(1, 1),
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(outputArguments, Has.Count.EqualTo(4));
                Assert.That(GetString(GetVariantArray(outputArguments[0])[0]), Is.EqualTo("done"));
                Assert.That(GetString(GetVariantArray(outputArguments[1])[0]), Is.EqualTo("updated"));
                Assert.That(GetBoolean(outputArguments[2]), Is.True);
                Assert.That(GetString(outputArguments[3]), Is.Empty);
            });
        }

        [TestCase(0, 1)]
        [TestCase(2, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 2)]
        public async Task InvokeWithArityMismatchReturnsBadInvalidArgumentAsync(
            int inputCount,
            int inoutputCount)
        {
            RuntimeCallbacks callbacks = await CreateCallbacksAsync(
                new DocumentAasValueProvider(),
                new DefaultAasOperationHandler()).ConfigureAwait(false);

            List<Variant> outputArguments = [];
            ServiceResult result = await callbacks.Calls[InvokeNodeId()](
                null!,
                null!,
                AasServerTestData.ElementNodeId(AasServerTestData.OperationName),
                InvokeArguments(inputCount, inoutputCount),
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
                Assert.That(outputArguments, Is.Empty);
            });
        }

        [Test]
        public async Task InvokeOperationFailureReturnsGoodCallStatusAndFalseSuccessAsync()
        {
            var handler = new Mock<IAasOperationHandler>(MockBehavior.Strict);
            handler
                .Setup(h => h.InvokeAsync(It.IsAny<AasOperationInvokeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AasOperationInvokeResult(
                    new ArrayOf<Variant>(new[] { Variant.From("rejected") }),
                    new ArrayOf<Variant>(new[] { Variant.From("unchanged") }),
                    false,
                    "workpiece rejected"));
            RuntimeCallbacks callbacks = await CreateCallbacksAsync(
                new DocumentAasValueProvider(),
                handler.Object).ConfigureAwait(false);

            List<Variant> outputArguments = [];
            ServiceResult result = await callbacks.Calls[InvokeNodeId()](
                null!,
                null!,
                AasServerTestData.ElementNodeId(AasServerTestData.OperationName),
                InvokeArguments(1, 1),
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(GetBoolean(outputArguments[2]), Is.False);
                Assert.That(GetString(outputArguments[3]), Is.EqualTo("workpiece rejected"));
            });
        }

        private static async Task<RuntimeCallbacks> CreateCallbacksAsync(
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler)
        {
            var callbacks = new RuntimeCallbacks();
            var builder = new Mock<INodeManagerBuilder>(MockBehavior.Strict);
            builder
                .Setup(b => b.Node(It.IsAny<NodeId>()))
                .Returns((NodeId nodeId) => callbacks.CreateNodeBuilder(nodeId));

            // The runtime rebases the authored namespace index onto the index
            // the Server assigned, so the context has to answer with a table
            // that places the AAS namespace where the test expects its nodes.
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3);
            builder
                .Setup(b => b.Context)
                .Returns(new SystemContext(telemetry: null!) { NamespaceUris = namespaces });

            Type runtimeType = typeof(AasServerOptions).Assembly.GetType(
                "Opc.Ua.Aas.Server.Materialization.AasEnvironmentRuntime")!;
            object runtime = Activator.CreateInstance(
                runtimeType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    AasServerTestData.CreateEnvironment(),
                    valueProvider,
                    operationHandler
                },
                culture: null)!;
            object valueTask = runtimeType.GetMethod("ConfigureAsync")!.Invoke(
                runtime,
                new object[] { builder.Object, CancellationToken.None })!;
            var task = (Task)valueTask.GetType().GetMethod("AsTask")!.Invoke(valueTask, null)!;
            await task.ConfigureAwait(false);
            return callbacks;
        }

        private static NodeId InvokeNodeId()
        {
            return AasServerTestData.MemberNodeId(
                AasServerTestData.ElementNodeId(AasServerTestData.OperationName),
                "Invoke");
        }

        private static ArrayOf<Variant> InvokeArguments(int inputCount, int inoutputCount)
        {
            return new ArrayOf<Variant>(new[]
            {
                Variant.From(CreateValues(inputCount, "input")),
                Variant.From(CreateValues(inoutputCount, "inout")),
                Variant.From(0d)
            });
        }

        private static Variant[] CreateValues(int count, string prefix)
        {
            var values = new Variant[count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Variant.From(prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return values;
        }

        private static ArrayOf<Variant> GetVariantArray(Variant variant)
        {
            object? value = variant.AsBoxedObject(Variant.BoxingBehavior.Legacy);
            return value is ArrayOf<Variant> array
                ? array
                : new ArrayOf<Variant>((Variant[])value!);
        }

        private static string? GetString(Variant variant)
        {
            return variant.AsBoxedObject(Variant.BoxingBehavior.Legacy) as string;
        }

        private static bool GetBoolean(Variant variant)
        {
            return variant.AsBoxedObject(Variant.BoxingBehavior.Legacy) is bool value && value;
        }

        private sealed class RuntimeCallbacks
        {
            public Dictionary<NodeId, GenericMethodCalledEventHandler2Async> Calls { get; } = [];

            public Dictionary<NodeId, NodeValueEventHandlerAsync> Reads { get; } = [];

            public Dictionary<NodeId, NodeValueWriteEventHandlerAsync> Writes { get; } = [];

            public INodeBuilder CreateNodeBuilder(NodeId nodeId)
            {
                var node = new Mock<INodeBuilder>(MockBehavior.Strict);
                node
                    .Setup(n => n.OnCall(It.IsAny<GenericMethodCalledEventHandler2Async>()))
                    .Callback<GenericMethodCalledEventHandler2Async>(h => Calls[nodeId] = h)
                    .Returns(node.Object);
                node
                    .Setup(n => n.OnRead(It.IsAny<NodeValueEventHandlerAsync>()))
                    .Callback<NodeValueEventHandlerAsync>(h => Reads[nodeId] = h)
                    .Returns(node.Object);
                node
                    .Setup(n => n.OnWrite(It.IsAny<NodeValueWriteEventHandlerAsync>()))
                    .Callback<NodeValueWriteEventHandlerAsync>(h => Writes[nodeId] = h)
                    .Returns(node.Object);
                return node.Object;
            }
        }
    }
}
