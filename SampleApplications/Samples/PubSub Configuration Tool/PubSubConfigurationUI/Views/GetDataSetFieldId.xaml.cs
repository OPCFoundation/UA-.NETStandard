using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using NLogManager;
using Opc.Ua;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for GetPublisherIdDialog.xaml
    /// </summary>
    public partial class GetDataSetFieldIdDialog : Window
    {
        #region Private Member 

        private readonly DataSetMetaDataType _dataSetMetaDataType = new DataSetMetaDataType( );

        #endregion

        #region Private Methods

        private void GetPublisherIdDialog_Loaded( object sender, RoutedEventArgs e )
        {
            foreach ( var fieldMetaData in _dataSetMetaDataType.Fields )
            {
                var fieldMetaDataDefinition = new FieldMetaDataDefinition( );
                fieldMetaDataDefinition.ArrayDimensions = fieldMetaData.ArrayDimensions;
                fieldMetaDataDefinition.BuildInType = fieldMetaData.BuiltInType;
                fieldMetaDataDefinition.DataSetFieldFlags = fieldMetaData.FieldFlags;
                fieldMetaDataDefinition.DataSetFieldId = fieldMetaData.DataSetFieldId.GuidString;
                fieldMetaDataDefinition.Description = fieldMetaData.Description.Text;
                fieldMetaDataDefinition.Name = fieldMetaData.Name;
                fieldMetaDataDefinition.DataTypeId = fieldMetaData.DataType.ToString( );
                try
                {
                    fieldMetaDataDefinition.ValueRank = GetConstants( typeof( ValueRanks ) )
                    .Where( i => Convert.ToInt16( i.GetValue( i ) ) == fieldMetaData.ValueRank ).FirstOrDefault( )
                    .Name;
                }
                catch ( Exception ex )
                {
                    Log.ErrorException( "GetDataSetFieldIdDialog:GetPublisherIdDialog_Loaded", ex );
                }

                FieldIdGrid.Items.Add( fieldMetaDataDefinition );
            }
        }

        private List< FieldInfo > GetConstants( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.Where( fi => fi.IsLiteral && !fi.IsInitOnly ).ToList( );
        }

        private void OnApply_Click( object sender, RoutedEventArgs e )
        {
            if ( FieldIdGrid.SelectedItem == null )
            {
                MessageBox.Show( "No Field Ids are selected", "Get DataSet Field Id" );
                return;
            }
            _isApplied = true;
            Close( );
        }

        private void OnCancel_Click( object sender, RoutedEventArgs e )
        {
            _isApplied = false;
            Close( );
        }

        #endregion

        #region Constructors

        public GetDataSetFieldIdDialog( DataSetMetaDataType dataSetMetaDataType )
        {
            InitializeComponent( );
            _dataSetMetaDataType = dataSetMetaDataType;
            Loaded += GetPublisherIdDialog_Loaded;
        }

        #endregion

        public bool _isApplied;
    }
}