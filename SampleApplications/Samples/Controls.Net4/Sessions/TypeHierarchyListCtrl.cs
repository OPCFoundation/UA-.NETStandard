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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample
{
    public partial class TypeHierarchyListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        public TypeHierarchyListCtrl()
        {
            InitializeComponent();
			SetColumns(m_ColumnNames);
        }
		#endregion

        #region Private Fields
        private Session m_session;

		// The columns to display in the control.		
		private readonly object[][] m_ColumnNames = new object[][]
		{
			new object[] { "Name", HorizontalAlignment.Left, null },  
			new object[] { "Type", HorizontalAlignment.Left, null },  
			new object[] { "Description", HorizontalAlignment.Left, null }
		};

        private class InstanceDeclaration
        {
            public ILocalNode Instance;
            public string DisplayPath;
            public string DataType;
            public string Description;
        }
		#endregion

        #region Public Interface
        /// <summary>
        /// Initializes the control.
        /// </summary>
        public void Initialize(Session session, NodeId typeId)
        {
            ItemsLV.Items.Clear();
            AdjustColumns();

            if (session == null)
            {
                return;
            }

            ILocalNode root = session.NodeCache.Find(typeId) as ILocalNode;

            if (root == null)
            {
                return;
            }
            
            m_session = session;

            SortedDictionary<string,InstanceDeclaration> instances = new SortedDictionary<string,InstanceDeclaration>();
        
            InstanceDeclaration declaration = new InstanceDeclaration();

            declaration.Instance = root;
            declaration.DisplayPath = Utils.Format("({0})", root.NodeClass);
            declaration.Description = Utils.Format("{0}", root.Description);
            declaration.DataType = "NodeId";
            
            IVariableBase variable = root as IVariableBase;

            if (variable != null)
            {
                INode dataType =  m_session.NodeCache.Find(variable.DataType);

                if (dataType != null)
                {
                    declaration.DataType = Utils.Format("{0}", dataType);
                }

                if (variable.ValueRank >= 0)
                {
                    declaration.DataType += "[]";
                }
            }                

            instances.Add(declaration.DisplayPath, declaration);

            CollectInstances(root, String.Empty, instances);

            foreach (InstanceDeclaration instance in instances.Values)
            {
                AddItem(instance);
            }
            
            AdjustColumns();
        }
        #endregion
        
        #region Overridden Methods
        /// <see cref="Opc.Ua.Client.Controls.BaseListCtrl.UpdateItem(ListViewItem,object)" />
        protected override void UpdateItem(ListViewItem listItem, object item)
        {
            InstanceDeclaration instance = item as InstanceDeclaration;

			if (instance == null)
			{
				base.UpdateItem(listItem, item);
				return;
			}

			listItem.SubItems[0].Text = instance.DisplayPath;
			listItem.SubItems[1].Text = instance.DataType;
			listItem.SubItems[2].Text = instance.Description;
            
            listItem.ImageKey = GuiUtils.GetTargetIcon(m_session, instance.Instance.NodeClass, instance.Instance.TypeDefinitionId);
			listItem.Tag = item;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Collects the instance declarations to display in the control.
        /// </summary>
        private void CollectInstances(ILocalNode parent, string basePath, SortedDictionary<string,InstanceDeclaration> instances)
        {         
            if (parent == null)
            {
                return;
            }

            IList<IReference> supertypes = parent.References.Find(
                ReferenceTypeIds.HasSubtype,
                true,
                false,
                m_session.TypeTree);

            for (int ii = 0; ii < supertypes.Count; ii++)
            {
                ILocalNode supertype = m_session.NodeCache.Find(supertypes[ii].TargetId) as ILocalNode;

                if (supertype == null)
                {
                    continue;
                }

                CollectInstances(supertype, basePath, instances);
            }

            IList<IReference> children = parent.References.Find(
                ReferenceTypeIds.HierarchicalReferences,
                false,
                true,
                m_session.TypeTree);

            for (int ii = 0; ii < children.Count; ii++)
            {
                ILocalNode child = m_session.NodeCache.Find(children[ii].TargetId) as ILocalNode;

                if (child == null)
                {
                    continue;
                }

                if (child.NodeClass != NodeClass.Object && child.NodeClass != NodeClass.Variable)
                {
                    continue;
                }

                if (child.ModellingRule != Objects.ModellingRule_Mandatory && child.ModellingRule != Objects.ModellingRule_Optional)
                {
                    continue;
                }

                string displayPath = Utils.Format("{0}", child);
            
                if (!String.IsNullOrEmpty(basePath))
                {
                    displayPath = Utils.Format("{0}/{1}", basePath, displayPath);
                }
            
                InstanceDeclaration declaration = new InstanceDeclaration();

                declaration.Instance = child;
                declaration.DisplayPath = displayPath;
                declaration.Description = Utils.Format("{0}", child.Description);
                declaration.DataType = String.Empty;

                IVariableBase variable = child as IVariableBase;

                if (variable != null)
                {
                    INode dataType =  m_session.NodeCache.Find(variable.DataType);

                    if (dataType != null)
                    {
                        declaration.DataType = Utils.Format("{0}", dataType);
                    }

                    if (variable.ValueRank >= 0)
                    {
                        declaration.DataType += "[]";
                    }
                }
                
                IObject objectn = child as IObject;

                if (objectn != null)
                {                    
                    declaration.DataType = "NodeId";
                }

                instances[displayPath] = declaration;
                CollectInstances(child, displayPath, instances);
            }
        }
        #endregion
    }
}
