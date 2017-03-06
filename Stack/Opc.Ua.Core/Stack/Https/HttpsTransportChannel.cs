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
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
            get { return TransportChannelFeatures.Open | TransportChannelFeatures.Reconnect | TransportChannelFeatures.BeginSendRequest; }
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
            try
            {
                  m_client = new HttpClient();
            }
            catch (Exception ex)
            {
                Utils.Trace("Exception creating HTTPS Client: " + ex.Message);
                throw ex;
            }
        }

        public void Close()
        {
            if (m_client != null)
            {
                m_client.Dispose();
            }
        }

        private class AsyncResult : AsyncResultBase
        {
            public IServiceRequest Request;
            public HttpResponseMessage Response;

            public AsyncResult(
                AsyncCallback callback,
                object callbackData,
                int timeout,
                IServiceRequest request,
                HttpResponseMessage response)
            :
                base(callback, callbackData, timeout)
            {
                Request = request;
                Response = response;
            }
        }

        public IAsyncResult BeginSendRequest(IServiceRequest request, AsyncCallback callback, object callbackData)
        {
            HttpResponseMessage response = null;

            try
            {
                ByteArrayContent content = new ByteArrayContent(BinaryEncoder.EncodeMessage(request, m_quotas.MessageContext));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                Task<HttpResponseMessage> task = m_client.PostAsync(m_url, content);
                task.Wait();
                response = task.Result;

                response.EnsureSuccessStatusCode();

                AsyncResult result = new AsyncResult(callback, callbackData, m_operationTimeout, request, response);
                result.OperationCompleted();
                return result;
            }
            catch (Exception ex)
            {
                Utils.Trace("Exception sending HTTPS request: " + ex.Message);
                AsyncResult result = new AsyncResult(callback, callbackData, m_operationTimeout, request, response);
                result.Exception = ex;
                result.OperationCompleted();
                return result;
            }
        }

        public IServiceResponse EndSendRequest(IAsyncResult result)
        {
            AsyncResult result2 = result as AsyncResult;
            if (result2 == null)
            {
                throw new ArgumentException("Invalid result object passed.", "result");
            }

            try
            {
                result2.WaitForComplete();

                Task<Stream> task = result2.Response.Content.ReadAsStreamAsync();
                task.Wait();
                Stream responseContent = task.Result;

                return BinaryDecoder.DecodeMessage(responseContent, null, m_quotas.MessageContext) as IServiceResponse;
            }
            catch (Exception ex)
            {
                Utils.Trace("Exception reading HTTPS response: " + ex.Message);
                result2.Exception = ex;
                return result2 as IServiceResponse;
            }
        }

        public IAsyncResult BeginOpen(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        public void EndOpen(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public void Reconnect()
        {
            Utils.Trace("HttpsTransportChannel RECONNECT: Reconnecting to {0}.", m_url);
        }

        public IAsyncResult BeginReconnect(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        public void EndReconnect(IAsyncResult result)
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
            IAsyncResult result = BeginSendRequest(request, null, null);
            return EndSendRequest(result);
        }
        
        private void SaveSettings(Uri url, TransportChannelSettings settings)
        {
            m_url = new Uri(Utils.ReplaceLocalhost(url.ToString()));

            m_settings = settings;
            m_operationTimeout = settings.Configuration.OperationTimeout;

            // initialize the quotas.
            m_quotas = new ChannelQuotas();

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

        private Uri m_url;
        private int m_operationTimeout;
        private TransportChannelSettings m_settings;
        private ChannelQuotas m_quotas;
        private HttpClient m_client;
    }
}
