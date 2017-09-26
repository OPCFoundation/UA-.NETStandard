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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using ClientAdaptor;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.Views;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for published data set main view
    /// </summary>
    public class PublishedDataSetMainViewModel : BaseViewModel
    {
        #region Private Fields 

        private readonly IOPCUAClientAdaptor m_clientAdaptor;
        private Visibility m_isAddEventsVisible = Visibility.Collapsed;
        private Visibility m_isAddVariableVisible = Visibility.Collapsed;
        private Visibility m_isCancelVisible = Visibility.Collapsed;
        private Visibility m_isPublishedDataSetVisible = Visibility.Collapsed;
        private Visibility m_isPublishedEventsVisible = Visibility.Collapsed;
        private Visibility m_isRemoveEventsVisible = Visibility.Collapsed;
        private Visibility m_isRemovePublishedDataSetVisible = Visibility.Collapsed;
        private Visibility m_isRemovePublishedEventsVisible = Visibility.Collapsed;
        private Visibility m_isRemoveVariablesVisible = Visibility.Collapsed;
        private Visibility m_isRemoveVariableVisible = Visibility.Collapsed;
        private Visibility m_isUpdateVisible = Visibility.Collapsed;

        private ObservableCollection< PublishedDataSetBase > m_publishedDataSetCollection =
        new ObservableCollection< PublishedDataSetBase >( );

        private PublishedDataDefinition m_addPublisherVaraibleParentNode;
        private TreeViewNode m_rootNode;

        #endregion

        #region Private Methods

        private void AddPublishedDataView_Closing( object sender, CancelEventArgs e )
        {
            var addPublishedData = sender as AddPublishedDataSetDialog;
            if ( addPublishedData._isApplied )
            {
                var publishedDataSetBase =
                m_clientAdaptor.AddPublishedDataSet( addPublishedData.PublisherName.Text,
                                                    addPublishedData
                                                    .AddPublishedDataSetViewModel.VariableListDefinitionCollection );
                if (publishedDataSetBase != null ) PublishedDataSetCollection.Add(publishedDataSetBase);
            }
        }

        private void AddVariableDataView_Closing( object sender, CancelEventArgs e )
        {
            var addPublishedData = sender as AddPublishedDataSetDialog;
            if ( addPublishedData._isApplied )
            {
                var publishedDataSetDefinition =
                m_addPublisherVaraibleParentNode.ParentNode as PublishedDataSetDefinition;
                ConfigurationVersionDataType newConfigurationVersion;
                var errMsg =
                m_clientAdaptor.AddVariableToPublisher( publishedDataSetDefinition.Name,
                                                       publishedDataSetDefinition.PublishedDataSetNodeId,
                                                       publishedDataSetDefinition.ConfigurationVersionDataType,
                                                       addPublishedData
                                                       .AddPublishedDataSetViewModel.VariableListDefinitionCollection,
                                                       out newConfigurationVersion );
                if ( !string.IsNullOrWhiteSpace( errMsg ) )
                {
                    MessageBox.Show( errMsg, "Add Variables" );
                    addPublishedData._isApplied = false;
                    e.Cancel = true;
                    return;
                }
                (m_addPublisherVaraibleParentNode.ParentNode as PublishedDataSetDefinition).ConfigurationVersionDataType =
                newConfigurationVersion;
            }
        }

        #endregion

        #region Constructors

        public PublishedDataSetMainViewModel( IOPCUAClientAdaptor OPCUAClientAdaptor )
        {
            m_clientAdaptor = OPCUAClientAdaptor;
            PublishedDataSetCollection = new ObservableCollection< PublishedDataSetBase >( );
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsPublishedDataSetVisible
        {
            get { return m_isPublishedDataSetVisible; }
            set
            {
                m_isPublishedDataSetVisible = value;
                OnPropertyChanged( "IsPublishedDataSetVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsPublishedEventsVisible
        {
            get { return m_isPublishedEventsVisible; }
            set
            {
                m_isPublishedEventsVisible = value;
                OnPropertyChanged( "IsPublishedEventsVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsAddVariableVisible
        {
            get { return m_isAddVariableVisible; }
            set
            {
                m_isAddVariableVisible = value;
                OnPropertyChanged( "IsAddVariableVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemoveVariablesVisible
        {
            get { return m_isRemoveVariablesVisible; }
            set
            {
                m_isRemoveVariablesVisible = value;
                OnPropertyChanged( "IsRemoveVariablesVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsUpdateVisible
        {
            get { return m_isUpdateVisible; }
            set
            {
                m_isUpdateVisible = value;
                OnPropertyChanged( "IsUpdateVisible" );
            }
        }
        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsCancelVisible
        {
            get { return m_isCancelVisible; }
            set
            {
                m_isCancelVisible = value;
                OnPropertyChanged( "IsCancelVisible" );
            }
        }
        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemoveVariableVisible
        {
            get { return m_isRemoveVariableVisible; }
            set
            {
                m_isRemoveVariableVisible = value;
                OnPropertyChanged( "IsRemoveVariableVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemovePublishedDataSetVisible
        {
            get { return m_isRemovePublishedDataSetVisible; }
            set
            {
                m_isRemovePublishedDataSetVisible = value;
                OnPropertyChanged( "IsRemovePublishedDataSetVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsAddEventsVisible
        {
            get { return m_isAddEventsVisible; }
            set
            {
                m_isAddEventsVisible = value;
                OnPropertyChanged( "IsAddEventsVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemoveEventsVisible
        {
            get { return m_isRemoveEventsVisible; }
            set
            {
                m_isRemoveEventsVisible = value;
                OnPropertyChanged( "IsRemoveEventsVisible" );
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsRemovePublishedEventsVisible
        {
            get { return m_isRemovePublishedEventsVisible; }
            set
            {
                m_isRemovePublishedEventsVisible = value;
                OnPropertyChanged( "IsRemovePublishedEventsVisible" );
            }
        }
        /// <summary>
        /// defines collection of published data set
        /// </summary>
        public ObservableCollection< PublishedDataSetBase > PublishedDataSetCollection
        {
            get { return m_publishedDataSetCollection; }
            set
            {
                m_publishedDataSetCollection = value;
                OnPropertyChanged( "PublishedDataSetCollection" );
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Method to add new published DataSet.
        /// </summary>
        public void AddPublishedDataSet( )
        {
            var addPublishedData = new AddPublishedDataSetDialog( m_clientAdaptor, m_rootNode, Visibility.Visible );
            addPublishedData.Closing += AddPublishedDataView_Closing;
            addPublishedData.PubDataSetUserControl.PublishedDataItemTxt.Visibility = Visibility.Collapsed;
            
            addPublishedData.ShowInTaskbar = false;
            addPublishedData.ShowDialog( );
        }

        /// <summary>
        /// Method to add new Variable to DataSet
        /// </summary>
        /// <param name="ParentNode"></param>
        public void AddVariable( PublishedDataDefinition ParentNode )
        {
            var addPublishedData = new AddPublishedDataSetDialog( m_clientAdaptor, m_rootNode, Visibility.Collapsed );
            addPublishedData.Closing += AddVariableDataView_Closing;
            m_addPublisherVaraibleParentNode = ParentNode;
            addPublishedData.ShowDialog( );
        }

        /// <summary>
        /// Initialiser method for Published DataSet
        /// </summary>
        /// <param name="rootNode"></param>
        public void Initialize( TreeViewNode rootNode )
        {
            m_rootNode = rootNode;
            //Browse and load the user interface here.
            PublishedDataSetCollection = m_clientAdaptor.GetPublishedDataSets( );
        }

        /// <summary>
        /// Method to remove selected Published DataSet
        /// </summary>
        /// <param name="_PublishedDataSetDefinition"></param>
        public void RemovePublishedDataSet( PublishedDataSetDefinition _PublishedDataSetDefinition )
        {
            var errMessage =
            m_clientAdaptor.RemovePublishedDataSet( _PublishedDataSetDefinition.PublishedDataSetNodeId );
            if ( !string.IsNullOrWhiteSpace( errMessage ) )
            {
                MessageBox.Show( errMessage, "Remove Published DataSet" );
                return;
            }
            PublishedDataSetCollection.Remove( _PublishedDataSetDefinition );
        }

        /// <summary>
        /// Method to remove variables
        /// </summary>
        /// <param name="_PublishedDataSetBase"></param>
        /// <param name="_PublishedDataSetDefinition"></param>
        /// <param name="ConfigurationVersionDataType"></param>
        /// <param name="variableIndexs"></param>
        public void RemoveVariables(
            PublishedDataSetBase _PublishedDataSetBase, PublishedDataSetDefinition _PublishedDataSetDefinition,
            ConfigurationVersionDataType ConfigurationVersionDataType, List< uint > variableIndexs )
        {
            ConfigurationVersionDataType newConfigurationVersion;
            var errmsg =
            m_clientAdaptor.RemovePublishedDataSetVariables( _PublishedDataSetDefinition.Name,
                                                            _PublishedDataSetDefinition.PublishedDataSetNodeId,
                                                            ConfigurationVersionDataType, variableIndexs,
                                                            out newConfigurationVersion );

            if ( !string.IsNullOrWhiteSpace( errmsg ) )
            {
                MessageBox.Show( errmsg, "Remove Variables" );
                return;
            }
            foreach ( var index in variableIndexs ) _PublishedDataSetBase.Children.RemoveAt( ( int ) index );
            _PublishedDataSetDefinition.ConfigurationVersionDataType = newConfigurationVersion;
        }

        #endregion
    }
}