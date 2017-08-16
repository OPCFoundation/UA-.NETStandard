using Opc.Ua;
using Opc.Ua.Client;
using PubSubBase.Definitions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientAdaptor
{
    public class BrowseNodeControl
    {

        #region Contructor

        public BrowseNodeControl(Session session)
        {
            Browser = new Browser(session);
            Browser.BrowseDirection = BrowseDirection.Forward;
            Browser.ReferenceTypeId = null;
            Browser.IncludeSubtypes = true;
            Browser.NodeClassMask = 0;
            Browser.ContinueUntilDone = false;
        }

        #endregion

        #region Public & Private Variables

        private NodeId _rootId;
        private Browser _browser;

        public Browser Browser
        {
            get { return _browser; }
            set { _browser = value; }
        }

        private bool _showReferences;

        #endregion

        #region Public & Private Functions

        public bool Browse(ref TreeViewNode node)
        {
            //Fetch references.
            ReferenceDescriptionCollection references;
            try
            {
                if (!node.IsRoot) references = _browser.Browse(node.Id);
                else references = _browser.Browse(_rootId);

                //Add nodes to tree
                AddReferences(ref node, references);
                return true;
            }
            catch (Exception ex)
            {
                NLogManager.Log.Error("BrowseNodeControl.Browse API" + ex.Message);
               
            }

            return false;
        }

        public void InitializeBrowserView(BrowseViewType viewType, NodeId viewId)
        {
            _rootId = Objects.RootFolder;
            _showReferences = false;

            switch (viewType)
            {
                case BrowseViewType.All:
                {
                    _showReferences = true;
                    break;
                }

                case BrowseViewType.Objects:
                {
                    _rootId = Objects.ObjectsFolder;
                    Browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                    break;
                }

                case BrowseViewType.Types:
                {
                    _rootId = Objects.TypesFolder;
                    Browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                    break;
                }

                case BrowseViewType.ObjectTypes:
                {
                    _rootId = ObjectTypes.BaseObjectType;
                    Browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.EventTypes:
                {
                    _rootId = ObjectTypes.BaseEventType;
                    Browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.DataTypes:
                {
                    _rootId = DataTypeIds.BaseDataType;
                    Browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.ReferenceTypes:
                {
                    _rootId = ReferenceTypeIds.References;
                    Browser.ReferenceTypeId = ReferenceTypeIds.HasChild;
                    break;
                }

                case BrowseViewType.ServerDefinedView:
                {
                    _rootId = viewId;
                    Browser.View = new ViewDescription();
                    Browser.View.ViewId = viewId;
                    _showReferences = true;
                    break;
                }
            }
        }

        private void AddReferences(ref TreeViewNode parent, ReferenceDescriptionCollection references)
        {
            if (references.Count != 0)
                foreach (var reference in references)
                {
                    if (!_showReferences)
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

                    if (_showReferences) FindReferenceTypeContainer(parent, reference);
                    var treeViewNode = new TreeViewNode();
                    treeViewNode.Header = GetTargetText(reference);
                    treeViewNode.Id = reference.NodeId.ToString();
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
                        //if (reference.NodeClass == NodeClass.Variable && !reference.NodeId.IsAbsolute)
                        //    continue;
                        Browse(ref treeViewNode);
                    }

                }
        }

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

        private void FindReferenceTypeContainer(TreeViewNode parent, ReferenceDescription reference)
        {
            if (parent == null) return;

            var typeNode = _browser.Session.NodeCache.Find(reference.ReferenceTypeId) as ReferenceTypeNode;
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

            if (typeNode != null && (!reference.IsForward && typeNode.InverseName != null))
            {
            }
        }

        #endregion
    }
}
