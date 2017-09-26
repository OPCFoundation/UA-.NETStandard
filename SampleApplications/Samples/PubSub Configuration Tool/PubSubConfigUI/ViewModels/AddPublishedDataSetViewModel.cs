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
using System.Windows;
using ClientAdaptor;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// View Model definition Published Data set
    /// </summary>
    public class AddPublishedDataSetViewModel : BaseViewModel
    {
        #region Private Fields

        private readonly IOPCUAClientAdaptor m_OPCUAClientAdaptor;
        private Visibility m_publisherNameVisibility = Visibility.Visible;
        private readonly TreeViewNode m_rootNode;
        private ObservableCollection< TreeViewNode > m_serverItems = new ObservableCollection< TreeViewNode >( );
        private ObservableCollection< PublishedDataSetItemDefinition > m_variableListDefinitionCollection =
        new ObservableCollection< PublishedDataSetItemDefinition >( );

        #endregion

        #region Constructors

        /// <summary>
        /// initialising view model with root node and adaptor
        /// </summary> 
        public AddPublishedDataSetViewModel( IOPCUAClientAdaptor opcUAClientAdaptor, TreeViewNode RootNode )
        {
            m_rootNode = RootNode;
            m_OPCUAClientAdaptor = opcUAClientAdaptor;
        }

        #endregion

        #region Public Properties
        /// <summary>
        /// defines visibility of publisher name menu
        /// </summary>
        public Visibility PublisherNameVisibility
        {
            get { return m_publisherNameVisibility; }
            set
            {
                m_publisherNameVisibility = value;
                OnPropertyChanged( "PublisherNameVisibility" );
            }
        }

        /// <summary>
        /// defines collection of nodes of current server
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
        /// defines collection of published data set definitions.
        /// </summary>
        public ObservableCollection< PublishedDataSetItemDefinition > VariableListDefinitionCollection
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
        /// Adding variable to collection
        /// </summary>
       
        public void AddVariable( PublishedDataSetItemDefinition publishedDataSetItemDefinition )
        {
            VariableListDefinitionCollection.Add( publishedDataSetItemDefinition );
        }

        public void Initialize( )
        {
            if ( m_rootNode != null ) m_rootNode.Header = "Root";
            ServerItems.Clear( );
            ServerItems.Add( m_rootNode );
        }

        /// <summary>
        /// Rebrowse selected node.
        /// </summary>
        /// <param name="node"></param>
        internal void Rebrowse( ref TreeViewNode node )
        {
            m_OPCUAClientAdaptor.Rebrowse( ref node );
        }

        /// <summary>
        /// Remove selected variable from collection
        /// </summary> 
        public void RemoveVariable( PublishedDataSetItemDefinition _PublishedDataSetItemDefinition )
        {
            VariableListDefinitionCollection.Remove( _PublishedDataSetItemDefinition );
        }

        #endregion
    }
}