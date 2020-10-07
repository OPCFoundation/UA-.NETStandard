/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Gds.Client
{
    public class LocalDiscoveryServerClient
    {
        #region Constructors
        public LocalDiscoveryServerClient(ApplicationConfiguration configuration)
        {
            ApplicationConfiguration = configuration;
            MessageContext = configuration.CreateMessageContext();

            // set some defaults for the preferred locales.
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.CurrentUICulture;

            List<string> locales = new List<string>
            {
                culture.Name
            };

            culture = System.Globalization.CultureInfo.CurrentCulture;

            if (!locales.Contains(culture.Name))
            {
                locales.Add(culture.Name);
            }

            if (!locales.Contains("en-US"))
            {
                locales.Add("en-US");
            }

            PreferredLocales = locales.ToArray();
        }
        #endregion

        #region Public Properties
        public ApplicationConfiguration ApplicationConfiguration { get; private set; }

        public ServiceMessageContext MessageContext { get; private set; }

        public string[] PreferredLocales { get; set; }

        public int DefaultOperationTimeout { get; set; }
        #endregion

        #region FindServers
        public List<ApplicationDescription> FindServers()
        {
            IAsyncResult result = BeginFindServers(null, null, null, null, null, null, null);
            return EndFindServers(result);
        }

        public List<ApplicationDescription> FindServers(string endpointUrl, string endpointTransportProfileUri)
        {
            IAsyncResult result = BeginFindServers(endpointUrl, endpointTransportProfileUri, null, null, null, null, null);
            return EndFindServers(result);
        }

        public IAsyncResult BeginFindServers(
            AsyncCallback callback,
            object callbackData)
        {
            return BeginFindServers(null, null, null, null, null, callback, callbackData);
        }

        public IAsyncResult BeginFindServers(
            string endpointUrl,
            string endpointTransportProfileUri,
            string actualEndpointUrl,
            IList<string> preferredLocales,
            IList<string> serverUris,
            AsyncCallback callback,
            object callbackData)
        {
            DiscoveryClient client = CreateClient(endpointUrl, endpointTransportProfileUri);

            FindServersData data = new FindServersData(callback, callbackData, client.OperationTimeout)
            {
                DiscoveryClient = client
            };

            data.InnerResult = client.BeginFindServers(
                null,
                (actualEndpointUrl) ?? endpointUrl,
                new StringCollection((preferredLocales) ?? PreferredLocales),
                (serverUris != null) ? new StringCollection(serverUris) : null,
                OnFindServersComplete,
                data);

            return data;
        }

        public List<ApplicationDescription> EndFindServers(IAsyncResult result)
        {
            FindServersData data = result as FindServersData;

            if (data == null)
            {
                throw new ArgumentException("Did not pass the correct IAsyncResult to end method.", "success");
            }

            try
            {
                if (!data.WaitForComplete())
                {
                    throw new TimeoutException();
                }

                return data.Servers;
            }
            finally
            {
                data.DiscoveryClient.Close();
            }
        }

        private class FindServersData : AsyncResultBase
        {
            public FindServersData(
                AsyncCallback callback,
                object callbackData,
                int timeout)
            :
                base(callback, callbackData, timeout)
            {
            }

            public DiscoveryClient DiscoveryClient;
            public List<ApplicationDescription> Servers;
        }

        private void OnFindServersComplete(IAsyncResult result)
        {
            FindServersData data = result.AsyncState as FindServersData;

            try
            {
                ApplicationDescriptionCollection servers = null;
                data.DiscoveryClient.EndFindServers(result, out servers);

                data.Servers = servers;
                data.OperationCompleted();
            }
            catch (Exception e)
            {
                data.Exception = e;
                data.OperationCompleted();
            }
        }
        #endregion

        #region GetEndpoints
        public List<EndpointDescription> GetEndpoints(string endpointUrl)
        {
            IAsyncResult result = BeginGetEndpoints(endpointUrl, null, null, null);
            return EndGetEndpoints(result);
        }

        public List<EndpointDescription> GetEndpoints(string endpointUrl, string endpointTransportProfileUri)
        {
            IAsyncResult result = BeginGetEndpoints(endpointUrl, endpointTransportProfileUri, null, null);
            return EndGetEndpoints(result);
        }

        public IAsyncResult BeginGetEndpoints(
            string endpointUrl,
            string endpointTransportProfileUri,
            AsyncCallback callback,
            object callbackData)
        {
            DiscoveryClient client = CreateClient(endpointUrl, endpointTransportProfileUri);

            GetEndpointsData data = new GetEndpointsData(callback, callbackData, client.OperationTimeout)
            {
                DiscoveryClient = client
            };

            data.InnerResult = client.BeginGetEndpoints(
                null,
                endpointUrl,
                new StringCollection(PreferredLocales),
                null,
                OnGetEndpointsComplete,
                data);

            return data;
        }

        public List<EndpointDescription> EndGetEndpoints(IAsyncResult result)
        {
            GetEndpointsData data = result as GetEndpointsData;

            if (data == null)
            {
                throw new ArgumentException("Did not pass the correct IAsyncResult to end method.", "success");
            }

            try
            {
                if (!data.WaitForComplete())
                {
                    throw new TimeoutException();
                }

                return data.Endpoints;
            }
            finally
            {
                data.DiscoveryClient.Close();
            }
        }

        private class GetEndpointsData : AsyncResultBase
        {
            public GetEndpointsData(
                AsyncCallback callback,
                object callbackData,
                int timeout)
                :
                    base(callback, callbackData, timeout)
            {
            }

            public DiscoveryClient DiscoveryClient;
            public List<EndpointDescription> Endpoints;
        }

        private void OnGetEndpointsComplete(IAsyncResult result)
        {
            GetEndpointsData data = result.AsyncState as GetEndpointsData;

            try
            {
                EndpointDescriptionCollection endpoints = null;
                data.DiscoveryClient.EndGetEndpoints(result, out endpoints);

                data.Endpoints = endpoints;
                data.OperationCompleted();
            }
            catch (Exception e)
            {
                data.Exception = e;
                data.OperationCompleted();
            }
        }
        #endregion

        #region FindServersOnNetwork
        public List<ServerOnNetwork> FindServersOnNetwork(
            uint startingRecordId,
            uint maxRecordsToReturn,
            out DateTime lastCounterResetTime)
        {
            IAsyncResult result = BeginFindServersOnNetwork(null, null, startingRecordId, maxRecordsToReturn, null, null, null);
            return EndFindServersOnNetwork(result, out lastCounterResetTime);
        }

        public List<ServerOnNetwork> FindServersOnNetwork(
            string endpointUrl,
            string endpointTransportProfileUri,
            uint startingRecordId,
            uint maxRecordsToReturn,
            IList<string> serverCapabilityFilters,
            out DateTime lastCounterResetTime)
        {
            IAsyncResult result = BeginFindServersOnNetwork(endpointUrl, endpointTransportProfileUri, startingRecordId, maxRecordsToReturn, serverCapabilityFilters, null, null);
            return EndFindServersOnNetwork(result, out lastCounterResetTime);
        }

        public IAsyncResult BeginFindServersOnNetwork(
            uint startingRecordId,
            uint maxRecordsToReturn,
            AsyncCallback callback,
            object callbackData)
        {
            return BeginFindServersOnNetwork(null, null, startingRecordId, maxRecordsToReturn, null, callback, callbackData);
        }

        public IAsyncResult BeginFindServersOnNetwork(
            string endpointUrl,
            string endpointTransportProfileUri,
            uint startingRecordId,
            uint maxRecordsToReturn,
            IList<string> serverCapabilityFilters,
            AsyncCallback callback,
            object callbackData)
        {
            DiscoveryClient client = CreateClient(endpointUrl, endpointTransportProfileUri);

            FindServersOnNetworkData data = new FindServersOnNetworkData(callback, callbackData, client.OperationTimeout)
            {
                DiscoveryClient = client
            };

            data.InnerResult = client.BeginFindServersOnNetwork(
                null,
                startingRecordId,
                maxRecordsToReturn,
                (serverCapabilityFilters != null) ? new StringCollection(serverCapabilityFilters) : new StringCollection(),
                OnFindServersOnNetworkComplete,
                data);

            return data;
        }

        public List<ServerOnNetwork> EndFindServersOnNetwork(IAsyncResult result, out DateTime lastCounterResetTime)
        {
            FindServersOnNetworkData data = result as FindServersOnNetworkData;

            if (data == null)
            {
                throw new ArgumentException("Did not pass the correct IAsyncResult to end method.", "success");
            }

            try
            {
                if (!data.WaitForComplete())
                {
                    throw new TimeoutException();
                }

                lastCounterResetTime = data.LastCounterResetTime;
                return data.Servers;
            }
            finally
            {
                data.DiscoveryClient.Close();
            }
        }

        private class FindServersOnNetworkData : AsyncResultBase
        {
            public FindServersOnNetworkData(
                AsyncCallback callback,
                object callbackData,
                int timeout)
                :
                    base(callback, callbackData, timeout)
            {
            }

            public DiscoveryClient DiscoveryClient;
            public DateTime LastCounterResetTime;
            public List<ServerOnNetwork> Servers;
        }

        private void OnFindServersOnNetworkComplete(IAsyncResult result)
        {
            FindServersOnNetworkData data = result.AsyncState as FindServersOnNetworkData;

            try
            {
                DateTime lastCounterResetTime;
                ServerOnNetworkCollection servers = null;
                data.DiscoveryClient.EndFindServersOnNetwork(result, out lastCounterResetTime, out servers);

                data.LastCounterResetTime = lastCounterResetTime;
                data.Servers = servers;
                data.OperationCompleted();
            }
            catch (Exception e)
            {
                data.Exception = e;
                data.OperationCompleted();
            }
        }
        #endregion

        #region Protected Methods
        protected virtual DiscoveryClient CreateClient(
            string endpointUrl,
            string endpointTransportProfileUri)
        {
            if (String.IsNullOrEmpty(endpointUrl))
            {
                endpointUrl = DefaultUrl;
            }

            if (!Uri.IsWellFormedUriString(endpointUrl, UriKind.Absolute))
            {
                throw new ArgumentException("Not a valid URL.", nameof(endpointUrl));
            }

            ServiceMessageContext context = ApplicationConfiguration.CreateMessageContext();

            EndpointConfiguration configuration = EndpointConfiguration.Create(ApplicationConfiguration);

            if (DefaultOperationTimeout != 0)
            {
                configuration.OperationTimeout = DefaultOperationTimeout;
            }

            ITransportChannel channel = DiscoveryChannel.Create(new Uri(endpointUrl), configuration, context);

            DiscoveryClient client = new DiscoveryClient(channel);
            return client;
        }

        #region Private Methods
        #endregion

        #endregion

        #region Private Fields
        private const string DefaultUrl = "opc.tcp://localhost:4840";
        #endregion
    }
}
