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
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Hosts a server through <c>AddOpcUa()</c>, composed by the test, and
    /// reaches into it through the Asset Management Basics node manager.
    /// </summary>
    internal sealed class AmbHostedServer : IAsyncDisposable
    {
        private AmbHostedServer(ServiceProvider provider, IHostedService hostedService, string endpointUrl)
        {
            Provider = provider;
            m_hostedService = hostedService;
            EndpointUrl = endpointUrl;
            AssetManagement = provider.GetRequiredService<AssetManagement>();
        }

        public ServiceProvider Provider { get; }

        public AssetManagement AssetManagement { get; }

        public AmbNodeManager Manager => AssetManagement.Manager!;

        public IServerInternal Server => Manager.Server;

        public string EndpointUrl { get; }

        /// <summary>
        /// Builds and starts a server.
        /// </summary>
        /// <param name="name">A name unique to the test, used for the PKI and endpoint.</param>
        /// <param name="compose">Adds the node managers and their configuration.</param>
        public static async Task<AmbHostedServer> StartAsync(string name, Action<IOpcUaServerBuilder> compose)
        {
            string endpointUrl = "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                "/" +
                name;
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                string root = System.IO.Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    name,
                    Guid.NewGuid().ToString("N"));
                options.ApplicationName = name;
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
            var server = new AmbHostedServer(provider, hostedService, endpointUrl);

            var watch = Stopwatch.StartNew();
            while (server.AssetManagement.Manager?.Server.IsRunning != true)
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

        public NodeId ToNodeId(ExpandedNodeId nodeId)
        {
            return ExpandedNodeId.ToNodeId(nodeId, Server.NamespaceUris);
        }

        public async Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
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
                        ReferenceTypeId = referenceTypeId,
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

        public async Task<StatusCode> WriteAsync(NodeId nodeId, Variant value)
        {
            using OperationContext context = CreateContext(RequestType.Write);
            (ArrayOf<StatusCode> results, _) = await Server.NodeManager.WriteAsync(
                context,
                [
                    new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(value)
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            return results[0];
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
        /// Finds a node in whichever node manager owns it.
        /// </summary>
        /// <typeparam name="T">The type of the node state.</typeparam>
        public async Task<T> FindNodeAsync<T>(NodeId nodeId)
            where T : NodeState
        {
            NodeState? node = await Server.NodeManager.FindNodeInAddressSpaceAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(node, Is.InstanceOf<T>(), nodeId.ToString());
            return (T)node!;
        }

        /// <summary>
        /// Calls <c>FindAlias</c> on an alias category through the Call service.
        /// </summary>
        public async Task<CallMethodResult> CallFindAliasAsync(
            ExpandedNodeId categoryId,
            string pattern,
            NodeId referenceTypeFilter)
        {
            NodeId category = ToNodeId(categoryId);
            AliasNameCategoryState? state = Manager.FindPredefinedNode<AliasNameCategoryState>(category);
            Assert.That(state?.FindAlias, Is.Not.Null, categoryId.ToString());
            using OperationContext context = CreateContext(RequestType.Call);
            (ArrayOf<CallMethodResult> results, _) = await Server.NodeManager.CallAsync(
                context,
                [
                    new CallMethodRequest
                    {
                        ObjectId = category,
                        MethodId = state!.FindAlias!.NodeId,
                        InputArguments = [Variant.From(pattern), Variant.From(referenceTypeFilter)]
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            return results[0];
        }

        /// <summary>
        /// Calls <c>FindAlias</c> and returns the aliases it found.
        /// </summary>
        public async Task<ArrayOf<AliasNameDataType>> FindAliasAsync(ExpandedNodeId categoryId, string pattern)
        {
            CallMethodResult result = await CallFindAliasAsync(categoryId, pattern, NodeId.Null).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True, result.StatusCode.ToString());
            Assert.That(
                result.OutputArguments[0].TryGetStructure(out ArrayOf<AliasNameDataType> aliases),
                Is.True,
                "FindAlias returns an array of AliasNameDataType");
            return aliases;
        }

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
    }
}
