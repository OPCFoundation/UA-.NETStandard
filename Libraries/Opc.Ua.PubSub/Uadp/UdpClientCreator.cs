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
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Opc.Ua.PubSub.Uadp
{
    /// <summary>
    /// Where is a method call used in 
    /// </summary>
    public enum UsedInContext
    {
        /// <summary>
        /// Publisher context call
        /// </summary>
        Publisher,
        /// <summary>
        /// Subscriber context call
        /// </summary>
        Subscriber
    };

    /// <summary>
    /// Specialized in creating the necessary <see cref="UdpClient"/> instances from an URL
    /// </summary>
    internal class UdpClientCreator
    {
        public const int SIO_UDP_CONNRESET = -1744830452;

        /// <summary>
        /// Parse the url into an IPaddress and port number
        /// </summary>
        /// <param name="url"></param>
        /// <returns>A new instance of <see cref="IPEndPoint"/> or null if invalid URL.</returns>
        internal static IPEndPoint GetEndPoint(string url)
        {
            Uri connectionUri;
            if (url != null && Uri.TryCreate(url, UriKind.Absolute, out connectionUri))
            {
                if (connectionUri.Scheme != Utils.UriSchemeOpcUdp)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "Invalid Scheme specified in URL: {0}", url);
                    return null;
                }
                if (connectionUri.Port < 0)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "Invalid Port specified in URL: {0}", url);
                    return null;
                }
                string hostName = connectionUri.Host;
                if (hostName.ToLower() == "localhost")
                {
                    hostName = "127.0.0.1";
                }

                IPAddress ipAddress;
                if (IPAddress.TryParse(hostName, out ipAddress))
                {
                    return new IPEndPoint(ipAddress, connectionUri.Port);
                }
                try
                {
                    IPHostEntry hostEntry = Dns.GetHostEntry(hostName);

                    //you might get more than one IP for a hostname since 
                    //DNS supports more than one record
                    foreach(IPAddress address in hostEntry.AddressList)
                    {
                        if (address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return new IPEndPoint(address, connectionUri.Port);
                        }
                    }
                }
                catch(Exception ex)
                {
                    Utils.Trace(ex, "Could not resolve host name: {0}", hostName);
                }
            }
            return null;
        }

        /// <summary>
        /// Creates and returns a list of <see cref="UdpClient"/> created based on configuration options
        /// </summary>
        /// <param name="pubSubContext">Is the method called in a publisher context or a subscriber context</param>
        /// <param name="networkAddressUrl">The configuration object <see cref="NetworkAddressUrlDataType"/>.</param>
        /// <param name="configuredEndpoint">The configured <see cref="IPEndPoint"/> that will be used for data exchange.</param>
        /// <returns></returns>
        internal static List<UdpClient> GetUdpClients(UsedInContext pubSubContext, NetworkAddressUrlDataType networkAddressUrl, IPEndPoint configuredEndpoint)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.AppendFormat("networkAddressUrl.NetworkInterface = {0} \n", networkAddressUrl != null ? networkAddressUrl.NetworkInterface : "null");
            buffer.AppendFormat("networkAddressUrl.Url = {0} \n", networkAddressUrl.Url != null ? networkAddressUrl.Url : "null");
            buffer.AppendFormat("configuredEndpoint = {0}", configuredEndpoint != null ? configuredEndpoint.ToString() : "null");

            Utils.Trace(Utils.TraceMasks.Information, buffer.ToString());

            List<UdpClient> udpClients = new List<UdpClient>();
            //validate input parameters
            if (networkAddressUrl == null || configuredEndpoint == null)
            {
                //log warning?
                return udpClients;
            }
            //detect the list on network interfaces that will be used for creating the UdpClient s
            List<NetworkInterface> usableNetworkInterfaces = new List<NetworkInterface>();
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            if (string.IsNullOrEmpty(networkAddressUrl.NetworkInterface))
            {
                Utils.Trace(Utils.TraceMasks.Information, "No NetworkInterface name was provided. Use all available NICs.");
                usableNetworkInterfaces.AddRange(interfaces);
            }
            else
            {
                //the configuration contains a NetworkInterface name, try to locate it
                foreach (NetworkInterface nic in interfaces)
                {
                    if (nic.Name.Equals(networkAddressUrl.NetworkInterface, StringComparison.OrdinalIgnoreCase))
                    {
                        usableNetworkInterfaces.Add(nic);
                    }
                }
                if (usableNetworkInterfaces.Count == 0)
                {
                    Utils.Trace(Utils.TraceMasks.Information, "The configured value for NetworkInterface name('{0}') could not be used.", networkAddressUrl.NetworkInterface);
                    usableNetworkInterfaces.AddRange(interfaces);
                }
            }

            foreach (NetworkInterface nic in usableNetworkInterfaces)
            {
                Utils.Trace(Utils.TraceMasks.Information, "NetworkInterface name('{0}') attempts to create instance of UdpClient.", nic.Name);
                //ignore loop-back interface
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                //ignore tunnel interface
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                UdpClient udpClient = CreateUdpClientForNetworkInterface(pubSubContext, nic, configuredEndpoint);
                if (udpClient == null) continue;
                //store UdpClient
                udpClients.Add(udpClient);
                Utils.Trace(Utils.TraceMasks.Information, "NetworkInterface name('{0}') UdpClient successfully created.", nic.Name);
            }
            return udpClients;
        }

        /// <summary>
        /// Create specific <see cref="UdpClient"/> for specified <see cref="NetworkInterface"/> and <see cref="IPEndPoint"/>.
        /// </summary>
        /// <param name="pubSubContext">Is the method called in a publisher context or a subscriber context</param>
        /// <param name="networkInterface"></param>
        /// <param name="configuredEndpoint"></param>
        /// <returns></returns>
        private static UdpClient CreateUdpClientForNetworkInterface(UsedInContext pubSubContext, NetworkInterface networkInterface, IPEndPoint configuredEndpoint)
        {
            UdpClient udpClient = null;
            IPInterfaceProperties ipProps = networkInterface.GetIPProperties();
            IPAddress localAddress = IPAddress.Any;

            foreach (var address in ipProps.UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    localAddress = address.Address;
                }
            }

            try
            {
                //detect the port used for binding
                int port = 0;
                if (pubSubContext == UsedInContext.Subscriber)
                {
                    port = configuredEndpoint.Port;
                }
                if (IsIPv4MulticastAddress(configuredEndpoint.Address))
                {
                    //instantiate multi-cast UdpClient
                    udpClient = new UdpClientMulticast(localAddress, configuredEndpoint.Address, port);
                }
                else if (IsIPv4BroadcastAddress(configuredEndpoint.Address, networkInterface))
                {
                    //instantiate broadcast UdpClient depending on publisher/subscriber usage context
                    udpClient = new UdpClientBroadcast(localAddress, port, pubSubContext);
                }
                else
                {
                    //instantiate unicast UdpClient depending on publisher/subscriber usage context
                    udpClient = new UdpClientUnicast(localAddress, port);
                }
                if (pubSubContext == UsedInContext.Publisher)
                {
                    //try to send 1 byte for target IP
                    udpClient.Send(new byte[] { 0 }, 1, configuredEndpoint);
                }

                // On Windows Only since Linux does not support this
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Disable exceptions raised by ICMP Port Unreachable messages
                    udpClient.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(Utils.TraceMasks.Information, "Cannot use Network interface '{0}'. Exception: {1}",
                       networkInterface.Name, ex.Message);
                if (udpClient != null)
                {
                    //cleanup 
                    udpClient.Dispose();
                    udpClient = null;
                }
            }       

            return udpClient;
        }

        /// <summary>
        /// Checks if the address provided is an IPv4 multicast address
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        private static bool IsIPv4MulticastAddress(IPAddress address)
        {
            if (address == null) return false;
            byte[] bytes = address.GetAddressBytes();
            if (bytes[0] >= 224 && bytes[0] <= 239)
            {
                return true;
            }
            return false;
        }


        /// <summary>
        /// Checks if the address provided is an IPv4 broadcast address
        /// </summary>
        /// <param name="address"></param>
        /// <param name="networkInterface"></param>
        /// <returns></returns>
        private static bool IsIPv4BroadcastAddress(IPAddress address, NetworkInterface networkInterface)
        {
            var ip = networkInterface.GetPhysicalAddress();
            IPInterfaceProperties ipProps = networkInterface.GetIPProperties();
            foreach (UnicastIPAddressInformation localUnicastAddress in ipProps.UnicastAddresses)
            {
                if (localUnicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    byte[] subnetMask = localUnicastAddress.IPv4Mask.GetAddressBytes();
                    uint addressBits = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
                    uint invertedSubnetBits = ~BitConverter.ToUInt32(subnetMask, 0);

                    bool isBroadcast = ((addressBits & invertedSubnetBits) == invertedSubnetBits);
                    if (isBroadcast)
                    {
                return true;
            }
                }
            }
            return false;
        }
    }
}
