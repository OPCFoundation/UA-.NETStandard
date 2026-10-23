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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Exercises the public generic tools over a real policy and a borrowed, strictly mocked UA session.
    /// Willing wire callbacks expose dispatch even when authority or cancellation should have stopped it.
    /// </summary>
    [TestFixture]
    public sealed class McpFoundationGenericToolsTests
    {
        [SetUp]
        public void SetUp()
        {
            m_callers = new McpCallerContext();
            m_cancellation = new CancellationTokenSource();
            m_provider = null;
            m_call = null;
            m_write = null;
            m_addNodes = null;
            m_deleteNodes = null;
            m_addReferences = null;
            m_deleteReferences = null;
            m_history = null;
            m_callStatus = StatusCodes.Good;
            m_inputResults = default;
            m_returnNullInputResults = false;
            m_writeResults = default;
            m_deleteResults = default;
            m_historyResults = [StatusCodes.Good, StatusCodes.Good];
            m_callOutputs = [new Variant(17), new Variant("call-result")];
            InitializeModel(3);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_provider is not null)
                {
                    await m_provider.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_cancellation.Dispose();
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task CallRefusesProtectedObjectOrMethodBeforeMutationAsync(bool protectedObject)
        {
            ProtectCallTarget(protectedObject);
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await MethodServiceTools.CallAsync(
                manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            AssertClassified(Node(protectedObject ? "ObjectA" : "MethodA"));
            if (!protectedObject)
            {
                AssertClassified(Node("ObjectA"));
                AssertClassified(Node("RoleOwner"));
            }
        }

        [TestCaseSource(nameof(CallAllowedCases))]
        public async Task CallAllowedPathPreservesExactRequestAndResponseAsync(
            Authority authority, string[]? inputs)
        {
            McpExecutionOptions options = ConfigureAuthority(authority, Node("ObjectA"), Node("MethodA"));
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await MethodServiceTools.CallAsync(
                manager, kObject, kMethod, inputs, "prod-a", m_cancellation.Token).ConfigureAwait(false);

            AssertCallRequest(inputs);
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            AssertProperties(root, "responseHeader", "statusCode", "outputArguments", "inputArgumentResults");
            AssertHeader(root);
            Assert.That(root.GetProperty("statusCode").GetString(), Is.EqualTo("Good"));
            AssertOutputs(root);
            AssertStatuses(root.GetProperty("inputArgumentResults"),
                [.. Enumerable.Repeat("Good", inputs?.Length ?? 0)]);
            AssertClassified(Node("ObjectA"));
            AssertClassified(Node("MethodA"));
            AssertSelectedSession(manager);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task CallMethodRefusesProtectedObjectOrMethodBeforeMutationAsync(bool protectedObject)
        {
            ProtectCallTarget(protectedObject);
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await ConvenienceTools.CallMethodAsync(
                manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            AssertClassified(Node(protectedObject ? "ObjectA" : "MethodA"));
            if (!protectedObject)
            {
                AssertClassified(Node("ObjectA"));
                AssertClassified(Node("RoleOwner"));
            }
        }

        [TestCaseSource(nameof(CallAllowedCases))]
        public async Task CallMethodAllowedPathPreservesExactRequestAndResponseAsync(
            Authority authority, string[]? inputs)
        {
            McpExecutionOptions options = ConfigureAuthority(authority, Node("ObjectA"), Node("MethodA"));
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await ConvenienceTools.CallMethodAsync(
                manager, kObject, kMethod, inputs, "prod-a", m_cancellation.Token).ConfigureAwait(false);

            AssertCallRequest(inputs);
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            AssertProperties(root, "objectId", "methodId", "outputArguments");
            Assert.That(root.GetProperty("objectId").GetString(), Is.EqualTo(kObject));
            Assert.That(root.GetProperty("methodId").GetString(), Is.EqualTo(kMethod));
            AssertOutputs(root);
            AssertClassified(Node("ObjectA"));
            AssertClassified(Node("MethodA"));
            AssertSelectedSession(manager);
        }

        [Test]
        public async Task WriteRefusesProtectedNodeBeforeMutationAsync()
        {
            ProtectRole(Node("ValueA"));
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await AttributeServiceTools.WriteAsync(
                manager, [kValueA], ["42"], sessionName: kSession, ct: m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            AssertClassified(Node("ValueA"));
        }

        [TestCaseSource(nameof(WriteAllowedCases))]
        public async Task WriteAllowedPathPreservesExactBatchAndResultsAsync(
            Authority authority, int count, string? attribute, bool badSecond)
        {
            string[] nodes = count == 1 ? [kValueA] : [kValueA, kValueB];
            string[] values = count == 1 ? ["42"] : ["42", "beta"];
            McpExecutionOptions options = ConfigureAuthority(authority, Node("ValueA"), Node("ValueB"));
            if (badSecond)
            {
                m_writeResults = [StatusCodes.Good, StatusCodes.BadNotWritable];
            }
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await AttributeServiceTools.WriteAsync(
                manager, nodes, values, attribute, "prod-a", m_cancellation.Token).ConfigureAwait(false);

            AssertStringWrites(nodes, values, Attributes.Value);
            AssertStatusResponse(json, badSecond ? ["Good", "BadNotWritable"] : GoodNames(count));
            foreach (string node in nodes)
            {
                AssertClassified(NodeId.Parse(node));
            }
            AssertSelectedSession(manager);
        }

        [Test]
        public async Task WriteValueRefusesProtectedNodeBeforeMutationAsync()
        {
            ProtectRole(Node("ValueA"));
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await ConvenienceTools.WriteValueAsync(
                manager, kValueA, "42", "Int32", kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            AssertClassified(Node("ValueA"));
        }

        [TestCase(Authority.OrdinaryDefaultDeny, false)]
        [TestCase(Authority.OrdinaryDefaultDeny, true)]
        [TestCase(Authority.GrantedRole, false)]
        [TestCase(Authority.GrantedRole, true)]
        public async Task WriteValueAllowedPathPreservesExactTypedValueAsync(Authority authority, bool badResult)
        {
            McpExecutionOptions options = ConfigureAuthority(authority, Node("ValueA"));
            m_writeResults = [badResult ? StatusCodes.BadTypeMismatch : StatusCodes.Good];
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await ConvenienceTools.WriteValueAsync(
                manager, kValueA, "42", "Int32", "prod-a", m_cancellation.Token).ConfigureAwait(false);

            AssertInt32Write(Node("ValueA"), 42);
            AssertWriteValueResponse(json, badResult ? "BadTypeMismatch" : "Good");
            AssertClassified(Node("ValueA"));
            AssertSelectedSession(manager);
        }

        [TestCase(true, "Object")]
        [TestCase(false, "Variable")]
        public async Task AddNodesRefusesProtectedParentOrTypeBeforeMutationAsync(
            bool protectedParent, string nodeClass)
        {
            ProtectRole(Node(protectedParent ? "ParentA" : "TypeA"));
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.AddNodesAsync(
                manager, kParent, "i=35", "Created", nodeClass, kType, kSession, m_cancellation.Token)
                .ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            AssertClassified(Node("ParentA"));
            if (!protectedParent)
            {
                AssertClassified(Node("TypeA"));
            }
        }

        [TestCaseSource(nameof(AddNodeAllowedCases))]
        public async Task AddNodesAllowedPathPreservesExactNodeDescriptionAsync(
            Authority authority, string className, NodeClass nodeClass, Type attributesType,
            bool includeType, string browseName, ushort browseNamespace)
        {
            McpExecutionOptions options = ConfigureAuthority(authority, Node("ParentA"), Node("TypeA"));
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.AddNodesAsync(
                manager, kParent, "i=35", browseName, className, includeType ? kType : null,
                "prod-a", m_cancellation.Token).ConfigureAwait(false);

            WireRequest<AddNodesItem> wire = VerifyAddNodes();
            Assert.That(wire.Items.Count, Is.EqualTo(1));
            AddNodesItem item = wire.Items[0];
            Assert.That(item.ParentNodeId, Is.EqualTo(new ExpandedNodeId(Node("ParentA"))));
            Assert.That(item.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
            Assert.That(item.RequestedNewNodeId, Is.EqualTo(ExpandedNodeId.Null));
            Assert.That(item.BrowseName.Name, Is.EqualTo("Created"));
            Assert.That(item.BrowseName.NamespaceIndex, Is.EqualTo(browseNamespace));
            Assert.That(item.NodeClass, Is.EqualTo(nodeClass));
            Assert.That(item.TypeDefinition,
                Is.EqualTo(includeType ? new ExpandedNodeId(Node("TypeA")) : ExpandedNodeId.Null));
            Assert.That(item.NodeAttributes.TryGetValue(out NodeAttributes? attributes), Is.True);
            Assert.That(attributes, Is.TypeOf(attributesType));
            Assert.That(attributes!.DisplayName.Text, Is.EqualTo(browseName));
            if (nodeClass == NodeClass.Variable)
            {
                var variable = (VariableAttributes)attributes;
                Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.BaseDataType));
                Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
                Assert.That(variable.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
                Assert.That(variable.SpecifiedAttributes, Is.EqualTo(
                    (uint)NodeAttributesMask.DisplayName |
                    (uint)NodeAttributesMask.DataType |
                    (uint)NodeAttributesMask.AccessLevel |
                    (uint)NodeAttributesMask.UserAccessLevel));
            }
            else
            {
                Assert.That(attributes.SpecifiedAttributes, Is.EqualTo((uint)NodeAttributesMask.DisplayName));
            }
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            AssertProperties(root, "responseHeader", "results");
            AssertHeader(root);
            JsonElement results = root.GetProperty("results");
            Assert.That(results.GetArrayLength(), Is.EqualTo(1));
            AssertProperties(results[0], "statusCode", "addedNodeId");
            Assert.That(results[0].GetProperty("statusCode").GetString(), Is.EqualTo("Good"));
            Assert.That(results[0].GetProperty("addedNodeId").GetString(), Is.EqualTo("ns=2;s=CreatedNode"));
            AssertClassified(Node("ParentA"));
            Assert.That(m_model.Browses.Any(query => query.Request.NodeId == Node("TypeA")), Is.EqualTo(includeType));
            AssertSelectedSession(manager);
        }

        [TestCase(1, 0)]
        [TestCase(3, 0)]
        [TestCase(3, 1)]
        [TestCase(3, 2)]
        public async Task DeleteNodesRefusesAnyProtectedElementBeforeMutationAsync(int count, int deniedIndex)
        {
            string[] nodes = count == 1 ? [kValueA] : [kValueA, kValueB, kTarget];
            ProtectRole(NodeId.Parse(nodes[deniedIndex]));
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.DeleteNodesAsync(
                manager, nodes, false, kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            for (int index = 0; index <= deniedIndex; index++)
            {
                AssertClassified(NodeId.Parse(nodes[index]));
            }
            for (int index = deniedIndex + 1; index < count; index++)
            {
                Assert.That(m_model.Browses.Any(query => query.Request.NodeId == NodeId.Parse(nodes[index])),
                    Is.False);
            }
        }

        [TestCaseSource(nameof(DeleteNodeAllowedCases))]
        public async Task DeleteNodesAllowedPathPreservesOrderAndDeleteFlagAsync(
            Authority authority, int count, bool deleteTargetReferences, bool badSecond)
        {
            string[] nodes = count switch
            {
                1 => [kValueA],
                2 => [kValueA, kValueB],
                _ => [kValueA, kValueB, kTarget]
            };
            McpExecutionOptions options = ConfigureAuthority(
                authority, Node("ValueA"), Node("ValueB"), Node("TargetA"));
            if (badSecond)
            {
                m_deleteResults = [StatusCodes.Good, StatusCodes.BadNodeIdUnknown];
            }
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.DeleteNodesAsync(
                manager, nodes, deleteTargetReferences, "prod-a", m_cancellation.Token).ConfigureAwait(false);

            WireRequest<DeleteNodesItem> wire = VerifyDeleteNodes();
            Assert.That(wire.Items.Count, Is.EqualTo(count));
            for (int index = 0; index < count; index++)
            {
                Assert.That(wire.Items[index].NodeId, Is.EqualTo(NodeId.Parse(nodes[index])));
                Assert.That(wire.Items[index].DeleteTargetReferences, Is.EqualTo(deleteTargetReferences));
                AssertClassified(NodeId.Parse(nodes[index]));
            }
            AssertStatusResponse(json, badSecond ? ["Good", "BadNodeIdUnknown"] : GoodNames(count));
            AssertSelectedSession(manager);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task AddReferencesRefusesProtectedSourceOrLocalTargetBeforeMutationAsync(bool protectedSource)
        {
            ProtectRole(Node(protectedSource ? "ObjectA" : "TargetA"));
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.AddReferencesAsync(
                manager, kObject, "i=47", kTarget, false, kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            AssertClassified(Node("ObjectA"));
            if (!protectedSource)
            {
                AssertClassified(Node("TargetA"));
            }
        }

        [TestCase(Authority.OrdinaryDefaultDeny, false)]
        [TestCase(Authority.OrdinaryDefaultDeny, true)]
        [TestCase(Authority.GrantedRole, false)]
        [TestCase(Authority.GrantedRole, true)]
        public async Task AddReferencesAllowedPathPreservesExactReferenceAsync(Authority authority, bool isForward)
        {
            McpExecutionOptions options = ConfigureAuthority(authority, Node("ObjectA"), Node("TargetA"));
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.AddReferencesAsync(
                manager, kObject, "i=47", kTarget, isForward, "prod-a", m_cancellation.Token).ConfigureAwait(false);

            WireRequest<AddReferencesItem> wire = VerifyAddReferences();
            Assert.That(wire.Items.Count, Is.EqualTo(1));
            Assert.That(wire.Items[0].SourceNodeId, Is.EqualTo(Node("ObjectA")));
            Assert.That(wire.Items[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
            Assert.That(wire.Items[0].TargetNodeId, Is.EqualTo(new ExpandedNodeId(Node("TargetA"))));
            Assert.That(wire.Items[0].IsForward, Is.EqualTo(isForward));
            Assert.That(wire.Items[0].TargetNodeClass, Is.EqualTo(NodeClass.Unspecified));
            AssertStatusResponse(json, "Good");
            AssertClassified(Node("ObjectA"));
            AssertClassified(Node("TargetA"));
            AssertSelectedSession(manager);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task DeleteReferencesRefusesProtectedSourceOrLocalTargetBeforeMutationAsync(bool protectedSource)
        {
            ProtectRole(Node(protectedSource ? "ObjectA" : "TargetA"));
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.DeleteReferencesAsync(
                manager, kObject, "i=47", kTarget, false, true, kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            AssertClassified(Node("ObjectA"));
            if (!protectedSource)
            {
                AssertClassified(Node("TargetA"));
            }
        }

        [TestCaseSource(nameof(DeleteReferenceAllowedCases))]
        public async Task DeleteReferencesAllowedPathPreservesExactFlagsAsync(
            Authority authority, bool isForward, bool deleteBidirectional)
        {
            McpExecutionOptions options = ConfigureAuthority(authority, Node("ObjectA"), Node("TargetA"));
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.DeleteReferencesAsync(
                manager, kObject, "i=47", kTarget, isForward, deleteBidirectional,
                "prod-a", m_cancellation.Token).ConfigureAwait(false);

            WireRequest<DeleteReferencesItem> wire = VerifyDeleteReferences();
            Assert.That(wire.Items.Count, Is.EqualTo(1));
            Assert.That(wire.Items[0].SourceNodeId, Is.EqualTo(Node("ObjectA")));
            Assert.That(wire.Items[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
            Assert.That(wire.Items[0].TargetNodeId, Is.EqualTo(new ExpandedNodeId(Node("TargetA"))));
            Assert.That(wire.Items[0].IsForward, Is.EqualTo(isForward));
            Assert.That(wire.Items[0].DeleteBidirectional, Is.EqualTo(deleteBidirectional));
            AssertStatusResponse(json, "Good");
            AssertClassified(Node("ObjectA"));
            AssertClassified(Node("TargetA"));
            AssertSelectedSession(manager);
        }

        [Test]
        public async Task HistoryUpdateRefusesProtectedNodeBeforeMutationAsync()
        {
            ProtectRole(Node("ValueA"));
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await AttributeServiceTools.HistoryUpdateAsync(
                manager, kValueA, [kFirstTimestamp, kSecondTimestamp], ["42", "beta"],
                "Insert", kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            AssertClassified(Node("ValueA"));
        }

        [TestCaseSource(nameof(HistoryAllowedCases))]
        public async Task HistoryUpdateAllowedPathPreservesExactDetailsAsync(
            Authority authority, string updateType, PerformUpdateType expectedMode, bool badSecond)
        {
            McpExecutionOptions options = ConfigureAuthority(authority, Node("ValueA"));
            if (badSecond)
            {
                m_historyResults = [StatusCodes.Good, StatusCodes.BadOutOfRange];
            }
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await AttributeServiceTools.HistoryUpdateAsync(
                manager, kValueA, [kFirstTimestamp, kSecondTimestamp], ["42", "beta"],
                updateType, "prod-a", m_cancellation.Token).ConfigureAwait(false);

            WireRequest<ExtensionObject> wire = VerifyHistory();
            Assert.That(wire.Items.Count, Is.EqualTo(1));
            Assert.That(wire.Items[0].TryGetValue(out UpdateDataDetails? details), Is.True);
            Assert.That(details, Is.TypeOf<UpdateDataDetails>());
            Assert.That(details!.NodeId, Is.EqualTo(Node("ValueA")));
            Assert.That(details.PerformInsertReplace, Is.EqualTo(expectedMode));
            Assert.That(details.UpdateValues.Count, Is.EqualTo(2));
            AssertStringVariant(details.UpdateValues[0].WrappedValue, "42");
            AssertStringVariant(details.UpdateValues[1].WrappedValue, "beta");
            Assert.That(details.UpdateValues[0].StatusCode.Code, Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(details.UpdateValues[1].StatusCode.Code, Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(details.UpdateValues[0].SourceTimestamp, Is.EqualTo(new DateTimeUtc(2026, 1, 2, 3, 4, 5)));
            Assert.That(details.UpdateValues[1].SourceTimestamp, Is.EqualTo(new DateTimeUtc(2026, 1, 3, 4, 5, 6)));
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            AssertProperties(root, "responseHeader", "results");
            AssertHeader(root);
            JsonElement results = root.GetProperty("results");
            Assert.That(results.GetArrayLength(), Is.EqualTo(1));
            AssertProperties(results[0], "statusCode", "operationResults");
            Assert.That(results[0].GetProperty("statusCode").GetString(), Is.EqualTo("Good"));
            AssertStatuses(results[0].GetProperty("operationResults"),
                badSecond ? ["Good", "BadOutOfRange"] : ["Good", "Good"]);
            AssertClassified(Node("ValueA"));
            AssertSelectedSession(manager);
        }

        [TestCaseSource(nameof(PermissionAttributeCases))]
        public async Task WriteRefusesEachPermissionAttributeBeforeMutationAsync(string attribute, uint attributeId)
        {
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await AttributeServiceTools.WriteAsync(
                manager, [kValueA], ["42"], attribute, kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            Assert.That(m_model.Browses, Is.Empty, $"Attribute {attributeId} requires authority before model reads.");
            Assert.That(m_model.CacheQueries, Is.Empty);
        }

        [TestCase("IdentityOnly")]
        [TestCase("CertificateOnly")]
        [TestCase("Both")]
        public async Task WritePermissionAndDomainMutationRequiresBothGrantsAsync(string granted)
        {
            m_model.AddTypeDefinition(Node("ValueA"), new ExpandedNodeId(ObjectTypeIds.CertificateGroupType));
            McpCapability[] capabilities = granted switch
            {
                "IdentityOnly" => [McpCapability.IdentityAdministration],
                "CertificateOnly" => [McpCapability.CertificateAdministration],
                _ => [McpCapability.IdentityAdministration, McpCapability.CertificateAdministration]
            };
            McpExecutionOptions options = GrantedOptions(capabilities);
            options.EnabledCapabilities =
                [McpCapability.IdentityAdministration, McpCapability.CertificateAdministration];
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            Assert.That(m_model.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));

            string json = await AttributeServiceTools.WriteAsync(
                manager, [kValueA], ["42"], "RolePermissions", kSession, m_cancellation.Token).ConfigureAwait(false);

            if (granted == "Both")
            {
                AssertStringWrites([kValueA], ["42"], Attributes.RolePermissions);
                AssertStatusResponse(json, "Good");
                AssertClassified(Node("ValueA"));
            }
            else
            {
                string missing = granted == "IdentityOnly" ? "CertificateAdministration" : "IdentityAdministration";
                AssertDenied(json, "BadUserAccessDenied", $"The caller has no grant for the {missing} capability.");
                Assert.That(m_model.Browses.Count == 0, Is.EqualTo(granted == "CertificateOnly"));
            }
        }

        [TestCaseSource(nameof(PermissionAttributeCases))]
        public async Task WriteAllowsEachPermissionAttributeWithExactGrantAsync(string attribute, uint attributeId)
        {
            OpcUaSessionManager manager = await CreateManagerAsync(
                GrantedOptions(McpCapability.IdentityAdministration)).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            Assert.That(manager.OperationPolicy.RequireSession(McpCapability.IdentityAdministration, kSession),
                Is.EqualTo(s_caller));
            Assert.That(m_model.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));

            string json = await AttributeServiceTools.WriteAsync(
                manager, [kValueA], ["42"], attribute, "prod-a", m_cancellation.Token).ConfigureAwait(false);

            AssertStringWrites([kValueA], ["42"], attributeId);
            AssertStatusResponse(json, "Good");
            AssertClassified(Node("ValueA"));
            AssertSelectedSession(manager);
        }

        [TestCaseSource(nameof(PermissionMetadataCases))]
        public async Task WritePermissionMetadataRequiresExplicitIdentityGrantAsync(
            string attribute, uint attributeId, bool granted)
        {
            McpExecutionOptions options = granted
                ? GrantedOptions(McpCapability.IdentityAdministration)
                : GrantedOptions();
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            Assert.That(manager.OperationPolicy.IsEnabled(McpCapability.IdentityAdministration), Is.EqualTo(granted));

            string json = await AttributeServiceTools.WriteAsync(
                manager, [kValueA], ["42"], attribute, "prod-a", m_cancellation.Token).ConfigureAwait(false);

            if (granted)
            {
                AssertStringWrites([kValueA], ["42"], attributeId);
                AssertStatusResponse(json, "Good");
                AssertSelectedSession(manager);
            }
            else
            {
                // Access, execution, and write-mask metadata change permissions even on an ordinary node.
                AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task WriteMixedBatchNeverPartiallyDispatchesAsync(int deniedIndex)
        {
            string[] nodes = [kValueA, kValueB, kTarget];
            ProtectRole(NodeId.Parse(nodes[deniedIndex]));
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await AttributeServiceTools.WriteAsync(
                manager, nodes, ["42", "beta", "third"], "Value", kSession, m_cancellation.Token)
                .ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            for (int index = 0; index <= deniedIndex; index++)
            {
                AssertClassified(NodeId.Parse(nodes[index]));
            }
            for (int index = deniedIndex + 1; index < nodes.Length; index++)
            {
                Assert.That(m_model.Browses.Any(query => query.Request.NodeId == NodeId.Parse(nodes[index])),
                    Is.False);
            }
        }

        [TestCase("Unknown", "BadNodeIdUnknown")]
        [TestCase("BadMetadata", "BadNodeIdUnknown")]
        [TestCase("Unresolved", "BadNotSupported")]
        [TestCase("Remote", "BadNotSupported")]
        [TestCase("NullReference", "BadNotSupported")]
        [TestCase("NullTarget", "BadNodeIdInvalid")]
        [TestCase("Nodes129", "BadEncodingLimitsExceeded")]
        [TestCase("Depth17", "BadEncodingLimitsExceeded")]
        [TestCase("References513", "BadEncodingLimitsExceeded")]
        public async Task WriteValueReportsEachModelFailureBeforeMutationAsync(string failure, string status)
        {
            string target = ConfigureModelFailure(failure);
            string[] namespaces = m_model.NamespaceUris.ToArray();
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await ConvenienceTools.WriteValueAsync(
                manager, target, "42", "Int32", kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, status);
            Assert.That(m_model.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
            if (failure == "NullTarget")
            {
                AssertNoMetadata();
            }
            else
            {
                AssertClassified(NodeId.Parse(target));
            }
            if (failure == "Nodes129")
            {
                Assert.That(m_model.Browses.Select(query => query.Request.NodeId).Distinct().Count(), Is.EqualTo(128));
            }
            if (failure == "Depth17")
            {
                Assert.That(m_model.Browses.Select(query => query.Request.NodeId).Distinct().Count(), Is.EqualTo(17));
            }
            if (failure == "References513")
            {
                Assert.That(m_model.CacheQueries.Count(query => query.Actual == ReferenceTypeIds.HasNotifier),
                    Is.EqualTo(512));
            }
        }

        [Test]
        public async Task AddReferencesRefusesRemoteExpandedTargetBeforeMutationAsync()
        {
            var remote = new ExpandedNodeId(Node("TargetA"), serverIndex: 1);
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.AddReferencesAsync(
                manager, kObject, "i=47", remote.ToString(), false, kSession, m_cancellation.Token)
                .ConfigureAwait(false);

            // G01: the current generic entry point skips authorization for a non-local target.
            AssertDenied(json, "BadNotSupported");
            AssertClassified(Node("ObjectA"));
            Assert.That(remote.ServerIndex, Is.EqualTo(1u));
        }

        [Test]
        public async Task DeleteReferencesRefusesRemoteExpandedTargetBeforeMutationAsync()
        {
            var remote = new ExpandedNodeId(Node("TargetA"), serverIndex: 1);
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.DeleteReferencesAsync(
                manager, kObject, "i=47", remote.ToString(), false, true, kSession, m_cancellation.Token)
                .ConfigureAwait(false);

            AssertDenied(json, "BadNotSupported");
            AssertClassified(Node("ObjectA"));
            Assert.That(remote.ServerIndex, Is.EqualTo(1u));
        }

        [Test]
        public async Task CallPreservesBadPerMethodStatusWithoutGuardEnvelopeAsync()
        {
            m_callStatus = StatusCodes.BadInvalidArgument;
            m_inputResults = [StatusCodes.Good, StatusCodes.BadTypeMismatch];
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await MethodServiceTools.CallAsync(
                manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertCallRequest(["42", "beta"]);
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            AssertProperties(root, "responseHeader", "statusCode", "outputArguments", "inputArgumentResults");
            AssertHeader(root);
            Assert.That(root.GetProperty("statusCode").GetString(), Is.EqualTo("BadInvalidArgument"));
            AssertStatuses(root.GetProperty("inputArgumentResults"), "Good", "BadTypeMismatch");
            AssertOutputs(root);
        }

        [Test]
        public async Task CallMethodReportsExactPerMethodFailureAsync()
        {
            m_callStatus = StatusCodes.BadInvalidArgument;
            Assert.That(m_callStatus.Code, Is.EqualTo(0x80AB0000u));
            m_inputResults = [StatusCodes.Good, StatusCodes.BadTypeMismatch];
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await ConvenienceTools.CallMethodAsync(
                manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertCallRequest(["42", "beta"]);
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            AssertProperties(root, "error", "statusCode", "message", "inputArgumentResults");
            Assert.That(root.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("statusCode").GetString(), Is.EqualTo("BadInvalidArgument"));
            Assert.That(root.GetProperty("message").GetString(),
                Is.EqualTo("Method call failed: BadInvalidArgument [0x80AB0000]"));
            AssertStatuses(root.GetProperty("inputArgumentResults"), "Good", "BadTypeMismatch");
        }

        [TestCase("Write")]
        [TestCase("DeleteNodes")]
        public async Task EmptyMutationBatchWithoutCancellationRetainsBehaviorAsync(string entry)
        {
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = entry == "Write"
                ? await AttributeServiceTools.WriteAsync(
                    manager, [], [], ct: m_cancellation.Token).ConfigureAwait(false)
                : await NodeManagementServiceTools.DeleteNodesAsync(
                    manager, [], false, ct: m_cancellation.Token).ConfigureAwait(false);

            if (entry == "Write")
            {
                WireRequest<WriteValue> wire = VerifyWrite();
                Assert.That(wire.Items.Count, Is.Zero);
                Assert.That(wire.Items.IsNull, Is.False);
            }
            else
            {
                WireRequest<DeleteNodesItem> wire = VerifyDeleteNodes();
                Assert.That(wire.Items.Count, Is.Zero);
                Assert.That(wire.Items.IsNull, Is.False);
            }
            AssertStatusResponse(json);
            AssertNoMetadata();
        }

        [TestCase("Call")]
        [TestCase("CallMethod")]
        [TestCase("Write")]
        [TestCase("WriteValue")]
        [TestCase("AddNodes")]
        [TestCase("DeleteNodes")]
        [TestCase("AddReferences")]
        [TestCase("DeleteReferences")]
        [TestCase("HistoryUpdate")]
        public async Task EveryGenericEntryHonorsPreCancellationAsync(string entry)
        {
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            await m_cancellation.CancelAsync().ConfigureAwait(false);

            OperationCanceledException? failure = null;
            try
            {
                _ = await InvokeEntryAsync(manager, entry).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                failure = exception;
            }

            AssertCancelledBeforeDispatch(failure);
        }

        [TestCase("Write")]
        [TestCase("DeleteNodes")]
        public async Task EmptyMutationBatchPreCancellationPreventsDispatchAsync(string entry)
        {
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            await m_cancellation.CancelAsync().ConfigureAwait(false);

            OperationCanceledException? failure = null;
            try
            {
                if (entry == "Write")
                {
                    _ = await AttributeServiceTools.WriteAsync(
                        manager, [], [], ct: m_cancellation.Token).ConfigureAwait(false);
                }
                else
                {
                    _ = await NodeManagementServiceTools.DeleteNodesAsync(
                        manager, [], false, ct: m_cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException exception)
            {
                failure = exception;
            }

            AssertCancelledBeforeDispatch(failure);
        }

        [TestCase(false, 5)]
        [TestCase(true, 5)]
        [TestCase(false, 7)]
        [TestCase(true, 7)]
        public async Task GenericGdsCallsNeverReturnConfiguredSecretAsync(bool convenience, int namespaceIndex)
        {
            InitializeModel(namespaceIndex);
            var owner = new NodeId("DirectoryOwner", (ushort)namespaceIndex);
            m_model.AddNode(owner);
            m_model.AddOwner(Node("MethodA"), new ExpandedNodeId(owner), ReferenceTypeIds.HasComponent);
            m_callOutputs = [new Variant(17), new Variant(kGdsSecret)];
            OpcUaSessionManager manager = await CreateManagerAsync(
                GrantedOptions(McpCapability.DirectoryAdministration)).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            AssertSecretPreconditions(manager, McpCapability.DirectoryAdministration, kGdsSecret);
            Assert.That(m_model.NamespaceUris.GetString((uint)namespaceIndex),
                Is.EqualTo(McpAuthorizationModel.GdsNamespaceUri));
            Assert.That(m_model.Session.Object.NamespaceUris, Is.SameAs(m_model.MessageContext.NamespaceUris));
            string[] namespaces = m_model.NamespaceUris.ToArray();

            string json = convenience
                ? await ConvenienceTools.CallMethodAsync(
                    manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false)
                : await MethodServiceTools.CallAsync(
                    manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kTypedOnly);
            Assert.That(json, Does.Not.Contain(kGdsSecret));
            Assert.That(m_model.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
            AssertClassified(Node("MethodA"));
            Assert.That(m_model.Browses.Single(query =>
                query.Request.NodeId == Node("MethodA") &&
                query.Request.ReferenceTypeId == ReferenceTypeIds.HierarchicalReferences).Result.References[0].NodeId,
                Is.EqualTo(new ExpandedNodeId(owner)));
        }

        [TestCaseSource(nameof(SksCases))]
        public async Task GenericSksCallsNeverReturnConfiguredSecretAsync(
            bool convenience, NodeId protectedType, McpCapability capability)
        {
            NodeId owner = m_model.AddNode(Node("SecurityOwner"));
            m_model.AddTypeDefinition(owner, new ExpandedNodeId(protectedType));
            m_model.AddOwner(Node("MethodA"), new ExpandedNodeId(owner), ReferenceTypeIds.HasComponent);
            m_callOutputs = [new Variant(17), new Variant(kSksSecret)];
            OpcUaSessionManager manager = await CreateManagerAsync(GrantedOptions(capability)).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            AssertSecretPreconditions(manager, capability, kSksSecret);

            string json = convenience
                ? await ConvenienceTools.CallMethodAsync(
                    manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false)
                : await MethodServiceTools.CallAsync(
                    manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kTypedOnly);
            Assert.That(json, Does.Not.Contain(kSksSecret));
            AssertClassified(Node("MethodA"));
            Assert.That(m_model.Browses.Single(query => query.Request.NodeId == owner &&
                query.Request.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition).Result.References[0].NodeId,
                Is.EqualTo(new ExpandedNodeId(protectedType)));
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public async Task WriteValueRejectsGdsAndGrantedRoleOwnersInEitherOrderAsync(
            bool roleFirst, bool directoryGranted)
        {
            InitializeModel(5);
            NodeId role = m_model.AddNode(Node("RoleOwner"));
            ProtectRole(role);
            NodeId directory = m_model.AddNode(new NodeId("DirectoryOwner", 5));
            m_model.AddOwner(Node("ValueA"), new ExpandedNodeId(roleFirst ? role : directory),
                ReferenceTypeIds.HasComponent);
            m_model.AddOwner(Node("ValueA"), new ExpandedNodeId(roleFirst ? directory : role),
                ReferenceTypeIds.HasProperty);
            McpExecutionOptions options = directoryGranted
                ? GrantedOptions(McpCapability.IdentityAdministration, McpCapability.DirectoryAdministration)
                : GrantedOptions(McpCapability.IdentityAdministration);
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            Assert.That(manager.OperationPolicy.RequireSession(McpCapability.IdentityAdministration, kSession),
                Is.EqualTo(s_caller));
            Assert.That(manager.OperationPolicy.IsEnabled(McpCapability.DirectoryAdministration),
                Is.EqualTo(directoryGranted));
            if (directoryGranted)
            {
                Assert.That(manager.OperationPolicy.RequireSession(McpCapability.DirectoryAdministration, kSession),
                    Is.EqualTo(s_caller));
            }
            Assert.That(m_model.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(m_model.NamespaceUris.GetString(5), Is.EqualTo(McpAuthorizationModel.GdsNamespaceUri));

            string json = await ConvenienceTools.WriteValueAsync(
                manager, kValueA, "42", "Int32", kSession, m_cancellation.Token).ConfigureAwait(false);

            // G02: a granted Role owner must not hide a second, stricter Directory owner.
            AssertDenied(json, "BadUserAccessDenied", directoryGranted
                ? kTypedOnly
                : "The host has not enabled the DirectoryAdministration capability.");
            ArrayOf<ReferenceDescription> owners = m_model.Browses.Single(query =>
                query.Request.NodeId == Node("ValueA") &&
                query.Request.ReferenceTypeId == ReferenceTypeIds.HierarchicalReferences).Result.References;
            Assert.That(owners.ConvertAll(reference => reference.NodeId).ToArray(),
                Is.EqualTo(roleFirst
                    ? new[] { new ExpandedNodeId(role), new ExpandedNodeId(directory) }
                    : [new ExpandedNodeId(directory), new ExpandedNodeId(role)]));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task DefaultPolicyDeniesDespiteFullRegistrationOrServerAuthorityAsync(
            bool fullRegistration, bool serverAdministrator)
        {
            ProtectRole(Node("ValueA"));
            var serverIdentity = new Mock<IUserIdentity>(MockBehavior.Strict);
            serverIdentity.SetupGet(identity => identity.DisplayName)
                .Returns(serverAdministrator ? "ServerAdministrator" : "Anonymous");
            serverIdentity.SetupGet(identity => identity.TokenType)
                .Returns(serverAdministrator ? UserTokenType.UserName : UserTokenType.Anonymous);
            ArrayOf<NodeId> serverRoles = serverAdministrator
                ? [ObjectIds.WellKnownRole_SecurityAdmin, ObjectIds.WellKnownRole_ConfigureAdmin]
                : [ObjectIds.WellKnownRole_Anonymous];
            serverIdentity.SetupGet(identity => identity.GrantedRoleIds).Returns(serverRoles);
            m_model.Session.SetupGet(session => session.Identity).Returns(serverIdentity.Object);
            OpcUaSessionManager manager = await CreateManagerAsync(
                fullRegistration: fullRegistration, authType: serverAdministrator ? "UserName" : "Anonymous")
                .ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            Assert.That(manager.OperationPolicy.RequireCaller(), Is.EqualTo(s_caller));
            Assert.That(manager.OperationPolicy.IsEnabled(McpCapability.IdentityAdministration), Is.False);
            Assert.That(manager.GetSessionInfoOrThrow().AuthType,
                Is.EqualTo(serverAdministrator ? "UserName" : "Anonymous"));
            Assert.That(manager.GetSessionOrThrow().Identity.DisplayName,
                Is.EqualTo(serverAdministrator ? "ServerAdministrator" : "Anonymous"));
            Assert.That(manager.GetSessionOrThrow().Identity.TokenType,
                Is.EqualTo(serverAdministrator ? UserTokenType.UserName : UserTokenType.Anonymous));
            Assert.That(manager.GetSessionOrThrow().Identity.GrantedRoleIds.ToArray(),
                Is.EqualTo(serverRoles.ToArray()));

            string json = await ConvenienceTools.WriteValueAsync(
                manager, kValueA, "42", "Int32", kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            AssertClassified(Node("ValueA"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ManagerFallbackPolicyUsesOnlyConfiguredAuthorityAsync(bool configured)
        {
            ProtectRole(Node("ValueA"));
            var services = new ServiceCollection();
            services.AddSingleton(m_callers);
            services.AddSingleton<IMcpFixedSessionContract>(new FixedMockSessionContract(m_model.Session.Object));
            services.AddSingleton(new OpcUaMcpOptions
            {
                Execution = configured ? GrantedOptions(McpCapability.IdentityAdministration) : GrantedOptions()
            });
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(context => context.LoggerFactory).Returns(NullLoggerFactory.Instance);
            services.AddSingleton(provider => new OpcUaSessionManager(
                NullLogger<OpcUaSessionManager>.Instance, provider, new OpcUaClientOptions(), telemetry.Object));
            m_provider = services.BuildServiceProvider();
            OpcUaSessionManager manager = m_provider.GetRequiredService<OpcUaSessionManager>();
            Assert.That(m_provider.GetService<IMcpOperationPolicy>(), Is.Null);
            Assert.That(manager.OperationPolicy, Is.TypeOf<McpExecutionPolicy>());
            _ = await manager.RegisterExistingSessionAsync(
                kSession, m_model.Session.Object, "UserName", m_cancellation.Token).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await ConvenienceTools.WriteValueAsync(
                manager, kValueA, "42", "Int32", kSession, m_cancellation.Token).ConfigureAwait(false);

            if (configured)
            {
                AssertInt32Write(Node("ValueA"), 42);
                AssertWriteValueResponse(json, "Good");
                using IDisposable other = m_callers.EnterTrustedCaller(s_caller with { Issuer = "urn:test:other" });
                Assert.That(() => manager.OperationPolicy.RequireSession(
                    McpCapability.IdentityAdministration, kSession),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            else
            {
                AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            }
            AssertClassified(Node("ValueA"));
        }

        [TestCase(null)]
        [TestCase("Prod-A")]
        [TestCase("prod-a")]
        [TestCase("PROD-A")]
        public async Task RegisteredSessionSelectionUsesActualRegisteredNameAsync(string? requested)
        {
            OpcUaSessionManager manager = await CreateManagerAsync(authType: "UserName").ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo registered = manager.GetSessionInfoOrThrow(kSession);

            OpcUaSessionManager.SessionInfo selected = manager.GetSessionInfoOrThrow(requested);

            Assert.That(selected, Is.SameAs(registered));
            Assert.That(manager.GetSessionOrThrow(requested), Is.SameAs(m_model.Session.Object));
            Assert.That(selected.Name, Is.EqualTo("Prod-A"));
            Assert.That(selected.Session.SessionName, Is.EqualTo("ServerAssigned"));
            Assert.That(selected.Endpoint, Is.Not.SameAs(m_model.Endpoint));
            Assert.That(Utils.IsEqual(selected.Endpoint, m_model.Endpoint), Is.True);
            Assert.That(selected.AuthType, Is.EqualTo("UserName"));
            Assert.That(selected.IsConnected, Is.True);
            Assert.That(selected.OwnsSession, Is.False);
            m_model.VerifyNoMutations();
            AssertNoMetadata();
        }

        [TestCase("ServerAssigned", false)]
        [TestCase("Prod-A", true)]
        public async Task ToolGrantCannotUseRemoteSessionNameInsteadOfRegisteredNameAsync(
            string grantName, bool allowed)
        {
            ProtectRole(Node("ValueA"));
            McpExecutionOptions options = GrantedOptions(McpCapability.IdentityAdministration);
            options.CallerGrants[0].SessionNames = [grantName];
            OpcUaSessionManager manager = await CreateManagerAsync(options).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            if (allowed)
            {
                string json = await ConvenienceTools.WriteValueAsync(
                    manager, kValueA, "42", "Int32", "prod-a", m_cancellation.Token).ConfigureAwait(false);

                AssertSelectedSession(manager);
                Assert.That(manager.GetSessionInfoOrThrow("prod-a").Name, Is.EqualTo("Prod-A"));
                AssertInt32Write(Node("ValueA"), 42);
                AssertWriteValueResponse(json, "Good");
            }
            else
            {
                ServiceResultException exception = await CaptureAsync<ServiceResultException>(() =>
                    ConvenienceTools.WriteValueAsync(
                        manager, kValueA, "42", "Int32", "prod-a", m_cancellation.Token)).ConfigureAwait(false);
                Assert.That(exception.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(exception.Message,
                    Does.Contain("The caller has no privileged-operation grant for the selected UA session."));
                AssertNoMetadata();
            }
        }

        [TestCase("Empty")]
        [TestCase("Unknown")]
        [TestCase("DisconnectedNamed")]
        [TestCase("DisconnectedOmitted")]
        [TestCase("Ambiguous")]
        public async Task SessionLookupRejectsUnavailableOrAmbiguousSelectionAsync(string situation)
        {
            OpcUaSessionManager manager = await CreateManagerAsync(register: situation != "Empty")
                .ConfigureAwait(false);
            string? name = situation == "Unknown" ? "Missing" :
                situation == "DisconnectedNamed" ? kSession : null;
            if (situation.StartsWith("Disconnected", StringComparison.Ordinal))
            {
                m_model.Session.SetupGet(session => session.Connected).Returns(false);
            }
            McpAuthorizationModel? second = null;
            if (situation == "Ambiguous")
            {
                second = new McpAuthorizationModel(registeredName: "Prod-B");
                _ = await manager.RegisterExistingSessionAsync(
                    "Prod-B", second.Session.Object, "Anonymous", m_cancellation.Token).ConfigureAwait(false);
            }
            InvalidOperationException infoFailure = await CaptureAsync<InvalidOperationException>(() =>
            {
                _ = manager.GetSessionInfoOrThrow(name);
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            InvalidOperationException sessionFailure = await CaptureAsync<InvalidOperationException>(() =>
            {
                _ = manager.GetSessionOrThrow(name);
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            InvalidOperationException toolFailure = await CaptureAsync<InvalidOperationException>(
                async () => _ = await ConvenienceTools.WriteValueAsync(
                    manager, kValueA, "42", "Int32", name, m_cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false);

            if (situation == "Ambiguous")
            {
                Assert.That(infoFailure.Message, Is.AnyOf(
                    "Multiple sessions active. Specify a sessionName: Prod-A, Prod-B",
                    "Multiple sessions active. Specify a sessionName: Prod-B, Prod-A"));
            }
            else
            {
                string expected = name is null
                    ? "Not connected to an OPC UA server. Use the 'Connect' tool first."
                    : $"Session '{name}' not found or not connected. Use the 'Connect' tool first.";
                Assert.That(infoFailure.Message, Is.EqualTo(expected));
            }
            Assert.That(sessionFailure.Message, Is.EqualTo(infoFailure.Message));
            Assert.That(toolFailure.Message, Is.EqualTo(infoFailure.Message));
            m_model.VerifyNoMutations();
            second?.VerifyNoMutations();
            AssertNoMetadata();
        }

        [Test]
        public async Task BorrowedSessionLifetimeDoesNotDisposeTheSuppliedSessionAsync()
        {
            OpcUaSessionManager manager = await CreateManagerAsync(authType: "UserName").ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo registered = manager.GetSessionInfoOrThrow();
            Assert.That(registered.OwnsSession, Is.False);
            Assert.That(registered.Session, Is.SameAs(m_model.Session.Object));

            await m_provider!.DisposeAsync().ConfigureAwait(false);
            m_provider = null;

            m_model.Session.Verify(session => session.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            m_model.Session.Verify(session => session.Dispose(), Times.Never);
            Assert.That(m_model.Session.Object.Connected, Is.True);
            Assert.That(manager.IsConnected, Is.False);
            m_model.VerifyNoMutations();
            AssertNoMetadata();
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CallToolsPreserveNullAndEmptyServiceArraysAsync(bool convenience, bool nullArrays)
        {
            m_callOutputs = nullArrays ? default : [];
            m_inputResults = [];
            m_returnNullInputResults = nullArrays;
            Assert.That(m_callOutputs.IsNull, Is.EqualTo(nullArrays));
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = convenience
                ? await ConvenienceTools.CallMethodAsync(
                    manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false)
                : await MethodServiceTools.CallAsync(
                    manager, kObject, kMethod, ["42", "beta"], kSession, m_cancellation.Token).ConfigureAwait(false);

            AssertCallRequest(["42", "beta"]);
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("outputArguments").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(root.GetProperty("outputArguments").GetArrayLength(), Is.Zero);
            if (convenience)
            {
                AssertProperties(root, "objectId", "methodId", "outputArguments");
                Assert.That(root.GetProperty("objectId").GetString(), Is.EqualTo(kObject));
                Assert.That(root.GetProperty("methodId").GetString(), Is.EqualTo(kMethod));
            }
            else
            {
                AssertProperties(root, "responseHeader", "statusCode", "outputArguments", "inputArgumentResults");
                AssertHeader(root);
                Assert.That(root.GetProperty("statusCode").GetString(), Is.EqualTo("Good"));
                // The service-result path emits [] for both default and explicit-empty result collections.
                AssertStatuses(root.GetProperty("inputArgumentResults"));
            }
            AssertClassified(Node("ObjectA"));
            AssertClassified(Node("MethodA"));
        }

        [TestCaseSource(nameof(NamespaceMetadataWriteCases))]
        public async Task NamespaceMetadataWritesRequireIdentityAndEncryptionAsync(
            string targetName, bool convenience, string authority, MessageSecurityMode securityMode)
        {
            NodeId target = AddNamespaceMetadataTarget(targetName);
            m_model.Endpoint.SecurityMode = securityMode;
            OpcUaSessionManager manager = await CreateManagerAsync(NamespaceMetadataAuthority(authority))
                .ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            bool root = targetName == "NamespacesMetadata";
            bool restrictions = targetName == "DefaultAccessRestrictions";
            string value = restrictions ? "3" : "[]";
            string attribute = root ? "RolePermissions" : "Value";

            string json = convenience
                ? await ConvenienceTools.WriteValueAsync(
                    manager, target.ToString(), value, restrictions ? "UInt16" : null,
                    "prod-a", m_cancellation.Token).ConfigureAwait(false)
                : await AttributeServiceTools.WriteAsync(
                    manager, [target.ToString()], [value], attribute, "prod-a", m_cancellation.Token)
                    .ConfigureAwait(false);

            if (authority != "Granted")
            {
                AssertDenied(json, "BadUserAccessDenied", authority == "Disabled"
                    ? kIdentityHostDenied
                    : "The caller has no grant for the IdentityAdministration capability.");
            }
            else if (securityMode != MessageSecurityMode.SignAndEncrypt)
            {
                AssertDenied(json, "BadSecurityModeInsufficient");
            }
            else
            {
                WireRequest<WriteValue> wire = VerifyWrite();
                Assert.That(wire.Items.Count, Is.EqualTo(1));
                Assert.That(wire.Items[0].NodeId, Is.EqualTo(target));
                Assert.That(wire.Items[0].AttributeId,
                    Is.EqualTo(root ? Attributes.RolePermissions : Attributes.Value));
                Assert.That(wire.Items[0].Value.StatusCode.Code, Is.EqualTo(StatusCodes.Good.Code));
                if (convenience && restrictions)
                {
                    Variant actual = wire.Items[0].Value.WrappedValue;
                    Assert.That(actual.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt16));
                    Assert.That(actual.TryGetValue(out ushort mask), Is.True);
                    Assert.That(mask, Is.EqualTo((ushort)3));
                }
                else
                {
                    // Generic tools retain their existing string mapping; the server owns datatype validation.
                    AssertStringVariant(wire.Items[0].Value.WrappedValue, value);
                }
                if (convenience)
                {
                    using var document = JsonDocument.Parse(json);
                    AssertProperties(document.RootElement, "nodeId", "statusCode");
                    Assert.That(document.RootElement.GetProperty("nodeId").GetString(), Is.EqualTo(target.ToString()));
                    Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo("Good"));
                }
                else
                {
                    AssertStatusResponse(json, "Good");
                }
                AssertSelectedSession(manager);
            }

            if (!root || (authority == "Granted" && securityMode == MessageSecurityMode.SignAndEncrypt))
            {
                AssertNamespaceMetadataOwnership(target);
            }
            else
            {
                AssertNoMetadata();
            }
        }

        [TestCaseSource(nameof(NamespaceMetadataPermissionAttributeCases))]
        public async Task NamespaceMetadataPermissionAttributesCannotBypassIdentityAsync(
            string attribute, uint attributeId, bool enabled)
        {
            NodeId target = AddNamespaceMetadataTarget("DefaultAccessRestrictions");
            OpcUaSessionManager manager = await CreateManagerAsync(
                NamespaceMetadataAuthority(enabled ? "Granted" : "Disabled")).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await AttributeServiceTools.WriteAsync(
                manager, [target.ToString()], ["3"], attribute, "prod-a", m_cancellation.Token)
                .ConfigureAwait(false);

            if (enabled)
            {
                AssertStringWrites([target.ToString()], ["3"], attributeId);
                AssertStatusResponse(json, "Good");
                AssertNamespaceMetadataOwnership(target);
            }
            else
            {
                AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
                AssertNoMetadata();
            }
        }

        [TestCaseSource(nameof(NamespaceMetadataNodeCases))]
        public async Task NamespaceMetadataNodeChangesRequireIdentityAndEncryptionAsync(
            string targetName, string operation, bool enabled, MessageSecurityMode securityMode)
        {
            NodeId target = AddNamespaceMetadataTarget(targetName);
            m_model.Endpoint.SecurityMode = securityMode;
            OpcUaSessionManager manager = await CreateManagerAsync(
                NamespaceMetadataAuthority(enabled ? "Granted" : "Disabled")).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);
            string selected = target.ToString();

            string json = operation switch
            {
                "AddChild" => await NodeManagementServiceTools.AddNodesAsync(
                    manager, selected, "i=46", "2:CreatedMetadataProperty", "Variable",
                    sessionName: "prod-a", ct: m_cancellation.Token).ConfigureAwait(false),
                "DeleteNode" => await NodeManagementServiceTools.DeleteNodesAsync(
                    manager, [kValueB, selected], false, "prod-a", m_cancellation.Token).ConfigureAwait(false),
                "AddReferenceSource" => await NodeManagementServiceTools.AddReferencesAsync(
                    manager, selected, "i=47", kTarget, false, "prod-a", m_cancellation.Token).ConfigureAwait(false),
                "AddReferenceTarget" => await NodeManagementServiceTools.AddReferencesAsync(
                    manager, kObject, "i=47", selected, false, "prod-a", m_cancellation.Token).ConfigureAwait(false),
                "DeleteReferenceSource" => await NodeManagementServiceTools.DeleteReferencesAsync(
                    manager, selected, "i=47", kTarget, false, false, "prod-a", m_cancellation.Token)
                    .ConfigureAwait(false),
                "DeleteReferenceTarget" => await NodeManagementServiceTools.DeleteReferencesAsync(
                    manager, kObject, "i=47", selected, false, false, "prod-a", m_cancellation.Token)
                    .ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };

            if (!enabled)
            {
                AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            }
            else if (securityMode != MessageSecurityMode.SignAndEncrypt)
            {
                AssertDenied(json, "BadSecurityModeInsufficient");
            }
            else
            {
                AssertNamespaceMetadataNodeChange(operation, target, json);
                AssertSelectedSession(manager);
            }
            AssertNamespaceMetadataOwnership(target);
        }

        [TestCase(false, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(true, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(true, MessageSecurityMode.None)]
        [TestCase(true, MessageSecurityMode.Sign)]
        public async Task CreatingNamespaceMetadataByTypeRequiresIdentityAsync(
            bool enabled, MessageSecurityMode securityMode)
        {
            m_model.AddNode(ObjectTypeIds.NamespaceMetadataType);
            m_model.Endpoint.SecurityMode = securityMode;
            OpcUaSessionManager manager = await CreateManagerAsync(
                NamespaceMetadataAuthority(enabled ? "Granted" : "Disabled")).ConfigureAwait(false);
            using IDisposable caller = m_callers.EnterTrustedCaller(s_caller);

            string json = await NodeManagementServiceTools.AddNodesAsync(
                manager, kParent, "i=35", "2:CreatedNamespaceMetadata", "Object",
                ObjectTypeIds.NamespaceMetadataType.ToString(), "prod-a", m_cancellation.Token).ConfigureAwait(false);

            if (!enabled)
            {
                AssertDenied(json, "BadUserAccessDenied", kIdentityHostDenied);
            }
            else if (securityMode != MessageSecurityMode.SignAndEncrypt)
            {
                AssertDenied(json, "BadSecurityModeInsufficient");
            }
            else
            {
                WireRequest<AddNodesItem> wire = VerifyAddNodes();
                Assert.That(wire.Items.Count, Is.EqualTo(1));
                Assert.That(wire.Items[0].ParentNodeId, Is.EqualTo(new ExpandedNodeId(Node("ParentA"))));
                Assert.That(wire.Items[0].TypeDefinition,
                    Is.EqualTo(new ExpandedNodeId(ObjectTypeIds.NamespaceMetadataType)));
                Assert.That(wire.Items[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
                Assert.That(wire.Items[0].NodeClass, Is.EqualTo(NodeClass.Object));
                Assert.That(wire.Items[0].BrowseName, Is.EqualTo(new QualifiedName("CreatedNamespaceMetadata", 2)));
                AssertNamespaceMetadataAddResult(json);
            }
            AssertClassified(Node("ParentA"));
        }

        [TestCaseSource(nameof(NamespaceMetadataReadCases))]
        public async Task NamespaceMetadataReadsRemainAvailableWithoutMutationAuthorityAsync(
            string targetName, bool enabled, MessageSecurityMode securityMode)
        {
            NodeId target = AddNamespaceMetadataTarget(targetName);
            m_model.Endpoint.SecurityMode = securityMode;
            OpcUaSessionManager manager = await CreateManagerAsync(
                NamespaceMetadataAuthority(enabled ? "Granted" : "Disabled")).ConfigureAwait(false);
            using IDisposable? caller = enabled ? m_callers.EnterTrustedCaller(s_caller) : null;
            bool rootTarget = targetName == "NamespacesMetadata";
            uint attribute = rootTarget ? Attributes.BrowseName : Attributes.Value;
            Variant value;
            if (rootTarget)
            {
                value = new Variant(new QualifiedName("NamespacesMetadata", 2));
            }
            else if (targetName == "DefaultAccessRestrictions")
            {
                value = new Variant((ushort)3);
            }
            else
            {
                ArrayOf<ExtensionObject> permissions = [];
                value = new Variant(permissions);
            }
            WireRequest<ReadValueId>? read = null;
            double requestedMaxAge = -1;
            TimestampsToReturn requestedTimestamps = TimestampsToReturn.Neither;
            m_model.Session.Setup(session => session.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, double maxAge, TimestampsToReturn timestamps,
                    ArrayOf<ReadValueId> items, CancellationToken token) =>
                {
                    read = new WireRequest<ReadValueId>(header, items, token);
                    requestedMaxAge = maxAge;
                    requestedTimestamps = timestamps;
                    return ValueTask.FromResult(new ReadResponse
                    {
                        ResponseHeader = CreateResponseHeader(),
                        Results = [new DataValue(value, StatusCodes.Good, new DateTimeUtc(2026, 1, 2, 3, 4, 5))]
                    });
                });

            string json = await AttributeServiceTools.ReadAsync(
                manager, [target.ToString()], rootTarget ? "BrowseName" : "Value",
                12.5, "prod-a", m_cancellation.Token).ConfigureAwait(false);

            Assert.That(read, Is.Not.Null);
            WireRequest<ReadValueId> wire = read!;
            Assert.That(wire.Header, Is.Null);
            Assert.That(wire.Token, Is.EqualTo(m_cancellation.Token));
            Assert.That(wire.Items.Count, Is.EqualTo(1));
            Assert.That(wire.Items[0].NodeId, Is.EqualTo(target));
            Assert.That(wire.Items[0].AttributeId, Is.EqualTo(attribute));
            Assert.That(requestedMaxAge, Is.EqualTo(12.5));
            Assert.That(requestedTimestamps, Is.EqualTo(TimestampsToReturn.Both));
            m_model.Session.Verify(session => session.ReadAsync(
                null, 12.5, TimestampsToReturn.Both, wire.Items, m_cancellation.Token), Times.Once);
            using var document = JsonDocument.Parse(json);
            JsonElement result = document.RootElement;
            AssertProperties(result, "responseHeader", "results");
            AssertHeader(result);
            Assert.That(result.GetProperty("results").GetArrayLength(), Is.EqualTo(1));
            JsonElement item = result.GetProperty("results")[0];
            Assert.That(item.GetProperty("nodeId").GetString(), Is.EqualTo(target.ToString()));
            JsonElement data = item.GetProperty("result");
            Assert.That(data.GetProperty("statusCode").GetString(), Is.EqualTo("Good"));
            Assert.That(data.GetProperty("sourceTimestamp").GetString(),
                Is.EqualTo("2026-01-02T03:04:05.0000000Z"));
            if (rootTarget)
            {
                Assert.That(data.GetProperty("value").GetString(), Is.EqualTo("2:NamespacesMetadata"));
            }
            else if (targetName == "DefaultAccessRestrictions")
            {
                Assert.That(data.GetProperty("value").GetUInt16(), Is.EqualTo((ushort)3));
            }
            else
            {
                Assert.That(data.GetProperty("value").ValueKind, Is.EqualTo(JsonValueKind.Array));
                Assert.That(data.GetProperty("value").GetArrayLength(), Is.Zero);
            }
            Assert.That(manager.OperationPolicy.IsEnabled(McpCapability.IdentityAdministration), Is.EqualTo(enabled));
            Assert.That(m_callers.Current, Is.EqualTo(enabled ? s_caller : null));
            m_model.VerifyNoMutations();
            AssertNoMetadata();
        }

        [Test]
        public async Task NamespaceMetadataNamesWithoutTheActualTypeRemainOrdinaryAsync()
        {
            NodeId target = AddNamespaceMetadataTarget("DefaultAccessRestrictions", protectedOwner: false);
            m_model.Endpoint.SecurityMode = MessageSecurityMode.None;
            OpcUaSessionManager manager = await CreateManagerAsync().ConfigureAwait(false);

            string json = await ConvenienceTools.WriteValueAsync(
                manager, target.ToString(), "3", "UInt16", "prod-a", m_cancellation.Token).ConfigureAwait(false);

            WireRequest<WriteValue> wire = VerifyWrite();
            Assert.That(wire.Items.Count, Is.EqualTo(1));
            Assert.That(wire.Items[0].NodeId, Is.EqualTo(target));
            Assert.That(wire.Items[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(wire.Items[0].Value.WrappedValue.TryGetValue(out ushort mask), Is.True);
            Assert.That(mask, Is.EqualTo((ushort)3));
            using var document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("nodeId").GetString(), Is.EqualTo(target.ToString()));
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo("Good"));
            Assert.That(manager.OperationPolicy.IsEnabled(McpCapability.IdentityAdministration), Is.False);
            Assert.That(m_model.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
            AssertClassified(target);
            AssertClassified(Node("NamespacesMetadata"));
        }

        public enum Authority
        {
            OrdinaryDefaultDeny,
            GrantedRole
        }

        private static IEnumerable<TestCaseData> NamespaceMetadataWriteCases()
        {
            foreach (string target in s_namespaceMetadataTargets)
            {
                foreach (bool convenience in new[] { false, true })
                {
                    if (convenience && target == "NamespacesMetadata")
                    {
                        // The metadata root is an Object: its permission attribute, not Value, is writable.
                        continue;
                    }
                    foreach (string authority in new[] { "Disabled", "Ungranted", "Granted" })
                    {
                        yield return new TestCaseData(
                            target, convenience, authority, MessageSecurityMode.SignAndEncrypt);
                    }
                    yield return new TestCaseData(target, convenience, "Granted", MessageSecurityMode.None);
                    yield return new TestCaseData(target, convenience, "Granted", MessageSecurityMode.Sign);
                }
            }
        }

        private static IEnumerable<TestCaseData> NamespaceMetadataPermissionAttributeCases()
        {
            (string Name, uint Id)[] attributes =
            [
                ("RolePermissions", Attributes.RolePermissions),
                ("UserRolePermissions", Attributes.UserRolePermissions),
                ("AccessRestrictions", Attributes.AccessRestrictions),
                ("UserAccessLevel", Attributes.UserAccessLevel),
                ("UserExecutable", Attributes.UserExecutable),
                ("UserWriteMask", Attributes.UserWriteMask),
                ("AccessLevel", Attributes.AccessLevel),
                ("AccessLevelEx", Attributes.AccessLevelEx),
                ("Executable", Attributes.Executable),
                ("WriteMask", Attributes.WriteMask)
            ];
            foreach ((string name, uint id) in attributes)
            {
                yield return new TestCaseData(name, id, false);
                yield return new TestCaseData(name, id, true);
            }
        }

        private static IEnumerable<TestCaseData> NamespaceMetadataNodeCases()
        {
            foreach (string target in s_namespaceMetadataTargets)
            {
                foreach (string operation in s_namespaceMetadataNodeOperations)
                {
                    yield return new TestCaseData(target, operation, false, MessageSecurityMode.SignAndEncrypt);
                    yield return new TestCaseData(target, operation, true, MessageSecurityMode.SignAndEncrypt);
                }
            }
            foreach (string operation in s_namespaceMetadataNodeOperations)
            {
                yield return new TestCaseData("NamespacesMetadata", operation, true, MessageSecurityMode.None);
                yield return new TestCaseData("NamespacesMetadata", operation, true, MessageSecurityMode.Sign);
            }
        }

        private static IEnumerable<TestCaseData> NamespaceMetadataReadCases()
        {
            foreach (string target in s_namespaceMetadataTargets)
            {
                foreach (bool enabled in new[] { false, true })
                {
                    yield return new TestCaseData(target, enabled, MessageSecurityMode.None);
                    yield return new TestCaseData(target, enabled, MessageSecurityMode.SignAndEncrypt);
                }
            }
        }

        private NodeId AddNamespaceMetadataTarget(string targetName, bool protectedOwner = true)
        {
            NodeId root = Node("NamespacesMetadata");
            NodeId type = protectedOwner ? ObjectTypeIds.NamespaceMetadataType : ObjectTypeIds.BaseObjectType;
            m_model.AddNode(type);
            m_model.AddNode(VariableTypeIds.PropertyType);
            m_model.AddTypeDefinition(root, new ExpandedNodeId(type));
            foreach (string name in s_namespaceMetadataTargets)
            {
                if (name != "NamespacesMetadata")
                {
                    m_model.SetBrowsePage(
                        Node(name), ReferenceTypeIds.HasTypeDefinition, BrowseDirection.Forward,
                        [
                            new ReferenceDescription
                            {
                                NodeId = new ExpandedNodeId(VariableTypeIds.PropertyType),
                                ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                                IsForward = true,
                                NodeClass = NodeClass.VariableType
                            }
                        ]);
                    m_model.AddOwner(Node(name), new ExpandedNodeId(root), ReferenceTypeIds.HasProperty);
                }
            }
            Assert.That(m_model.NamespaceUris.GetString(2), Is.EqualTo(McpAuthorizationModel.BenignNamespaceUri));
            return Node(targetName);
        }

        private static McpExecutionOptions NamespaceMetadataAuthority(string authority)
        {
            McpExecutionOptions options = GrantedOptions(McpCapability.IdentityAdministration);
            if (authority == "Disabled")
            {
                options.EnabledCapabilities = [];
            }
            else if (authority == "Ungranted")
            {
                options.CallerGrants[0].Capabilities = [];
            }
            return options;
        }

        private void AssertNamespaceMetadataOwnership(NodeId target)
        {
            NodeId root = Node("NamespacesMetadata");
            AssertClassified(target);
            AssertClassified(root);
            ArrayOf<ReferenceDescription> types = m_model.Browses.Single(query =>
                query.Request.NodeId == root &&
                query.Request.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition).Result.References;
            Assert.That(types.Count, Is.EqualTo(1));
            Assert.That(types[0].NodeId, Is.EqualTo(new ExpandedNodeId(ObjectTypeIds.NamespaceMetadataType)));
            Assert.That(types[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasTypeDefinition));
            Assert.That(types[0].IsForward, Is.True);
            if (target != root)
            {
                ArrayOf<ReferenceDescription> propertyTypes = m_model.Browses.Single(query =>
                    query.Request.NodeId == target &&
                    query.Request.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition).Result.References;
                Assert.That(propertyTypes.Count, Is.EqualTo(1));
                Assert.That(propertyTypes[0].NodeId, Is.EqualTo(new ExpandedNodeId(VariableTypeIds.PropertyType)));
                Assert.That(propertyTypes[0].NodeClass, Is.EqualTo(NodeClass.VariableType));
                ArrayOf<ReferenceDescription> owners = m_model.Browses.Single(query =>
                    query.Request.NodeId == target &&
                    query.Request.ReferenceTypeId == ReferenceTypeIds.HierarchicalReferences).Result.References;
                Assert.That(owners.Count, Is.EqualTo(1));
                Assert.That(owners[0].NodeId, Is.EqualTo(new ExpandedNodeId(root)));
                Assert.That(owners[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
                Assert.That(owners[0].IsForward, Is.False);
            }
        }

        private void AssertNamespaceMetadataNodeChange(string operation, NodeId target, string json)
        {
            switch (operation)
            {
                case "AddChild":
                    WireRequest<AddNodesItem> added = VerifyAddNodes();
                    Assert.That(added.Items.Count, Is.EqualTo(1));
                    AddNodesItem node = added.Items[0];
                    Assert.That(node.ParentNodeId, Is.EqualTo(new ExpandedNodeId(target)));
                    Assert.That(node.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
                    Assert.That(node.RequestedNewNodeId, Is.EqualTo(ExpandedNodeId.Null));
                    Assert.That(node.TypeDefinition, Is.EqualTo(ExpandedNodeId.Null));
                    Assert.That(node.NodeClass, Is.EqualTo(NodeClass.Variable));
                    Assert.That(node.BrowseName, Is.EqualTo(new QualifiedName("CreatedMetadataProperty", 2)));
                    Assert.That(node.NodeAttributes.TryGetValue(out VariableAttributes? attributes), Is.True);
                    Assert.That(attributes!.DisplayName.Text, Is.EqualTo("2:CreatedMetadataProperty"));
                    Assert.That(attributes.DataType, Is.EqualTo(DataTypeIds.BaseDataType));
                    Assert.That(attributes.AccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
                    Assert.That(attributes.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
                    AssertNamespaceMetadataAddResult(json);
                    break;
                case "DeleteNode":
                    WireRequest<DeleteNodesItem> deleted = VerifyDeleteNodes();
                    Assert.That(deleted.Items.Count, Is.EqualTo(2));
                    Assert.That(deleted.Items[0].NodeId, Is.EqualTo(Node("ValueB")));
                    Assert.That(deleted.Items[1].NodeId, Is.EqualTo(target));
                    Assert.That(deleted.Items[0].DeleteTargetReferences, Is.False);
                    Assert.That(deleted.Items[1].DeleteTargetReferences, Is.False);
                    AssertStatusResponse(json, "Good", "Good");
                    break;
                case "AddReferenceSource":
                case "AddReferenceTarget":
                    WireRequest<AddReferencesItem> referencesAdded = VerifyAddReferences();
                    Assert.That(referencesAdded.Items.Count, Is.EqualTo(1));
                    AddReferencesItem reference = referencesAdded.Items[0];
                    Assert.That(reference.SourceNodeId,
                        Is.EqualTo(operation == "AddReferenceSource" ? target : Node("ObjectA")));
                    Assert.That(reference.TargetNodeId,
                        Is.EqualTo(new ExpandedNodeId(operation == "AddReferenceTarget" ? target : Node("TargetA"))));
                    Assert.That(reference.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                    Assert.That(reference.IsForward, Is.False);
                    Assert.That(reference.TargetNodeClass, Is.EqualTo(NodeClass.Unspecified));
                    AssertStatusResponse(json, "Good");
                    break;
                case "DeleteReferenceSource":
                case "DeleteReferenceTarget":
                    WireRequest<DeleteReferencesItem> referencesDeleted = VerifyDeleteReferences();
                    Assert.That(referencesDeleted.Items.Count, Is.EqualTo(1));
                    DeleteReferencesItem removed = referencesDeleted.Items[0];
                    Assert.That(removed.SourceNodeId,
                        Is.EqualTo(operation == "DeleteReferenceSource" ? target : Node("ObjectA")));
                    NodeId removedTarget = operation == "DeleteReferenceTarget" ? target : Node("TargetA");
                    Assert.That(removed.TargetNodeId, Is.EqualTo(new ExpandedNodeId(removedTarget)));
                    Assert.That(removed.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
                    Assert.That(removed.IsForward, Is.False);
                    Assert.That(removed.DeleteBidirectional, Is.False);
                    AssertStatusResponse(json, "Good");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private static void AssertNamespaceMetadataAddResult(string json)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            AssertProperties(root, "responseHeader", "results");
            AssertHeader(root);
            Assert.That(root.GetProperty("results").GetArrayLength(), Is.EqualTo(1));
            JsonElement result = root.GetProperty("results")[0];
            AssertProperties(result, "statusCode", "addedNodeId");
            Assert.That(result.GetProperty("statusCode").GetString(), Is.EqualTo("Good"));
            Assert.That(result.GetProperty("addedNodeId").GetString(), Is.EqualTo("ns=2;s=CreatedNode"));
        }

        private static IEnumerable<TestCaseData> CallAllowedCases()
        {
            foreach (Authority authority in s_authorities)
            {
                yield return new TestCaseData(authority, null);
                yield return new TestCaseData(authority, Array.Empty<string>());
                yield return new TestCaseData(authority, s_singleCallInput.ToArray());
                yield return new TestCaseData(authority, s_multipleCallInputs.ToArray());
            }
        }

        private static IEnumerable<TestCaseData> WriteAllowedCases()
        {
            foreach (Authority authority in s_authorities)
            {
                foreach (int count in new[] { 1, 2 })
                {
                    foreach (string? attribute in new string?[] { null, "Value", "13" })
                    {
                        yield return new TestCaseData(authority, count, attribute, false);
                    }
                }
                yield return new TestCaseData(authority, 2, "Value", true);
            }
        }

        private static IEnumerable<TestCaseData> AddNodeAllowedCases()
        {
            // Valid DTOs isolate authorization wiring from unrelated node-class construction behavior.
            (string Name, NodeClass Class, Type Attributes)[] classes =
            [
                ("Object", NodeClass.Object, typeof(ObjectAttributes)),
                ("Variable", NodeClass.Variable, typeof(VariableAttributes))
            ];
            foreach (Authority authority in s_authorities)
            {
                foreach ((string name, NodeClass nodeClass, Type attributes) in classes)
                {
                    yield return new TestCaseData(authority, name, nodeClass, attributes, true, "Created", (ushort)0);
                }
                yield return new TestCaseData(
                    authority, "Object", NodeClass.Object, typeof(ObjectAttributes), false, "Created", (ushort)0);
                yield return new TestCaseData(
                    authority, "Variable", NodeClass.Variable, typeof(VariableAttributes),
                    true, "2:Created", (ushort)2);
            }
        }

        private static IEnumerable<TestCaseData> DeleteNodeAllowedCases()
        {
            foreach (Authority authority in s_authorities)
            {
                foreach (bool deleteReferences in new[] { false, true })
                {
                    yield return new TestCaseData(authority, 1, deleteReferences, false);
                    yield return new TestCaseData(authority, 3, deleteReferences, false);
                    yield return new TestCaseData(authority, 2, deleteReferences, true);
                }
            }
        }

        private static IEnumerable<TestCaseData> DeleteReferenceAllowedCases()
        {
            foreach (Authority authority in s_authorities)
            {
                foreach (bool forward in new[] { false, true })
                {
                    foreach (bool bidirectional in new[] { false, true })
                    {
                        yield return new TestCaseData(authority, forward, bidirectional);
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> HistoryAllowedCases()
        {
            foreach (Authority authority in s_authorities)
            {
                yield return new TestCaseData(authority, "Insert", PerformUpdateType.Insert, false);
                yield return new TestCaseData(authority, "insert", PerformUpdateType.Insert, false);
                yield return new TestCaseData(authority, "Replace", PerformUpdateType.Replace, false);
                yield return new TestCaseData(authority, "rEpLaCe", PerformUpdateType.Replace, false);
                yield return new TestCaseData(authority, "Update", PerformUpdateType.Update, false);
                yield return new TestCaseData(authority, "unrecognized", PerformUpdateType.Update, false);
                yield return new TestCaseData(authority, "Update", PerformUpdateType.Update, true);
            }
        }

        private static IEnumerable<TestCaseData> PermissionAttributeCases()
        {
            yield return new TestCaseData("RolePermissions", Attributes.RolePermissions);
            yield return new TestCaseData("24", Attributes.RolePermissions);
            yield return new TestCaseData("UserRolePermissions", Attributes.UserRolePermissions);
            yield return new TestCaseData("25", Attributes.UserRolePermissions);
            yield return new TestCaseData("AccessRestrictions", Attributes.AccessRestrictions);
            yield return new TestCaseData("26", Attributes.AccessRestrictions);
            yield return new TestCaseData("UserAccessLevel", Attributes.UserAccessLevel);
            yield return new TestCaseData("18", Attributes.UserAccessLevel);
            yield return new TestCaseData("UserExecutable", Attributes.UserExecutable);
            yield return new TestCaseData("22", Attributes.UserExecutable);
            yield return new TestCaseData("UserWriteMask", Attributes.UserWriteMask);
            yield return new TestCaseData("7", Attributes.UserWriteMask);
        }

        private static IEnumerable<TestCaseData> PermissionMetadataCases()
        {
            (string Name, string Numeric, uint Attribute)[] attributes =
            [
                ("AccessLevel", "17", Attributes.AccessLevel),
                ("AccessLevelEx", "27", Attributes.AccessLevelEx),
                ("Executable", "21", Attributes.Executable),
                ("WriteMask", "6", Attributes.WriteMask)
            ];
            foreach ((string name, string numeric, uint attribute) in attributes)
            {
                foreach (bool granted in new[] { false, true })
                {
                    yield return new TestCaseData(name, attribute, granted);
                    yield return new TestCaseData(numeric, attribute, granted);
                }
            }
        }

        private static IEnumerable<TestCaseData> SksCases()
        {
            foreach (bool convenience in new[] { false, true })
            {
                yield return new TestCaseData(
                    convenience, ObjectTypeIds.PubSubKeyServiceType, McpCapability.PubSubSecurity);
                yield return new TestCaseData(
                    convenience, ObjectTypeIds.SecurityGroupType, McpCapability.PubSubSecurity);
                yield return new TestCaseData(
                    convenience, ObjectTypeIds.SecurityGroupFolderType, McpCapability.PubSubSecurity);
                yield return new TestCaseData(
                    convenience, ObjectTypeIds.PublishSubscribeType, McpCapability.PubSubConfiguration);
            }
        }

        private void InitializeModel(int gdsIndex)
        {
            string[] namespaces =
                [.. Enumerable.Range(1, Math.Max(5, gdsIndex)).Select(index => $"urn:test:padding:{index}")];
            namespaces[1] = McpAuthorizationModel.BenignNamespaceUri;
            namespaces[gdsIndex - 1] = McpAuthorizationModel.GdsNamespaceUri;
            m_model = new McpAuthorizationModel(namespaceUris: namespaces);
            m_model.Session.SetupGet(session => session.SessionName).Returns("ServerAssigned");
            foreach (string name in new[] { "ObjectA", "MethodA", "ValueA", "ValueB", "ParentA", "TypeA", "TargetA" })
            {
                m_model.AddNode(Node(name));
            }
            ConfigureRawServices();
        }

        private async Task<OpcUaSessionManager> CreateManagerAsync(
            McpExecutionOptions? options = null, bool fullRegistration = false,
            string authType = "Anonymous", bool register = true)
        {
            var services = new ServiceCollection();
            services.AddSingleton(m_callers);
            services.AddSingleton<IMcpFixedSessionContract>(new FixedMockSessionContract(m_model.Session.Object));
            services.AddOpcUaMcpCore(new OpcUaMcpOptions { Execution = options ?? GrantedOptions() });
            if (fullRegistration)
            {
                services.AddMcpServer().WithOpcUaCoreTools(McpToolProfile.Full);
            }
            m_provider = services.BuildServiceProvider();
            OpcUaSessionManager manager = m_provider.GetRequiredService<OpcUaSessionManager>();
            Assert.That(manager.OperationPolicy, Is.SameAs(m_provider.GetRequiredService<IMcpOperationPolicy>()));
            Assert.That(m_provider.GetRequiredService<McpCallerContext>(), Is.SameAs(m_callers));
            if (register)
            {
                string message = await manager.RegisterExistingSessionAsync(
                    kSession, m_model.Session.Object, authType, m_cancellation.Token).ConfigureAwait(false);
                Assert.That(message, Is.EqualTo(
                    "Registered existing session 'Prod-A'. SessionName=ServerAssigned, SessionId=ns=1;s=wire-session"));
            }
            return manager;
        }

        private McpExecutionOptions ConfigureAuthority(Authority authority, params NodeId[] nodes)
        {
            if (authority == Authority.OrdinaryDefaultDeny)
            {
                return GrantedOptions();
            }
            foreach (NodeId node in nodes)
            {
                ProtectRole(node);
            }
            return GrantedOptions(McpCapability.IdentityAdministration);
        }

        private static McpExecutionOptions GrantedOptions(params McpCapability[] capabilities)
        {
            return new McpExecutionOptions
            {
                EnabledCapabilities = capabilities,
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = s_caller,
                        Capabilities = capabilities,
                        SessionNames = [kSession]
                    }
                ]
            };
        }

        private void ProtectRole(NodeId node)
        {
            m_model.AddTypeDefinition(node, new ExpandedNodeId(ObjectTypeIds.RoleType));
        }

        private void ProtectCallTarget(bool protectedObject)
        {
            if (protectedObject)
            {
                ProtectRole(Node("ObjectA"));
            }
            else
            {
                NodeId owner = m_model.AddNode(Node("RoleOwner"));
                ProtectRole(owner);
                m_model.AddOwner(Node("MethodA"), new ExpandedNodeId(owner), ReferenceTypeIds.HasComponent);
            }
        }

        private string ConfigureModelFailure(string failure)
        {
            NodeId root = Node("ValueA");
            switch (failure)
            {
                case "Unknown":
                    return "ns=2;s=Missing";
                case "BadMetadata":
                    m_model.SetBrowseFailure(root, ReferenceTypeIds.HasTypeDefinition,
                        BrowseDirection.Forward, StatusCodes.BadNodeIdUnknown);
                    break;
                case "Unresolved":
                    m_model.AddOwner(root, new ExpandedNodeId(
                        new NodeId("Owner", 0), "urn:test:not-advertised"), ReferenceTypeIds.HasComponent);
                    break;
                case "Remote":
                    m_model.AddOwner(root, new ExpandedNodeId(Node("TargetA"), serverIndex: 1),
                        ReferenceTypeIds.HasComponent);
                    break;
                case "NullReference":
                    m_model.AddOwner(root, ExpandedNodeId.Null, ReferenceTypeIds.HasComponent);
                    break;
                case "NullTarget":
                    return "i=0";
                case "Nodes129":
                    for (int index = 0; index < 128; index++)
                    {
                        NodeId owner = m_model.AddNode(Node($"Wide{index}"));
                        m_model.AddOwner(root, new ExpandedNodeId(owner), ReferenceTypeIds.HasComponent);
                    }
                    break;
                case "Depth17":
                    NodeId previous = root;
                    for (int index = 1; index <= 17; index++)
                    {
                        NodeId owner = m_model.AddNode(Node($"Depth{index}"));
                        m_model.AddOwner(previous, new ExpandedNodeId(owner), ReferenceTypeIds.HasComponent);
                        previous = owner;
                    }
                    break;
                case "References513":
                    var references = new ReferenceDescription[513];
                    for (int index = 0; index < references.Length; index++)
                    {
                        references[index] = new ReferenceDescription
                        {
                            NodeId = new ExpandedNodeId(Node("TargetA")),
                            ReferenceTypeId = ReferenceTypeIds.HasNotifier,
                            IsForward = false,
                            NodeClass = NodeClass.Object
                        };
                    }
                    m_model.SetBrowsePage(root, ReferenceTypeIds.HierarchicalReferences,
                        BrowseDirection.Inverse, references);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(failure));
            }
            return kValueA;
        }

        private Task<string> InvokeEntryAsync(OpcUaSessionManager manager, string entry)
        {
            CancellationToken token = m_cancellation.Token;
            return entry switch
            {
                "Call" => MethodServiceTools.CallAsync(manager, kObject, kMethod, ["42"], kSession, token),
                "CallMethod" => ConvenienceTools.CallMethodAsync(manager, kObject, kMethod, ["42"], kSession, token),
                "Write" => AttributeServiceTools.WriteAsync(manager, [kValueA], ["42"], "Value", kSession, token),
                "WriteValue" => ConvenienceTools.WriteValueAsync(manager, kValueA, "42", "Int32", kSession, token),
                "AddNodes" => NodeManagementServiceTools.AddNodesAsync(
                    manager, kParent, "i=35", "Created", "Object", kType, kSession, token),
                "DeleteNodes" => NodeManagementServiceTools.DeleteNodesAsync(
                    manager, [kValueA], false, kSession, token),
                "AddReferences" => NodeManagementServiceTools.AddReferencesAsync(
                    manager, kObject, "i=47", kTarget, false, kSession, token),
                "DeleteReferences" => NodeManagementServiceTools.DeleteReferencesAsync(
                    manager, kObject, "i=47", kTarget, false, true, kSession, token),
                "HistoryUpdate" => AttributeServiceTools.HistoryUpdateAsync(
                    manager, kValueA, [kFirstTimestamp, kSecondTimestamp], ["42", "beta"], "Insert", kSession, token),
                _ => throw new ArgumentOutOfRangeException(nameof(entry))
            };
        }

        private void ConfigureRawServices()
        {
            // These callbacks intentionally ignore cancellation: any forbidden dispatch is recorded and succeeds.
            m_model.Session.Setup(session => session.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<CallMethodRequest> items, CancellationToken token) =>
                {
                    m_model.Mutations.Add("Call");
                    m_call = new WireRequest<CallMethodRequest>(header, items, token);
                    return ValueTask.FromResult(new CallResponse
                    {
                        ResponseHeader = CreateResponseHeader(),
                        Results = items.ConvertAll(item => new CallMethodResult
                        {
                            StatusCode = m_callStatus,
                            OutputArguments = m_callOutputs,
                            InputArgumentResults = m_returnNullInputResults
                                ? default
                                : m_inputResults.IsNull ? GoodStatuses(item.InputArguments.Count) : m_inputResults
                        })
                    });
                });
            m_model.Session.Setup(session => session.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<WriteValue> items, CancellationToken token) =>
                {
                    m_model.Mutations.Add("Write");
                    m_write = new WireRequest<WriteValue>(header, items, token);
                    return ValueTask.FromResult(new WriteResponse
                    {
                        ResponseHeader = CreateResponseHeader(),
                        Results = m_writeResults.IsNull ? GoodStatuses(items.Count) : m_writeResults
                    });
                });
            m_model.Session.Setup(session => session.AddNodesAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<AddNodesItem>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<AddNodesItem> items, CancellationToken token) =>
                {
                    m_model.Mutations.Add("AddNodes");
                    m_addNodes = new WireRequest<AddNodesItem>(header, items, token);
                    return ValueTask.FromResult(new AddNodesResponse
                    {
                        ResponseHeader = CreateResponseHeader(),
                        Results = items.ConvertAll(_ => new AddNodesResult
                        {
                            StatusCode = StatusCodes.Good,
                            AddedNodeId = Node("CreatedNode")
                        })
                    });
                });
            m_model.Session.Setup(session => session.DeleteNodesAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<DeleteNodesItem>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<DeleteNodesItem> items, CancellationToken token) =>
                {
                    m_model.Mutations.Add("DeleteNodes");
                    m_deleteNodes = new WireRequest<DeleteNodesItem>(header, items, token);
                    return ValueTask.FromResult(new DeleteNodesResponse
                    {
                        ResponseHeader = CreateResponseHeader(),
                        Results = m_deleteResults.IsNull ? GoodStatuses(items.Count) : m_deleteResults
                    });
                });
            m_model.Session.Setup(session => session.AddReferencesAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<AddReferencesItem>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<AddReferencesItem> items, CancellationToken token) =>
                {
                    m_model.Mutations.Add("AddReferences");
                    m_addReferences = new WireRequest<AddReferencesItem>(header, items, token);
                    return ValueTask.FromResult(new AddReferencesResponse
                    {
                        ResponseHeader = CreateResponseHeader(),
                        Results = GoodStatuses(items.Count)
                    });
                });
            m_model.Session.Setup(session => session.DeleteReferencesAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<DeleteReferencesItem>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<DeleteReferencesItem> items, CancellationToken token) =>
                {
                    m_model.Mutations.Add("DeleteReferences");
                    m_deleteReferences = new WireRequest<DeleteReferencesItem>(header, items, token);
                    return ValueTask.FromResult(new DeleteReferencesResponse
                    {
                        ResponseHeader = CreateResponseHeader(),
                        Results = GoodStatuses(items.Count)
                    });
                });
            m_model.Session.Setup(session => session.HistoryUpdateAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<ExtensionObject>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<ExtensionObject> items, CancellationToken token) =>
                {
                    m_model.Mutations.Add("HistoryUpdate");
                    m_history = new WireRequest<ExtensionObject>(header, items, token);
                    return ValueTask.FromResult(new HistoryUpdateResponse
                    {
                        ResponseHeader = CreateResponseHeader(),
                        Results = items.ConvertAll(_ => new HistoryUpdateResult
                        {
                            StatusCode = StatusCodes.Good,
                            OperationResults = m_historyResults
                        })
                    });
                });
        }

        private WireRequest<T> AssertWire<T>(WireRequest<T>? wire, string operation)
        {
            Assert.That(wire, Is.Not.Null);
            Assert.That(m_model.Mutations, Is.EqualTo([operation]));
            Assert.That(wire!.Header, Is.Null);
            Assert.That(wire.Token, Is.EqualTo(m_cancellation.Token));
            Assert.That(wire.Token.CanBeCanceled, Is.True);
            return wire;
        }

        private WireRequest<CallMethodRequest> VerifyCall()
        {
            WireRequest<CallMethodRequest> wire = AssertWire(m_call, "Call");
            m_model.Session.Verify(session => session.CallAsync(null, wire.Items, m_cancellation.Token), Times.Once);
            return wire;
        }

        private WireRequest<WriteValue> VerifyWrite()
        {
            WireRequest<WriteValue> wire = AssertWire(m_write, "Write");
            m_model.Session.Verify(session => session.WriteAsync(null, wire.Items, m_cancellation.Token), Times.Once);
            return wire;
        }

        private WireRequest<AddNodesItem> VerifyAddNodes()
        {
            WireRequest<AddNodesItem> wire = AssertWire(m_addNodes, "AddNodes");
            m_model.Session.Verify(session => session.AddNodesAsync(
                null, wire.Items, m_cancellation.Token), Times.Once);
            return wire;
        }

        private WireRequest<DeleteNodesItem> VerifyDeleteNodes()
        {
            WireRequest<DeleteNodesItem> wire = AssertWire(m_deleteNodes, "DeleteNodes");
            m_model.Session.Verify(session => session.DeleteNodesAsync(
                null, wire.Items, m_cancellation.Token), Times.Once);
            return wire;
        }

        private WireRequest<AddReferencesItem> VerifyAddReferences()
        {
            WireRequest<AddReferencesItem> wire = AssertWire(m_addReferences, "AddReferences");
            m_model.Session.Verify(session => session.AddReferencesAsync(
                null, wire.Items, m_cancellation.Token), Times.Once);
            return wire;
        }

        private WireRequest<DeleteReferencesItem> VerifyDeleteReferences()
        {
            WireRequest<DeleteReferencesItem> wire = AssertWire(m_deleteReferences, "DeleteReferences");
            m_model.Session.Verify(session => session.DeleteReferencesAsync(
                null, wire.Items, m_cancellation.Token), Times.Once);
            return wire;
        }

        private WireRequest<ExtensionObject> VerifyHistory()
        {
            WireRequest<ExtensionObject> wire = AssertWire(m_history, "HistoryUpdate");
            m_model.Session.Verify(session => session.HistoryUpdateAsync(
                null, wire.Items, m_cancellation.Token), Times.Once);
            return wire;
        }

        private void AssertCallRequest(string[]? inputs)
        {
            WireRequest<CallMethodRequest> wire = VerifyCall();
            Assert.That(wire.Items.Count, Is.EqualTo(1));
            Assert.That(wire.Items[0].ObjectId, Is.EqualTo(Node("ObjectA")));
            Assert.That(wire.Items[0].MethodId, Is.EqualTo(Node("MethodA")));
            ArrayOf<Variant> arguments = wire.Items[0].InputArguments;
            Assert.That(arguments.IsNull, Is.False);
            Assert.That(arguments.Count, Is.EqualTo(inputs?.Length ?? 0));
            for (int index = 0; index < arguments.Count; index++)
            {
                AssertStringVariant(arguments[index], inputs![index]);
            }
        }

        private void AssertStringWrites(string[] nodes, string[] values, uint attribute)
        {
            WireRequest<WriteValue> wire = VerifyWrite();
            Assert.That(wire.Items.Count, Is.EqualTo(nodes.Length));
            for (int index = 0; index < nodes.Length; index++)
            {
                Assert.That(wire.Items[index].NodeId, Is.EqualTo(NodeId.Parse(nodes[index])));
                Assert.That(wire.Items[index].AttributeId, Is.EqualTo(attribute));
                Assert.That(wire.Items[index].Value.StatusCode.Code, Is.EqualTo(StatusCodes.Good.Code));
                AssertStringVariant(wire.Items[index].Value.WrappedValue, values[index]);
            }
        }

        private void AssertInt32Write(NodeId node, int value)
        {
            WireRequest<WriteValue> wire = VerifyWrite();
            Assert.That(wire.Items.Count, Is.EqualTo(1));
            Assert.That(wire.Items[0].NodeId, Is.EqualTo(node));
            Assert.That(wire.Items[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(wire.Items[0].Value.StatusCode.Code, Is.EqualTo(StatusCodes.Good.Code));
            Variant variant = wire.Items[0].Value.WrappedValue;
            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(variant.TryGetValue(out int actual), Is.True);
            Assert.That(actual, Is.EqualTo(value));
        }

        private static void AssertStringVariant(Variant variant, string expected)
        {
            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.String));
            Assert.That(variant.TryGetValue(out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private void AssertDenied(string json, string status, string? message = null)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            using (Assert.EnterMultipleScope())
            {
                AssertProperties(root, "error", "statusCode", "message");
                Assert.That(root.TryGetProperty("error", out JsonElement error) &&
                    error.ValueKind == JsonValueKind.True, Is.True);
                Assert.That(root.TryGetProperty("statusCode", out JsonElement code) ? code.GetString() : null,
                    Is.EqualTo(status));
                string? actualMessage = root.TryGetProperty("message", out JsonElement text) ? text.GetString() : null;
                Assert.That(actualMessage, Is.Not.Null.And.Not.Empty);
                if (message is not null)
                {
                    Assert.That(actualMessage, Is.EqualTo(message));
                }
                m_model.VerifyNoMutations();
            }
        }

        private static void AssertStatusResponse(string json, params string[] statuses)
        {
            using var document = JsonDocument.Parse(json);
            AssertProperties(document.RootElement, "responseHeader", "results");
            AssertHeader(document.RootElement);
            AssertStatuses(document.RootElement.GetProperty("results"), statuses);
        }

        private static void AssertWriteValueResponse(string json, string status)
        {
            using var document = JsonDocument.Parse(json);
            AssertProperties(document.RootElement, "nodeId", "statusCode");
            Assert.That(document.RootElement.GetProperty("nodeId").GetString(), Is.EqualTo(kValueA));
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo(status));
        }

        private static void AssertHeader(JsonElement root)
        {
            JsonElement header = root.GetProperty("responseHeader");
            AssertProperties(header, "timestamp", "requestHandle", "serviceResult");
            Assert.That(header.GetProperty("timestamp").GetString(), Is.EqualTo("2026-01-02T03:04:05.0000000Z"));
            Assert.That(header.GetProperty("requestHandle").GetUInt32(), Is.EqualTo(73u));
            Assert.That(header.GetProperty("serviceResult").GetString(), Is.EqualTo("Good"));
        }

        private static void AssertOutputs(JsonElement root)
        {
            JsonElement arguments = root.GetProperty("outputArguments");
            Assert.That(arguments.GetArrayLength(), Is.EqualTo(2));
            Assert.That(arguments[0].ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(arguments[0].GetInt32(), Is.EqualTo(17));
            Assert.That(arguments[1].ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(arguments[1].GetString(), Is.EqualTo("call-result"));
        }

        private static void AssertProperties(JsonElement root, params string[] names)
        {
            Assert.That(root.EnumerateObject().Select(property => property.Name).ToArray(), Is.EquivalentTo(names));
        }

        private static void AssertStatuses(JsonElement array, params string[] expected)
        {
            Assert.That(array.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(array.EnumerateArray().Select(value => value.GetString()).ToArray(), Is.EqualTo(expected));
        }

        private void AssertSecretPreconditions(OpcUaSessionManager manager, McpCapability capability, string marker)
        {
            Assert.That(manager.OperationPolicy, Is.TypeOf<McpExecutionPolicy>());
            Assert.That(manager.OperationPolicy.RequireSession(capability, manager.GetSessionInfoOrThrow().Name),
                Is.EqualTo(s_caller));
            Assert.That(m_model.Session.Object.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(m_callOutputs.Count, Is.EqualTo(2));
            AssertStringVariant(m_callOutputs[1], marker);
        }

        private void AssertSelectedSession(OpcUaSessionManager manager)
        {
            Assert.That(manager.GetSessionInfoOrThrow("prod-a").Name, Is.EqualTo(kSession));
            Assert.That(manager.GetSessionOrThrow("prod-a"), Is.SameAs(m_model.Session.Object));
        }

        private void AssertClassified(NodeId node)
        {
            Assert.That(m_model.Browses.Any(query =>
                query.Request.NodeId == node && query.Request.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition),
                Is.True, $"The actual target {node} must be classified.");
            foreach (McpAuthorizationModel.BrowseQuery query in m_model.Browses)
            {
                Assert.That(query.Header, Is.Null);
                Assert.That(query.View, Is.Null);
                Assert.That(query.Maximum, Is.EqualTo(100u));
                Assert.That(query.Token, Is.EqualTo(m_cancellation.Token));
                Assert.That(query.Request.IncludeSubtypes, Is.True);
                Assert.That(query.Request.NodeClassMask, Is.Zero);
                Assert.That(query.Request.ResultMask, Is.EqualTo((uint)BrowseResultMask.All));
            }
            Assert.That(m_model.CacheQueries.All(query => query.Token == m_cancellation.Token), Is.True);
        }

        private void AssertNoMetadata()
        {
            Assert.That(m_model.Browses, Is.Empty);
            Assert.That(m_model.Continuations, Is.Empty);
            Assert.That(m_model.CacheQueries, Is.Empty);
            m_model.Session.Verify(session => session.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            m_model.Session.Verify(session => session.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<bool>(), It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private void AssertCancelledBeforeDispatch(OperationCanceledException? failure)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure, Is.TypeOf<OperationCanceledException>());
                if (failure is not null)
                {
                    Assert.That(failure.CancellationToken, Is.EqualTo(m_cancellation.Token));
                }
                m_model.VerifyNoMutations();
                AssertNoMetadata();
            }
        }

        private static async Task<TException> CaptureAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            TException? caught = null;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException exception)
            {
                caught = exception;
            }
            Assert.That(caught, Is.Not.Null, $"Expected {typeof(TException).Name} before dispatch.");
            return caught!;
        }

        private static ResponseHeader CreateResponseHeader()
        {
            return new ResponseHeader
            {
                Timestamp = new DateTimeUtc(2026, 1, 2, 3, 4, 5),
                RequestHandle = 73,
                ServiceResult = StatusCodes.Good
            };
        }

        private static NodeId Node(string identifier)
        {
            return new(identifier, 2);
        }

        private static string[] GoodNames(int count)
        {
            return [.. Enumerable.Repeat("Good", count)];
        }

        private static ArrayOf<StatusCode> GoodStatuses(int count)
        {
            return Enumerable.Repeat(StatusCodes.Good, count).ToArray();
        }

        private sealed record WireRequest<T>(RequestHeader? Header, ArrayOf<T> Items, CancellationToken Token);

        /// <summary>
        /// Declares only this fixture's fixed mock transport; it grants no authority or generation atomicity.
        /// </summary>
        private sealed class FixedMockSessionContract(ISession session) : IMcpFixedSessionContract
        {
            public bool IsFixed(OpcUaSessionManager.SessionInfo registration)
            {
                return registration is not null && ReferenceEquals(registration.Session, session);
            }
        }

        private const string kSession = "Prod-A";
        private const string kObject = "ns=2;s=ObjectA";
        private const string kMethod = "ns=2;s=MethodA";
        private const string kValueA = "ns=2;s=ValueA";
        private const string kValueB = "ns=2;s=ValueB";
        private const string kParent = "ns=2;s=ParentA";
        private const string kType = "ns=2;s=TypeA";
        private const string kTarget = "ns=2;s=TargetA";
        private const string kFirstTimestamp = "2026-01-02T03:04:05Z";
        private const string kSecondTimestamp = "2026-01-03T04:05:06Z";
        private const string kIdentityHostDenied = "The host has not enabled the IdentityAdministration capability.";

        private const string kTypedOnly =
            "This protected model requires a typed, secret-safe MCP tool instead of a generic mutation.";

        private const string kGdsSecret = "synthetic-gds-key-DO-NOT-RETURN-4d8b";
        private const string kSksSecret = "synthetic-sks-key-DO-NOT-RETURN-7a2c";
        private static readonly McpCallerIdentity s_caller = new("Caller-A", "urn:test:issuer-a");
        private static readonly Authority[] s_authorities = [Authority.OrdinaryDefaultDeny, Authority.GrantedRole];
        private static readonly string[] s_singleCallInput = ["42"];
        private static readonly string[] s_multipleCallInputs = ["42", "beta", "quoted \"text\"\nΩ"];

        private static readonly string[] s_namespaceMetadataTargets =
        [
            "NamespacesMetadata", "DefaultRolePermissions", "DefaultUserRolePermissions", "DefaultAccessRestrictions"
        ];

        private static readonly string[] s_namespaceMetadataNodeOperations =
        [
            "AddChild", "DeleteNode", "AddReferenceSource", "AddReferenceTarget",
            "DeleteReferenceSource", "DeleteReferenceTarget"
        ];

        private McpAuthorizationModel m_model = null!;
        private McpCallerContext m_callers = null!;
        private CancellationTokenSource m_cancellation = null!;
        private ServiceProvider? m_provider;
        private StatusCode m_callStatus;
        private ArrayOf<Variant> m_callOutputs;
        private ArrayOf<StatusCode> m_inputResults;
        private bool m_returnNullInputResults;
        private ArrayOf<StatusCode> m_writeResults;
        private ArrayOf<StatusCode> m_deleteResults;
        private ArrayOf<StatusCode> m_historyResults;
        private WireRequest<CallMethodRequest>? m_call;
        private WireRequest<WriteValue>? m_write;
        private WireRequest<AddNodesItem>? m_addNodes;
        private WireRequest<DeleteNodesItem>? m_deleteNodes;
        private WireRequest<AddReferencesItem>? m_addReferences;
        private WireRequest<DeleteReferencesItem>? m_deleteReferences;
        private WireRequest<ExtensionObject>? m_history;
    }
}
#endif
