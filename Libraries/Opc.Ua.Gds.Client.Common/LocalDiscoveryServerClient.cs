/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Client
{
    public class LocalDiscoveryServerClient
    {
        /// <summary>
        /// Create local discovery client
        /// </summary>
        /// <param name="configuration">Application configuration to use</param>
        /// <param name="diagnosticsMasks">The return diagnostics for all discovery requests</param>
        public LocalDiscoveryServerClient(
            ApplicationConfiguration configuration,
            DiagnosticsMasks diagnosticsMasks = DiagnosticsMasks.None)
        {
            ApplicationConfiguration = configuration;
            DiagnosticsMasks = diagnosticsMasks;
            MessageContext = configuration.CreateMessageContext();

            // set some defaults for the preferred locales.
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo
                .CurrentUICulture;

            var locales = new List<string> { culture.Name };

            culture = System.Globalization.CultureInfo.CurrentCulture;

            if (!locales.Contains(culture.Name))
            {
                locales.Add(culture.Name);
            }

            if (!locales.Contains("en-US"))
            {
                locales.Add("en-US");
            }

            PreferredLocales = [.. locales];
        }

        public ApplicationConfiguration ApplicationConfiguration { get; }
        public DiagnosticsMasks DiagnosticsMasks { get; }
        public IServiceMessageContext MessageContext { get; }

        public string[] PreferredLocales { get; set; }

        public int DefaultOperationTimeout { get; set; }

        public Task<List<ApplicationDescription>> FindServersAsync(
            CancellationToken ct = default)
        {
            return FindServersAsync(null, null, ct);
        }

        public async Task<List<ApplicationDescription>> FindServersAsync(
            string endpointUrl,
            string endpointTransportProfileUri,
            CancellationToken ct = default)
        {
            DiscoveryClient client = await CreateClientAsync(
                endpointUrl,
                endpointTransportProfileUri,
                ct).ConfigureAwait(false);

            FindServersResponse response = await client.FindServersAsync(
                null,
                endpointUrl,
                PreferredLocales,
                null,
                ct).ConfigureAwait(false);

            return response.Servers;
        }

        [Obsolete("Use FindServersAsync instead.")]
        public List<ApplicationDescription> FindServers()
        {
            IAsyncResult result = BeginFindServers(null, null, null, null, null, null, null);
            return EndFindServers(result);
        }

        [Obsolete("Use FindServersAsync instead.")]
        public List<ApplicationDescription> FindServers(
            string endpointUrl,
            string endpointTransportProfileUri)
        {
            IAsyncResult result = BeginFindServers(
                endpointUrl,
                endpointTransportProfileUri,
                null,
                null,
                null,
                null,
                null);
            return EndFindServers(result);
        }

        [Obsolete("Use FindServersAsync instead.")]
        public IAsyncResult BeginFindServers(
            AsyncCallback callback,
            object callbackData)
        {
            return BeginFindServers(null, null, null, null, null, callback, callbackData);
        }

        [Obsolete("Use FindServersAsync instead.")]
        public IAsyncResult BeginFindServers(
            string endpointUrl,
            string endpointTransportProfileUri,
            string actualEndpointUrl,
            IList<string> preferredLocales,
            IList<string> serverUris,
            AsyncCallback callback,
            object callbackData)
        {
            DiscoveryClient client = CreateClientAsync(endpointUrl, endpointTransportProfileUri).GetAwaiter().GetResult();

            var data = new FindServersData(callback, callbackData, client.OperationTimeout)
            {
                DiscoveryClient = client
            };

            data.InnerResult = client.BeginFindServers(
                null,
                (actualEndpointUrl) ?? endpointUrl,
                [.. (preferredLocales) ?? PreferredLocales],
                serverUris != null ? [.. serverUris] : null,
                OnFindServersComplete,
                data);

            return data;
        }

        [Obsolete("Use FindServersAsync instead.")]
        public List<ApplicationDescription> EndFindServers(IAsyncResult result)
        {
            if (result is not FindServersData data)
            {
                throw new ArgumentException(
                    "Did not pass the correct IAsyncResult to end method.",
                    nameof(result));
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
                data.DiscoveryClient.CloseAsync().GetAwaiter().GetResult();
            }
        }

        [Obsolete("Use FindServersAsync instead.")]
        private class FindServersData : AsyncResultBase
        {
            public FindServersData(AsyncCallback callback, object callbackData, int timeout)
                : base(callback, callbackData, timeout)
            {
            }

            public DiscoveryClient DiscoveryClient;
            public List<ApplicationDescription> Servers;
        }

        [Obsolete("Use FindServersAsync instead.")]
        private void OnFindServersComplete(IAsyncResult result)
        {
            var data = result.AsyncState as FindServersData;

            try
            {
                data.DiscoveryClient
                    .EndFindServers(result, out ApplicationDescriptionCollection servers);

                data.Servers = servers;
                data.OperationCompleted();
            }
            catch (Exception e)
            {
                data.Exception = e;
                data.OperationCompleted();
            }
        }

        public Task<List<EndpointDescription>> GetEndpointsAsync(string endpointUrl, CancellationToken ct = default)
        {
            return GetEndpointsAsync(endpointUrl, null, ct);
        }

        public async Task<List<EndpointDescription>> GetEndpointsAsync(
            string endpointUrl,
            string endpointTransportProfileUri,
            CancellationToken ct = default)
        {
            DiscoveryClient client = await CreateClientAsync(endpointUrl, endpointTransportProfileUri, ct).ConfigureAwait(false);

            GetEndpointsResponse response = await client.GetEndpointsAsync(
                null,
                endpointUrl,
                [.. PreferredLocales],
                null,
                ct)
                .ConfigureAwait(false);

            return response.Endpoints;
        }

        [Obsolete("Use GetEndpointsAsync instead.")]
        public List<EndpointDescription> GetEndpoints(string endpointUrl)
        {
            IAsyncResult result = BeginGetEndpoints(endpointUrl, null, null, null);
            return EndGetEndpoints(result);
        }

        [Obsolete("Use GetEndpointsAsync instead.")]
        public List<EndpointDescription> GetEndpoints(
            string endpointUrl,
            string endpointTransportProfileUri)
        {
            IAsyncResult result = BeginGetEndpoints(
                endpointUrl,
                endpointTransportProfileUri,
                null,
                null);
            return EndGetEndpoints(result);
        }

        [Obsolete("Use GetEndpointsAsync instead.")]
        public IAsyncResult BeginGetEndpoints(
            string endpointUrl,
            string endpointTransportProfileUri,
            AsyncCallback callback,
            object callbackData)
        {
            DiscoveryClient client = CreateClientAsync(endpointUrl, endpointTransportProfileUri).GetAwaiter().GetResult();

            var data = new GetEndpointsData(callback, callbackData, client.OperationTimeout)
            {
                DiscoveryClient = client
            };

            data.InnerResult = client.BeginGetEndpoints(
                null,
                endpointUrl,
                [.. PreferredLocales],
                null,
                OnGetEndpointsComplete,
                data);

            return data;
        }

        [Obsolete("Use GetEndpointsAsync instead.")]
        public List<EndpointDescription> EndGetEndpoints(IAsyncResult result)
        {
            if (result is not GetEndpointsData data)
            {
                throw new ArgumentException(
                    "Did not pass the correct IAsyncResult to end method.",
                    nameof(result));
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
                data.DiscoveryClient.CloseAsync().GetAwaiter().GetResult();
            }
        }

        [Obsolete("Use GetEndpointsAsync instead.")]
        private class GetEndpointsData : AsyncResultBase
        {
            public GetEndpointsData(AsyncCallback callback, object callbackData, int timeout)
                : base(callback, callbackData, timeout)
            {
            }

            public DiscoveryClient DiscoveryClient;
            public List<EndpointDescription> Endpoints;
        }

        [Obsolete("Use GetEndpointsAsync instead.")]
        private void OnGetEndpointsComplete(IAsyncResult result)
        {
            var data = result.AsyncState as GetEndpointsData;

            try
            {
                data.DiscoveryClient
                    .EndGetEndpoints(result, out EndpointDescriptionCollection endpoints);

                data.Endpoints = endpoints;
                data.OperationCompleted();
            }
            catch (Exception e)
            {
                data.Exception = e;
                data.OperationCompleted();
            }
        }

        public Task<(List<ServerOnNetwork>, DateTime lastCounterResetTime)> FindServersOnNetworkAsync(
            uint startingRecordId,
            uint maxRecordsToReturn,
            CancellationToken ct = default)
        {
            return FindServersOnNetworkAsync(
                null,
                null,
                startingRecordId,
                maxRecordsToReturn,
                null,
                ct);
        }

        public async Task<(List<ServerOnNetwork>, DateTime lastCounterResetTime)> FindServersOnNetworkAsync(
            string endpointUrl,
            string endpointTransportProfileUri,
            uint startingRecordId,
            uint maxRecordsToReturn,
            IList<string> serverCapabilityFilters,
            CancellationToken ct = default)
        {
            DiscoveryClient client = await CreateClientAsync(endpointUrl, endpointTransportProfileUri, ct).ConfigureAwait(false);

            FindServersOnNetworkResponse response = await client.FindServersOnNetworkAsync(
                null,
                startingRecordId,
                maxRecordsToReturn,
                serverCapabilityFilters != null ? [.. serverCapabilityFilters] : [],
                ct).ConfigureAwait(false);

            return (response.Servers, response.LastCounterResetTime);
        }

        [Obsolete("Use FindServersOnNetworkAsync instead.")]
        public List<ServerOnNetwork> FindServersOnNetwork(
            uint startingRecordId,
            uint maxRecordsToReturn,
            out DateTime lastCounterResetTime)
        {
            IAsyncResult result = BeginFindServersOnNetwork(
                null,
                null,
                startingRecordId,
                maxRecordsToReturn,
                null,
                null,
                null);
            return EndFindServersOnNetwork(result, out lastCounterResetTime);
        }

        [Obsolete("Use FindServersOnNetworkAsync instead.")]
        public List<ServerOnNetwork> FindServersOnNetwork(
            string endpointUrl,
            string endpointTransportProfileUri,
            uint startingRecordId,
            uint maxRecordsToReturn,
            IList<string> serverCapabilityFilters,
            out DateTime lastCounterResetTime)
        {
            IAsyncResult result = BeginFindServersOnNetwork(
                endpointUrl,
                endpointTransportProfileUri,
                startingRecordId,
                maxRecordsToReturn,
                serverCapabilityFilters,
                null,
                null);
            return EndFindServersOnNetwork(result, out lastCounterResetTime);
        }

        [Obsolete("Use FindServersOnNetworkAsync instead.")]
        public IAsyncResult BeginFindServersOnNetwork(
            uint startingRecordId,
            uint maxRecordsToReturn,
            AsyncCallback callback,
            object callbackData)
        {
            return BeginFindServersOnNetwork(
                null,
                null,
                startingRecordId,
                maxRecordsToReturn,
                null,
                callback,
                callbackData);
        }

        [Obsolete("Use FindServersOnNetworkAsync instead.")]
        public IAsyncResult BeginFindServersOnNetwork(
            string endpointUrl,
            string endpointTransportProfileUri,
            uint startingRecordId,
            uint maxRecordsToReturn,
            IList<string> serverCapabilityFilters,
            AsyncCallback callback,
            object callbackData)
        {
            DiscoveryClient client = CreateClientAsync(endpointUrl, endpointTransportProfileUri).GetAwaiter().GetResult();

            var data = new FindServersOnNetworkData(callback, callbackData, client.OperationTimeout)
            {
                DiscoveryClient = client
            };

            data.InnerResult = client.BeginFindServersOnNetwork(
                null,
                startingRecordId,
                maxRecordsToReturn,
                serverCapabilityFilters != null ? [.. serverCapabilityFilters] : [],
                OnFindServersOnNetworkComplete,
                data);

            return data;
        }

        [Obsolete("Use FindServersOnNetworkAsync instead.")]
        public List<ServerOnNetwork> EndFindServersOnNetwork(
            IAsyncResult result,
            out DateTime lastCounterResetTime)
        {
            if (result is not FindServersOnNetworkData data)
            {
                throw new ArgumentException(
                    "Did not pass the correct IAsyncResult to end method.",
                    nameof(result));
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
                data.DiscoveryClient.CloseAsync().GetAwaiter().GetResult();
            }
        }

        [Obsolete("Use FindServersOnNetworkAsync instead.")]
        private class FindServersOnNetworkData : AsyncResultBase
        {
            public FindServersOnNetworkData(
                AsyncCallback callback,
                object callbackData,
                int timeout)
                : base(callback, callbackData, timeout)
            {
            }

            public DiscoveryClient DiscoveryClient;
            public DateTime LastCounterResetTime;
            public List<ServerOnNetwork> Servers;
        }

        [Obsolete("Use FindServersOnNetworkAsync instead.")]
        private void OnFindServersOnNetworkComplete(IAsyncResult result)
        {
            var data = result.AsyncState as FindServersOnNetworkData;

            try
            {
                data.DiscoveryClient.EndFindServersOnNetwork(
                    result,
                    out DateTime lastCounterResetTime,
                    out ServerOnNetworkCollection servers);

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

        protected virtual Task<DiscoveryClient> CreateClientAsync(
            string endpointUrl,
            string endpointTransportProfileUri,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(endpointUrl))
            {
                endpointUrl = kDefaultUrl;
            }

            if (!Uri.IsWellFormedUriString(endpointUrl, UriKind.Absolute))
            {
                throw new ArgumentException("Not a valid URL.", nameof(endpointUrl));
            }

            var configuration = EndpointConfiguration.Create(ApplicationConfiguration);

            if (DefaultOperationTimeout != 0)
            {
                configuration.OperationTimeout = DefaultOperationTimeout;
            }

            return DiscoveryClient.CreateAsync(
                ApplicationConfiguration,
                new Uri(endpointUrl),
                configuration,
                DiagnosticsMasks,
                ct);
        }

        private const string kDefaultUrl = "opc.tcp://localhost:4840";
    }
}
