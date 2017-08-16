using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class PublishedDataSetEditViewModel : BaseViewModel
    {
        #region Private Member 

        private string _DataSetNodeId = string.Empty;
        private PublishedDataSetDefinition _Definition;
        private uint _MaxVersion;
        private uint _MinVersion;

        #endregion

        #region Constructors

        public PublishedDataSetEditViewModel( )
        {
            Definition = new PublishedDataSetDefinition( );
            //PublishedDataSetCollection = new ObservableCollection<PublishedDataSetBase>();
        }

        #endregion

        #region Public Property

        public PublishedDataSetDefinition Definition
        {
            get { return _Definition; }
            set
            {
                _Definition = value;
                OnPropertyChanged( "Definition" );
            }
        }

        public string DataSetNodeId
        {
            get { return _DataSetNodeId; }
            set
            {
                _DataSetNodeId = value;
                OnPropertyChanged( "DataSetNodeId" );
            }
        }

        public uint MinVersion
        {
            get { return _MinVersion; }
            set
            {
                _MinVersion = value;
                OnPropertyChanged( "MinVersion" );
            }
        }

        public uint MaxVersion
        {
            get { return _MaxVersion; }
            set
            {
                _MaxVersion = value;
                OnPropertyChanged( "MaxVersion" );
            }
        }

        #endregion

        #region Public Methods

        public void Initialize( )
        {
            DataSetNodeId = Definition.DataSetNodeId;
            MinVersion = Definition.ConfigurationVersionDataType.MinorVersion;
            MaxVersion = Definition.ConfigurationVersionDataType.MajorVersion;
        }

        #endregion
    }
}