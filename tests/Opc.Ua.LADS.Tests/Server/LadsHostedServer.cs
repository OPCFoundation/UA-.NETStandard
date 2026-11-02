/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Hosts a server composed by the test through <c>AddOpcUa()</c> and reaches the LADS devices
    /// through the device registry.
    /// </summary>
    internal sealed class LadsHostedServer : IAsyncDisposable
    {
        private LadsHostedServer(ServiceProvider provider, IHostedService hostedService)
        {
            Provider = provider;
            m_hostedService = hostedService;
            Registry = provider.GetRequiredService<ILadsDeviceRegistry>();
        }

        /// <summary>
        /// Gets the service provider.
        /// </summary>
        public ServiceProvider Provider { get; }

        /// <summary>
        /// Gets the device registry.
        /// </summary>
        public ILadsDeviceRegistry Registry { get; }

        /// <summary>
        /// Gets the server.
        /// </summary>
        public IServerInternal Server => m_server ?? throw new InvalidOperationException("Not started.");

        /// <summary>
        /// Builds and starts a server.
        /// </summary>
        public static async Task<LadsHostedServer> StartAsync(
            string name,
            Action<IOpcUaServerBuilder> compose,
            string deviceName = LadsIncubator.DeviceName)
        {
            string endpointUrl = "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) + "/" + name;
            var services = new ServiceCollection();
            var logs = new RecordingLoggerProvider(LogLevel.Warning);
            services.AddLogging(logging => logging.AddProvider(logs));
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                // Short names keep the pki paths below MAX_PATH on .NET Framework.
                string shortName = name.Length > 24 ? name[..24] : name;
                string root = System.IO.Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "lads",
                    Guid.NewGuid().ToString("N")[..8]);
                options.ApplicationName = shortName;
                options.ApplicationUri = "urn:localhost:" + name;
                options.ProductUri = "urn:localhost:" + name + ":product";
                options.PkiRoot = System.IO.Path.Combine(root, "pki");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(endpointUrl);
            });
            compose(builder);
            ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hostedService = provider.GetServices<IHostedService>().Single();
            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
            var server = new LadsHostedServer(provider, hostedService);
            try
            {
                ILadsDevice device = await server.DeviceAsync(deviceName).ConfigureAwait(false);
                server.m_server = ((LadsDevice)device).Runtime.Manager.Server;
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                Assert.Fail("The device was not published: " + string.Join(
                    Environment.NewLine,
                    logs.Records.Select(record =>
                        record.Message + " " + record.Exception + " " + record.Exception?.StackTrace)));
                throw;
            }
            var watch = Stopwatch.StartNew();
            while (!server.Server.IsRunning)
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(90))
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                    Assert.Fail("The server did not start.");
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            return server;
        }

        /// <summary>
        /// Builds and starts the standalone incubator server.
        /// </summary>
        public static Task<LadsHostedServer> StartIncubatorAsync(
            string name,
            Action<IOpcUaServerBuilder>? services = null,
            Action<LadsServerOptions>? options = null)
        {
            return StartAsync(name, builder =>
            {
                services?.Invoke(builder);
                builder.AddLads(options).ConfigureLads(LadsIncubator.Configure);
            });
        }

        /// <summary>
        /// Waits for a published device.
        /// </summary>
        public async Task<ILadsDevice> DeviceAsync(string name = LadsIncubator.DeviceName)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            return await Registry.WaitForDeviceAsync(name, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Converts an expanded node id.
        /// </summary>
        public NodeId ToNodeId(ExpandedNodeId nodeId)
        {
            return ExpandedNodeId.ToNodeId(nodeId, Server.NamespaceUris);
        }

        /// <summary>
        /// Browses forward hierarchical references.
        /// </summary>
        public async Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(
            NodeId nodeId,
            NodeId? referenceTypeId = null,
            BrowseDirection direction = BrowseDirection.Forward)
        {
            using OperationContext context = CreateContext(RequestType.Browse);
            (ArrayOf<BrowseResult> results, _) = await Server.NodeManager.BrowseAsync(
                context,
                new ViewDescription(),
                0,
                [
                    new BrowseDescription
                    {
                        NodeId = nodeId,
                        BrowseDirection = direction,
                        ReferenceTypeId = referenceTypeId ?? ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(results[0].StatusCode), Is.True, results[0].StatusCode.ToString());
            var references = new List<ReferenceDescription>();
            foreach (ReferenceDescription reference in results[0].References)
            {
                references.Add(reference);
            }
            return references;
        }

        /// <summary>
        /// Returns the browse names of the forward hierarchical children.
        /// </summary>
        public async Task<string[]> ChildNamesAsync(NodeId nodeId)
        {
            IReadOnlyList<ReferenceDescription> references = await BrowseAsync(nodeId).ConfigureAwait(false);
            return [.. references.Select(reference => reference.BrowseName.Name ?? string.Empty)];
        }

        /// <summary>
        /// Resolves a child by a path of browse names.
        /// </summary>
        public async Task<NodeId> ChildAsync(NodeId nodeId, params string[] path)
        {
            NodeId current = nodeId;
            foreach (string name in path)
            {
                IReadOnlyList<ReferenceDescription> references = await BrowseAsync(current).ConfigureAwait(false);
                ReferenceDescription? match = references.FirstOrDefault(r => r.BrowseName.Name == name);
                Assert.That(match, Is.Not.Null, $"{current}/{name}");
                current = ToNodeId(match!.NodeId);
            }
            return current;
        }

        /// <summary>
        /// Reads an attribute.
        /// </summary>
        public async Task<DataValue> ReadAsync(NodeId nodeId, uint attributeId = Attributes.Value)
        {
            using OperationContext context = CreateContext(RequestType.Read);
            (ArrayOf<DataValue> values, _) = await Server.NodeManager.ReadAsync(
                context,
                0,
                TimestampsToReturn.Both,
                [new ReadValueId { NodeId = nodeId, AttributeId = attributeId }],
                CancellationToken.None).ConfigureAwait(false);
            return values[0];
        }

        /// <summary>
        /// Calls a method through the Call service.
        /// </summary>
        public async Task<CallMethodResult> CallAsync(NodeId objectId, NodeId methodId, params Variant[] arguments)
        {
            using OperationContext context = CreateContext(RequestType.Call);
            (ArrayOf<CallMethodResult> results, _) = await Server.NodeManager.CallAsync(
                context,
                [
                    new CallMethodRequest
                    {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = arguments.ToArrayOf()
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            return results[0];
        }

        /// <summary>
        /// Calls a method found by browse name below the object.
        /// </summary>
        public async Task<CallMethodResult> CallByNameAsync(
            NodeId objectId,
            string method,
            params Variant[] arguments)
        {
            NodeId methodId = await ChildAsync(objectId, method).ConfigureAwait(false);
            return await CallAsync(objectId, methodId, arguments).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the hosted service.
        /// </summary>
        public Task StopAsync()
        {
            return m_hostedService.StopAsync(CancellationToken.None);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await m_hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await Provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static OperationContext CreateContext(RequestType requestType)
        {
            return new OperationContext(
                new RequestHeader(),
                null,
                requestType,
                RequestLifetime.None,
                new UserIdentity());
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private readonly IHostedService m_hostedService;
        private IServerInternal? m_server;
    }
}
