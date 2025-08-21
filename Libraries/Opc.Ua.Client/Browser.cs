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

using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Stores the options to use for a browse operation.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class Browser
    {
        /// <summary>
        /// Creates an unattached instance of a browser.
        /// </summary>
        public Browser()
        {
            Initialize();
        }

        /// <summary>
        /// Creates new instance of a browser and attaches it to a session.
        /// </summary>
        public Browser(ISession session)
        {
            Initialize();
            m_session = session;
        }

        /// <summary>
        /// Creates a copy of a browser.
        /// </summary>
        public Browser(Browser template)
        {
            Initialize();

            if (template != null)
            {
                m_session = template.m_session;
                m_view = template.m_view;
                m_maxReferencesReturned = template.m_maxReferencesReturned;
                m_browseDirection = template.m_browseDirection;
                m_referenceTypeId = template.m_referenceTypeId;
                m_includeSubtypes = template.m_includeSubtypes;
                m_nodeClassMask = template.m_nodeClassMask;
                m_resultMask = template.m_resultMask;
                m_continueUntilDone = template.m_continueUntilDone;
            }
        }

        /// <summary>
        /// Sets all private fields to default values.
        /// </summary>
        private void Initialize()
        {
            m_session = null;
            m_view = null;
            m_maxReferencesReturned = 0;
            m_browseDirection = BrowseDirection.Forward;
            m_referenceTypeId = null;
            m_includeSubtypes = true;
            m_nodeClassMask = 0;
            m_resultMask = (uint)BrowseResultMask.All;
            m_continueUntilDone = false;
            m_browseInProgress = false;
        }

        /// <summary>
        /// The session that the browse is attached to.
        /// </summary>
        public ISession Session
        {
            get => m_session;
            set
            {
                CheckBrowserState();
                m_session = value;
            }
        }

        /// <summary>
        /// The view to use for the browse operation.
        /// </summary>
        [DataMember(Order = 1)]
        public ViewDescription View
        {
            get => m_view;
            set
            {
                CheckBrowserState();
                m_view = value;
            }
        }

        /// <summary>
        /// The maximum number of references to return in a single browse operation.
        /// </summary>
        [DataMember(Order = 2)]
        public uint MaxReferencesReturned
        {
            get => m_maxReferencesReturned;
            set
            {
                CheckBrowserState();
                m_maxReferencesReturned = value;
            }
        }

        /// <summary>
        /// The direction to browse.
        /// </summary>
        [DataMember(Order = 3)]
        public BrowseDirection BrowseDirection
        {
            get => m_browseDirection;
            set
            {
                CheckBrowserState();
                m_browseDirection = value;
            }
        }

        /// <summary>
        /// The reference type to follow.
        /// </summary>
        [DataMember(Order = 4)]
        public NodeId ReferenceTypeId
        {
            get => m_referenceTypeId;
            set
            {
                CheckBrowserState();
                m_referenceTypeId = value;
            }
        }

        /// <summary>
        /// Whether subtypes of the reference type should be included.
        /// </summary>
        [DataMember(Order = 5)]
        public bool IncludeSubtypes
        {
            get => m_includeSubtypes;
            set
            {
                CheckBrowserState();
                m_includeSubtypes = value;
            }
        }

        /// <summary>
        /// The classes of the target nodes.
        /// </summary>
        [DataMember(Order = 6)]
        public int NodeClassMask
        {
            get => Utils.ToInt32(m_nodeClassMask);
            set
            {
                CheckBrowserState();
                m_nodeClassMask = Utils.ToUInt32(value);
            }
        }

        /// <summary>
        /// The results to return.
        /// </summary>
        [DataMember(Order = 6)]
        public uint ResultMask
        {
            get => m_resultMask;
            set
            {
                CheckBrowserState();
                m_resultMask = value;
            }
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
        public bool ContinueUntilDone
        {
            get => m_continueUntilDone;
            set
            {
                CheckBrowserState();
                m_continueUntilDone = value;
            }
        }

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
            if (m_session == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadServerNotConnected,
                    "Cannot browse if not connected to a server.");
            }

            try
            {
                m_browseInProgress = true;

                // construct request.
                var nodeToBrowse = new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = m_browseDirection,
                    ReferenceTypeId = m_referenceTypeId,
                    IncludeSubtypes = m_includeSubtypes,
                    NodeClassMask = m_nodeClassMask,
                    ResultMask = m_resultMask
                };

                var nodesToBrowse = new BrowseDescriptionCollection { nodeToBrowse };

                // make the call to the server.
                BrowseResponse browseResponse = await m_session.BrowseAsync(
                    null,
                    m_view,
                    m_maxReferencesReturned,
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

                        if (!m_continueUntilDone && m_MoreReferences != null)
                        {
                            var args = new BrowserEventArgs(references);
                            m_MoreReferences(this, args);

                            // cancel browser and return the references fetched so far.
                            if (args.Cancel)
                            {
                                (_, continuationPoint) = await BrowseNextAsync(
                                    continuationPoint,
                                    true,
                                    ct).ConfigureAwait(false);
                                return references;
                            }

                            m_continueUntilDone = args.ContinueUntilDone;
                        }

                        (additionalReferences, continuationPoint) = await BrowseNextAsync(
                            continuationPoint,
                            false,
                            ct).ConfigureAwait(false);
                        if (additionalReferences != null && additionalReferences.Count > 0)
                        {
                            references.AddRange(additionalReferences);
                        }
                        else
                        {
                            Utils.LogWarning(
                                "Browser: Continuation point exists, but the browse results are null/empty.");
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (continuationPoint?.Length > 0)
                {
                    (_, continuationPoint) = await BrowseNextAsync(
                        continuationPoint,
                        true,
                        default).ConfigureAwait(false);
                }
                // return the results.
                return references;
            }
            finally
            {
                m_browseInProgress = false;
            }
        }

        /// <summary>
        /// Checks the state of the browser.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void CheckBrowserState()
        {
            if (m_browseInProgress)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Cannot change browse parameters while a browse operation is in progress.");
            }
        }

        /// <summary>
        /// Fetches the next batch of references.
        /// </summary>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <param name="cancel">if set to <c>true</c> the browse operation is cancelled.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The next batch of references</returns>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<(ReferenceDescriptionCollection, byte[])> BrowseNextAsync(
            byte[] continuationPoint,
            bool cancel,
            CancellationToken ct = default)
        {
            var continuationPoints = new ByteStringCollection { continuationPoint };

            // make the call to the server.
            BrowseNextResponse browseResponse = await m_session.BrowseNextAsync(
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

        private ISession m_session;
        private ViewDescription m_view;
        private uint m_maxReferencesReturned;
        private BrowseDirection m_browseDirection;
        private NodeId m_referenceTypeId;
        private bool m_includeSubtypes;
        private uint m_nodeClassMask;
        private uint m_resultMask;
        private event BrowserEventHandler m_MoreReferences;
        private bool m_continueUntilDone;
        private bool m_browseInProgress;
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
