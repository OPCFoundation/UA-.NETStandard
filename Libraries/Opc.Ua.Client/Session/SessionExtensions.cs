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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Extensions to ISession that are not dependent on anything internal
    /// to the Session but layer over ISession
    /// </summary>
    public static class SessionExtensions
    {
        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="identity">The user identity.</param>
        /// <param name="ct">The cancellation token.</param>
        public static Task OpenAsync(
            this ISession session,
            string sessionName,
            IUserIdentity identity,
            CancellationToken ct = default)
        {
            return session.OpenAsync(sessionName, 0, identity, null, ct);
        }

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The session timeout.</param>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The list of preferred locales.</param>
        /// <param name="ct">The cancellation token.</param>
        public static Task OpenAsync(
            this ISession session,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string>? preferredLocales,
            CancellationToken ct = default)
        {
            return session.OpenAsync(
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                true,
                ct);
        }

        /// <summary>
        /// Establishes a session with the server.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="sessionName">The name to assign to the session.</param>
        /// <param name="sessionTimeout">The session timeout.</param>
        /// <param name="identity">The user identity.</param>
        /// <param name="preferredLocales">The list of preferred locales.</param>
        /// <param name="checkDomain">If set to <c>true</c> then the
        /// domain in the certificate must match the endpoint used.</param>
        /// <param name="ct">The cancellation token.</param>
        public static Task OpenAsync(
            this ISession session,
            string sessionName,
            uint sessionTimeout,
            IUserIdentity identity,
            IList<string>? preferredLocales,
            bool checkDomain,
            CancellationToken ct = default)
        {
            return session.OpenAsync(
                sessionName,
                sessionTimeout,
                identity,
                preferredLocales,
                checkDomain,
                true,
                ct);
        }

        /// <summary>
        /// Reconnects to the server after a network failure.
        /// Uses the current channel if possible or creates
        /// a new one.
        /// </summary>
        public static Task ReconnectAsync(
            this ISession session,
            CancellationToken ct = default)
        {
            return session.ReconnectAsync(null, null, ct);
        }

        /// <summary>
        /// Reconnects to the server on a waiting connection
        /// </summary>
        public static Task ReconnectAsync(
            this ISession session,
            ITransportWaitingConnection connection,
            CancellationToken ct = default)
        {
            return session.ReconnectAsync(connection, null, ct);
        }

        /// <summary>
        /// Reconnects to the server after a network failure
        /// using a new channel.
        /// </summary>
        public static Task ReconnectAsync(
            this ISession session,
            ITransportChannel channel,
            CancellationToken ct = default)
        {
            return session.ReconnectAsync(null, channel, ct);
        }

        /// <summary>
        /// Saves all the subscriptions of the session.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="filePath">The file path.</param>
        /// <param name="knownTypes">Known types</param>
        public static void Save(
            this ISession session,
            string filePath,
            IEnumerable<Type>? knownTypes = null)
        {
            session.Save(filePath, session.Subscriptions, knownTypes);
        }

        /// <summary>
        /// Load the list of subscriptions saved in a file.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="filePath">The file path.</param>
        /// <param name="transferSubscriptions">Load the subscriptions for transfer
        /// after load.</param>
        /// <param name="knownTypes">Additional known types that may be needed to
        /// read the saved subscriptions.</param>
        /// <returns>The list of loaded subscriptions</returns>
        public static IEnumerable<Subscription> Load(
            this ISession session,
            string filePath,
            bool transferSubscriptions = false,
            IEnumerable<Type>? knownTypes = null)
        {
            using FileStream stream = File.OpenRead(filePath);
            return session.Load(stream, transferSubscriptions, knownTypes);
        }

        /// <summary>
        /// Saves a set of subscriptions to a file.
        /// </summary>
        public static void Save(
            this ISession session,
            string filePath,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type>? knownTypes = null)
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            session.Save(stream, subscriptions, knownTypes);
        }

        /// <summary>
        /// Close the session with the server and optionally closes the channel.
        /// </summary>
        public static Task<StatusCode> CloseAsync(
            this ISession session,
            bool closeChannel,
            CancellationToken ct = default)
        {
            return session.CloseAsync(session.KeepAliveInterval, closeChannel, ct);
        }

        /// <summary>
        /// Disconnects from the server and frees any network resources (closes
        /// the channel) with the specified timeout.
        /// </summary>
        public static Task<StatusCode> CloseAsync(
            this ISession session,
            int timeout,
            CancellationToken ct = default)
        {
            return session.CloseAsync(timeout, true, ct);
        }

        /// <summary>
        /// Reads a byte string which is too large for the (server side) encoder to handle.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="nodeId">The node id of a byte string variable</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<byte[]> ReadByteStringInChunksAsync(
            this ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            int maxByteStringLength = (int)session.ServerCapabilities.MaxByteStringLength;
            if (maxByteStringLength <= 1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIndexRangeNoData,
                    "The MaxByteStringLength is not known or too small for reading data in chunks.");
            }

            ReadOnlyMemory<byte> buffer = await session.ReadBytesAsync(
                nodeId,
                maxByteStringLength,
                ct).ConfigureAwait(false);

            return buffer.ToArray();
        }

        /// <summary>
        /// Finds the NodeIds for the components for an instance.
        /// </summary>
        public static async Task<(NodeIdCollection, IList<ServiceResult>)> FindComponentIdsAsync(
            this ISession session,
            NodeId instanceId,
            IList<string> componentPaths,
            CancellationToken ct = default)
        {
            var componentIds = new NodeIdCollection();
            var errors = new List<ServiceResult>();

            // build list of paths to translate.
            var pathsToTranslate = new BrowsePathCollection();

            for (int ii = 0; ii < componentPaths.Count; ii++)
            {
                var pathToTranslate = new BrowsePath
                {
                    StartingNode = instanceId,
                    RelativePath = RelativePath.Parse(componentPaths[ii], session.TypeTree)
                };

                pathsToTranslate.Add(pathToTranslate);
            }

            // translate the paths.

            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                pathsToTranslate,
                ct).ConfigureAwait(false);

            BrowsePathResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
            ResponseHeader responseHeader = response.ResponseHeader;

            // verify that the server returned the correct number of results.
            ClientBase.ValidateResponse(results, pathsToTranslate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, pathsToTranslate);

            for (int ii = 0; ii < componentPaths.Count; ii++)
            {
                componentIds.Add(NodeId.Null);
                errors.Add(ServiceResult.Good);

                // process any diagnostics associated with any error.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    errors[ii] = new ServiceResult(
                        results[ii].StatusCode,
                        ii,
                        diagnosticInfos,
                        responseHeader.StringTable);
                    continue;
                }

                // Expecting exact one NodeId for a local node.
                // Report an error if the server returns anything other than that.

                if (results[ii].Targets.Count == 0)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTargetNodeIdInvalid,
                        "Could not find target for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets.Count != 1)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTooManyMatches,
                        "Too many matches found for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets[0].RemainingPathIndex != uint.MaxValue)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTargetNodeIdInvalid,
                        "Cannot follow path to external server: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets[0].TargetId.IsNull)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadUnexpectedError,
                        "Server returned a null NodeId for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets[0].TargetId.IsAbsolute)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadUnexpectedError,
                        "Server returned a remote node for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                // suitable target found.
                componentIds[ii] = ExpandedNodeId.ToNodeId(
                    results[ii].Targets[0].TargetId,
                    session.NamespaceUris);
            }
            return (componentIds, errors);
        }

        /// <summary>
        /// Returns the available encodings for a node
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="variableId">The variable node.</param>
        /// <param name="ct">Cancellation token to use to cancel the operation</param>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<ReferenceDescriptionCollection> ReadAvailableEncodingsAsync(
            this ISession session,
            NodeId variableId,
            CancellationToken ct = default)
        {
            if (await session.NodeCache.FindAsync(variableId, ct).ConfigureAwait(false)
                is not VariableNode variable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "NodeId does not refer to a valid variable node.");
            }

            // no encodings available if there was a problem reading the
            // data type for the node.
            if (variable.DataType.IsNull)
            {
                return [];
            }

            // no encodings for non-structures.
            if (!await session.NodeCache.IsTypeOfAsync(
                variable.DataType,
                DataTypes.Structure,
                ct).ConfigureAwait(false))
            {
                return [];
            }

            // look for cached values.
            IList<INode> encodings = await session.NodeCache.FindAsync(
                variableId,
                ReferenceTypeIds.HasEncoding,
                false,
                true,
                ct).ConfigureAwait(false);

            if (encodings.Count > 0)
            {
                var references = new ReferenceDescriptionCollection();

                foreach (INode encoding in encodings)
                {
                    var reference = new ReferenceDescription
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                        IsForward = true,
                        NodeId = encoding.NodeId,
                        NodeClass = encoding.NodeClass,
                        BrowseName = encoding.BrowseName,
                        DisplayName = encoding.DisplayName,
                        TypeDefinition = encoding.TypeDefinitionId
                    };

                    references.Add(reference);
                }

                return references;
            }

            var browser = new Browser(session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                IncludeSubtypes = false,
                NodeClassMask = 0
            });

            return await browser.BrowseAsync(variable.DataType, ct).ConfigureAwait(false);
        }
    }
}
