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

#pragma warning disable CA2000
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    public partial class AsyncCustomNodeManagerTests
    {
        /// <summary>
        /// Verifies that the service calls of one request share one system context per node
        /// manager: two node managers in one request each get a context made from their own
        /// template, the copies are reused for the rest of the request, and a different
        /// request gets new copies.
        /// </summary>
        [Test]
        public async Task ReadSharesOneSystemContextPerNodeManagerAndRequestAsync()
        {
            if (m_managerType == AsyncCustomNodeManagerType.CustomNodeManager2ViaAdapter)
            {
                Assert.Ignore("CustomNodeManager2 copies its own system context per call.");
            }

            using ITestNodeManager first = CreateManager();
            using ITestNodeManager second = CreateManager();
            var firstContexts = new List<ISystemContext>();
            var secondContexts = new List<ISystemContext>();
            NodeId firstId = await AddCapturingVariableAsync(first, "First", firstContexts).ConfigureAwait(false);
            NodeId secondId = await AddCapturingVariableAsync(second, "Second", secondContexts).ConfigureAwait(false);

            var request = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);

            // Interleave the two node managers within one request twice.
            for (int round = 0; round < 2; round++)
            {
                await ReadValueAsync(first, request, firstId).ConfigureAwait(false);
                await ReadValueAsync(second, request, secondId).ConfigureAwait(false);
            }

            Assert.That(firstContexts, Has.Count.EqualTo(2));
            Assert.That(secondContexts, Has.Count.EqualTo(2));
            Assert.That(firstContexts[1], Is.SameAs(firstContexts[0]), "first manager's copy evicted");
            Assert.That(secondContexts[1], Is.SameAs(secondContexts[0]), "second manager's copy evicted");
            Assert.That(firstContexts[0], Is.Not.SameAs(secondContexts[0]));
            Assert.That(firstContexts[0], Is.Not.SameAs(first.SystemContext), "the template must not be handed out");

            var firstContext = (ServerSystemContext)firstContexts[0];
            var secondContext = (ServerSystemContext)secondContexts[0];
            Assert.That(firstContext.NodeIdFactory, Is.SameAs(first.SystemContext.NodeIdFactory));
            Assert.That(secondContext.NodeIdFactory, Is.SameAs(second.SystemContext.NodeIdFactory));
            Assert.That(firstContext.OperationContext, Is.SameAs(request));
            Assert.That(secondContext.OperationContext, Is.SameAs(request));

            // A different request (or session) never sees the copy of another one.
            var nextRequest = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            await ReadValueAsync(first, nextRequest, firstId).ConfigureAwait(false);

            Assert.That(firstContexts, Has.Count.EqualTo(3));
            Assert.That(firstContexts[2], Is.Not.SameAs(firstContexts[0]));
            Assert.That(((ServerSystemContext)firstContexts[2]).OperationContext, Is.SameAs(nextRequest));
            Assert.That(first.SystemContext.OperationContext, Is.Null, "the template must stay unbound");
        }

        /// <summary>
        /// Verifies that the per-request cache keeps a copy for every node manager a request
        /// touches up to its bound, and still returns a correct copy past it.
        /// </summary>
        [Test]
        public void OperationSystemContextCacheKeepsOneCopyPerTemplate()
        {
            var request = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var templates = new List<ServerSystemContext>();
            for (int ii = 0; ii < 12; ii++)
            {
                templates.Add(new ServerSystemContext(m_mockServer.Object) { SystemHandle = ii });
            }

            // Eight node managers in one request are all kept.
            var copies = new List<ServerSystemContext>();
            for (int ii = 0; ii < 8; ii++)
            {
                copies.Add(request.GetSystemContext(templates[ii]));
            }

            for (int ii = 0; ii < 8; ii++)
            {
                Assert.That(request.GetSystemContext(templates[ii]), Is.SameAs(copies[ii]));
            }

            // Past the bound a template still gets a correct copy bound to the request.
            for (int ii = 0; ii < templates.Count; ii++)
            {
                ServerSystemContext again = request.GetSystemContext(templates[ii]);
                Assert.That(again.SystemHandle, Is.EqualTo(ii));
                Assert.That(again.OperationContext, Is.SameAs(request));
                Assert.That(again, Is.Not.SameAs(templates[ii]));
            }
        }

        private async Task<NodeId> AddCapturingVariableAsync(
            ITestNodeManager manager,
            string name,
            List<ISystemContext> contexts)
        {
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId(name, nsIdx);
            variable.BrowseName = new QualifiedName(name, nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.Value = 1;
            variable.OnReadValue = (
                ISystemContext readContext,
                NodeState node,
                NumericRange indexRange,
                QualifiedName dataEncoding,
                ref Variant value,
                ref StatusCode statusCode,
                ref DateTimeUtc timestamp) =>
            {
                lock (contexts)
                {
                    contexts.Add(readContext);
                }

                return ServiceResult.Good;
            };

            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);
            return variable.NodeId;
        }

        private static async Task ReadValueAsync(
            ITestNodeManager manager,
            OperationContext request,
            NodeId nodeId)
        {
            var nodesToRead = new List<ReadValueId>
            {
                new() { NodeId = nodeId, AttributeId = Attributes.Value }
            };
            var values = new List<DataValue> { default };
            var errors = new List<ServiceResult> { null! };

            await manager.ReadAsync(request, 0, nodesToRead, values, errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True, errors[0]?.ToString());
        }
    }
}
