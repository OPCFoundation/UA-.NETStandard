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
using System.Xml;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using OpcRcw.Hda;
using OpcRcw.Comn;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Browses the children of a segment.
    /// </summary>
    public class HdaElementBrower : NodeBrowser
    {
        #region Constructors
        /// <summary>
        /// Creates a new browser object with a set of filters.
        /// </summary>
        /// <param name="context">The system context to use.</param>
        /// <param name="view">The view which may restrict the set of references/nodes found.</param>
        /// <param name="referenceType">The type of references being followed.</param>
        /// <param name="includeSubtypes">Whether subtypes of the reference type are followed.</param>
        /// <param name="browseDirection">Which way the references are being followed.</param>
        /// <param name="browseName">The browse name of a specific target (used when translating browse paths).</param>
        /// <param name="additionalReferences">Any additional references that should be included.</param>
        /// <param name="internalOnly">If true the browser should not making blocking calls to external systems.</param>
        /// <param name="itemId">Name of the qualified.</param>
        /// <param name="typeDefinitionId">The type definition id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public HdaElementBrower(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly,
            string itemId,
            NodeId sourceTypeDefinitionId,
            QualifiedName sourceBrowseName,
            ushort namespaceIndex)
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
            m_namespaceIndex = namespaceIndex;
            m_itemId = itemId;
            m_sourceTypeDefinitionId = sourceTypeDefinitionId;
            m_sourceBrowseName = sourceBrowseName;
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

                // fetch references from the server.
                do
                {
                    // fetch next reference.
                    reference = NextChild();

                    if (reference != null)
                    {
                        return reference;
                    }

                    // go to the next stage.
                    NextStage();
                }
                while (m_stage != Stage.Done);

                // all done.
                return null;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the next child.
        /// </summary>
        private IReference NextChild()
        {
            // check if a specific browse name is requested.
            if (QualifiedName.IsNull(base.BrowseName))
            {
                return NextChild(m_stage);                
            }

            // keep fetching references until a matching browse name if found.
            NodeStateReference reference = null;

            do
            {
                reference = NextChild(m_stage);

                if (reference != null)
                {
                    // need to let the caller look up the browse name.
                    if (reference.Target == null)
                    {
                        return reference;
                    }

                    // check for browse name match.
                    if (reference.Target.BrowseName == base.BrowseName)
                    {
                        return reference;
                    }
                }
            }
            while (reference != null);

            // no match - need to go onto the next stage.
            return null;
        }

        /// <summary>
        /// Returns the next child.
        /// </summary>
        private NodeStateReference NextChild(Stage stage)
        {
            // fetch children.
            if (stage == Stage.Browse)
            {
                if (m_browser == null)
                {
                    return null;
                }

                BaseInstanceState node = m_browser.Next(SystemContext, m_namespaceIndex);

                if (node != null)
                {
                    return new NodeStateReference(ReferenceTypeIds.Organizes, false, node);
                }

                // all done.
                return null;
            }

            // fetch attributes.
            if (stage == Stage.Children)
            {
                if (m_children == null)
                {
                    return null;
                }

                for (int ii = m_position; ii < m_children.Length;)
                {
                    m_position = ii+1;
                    return new NodeStateReference(m_children[ii].ReferenceTypeId, false, m_children[ii]);
                }

                // all done.
                return null;
            }

            // fetch child parents.
            if (stage == Stage.Parents)
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Initializes the next stage of browsing.
        /// </summary>
        private void NextStage()
        {
            ComHdaClientManager system = (ComHdaClientManager)this.SystemContext.SystemHandle;
            ComHdaClient client = (ComHdaClient)system.SelectClient((ServerSystemContext)SystemContext, false);

            // determine which stage is next based on the reference types requested.
            for (Stage next = m_stage+1; next <= Stage.Done; next++)
            {
                if (next == Stage.Browse)
                {
                    if (IsRequired(ReferenceTypeIds.Organizes, false))
                    {
                        m_stage = next;
                        break;
                    }
                }

                else if (next == Stage.Children)
                {
                    if (IsRequired(ReferenceTypeIds.HasProperty, false) || IsRequired(ReferenceTypeIds.HasHistoricalConfiguration, false))
                    {
                        m_stage = next;
                        break;
                    }
                }

                else if (next == Stage.Parents)
                {
                    if (IsRequired(ReferenceTypeIds.Organizes, true) || IsRequired(ReferenceTypeIds.HasHistoricalConfiguration, true))
                    {
                        m_stage = next;
                        break;
                    }
                }

                else if (next == Stage.Done)
                {
                    m_stage = next;
                    break;
                }
            }

            // start enumerating areas.
            if (m_stage == Stage.Browse)
            {
                m_browser = null;

                if (m_sourceTypeDefinitionId == Opc.Ua.ObjectTypeIds.FolderType)
                {
                    m_browser = new ComHdaBrowserClient(client, m_itemId);
                }

                return;
            }

            // start enumerating attributes.
            if (m_stage == Stage.Children)
            {
                m_position = 0;
                m_children = null;

                if (m_sourceTypeDefinitionId == Opc.Ua.ObjectTypeIds.FolderType)
                {
                    return;
                }

                List<BaseInstanceState> children = new List<BaseInstanceState>();

                // check if browsing aggregate functions.
                if (m_sourceBrowseName == Opc.Ua.BrowseNames.AggregateFunctions)
                {
                    if (IsRequired(ReferenceTypeIds.HasComponent, false))
                    {
                        BaseObjectState[] aggregates = client.GetSupportedAggregates(m_namespaceIndex);

                        if (aggregates != null)
                        {
                            children.AddRange(aggregates);
                        }

                        m_children = children.ToArray();
                    }

                    return;
                }
                
                // must be browsing children of an item.
                HdaItem[] items = client.GetItems(m_itemId);
                
                if (items[0].Error < 0)
                {
                    return;
                }
                    
                try
                {
                    HdaAttributeValue[] attributes = client.ReadAvailableAttributes(items[0]);

                    if (m_sourceTypeDefinitionId == Opc.Ua.VariableTypeIds.DataItemType)
                    {
                        FindChildrenForHdaItem(client, children, attributes);
                    }

                    if (m_sourceTypeDefinitionId == Opc.Ua.ObjectTypeIds.HistoricalDataConfigurationType)
                    {
                        FindChildrenForHdaItemConfiguration(client, children, attributes);
                    }
                }
                finally
                {
                    client.ReleaseItemHandles(items);
                }

                m_children = children.ToArray();
                return;
            }

            // start enumerating parents.
            if (m_stage == Stage.Parents)
            {
                return;
            }

            // all done.
        }

        /// <summary>
        /// Finds the children for hda item.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="children">The children.</param>
        /// <param name="attributes">The attribute values.</param>
        private void FindChildrenForHdaItem(ComHdaClient client, List<BaseInstanceState> children, HdaAttributeValue[] attributes)
        {
            BaseInstanceState child = null;

            if (IsRequired(ReferenceTypeIds.HasHistoricalConfiguration, false))
            {
                child = HdaModelUtils.GetItemConfigurationNode(m_itemId, m_namespaceIndex);

                if (child != null)
                {
                    children.Add(child);
                }
            }

            if (IsRequired(ReferenceTypeIds.HasProperty, false))
            {
                child = client.FindItemAnnotations(m_itemId, m_namespaceIndex);

                if (child != null)
                {
                    children.Add(child);
                }

                if (attributes != null)
                {
                    for (int ii = 0; ii < attributes.Length; ii++)
                    {
                        if (attributes[ii].Error < 0 || attributes[ii].Error == ResultIds.S_NODATA)
                        {
                            continue;
                        }

                        bool skip = false;

                        switch (attributes[ii].AttributeId)
                        {
                            case Constants.OPCHDA_DATA_TYPE:
                            case Constants.OPCHDA_DESCRIPTION:
                            case Constants.OPCHDA_ITEMID:
                            case Constants.OPCHDA_ARCHIVING:
                            case Constants.OPCHDA_DERIVE_EQUATION:
                            case Constants.OPCHDA_STEPPED:
                            case Constants.OPCHDA_MAX_TIME_INT:
                            case Constants.OPCHDA_MIN_TIME_INT:
                            case Constants.OPCHDA_EXCEPTION_DEV:
                            case Constants.OPCHDA_EXCEPTION_DEV_TYPE:
                                {
                                    skip = true;
                                    break;
                                }
                        }

                        if (skip)
                        {
                            continue;
                        }

                        child = client.FindItemAttribute(m_itemId, attributes[ii].AttributeId, m_namespaceIndex);

                        if (child != null)
                        {
                            children.Add(child);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Finds the children for hda item configuration.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="children">The children.</param>
        /// <param name="attributes">The attribute values.</param>
        private void FindChildrenForHdaItemConfiguration(ComHdaClient client, List<BaseInstanceState> children, HdaAttributeValue[] attributes)
        {          
            BaseInstanceState child = null;

            if (IsRequired(ReferenceTypeIds.HasProperty, false))
            {
                for (int ii = 0; ii < attributes.Length; ii++)
                {
                    if (attributes[ii].Error < 0 || attributes[ii].Error == ResultIds.S_NODATA)
                    {
                        continue;
                    }

                    bool skip = true;

                    switch (attributes[ii].AttributeId)
                    {
                        case Constants.OPCHDA_DERIVE_EQUATION:
                        case Constants.OPCHDA_STEPPED:
                        case Constants.OPCHDA_MAX_TIME_INT:
                        case Constants.OPCHDA_MIN_TIME_INT:
                        case Constants.OPCHDA_EXCEPTION_DEV:
                        case Constants.OPCHDA_EXCEPTION_DEV_TYPE:
                        {
                            skip = false;
                            break;
                        }
                    }

                    if (skip)
                    {
                        continue;
                    }

                    child = client.FindItemAttribute(m_itemId, attributes[ii].AttributeId, m_namespaceIndex);

                    if (child != null)
                    {
                        children.Add(child);
                    }
                }
            }
        }
        #endregion

        #region Stage Enumeration
        /// <summary>
        /// The stages available in a browse operation.
        /// </summary>
        private enum Stage
        {
            Begin,
            Browse,
            Children,
            Parents,
            Done
        }
        #endregion

        #region Private Fields
        private Stage m_stage;
        private string m_itemId;
        private NodeId m_sourceTypeDefinitionId;
        private QualifiedName m_sourceBrowseName;
        private ushort m_namespaceIndex;
        private ComHdaBrowserClient m_browser;
        private BaseInstanceState[] m_children;
        private int m_position;
        #endregion
    }
}
