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
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Subscriber;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit adapters using the shipped source/sink/action implementations over a scoped borrowed session.
    /// </summary>
    [McpServerToolType]
    public sealed class PubSubAdapterTools
    {
        /// <summary>
        /// Creates adapters without creating sessions, subscriptions or background transport work.
        /// </summary>
        public PubSubAdapterTools(
            McpPubSubRuntimeManager runtimes, OpcUaSessionManager sessions, McpPubSubOptions options, TimeProvider time)
        {
            m_runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_time = time ?? throw new ArgumentNullException(nameof(time));
        }

        /// <summary>
        /// Attaches an explicit cyclic client source to a stopped publisher.
        /// </summary>
        [McpServerTool(Name = "pubsub_adapter_bind_source")]
        [Description("Bind a stopped publisher to exact Value nodes in an existing named session using the shipped " +
            "cyclic-read adapter. Host endpoint/node/read grants required. " +
            "Returns binding status, session name and field count; does not start the runtime or own the session.")]
        public Task<CallToolResult> BindSourceAsync(
            [Description(PubSubMcpFilters.ApplicationHandleDescription)]
            string handle,
            [Description("Ordered field aliases and exact source NodeIds, e.g. ns=2;s=Count. " +
                "Names/order must match the publisher schema; attributeId must be 13 (Value).")]
            ArrayOf<PubSubVariableInput> variables,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return BindSourceAsync(handle, new PubSubVariablesInput { Variables = variables }, sessionName, ct);
        }

        /// <summary>
        /// Binds a source using an existing direct variable mapping.
        /// </summary>
        public Task<CallToolResult> BindSourceAsync(
            string handle, PubSubVariablesInput input, string? sessionName = null, CancellationToken ct = default)
        {
            return BindAsync(handle, input, "read", sessionName, ct);
        }

        /// <summary>
        /// Attaches an explicit subscriber sink to existing writable nodes.
        /// </summary>
        [McpServerTool(Name = "pubsub_adapter_bind_sink", Destructive = true)]
        [Description("Bind a stopped subscriber to exact Value target nodes using the shipped TargetVariables sink. " +
            "Host write grants and generic protected-target guards apply. Returns binding status, session name and " +
            "field count. Later runtime status reports actual per-write outcomes.")]
        public Task<CallToolResult> BindSinkAsync(
            [Description(PubSubMcpFilters.ApplicationHandleDescription)]
            string handle,
            [Description("Ordered field aliases and exact writable target NodeIds, e.g. ns=2;s=Count. " +
                "Names/order must match the subscriber schema; attributeId must be 13 (Value).")]
            ArrayOf<PubSubVariableInput> variables,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return BindSinkAsync(handle, new PubSubVariablesInput { Variables = variables }, sessionName, ct);
        }

        /// <summary>
        /// Binds a sink using an existing direct variable mapping.
        /// </summary>
        public Task<CallToolResult> BindSinkAsync(
            string handle, PubSubVariablesInput input, string? sessionName = null, CancellationToken ct = default)
        {
            return BindAsync(handle, input, "write", sessionName, ct);
        }

        /// <summary>
        /// Attaches one exact method responder through the existing action adapter.
        /// </summary>
        [McpServerTool(Name = "pubsub_adapter_bind_action", Destructive = true)]
        [Description("Bind one PubSub ActionTarget to an exact existing session/object/method. Uses the shared " +
            "generic guard, so issuance/admin methods cannot bypass disabled capabilities. " +
            "Unsecured actions default to refusal. Returns binding status, operation and borrowed session name.")]
        public Task<CallToolResult> BindActionAsync(
            [Description(PubSubMcpFilters.ApplicationHandleDescription)]
            string handle,
            [Description("Exact object/method NodeIds and UInt16 ActionTargetId; optional output names follow method " +
                "output order. allowUnsecured is route consent only and still requires host downgrade permission.")]
            PubSubActionBindingInput input,
            [Description(PubSubMcpFilters.SessionNameDescription)]
            string? sessionName = null, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                OpcUaSessionManager.SessionInfo info = Session(sessionName);
                NodeId objectId = McpPubSubRemote.Node(info.Session, input.ObjectId);
                NodeId methodId = McpPubSubRemote.Node(info.Session, input.MethodId);
                RequireRule(info, "call", objectId, methodId);
                if (input.AllowUnsecured && !m_options.AllowSecurityDowngrade)
                {
                    throw McpPubSubTransportProvider.Refused();
                }
                ArgumentOutOfRangeException.ThrowIfGreaterThan(input.OutputFieldNames.Count, m_options.MaxFields);
                foreach (string name in input.OutputFieldNames)
                {
                    PubSubInputValidation.Text(name, 128);
                }
                await m_sessions.UaOperationGuard.AuthorizeCallsAsync(info,
                    [new CallMethodRequest { ObjectId = objectId, MethodId = methodId }], token).ConfigureAwait(false);
                McpRuntimeUse<McpPubSubRuntime> use = await m_runtimes.AcquireAsync(handle, token)
                    .ConfigureAwait(false);
                await using (use.ConfigureAwait(false))
                {
                    var borrowed = new McpPubSubBorrowedSession(
                        m_sessions, info, "call", [objectId], m_options, m_time, methodId);
                    bool transferred = false;
                    try
                    {
                        ActionMethodMap map = new ActionMethodMap().Add(use.Runtime.Input.DataSetWriterId,
                            input.ActionTargetId, objectId, methodId, input.OutputFieldNames);
                        var handler = new ServerActionHandler(borrowed, map, m_sessions.Telemetry);
                        var target = new PubSubActionTarget
                        {
                            DataSetWriterId = use.Runtime.Input.DataSetWriterId,
                            ActionTargetId = input.ActionTargetId,
                            ConnectionName = McpPubSubConfiguration.ConnectionName
                        };
                        await use.Runtime.BindActionAsync(target, handler, borrowed, input.AllowUnsecured,
                            use.CancellationToken).ConfigureAwait(false);
                        transferred = true;
                        return new JsonObject
                        {
                            ["bound"] = true,
                            ["started"] = false,
                            ["operation"] = "call",
                            ["sessionName"] = info.Name,
                            ["ownsSession"] = false
                        };
                    }
                    finally
                    {
                        if (!transferred)
                        {
                            await borrowed.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
            }, ct);
        }

        /// <summary>
        /// Composes one source/sink using immutable declared metadata and exact target permissions.
        /// </summary>
        private Task<CallToolResult> BindAsync(
            string handle, PubSubVariablesInput input, string operation, string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                OpcUaSessionManager.SessionInfo info = Session(sessionName);
                ArrayOf<PublishedVariableDataType> variables = McpPubSubRemote.Variables(
                    info.Session, input, m_options);
                foreach (PublishedVariableDataType variable in variables)
                {
                    if (variable.AttributeId != Attributes.Value)
                    {
                        throw new ArgumentException("Client adapter mappings support explicit Value attributes only.");
                    }
                    RequireRule(info, operation, variable.PublishedVariable);
                }
                if (operation == "write")
                {
                    await m_sessions.UaOperationGuard.AuthorizeWritesAsync(info,
                        new ArrayOf<WriteValue>(variables.ToList().Select(variable => new WriteValue
                        {
                            NodeId = variable.PublishedVariable,
                            AttributeId = Attributes.Value
                        }).ToArray()), token).ConfigureAwait(false);
                }
                McpRuntimeUse<McpPubSubRuntime> use = await m_runtimes.AcquireAsync(handle, token)
                    .ConfigureAwait(false);
                await using (use.ConfigureAwait(false))
                {
                    if (input.Variables.Count != use.Runtime.Input.Fields.Count ||
                        !input.Variables.ToList().Select(value => value.Name).SequenceEqual(
                            use.Runtime.Input.Fields.ToList().Select(value => value.Name), StringComparer.Ordinal))
                    {
                        throw new ArgumentException("Adapter mappings must match the declared field names and order.");
                    }
                    var borrowed = new McpPubSubBorrowedSession(m_sessions, info, operation,
                        new ArrayOf<NodeId>(variables.ToList().Select(value => value.PublishedVariable).ToArray()),
                        m_options, m_time);
                    bool transferred = false;
                    try
                    {
                        IPublishedDataSetSource? source = null;
                        ISubscribedDataSetSink? sink = null;
                        DataSetMetaDataType metadata = use.Runtime.DeclaredMetadata();
                        if (operation == "read")
                        {
                            var configuration = new PublishedDataSetDataType
                            {
                                Name = use.Runtime.Input.DataSetName,
                                DataSetMetaData = metadata,
                                DataSetSource = new ExtensionObject(
                                    new PublishedDataItemsDataType { PublishedData = variables })
                            };
                            source = new ServerPublishedDataSetSource(configuration,
                                new CyclicReadStrategy(borrowed, m_sessions.Telemetry),
                                new DeclaredMetadata(metadata), m_sessions.Telemetry, m_time);
                        }
                        else
                        {
                            var targets = new FieldTargetDataType[variables.Count];
                            for (int index = 0; index < targets.Length; index++)
                            {
                                targets[index] = new FieldTargetDataType
                                {
                                    DataSetFieldId = metadata.Fields[index].DataSetFieldId,
                                    TargetNodeId = variables[index].PublishedVariable,
                                    AttributeId = Attributes.Value
                                };
                            }
                            sink = ServerSubscribedDataSetSink.Create(
                                new TargetVariablesDataType
                                {
                                    TargetVariables = new ArrayOf<FieldTargetDataType>(targets)
                                },
                                borrowed, m_sessions.Telemetry);
                        }
                        await use.Runtime.BindAsync(source, sink, borrowed, use.CancellationToken)
                            .ConfigureAwait(false);
                        transferred = true;
                        return new JsonObject
                        {
                            ["bound"] = true,
                            ["started"] = false,
                            ["operation"] = operation,
                            ["sessionName"] = info.Name,
                            ["fields"] = variables.Count,
                            ["metadataMode"] = "explicit immutable schema",
                            ["ownsSession"] = false,
                            ["writeAtomicity"] = "remote per-item service semantics; inspect lastWriteResults"
                        };
                    }
                    finally
                    {
                        if (!transferred)
                        {
                            await borrowed.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
            }, ct);
        }

        /// <summary>
        /// Captures the actual session name under an explicit bridge grant.
        /// </summary>
        private OpcUaSessionManager.SessionInfo Session(string? sessionName)
        {
            return McpPubSubRemote.Session(m_sessions, sessionName, McpCapability.PubSubBridge);
        }

        /// <summary>
        /// Checks host-authored endpoint, node and method identity independently of the caller's session grant.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void RequireRule(
            OpcUaSessionManager.SessionInfo info, string operation, NodeId node, NodeId method = default)
        {
            if (!m_options.AdapterRules.ToList().Any(rule => rule.SessionName == info.Name &&
                rule.Endpoint == info.Endpoint.EndpointUrl &&
                rule.Operation == operation &&
                McpPubSubRemote.Node(info.Session, rule.NodeId) == node &&
                (operation != "call" ||
                    (rule.MethodId is not null &&
                        McpPubSubRemote.Node(info.Session, rule.MethodId) == method))))
            {
                throw McpPubSubTransportProvider.Refused();
            }
        }

        private readonly McpPubSubRuntimeManager m_runtimes;
        private readonly OpcUaSessionManager m_sessions;
        private readonly McpPubSubOptions m_options;
        private readonly TimeProvider m_time;

        /// <summary>
        /// Supplies the immutable declared schema to the existing adapter without implicit model subscriptions.
        /// </summary>
        private sealed class DeclaredMetadata(DataSetMetaDataType metadata) : IDataSetMetaDataBuilder
        {
            /// <inheritdoc/>
            public event EventHandler? MetaDataChanged
            {
                add
                {
                }
                remove
                {
                }
            }

            /// <inheritdoc/>
            public DataSetMetaDataType BuildMetaData()
            {
                return metadata;
            }

            /// <inheritdoc/>
            public ValueTask<DataSetMetaDataType> ResolveAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(metadata);
            }

            /// <inheritdoc/>
            public ValueTask<bool> RefreshAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(false);
            }
        }
    }
}
