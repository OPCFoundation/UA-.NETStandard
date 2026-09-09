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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Parallelizable]
    public sealed class MethodInvocationResultTests
    {
        [Test]
        public void CompleteResultRejectsMissingOperationAndInputPositions()
        {
            Assert.Throws<ArgumentNullException>(() => new MethodInvocationResult(null!));
            Assert.Throws<ArgumentException>(() => new MethodInvocationResult(
                ServiceResult.Good, inputArgumentResults: [ServiceResult.Good, null!]));
        }

        [Test]
        public void CompleteResultOwnsItsValidatedArgumentArrays()
        {
            ServiceResult[] errors = [new ServiceResult(StatusCodes.BadTypeMismatch)];
            Variant[] outputs = [new Variant(42)];
            var actual = new MethodInvocationResult(StatusCodes.BadInvalidArgument, outputs, errors);

            errors[0] = null!;
            outputs[0] = new Variant(99);

            Assert.That(actual.InputArgumentResults[0].StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(actual.OutputArguments[0].TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
        }

        [TestCaseSource(nameof(s_statuses))]
        public async Task CompleteCallbackPreservesReceiverCancellationStatusAndOrderedOutputs(StatusCode status)
        {
            MethodState method = CreateMethod();
            SystemContext context = CreateContext();
            var receiver = new NodeId("actual-owner", 1);
            using var cancellation = new CancellationTokenSource();
            var expected = new ServiceResult(
                "urn:method", new StatusCode(status.Code, "MethodStatus"),
                new LocalizedText("en", "Method result"), "detail", innerResult: null);
            int calls = 0;
            method.OnCallMethodWithResultAsync = (actualContext, actualMethod, owner, inputs, token) =>
            {
                calls++;
                Assert.That(actualContext, Is.SameAs(context));
                Assert.That(actualMethod, Is.SameAs(method));
                Assert.That(owner, Is.EqualTo(receiver));
                Assert.That(token, Is.EqualTo(cancellation.Token));
                Assert.That(inputs.Count, Is.EqualTo(1));
                Assert.That(inputs[0].TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(31));
                return new ValueTask<MethodInvocationResult>(new MethodInvocationResult(expected, [new Variant(32)]));
            };
            var inputErrors = new List<ServiceResult>();
            var outputs = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                context, receiver, [new Variant(31)], inputErrors, outputs, cancellation.Token).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result, Is.SameAs(expected));
            Assert.That(inputErrors, Is.Empty);
            if (StatusCode.IsBad(status))
            {
                Assert.That(outputs, Is.Empty);
            }
            else
            {
                Assert.That(outputs, Has.Count.EqualTo(1));
                Assert.That(outputs[0].TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(32));
            }
        }

        [TestCase("input-count")]
        [TestCase("output-count")]
        [TestCase("null-result")]
        public async Task MalformedCompleteCallbacksCannotReturnPartialOutputs(string malformed)
        {
            MethodState method = CreateMethod();
            method.OnCallMethodWithResultAsync = (_, _, _, _, _) =>
                new ValueTask<MethodInvocationResult>(malformed switch
                {
                    "input-count" => new MethodInvocationResult(
                        StatusCodes.BadInvalidArgument, [new Variant(10)],
                        [ServiceResult.Good, new ServiceResult(StatusCodes.BadTypeMismatch)]),
                    "output-count" => new MethodInvocationResult(ServiceResult.Good, []),
                    _ => null!
                });
            var outputs = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                CreateContext(), NodeId.Null, [new Variant(1)], [], outputs).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(outputs, Is.Empty);
            Assert.That(result.LocalizedText.Text, Does.Contain("Method"));
        }

        [Test]
        public async Task CompleteCallbackRetainsAnOmittedUpstreamInputResultArray()
        {
            MethodState method = CreateMethod();
            method.OnCallMethodWithResultAsync = (_, _, _, _, _) =>
                new ValueTask<MethodInvocationResult>(new MethodInvocationResult(StatusCodes.BadInvalidArgument));
            var errors = new List<ServiceResult>();
            var outputs = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                CreateContext(), NodeId.Null, [new Variant(1)], errors, outputs).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(errors, Is.Empty, "Local validation cannot fabricate results omitted by the selected source.");
            Assert.That(outputs, Is.Empty);
        }

        [TestCase("missing")]
        [TestCase("extra")]
        [TestCase("type")]
        public async Task InvalidInputsDoNotEnterTheCompleteCallback(string kind)
        {
            MethodState method = CreateMethod();
            int calls = 0;
            method.OnCallMethodWithResultAsync = (_, _, _, _, _) =>
            {
                calls++;
                return new ValueTask<MethodInvocationResult>(new MethodInvocationResult(ServiceResult.Good));
            };
            ArrayOf<Variant> inputs = kind switch
            {
                "missing" => [],
                "extra" => [new Variant(1), new Variant(2)],
                _ => [new Variant("not an integer")]
            };
            var errors = new List<ServiceResult>();
            var outputs = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                CreateContext(), NodeId.Null, inputs, errors, outputs).ConfigureAwait(false);

            Assert.That(calls, Is.Zero);
            Assert.That(outputs, Is.Empty);
            if (kind == "type")
            {
                Assert.That(errors, Has.Count.EqualTo(1));
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            }
            else
            {
                Assert.That(result.StatusCode, Is.EqualTo(kind == "missing"
                    ? StatusCodes.BadArgumentsMissing : StatusCodes.BadTooManyArguments));
            }
        }

        [Test]
        public void SynchronousCallDoesNotRunAnAsynchronousCompleteCallback()
        {
            MethodState method = CreateMethod();
            int calls = 0;
            method.OnCallMethodWithResultAsync = (_, _, _, _, _) =>
            {
                calls++;
                return new ValueTask<MethodInvocationResult>(new MethodInvocationResult(ServiceResult.Good));
            };
            var outputs = new List<Variant>();

            ServiceResult result = method.Call(
                CreateContext(), NodeId.Null, [new Variant(1)], [], outputs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(outputs, Is.Empty);
            Assert.That(calls, Is.Zero);
        }

        private static SystemContext CreateContext()
        {
            var namespaces = new NamespaceTable();
            return new SystemContext(null)
            {
                NamespaceUris = namespaces,
                TypeTable = new TypeTable(namespaces)
            };
        }

        private static MethodState CreateMethod()
        {
            var method = new MethodState(null);
            method.InputArguments = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
            method.InputArguments.Value =
            [
                new Argument { Name = "Input", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
            ];
            method.OutputArguments = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
            method.OutputArguments.Value =
            [
                new Argument { Name = "Output", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
            ];
            return method;
        }

        private static readonly StatusCode[] s_statuses = [StatusCodes.Good, StatusCodes.Uncertain, StatusCodes.Bad];
    }
}
