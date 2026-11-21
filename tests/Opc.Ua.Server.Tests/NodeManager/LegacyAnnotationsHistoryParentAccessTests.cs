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
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// History operations on the Annotations property of a variable are routed to the
    /// annotations of that variable, so legacy <see cref="CustomNodeManager2"/> node
    /// managers apply the access checks of the variable to them as well.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("Historian")]
    public sealed class LegacyAnnotationsHistoryParentAccessTests
    {
        public enum ParentAccess
        {
            Unrestricted,
            UserAccessLevelDenied,
            RolePermissionsDenied,
            AccessCallbackFails,
            PropertyDeniesWrite
        }

        [Test]
        public async Task LegacyNodeManagerAppliesParentAccessToAnnotationsHistoryAsync(
            [Values] ParentAccess parentAccess,
            [Values] bool deferredHandle)
        {
            StatusCode expectedRead = parentAccess switch
            {
                ParentAccess.Unrestricted or ParentAccess.PropertyDeniesWrite => StatusCodes.Good,
                ParentAccess.AccessCallbackFails => StatusCodes.BadConfigurationError,
                _ => StatusCodes.BadUserAccessDenied
            };
            StatusCode expectedUpdate = parentAccess switch
            {
                ParentAccess.Unrestricted => StatusCodes.Good,
                ParentAccess.AccessCallbackFails => StatusCodes.BadConfigurationError,
                _ => StatusCodes.BadUserAccessDenied
            };
            LegacyAnnotationsNodeManager? nodeManager = null;
            var fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(new Factory(parentAccess, created => nodeManager = created));
                return server;
            })
            {
                AutoAccept = true
            };

            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                    await server.CreateAndActivateSessionAsync(nameof(LegacyAnnotationsNodeManager))
                        .ConfigureAwait(false);
                Assert.That(nodeManager, Is.Not.Null);
                ushort namespaceIndex = (ushort)server.CurrentInstance.NamespaceUris
                    .GetIndex(LegacyAnnotationsNodeManager.NamespaceUri);
                var annotationsId = new NodeId(LegacyAnnotationsNodeManager.AnnotationsName, namespaceIndex);
                nodeManager!.DeferHandles = deferredHandle;

                requestHeader.Timestamp = DateTimeUtc.Now;
                HistoryReadResponse readResponse = await server.HistoryReadAsync(
                    secureChannelContext,
                    requestHeader,
                    new ExtensionObject(new ReadRawModifiedDetails
                    {
                        StartTime = DateTime.UtcNow.AddMinutes(-1),
                        EndTime = DateTime.UtcNow.AddSeconds(1),
                        NumValuesPerNode = 0
                    }),
                    TimestampsToReturn.Source,
                    false,
                    [new HistoryReadValueId { NodeId = annotationsId }],
                    RequestLifetime.None).ConfigureAwait(false);
                Assert.That(readResponse.Results.Count, Is.EqualTo(1));
                Assert.That(readResponse.Results[0].StatusCode, Is.EqualTo(expectedRead));
                Assert.That(nodeManager.Reads, Is.EqualTo(expectedRead == StatusCodes.Good ? 1 : 0));

                requestHeader.Timestamp = DateTimeUtc.Now;
                HistoryUpdateResponse updateResponse = await server.HistoryUpdateAsync(
                    secureChannelContext,
                    requestHeader,
                    [
                        new ExtensionObject(new UpdateStructureDataDetails
                        {
                            NodeId = annotationsId,
                            PerformInsertReplace = PerformUpdateType.Insert,
                            UpdateValues =
                            [
                                new DataValue(
                                    new Variant(new ExtensionObject(new Annotation
                                    {
                                        AnnotationTime = DateTime.UtcNow,
                                        UserName = "tester",
                                        Message = "note"
                                    })))
                            ]
                        })
                    ],
                    RequestLifetime.None).ConfigureAwait(false);
                Assert.That(updateResponse.Results.Count, Is.EqualTo(1));
                Assert.That(updateResponse.Results[0].StatusCode, Is.EqualTo(expectedUpdate));
                Assert.That(nodeManager.Updates, Is.EqualTo(expectedUpdate == StatusCodes.Good ? 1 : 0));

                requestHeader.Timestamp = DateTimeUtc.Now;
                await server.CloseSessionAsync(secureChannelContext, requestHeader, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private sealed class Factory(ParentAccess parentAccess, Action<LegacyAnnotationsNodeManager> created)
            : INodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [LegacyAnnotationsNodeManager.NamespaceUri];

            public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
            {
#pragma warning disable CA2000 // Ownership of the node manager is transferred to the server.
                var nodeManager = new LegacyAnnotationsNodeManager(server, configuration, parentAccess);
#pragma warning restore CA2000
                created(nodeManager);
                return nodeManager;
            }
        }

        /// <summary>
        /// A legacy node manager with one historized variable and its Annotations property
        /// that records the history operations which pass the access checks.
        /// </summary>
        private sealed class LegacyAnnotationsNodeManager : CustomNodeManager2
        {
            public const string NamespaceUri = "urn:tests:legacy-annotations-history";
            public const string VariableName = "Historized";
            public const string AnnotationsName = "Historized_Annotations";

            public LegacyAnnotationsNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ParentAccess parentAccess)
                : base(server, configuration, NamespaceUri)
            {
                m_parentAccess = parentAccess;
            }

            public int Reads => Volatile.Read(ref m_reads);

            public int Updates => Volatile.Read(ref m_updates);

            public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
            {
                base.CreateAddressSpace(externalReferences);
                ushort namespaceIndex = NamespaceIndexes[0];
                const byte history = AccessLevels.HistoryRead | AccessLevels.HistoryWrite;

                var variable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(VariableName, namespaceIndex),
                    BrowseName = new QualifiedName(VariableName, namespaceIndex),
                    DisplayName = new LocalizedText(VariableName),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Double,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = history,
                    UserAccessLevel = m_parentAccess == ParentAccess.UserAccessLevelDenied
                        ? AccessLevels.CurrentRead
                        : history,
                    Historizing = true
                };
                if (m_parentAccess == ParentAccess.RolePermissionsDenied)
                {
                    variable.RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                            Permissions = (uint)(PermissionType.ReadHistory | PermissionType.InsertHistory)
                        }
                    ];
                }

                if (m_parentAccess == ParentAccess.AccessCallbackFails)
                {
                    variable.OnReadUserAccessLevel = (ISystemContext _, NodeState _, ref byte _) =>
                        StatusCodes.BadConfigurationError;
                }

                var annotations = new PropertyState(variable)
                {
                    NodeId = new NodeId(AnnotationsName, namespaceIndex),
                    BrowseName = new QualifiedName(BrowseNames.Annotations, 0),
                    DisplayName = new LocalizedText(BrowseNames.Annotations),
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    DataType = DataTypeIds.Annotation,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = history,
                    UserAccessLevel = m_parentAccess == ParentAccess.PropertyDeniesWrite
                        ? AccessLevels.HistoryRead
                        : history
                };
                variable.AddChild(annotations);
                AddPredefinedNode(SystemContext, variable);
            }

            /// <summary>
            /// Whether handles are returned without their node, which is then resolved
            /// when the handle is validated.
            /// </summary>
            public bool DeferHandles { get; set; }

            protected override NodeHandle? GetManagerHandle(
                ServerSystemContext context,
                NodeId nodeId,
                IDictionary<NodeId, NodeState> cache)
            {
                NodeHandle? handle = base.GetManagerHandle(context, nodeId, cache);
                if (DeferHandles && handle != null)
                {
                    handle.Node = null!;
                    handle.Validated = false;
                }
                return handle;
            }

            protected override NodeState? ValidateNode(
                ServerSystemContext context,
                NodeHandle handle,
                IDictionary<NodeId, NodeState> cache)
            {
                if (handle.Node == null && PredefinedNodes.TryGetValue(handle.NodeId, out NodeState? node))
                {
                    handle.Node = node;
                    handle.Validated = true;
                }
                return base.ValidateNode(context, handle, cache);
            }

            protected override void HistoryReadRawModified(
                ServerSystemContext context,
                ReadRawModifiedDetails details,
                TimestampsToReturn timestampsToReturn,
                ArrayOf<HistoryReadValueId> nodesToRead,
                IList<HistoryReadResult> results,
                IList<ServiceResult> errors,
                List<NodeHandle> nodesToProcess,
                IDictionary<NodeId, NodeState> cache)
            {
                Interlocked.Increment(ref m_reads);
                foreach (NodeHandle handle in nodesToProcess)
                {
                    errors[handle.Index] = ServiceResult.Good;
                }
            }

            protected override void HistoryUpdate(
                ServerSystemContext context,
                Type detailsType,
                ArrayOf<HistoryUpdateDetails> nodesToUpdate,
                IList<HistoryUpdateResult> results,
                IList<ServiceResult> errors,
                List<NodeHandle> nodesToProcess,
                IDictionary<NodeId, NodeState> cache)
            {
                Interlocked.Increment(ref m_updates);
                foreach (NodeHandle handle in nodesToProcess)
                {
                    errors[handle.Index] = ServiceResult.Good;
                }
            }

            private readonly ParentAccess m_parentAccess;
            private int m_reads;
            private int m_updates;
        }
    }
}
