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


using System.Collections.ObjectModel;
using ClientAdaptor;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for Target variables view
    /// </summary>
    public class TargetVariablesViewModel : BaseViewModel
    {
        #region Private Fields 

        private int m_majorVersion;
        private int m_minorVersion;
        private readonly IOPCUAClientAdaptor m_OPCUAClientAdaptor;
        private readonly TreeViewNode m_rootNode;
        private ObservableCollection< TreeViewNode > m_serverItems = new ObservableCollection< TreeViewNode >( );

        private ObservableCollection< FieldTargetVariableDefinition > m_variableListDefinitionCollection =
        new ObservableCollection< FieldTargetVariableDefinition >( );

        #endregion

        #region Constructors

        public TargetVariablesViewModel( IOPCUAClientAdaptor opcUAClientAdaptor, TreeViewNode RootNode )
        {
            m_rootNode = RootNode;
            m_OPCUAClientAdaptor = opcUAClientAdaptor;
        }

        #endregion

        #region Public Properties
        /// <summary>
        /// defines minor version of target variable
        /// </summary>
        public int MinorVersion
        {
            get { return m_minorVersion; }
            set
            {
                m_minorVersion = value;
                OnPropertyChanged( "MinorVersion" );
            }
        }

        /// <summary>
        /// defines major version of target variable
        /// </summary>
        public int MajorVersion
        {
            get { return m_majorVersion; }
            set
            {
                m_majorVersion = value;
                OnPropertyChanged( "MajorVersion" );
            }
        }

        /// <summary>
        /// defines collection of treenodes in connected server
        /// </summary>
        public ObservableCollection< TreeViewNode > ServerItems
        {
            get { return m_serverItems; }
            set
            {
                m_serverItems = value;
                OnPropertyChanged( "ServerItems" );
            }
        }

        /// <summary>
        /// defines collection of field target variable definition
        /// </summary>
        public ObservableCollection< FieldTargetVariableDefinition > VariableListDefinitionCollection
        {
            get { return m_variableListDefinitionCollection; }
            set
            {
                m_variableListDefinitionCollection = value;
                OnPropertyChanged( "VariableListDefinitionCollection" );
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Method to add Variable to list
        /// </summary>
        /// <param name="_FieldTargetVariableDefinition"></param>
        public void AddVariable( FieldTargetVariableDefinition _FieldTargetVariableDefinition )
        {
            VariableListDefinitionCollection.Add( _FieldTargetVariableDefinition );
        }

        /// <summary>
        /// Initialiser method for TargetVariables.
        /// </summary>
        public void Initialize( )
        {
            if ( m_rootNode != null ) m_rootNode.Header = "Root";
            ServerItems.Clear( );
            ServerItems.Add( m_rootNode );
        }

        /// <summary>
        /// Method to Rebrowse for selected node.
        /// </summary>
        /// <param name="node"></param>
        internal void Rebrowse( ref TreeViewNode node )
        {
            m_OPCUAClientAdaptor.Rebrowse( ref node );
        }

        /// <summary>
        /// Method to remove selected variable from list.
        /// </summary>
        /// <param name="fieldTargetVariableDefinition"></param>
        public void RemoveVariable( FieldTargetVariableDefinition fieldTargetVariableDefinition )
        {
            VariableListDefinitionCollection.Remove( fieldTargetVariableDefinition );
        }

        #endregion
    }
}