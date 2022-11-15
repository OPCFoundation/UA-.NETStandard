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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Stores the options to use for a browse operation.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class Browser
    {
        #region Constructors
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
            m_browseDirection = Opc.Ua.BrowseDirection.Forward;
            m_referenceTypeId = null;
            m_includeSubtypes = true;
            m_nodeClassMask = 0;
            m_resultMask = (uint)BrowseResultMask.All;
            m_continueUntilDone = false;
            m_browseInProgress = false;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The session that the browse is attached to.
        /// </summary>
        public ISession Session
        {
            get { return m_session; }

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
            get { return m_view; }

            set
            {
                CheckBrowserState();
                m_view = value;
            }
        }

        /// <summary>
        /// The maximum number of refrences to return in a single browse operation.
        /// </summary>
        [DataMember(Order = 2)]
        public uint MaxReferencesReturned
        {
            get { return m_maxReferencesReturned; }

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
            get { return m_browseDirection; }

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
            get { return m_referenceTypeId; }

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
            get { return m_includeSubtypes; }

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
            get { return Utils.ToInt32(m_nodeClassMask); }

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
            get { return m_resultMask; }

            set
            {
                CheckBrowserState();
                m_resultMask = value;
            }
        }

        /// <summary>
        /// Raised when a browse operation halted because of a continuation point.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event BrowserEventHandler MoreReferences
        {
            add { m_MoreReferences += value; }
            remove { m_MoreReferences -= value; }
        }

        /// <summary>
        /// Whether subsequent continuation points should be processed automatically.
        /// </summary>
        public bool ContinueUntilDone
        {
            get { return m_continueUntilDone; }

            set
            {
                CheckBrowserState();
                m_continueUntilDone = value;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Browses the specified node.
        /// </summary>
        public ReferenceDescriptionCollection Browse(NodeId nodeId)
        {
            if (m_session == null)
            {
                throw new ServiceResultException(StatusCodes.BadServerNotConnected, "Cannot browse if not connected to a server.");
            }

            try
            {
                m_browseInProgress = true;

                // construct request.
                BrowseDescription nodeToBrowse = new BrowseDescription();

                nodeToBrowse.NodeId = nodeId;
                nodeToBrowse.BrowseDirection = m_browseDirection;
                nodeToBrowse.ReferenceTypeId = m_referenceTypeId;
                nodeToBrowse.IncludeSubtypes = m_includeSubtypes;
                nodeToBrowse.NodeClassMask = m_nodeClassMask;
                nodeToBrowse.ResultMask = m_resultMask;

                BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
                nodesToBrowse.Add(nodeToBrowse);

                // make the call to the server.
                BrowseResultCollection results;
                DiagnosticInfoCollection diagnosticInfos;

                ResponseHeader responseHeader = m_session.Browse(
                    null,
                    m_view,
                    m_maxReferencesReturned,
                    nodesToBrowse,
                    out results,
                    out diagnosticInfos);

                // ensure that the server returned valid results.
                ClientBase.ValidateResponse(results, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                // check if valid.
                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable);
                }

                // fetch initial set of references.
                byte[] continuationPoint = results[0].ContinuationPoint;
                ReferenceDescriptionCollection references = results[0].References;

                // process any continuation point.
                while (continuationPoint != null)
                {
                    ReferenceDescriptionCollection additionalReferences;

                    if (!m_continueUntilDone && m_MoreReferences != null)
                    {
                        BrowserEventArgs args = new BrowserEventArgs(references);
                        m_MoreReferences(this, args);

                        // cancel browser and return the references fetched so far.
                        if (args.Cancel)
                        {
                            BrowseNext(ref continuationPoint, true);
                            return references;
                        }

                        m_continueUntilDone = args.ContinueUntilDone;
                    }

                    additionalReferences = BrowseNext(ref continuationPoint, false);
                    if (additionalReferences != null && additionalReferences.Count > 0)
                    {
                        references.AddRange(additionalReferences);
                    }
                    else
                    {
                        Utils.LogWarning("Browser: Continuation point exists, but the browse results are null/empty.");
                        break;
                    }
                }

                // return the results.
                return references;
            }
            finally
            {
                m_browseInProgress = false;
            }
        }
        #endregion        

        #region Private Methods
        /// <summary>
        /// Checks the state of the browser.
        /// </summary>
        private void CheckBrowserState()
        {
            if (m_browseInProgress)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "Cannot change browse parameters while a browse operation is in progress.");
            }
        }

        /// <summary>
        /// Fetches the next batch of references.
        /// </summary>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <param name="cancel">if set to <c>true</c> the browse operation is cancelled.</param>
        /// <returns>The next batch of references</returns>
        private ReferenceDescriptionCollection BrowseNext(ref byte[] continuationPoint, bool cancel)
        {
            ByteStringCollection continuationPoints = new ByteStringCollection();
            continuationPoints.Add(continuationPoint);

            // make the call to the server.
            BrowseResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = m_session.BrowseNext(
                null,
                cancel,
                continuationPoints,
                out results,
                out diagnosticInfos);

            // ensure that the server returned valid results.
            ClientBase.ValidateResponse(results, continuationPoints);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

            // check if valid.
            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable);
            }

            // update continuation point.
            continuationPoint = results[0].ContinuationPoint;

            // return references.
            return results[0].References;
        }
        #endregion

        #region Private Fields
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
        #endregion        
    }

    #region BrowserEventArgs Class
    /// <summary>
    /// The event arguments provided a browse operation returns a continuation point.
    /// </summary>
    public class BrowserEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal BrowserEventArgs(ReferenceDescriptionCollection references)
        {
            m_references = references;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Whether the browse operation should be cancelled.
        /// </summary>
        public bool Cancel
        {
            get { return m_cancel; }
            set { m_cancel = value; }
        }

        /// <summary>
        /// Whether subsequent continuation points should be processed automatically.
        /// </summary>
        public bool ContinueUntilDone
        {
            get { return m_continueUntilDone; }
            set { m_continueUntilDone = value; }
        }

        /// <summary>
        /// The references that have been fetched so far.
        /// </summary>
        public ReferenceDescriptionCollection References => m_references;
        #endregion

        #region Private Fields
        private bool m_cancel;
        private bool m_continueUntilDone;
        private ReferenceDescriptionCollection m_references;
        #endregion
    }

    /// <summary>
    /// A delegate used to received browser events.
    /// </summary>
    public delegate void BrowserEventHandler(Browser sender, BrowserEventArgs e);
    #endregion
}
