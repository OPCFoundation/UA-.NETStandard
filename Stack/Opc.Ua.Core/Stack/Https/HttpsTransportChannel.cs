/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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

using System;
using System.Net;
using System.IO;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Wraps the HttpsTransportChannel and provides an ITransportChannel implementation.
    /// </summary>
    public class HttpsTransportChannel : ITransportChannel
    {
        public void Dispose()
        {   
        }

        public TransportChannelFeatures SupportedFeatures
        {
            get { return TransportChannelFeatures.Open; }
        }

        public EndpointDescription EndpointDescription
        {
            get { return m_settings.Description; }
        }

        public EndpointConfiguration EndpointConfiguration
        {
            get { return m_settings.Configuration; }
        }

        public ServiceMessageContext MessageContext
        {
            get { return m_quotas.MessageContext; }
        }

        public int OperationTimeout
        {
            get { return m_operationTimeout;  }
            set { m_operationTimeout = value; }
        }

        public void Initialize(
            Uri url,
            TransportChannelSettings settings)
        {
            SaveSettings(url, settings);
        }

        public void Open()
        {
            // nothing to do
        }

        public IAsyncResult BeginOpen(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginSendRequest(IServiceRequest request, AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        public IServiceResponse EndSendRequest(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public void EndOpen(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public void Reconnect()
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginReconnect(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        public void EndReconnect(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginClose(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        public void EndClose(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public IServiceResponse SendRequest(IServiceRequest request)
        {
            HttpWebRequest webRequest = (HttpWebRequest)HttpWebRequest.Create(m_url.ToString());
            webRequest.Method = "POST";
            webRequest.ContentType = "application/octet-stream";
            webRequest.UseDefaultCredentials = true;
            
            Task<Stream> t = Task.Run(() => webRequest.GetRequestStreamAsync());
            t.Wait();
            Stream ostrm = (Stream)t.Result;

            MemoryStream mstrm = new MemoryStream();
            BinaryEncoder encoder = new BinaryEncoder(mstrm, this.MessageContext);
            encoder.EncodeMessage(request);
            
            int bytesToRead = (int)mstrm.Position;
            mstrm.Position = 0;

            int bytesRead = 0;
            int blockSize = 0;
            byte[] buffer = new byte[4096];

            do
            {
                blockSize = mstrm.Read(buffer, 0, buffer.Length);
                bytesRead += blockSize;

                if (bytesRead > bytesToRead)
                {
                    blockSize -= (bytesRead - bytesToRead);
                }

                ostrm.Write(buffer, 0, blockSize);
            }
            while (blockSize >= 0 && bytesRead < bytesToRead);

            ostrm.Dispose();
            mstrm.Dispose();

            Task<WebResponse> t2 = Task.Run(() => webRequest.GetResponseAsync());
            t2.Wait();
            HttpWebResponse response = (HttpWebResponse) t2.Result;

            mstrm = new MemoryStream();

            using (Stream istrm = response.GetResponseStream())
            {
                bytesRead = 0;
                do
                {
                    bytesRead = istrm.Read(buffer, 0, buffer.Length);
                    mstrm.Write(buffer, 0, bytesRead);
                }
                while (bytesRead != 0);
                mstrm.Position = 0;
            }

            IEncodeable message = BinaryDecoder.DecodeMessage(mstrm, null, this.MessageContext);

            return message as IServiceResponse;
        }
        
        private void SaveSettings(Uri url, TransportChannelSettings settings)
        {
            // save the settings.
            m_url = url;
            m_settings = settings;
            m_operationTimeout = settings.Configuration.OperationTimeout;

            // initialize the quotas.
            m_quotas = new TcpChannelQuotas();

            m_quotas.MaxBufferSize = m_settings.Configuration.MaxBufferSize;
            m_quotas.MaxMessageSize = m_settings.Configuration.MaxMessageSize;
            m_quotas.ChannelLifetime = m_settings.Configuration.ChannelLifetime;
            m_quotas.SecurityTokenLifetime = m_settings.Configuration.SecurityTokenLifetime;

            m_quotas.MessageContext = new ServiceMessageContext();

            m_quotas.MessageContext.MaxArrayLength = m_settings.Configuration.MaxArrayLength;
            m_quotas.MessageContext.MaxByteStringLength = m_settings.Configuration.MaxByteStringLength;
            m_quotas.MessageContext.MaxMessageSize = m_settings.Configuration.MaxMessageSize;
            m_quotas.MessageContext.MaxStringLength = m_settings.Configuration.MaxStringLength;
            m_quotas.MessageContext.NamespaceUris = m_settings.NamespaceUris;
            m_quotas.MessageContext.ServerUris = new StringTable();
            m_quotas.MessageContext.Factory = m_settings.Factory;

            m_quotas.CertificateValidator = settings.CertificateValidator;
        }

        private object m_lock = new object();
        private Uri m_url;
        private int m_operationTimeout;
        private TransportChannelSettings m_settings;
        private TcpChannelQuotas m_quotas;
    }
}
