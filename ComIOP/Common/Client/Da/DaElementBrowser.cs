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

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Browses the children of a segment.
    /// </summary>
    public class DaElementBrowser : NodeBrowser
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
        /// <param name="itemId">The item id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public DaElementBrowser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly,
            string itemId,
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
            m_itemId = itemId;
            m_namespaceIndex = namespaceIndex;
            m_stage = Stage.Begin;
        }
        #endregion
        
        #region Overridden Methods
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        /// <param name="disposing">True if being called explicitly</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(m_browser);
                m_browser = null;
            }

            base.Dispose(disposing);
        }

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
            ComDaClientManager system = (ComDaClientManager)this.SystemContext.SystemHandle;
            ComDaClient client = system.SelectClient((ServerSystemContext)SystemContext, false);

            DaElement element = null;

            if (stage == Stage.Children)
            {
                if (m_browser == null)
                {
                    return null;
                }

                element = m_browser.Next();

                if (element == null)
                {
                    return null;
                }

                // construct the node.
                NodeState node = DaModelUtils.ConstructElement(SystemContext, element, m_namespaceIndex);
                
                // return the reference.
                return new NodeStateReference(ReferenceTypeIds.Organizes, false, node);
            }

            if (stage == Stage.Properties)
            {
                if (m_properties == null)
                {
                    return null;
                }

                for (int ii = m_position; ii < m_properties.Length; ii++)
                {
                    if (m_properties[ii].PropertyId <= PropertyIds.TimeZone)
                    {
                        continue;
                    }

                    m_position = ii+1;

                    // construct the node.
                    NodeState node = DaModelUtils.ConstructProperty(SystemContext, m_itemId, m_properties[ii], m_namespaceIndex);

                    // return the reference.
                    return new NodeStateReference(ReferenceTypeIds.HasProperty, false, node);
                }

                // all done.
                return null;
            }

            if (stage == Stage.Parents)
            {
                if (m_parentId != null)
                {
                    NodeId parentId = DaModelUtils.ConstructIdForDaElement(m_parentId, -1, m_namespaceIndex);
                    m_parentId = null;
                    return new NodeStateReference(ReferenceTypeIds.Organizes, true, parentId);
                }
            }

            return null;
        }

        /// <summary>
        /// Initializes the next stage of browsing.
        /// </summary>
        private void NextStage()
        {
            ComDaClientManager system = (ComDaClientManager)this.SystemContext.SystemHandle;
            ComDaClient client = system.SelectClient((ServerSystemContext)SystemContext, false);

            // determine which stage is next based on the reference types requested.
            for (Stage next = m_stage+1; next <= Stage.Done; next++)
            {
                if (next == Stage.Children)
                {
                    if (IsRequired(ReferenceTypeIds.Organizes, false))
                    {
                        m_stage = next;
                        break;
                    }
                }

                else if (next == Stage.Properties)
                {
                    if (IsRequired(ReferenceTypeIds.HasProperty, false))
                    {
                        m_stage = next;
                        break;
                    }
                }

                else if (next == Stage.Parents)
                {
                    if (IsRequired(ReferenceTypeIds.Organizes, true))
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

            m_position = 0;

            // start enumerating branches.
            if (m_stage == Stage.Children)
            {
                m_browser = client.CreateBrowser(m_itemId);
                return;
            }

            // start enumerating properties.
            if (m_stage == Stage.Properties)
            {
                m_properties = client.ReadAvailableProperties(m_itemId, true);
                m_position = 0;
                return;
            }

            // start enumerating parents.
            if (m_stage == Stage.Parents)
            {
                m_parentId = client.FindElementParentId(m_itemId);
                return;
            }

            // all done.
        }

        #endregion

        #region Stage Enumeration
        /// <summary>
        /// The stages available in a browse operation.
        /// </summary>
        private enum Stage
        {
            Begin,
            Children,
            Properties,
            Parents,
            Done
        }
        #endregion

        #region Private Fields
        private Stage m_stage;
        private string m_itemId;
        private ushort m_namespaceIndex;
        private DaProperty[] m_properties;
        private int m_position;
        private string m_parentId;
        private IDaElementBrowser m_browser;
        #endregion
    }
}
