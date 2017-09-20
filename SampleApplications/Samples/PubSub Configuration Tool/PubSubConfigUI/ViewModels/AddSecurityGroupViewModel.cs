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
    /// defines viewmodel for security group tab
    /// </summary>
    public class SecurityGroupViewModel : BaseViewModel
    {
        #region Private Fields 

        private readonly IOPCUAClientAdaptor m_clientAdaptor;
        private Visibility m_isCancelVisible = Visibility.Collapsed;
        private Visibility m_isSecurityAddGroupVisible = Visibility.Visible;
        private Visibility m_isSecurityAddKeysVisible = Visibility.Collapsed;
        private Visibility m_isSecurityGroupRemoveVisible = Visibility.Collapsed;
        private Visibility m_isUpdateVisible = Visibility.Collapsed;
        private ObservableCollection< SecurityBase > m_securityCollection;

        #endregion

        #region Constructors
        /// <summary>
        /// initialise security group with adaptor
        /// </summary> 
        public SecurityGroupViewModel( IOPCUAClientAdaptor OPCUAClientAdaptor )
        {
            m_clientAdaptor = OPCUAClientAdaptor;
            SecurityCollection = new ObservableCollection< SecurityBase >( );
        }

        #endregion

        #region Public Properties
        /// <summary> 
        /// defines visibility for security add group menu
        /// </summary>
        public Visibility IsSecurityAddGroupVisible
        {
            get { return m_isSecurityAddGroupVisible; }
            set
            {
                m_isSecurityAddGroupVisible = value;
                OnPropertyChanged( "IsSecurityAddGroupVisible" );
            }
        }

        /// <summary> 
        /// defines visibility for update security menu
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
        /// defines visibility for Cancel menu
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
        /// defines visibility for Security Add keys menu
        /// </summary>
        public Visibility IsSecurityAddKeysVisible
        {
            get { return m_isSecurityAddKeysVisible; }
            set
            {
                m_isSecurityAddKeysVisible = value;
                OnPropertyChanged( "IsSecurityAddKeysVisible" );
            }
        }
        /// <summary>
        /// defines visibility for security group remove menu 
        /// </summary>
        public Visibility IsSecurityGroupRemoveVisible
        {
            get { return m_isSecurityGroupRemoveVisible; }
            set
            {
                m_isSecurityGroupRemoveVisible = value;
                OnPropertyChanged( "IsSecurityGroupRemoveVisible" );
            }
        }
        /// <summary>
        /// defines collection of security base 
        /// </summary>
        public ObservableCollection< SecurityBase > SecurityCollection
        {
            get { return m_securityCollection; }
            set 
            {
                m_securityCollection = value;
                OnPropertyChanged( "SecurityCollection" );
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Add new Security group
        /// </summary>
        /// <param name="name">name of the security group</param>
         
        public bool AddSecurityGroup( string name )
        {
            SecurityGroup _SecurityGroup;
            var errorMessage = m_clientAdaptor.AddNewSecurityGroup( name, out _SecurityGroup );
            if ( _SecurityGroup != null )
            {
                SecurityCollection.Add( _SecurityGroup );
            }
            else
            {
                MessageBox.Show( errorMessage, "Add Security Group" );
                return false;
            }
            return true;
        }

        /// <summary>
        /// Method to initialize security groups.
        /// </summary>
        /// <returns></returns>
        public ObservableCollection< SecurityGroup > InitailizeSecurityGroups( )
        {
            SecurityCollection.Clear( );
            var securityGroups = m_clientAdaptor.GetSecurityGroups( );
            foreach ( var securityGroup in securityGroups ) SecurityCollection.Add( securityGroup );
            return securityGroups;
        }

        public void Initialize( )
        {
            InitailizeSecurityGroups( );
            foreach ( SecurityGroup group in SecurityCollection )
            {
                SecurityKeys _SecurityKeys;
                m_clientAdaptor.GetSecurityKeys( group.SecurityGroupId, uint.MaxValue, out _SecurityKeys );
                if ( _SecurityKeys != null )
                {
                    _SecurityKeys.ParentNode = group;
                    group.Children.Add( _SecurityKeys );
                }
            }
        }

        /// <summary>
        /// Method to remove selected security group.
        /// </summary>
        public void RemoveSecurityGroup( SecurityGroup SecurityGroup )
        {
            var errorMessage = m_clientAdaptor.RemoveSecurityGroup( SecurityGroup.GroupNodeId );

            if ( string.IsNullOrWhiteSpace( errorMessage ) ) SecurityCollection.Remove( SecurityGroup );
            else MessageBox.Show( errorMessage );
        }

        /// <summary>
        /// Method to set Security keys.
        /// </summary>
        public void SetSecurityKeys( SecurityBase securityBase, SecurityKeys securitykeys )
        {
            var errmsg = m_clientAdaptor.SetSecurityKeys( securitykeys );
            if ( !string.IsNullOrWhiteSpace( errmsg ) )
            {
                MessageBox.Show( errmsg, "Set Security keys" );
                return;
            }
            securityBase.Children.Clear( );
            securityBase.Children.Add( securitykeys );
        }

        #endregion
    }
}