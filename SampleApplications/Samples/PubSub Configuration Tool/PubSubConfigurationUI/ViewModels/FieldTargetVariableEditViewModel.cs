using System.ComponentModel;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.Views;

namespace PubSubConfigurationUI.ViewModels
{
    public class FieldTargetVariableEditViewModel : BaseViewModel
    {
        #region Private Member 

        private uint _AttributeId = 13;
        private string _DataSetFieldId = string.Empty;
        private FieldTargetVariableDefinition _Definition;
        private string _name = string.Empty;
        private object _OverrideValue;
        private int _OverrideValueHandling = 1;
        private string _ReceiverIndexRange = string.Empty;
        private NodeId _TargetFieldNodeId = string.Empty;
        private string _TargetNodeId = string.Empty;
        private string _WriteIndexRange = string.Empty;

        #endregion

        #region Private Methods

        private void _GetDataSetFieldIdDialog_Closing( object sender, CancelEventArgs e )
        {
            var _GetDataSetFieldIdDialog = sender as GetDataSetFieldIdDialog;
            if ( _GetDataSetFieldIdDialog._isApplied )
                if ( _GetDataSetFieldIdDialog.FieldIdGrid.SelectedItem != null )
                {
                    var metadata = _GetDataSetFieldIdDialog.FieldIdGrid.SelectedItem as FieldMetaDataDefinition;
                    DataSetFieldId = metadata.DataSetFieldId;
                }
        }

        #endregion

        #region Constructors

        public FieldTargetVariableEditViewModel( )
        {
            Definition = new FieldTargetVariableDefinition( );
            //PublishedDataSetCollection = new ObservableCollection<PublishedDataSetBase>();
        }

        #endregion

        #region Public Property

        public DataSetMetaDataType DataSetMetaDataType { get; set; }

        public FieldTargetVariableDefinition Definition
        {
            get { return _Definition; }
            set
            {
                _Definition = value;
                OnPropertyChanged( "Definition" );
            }
        }

        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                OnPropertyChanged( "Name" );
            }
        }

        public string DataSetFieldId
        {
            get { return _DataSetFieldId; }
            set
            {
                _DataSetFieldId = value;
                OnPropertyChanged( "DataSetFieldId" );
            }
        }

        public string ReceiverIndexRange
        {
            get { return _ReceiverIndexRange; }
            set
            {
                _ReceiverIndexRange = value;
                OnPropertyChanged( "ReceiverIndexRange" );
            }
        }

        public string TargetNodeId
        {
            get { return _TargetNodeId; }
            set
            {
                _TargetNodeId = value;
                OnPropertyChanged( "TargetNodeId" );
            }
        }

        public NodeId TargetFieldNodeId
        {
            get { return _TargetFieldNodeId; }
            set
            {
                _TargetFieldNodeId = value;
                OnPropertyChanged( "TargetFieldNodeId" );
            }
        }

        public uint AttributeId
        {
            get { return _AttributeId; }
            set
            {
                _AttributeId = value;
                OnPropertyChanged( "AttributeId" );
            }
        }

        public string WriteIndexRange
        {
            get { return _WriteIndexRange; }
            set
            {
                _WriteIndexRange = value;
                OnPropertyChanged( "WriteIndexRange" );
            }
        }

        public int OverrideValueHandling
        {
            get { return _OverrideValueHandling; }
            set
            {
                _OverrideValueHandling = value;
                OnPropertyChanged( "OverrideValueHandling" );
            }
        }

        public object OverrideValue
        {
            get { return _OverrideValue; }
            set
            {
                _OverrideValue = value;
                OnPropertyChanged( "OverrideValue" );
            }
        }

        #endregion

        #region Public Methods

        public void GetDataSetFieldId( )
        {
            var _GetDataSetFieldIdDialog = new GetDataSetFieldIdDialog( DataSetMetaDataType );
            _GetDataSetFieldIdDialog.Closing += _GetDataSetFieldIdDialog_Closing;
            _GetDataSetFieldIdDialog.ShowInTaskbar = false;
            _GetDataSetFieldIdDialog.ShowDialog( );
        }

        public void Initialize( )
        {
            Name = Definition.Name;
            DataSetFieldId = Definition.DataSetFieldId;
            ReceiverIndexRange = Definition.ReceiverIndexRange;
            TargetNodeId = Definition.TargetNodeId;
            TargetFieldNodeId = Definition.TargetNodeId;
            AttributeId = Definition.AttributeId;
            WriteIndexRange = Definition.WriteIndexRange;
            OverrideValueHandling = Definition.OverrideValueHandling;
            OverrideValue = Definition.OverrideValue;
        }

        #endregion
    }
}