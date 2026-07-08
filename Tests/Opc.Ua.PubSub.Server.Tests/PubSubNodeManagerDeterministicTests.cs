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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Deterministic, offline coverage for the residual address-space,
    /// configuration-file, security-group, key-push-target and per-instance
    /// status paths of <see cref="PubSubNodeManager"/> that are not already
    /// exercised by <see cref="PubSubNodeManagerTests"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSubNodeManagerDeterministic")]
    public class PubSubNodeManagerDeterministicTests
    {
        [Test]
        public async Task CreateAddressSpaceAsync_WithoutDiagnosticsNodeManager_DoesNotBindMethods()
        {
            using var harness = new Harness(withoutDiagnosticsNodeManager: true);

            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.Manager.AreMethodsBound, Is.False);
                Assert.That(harness.Manager.StatusBinding, Is.Null);
                Assert.That(harness.EnableMethod.OnCallMethod, Is.Null);
                Assert.That(
                    harness.Manager.FindPredefinedNode<BaseObjectState>(
                        new NodeId("pubsub:configuration", harness.Manager.AddressSpaceNamespaceIndex)),
                    Is.Null);
            });
        }

        [Test]
        public async Task RebuildConfigurationAddressSpace_WithRichTopology_MaterializesInstanceNodes()
        {
            Mock<IPubSubApplication> app = CreateRichApplicationMock();
            using var harness = new Harness(applicationOverride: app.Object);
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            ushort ns = harness.Manager.AddressSpaceNamespaceIndex;

            BaseObjectState connection = harness.Manager.FindPredefinedNode<BaseObjectState>(
                new NodeId("pubsub:connection:Conn1", ns));
            BaseObjectState writerGroup = harness.Manager.FindPredefinedNode<BaseObjectState>(
                new NodeId("pubsub:writer-group:Conn1:WG1", ns));
            BaseObjectState writer = harness.Manager.FindPredefinedNode<BaseObjectState>(
                new NodeId("pubsub:writer:Conn1:WG1:W1", ns));
            BaseObjectState readerGroup = harness.Manager.FindPredefinedNode<BaseObjectState>(
                new NodeId("pubsub:reader-group:Conn1:RG1", ns));
            BaseObjectState reader = harness.Manager.FindPredefinedNode<BaseObjectState>(
                new NodeId("pubsub:reader:Conn1:RG1:R1", ns));
            BaseObjectState publishedDataSet = harness.Manager.FindPredefinedNode<BaseObjectState>(
                new NodeId("pubsub:published-data-set:PDS1", ns));
            var addWriter = (MethodState)writerGroup.FindChild(
                harness.Context, new QualifiedName("AddDataSetWriter", ns))!;
            var addReader = (MethodState)readerGroup.FindChild(
                harness.Context, new QualifiedName("AddDataSetReader", ns))!;
            var addVariables = (MethodState)publishedDataSet.FindChild(
                harness.Context, new QualifiedName("AddVariables", ns))!;
            var publishedData = (BaseDataVariableState)publishedDataSet.FindChild(
                harness.Context, new QualifiedName("PublishedData", ns))!;

            Assert.Multiple(() =>
            {
                Assert.That(connection.TypeDefinitionId, Is.EqualTo(new NodeId(14209u)));
                Assert.That(connection.BrowseName.Name, Is.EqualTo("Conn1"));
                Assert.That(writerGroup.TypeDefinitionId, Is.EqualTo(new NodeId(17725u)));
                Assert.That(writerGroup.BrowseName.Name, Is.EqualTo("WG1"));
                Assert.That(writer.TypeDefinitionId, Is.EqualTo(new NodeId(15298u)));
                Assert.That(writer.BrowseName.Name, Is.EqualTo("W1"));
                Assert.That(readerGroup.TypeDefinitionId, Is.EqualTo(new NodeId(17999u)));
                Assert.That(readerGroup.BrowseName.Name, Is.EqualTo("RG1"));
                Assert.That(reader.TypeDefinitionId, Is.EqualTo(new NodeId(15306u)));
                Assert.That(reader.BrowseName.Name, Is.EqualTo("R1"));
                Assert.That(publishedDataSet.TypeDefinitionId, Is.EqualTo(new NodeId(14534u)));
                Assert.That(publishedDataSet.BrowseName.Name, Is.EqualTo("PDS1"));
                Assert.That(addWriter, Is.Not.Null);
                Assert.That(addReader, Is.Not.Null);
                Assert.That(addVariables, Is.Not.Null);
                Assert.That(publishedData.BrowseName.Name, Is.EqualTo("PublishedData"));
            });
        }

        [Test]
        public async Task PubSubConfigurationFileRead_WithInvalidArguments_ReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            MethodState read = FindConfigurationFileMethod(harness, "Read");

            ServiceResult missingArguments = read.OnCallMethod!(
                harness.Context, read, BuildArray(Variant.From(1u)), new List<Variant>());
            ServiceResult unknownHandle = read.OnCallMethod!(
                harness.Context,
                read,
                BuildArray(Variant.From(4242u), Variant.From(1024)),
                new List<Variant>());

            Assert.Multiple(() =>
            {
                Assert.That(missingArguments.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(unknownHandle.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
            });
        }

        [Test]
        public async Task PubSubConfigurationFileWrite_WithInvalidArguments_ReturnsExpectedStatus()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            MethodState open = FindConfigurationFileMethod(harness, "Open");
            MethodState write = FindConfigurationFileMethod(harness, "Write");
            var openOutputs = new List<Variant>();
            open.OnCallMethod!(harness.Context, open, BuildArray(Variant.From((byte)1)), openOutputs);
            Assert.That(openOutputs[0].TryGetValue(out uint readHandle), Is.True);

            ServiceResult missingArguments = write.OnCallMethod!(
                harness.Context, write, BuildArray(Variant.From(readHandle)), []);
            ServiceResult readOnlyHandle = write.OnCallMethod!(
                harness.Context,
                write,
                BuildArray(Variant.From(readHandle), Variant.From(new ArrayOf<byte>(new byte[] { 1, 2, 3 }))),
                []);

            Assert.Multiple(() =>
            {
                Assert.That(missingArguments.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(readOnlyHandle.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
            });
        }

        [Test]
        public async Task PubSubConfigurationFileClose_RemovesHandleAndRejectsMissingArguments()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            MethodState open = FindConfigurationFileMethod(harness, "Open");
            MethodState read = FindConfigurationFileMethod(harness, "Read");
            MethodState close = FindConfigurationFileMethod(harness, "Close");
            var openOutputs = new List<Variant>();
            open.OnCallMethod!(harness.Context, open, BuildArray(Variant.From((byte)1)), openOutputs);
            Assert.That(openOutputs[0].TryGetValue(out uint handle), Is.True);

            ServiceResult closeResult = close.OnCallMethod!(
                harness.Context, close, BuildArray(Variant.From(handle)), []);
            ServiceResult readAfterClose = read.OnCallMethod!(
                harness.Context,
                read,
                BuildArray(Variant.From(handle), Variant.From(1024)),
                new List<Variant>());
            ServiceResult missingArguments = close.OnCallMethod!(
                harness.Context, close, BuildArray(), []);

            Assert.Multiple(() =>
            {
                Assert.That(closeResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(readAfterClose.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(missingArguments.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
            });
        }

        [Test]
        public async Task PubSubConfigurationFileCloseAndUpdate_WithInvalidInputs_ReturnsExpectedStatus()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            MethodState open = FindConfigurationFileMethod(harness, "Open");
            MethodState write = FindConfigurationFileMethod(harness, "Write");
            MethodState closeAndUpdate = FindConfigurationFileMethod(harness, "CloseAndUpdate");
            var openOutputs = new List<Variant>();
            open.OnCallMethod!(harness.Context, open, BuildArray(Variant.From((byte)2)), openOutputs);
            Assert.That(openOutputs[0].TryGetValue(out uint writeHandle), Is.True);
            write.OnCallMethod!(
                harness.Context,
                write,
                BuildArray(Variant.From(writeHandle), Variant.From(new ArrayOf<byte>(new byte[] { 0, 1, 2, 3, 4 }))),
                []);

            ServiceResult missingArguments = closeAndUpdate.OnCallMethod!(
                harness.Context, closeAndUpdate, BuildArray(), new List<Variant>());
            ServiceResult unknownHandle = closeAndUpdate.OnCallMethod!(
                harness.Context, closeAndUpdate, BuildArray(Variant.From(9999u)), new List<Variant>());
            ServiceResult corruptPayload = closeAndUpdate.OnCallMethod!(
                harness.Context, closeAndUpdate, BuildArray(Variant.From(writeHandle)), new List<Variant>());

            Assert.Multiple(() =>
            {
                Assert.That(missingArguments.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(unknownHandle.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(corruptPayload.StatusCode.Code, Is.EqualTo(StatusCodes.BadConfigurationError));
            });
        }

        [Test]
        public async Task PubSubConfigurationReserveIds_WithMissingArguments_ReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            MethodState reserve = FindConfigurationFileMethod(harness, "ReserveIds");

            ServiceResult result = reserve.OnCallMethod!(
                harness.Context,
                reserve,
                BuildArray(Variant.From(Profiles.PubSubUdpUadpTransport), Variant.From((ushort)1)),
                new List<Variant>());

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task AddDataSetFolder_WithEmptyOrMissingName_ReturnsBadInvalidArgument()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            MethodState addFolder = harness.AddDataSetFolderMethod;

            ServiceResult emptyName = addFolder.OnCallMethod!(
                harness.Context, addFolder, BuildArray(Variant.From(string.Empty)), new List<Variant>());
            ServiceResult missingArguments = addFolder.OnCallMethod!(
                harness.Context, addFolder, BuildArray(), new List<Variant>());

            Assert.Multiple(() =>
            {
                Assert.That(emptyName.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(missingArguments.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
            });
        }

        [Test]
        public async Task RemoveDataSetFolder_WithInvalidInputs_ReturnsExpectedStatus()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            MethodState removeFolder = harness.RemoveDataSetFolderMethod;

            ServiceResult missingArguments = removeFolder.OnCallMethod!(
                harness.Context, removeFolder, BuildArray(), []);
            ServiceResult nonFolderNodeId = removeFolder.OnCallMethod!(
                harness.Context, removeFolder, BuildArray(Variant.From(new NodeId(4321u))), []);

            Assert.Multiple(() =>
            {
                Assert.That(missingArguments.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(nonFolderNodeId.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        [Test]
        public async Task AddPushTarget_WithMissingOrEmptyArguments_ReturnsBadInvalidArgument()
        {
            using var harness = new Harness(opt => opt.ExposeSecurityKeyService = true, includeSks: true);
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            MethodState addPushTarget = harness.AddPushTargetMethod;

            ServiceResult missingArguments = addPushTarget.OnCallMethod!(
                harness.Context,
                addPushTarget,
                BuildArray(Variant.From("app"), Variant.From("endpoint")),
                new List<Variant>());
            ServiceResult emptyApplicationUri = addPushTarget.OnCallMethod!(
                harness.Context,
                addPushTarget,
                BuildArray(
                    Variant.From(string.Empty),
                    Variant.From("endpoint"),
                    Variant.From(PubSubSecurityPolicyUri.PubSubAes128Ctr),
                    Variant.From(UserTokenType.Anonymous),
                    Variant.From((ushort)1),
                    Variant.From(1000.0)),
                new List<Variant>());

            Assert.Multiple(() =>
            {
                Assert.That(missingArguments.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(emptyApplicationUri.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
            });
        }

        [Test]
        public async Task RemovePushTarget_WithInvalidInputs_ReturnsExpectedStatus()
        {
            using var harness = new Harness(opt => opt.ExposeSecurityKeyService = true, includeSks: true);
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            MethodState removePushTarget = harness.RemovePushTargetMethod;

            ServiceResult missingArguments = removePushTarget.OnCallMethod!(
                harness.Context, removePushTarget, BuildArray(), []);
            ServiceResult unknownTarget = removePushTarget.OnCallMethod!(
                harness.Context, removePushTarget, BuildArray(Variant.From(new NodeId(7777u))), []);

            Assert.Multiple(() =>
            {
                Assert.That(missingArguments.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(unknownTarget.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        [Test]
        public async Task GetSecurityGroup_ForExistingGroup_MaterializesSecurityGroupNode()
        {
            using var harness = new Harness(opt => opt.ExposeSecurityKeyService = true, includeSks: true);
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            await harness.SksServer.AddSecurityGroupAsync(new SksSecurityGroup(
                "grp-get",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(1),
                1,
                1,
                Array.Empty<PubSubSecurityKey>())).ConfigureAwait(false);
            MethodState getGroup = harness.GetSecurityGroupMethod;
            var outputs = new List<Variant>();

            ServiceResult result = getGroup.OnCallMethod!(
                harness.Context, getGroup, BuildArray(Variant.From("grp-get")), outputs);
            Assert.That(outputs[0].TryGetValue(out NodeId groupNodeId), Is.True);
            BaseObjectState groupNode = harness.Manager.FindPredefinedNode<BaseObjectState>(groupNodeId);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(groupNode, Is.Not.Null);
                Assert.That(groupNode.TypeDefinitionId, Is.EqualTo(new NodeId(15471u)));
            });
        }

        [Test]
        public async Task RemoveSecurityGroup_ForExistingGroup_RemovesNodeAndSksEntry()
        {
            using var harness = new Harness(opt => opt.ExposeSecurityKeyService = true, includeSks: true);
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            await harness.SksServer.AddSecurityGroupAsync(new SksSecurityGroup(
                "grp-remove",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(1),
                1,
                1,
                Array.Empty<PubSubSecurityKey>())).ConfigureAwait(false);
            await harness.Manager.RebuildSksAddressSpaceForTestsAsync().ConfigureAwait(false);
            NodeId groupNodeId = harness.Manager.MethodHandlers.TryGetSecurityGroupNodeId("grp-remove") ?? NodeId.Null;
            MethodState removeGroup = harness.RemoveSecurityGroupMethod;

            ServiceResult result = removeGroup.OnCallMethod!(
                harness.Context, removeGroup, BuildArray(Variant.From(groupNodeId)), []);
            SksSecurityGroup? afterRemoval = await harness.SksServer
                .GetSecurityGroupAsync("grp-remove").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(harness.Manager.FindPredefinedNode<BaseObjectState>(groupNodeId), Is.Null);
                Assert.That(afterRemoval, Is.Null);
            });
        }

        [Test]
        public async Task ConnectSecurityGroups_WithUnknownGroupNode_ReturnsElementBadNodeIdUnknown()
        {
            using var harness = new Harness(opt => opt.ExposeSecurityKeyService = true, includeSks: true);
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            NodeId targetNodeId = AddPushTarget(harness, "connect-app", "connect-endpoint");
            BaseObjectState targetNode = harness.Manager.FindPredefinedNode<BaseObjectState>(targetNodeId);
            var connect = (MethodState)targetNode.FindChild(
                harness.Context,
                new QualifiedName("ConnectSecurityGroups", harness.Manager.AddressSpaceNamespaceIndex))!;
            var outputs = new List<Variant>();

            ServiceResult result = connect.OnCallMethod!(
                harness.Context,
                connect,
                BuildArray(Variant.From(new ArrayOf<NodeId>(new[] { new NodeId(654321u) }))),
                outputs);
            Assert.That(outputs[0].TryGetValue(out ArrayOf<StatusCode> results), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0].Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        [Test]
        public async Task DisconnectSecurityGroups_ForConnectedGroup_ReturnsGoodResults()
        {
            using var harness = new Harness(opt => opt.ExposeSecurityKeyService = true, includeSks: true);
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            await harness.SksServer.AddSecurityGroupAsync(new SksSecurityGroup(
                "grp-disconnect",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(1),
                1,
                1,
                Array.Empty<PubSubSecurityKey>())).ConfigureAwait(false);
            await harness.Manager.RebuildSksAddressSpaceForTestsAsync().ConfigureAwait(false);
            NodeId groupNodeId = harness.Manager.MethodHandlers
                .TryGetSecurityGroupNodeId("grp-disconnect") ?? NodeId.Null;
            NodeId targetNodeId = AddPushTarget(harness, "disconnect-app", "disconnect-endpoint");
            BaseObjectState targetNode = harness.Manager.FindPredefinedNode<BaseObjectState>(targetNodeId);
            ushort ns = harness.Manager.AddressSpaceNamespaceIndex;
            var connect = (MethodState)targetNode.FindChild(
                harness.Context, new QualifiedName("ConnectSecurityGroups", ns))!;
            var disconnect = (MethodState)targetNode.FindChild(
                harness.Context, new QualifiedName("DisconnectSecurityGroups", ns))!;

            ServiceResult connectResult = connect.OnCallMethod!(
                harness.Context,
                connect,
                BuildArray(Variant.From(new ArrayOf<NodeId>(new[] { groupNodeId }))),
                new List<Variant>());
            var disconnectOutputs = new List<Variant>();
            ServiceResult disconnectResult = disconnect.OnCallMethod!(
                harness.Context,
                disconnect,
                BuildArray(Variant.From(new ArrayOf<NodeId>(new[] { groupNodeId }))),
                disconnectOutputs);
            Assert.That(disconnectOutputs[0].TryGetValue(out ArrayOf<StatusCode> results), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(connectResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(disconnectResult.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0].Code, Is.EqualTo(StatusCodes.Good));
            });
        }

        [Test]
        public async Task TriggerKeyUpdate_WithoutConnectedGroups_ReturnsBadInvalidState()
        {
            var pushProvider = new PushSecurityKeyProvider("push-endpoint", NUnitTelemetryContext.Create());
            using var harness = new Harness(
                opt => opt.ExposeSecurityKeyService = true, includeSks: true, pushProvider: pushProvider);
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            NodeId targetNodeId = AddPushTarget(harness, "trigger-app", "push-endpoint");
            BaseObjectState targetNode = harness.Manager.FindPredefinedNode<BaseObjectState>(targetNodeId);
            var trigger = (MethodState)targetNode.FindChild(
                harness.Context,
                new QualifiedName("TriggerKeyUpdate", harness.Manager.AddressSpaceNamespaceIndex))!;

            ServiceResult result = trigger.OnCallMethod!(harness.Context, trigger, BuildArray(), []);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task TriggerKeyUpdate_WithoutMatchingPushProvider_ReturnsBadNotFound()
        {
            using var harness = new Harness(opt => opt.ExposeSecurityKeyService = true, includeSks: true);
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            NodeId targetNodeId = AddPushTarget(harness, "orphan-app", "orphan-endpoint");
            BaseObjectState targetNode = harness.Manager.FindPredefinedNode<BaseObjectState>(targetNodeId);
            var trigger = (MethodState)targetNode.FindChild(
                harness.Context,
                new QualifiedName("TriggerKeyUpdate", harness.Manager.AddressSpaceNamespaceIndex))!;

            ServiceResult result = trigger.OnCallMethod!(harness.Context, trigger, BuildArray(), []);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task ConnectionStatusEnable_WhenTransportUnavailable_ReturnsBadInvalidState()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            NodeId connectionId = await harness.Application.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "enable-conn",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://224.0.0.22:4840"
                })
            }).ConfigureAwait(false);
            MethodState enable = harness.Manager.FindPredefinedNode<MethodState>(
                new NodeId(
                    "pubsub:connection:enable-conn:Status:Enable",
                    harness.Manager.AddressSpaceNamespaceIndex));

            ServiceResult result = enable.OnCallMethod!(harness.Context, enable, BuildArray(), []);

            Assert.Multiple(() =>
            {
                Assert.That(connectionId.IsNull, Is.False);
                Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
            });
        }

        [Test]
        public async Task ConnectionStatusDisable_ForExistingConnection_ReturnsGood()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            _ = await harness.Application.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "disable-conn",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://224.0.0.22:4840"
                })
            }).ConfigureAwait(false);
            MethodState disable = harness.Manager.FindPredefinedNode<MethodState>(
                new NodeId(
                    "pubsub:connection:disable-conn:Status:Disable",
                    harness.Manager.AddressSpaceNamespaceIndex));

            ServiceResult result = disable.OnCallMethod!(harness.Context, disable, BuildArray(), []);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ConnectionStatusEnable_ForRemovedComponent_ReturnsBadInvalidState()
        {
            using var harness = new Harness();
            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            NodeId connectionId = await harness.Application.AddConnectionAsync(new PubSubConnectionDataType
            {
                Name = "removed-conn",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://224.0.0.22:4840"
                })
            }).ConfigureAwait(false);
            MethodState enable = harness.Manager.FindPredefinedNode<MethodState>(
                new NodeId(
                    "pubsub:connection:removed-conn:Status:Enable",
                    harness.Manager.AddressSpaceNamespaceIndex));
            await harness.Application.RemoveConnectionAsync(connectionId).ConfigureAwait(false);

            ServiceResult result = enable.OnCallMethod!(harness.Context, enable, BuildArray(), []);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        private static ArrayOf<Variant> BuildArray(params Variant[] values)
        {
            return new ArrayOf<Variant>(values);
        }

        private static NodeId AddPushTarget(Harness harness, string applicationUri, string endpointUrl)
        {
            var outputs = new List<Variant>();
            harness.AddPushTargetMethod.OnCallMethod!(
                harness.Context,
                harness.AddPushTargetMethod,
                BuildArray(
                    Variant.From(applicationUri),
                    Variant.From(endpointUrl),
                    Variant.From(PubSubSecurityPolicyUri.PubSubAes128Ctr),
                    Variant.From(UserTokenType.Anonymous),
                    Variant.From((ushort)1),
                    Variant.From(1000.0)),
                outputs);
            _ = outputs[0].TryGetValue(out NodeId targetNodeId);
            return targetNodeId;
        }

        private static MethodState FindConfigurationFileMethod(Harness harness, string browseName)
        {
            ushort ns = harness.Manager.AddressSpaceNamespaceIndex;
            BaseObjectState fileNode = harness.Manager.FindPredefinedNode<BaseObjectState>(
                new NodeId("pubsub:configuration", ns));
            return (MethodState)fileNode.FindChild(harness.Context, new QualifiedName(browseName, ns))!;
        }

        private static Mock<IPubSubApplication> CreateRichApplicationMock()
        {
            var app = new Mock<IPubSubApplication>();
            app.Setup(a => a.GetConfiguration()).Returns(CreateRichConfiguration());
            app.Setup(a => a.ConfigurationVersion).Returns(new ConfigurationVersionDataType());
            return app;
        }

        private static PubSubConfigurationDataType CreateRichConfiguration()
        {
            var writer = new DataSetWriterDataType { Name = "W1" };
            var writerGroup = new WriterGroupDataType
            {
                Name = "WG1",
                DataSetWriters = new ArrayOf<DataSetWriterDataType>(new[] { writer })
            };
            var reader = new DataSetReaderDataType { Name = "R1" };
            var readerGroup = new ReaderGroupDataType
            {
                Name = "RG1",
                DataSetReaders = new ArrayOf<DataSetReaderDataType>(new[] { reader })
            };
            var connection = new PubSubConnectionDataType
            {
                Name = "Conn1",
                WriterGroups = new ArrayOf<WriterGroupDataType>(new[] { writerGroup }),
                ReaderGroups = new ArrayOf<ReaderGroupDataType>(new[] { readerGroup })
            };
            var publishedDataSet = new PublishedDataSetDataType
            {
                Name = "PDS1",
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = new ArrayOf<PublishedVariableDataType>(
                        Array.Empty<PublishedVariableDataType>())
                })
            };
            return new PubSubConfigurationDataType
            {
                Enabled = true,
                Connections = new ArrayOf<PubSubConnectionDataType>(new[] { connection }),
                PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(new[] { publishedDataSet })
            };
        }

        private sealed class Harness : IDisposable
        {
            public Harness(
                Action<PubSubServerOptions>? configure = null,
                bool includeSks = false,
                PushSecurityKeyProvider? pushProvider = null,
                IPubSubApplication? applicationOverride = null,
                bool withoutDiagnosticsNodeManager = false)
            {
                MockServer = new Mock<IServerInternal>();
                NamespaceTable = new NamespaceTable();
                NamespaceTable.Append(Namespaces.OpcUa);
                MockServer.Setup(s => s.NamespaceUris).Returns(NamespaceTable);
                MockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                MockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                MockServer.Setup(s => s.TypeTree).Returns(new TypeTable(NamespaceTable));

                var mockMaster = new Mock<IMasterNodeManager>();
                var mockConfig = new Mock<IConfigurationNodeManager>();
                mockMaster.Setup(m => m.ConfigurationNodeManager).Returns(mockConfig.Object);
                MockServer.Setup(s => s.NodeManager).Returns(mockMaster.Object);

                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                MockServer.Setup(s => s.Telemetry).Returns(telemetry);

                m_queueFactory = new MonitoredItemQueueFactory(telemetry);
                MockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactory);

                m_serverSystemContext = new ServerSystemContext(MockServer.Object);
                MockServer.Setup(s => s.DefaultSystemContext).Returns(m_serverSystemContext);

                Configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 200
                    }
                };

                EnableMethod = NewMethod(17407);
                DisableMethod = NewMethod(17408);
                SetSecurityKeysMethod = NewMethod(17364);
                AddConnectionMethod = NewMethod(17366);
                RemoveConnectionMethod = NewMethod(17369);
                GetSecurityKeysMethod = NewMethod(15215);
                GetSecurityGroupMethod = NewMethod(15440);
                AddSecurityGroupMethod = NewMethod(15444);
                RemoveSecurityGroupMethod = NewMethod(15447);
                AddPushTargetMethod = NewMethod(25441);
                RemovePushTargetMethod = NewMethod(25444);
                AddDataSetFolderMethod = NewMethod(16884);
                RemoveDataSetFolderMethod = NewMethod(16923);
                StatusVariable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(17406u),
                    BrowseName = new QualifiedName("State")
                };
                PublishSubscribeObject = new BaseObjectState(null)
                {
                    NodeId = ObjectIds.PublishSubscribe,
                    BrowseName = new QualifiedName("PublishSubscribe")
                };
                PublishedDataSetsObject = new BaseObjectState(PublishSubscribeObject)
                {
                    NodeId = new NodeId(14478u),
                    BrowseName = new QualifiedName("PublishedDataSets")
                };
                SecurityGroupsObject = new BaseObjectState(PublishSubscribeObject)
                {
                    NodeId = new NodeId(15443u),
                    BrowseName = new QualifiedName("SecurityGroups")
                };
                KeyPushTargetsObject = new BaseObjectState(PublishSubscribeObject)
                {
                    NodeId = new NodeId(25440u),
                    BrowseName = new QualifiedName("KeyPushTargets")
                };

                var diagnosticsNm = new Mock<IDiagnosticsNodeManager>();
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17407u))).Returns(EnableMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17408u))).Returns(DisableMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17364u))).Returns(SetSecurityKeysMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17366u))).Returns(AddConnectionMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17369u))).Returns(RemoveConnectionMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(15215u))).Returns(GetSecurityKeysMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(15440u))).Returns(GetSecurityGroupMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(15444u))).Returns(AddSecurityGroupMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(15447u))).Returns(RemoveSecurityGroupMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(25441u))).Returns(AddPushTargetMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(25444u))).Returns(RemovePushTargetMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(16884u))).Returns(AddDataSetFolderMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(16923u))).Returns(RemoveDataSetFolderMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<BaseVariableState>(new NodeId(17406u))).Returns(StatusVariable);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<BaseVariableState>(It.IsAny<NodeId>()))
                    .Returns((NodeId id) => id == new NodeId(17406u) ? StatusVariable : null!);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<BaseObjectState>(ObjectIds.PublishSubscribe))
                    .Returns(PublishSubscribeObject);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<BaseObjectState>(new NodeId(14478u)))
                    .Returns(PublishedDataSetsObject);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<BaseObjectState>(new NodeId(15443u)))
                    .Returns(SecurityGroupsObject);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<BaseObjectState>(new NodeId(25440u)))
                    .Returns(KeyPushTargetsObject);
                if (!withoutDiagnosticsNodeManager)
                {
                    MockServer.Setup(s => s.DiagnosticsNodeManager).Returns(diagnosticsNm.Object);
                }

                if (applicationOverride is not null)
                {
                    Application = applicationOverride;
                    m_ownsApplication = false;
                }
                else
                {
                    Application = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                        .WithApplicationId("test-nodemanager")
                        .UseConfiguration(new PubSubConfigurationDataType
                        {
                            Connections = [],
                            PublishedDataSets = []
                        })
                        .UseAllStandardEncoders()
                        .AddTransportFactory(new StubTransportFactory())
                        .Build();
                    m_ownsApplication = true;
                }

                SksServer = new InMemoryPubSubKeyServiceServer();

                Options = new PubSubServerOptions();
                configure?.Invoke(Options);

                Manager = new PubSubNodeManager(
                    MockServer.Object,
                    Configuration,
                    Application,
                    includeSks ? SksServer : null,
                    Options,
                    telemetry,
                    pushKeyProviders: pushProvider is null ? null : [pushProvider]);
            }

            public Mock<IServerInternal> MockServer { get; }
            public NamespaceTable NamespaceTable { get; }
            public ApplicationConfiguration Configuration { get; }
            public IPubSubApplication Application { get; }
            public InMemoryPubSubKeyServiceServer SksServer { get; }
            public PubSubServerOptions Options { get; }
            public PubSubNodeManager Manager { get; }
            public MethodState EnableMethod { get; }
            public MethodState DisableMethod { get; }
            public MethodState SetSecurityKeysMethod { get; }
            public MethodState AddConnectionMethod { get; }
            public MethodState RemoveConnectionMethod { get; }
            public MethodState GetSecurityKeysMethod { get; }
            public MethodState GetSecurityGroupMethod { get; }
            public MethodState AddSecurityGroupMethod { get; }
            public MethodState RemoveSecurityGroupMethod { get; }
            public MethodState AddPushTargetMethod { get; }
            public MethodState RemovePushTargetMethod { get; }
            public MethodState AddDataSetFolderMethod { get; }
            public MethodState RemoveDataSetFolderMethod { get; }
            public BaseDataVariableState StatusVariable { get; }
            public BaseObjectState PublishSubscribeObject { get; }
            public BaseObjectState PublishedDataSetsObject { get; }
            public BaseObjectState SecurityGroupsObject { get; }
            public BaseObjectState KeyPushTargetsObject { get; }
            public ServerSystemContext Context => m_serverSystemContext;

            public void Dispose()
            {
                Manager.Dispose();
                if (m_ownsApplication)
                {
                    (Application as IDisposable)?.Dispose();
                    (Application as IAsyncDisposable)?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                m_queueFactory.Dispose();
            }

            private static MethodState NewMethod(uint nodeId)
            {
                return new MethodState(null)
                {
                    NodeId = new NodeId(nodeId),
                    BrowseName = new QualifiedName("M" + nodeId)
                };
            }

            private readonly bool m_ownsApplication;
            private readonly MonitoredItemQueueFactory m_queueFactory;
            private readonly ServerSystemContext m_serverSystemContext;

            private sealed class StubTransportFactory : IPubSubTransportFactory
            {
                public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

                public IPubSubTransport Create(
                    PubSubConnectionDataType connection,
                    ITelemetryContext telemetry,
                    TimeProvider timeProvider)
                {
                    _ = connection;
                    _ = telemetry;
                    _ = timeProvider;
                    throw new NotSupportedException();
                }
            }
        }
    }
}
