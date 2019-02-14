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
using OpcRcw.Ae;
using OpcRcw.Comn;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Browses the children of a segment.
    /// </summary>
    public class AeAreaBrower : NodeBrowser
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
        /// <param name="qualifiedName">Name of the qualified.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public AeAreaBrower(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly,
            string qualifiedName,
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
            m_qualifiedName = qualifiedName;
            m_namespaceIndex = namespaceIndex;
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
            if (stage == Stage.Children)
            {
                if (m_browser == null)
                {
                    return null;
                }

                BaseObjectState node = m_browser.Next(SystemContext, m_namespaceIndex);

                if (node != null)
                {
                    return new NodeStateReference(ReferenceTypeIds.HasNotifier, false, node.NodeId);
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
            ComAeClientManager system = (ComAeClientManager)this.SystemContext.SystemHandle;
            ComAeClient client = system.SelectClient((ServerSystemContext)SystemContext, false);

            // determine which stage is next based on the reference types requested.
            for (Stage next = m_stage+1; next <= Stage.Done; next++)
            {
                if (next == Stage.Children)
                {
                    if (IsRequired(ReferenceTypeIds.HasNotifier, false))
                    {
                        m_stage = next;
                        break;
                    }
                }

                else if (next == Stage.Parents)
                {
                    if (IsRequired(ReferenceTypeIds.HasNotifier, true))
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
            if (m_stage == Stage.Children)
            {
                m_browser = new ComAeBrowserClient(client, m_qualifiedName);
                return;
            }

            // start enumerating parents.
            if (m_stage == Stage.Parents)
            {
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
            Parents,
            Done
        }
        #endregion

        #region Private Fields
        private Stage m_stage;
        private string m_qualifiedName;
        private ushort m_namespaceIndex;
        private ComAeBrowserClient m_browser;
        #endregion
    }
}
