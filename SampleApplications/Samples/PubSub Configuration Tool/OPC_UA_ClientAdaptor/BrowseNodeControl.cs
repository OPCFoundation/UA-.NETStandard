/* Copyright (c) 1996-2017, OPC Foundation. All rights reserved.

   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else

   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/

   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2

   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Sample.Controls;
using PubSubBase.Definitions;
using System;
using System.Runtime;

namespace ClientAdaptor
{
    /// <summary>
    /// Class to browse address space
    /// </summary>
    public class BrowseNodeControl
    {
        #region Contructor
        /// <summary>
        /// initialise browse control for session
        /// </summary>
        /// <param name="session"></param>
        public BrowseNodeControl(Session session)
        {
            Browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = null,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ContinueUntilDone = false
            };
        }

        #endregion

        #region Private Fields

        private NodeId m_rootId;
        private Browser m_browser;
        private bool m_showReferences;

        #endregion

        #region Public Properties
        /// <summary>
        /// browser for Browse control
        /// </summary>
        public Browser Browser
        {
            get { return m_browser; }
            set { m_browser = value; }
        }
        #endregion

        #region Public methods

        /// <summary>
        /// Method to browse the selected node.
        /// </summary>
        /// <param name="node">selected node</param>
        /// <returns></returns>
        public bool Browse(ref TreeViewNode node)
        {
            //Fetch references.
            ReferenceDescriptionCollection references;
            try
            {
                if (!node.IsRoot) references = m_browser.Browse(node.Id);
                else  references = m_browser.Browse(m_rootId);

                //Add nodes to tree
                AddReferences(ref node,  references);
                return true;
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "BrowseNodeControl.Browse API" + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// Initialize browser.
        /// </summary>
        /// <param name="viewType">indicate view type</param>
        /// <param name="viewId">indicate node id</param>
        public void InitializeBrowserView(BrowseViewType viewType, NodeId viewId)
        {
            m_rootId = Objects.RootFolder;
            m_showReferences = false;

            switch (viewType)
            {
                case BrowseViewType.All:
                    {
                        m_showReferences = true;
                        break;
                    }

                case BrowseViewType.Objects:
                    {
                        m_rootId = Objects.ObjectsFolder;
                        Browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                        break;
                    }

                case BrowseViewType.Types:
                    {
                        m_rootId = Objects.TypesFolder;
                        Browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                        break;
                    }

                case BrowseViewType.ObjectTypes:
                    {
                        m_rootId = ObjectTypes.BaseObjectType;
                        Browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                        break;
                    }

                case BrowseViewType.EventTypes:
                    {
                        m_rootId = ObjectTypes.BaseEventType;
                        Browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                        break;
                    }

                case BrowseViewType.DataTypes:
                    {
                        m_rootId = DataTypeIds.BaseDataType;
                        Browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                        break;
                    }

                case BrowseViewType.ReferenceTypes:
                    {
                        m_rootId = ReferenceTypeIds.References;
                        Browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                        break;
                    }

                case BrowseViewType.ServerDefinedView:
                    {
                        m_rootId = viewId;
                        Browser.View = new ViewDescription
                        {
                            ViewId = viewId
                        };
                        m_showReferences = true;
                        break;
                    }
            }
        }


        #endregion

        #region Private Methods
        /// <summary>
        /// Metod to add reference 
        /// </summary>
        private void AddReferences(ref TreeViewNode parent, ReferenceDescriptionCollection references)
        {
            if (references.Count != 0)
                foreach (var reference in references)
                {
                    if (!m_showReferences)
                    {
                        var exists = false;
                        if (parent != null)
                            foreach (var existingChild in parent.Children)
                            {
                                var existingReference = existingChild.Reference;
                                if (existingReference != null &&
                                    existingReference.NodeId == reference.NodeId) //ToDO: Need to convert to nodeId
                                {
                                    exists = true;
                                    break;
                                }
                            }

                        if (exists) continue;
                    }

                    if (m_showReferences) FindReferenceTypeContainer(parent, reference);
                    var treeViewNode = new TreeViewNode
                    {
                        Header = GetTargetText(reference),
                        Id = reference.NodeId.ToString()
                    };
                    treeViewNode.Reference.BrowseName = reference.BrowseName.Name;
                    treeViewNode.Reference.IsForward = reference.IsForward;
                    treeViewNode.Reference.NodeId = reference.NodeId.ToString();
                    treeViewNode.Reference.NodeClass = reference.NodeClass.ToString();
                    treeViewNode.Reference.DisplayName = reference.DisplayName.ToString();
                    treeViewNode.Reference.TypeDefinition = reference.TypeDefinition.ToString();
                    treeViewNode.ParentId = parent.Id;

                    if (parent != null) parent.Children.Add(treeViewNode);

                    if (!reference.NodeId.IsAbsolute)
                    {
                        Browse(ref treeViewNode);
                    }

                }
        }

        /// <summary>
        /// Method to get name of selected node.
        /// </summary>
        private string GetTargetText(ReferenceDescription reference)
        {
            if (reference != null)
            {
                if (reference.DisplayName != null && !string.IsNullOrEmpty(reference.DisplayName.Text))
                    return reference.DisplayName.Text;

                if (reference.BrowseName != null) return reference.BrowseName.Name;
            }

            return null;
        }

        /// <summary>
        /// Method to find reference container type.
        /// </summary>
        /// <param name="parent"> parent node</param>
        /// <param name="reference"> current reference info</param>
        private void FindReferenceTypeContainer(TreeViewNode parent, ReferenceDescription reference)
        {
            if (parent == null) return;

            var typeNode = m_browser.Session.NodeCache.Find(reference.ReferenceTypeId) as ReferenceTypeNode;
            foreach (var child in parent.Children)
                if (typeNode != null && typeNode.NodeId == child.Reference.NodeId) //ToDO: covert to nodeId 
                {
                    if (typeNode.InverseName == null) return;

                    if (reference.IsForward)
                    {
                        if (child.Reference.DisplayName == typeNode.DisplayName.Text) return;
                    }
                    else
                    {
                        if (child.Reference.DisplayName == typeNode.InverseName.Text) return;
                    }
                }
        }

        #endregion
    }
}
