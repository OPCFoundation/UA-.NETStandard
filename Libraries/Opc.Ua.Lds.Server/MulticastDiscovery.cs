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
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Lds.Server
{
    /// <summary>
    /// Multicast DNS wrapper that announces this LDS as an
    /// <c>_opcua-tcp._tcp</c> service per OPC UA Part 12 §6.4.6 and observes
    /// peer announcements, surfacing them to the
    /// <see cref="RegisteredServerStore"/>.
    /// </summary>
    public sealed class MulticastDiscovery : IDisposable
    {
        /// <summary>
        /// Standard mDNS service type for OPC UA discovery per Part 12.
        /// </summary>
        public const string OpcUaServiceType = "_opcua-tcp._tcp";

        private static readonly char[] s_capsSeparator = [','];

        private readonly RegisteredServerStore m_store;
        private readonly ILogger m_logger;
        private readonly bool m_loopbackOnly;
        private MulticastService m_service;
        private ServiceDiscovery m_discovery;
        private readonly List<ServiceProfile> m_profiles = [];
        private bool m_started;
        private bool m_disposed;

        /// <summary>
        /// Creates a new multicast wrapper.
        /// </summary>
        /// <param name="store">Store to publish observed peers into.</param>
        /// <param name="loopbackOnly">When true, restricts announcements and
        ///   queries to the loopback NIC. Used by in-process tests.</param>
        /// <param name="logger">Optional logger.</param>
        public MulticastDiscovery(
            RegisteredServerStore store,
            bool loopbackOnly = false,
            ILogger logger = null)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            m_store = store;
            m_loopbackOnly = loopbackOnly;
            m_logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        /// <summary>
        /// True after <see cref="StartAsync(string, IList{string}, IList{string}, CancellationToken)"/> has run.
        /// </summary>
        public bool IsRunning => m_started;

        /// <summary>
        /// Starts mDNS announcement and discovery.
        /// </summary>
        /// <param name="applicationUri">The LDS's ApplicationUri (used as the
        ///   default mDNS instance name).</param>
        /// <param name="discoveryUrls">The LDS's discovery URLs.</param>
        /// <param name="capabilities">The LDS's server capabilities (e.g. LDS, LDS-ME).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task StartAsync(
            string applicationUri,
            IList<string> discoveryUrls,
            IList<string> capabilities,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(applicationUri))
            {
                throw new ArgumentException("Application URI must be provided.", nameof(applicationUri));
            }
            if (discoveryUrls == null)
            {
                throw new ArgumentNullException(nameof(discoveryUrls));
            }
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            if (m_started)
            {
                return Task.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();

            Func<IEnumerable<NetworkInterface>, IEnumerable<NetworkInterface>> filter = m_loopbackOnly
                ? nics => nics.Where(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                : null;

            m_service = new MulticastService(filter);

            m_discovery = new ServiceDiscovery(m_service);
            m_discovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;

            foreach (string url in discoveryUrls)
            {
                ServiceProfile profile = TryBuildProfile(applicationUri, url, capabilities);
                if (profile != null)
                {
                    m_profiles.Add(profile);
                    m_discovery.Advertise(profile);
                }
            }

            m_service.Start();
            m_started = true;

            // proactively probe for other LDS / OPC UA servers on the network.
            try
            {
                m_discovery.QueryServiceInstances(OpcUaServiceType);
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "Multicast initial query failed.");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops mDNS announcement (sends goodbye packets where supported).
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_started)
            {
                return Task.CompletedTask;
            }

            try
            {
                foreach (ServiceProfile profile in m_profiles)
                {
                    try
                    {
                        m_discovery.Unadvertise(profile);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogDebug(ex, "Multicast unadvertise failed.");
                    }
                }
                m_profiles.Clear();
            }
            finally
            {
                m_discovery?.Dispose();
                m_discovery = null;

                m_service?.Stop();
                m_service?.Dispose();
                m_service = null;
                m_started = false;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "Multicast dispose failed.");
            }
        }

        private void OnServiceInstanceDiscovered(object sender, ServiceInstanceDiscoveryEventArgs e)
        {
            try
            {
                Message msg = e.Message;
                if (msg == null)
                {
                    return;
                }

                // Extract SRV (host+port), A/AAAA (address), TXT (path/caps).
                SRVRecord srv = msg.AdditionalRecords.OfType<SRVRecord>().FirstOrDefault()
                    ?? msg.Answers.OfType<SRVRecord>().FirstOrDefault();
                if (srv == null)
                {
                    return;
                }

                string instanceName = e.ServiceInstanceName?.ToString() ?? srv.Name?.ToString();
                if (string.IsNullOrEmpty(instanceName))
                {
                    return;
                }

                IPAddress address = msg.AdditionalRecords.OfType<ARecord>().FirstOrDefault()?.Address
                    ?? msg.Answers.OfType<ARecord>().FirstOrDefault()?.Address
                    ?? msg.AdditionalRecords.OfType<AAAARecord>().FirstOrDefault()?.Address
                    ?? IPAddress.Loopback;

                TXTRecord txt = msg.AdditionalRecords.OfType<TXTRecord>().FirstOrDefault()
                    ?? msg.Answers.OfType<TXTRecord>().FirstOrDefault();

                string path = "/";
                List<string> caps = [];
                if (txt != null)
                {
                    foreach (string str in txt.Strings)
                    {
                        if (str.StartsWith("path=", StringComparison.Ordinal))
                        {
                            path = str.Substring("path=".Length);
                        }
                        else if (str.StartsWith("caps=", StringComparison.Ordinal))
                        {
                            foreach (string raw in str.Substring("caps=".Length)
                                .Split(s_capsSeparator, StringSplitOptions.RemoveEmptyEntries))
                            {
                                string trimmed = raw.Trim();
                                if (trimmed.Length > 0)
                                {
                                    caps.Add(trimmed);
                                }
                            }
                        }
                    }
                }

                string discoveryUrl = $"opc.tcp://{address}:{srv.Port}{path}";

                m_store.UpsertMulticastRecord(
                    serverUri: null,
                    serverName: instanceName,
                    discoveryUrl: discoveryUrl,
                    capabilities: caps);
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "Failed to process mDNS service instance.");
            }
        }

        private ServiceProfile TryBuildProfile(
            string applicationUri,
            string discoveryUrl,
            IList<string> capabilities)
        {
            try
            {
                Uri parsed = new(discoveryUrl, UriKind.Absolute);
                if (parsed.Port <= 0)
                {
                    return null;
                }

                IEnumerable<IPAddress> addrs = ResolveLocalAddresses();
                string instanceName = SanitizeInstanceName(applicationUri);

                var profile = new ServiceProfile(
                    instanceName,
                    OpcUaServiceType,
                    (ushort)parsed.Port,
                    addrs);

                string path = string.IsNullOrEmpty(parsed.AbsolutePath) ? "/" : parsed.AbsolutePath;
                profile.AddProperty("path", path);
                if (capabilities.Count > 0)
                {
                    profile.AddProperty("caps", string.Join(",", capabilities));
                }

                return profile;
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "Failed to build mDNS profile for {Url}.", discoveryUrl);
                return null;
            }
        }

        private IEnumerable<IPAddress> ResolveLocalAddresses()
        {
            if (m_loopbackOnly)
            {
                yield return IPAddress.Loopback;
                yield break;
            }

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                IPInterfaceProperties ipProps = nic.GetIPProperties();
                foreach (UnicastIPAddressInformation u in ipProps.UnicastAddresses)
                {
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork
                        || u.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        yield return u.Address;
                    }
                }
            }
        }

        private static string SanitizeInstanceName(string applicationUri)
        {
            if (string.IsNullOrEmpty(applicationUri))
            {
                return "OpcUaLds";
            }

            // mDNS instance names should be friendly UTF-8 strings; strip URI scheme.
            int schemeIdx = applicationUri.IndexOf("://", StringComparison.Ordinal);
            string trimmed = schemeIdx >= 0 ? applicationUri.Substring(schemeIdx + 3) : applicationUri;
            // limit length and replace separators that confuse some browsers.
            string sanitized = trimmed
                .Replace('/', '-')
                .Replace(':', '-')
                .Replace('?', '-');
            return sanitized.Length > 63 ? sanitized.Substring(0, 63) : sanitized;
        }
    }
}
