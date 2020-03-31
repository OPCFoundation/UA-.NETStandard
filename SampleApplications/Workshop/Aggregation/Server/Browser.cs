/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Server;

namespace AggregationServer
{
    /// <summary>
    /// Browses the children of a segment.
    /// </summary>
    public class Browser : NodeBrowser
    {
        #region Constructors
        /// <summary>
        /// Creates a new browser object with a set of filters.
        /// </summary>
        public Browser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly,
            Opc.Ua.Client.Session client,
            NamespaceMapper mapper,
            NodeState source,
            NodeId rootId)
        :
            base(
                context,
                view,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                additionalReferences,
                internalOnly)
        {
            m_client = client;
            m_mapper = mapper;
            m_source = source;
            m_rootId = rootId;
            m_stage = Stage.Begin;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns the next reference.
        /// </summary>
        /// <returns>The next reference that meets the browse criteria.</returns>
        public override IReference Next()
        {
            lock (DataLock)
            {
                IReference reference = null;

                // enumerate pre-defined references.
                // always call first to ensure any pushed-back references are returned first.
                reference = base.Next();

                if (reference != null)
                {
                    return reference;
                }

                // don't start browsing huge number of references when only internal references are requested.
                if (InternalOnly)
                {
                    return null;
                }

                if (m_stage == Stage.Begin)
                {
                    // construct request.
                    BrowseDescription nodeToBrowse = new BrowseDescription();

                    NodeId startId = ObjectIds.ObjectsFolder;

                    if (m_source != null)
                    {
                        startId = m_mapper.ToRemoteId(m_source.NodeId);
                    }

                    nodeToBrowse.NodeId = startId;
                    nodeToBrowse.BrowseDirection = this.BrowseDirection;
                    nodeToBrowse.ReferenceTypeId = this.ReferenceType;
                    nodeToBrowse.IncludeSubtypes = this.IncludeSubtypes;
                    nodeToBrowse.NodeClassMask = 0;
                    nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

                    BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
                    nodesToBrowse.Add(nodeToBrowse);

                    // start the browse operation.
                    BrowseResultCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;

                    ResponseHeader responseHeader = m_client.Browse(
                        null,
                        null,
                        0,
                        nodesToBrowse,
                        out results,
                        out diagnosticInfos);

                    // these do sanity checks on the result - make sure response matched the request.
                    ClientBase.ValidateResponse(results, nodesToBrowse);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                    m_position = 0;
                    m_references = null;
                    m_continuationPoint = null;
                    m_stage = Stage.References;

                    // check status.
                    if (StatusCode.IsGood(results[0].StatusCode))
                    {
                        m_references = results[0].References;
                        m_continuationPoint = results[0].ContinuationPoint;

                        reference = NextChild();

                        if (reference != null)
                        {
                            return reference;
                        }
                    }
                }

                if (m_stage == Stage.References)
                {
                    reference = NextChild();

                    if (reference != null)
                    {
                        return reference;
                    }

                    if (m_source == null && IsRequired(ReferenceTypes.HasNotifier, false))
                    {
                        // construct request.
                        BrowseDescription nodeToBrowse = new BrowseDescription();

                        nodeToBrowse.NodeId = ObjectIds.Server;
                        nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
                        nodeToBrowse.ReferenceTypeId = ReferenceTypes.HasNotifier;
                        nodeToBrowse.IncludeSubtypes = true;
                        nodeToBrowse.NodeClassMask = 0;
                        nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

                        BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
                        nodesToBrowse.Add(nodeToBrowse);

                        // start the browse operation.
                        BrowseResultCollection results = null;
                        DiagnosticInfoCollection diagnosticInfos = null;

                        ResponseHeader responseHeader = m_client.Browse(
                            null,
                            null,
                            0,
                            nodesToBrowse,
                            out results,
                            out diagnosticInfos);

                        // these do sanity checks on the result - make sure response matched the request.
                        ClientBase.ValidateResponse(results, nodesToBrowse);
                        ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                        m_position = 0;
                        m_references = null;
                        m_continuationPoint = null;
                        m_stage = Stage.Notifiers;

                        // check status.
                        if (StatusCode.IsGood(results[0].StatusCode))
                        {
                            m_references = results[0].References;
                            m_continuationPoint = results[0].ContinuationPoint;
                        }
                    }

                    m_stage = Stage.Notifiers;
                }

                if (m_stage == Stage.Notifiers)
                {
                    reference = NextChild();

                    if (reference != null)
                    {
                        return reference;
                    }

                    m_stage = Stage.Done;
                }

                // all done.
                return null;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Fetches the next batch of references.
        /// </summary>
        private bool BrowseNext()
        {
            if (m_continuationPoint != null)
            {
                ByteStringCollection continuationPoints = new ByteStringCollection();
                continuationPoints.Add(m_continuationPoint);

                // start the browse operation.
                BrowseResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                ResponseHeader responseHeader = m_client.BrowseNext(
                    null,
                    false,
                    continuationPoints,
                    out results,
                    out diagnosticInfos);

                // these do sanity checks on the result - make sure response matched the request.
                ClientBase.ValidateResponse(results, continuationPoints);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

                m_position = 0;
                m_references = null;
                m_continuationPoint = null;

                // check status.
                if (StatusCode.IsGood(results[0].StatusCode))
                {
                    m_references = results[0].References;
                    m_continuationPoint = results[0].ContinuationPoint;
                }

                return m_references != null || m_references.Count > 0;
            }

            return false;
        }

        /// <summary>
        /// Converts a ReferenceDescription to an IReference.
        /// </summary>
        private IReference ToReference(ReferenceDescription reference)
        {
            if (reference.NodeId.IsAbsolute || reference.TypeDefinition.IsAbsolute)
            {
                return new NodeStateReference(reference.ReferenceTypeId, !reference.IsForward, reference.NodeId);
            }

            if (m_source != null && (reference.NodeId == ObjectIds.ObjectsFolder || reference.NodeId == ObjectIds.Server))
            {
                return new NodeStateReference(reference.ReferenceTypeId, !reference.IsForward, m_rootId);
            }

            NodeState target = null;

            switch (reference.NodeClass)
            {
                case NodeClass.DataType: { target = new DataTypeState(); break; }
                case NodeClass.Method: { target = new MethodState(null); break; }
                case NodeClass.Object: { target = new BaseObjectState(null); break; }
                case NodeClass.ObjectType: { target = new BaseObjectTypeState(); break; }
                case NodeClass.ReferenceType: { target = new ReferenceTypeState(); break; }
                case NodeClass.Variable: { target = new BaseDataVariableState(null); break; }
                case NodeClass.VariableType: { target = new BaseDataVariableTypeState(); break; }
                case NodeClass.View: { target = new ViewState(); break; }
            }

            target.NodeId = m_mapper.ToLocalId((NodeId)reference.NodeId);
            target.BrowseName = m_mapper.ToLocalName(reference.BrowseName);
            target.DisplayName = reference.DisplayName;

            if (target is BaseInstanceState)
            {
                ((BaseInstanceState)target).TypeDefinitionId = m_mapper.ToLocalId((NodeId)reference.TypeDefinition);
            }

            return new NodeStateReference(reference.ReferenceTypeId, !reference.IsForward, target);
        }

        /// <summary>
        /// Returns the next child.
        /// </summary>
        private IReference NextChild()
        {
            // check if a specific browse name is requested.
            if (!QualifiedName.IsNull(base.BrowseName))
            {
                // check if match found previously.
                if (m_position == Int32.MaxValue)
                {
                    return null;
                }

                // look for matching reference.
                if (m_stage != Stage.Done)
                {
                    do
                    {
                        while (m_references != null && m_position < m_references.Count)
                        {
                            ReferenceDescription reference = m_references[m_position++];

                            if (m_mapper.ToLocalName(reference.BrowseName) == this.BrowseName)
                            {
                                if (m_source != null || (reference.NodeId != ObjectIds.ObjectsFolder && reference.NodeId != ObjectIds.Server))
                                {
                                    return ToReference(reference);
                                }
                            }
                        }
                    }
                    while (BrowseNext());
                }
            }

            // return the child at the next position.
            else
            {
                if (m_stage != Stage.Done)
                {
                    do
                    {
                        while (m_references != null && m_position < m_references.Count)
                        {
                            ReferenceDescription reference = m_references[m_position++];

                            if (m_source != null || (reference.NodeId != ObjectIds.ObjectsFolder && reference.NodeId != ObjectIds.Server))
                            {
                                return ToReference(reference);
                            }
                        }
                    }
                    while (BrowseNext());
                }
            }

            return null;
        }
        #endregion

        #region Stage Enumeration
        /// <summary>
        /// The stages available in a browse operation.
        /// </summary>
        private enum Stage
        {
            Begin,
            References,
            Notifiers,
            Done
        }
        #endregion

        #region Private Fields
        private Stage m_stage;
        private int m_position;
        private Opc.Ua.Client.Session m_client;
        private NamespaceMapper m_mapper;
        private NodeState m_source;
        private byte[] m_continuationPoint;
        private ReferenceDescriptionCollection m_references;
        private NodeId m_rootId;
        #endregion
    }
}
