/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Text;
using System.Net;
using System.IO;
using System.Xml;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Wraps the HttpsTransportChannel and provides an ITransportChannel implementation.
    /// </summary>
    public class HttpsTransportChannel : ITransportChannel
    {
        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // nothing to do.
            }
        }
        #endregion

        #region ITransportChannel Members
        /// <summary>
        /// A masking indicating which features are implemented.
        /// </summary>
        public TransportChannelFeatures SupportedFeatures
        {
            get { return TransportChannelFeatures.Open | TransportChannelFeatures.BeginOpen | TransportChannelFeatures.Reconnect | TransportChannelFeatures.BeginSendRequest; }
        }

        /// <summary>
        /// Gets the description for the endpoint used by the channel.
        /// </summary>
        public EndpointDescription EndpointDescription
        {
            get { return m_settings.Description; }
        }

        /// <summary>
        /// Gets the configuration for the channel.
        /// </summary>
        public EndpointConfiguration EndpointConfiguration
        {
            get { return m_settings.Configuration; }
        }

        /// <summary>
        /// Gets the context used when serializing messages exchanged via the channel.
        /// </summary>
        public ServiceMessageContext MessageContext
        {
            get { return m_quotas.MessageContext; }
        }

        /// <summary>
        /// Gets or sets the default timeout for requests send via the channel.
        /// </summary>
        public int OperationTimeout
        {
            get { return m_operationTimeout;  }
            set { m_operationTimeout = value; }
        }

        /// <summary>
        /// Initializes a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <param name="url">The URL for the endpoint.</param>
        /// <param name="settings">The settings to use when creating the channel.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Initialize(
            Uri url,
            TransportChannelSettings settings)
        {
            SaveSettings(url, settings);
        }

        /// <summary>
        /// Opens a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Open()
        {
            // opens when the first request is called to preserve previous behavoir.
        }

        /// <summary>
        /// Begins an asynchronous operation to open a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>
        /// The result which must be passed to the EndOpen method.
        /// </returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Open"/>
        public IAsyncResult BeginOpen(AsyncCallback callback, object callbackData)
        {
            return new AsyncResultBase(callback, callbackData, m_operationTimeout);
        }

        /// <summary>
        /// Completes an asynchronous operation to open a secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginOpen call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Open"/>
        public void EndOpen(IAsyncResult result)
        {
        }

        /// <summary>
        /// Closes any existing secure channel and opens a new one.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <remarks>
        /// Calling this method will cause outstanding requests over the current secure channel to fail.
        /// </remarks>
        public void Reconnect()
        {
            Utils.Trace("HttpsTransportChannel RECONNECT: Reconnecting to {0}.", m_url);
        }

        /// <summary>
        /// Begins an asynchronous operation to close the existing secure channel and open a new one.
        /// </summary>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>
        /// The result which must be passed to the EndReconnect method.
        /// </returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Reconnect"/>
        public IAsyncResult BeginReconnect(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Completes an asynchronous operation to close the existing secure channel and open a new one.
        /// </summary>
        /// <param name="result">The result returned from the BeginReconnect call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Reconnect"/>
        public void EndReconnect(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Closes the secure channel.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Close()
        {
        }

        /// <summary>
        /// Begins an asynchronous operation to close the secure channel.
        /// </summary>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>
        /// The result which must be passed to the EndClose method.
        /// </returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Close"/>
        public IAsyncResult BeginClose(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Completes an asynchronous operation to close the secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginClose call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Close"/>
        public void EndClose(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Sends a request over the secure channel.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <returns>The response returned by the server.</returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public IServiceResponse SendRequest(IServiceRequest request)
        {
            IAsyncResult result = BeginSendRequest(request, null, null);
            return EndSendRequest(result);
        }

        /// <summary>
        /// Stores the results for an operation.
        /// </summary>
        private class AsyncResult : AsyncResultBase
        {
            public HttpWebRequest WebRequest;
            public IServiceRequest Request;

            public AsyncResult(
                AsyncCallback callback,
                object callbackData,
                int timeout,
                IServiceRequest request,
                HttpWebRequest webRequest)
            :
                base(callback, callbackData, timeout)
            {
                Request = request;
                WebRequest = webRequest;
            }
        }

        /// <summary>
        /// Begins an asynchronous operation to send a request over the secure channel.
        /// </summary>
        public IAsyncResult BeginSendRequest(IServiceRequest request, AsyncCallback callback, object callbackData)
        {
            HttpWebRequest webRequest = (HttpWebRequest)HttpWebRequest.Create(m_url.ToString());
            webRequest.Method = "POST";

            if (m_settings.Configuration.UseBinaryEncoding)
            {
                webRequest.ContentType = "application/octet-stream";
            }
            else
            {
                StringBuilder contentType = new StringBuilder();
                contentType.Append("application/soap+xml; charset=\"utf-8\"; action=\"");
                contentType.Append(Namespaces.OpcUaWsdl);
                contentType.Append("/");

                string typeName = request.GetType().Name;
                int index = typeName.LastIndexOf("Request");
                contentType.Append(typeName.Substring(0, index)); ;
                contentType.Append("\"");

                webRequest.ContentType = contentType.ToString();
            }

            AsyncResult result = new AsyncResult(callback, callbackData, m_operationTimeout, request, webRequest);
            webRequest.BeginGetRequestStream(OnGetRequestStreamComplete, result);
            return result;
        }

        /// <summary>
        /// Writes a message in SOAP/XML.
        /// </summary>
        public static void WriteSoapMessage(
            Stream ostrm, 
            string typeName, 
            IEncodeable message, 
            ServiceMessageContext messageContext)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = System.Text.Encoding.UTF8;
            settings.OmitXmlDeclaration = false;

            XmlWriter writer = XmlWriter.Create(ostrm, settings);
            writer.WriteStartElement("soap12", "Envelope", "http://www.w3.org/2003/05/soap-envelope");

            XmlEncoder encoder = new XmlEncoder(
                new XmlQualifiedName("Body", "http://www.w3.org/2003/05/soap-envelope"),
                writer,
                messageContext);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteEncodeable(
                typeName,
                message,
                null);

            encoder.PopNamespace();

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.Flush();
        }

        /// <summary>
        /// Read a message in SOAP/XML.
        /// </summary>
        public static IEncodeable ReadSoapMessage(
            Stream istrm,
            string typeName,
            Type messageType,
            ServiceMessageContext messageContext)
        {
            XmlReader reader = XmlReader.Create(istrm);
            reader.MoveToContent();
            reader.ReadStartElement("Envelope", "http://www.w3.org/2003/05/soap-envelope");
            reader.MoveToContent();

            while (!reader.IsStartElement("Body", "http://www.w3.org/2003/05/soap-envelope"))
            {
                reader.Skip();
            }

            XmlDecoder decoder = new XmlDecoder(null, reader, messageContext);

            decoder.PushNamespace(Namespaces.OpcUaXsd);
            IEncodeable message = decoder.ReadEncodeable(typeName, messageType);
            decoder.PopNamespace();

            reader.ReadEndElement();
            reader.ReadEndElement();
            reader.Dispose();

            return message;
        }

        /// <summary>
        /// Completes an asynchronous operation to send a request over the secure channel.
        /// </summary>
        public void OnGetRequestStreamComplete(IAsyncResult result)
        {
            AsyncResult result2 = result.AsyncState as AsyncResult;

            if (result2 == null)
            {
                return;
            }

            try
            {
                Stream ostrm = result2.WebRequest.EndGetRequestStream(result);

                MemoryStream mstrm = new MemoryStream();

                if (m_settings.Configuration.UseBinaryEncoding)
                {
                    BinaryEncoder encoder = new BinaryEncoder(mstrm, this.MessageContext);
                    encoder.EncodeMessage(result2.Request);
                }
                else
                {
                    WriteSoapMessage(
                        mstrm, 
                        result2.Request.GetType().Name,
                        result2.Request,
                        this.MessageContext);
                }

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

                result2.InnerResult = result2.WebRequest.BeginGetResponse(OnBeginGetResponseComplete, result2);
            }
            catch (Exception exception)
            {
                result2.Exception = exception;
                result2.OperationCompleted();
            }
        }

        /// <summary>
        /// Completes an asynchronous operation to send a request over the secure channel.
        /// </summary>
        public void OnBeginGetResponseComplete(IAsyncResult result)
        {
            AsyncResult result2 = result.AsyncState as AsyncResult;

            if (result2 == null)
            {
                return;
            }

            if (result2.InnerResult == null)
            {
                result2.InnerResult = result;
            }

            result2.OperationCompleted();
        }

        /// <summary>
        /// Completes an asynchronous operation to send a request over the secure channel.
        /// </summary>
        public IServiceResponse EndSendRequest(IAsyncResult result)
        {
            AsyncResult result2 = result as AsyncResult;

            if (result2 == null)
            {
                throw new ArgumentException("Invalid result object passed.", "result");
            }

            result2.WaitForComplete();

            HttpWebResponse response = (HttpWebResponse)result2.WebRequest.EndGetResponse(result2.InnerResult);
            MemoryStream mstrm = new MemoryStream();

            using (Stream istrm = response.GetResponseStream())
            {
                int bytesRead = 0;
                byte[] buffer = new byte[4096];

                do
                {
                    bytesRead = istrm.Read(buffer, 0, buffer.Length);
                    mstrm.Write(buffer, 0, bytesRead);
                }
                while (bytesRead != 0);
                mstrm.Position = 0;
            }

            IEncodeable message = null;

            if (m_settings.Configuration.UseBinaryEncoding)
            {
                message = BinaryDecoder.DecodeMessage(mstrm, null, this.MessageContext);
            }
            else
            {
                string responseType = result2.Request.GetType().FullName.Replace("Request", "Response");

                message = ReadSoapMessage(
                    mstrm,
                    responseType.Substring("Opc.Ua.".Length),
                    Type.GetType(responseType),
                    this.MessageContext);
            }

            return message as IServiceResponse;
        }

        /// <summary>
        /// Saves the settings so the channel can be opened later.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="settings">The settings.</param>
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
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Uri m_url;
        private int m_operationTimeout;
        private TransportChannelSettings m_settings;
        private TcpChannelQuotas m_quotas;

        #endregion
    }
}
