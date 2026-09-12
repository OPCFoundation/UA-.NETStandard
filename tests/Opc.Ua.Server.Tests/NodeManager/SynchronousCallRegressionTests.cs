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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class SynchronousCallRegressionTests
    {
        [Test]
        public void SynchronousCallPropagatesDispatchFailureInsteadOfReturningAnUnfinishedResult()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new CallHooks(server.Object))
            using (var call = new CallCase(manager))
            {
                var expected = new InvalidOperationException("method failure");
                manager.DispatchFailure = expected;
                InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
                    () => call.Execute(manager));
                Assert.That(actual, Is.SameAs(expected));
            }
        }

        [Test]
        public void SynchronousMethodCallbackFailureRemainsAServiceError()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new CallHooks(server.Object))
            using (var call = new CallCase(manager))
            {
                manager.Method.OnCallMethod = (_, _, _, _) => throw new InvalidOperationException("callback failure");
                call.Execute(manager);
                call.AssertFailure(StatusCodes.Bad);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SuspendingAsyncHookIsUsedOnlyByTheAwaitableSurfaceAsync(bool asynchronous)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new CallHooks(server.Object) { DelayAsyncHook = true })
            using (var call = new CallCase(manager))
            {
                try
                {
                    if (asynchronous)
                    {
                        Task operation = call.ExecuteAsync(manager).AsTask();
                        Assert.That(operation.IsCompleted, Is.False);
                        manager.ReleaseAsyncHook();
                        await operation.ConfigureAwait(false);
                    }
                    else
                    {
                        call.Execute(manager);
                    }
                    call.AssertOutput();
                    Assert.That(manager.AsyncHookCalls, Is.EqualTo(asynchronous ? 1 : 0));
                }
                finally
                {
                    manager.ReleaseAsyncHook();
                    await manager.LastHook.ConfigureAwait(false);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MethodArgumentsAndResultsRemainConsistentAcrossCallSurfacesAsync(bool asynchronous)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new CallHooks(server.Object))
            using (var call = new CallCase(manager))
            {
                if (asynchronous)
                {
                    await call.ExecuteAsync(manager).ConfigureAwait(false);
                }
                else
                {
                    call.Execute(manager);
                }
                call.AssertOutput();
                Assert.That(call.Results[0].InputArgumentResults.Count, Is.Zero);
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task InputArgumentValidationIsPreservedAsync(bool asynchronous, bool invalid)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new CallHooks(server.Object))
            {
                manager.Method.InputArguments.Value =
                [
                    new Argument { Name = "Input", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
                ];
                using var call = new CallCase(
                    manager, [invalid ? new Variant("wrong type") : new Variant(1)]);
                if (asynchronous)
                {
                    await call.ExecuteAsync(manager).ConfigureAwait(false);
                }
                else
                {
                    call.Execute(manager);
                }
                if (invalid)
                {
                    call.AssertFailure(StatusCodes.BadInvalidArgument);
                    Assert.That(call.Results[0].InputArgumentResults, Has.Count.EqualTo(1));
                    Assert.That(call.Results[0].InputArgumentResults[0], Is.EqualTo(StatusCodes.BadTypeMismatch));
                }
                else
                {
                    call.AssertOutput();
                    Assert.That(call.Results[0].InputArgumentResults.Count, Is.Zero);
                }
            }
        }

        private sealed class CallCase : IDisposable
        {
            public CallCase(CallHooks manager, ArrayOf<Variant> arguments = default)
            {
                m_context = new OperationContext(new RequestHeader(), null, RequestType.Call, RequestLifetime.None);
                m_request = new CallMethodRequest
                {
                    ObjectId = manager.Parent.NodeId,
                    MethodId = manager.Method.NodeId,
                    InputArguments = arguments
                };
            }

            public List<CallMethodResult> Results { get; } = [null];

            public void Execute(CallHooks manager)
            {
                manager.Call(m_context, [m_request], Results, m_errors);
            }

            public ValueTask ExecuteAsync(CallHooks manager)
            {
                return manager.CallAsync(m_context, [m_request], Results, m_errors);
            }

            public void AssertOutput()
            {
                Assert.That(m_request.Processed, Is.True);
                Assert.That(m_errors[0], Is.Not.Null);
                Assert.That(Results[0], Is.Not.Null);
                Assert.That(m_errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(Results[0].OutputArguments, Has.Count.EqualTo(1));
                Assert.That(Results[0].OutputArguments[0].TryGetValue(out int output), Is.True);
                Assert.That(output, Is.EqualTo(42));
            }

            public void AssertFailure(StatusCode expected)
            {
                Assert.That(m_errors[0].StatusCode, Is.EqualTo(expected));
                Assert.That(Results[0].OutputArguments.Count, Is.Zero);
            }

            public void Dispose()
            {
                m_context.Dispose();
            }

            private readonly OperationContext m_context;
            private readonly CallMethodRequest m_request;
            private readonly List<ServiceResult> m_errors = [null];
        }

        private sealed class CallHooks : CustomNodeManager2
        {
            public CallHooks(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:synchronous-call")
            {
                Parent = new BaseObjectState(null)
                {
                    NodeId = new NodeId(1, NamespaceIndex),
                    BrowseName = new QualifiedName("Parent", NamespaceIndex)
                };
                Method = new MethodState(Parent)
                {
                    NodeId = new NodeId(2, NamespaceIndex),
                    BrowseName = new QualifiedName("Method", NamespaceIndex),
                    Executable = true,
                    UserExecutable = true
                };
                Method.InputArguments =
                    new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(Method)
                    {
                        Value = []
                    };
                Method.OutputArguments =
                    new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(Method)
                    {
                        Value = [new Argument
                        {
                            Name = "Result",
                            DataType = DataTypeIds.Int32,
                            ValueRank = ValueRanks.Scalar
                        }]
                    };
                Method.OnCallMethod = (_, _, _, output) =>
                {
                    output[0] = new Variant(42);
                    return ServiceResult.Good;
                };
                Parent.AddChild(Method);
                AddPredefinedNode(SystemContext, Parent);
            }

            public BaseObjectState Parent { get; }
            public MethodState Method { get; }
            public bool DelayAsyncHook { get; set; }
            public InvalidOperationException DispatchFailure { get; set; }
            public int AsyncHookCalls { get; private set; }
            public Task LastHook { get; private set; } = Task.CompletedTask;

            public void ReleaseAsyncHook()
            {
                m_release.TrySetResult(true);
            }

            protected override ValueTask CallInternalAsync(
                OperationContext context,
                ArrayOf<CallMethodRequest> methodsToCall,
                IList<CallMethodResult> results,
                IList<ServiceResult> errors,
                bool sync,
                CancellationToken cancellationToken = default)
            {
                if (!DelayAsyncHook)
                {
                    return base.CallInternalAsync(context, methodsToCall, results, errors, sync, cancellationToken);
                }
                AsyncHookCalls++;
                LastHook = WaitThenCallAsync(context, methodsToCall, results, errors, sync, cancellationToken);
                return new ValueTask(LastHook);
            }

            protected override ServiceResult Call(
                ISystemContext context,
                CallMethodRequest methodToCall,
                MethodState method,
                CallMethodResult result)
            {
                if (DispatchFailure != null)
                {
                    throw DispatchFailure;
                }
                return base.Call(context, methodToCall, method, result);
            }

            private async Task WaitThenCallAsync(
                OperationContext context,
                ArrayOf<CallMethodRequest> methodsToCall,
                IList<CallMethodResult> results,
                IList<ServiceResult> errors,
                bool sync,
                CancellationToken cancellationToken)
            {
                await m_release.Task.ConfigureAwait(false);
                await base.CallInternalAsync(
                    context, methodsToCall, results, errors, sync, cancellationToken).ConfigureAwait(false);
            }

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
