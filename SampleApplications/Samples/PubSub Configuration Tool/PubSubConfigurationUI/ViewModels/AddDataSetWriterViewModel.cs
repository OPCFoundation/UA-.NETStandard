using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class AddDataSetWriterViewModel : BaseViewModel
    {
        #region Private Member 

        private int _dataSetWriterId;
        private int _keyFrameCount;

        //public AddDataSetWriterViewModel(IOPCUAClientAdaptor OPCUAClientAdaptor)
        //{

        //}
        private int _publisherDataSetId;

        #endregion

        #region Public Property

        public int PublisherDataSetId
        {
            get { return _publisherDataSetId; }
            set
            {
                _publisherDataSetId = value;
                OnPropertyChanged( "PublisherDataSetId" );
            }
        }

        public int KeyFrameCount
        {
            get { return _keyFrameCount; }
            set
            {
                _keyFrameCount = value;
                OnPropertyChanged( "KeyFrameCount" );
            }
        }

        public int DataSetWriterId
        {
            get { return _dataSetWriterId; }
            set
            {
                _dataSetWriterId = value;
                OnPropertyChanged( "DataSetWriterId" );
            }
        }

        #endregion
    }
}