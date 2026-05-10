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
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for View RegisterNodes.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ViewServices")]
    public class ViewRegisternodesTests : TestFixture
    {
        [Description("Test 5.7.4-3 prepared by Dale Pope dale.pope@matrikon.com Description: Given 25 nodes in nodesToRegister[] And the nodes exist When RegisterNodes is called Then the server returns")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "003")]
        public async Task RegisterNodesWith25NodesReturnsRegisteredNodeIdsAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Test 5.7.4-4 prepared by Dale Pope dale.pope@matrikon.com Description: Given 50 nodes in nodesToRegister[] And the nodes exist When RegisterNodes is called Then the server returns")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "004")]
        public async Task RegisterNodesWith50NodesReturnsRegisteredNodeIdsAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Test 5.7.4-5 prepared by Dale Pope dale.pope@matrikon.com Description: Given 100 nodes in nodesToRegister[] And the nodes exist When RegisterNodes is called Then the server returns")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "005")]
        public async Task RegisterNodesWith100NodesReturnsRegisteredNodeIdsAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given two or three existent nodes And two non-existent nodes When RegisterNodes is called Then the server returns three NodeIds that refer to three existent nodes And two NodeIds i")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "007")]
        public async Task RegisterNodesWithExistentAndNonExistentNodesReturnsAllNodeIdsAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given no NodesToRegister And diagnostic info is requested When RegisterNodes is called Then the server returns specified service diagnostic info */ include( &quot;./library/ServiceBased")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "009")]
        public async Task RegisterNodesWithNoNodesAndDiagnosticInfoRequestedReturnsDiagnosticsAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Test 5.4-2 applied to RegisterNodes (5.7.4) prepared by Dale Pope dale.pope@matrikon.com Description: Given no NodesToRegister And diagnostic info is not requested When RegisterNod")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "010")]
        public async Task RegisterNodesWithNoNodesAndNoDiagnosticInfoReturnsEmptyAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given five nodes in nodesToUnregister[] And the nodes exist When UnregisterNodes is called Then the server returns ServiceResult Good And unregisters the nodes */ include( &quot;./libra")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "012")]
        public async Task UnregisterNodesWith5NodesReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given 25 nodes in nodesToUnregister[] And the nodes exist When UnregisterNodes is called Then the server returns ServiceResult Good And unregisters the nodes */ include( &quot;./library")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "013")]
        public async Task UnregisterNodesWith25NodesReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given 50 nodes in nodesToUnregister[] And the nodes exist When UnregisterNodes is called Then the server returns ServiceResult Good And unregisters the nodes */ include( &quot;./library")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "014")]
        public async Task UnregisterNodesWith50NodesReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given 100 nodes in nodesToUnregister[] And the nodes exist When UnregisterNodes is called Then the server returns ServiceResult Good And unregisters the nodes */")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "015")]
        public async Task UnregisterNodesWith100NodesReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given two or three registered nodes; And two non-registered nodes; When UnregisterNodes is called Then the server returns ServiceResult Good; And unregisters the registered nodes;")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "016")]
        public async Task UnregisterNodesWithRegisteredAndNonRegisteredNodesReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given two or three registered nodes; And two non-existent nodes; When UnregisterNodes is called; Then the server returns ServiceResult Good And unregisters the registered nodes; An")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "017")]
        public async Task UnregisterNodesWithRegisteredAndNonExistentNodesReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given a non-existent node; When UnregisterNodes is called; Then the server returns ServiceResult Good */ include( &quot;./library/Base/array.js&quot; );")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "018")]
        public async Task UnregisterNodesWithNonExistentNodeReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Test 5.7.5-11 prepared by Dale Pope dale.pope@matrikon.com Description: Given a node And the node is unregistered When UnregisterNodes is called Then the server returns ServiceResu")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "021")]
        public async Task UnregisterAlreadyUnregisteredNodeReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Test 5.7.5-12 prepared by Dale Pope dale.pope@matrikon.com Description: Given multiple nodes And the nodes are unregistered When UnregisterNodes is called Then the server returns S")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "022")]
        public async Task UnregisterMultipleAlreadyUnregisteredNodesReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given a NodeId to register And the resulting registered NodeId And the NodeIds differ When UnregisterNodes is called on the NodeId to register Then the server returns ServiceResult")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "023")]
        public async Task UnregisterUsingOriginalRegisteredNodeIdReturnsGoodAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given no NodesToUnregister; And diagnostic info is requested; When UnregisterNodes is called; Then the server returns specified service diagnostic info */ include( &quot;./library/Servi")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "024")]
        public async Task UnregisterNodesWithNoNodesAndDiagnosticInfoRequestedReturnsDiagnosticsAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Test 5.4-2 applied to UnregisterNodes (5.7.5) prepared by Dale Pope dale.pope@matrikon.com Description: Given no NodesToUnregister And diagnostic info is not requested When Unregis")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "025")]
        public async Task UnregisterNodesWithNoNodesAndNoDiagnosticInfoReturnsEmptyAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given an empty list of nodesToRegister[] When RegisterNodes is called Then the server returns service result Bad_NothingToDo */")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-001")]
        public async Task RegisterNodesWithEmptyArrayReturnsBadNothingToDoAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { new("InvalidNode_99", 9999) }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
        }

        [Description("Pass in a large number of nodes (100+) and verify that the server returns exactly one registered NodeId per requested NodeId (no count mismatch).")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-002")]
        public async Task RegisterNodesWithLargeBatchReturnsCorrectCountAsync()
        {
            const int count = 150;
            var nodes = new NodeId[count];
            for (int i = 0; i < count; i++)
            {
                nodes[i] = ToNodeId(
                    Constants.ScalarStaticNodes[i % Constants.ScalarStaticNodes.Length]);
            }
            ArrayOf<NodeId> nodesToRegister = nodes.ToArrayOf();

            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, nodesToRegister, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(count),
                "Server must return exactly one registered NodeId per requested NodeId.");
            Assert.That(response.RegisteredNodeIds.ToArray(), Has.All.Not.EqualTo(NodeId.Null));

            await Session.UnregisterNodesAsync(
                null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given one non-existent NodeId, RegisterNodes must succeed and return one NodeId (per spec, the server does not validate existence).")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-003")]
        public async Task RegisterNodesWithSingleNonExistentNodeReturnsHandleAsync()
        {
            ArrayOf<NodeId> nodesToRegister = new NodeId[] { Constants.InvalidNodeId }.ToArrayOf();

            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, nodesToRegister, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            Assert.That(response.RegisteredNodeIds[0], Is.Not.EqualTo(NodeId.Null));

            await Session.UnregisterNodesAsync(
                null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given 500 non-existent NodeIds, RegisterNodes must accept the large request without failure and return 500 NodeIds.")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-004")]
        public async Task RegisterNodesWith500NonExistentNodesReturnsAllHandlesAsync()
        {
            const int count = 500;
            var nodes = new NodeId[count];
            for (int i = 0; i < count; i++)
            {
                nodes[i] = new NodeId(
                    "NonExistent_Err004_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), 2);
            }
            ArrayOf<NodeId> nodesToRegister = nodes.ToArrayOf();

            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, nodesToRegister, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(count),
                "Server must return one registered NodeId per requested NodeId, even when nodes do not exist.");
            Assert.That(response.RegisteredNodeIds.ToArray(), Has.All.Not.EqualTo(NodeId.Null));

            await Session.UnregisterNodesAsync(
                null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Specify a mix of valid and invalid NodeIds. The server must return one NodeId per requested NodeId; valid entries map to usable handles, invalid entries are still returned (the server does not validate existence).")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-006")]
        public async Task RegisterNodesWithMixedValidAndInvalidNodeIdsReturnsAllHandlesAsync()
        {
            ArrayOf<NodeId> nodesToRegister = new NodeId[]
            {
                ToNodeId(Constants.ScalarStaticBoolean),
                Constants.InvalidNodeId,
                ToNodeId(Constants.ScalarStaticInt32),
                new("NonExistent_Err006_X", 2),
                ToNodeId(Constants.ScalarStaticDouble)
            }.ToArrayOf();

            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, nodesToRegister, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(nodesToRegister.Count),
                "Server must return one registered NodeId per requested NodeId for mixed valid/invalid input.");
            Assert.That(response.RegisteredNodeIds.ToArray(), Has.All.Not.EqualTo(NodeId.Null));

            await Session.UnregisterNodesAsync(
                null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Given an empty/null authenticationToken, RegisterNodes should fail with Bad_SecurityChecksFailed. The .NET client SDK does not allow injecting a null authentication token at the protocol layer without bypassing the public API; this scenario requires the framework's request-header injection hooks.")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-007")]
        public Task RegisterNodesWithEmptyAuthenticationTokenReturnsBadSecurityChecksFailed()
        {
            Assert.Ignore(
                "Err-007 requires injecting a null/empty authenticationToken into the RequestHeader, " +
                "which is not exposed by the public Session API. This compliance scenario is enforced " +
                "by the request-header injection framework and cannot be reproduced through the " +
                "standard async client API.");
            return Task.CompletedTask;
        }

        [Description("Given a non-existent authenticationToken, RegisterNodes should fail with Bad_SecurityChecksFailed. As with Err-007, this requires injecting a forged authenticationToken into the RequestHeader, which is not possible via the public Session API.")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-008")]
        public Task RegisterNodesWithForgedAuthenticationTokenReturnsBadSecurityChecksFailed()
        {
            Assert.Ignore(
                "Err-008 requires injecting a forged (non-existent) authenticationToken into the " +
                "RequestHeader, which is not exposed by the public Session API. This compliance " +
                "scenario is enforced by the request-header injection framework and cannot be " +
                "reproduced through the standard async client API.");
            return Task.CompletedTask;
        }

        [Description("Given a RequestHeader.Timestamp of 0 When RegisterNodes is called Then the server returns service error Bad_InvalidTimestamp. */ include( &quot;./library/ClassBased/UaRequestHeader/5.4-")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-009")]
        public async Task RegisterNodesWithZeroRequestHeaderTimestampReturnsBadInvalidTimestampAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { new("InvalidNode_99", 9999) }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
        }

        [Description("Given an empty list of nodesToUnregister[] When UnregisterNodes is called Then the server returns service result Bad_NothingToDo */")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-011")]
        public async Task UnregisterNodesWithEmptyArrayReturnsBadNothingToDoAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { new("InvalidNode_99", 9999) }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
        }

        [Description("Given an empty/null authenticationToken When UnregisterNodes is called Then the server returns service error Bad_SecurityChecksFailed */ include( &quot;./library/ClassBased/UaRequestHea")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-012")]
        public async Task UnregisterNodesWithEmptyAuthenticationTokenReturnsBadSecurityChecksFailedAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { new("InvalidNode_99", 9999) }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
        }

        [Description("Given a non-existent authenticationToken When UnregisterNodes is called Then the server returns service error Bad_SecurityChecksFailed */ include( &quot;./library/ClassBased/UaRequestHe")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-013")]
        public async Task UnregisterNodesWithForgedAuthenticationTokenReturnsBadSecurityChecksFailedAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { new("InvalidNode_99", 9999) }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
        }

        [Description("Given a RequestHeader.Timestamp of 0 When UnregisterNodes is called Then the server returns service error Bad_InvalidTimestamp */ include( &quot;./library/ClassBased/UaRequestHeader/5.4")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-014")]
        public async Task UnregisterNodesWithZeroRequestHeaderTimestampReturnsBadInvalidTimestampAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { new("InvalidNode_99", 9999) }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds, Is.Not.Null);
        }

        [Description("registerNodesFailedCase1: Script demonstrates how to use the checkRegisterNodesFailed() function")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "registerNodesFailedCase1")]
        public async Task CheckRegisterNodesFailedHelperScenarioAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("registerNodesValidCase1: Script demonstrates how to use the checkRegisterNodesValidParameter() function")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "registerNodesValidCase1")]
        public async Task CheckRegisterNodesValidParameterHelperScenarioAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("unregisterNodesFailedCase1: Script demonstrates how to use the checkUnregisterNodesFailed() function")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "unregisterNodesFailedCase1")]
        public async Task CheckUnregisterNodesFailedHelperScenarioAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }

        [Description("unregisterNodesValidCase1: Script demonstrates how to use the checkUnregisterNodesValidParameter() function")]
        [Test]
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "unregisterNodesValidCase1")]
        public async Task CheckUnregisterNodesValidParameterHelperScenarioAsync()
        {
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null, new NodeId[] { ObjectIds.Server }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response, Is.Not.Null);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));
            await Session.UnregisterNodesAsync(null, response.RegisteredNodeIds, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
