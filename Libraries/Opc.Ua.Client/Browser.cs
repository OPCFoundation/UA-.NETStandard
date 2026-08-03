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
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Address space browsing functionality.
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
            m_telemetry = telemetry;
            m_logger = m_telemetry.CreateLogger<Browser>();
            State = options ?? new BrowserOptions();
        }

        /// <summary>
        /// Creates new instance of a browser and attaches it to a session.
        /// </summary>
        public Browser(
            ISessionClient session,
            BrowserOptions? options = null)
        {
            m_telemetry = session.MessageContext.Telemetry;
            m_logger = m_telemetry.CreateLogger<Browser>();
            State = options ?? new BrowserOptions();
            Session = session;
        }

        /// <summary>
        /// Creates an unattached instance of a browser.
        /// </summary>
        [Obsolete("Use constructor with ISessionClient or ITelemetryContext.")]
        public Browser(BrowserOptions? options = null)
        {
            m_logger = m_telemetry.CreateLogger<Browser>();
            State = options ?? new BrowserOptions();
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
            m_logger = template.m_logger;
            m_telemetry = template.m_telemetry;
            State = template.State;
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
        public ISessionClient? Session
        {
            get => m_session;
            set
            {
                if (value is ISession session)
                {
                    MaxNodesPerBrowse =
                        session.OperationLimits.MaxNodesPerBrowse;
                    MaxBrowseContinuationPoints =
                        session.ServerCapabilities.MaxBrowseContinuationPoints;
                    ContinuationPointPolicy =
                        session.ContinuationPointPolicy;
                }
                m_session = value;
            }
        }

        /// <summary>
        /// The view to use for the browse operation.
        /// </summary>
        public RequestHeader? RequestHeader
        {
            get => State.RequestHeader;
            set => State = State with { RequestHeader = value };
        }

        /// <summary>
        /// The continuation point strategy to use
        /// </summary>
        public ContinuationPointPolicy ContinuationPointPolicy
        {
            get => State.ContinuationPointPolicy;
            set => State = State with { ContinuationPointPolicy = value };
        }

        /// <summary>
        /// Max nodes to browse in a single operation
        /// </summary>
        public uint MaxNodesPerBrowse
        {
            get => State.MaxNodesPerBrowse;
            set => State = State with { MaxNodesPerBrowse = value };
        }

        /// <summary>
        /// Max continuation point limit to use
        /// </summary>
        public ushort MaxBrowseContinuationPoints
        {
            get => State.MaxBrowseContinuationPoints;
            set => State = State with { MaxBrowseContinuationPoints = value };
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
            ISessionClient session = Session ??
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
                state.RequestHeader,
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
            byte[]? continuationPoint = results[0].ContinuationPoint;
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
        /// 1. if, during a Browse or BrowseNext, service call one of the status codes
        /// BadNoContinuationPoint or BadContinuationPointInvalid is returned,
        /// the node is browsed again.
        /// 2. tries to avoid the status code BadNoContinuationPoint by creating
        /// packages of size at most MaxNodesPerBrowse, taken from the Operationlimits
        /// retrieved from the server at client startup.
        /// 3. with the help of a new property of the session the new method calls
        /// Browse for at most
        /// min(MaxNodesPerBrowse, MaxBrowseContinuationPoints)
        /// nodes in one call, to further reduce the risk of the status code
        /// BadNoContinuationPoint
        /// 4. calls BrowseNext, if necessary, before working on a new package. This is
        /// the reason that the packages have to be created directly in this method
        /// and package creation cannot be delegated to the SessionClientBatched class.
        /// This call sequence avoids the cannibalization of continuation points from
        /// previously worked on packages, at least if no concurrent browse operation
        /// is started. (The server is supposed to manage the continuation point quota
        /// on session level.The reference server, which is used in the tests, does
        /// this correctly).
        /// 5. the maximum number of browse continuation points is retrieved from
        /// the server capabilities in a new method, which may also be viewed as
        /// a prototype for evaluation of all server capabilities (this is not an
        /// operation limit).
        /// </summary>
        /// <param name="nodesToBrowse">The nodes to browse</param>
        /// <param name="ct">Cancellation token to use</param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ResultSet<ReferenceDescriptionCollection>> BrowseAsync(
            IReadOnlyList<NodeId> nodesToBrowse,
            CancellationToken ct = default)
        {
            BrowserOptions state = State;
            ISessionClient session = Session ??
                throw new ServiceResultException(
                    StatusCodes.BadServerNotConnected,
                    "Cannot browse if not connected to a server.");

            int count = nodesToBrowse.Count;
            var result = new List<ReferenceDescriptionCollection>(count);
            var errors = new List<ServiceResult>(count);

            // first attempt for implementation: create the references for the output in advance.
            // optimize later, when everything works fine.
            for (int i = 0; i < nodesToBrowse.Count; i++)
            {
                result.Add([]);
                errors.Add(new ServiceResult(StatusCodes.Good));
            }
            // in the first pass, we browse all nodes from the input.
            // Some nodes may need to be browsed again, these are then fed into the next pass.
            var nodesToBrowseForPass = new List<NodeId>(count);
            nodesToBrowseForPass.AddRange(nodesToBrowse);

            var resultForPass = new List<ReferenceDescriptionCollection>(count);
            resultForPass.AddRange(result);

            var errorsForPass = new List<ServiceResult>(count);
            errorsForPass.AddRange(errors);

            int passCount = 0;

            do
            {
                int badNoCPErrorsPerPass = 0;
                int badCPInvalidErrorsPerPass = 0;
                int otherErrorsPerPass = 0;
                uint maxNodesPerBrowse = MaxNodesPerBrowse;

                if (ContinuationPointPolicy == ContinuationPointPolicy.Balanced &&
                    MaxBrowseContinuationPoints > 0)
                {
                    maxNodesPerBrowse =
                        MaxBrowseContinuationPoints < maxNodesPerBrowse
                            ? MaxBrowseContinuationPoints
                            : maxNodesPerBrowse;
                }

                // split input into batches
                int batchOffset = 0;

                var nodesToBrowseForNextPass = new List<NodeId>();
                var referenceDescriptionsForNextPass
                    = new List<ReferenceDescriptionCollection>();
                var errorsForNextPass = new List<ServiceResult>();

                // loop over the batches
                foreach (List<NodeId> nodesToBrowseBatch in nodesToBrowseForPass
                    .Batch<NodeId, List<NodeId>>(maxNodesPerBrowse))
                {
                    int nodesToBrowseBatchCount = nodesToBrowseBatch.Count;

                    ResultSet<ReferenceDescriptionCollection> results = await BrowseAsync(
                        session,
                        state.RequestHeader,
                        state.View,
                        nodesToBrowseBatch,
                        state.MaxReferencesReturned,
                        state.BrowseDirection,
                        state.ReferenceTypeId,
                        state.IncludeSubtypes,
                        state.NodeClassMask,
                        ct)
                    .ConfigureAwait(false);

                    int resultOffset = batchOffset;
                    for (int ii = 0; ii < nodesToBrowseBatchCount; ii++)
                    {
                        StatusCode statusCode = results.Errors[ii].StatusCode;
                        if (StatusCode.IsBad(statusCode))
                        {
                            bool addToNextPass = false;
                            if (statusCode == StatusCodes.BadNoContinuationPoints)
                            {
                                addToNextPass = true;
                                badNoCPErrorsPerPass++;
                            }
                            else if (statusCode == StatusCodes.BadContinuationPointInvalid)
                            {
                                addToNextPass = true;
                                badCPInvalidErrorsPerPass++;
                            }
                            else
                            {
                                otherErrorsPerPass++;
                            }

                            if (addToNextPass)
                            {
                                nodesToBrowseForNextPass.Add(
                                    nodesToBrowseForPass[resultOffset]);
                                referenceDescriptionsForNextPass.Add(
                                    resultForPass[resultOffset]);
                                errorsForNextPass.Add(errorsForPass[resultOffset]);
                            }
                        }

                        resultForPass[resultOffset].Clear();
                        resultForPass[resultOffset].AddRange(results.Results[ii]);
                        errorsForPass[resultOffset] = results.Errors[ii];
                        errors[resultOffset] = results.Errors[ii];
                        resultOffset++;
                    }

                    batchOffset += nodesToBrowseBatchCount;
                }

                resultForPass = referenceDescriptionsForNextPass;
                errorsForPass = errorsForNextPass;
                nodesToBrowseForPass = nodesToBrowseForNextPass;

                if (badCPInvalidErrorsPerPass > 0)
                {
                    m_logger.LogDebug(
                        "ManagedBrowse: in pass {Pass}, {Count} error(s) occured with a status code {StatusCode}.",
                        passCount,
                        badCPInvalidErrorsPerPass,
                        nameof(StatusCodes.BadContinuationPointInvalid));
                }
                if (badNoCPErrorsPerPass > 0)
                {
                    m_logger.LogDebug(
                        "ManagedBrowse: in pass {Pass}, {Count} error(s) occured with a status code {StatusCode}.",
                        passCount,
                        badNoCPErrorsPerPass,
                        nameof(StatusCodes.BadNoContinuationPoints));
                }
                if (otherErrorsPerPass > 0)
                {
                    m_logger.LogDebug(
                        "ManagedBrowse: in pass {Pass}, {Count} error(s) occured with a status code {StatusCode}.",
                        passCount,
                        otherErrorsPerPass,
                        $"different from {nameof(StatusCodes.BadNoContinuationPoints)} or {nameof(StatusCodes.BadContinuationPointInvalid)}");
                }
                if (otherErrorsPerPass == 0 &&
                    badCPInvalidErrorsPerPass == 0 &&
                    badNoCPErrorsPerPass == 0)
                {
                    m_logger.LogTrace("ManagedBrowse completed with no errors.");
                }

                passCount++;
            } while (nodesToBrowseForPass.Count > 0);
            return new ResultSet<ReferenceDescriptionCollection>(result, errors);
        }

        /// <summary>
        /// Call the browse service asynchronously and call browse next,
        /// if applicable, immediately afterwards. Observe proper treatment
        /// of specific service results, specifically
        /// BadNoContinuationPoint and BadContinuationPointInvalid
        /// </summary>
        private static async ValueTask<ResultSet<ReferenceDescriptionCollection>> BrowseAsync(
            ISessionClient session,
            RequestHeader? requestHeader,
            ViewDescription? view,
            List<NodeId> nodeIds,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            int nodeClassMask,
            CancellationToken ct = default)
        {
            requestHeader?.RequestHandle = 0;

            var result = new List<ReferenceDescriptionCollection>(nodeIds.Count);
            (
                _,
                ByteStringCollection continuationPoints,
                IList<ReferenceDescriptionCollection> referenceDescriptions,
                IList<ServiceResult> errors
            ) = await session.BrowseAsync(
                    requestHeader,
                    view,
                    nodeIds,
                    maxResultsToReturn,
                    browseDirection,
                    referenceTypeId,
                    includeSubtypes,
                    (uint)nodeClassMask,
                    ct)
                .ConfigureAwait(false);

            result.AddRange(referenceDescriptions);

            // process any continuation point.
            List<ReferenceDescriptionCollection> previousResults = result;
            var errorAnchors = new List<ReferenceWrapper<ServiceResult>>();
            var previousErrors = new List<ReferenceWrapper<ServiceResult>>();
            foreach (ServiceResult error in errors)
            {
                previousErrors.Add(new ReferenceWrapper<ServiceResult> { Reference = error });
                errorAnchors.Add(previousErrors[^1]);
            }

            var nextContinuationPoints = new ByteStringCollection();
            var nextResults = new List<ReferenceDescriptionCollection>();
            var nextErrors = new List<ReferenceWrapper<ServiceResult>>();

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                if (continuationPoints[ii] != null &&
                    !StatusCode.IsBad(previousErrors[ii].Reference.StatusCode))
                {
                    nextContinuationPoints.Add(continuationPoints[ii]);
                    nextResults.Add(previousResults[ii]);
                    nextErrors.Add(previousErrors[ii]);
                }
            }
            while (nextContinuationPoints.Count > 0)
            {
                requestHeader?.RequestHandle = 0;
                (
                    _,
                    ByteStringCollection revisedContinuationPoints,
                    IList<ReferenceDescriptionCollection> browseNextResults,
                    IList<ServiceResult> browseNextErrors
                ) = await session.BrowseNextAsync(
                    requestHeader,
                    nextContinuationPoints,
                    false,
                    ct).ConfigureAwait(false);

                for (int ii = 0; ii < browseNextResults.Count; ii++)
                {
                    nextResults[ii].AddRange(browseNextResults[ii]);
                    nextErrors[ii].Reference = browseNextErrors[ii];
                }

                previousResults = nextResults;
                previousErrors = nextErrors;

                nextResults = [];
                nextErrors = [];
                nextContinuationPoints = [];

                for (int ii = 0; ii < revisedContinuationPoints.Count; ii++)
                {
                    if (revisedContinuationPoints[ii] != null &&
                        !StatusCode.IsBad(browseNextErrors[ii].StatusCode))
                    {
                        nextContinuationPoints.Add(revisedContinuationPoints[ii]);
                        nextResults.Add(previousResults[ii]);
                        nextErrors.Add(previousErrors[ii]);
                    }
                }
            }
            var finalErrors = new List<ServiceResult>(errorAnchors.Count);
            foreach (ReferenceWrapper<ServiceResult> errorReference in errorAnchors)
            {
                finalErrors.Add(errorReference.Reference);
            }

            return new ResultSet<ReferenceDescriptionCollection>(result, finalErrors);
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
        private static async ValueTask<(ReferenceDescriptionCollection, byte[]?)> BrowseNextAsync(
            ISessionClient session,
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
            using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
            var serializer = new DataContractSerializer(typeof(BrowserOptions));
            serializer.WriteObject(stream, State);
        }

        /// <summary>
        /// Used to pass on references to the Service results in the loop in ManagedBrowseAsync.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private class ReferenceWrapper<T>
        {
            public required T Reference { get; set; }
        }

        private readonly ILogger m_logger;
        private readonly ITelemetryContext? m_telemetry;
        private ISessionClient? m_session;

        private event BrowserEventHandler? m_MoreReferences;
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
