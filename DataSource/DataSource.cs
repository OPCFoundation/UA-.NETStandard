using System.Collections.Generic;
 

namespace DataSource
{
    public delegate void dataReceivedEventHandler(object receivedData);
    public interface IDataSource
    { 
        event dataReceivedEventHandler DataReceived;
        void InitializeReceiveData(bool isEnable);
        bool SendData(byte[] data, Dictionary<string, object> Settings);

        void ReceiveData(string queueName);
    }
    public class BaseDataSource: IDataSource
    {
       
        
        protected bool m_InitializeReceiveData = false;

        public event dataReceivedEventHandler DataReceived;

        public void OnDataReceived(object receivedData)
        {
            dataReceivedEventHandler statusHandler = DataReceived;
            if (statusHandler != null)
            {
                statusHandler(receivedData);
            }
        }
        public void InitializeReceiveData(bool isEnable)
        {
            m_InitializeReceiveData = isEnable;

        }
        public virtual bool SendData(byte[] data, Dictionary<string, object> Settings)
        {
            return true;    
        }
        public virtual void ReceiveData(string queueName)
        {
             
        }
    }
}
