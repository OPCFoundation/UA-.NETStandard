/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Stores the options to use for a browse operation.
    /// </summary>
    public class Browser
    {
        /// <summary>
        /// Creates new instance of a browser and attaches it to a session.
        /// </summary>
        public Browser(
            ITelemetryContext telemetry,
            BrowserOptions? options = null)
        {
            State = options ?? new BrowserOptions();
            m_telemetry = telemetry;
            m_logger = m_telemetry.CreateLogger<Browser>();
        }

        /// <summary>
        /// Creates new instance of a browser and attaches it to a session.
        /// </summary>
        public Browser(
            ISession session,
            BrowserOptions? options = null)
        {
            State = options ?? new BrowserOptions();
            m_telemetry = session.MessageContext.Telemetry;
            m_logger = m_telemetry.CreateLogger<Browser>();
            Session = session;
        }

        /// <summary>
        /// Compatibility constructor accepting explicit telemetry context plus session.
        /// </summary>
        /// <remarks>
        /// Some existing code instantiates the browser with (session, telemetry). This overload preserves
        /// that pattern while delegating to the session-based constructor and then overriding the telemetry.
        /// </remarks>
        public Browser(ISession session, ITelemetryContext telemetry)
        {
            State = new BrowserOptions();
            Session = session;
            m_telemetry = telemetry ?? session.MessageContext.Telemetry;
            m_logger = m_telemetry.CreateLogger<Browser>();
        }

        /// <summary>
        /// Creates an unattached instance of a browser.
        /// </summary>
        [Obsolete("Use constructor with ISession or ITelemetryContext.")]
        public Browser(BrowserOptions? options = null)
        {
            State = options ?? new BrowserOptions();
            m_logger = m_telemetry.CreateLogger<Browser>();
        }

        /// <summary>
        /// Creates a copy of a browser.
        /// </summary>
        [Obsolete("Use the constructor that accepts BrowserOptions instead.")]
        public Browser(Browser template)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }
            State = template.State;
            m_logger = template.m_logger;
            m_telemetry = template.m_telemetry;
            Session = template.Session;
            ContinueUntilDone = template.ContinueUntilDone;
        }

        /// <summary>
        /// Browwser state
        /// </summary>
        public BrowserOptions State { get; private set; }

        /// <summary>
        /// The session that the browse is attached to.
        /// </summary>
        public ISession? Session { get; set; }

        /// <summary>
        /// Enables owners to set the telemetry context
        /// </summary>
        public ITelemetryContext? Telemetry
        {
            get => m_telemetry;
            set
            {
                m_telemetry = value;
                m_logger = value.CreateLogger(this);
            }
        }

        /// <summary>
        /// The view to use for the browse operation.
        /// </summary>
        public ViewDescription? View
        {
            get => State.View;
            set => State = State with { View = value };
        }

        /// <summary>
        /// The maximum number of references to return in a single browse operation.
        /// </summary>
        public uint MaxReferencesReturned
        {
            get => State.MaxReferencesReturned;
            set => State = State with { MaxReferencesReturned = value };
        }

        /// <summary>
        /// The direction to browse.
        /// </summary>
        public BrowseDirection BrowseDirection
        {
            get => State.BrowseDirection;
            set => State = State with { BrowseDirection = value };
        }

        /// <summary>
        /// The reference type to follow.
        /// </summary>
        public NodeId ReferenceTypeId
        {
            get => State.ReferenceTypeId;
            set => State = State with { ReferenceTypeId = value };
        }

        /// <summary>
        /// Whether subtypes of the reference type should be included.
        /// </summary>
        public bool IncludeSubtypes
        {
            get => State.IncludeSubtypes;
            set => State = State with { IncludeSubtypes = value };
        }

        /// <summary>
        /// The classes of the target nodes.
        /// </summary>
        public uint NodeClassMask
        {
            get => Utils.ToUInt32(State.NodeClassMask);
            set => State = State with { NodeClassMask = Utils.ToInt32(value) };
        }

        /// <summary>
        /// The results to return.
        /// </summary>
        public uint ResultMask
        {
            get => State.ResultMask;
            set => State = State with { ResultMask = value };
        }

        /// <summary>
        /// Raised when a browse operation halted because of a continuation point.
        /// </summary>
        public event BrowserEventHandler MoreReferences
        {
            add => m_MoreReferences += value;
            remove => m_MoreReferences -= value;
        }

        /// <summary>
        /// Whether subsequent continuation points should be processed automatically.
        /// </summary>
        public bool ContinueUntilDone { get; set; }

        /// <summary>
        /// Browses the specified node.
        /// </summary>
        [Obsolete("Use BrowseAsync(NodeId) instead.")]
        public ReferenceDescriptionCollection Browse(NodeId nodeId)
        {
            return BrowseAsync(nodeId).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Browses the specified node.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ReferenceDescriptionCollection> BrowseAsync(
            NodeId nodeId,
            CancellationToken ct = default)
        {
            BrowserOptions state = State;
            ISession session = Session ??
                throw new ServiceResultException(
                    StatusCodes.BadServerNotConnected,
                    "Cannot browse if not connected to a server.");

            // construct request.
            var nodeToBrowse = new BrowseDescription
            {
                NodeId = nodeId,
                BrowseDirection = state.BrowseDirection,
                ReferenceTypeId = state.ReferenceTypeId,
                IncludeSubtypes = state.IncludeSubtypes,
                NodeClassMask = Utils.ToUInt32(state.NodeClassMask),
                ResultMask = state.ResultMask
            };

            var nodesToBrowse = new BrowseDescriptionCollection { nodeToBrowse };

            // make the call to the server.
            BrowseResponse browseResponse = await session.BrowseAsync(
                null,
                state.View,
                state.MaxReferencesReturned,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            BrowseResultCollection results = browseResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = browseResponse.DiagnosticInfos;
            ResponseHeader responseHeader = browseResponse.ResponseHeader;

            // ensure that the server returned valid results.
            ClientBase.ValidateResponse(results, nodesToBrowse);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

            // check if valid.
            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(
                    results[0].StatusCode,
                    0,
                    diagnosticInfos,
                    responseHeader.StringTable);
            }

            // fetch initial set of references.
            byte[] continuationPoint = results[0].ContinuationPoint;
            ReferenceDescriptionCollection references = results[0].References;

            try
            {
                // process any continuation point.
                while (continuationPoint != null)
                {
                    ReferenceDescriptionCollection additionalReferences;

                    if (!ContinueUntilDone && m_MoreReferences != null)
                    {
                        var args = new BrowserEventArgs(references);
                        m_MoreReferences(this, args);

                        // cancel browser and return the references fetched so far.
                        if (args.Cancel)
                        {
                            session = Session;
                            if (session != null)
                            {
                                (_, continuationPoint) = await BrowseNextAsync(
                                    session,
                                    continuationPoint,
                                    true,
                                    ct).ConfigureAwait(false);
                            }
                            return references;
                        }

                        ContinueUntilDone = args.ContinueUntilDone;
                    }

                    // See if the session was updated
                    session = Session ??
                        throw new ServiceResultException(
                            StatusCodes.BadServerNotConnected,
                            "Cannot browse if not connected to a server.");
                    (additionalReferences, continuationPoint) = await BrowseNextAsync(
                        session,
                        continuationPoint,
                        false,
                        ct).ConfigureAwait(false);
                    if (additionalReferences != null && additionalReferences.Count > 0)
                    {
                        references.AddRange(additionalReferences);
                    }
                    else
                    {
                        m_logger.LogWarning(
                            "Browser: Continuation point exists, but the browse results are null/empty.");
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (continuationPoint?.Length > 0)
            {
                session = Session;
                if (session != null)
                {
                    (_, _) = await BrowseNextAsync(
                    session,
                    continuationPoint,
                    true,
                    default).ConfigureAwait(false);
                }
            }
            // return the results.
            return references;
        }

        /// <summary>
        /// Fetches the next batch of references.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <param name="cancel">if set to <c>true</c> the browse operation is cancelled.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The next batch of references</returns>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<(ReferenceDescriptionCollection, byte[])> BrowseNextAsync(
            ISession session,
            byte[] continuationPoint,
            bool cancel,
            CancellationToken ct = default)
        {
            var continuationPoints = new ByteStringCollection { continuationPoint };

            // make the call to the server.
            BrowseNextResponse browseResponse = await session.BrowseNextAsync(
                null,
                cancel,
                continuationPoints,
                ct)
                .ConfigureAwait(false);

            BrowseResultCollection results = browseResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = browseResponse.DiagnosticInfos;
            ResponseHeader responseHeader = browseResponse.ResponseHeader;

            // ensure that the server returned valid results.
            ClientBase.ValidateResponse(results, continuationPoints);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

            // check if valid.
            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(
                    results[0].StatusCode,
                    0,
                    diagnosticInfos,
                    responseHeader.StringTable);
            }

            // return references and continuation point if provided.
            return (results[0].References, results[0].ContinuationPoint);
        }

        /// <summary>
        /// Creates the browser from a persisted stream
        /// </summary>
        public static Browser? Load(Stream stream, ITelemetryContext telemetry)
        {
            // secure settings
            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            using var reader = XmlReader.Create(stream, settings);
            var serializer = new DataContractSerializer(typeof(BrowserOptions));
            using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
            var options = (BrowserOptions?)serializer.ReadObject(reader);
            return new Browser(telemetry, options);
        }

        /// <summary>
        /// Saves the state to the stream
        /// </summary>
        public void Save(Stream stream)
        {
            // secure settings
            using IDisposable scope = AmbientMessageContext.SetScopedContext(Telemetry);
            var serializer = new DataContractSerializer(typeof(BrowserOptions));
            serializer.WriteObject(stream, State);
        }

        private ILogger m_logger;
        private ITelemetryContext? m_telemetry;
        private event BrowserEventHandler? m_MoreReferences;
    }

    [JsonSerializable(typeof(BrowserOptions))]
    internal partial class BrowserOptionsContext : JsonSerializerContext;

    /// <summary>
    /// Stores the options to use for a browse operation. Can be serialized and
    /// deserialized.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public record class BrowserOptions
    {
        /// <summary>
        /// The view to use for the browse operation.
        /// </summary>
        [DataMember(Order = 1)]
        public ViewDescription? View { get; init; }

        /// <summary>
        /// The maximum number of references to return in a single browse operation.
        /// </summary>
        [DataMember(Order = 2)]
        public uint MaxReferencesReturned { get; init; }

        /// <summary>
        /// The direction to browse.
        /// </summary>
        [DataMember(Order = 3)]
        public BrowseDirection BrowseDirection { get; init; } = BrowseDirection.Forward;

        /// <summary>
        /// The reference type to follow.
        /// </summary>
        [DataMember(Order = 4)]
        public NodeId ReferenceTypeId { get; init; } = NodeId.Null;

        /// <summary>
        /// Whether subtypes of the reference type should be included.
        /// </summary>
        [DataMember(Order = 5)]
        public bool IncludeSubtypes { get; init; } = true;

        /// <summary>
        /// The classes of the target nodes.
        /// </summary>
        [DataMember(Order = 6)]
        public int NodeClassMask { get; init; }

        /// <summary>
        /// The results to return.
        /// </summary>
        [DataMember(Order = 7)]
        public uint ResultMask { get; init; } = (uint)BrowseResultMask.All;
    }

    /// <summary>
    /// The event arguments provided a browse operation returns a continuation point.
    /// </summary>
    public class BrowserEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal BrowserEventArgs(ReferenceDescriptionCollection references)
        {
            References = references;
        }

        /// <summary>
        /// Whether the browse operation should be cancelled.
        /// </summary>
        public bool Cancel { get; set; }

        /// <summary>
        /// Whether subsequent continuation points should be processed automatically.
        /// </summary>
        public bool ContinueUntilDone { get; set; }

        /// <summary>
        /// The references that have been fetched so far.
        /// </summary>
        public ReferenceDescriptionCollection References { get; }
    }

    /// <summary>
    /// A delegate used to received browser events.
    /// </summary>
    public delegate void BrowserEventHandler(Browser sender, BrowserEventArgs e);
}
