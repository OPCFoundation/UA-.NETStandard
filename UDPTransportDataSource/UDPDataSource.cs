using DataSource;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace UDPTransportDataSource
{
    public class UDPDataSource: BaseDataSource
    { 
        #region Private Methods

        private void StartListening()
        {
            ar_ = m_UdpClient.BeginReceive(Receive, new object());
        }
        private void Receive(IAsyncResult ar)
        {

            byte[] recvBuffer = m_UdpClient.EndReceive(ar, ref m_IPEndPoint);
            try
            {
                if (m_InitializeReceiveData)
                {
                    OnDataReceived(recvBuffer);
                }
            }
            catch (Exception ex)
            {

            }
            Thread.Sleep(10);
            StartListening();
        }

        #endregion

        #region Public Methods

        public bool Initialize(string format, string address)
        {
            try
            {
                   
                try
                {
                    string[] addressarray = address.Split(':');

                    string Address = addressarray[1].Replace("/", string.Empty);
                    if (Address.ToLower() == "localhost")
                    {
                        Address = "127.0.0.1";
                    }
                    string port = addressarray[2];

                    System.Net.IPAddress IPAddress;
                     System.Net.IPAddress.TryParse(Address, out IPAddress);

                    m_IPEndPoint = new IPEndPoint(IPAddress, Convert.ToInt32(port));
                }
                catch(Exception ex)
                {

                }
                m_UdpClient = new UdpClient(); 
                
                return true;
            }
            catch(Exception ex)
            {
                 
            }
            return false;
        }
        public override bool SendData(byte[] data, Dictionary<string, object> settings)
        {
            try
            {
                 
                if (m_UdpClient != null)
                {

                    m_UdpClient.Send(data, data.Length, m_IPEndPoint);
                    
                    return true;
                }
            }
            catch(Exception ex)
            {
                return false;
            }
            return false;
        }
        IAsyncResult ar_ = null;
        public override void ReceiveData(string queueName)
        {
            try
            {
                if (!m_InitializeReceiveData)
                {
                    m_UdpClient.Client.Bind(m_IPEndPoint);
                    StartListening();
                }
                InitializeReceiveData(true);
                
            }
            catch(Exception ex)
            {

            }
        }

        #endregion

        #region Private Fields
        
        IPEndPoint m_IPEndPoint = null;
        UdpClient m_UdpClient = null;
        #endregion
    }
}
