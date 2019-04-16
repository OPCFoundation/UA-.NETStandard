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

namespace Opc.Ua.Com.Client
{    
    /// <summary>
    /// A object which maps a segment to a UA object.
    /// </summary>
    public partial class DaBranchState : FolderState
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="DaBranchState"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="element">The element.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public DaBranchState(
            ISystemContext context, 
            DaElement element, 
            ushort namespaceIndex)
        : 
            base(null)
        {
            this.TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType;
            this.Description = null;
            this.WriteMask = 0;
            this.UserWriteMask = 0;
            this.EventNotifier = EventNotifiers.None;

            if (element != null)
            {
                Initialize(context, element, namespaceIndex);
            }
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Gets the item id.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get
            {
                if (m_element != null)
                {
                    return m_element.ItemId;
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the element.
        /// </summary>
        /// <value>The element.</value>
        public DaElement Element
        {
            get { return m_element; }
        }

        /// <summary>
        /// Initializes the node from the element.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="element">The element.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        public void Initialize(ISystemContext context, DaElement element, ushort namespaceIndex)
        {
            m_element = element;

            if (element == null)
            {
                return;
            }

            this.NodeId = DaModelUtils.ConstructIdForDaElement(element.ItemId, -1, namespaceIndex);
            this.BrowseName = new QualifiedName(element.Name, namespaceIndex);
            this.DisplayName = new LocalizedText(element.Name);
        }

        /// <summary>
        /// Creates a browser that finds the references to the branch.
        /// </summary>
        /// <param name="context">The system context to use.</param>
        /// <param name="view">The view which may restrict the set of references/nodes found.</param>
        /// <param name="referenceType">The type of references being followed.</param>
        /// <param name="includeSubtypes">Whether subtypes of the reference type are followed.</param>
        /// <param name="browseDirection">Which way the references are being followed.</param>
        /// <param name="browseName">The browse name of a specific target (used when translating browse paths).</param>
        /// <param name="additionalReferences">Any additional references that should be included.</param>
        /// <param name="internalOnly">If true the browser should not making blocking calls to external systems.</param>
        /// <returns>The browse object (must be disposed).</returns>
        public override INodeBrowser CreateBrowser(
            ISystemContext context, 
            ViewDescription view, 
            NodeId referenceType, 
            bool includeSubtypes, 
            BrowseDirection browseDirection, 
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly)
        {
            NodeBrowser browser = new DaElementBrowser(
                context,
                view,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                additionalReferences,
                internalOnly,
                this.ItemId,
                this.NodeId.NamespaceIndex);

            PopulateBrowser(context, browser);

            return browser;
        }
        #endregion

        #region Private Fields
        private DaElement m_element;
        #endregion
    }
}
