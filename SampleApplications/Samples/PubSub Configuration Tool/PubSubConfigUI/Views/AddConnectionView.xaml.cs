using System;
using System.Windows;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddConnection.xaml
    /// </summary>
    public partial class AddConnectionView : Window
    {
        #region Event Handlers

        private void OnApplyClick( object sender, RoutedEventArgs e )
        {
            if ( string.IsNullOrWhiteSpace( PublisherIdTxt.Text ) )
            {
                MessageBox.Show( "Publisher Id cannot be empty","Add Connection");
                return;
            }
            if ( string.IsNullOrWhiteSpace( ConnectionNameTxt.Text ) )
            {
                MessageBox.Show( "Connection name cannot be empty","Add Connection");
                return;
            }
            if ( string.IsNullOrWhiteSpace( AddressTxt.Text ) )
            {
                MessageBox.Show( "Address cannot be empty", "Add Connection");
                return;
            }
            ConnectionName = ConnectionNameTxt.Text;
            Address = AddressTxt.Text;
            if ( PublisherDataType.SelectedIndex == 0 )
            {
                PublisherId = PublisherIdTxt.Text;
            }
            else if ( PublisherDataType.SelectedIndex == 1 )
            {
                byte PublisherIdbyteType;
                if ( !byte.TryParse( PublisherIdTxt.Text, out PublisherIdbyteType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType.", "Add Connection" );
                    return;
                }
                PublisherId = PublisherIdbyteType;
            }
            else if ( PublisherDataType.SelectedIndex == 2 )
            {
                ushort PublisherIdType;
                if ( !ushort.TryParse( PublisherIdTxt.Text, out PublisherIdType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType.", "Add Connection" );
                    return;
                }
                PublisherId = PublisherIdType;
            }
            else if ( PublisherDataType.SelectedIndex == 3 )
            {
                uint PublisherIdType;
                if ( !uint.TryParse( PublisherIdTxt.Text, out PublisherIdType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType.", "Add Connection" );
                    return;
                }
                PublisherId = PublisherIdType;
            }
            else if ( PublisherDataType.SelectedIndex == 4 )
            {
                ulong PublisherIdType;
                if ( !ulong.TryParse( PublisherIdTxt.Text, out PublisherIdType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType.", "Add Connection" );
                    return;
                }
                PublisherId = PublisherIdType;
            }
            else if ( PublisherDataType.SelectedIndex == 5 )
            {
                Guid PublisherIdType;

                if ( !Guid.TryParse( PublisherIdTxt.Text, out PublisherIdType ) )
                {
                    MessageBox.Show( "Publisher Id value doesn't match for the selected DataType.", "Add Connection" );
                    return;
                }
                PublisherId = PublisherIdType;
            }

            ConnectionType = ConnectionTypeCmb.SelectedIndex;
            if ( string.IsNullOrWhiteSpace( ConnectionNameTxt.Text ) )
            {
                MessageBox.Show( "Connection Name cannot be empty.", "Add Connection" );
                return;
            }
            IsApplied = true;
            Close( );
        }

        private void OnCancelClick( object sender, RoutedEventArgs e )
        {
            IsApplied = false;
            Close( );
        }

        #endregion

        #region Constructors

        public AddConnectionView( )
        {
            InitializeComponent( );
        }

        #endregion

        public bool IsApplied;
        public string ConnectionName = string.Empty;
        public string Address = string.Empty;
        public object PublisherId;
        public int ConnectionType;
    }
}