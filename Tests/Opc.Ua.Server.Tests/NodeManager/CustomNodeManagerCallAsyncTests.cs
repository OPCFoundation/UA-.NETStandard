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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

// CA2000: test code; the node manager is short-lived and torn down per test.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Deterministic, offline unit tests for the asynchronous <c>CallAsync</c>
    /// surface implemented in CustomNodeManagerAsync.cs.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("CustomNodeManagerCallAsync")]
    [Parallelizable(ParallelScope.All)]
    public class CustomNodeManagerCallAsyncTests
    {
        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };
        }

        private static TestableCustomNodeManager2 CreateManager()
        {
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            return new TestableCustomNodeManager2(
                mockServer.Object,
                CreateConfiguration(),
                false,
                new Mock<ILogger>().Object,
                DeterministicServerMock.TestNamespaceUri);
        }

        private static OperationContext NewContext()
        {
            return new OperationContext(
                new RequestHeader(), null, RequestType.Call, RequestLifetime.None);
        }

        [Test]
        public async Task CallAsyncWithNoMethodsDoesNothingAsync()
        {
            using TestableCustomNodeManager2 manager = CreateManager();
            var results = new List<CallMethodResult>();
            var errors = new List<ServiceResult>();

            await manager.CallAsync(
                NewContext(),
                Array.Empty<CallMethodRequest>().ToArrayOf(),
                results,
                errors).ConfigureAwait(false);

            Assert.That(results, Is.Empty);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public async Task CallAsyncWithForeignObjectLeavesRequestUnprocessedAsync()
        {
            using TestableCustomNodeManager2 manager = CreateManager();

            var request = new CallMethodRequest
            {
                ObjectId = ObjectIds.Server,
                MethodId = MethodIds.Server_GetMonitoredItems,
                InputArguments = []
            };
            var results = new List<CallMethodResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await manager.CallAsync(
                NewContext(),
                new[] { request }.ToArrayOf(),
                results,
                errors).ConfigureAwait(false);

            Assert.That(request.Processed, Is.False);
            Assert.That(errors[0], Is.Null);
            Assert.That(results[0], Is.Null);
        }

        [Test]
        public async Task CallAsyncWithUnknownMethodReturnsBadMethodInvalidAsync()
        {
            using TestableCustomNodeManager2 manager = CreateManager();

            var target = new BaseObjectState(null);
            target.CreateAsPredefinedNode(manager.SystemContext);
            target.NodeId = new NodeId("CallTargetObject", manager.NamespaceIndex);
            target.BrowseName = new QualifiedName("CallTargetObject", manager.NamespaceIndex);
            target.DisplayName = new LocalizedText("CallTargetObject");
            manager.AddPredefinedNodePublic(manager.SystemContext, target);

            var request = new CallMethodRequest
            {
                ObjectId = target.NodeId,
                MethodId = new NodeId("MissingMethod", manager.NamespaceIndex),
                InputArguments = []
            };
            var results = new List<CallMethodResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await manager.CallAsync(
                NewContext(),
                new[] { request }.ToArrayOf(),
                results,
                errors).ConfigureAwait(false);

            Assert.That(request.Processed, Is.True);
            Assert.That(errors[0], Is.Not.Null);
            Assert.That(
                errors[0].StatusCode,
                Is.EqualTo(StatusCodes.BadMethodInvalid));
        }
    }
}
