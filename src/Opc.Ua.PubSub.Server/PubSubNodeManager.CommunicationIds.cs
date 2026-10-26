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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Server.Internal;
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server
{
    public sealed partial class PubSubNodeManager
    {
        /// <summary>
        /// Binds an append-style asynchronous implementation to the generated standard contract.
        /// </summary>
        internal static void BindAsyncConfigurationMethod(
            MethodState? method,
            GenericMethodCalledEventHandler2Async handler)
        {
            if (method is null)
            {
                return;
            }
            method.OnCallMethod2Async = async (context, calledMethod, objectId, inputs, outputs, ct) =>
            {
                outputs.Clear();
                try
                {
                    return await handler(context, calledMethod, objectId, inputs, outputs, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException ex)
                {
                    outputs.Clear();
                    return new ServiceResult(ex.StatusCode, new LocalizedText(ex.Message));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    outputs.Clear();
                    return new ServiceResult(StatusCodes.BadRequestCancelledByClient);
                }
            };
        }

        /// <summary>
        /// Creates an instance method using its generated standard declaration and an asynchronous implementation.
        /// </summary>
        private void AddAsyncConfigurationMethod(
            BaseObjectState parent,
            string browseName,
            MethodState method,
            GenericMethodCalledEventHandler2Async handler)
        {
            InitializeConfigurationMethod(parent, browseName, method);
            BindAsyncConfigurationMethod(method, handler);
            parent.AddChild(method);
        }

        /// <summary>
        /// Preserves namespace-zero standard BrowseNames while assigning unique instance and argument NodeIds.
        /// </summary>
        private void InitializeConfigurationMethod(BaseObjectState parent, string browseName, MethodState method)
        {
            string identifier = $"{parent.NodeId.IdentifierAsString}:{browseName}";
            method.Create(SystemContext, new NodeId(identifier, parent.NodeId.NamespaceIndex),
                new QualifiedName(browseName), new LocalizedText(browseName), true);
            method.Executable = true;
            method.UserExecutable = true;
            if (method.InputArguments is { } inputs)
            {
                inputs.NodeId = new NodeId($"{identifier}:InputArguments", parent.NodeId.NamespaceIndex);
            }
            if (method.OutputArguments is { } outputs)
            {
                outputs.NodeId = new NodeId($"{identifier}:OutputArguments", parent.NodeId.NamespaceIndex);
            }
        }

        /// <summary>
        /// Declares the existing vendor replacement operation without presenting it as a standard file import.
        /// </summary>
        private void AddVendorSetConfigurationMethod(BaseObjectState parent)
        {
            var method = new MethodState(parent)
            {
                NodeId = new NodeId($"{parent.NodeId.IdentifierAsString}:SetConfiguration",
                    parent.NodeId.NamespaceIndex),
                BrowseName = new QualifiedName("SetConfiguration", parent.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("SetConfiguration"),
                Executable = true,
                UserExecutable = true
            };
            method.InputArguments = CreateArguments(method, BrowseNames.InputArguments,
                new Argument
                {
                    Name = "Configuration",
                    DataType = DataTypeIds.PubSubConfigurationDataType,
                    ValueRank = ValueRanks.Scalar
                });
            method.OutputArguments = CreateArguments(method, BrowseNames.OutputArguments,
                new Argument
                {
                    Name = "Results",
                    DataType = DataTypeIds.StatusCode,
                    ValueRank = ValueRanks.OneDimension
                });
            BindAsyncConfigurationMethod(method, OnConfigurationMutationAsync);
            parent.AddChild(method);
        }

        /// <summary>
        /// Creates argument metadata for vendor methods that have no standard declaration.
        /// </summary>
        private static PropertyState<ArrayOf<Argument>> CreateArguments(
            MethodState method, string name, Argument argument)
        {
            PropertyState<ArrayOf<Argument>> property =
                PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
            property.NodeId = new NodeId($"{method.NodeId.IdentifierAsString}:{name}", method.NodeId.NamespaceIndex);
            property.BrowseName = new QualifiedName(name);
            property.DisplayName = new LocalizedText(name);
            property.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            property.TypeDefinitionId = VariableTypeIds.PropertyType;
            property.DataType = DataTypeIds.Argument;
            property.ValueRank = ValueRanks.OneDimension;
            property.AccessLevel = AccessLevels.CurrentRead;
            property.UserAccessLevel = AccessLevels.CurrentRead;
            property.Value = [argument];
            return property;
        }

        /// <summary>
        /// Requires an actual Session with the ConfigureAdmin role for reservation and consumption methods.
        /// </summary>
        private NodeId GetConfigurationSession(ISystemContext context)
        {
            return RequireConfigurationSession(context, m_options.ExposeConfigurationMethods);
        }

        /// <summary>
        /// Authorizes configuration against the actual Session rather than a manager URI or data-peer identity.
        /// </summary>
        internal static NodeId RequireConfigurationSession(ISystemContext context, bool enabled)
        {
            if (!enabled ||
                context is not ISessionSystemContext session ||
                session.SessionId is not NodeId id || id.IsNull ||
                session.UserIdentity is not { } identity ||
                !identity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_ConfigureAdmin))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return id;
        }

        /// <summary>
        /// Implements the standard typed, transport-scoped ReserveIds operation.
        /// </summary>
        private async ValueTask<ServiceResult> OnReservePubSubConfigurationIdsAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            NodeId sessionId = GetConfigurationSession(context);
            if (m_application is not IPubSubCommunicationIdApplication application)
            {
                return new ServiceResult(StatusCodes.BadNotSupported);
            }
            if (inputArguments.Count != 3 ||
                !inputArguments[0].TryGetValue(out string? scope) || string.IsNullOrEmpty(scope) ||
                !inputArguments[1].TryGetValue(out ushort groupCount) ||
                !inputArguments[2].TryGetValue(out ushort writerCount))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            PubSubCommunicationIdReservation result = await application.ReserveCommunicationIdsAsync(
                sessionId, scope, groupCount, writerCount, cancellationToken).ConfigureAwait(false);
            outputArguments.Add(result.DefaultPublisherId);
            outputArguments.Add(Variant.From(result.WriterGroupIds));
            outputArguments.Add(Variant.From(result.DataSetWriterIds));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Awaits consuming mutations under the actual calling Session identity.
        /// </summary>
        private async ValueTask<ServiceResult> OnConfigurationMutationAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            NodeId sessionId = GetConfigurationSession(context);
            if (inputArguments.Count != 1 ||
                !inputArguments[0].TryGetValue(out ExtensionObject ext))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            try
            {
                return await PubSubConfigurationSession.ExecuteAsync(sessionId, async ct =>
                {
                    NodeId result;
                    switch (method.BrowseName.Name)
                    {
                        case BrowseNames.AddConnection:
                            if (!ext.TryGetValue(out PubSubConnectionDataType? connection) || connection is null)
                            {
                                return new ServiceResult(StatusCodes.BadInvalidArgument);
                            }
                            result = await m_application.AddConnectionAsync(connection, ct).ConfigureAwait(false);
                            break;
                        case BrowseNames.AddWriterGroup:
                            if (!ext.TryGetValue(out WriterGroupDataType? group) || group is null)
                            {
                                return new ServiceResult(StatusCodes.BadInvalidArgument);
                            }
                            result = await m_application.AddWriterGroupAsync(objectId, group, ct).ConfigureAwait(false);
                            break;
                        case BrowseNames.AddReaderGroup:
                            if (!ext.TryGetValue(out ReaderGroupDataType? readers) || readers is null)
                            {
                                return new ServiceResult(StatusCodes.BadInvalidArgument);
                            }
                            result = await m_application.AddReaderGroupAsync(objectId, readers, ct)
                                .ConfigureAwait(false);
                            break;
                        case BrowseNames.AddDataSetWriter:
                            if (!ext.TryGetValue(out DataSetWriterDataType? writer) || writer is null)
                            {
                                return new ServiceResult(StatusCodes.BadInvalidArgument);
                            }
                            result = await m_application.AddDataSetWriterAsync(objectId, writer, ct)
                                .ConfigureAwait(false);
                            break;
                        case BrowseNames.AddDataSetReader:
                            if (!ext.TryGetValue(out DataSetReaderDataType? reader) || reader is null)
                            {
                                return new ServiceResult(StatusCodes.BadInvalidArgument);
                            }
                            result = await m_application.AddDataSetReaderAsync(objectId, reader, ct)
                                .ConfigureAwait(false);
                            break;
                        case "SetConfiguration":
                            if (!ext.TryGetValue(out PubSubConfigurationDataType? configuration) ||
                                configuration is null)
                            {
                                return new ServiceResult(StatusCodes.BadInvalidArgument);
                            }
                            ArrayOf<StatusCode> statuses =
                                await m_application.ReplaceConfigurationAsync(configuration, ct).ConfigureAwait(false);
                            outputArguments.Add(Variant.From(statuses));
                            return ServiceResult.Good;
                        default:
                            return new ServiceResult(StatusCodes.BadNotSupported);
                    }
                    outputArguments.Add(Variant.From(result));
                    return ServiceResult.Good;
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (PubSubConfigurationException ex)
            {
                return new ServiceResult(StatusCodes.BadConfigurationError, new LocalizedText(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown, new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Removes a child through the same Session-aware transaction used by additions.
        /// </summary>
        private async ValueTask<ServiceResult> OnConfigurationRemovalAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            NodeId sessionId = GetConfigurationSession(context);
            if (inputArguments.Count != 1 ||
                !inputArguments[0].TryGetValue(out NodeId childId) || childId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            BaseObjectState? child = FindPredefinedNode<BaseObjectState>(childId);
            if (child?.Parent is not BaseObjectState parent || parent.NodeId != objectId)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }
            try
            {
                return await PubSubConfigurationSession.ExecuteAsync(sessionId, async ct =>
                {
                    switch (method.BrowseName.Name)
                    {
                        case BrowseNames.RemoveGroup:
                            await m_application.RemoveGroupAsync(childId, ct).ConfigureAwait(false);
                            break;
                        case BrowseNames.RemoveDataSetWriter:
                            await m_application.RemoveDataSetWriterAsync(childId, ct).ConfigureAwait(false);
                            break;
                        case BrowseNames.RemoveDataSetReader:
                            await m_application.RemoveDataSetReaderAsync(childId, ct).ConfigureAwait(false);
                            break;
                        default:
                            return new ServiceResult(StatusCodes.BadNotSupported);
                    }
                    return ServiceResult.Good;
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown, new LocalizedText(ex.Message));
            }
            catch (PubSubConfigurationException ex)
            {
                return new ServiceResult(StatusCodes.BadConfigurationError, new LocalizedText(ex.Message));
            }
        }

        /// <summary>
        /// Imports only explicitly referenced elements from the Session-owned binary file.
        /// </summary>
        private async ValueTask<ServiceResult> OnCloseAndUpdatePubSubConfigurationFileAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            NodeId sessionId = GetConfigurationSession(context);
            cancellationToken.ThrowIfCancellationRequested();
            if (inputArguments.Count != 3 || !inputArguments[0].TryGetValue(out uint handle) ||
                !inputArguments[1].TryGetValue(out bool requireCompleteUpdate) ||
                !inputArguments[2].TryGetValue(out ArrayOf<ExtensionObject> references))
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
            byte[] content;
            lock (m_addressSpaceGate)
            {
                ServiceResult access = GetConfigurationFile(context, handle, out PubSubConfigurationFileHandle? file);
                if (ServiceResult.IsBad(access))
                {
                    return access;
                }
                if (!file!.Writable)
                {
                    return new ServiceResult(StatusCodes.BadInvalidState);
                }
                content = file.ToArray();
                _ = m_fileHandles.Remove(handle);
            }
            if (references.IsEmpty)
            {
                return new ServiceResult(StatusCodes.BadNothingToDo);
            }
            PubSubConfiguration2DataType configuration = DecodeBinaryConfiguration(content);
            var typedReferences = new PubSubConfigurationRefDataType[references.Count];
            for (int i = 0; i < references.Count; i++)
            {
                if (!references[i].TryGetValue(out PubSubConfigurationRefDataType? reference) ||
                    reference is null)
                {
                    return new ServiceResult(StatusCodes.BadTypeMismatch);
                }
                typedReferences[i] = reference;
            }
            if (m_application is not IPubSubConfigurationImportApplication importApplication)
            {
                return new ServiceResult(StatusCodes.BadNotSupported);
            }
            bool allowSecurity = context is ISessionSystemContext session &&
                session.UserIdentity?.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin) == true;
            PubSubConfigurationImportResult result = await PubSubConfigurationSession.ExecuteAsync(
                sessionId,
                ct => importApplication.ImportConfigurationAsync(
                    configuration, typedReferences, requireCompleteUpdate, allowSecurity, ct),
                cancellationToken).ConfigureAwait(false);
            outputArguments.Add(Variant.From(result.ChangesApplied));
            outputArguments.Add(Variant.From(result.ReferenceResults));
            outputArguments.Add(Variant.From(new ArrayOf<ExtensionObject>(
                result.ConfigurationValues.ToList().Select(value => new ExtensionObject(value)).ToArray())));
            outputArguments.Add(Variant.From(result.ConfigurationObjects));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Invalidates the actual Session before any in-flight reservation can publish its result.
        /// </summary>
        private void OnReservationSessionClosing(ISession session, SessionEventReason reason)
        {
            if (m_application is IPubSubCommunicationIdApplication application)
            {
                application.CommunicationIds.CloseSession(session.Id);
            }
            lock (m_addressSpaceGate)
            {
                m_closedConfigurationFileSessions.Add(session.Id);
                foreach (uint handle in m_fileHandles
                    .Where(entry => entry.Value.Owner == session.Id)
                    .Select(entry => entry.Key).ToArray())
                {
                    m_fileHandles.Remove(handle);
                }
            }
        }

        /// <summary>Session manager whose close events release unused reservations.</summary>
        private readonly ISessionManager? m_sessionManager;
    }
}
