/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Server;
using ISession = Opc.Ua.Client.ISession;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Test-only non-singleton DCA address space. Its managed methods forward over a real secure session
    /// to the managed server's one standard configuration engine, not a second writer over its stores.
    /// </summary>
    internal sealed class ConnectedManagedApplicationBridge : AsyncCustomNodeManager
    {
        /// <summary>
        /// Borrows the fixture-owned managed transport and its independent application metadata.
        /// </summary>
        internal ConnectedManagedApplicationBridge(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ApplicationConfiguration managedConfiguration,
            Func<CancellationToken, ValueTask<ISession>> connect,
            Func<CancellationToken, Task> drain)
            : base(server, configuration, NamespaceUri)
        {
            m_managedConfiguration = managedConfiguration;
            m_connect = connect;
            m_drain = drain;
        }

        /// <summary>
        /// Gets successful forwarded method names, including reads, for interaction assertions.
        /// </summary>
        internal List<string> Calls { get; } = [];

        /// <summary>
        /// Gets the generated non-singleton device address.
        /// </summary>
        internal NodeId DeviceId { get; private set; }

        /// <summary>
        /// Gets the generated singleton flag address.
        /// </summary>
        internal NodeId SingletonId { get; private set; }

        /// <summary>
        /// Gets the number of forwarded operations that can mutate keys, trust or certificates.
        /// </summary>
        internal int Mutations => Calls.Count(static name => name is
            Ua.BrowseNames.CreateSigningRequest or Ua.BrowseNames.UpdateCertificate or
            Ua.BrowseNames.ApplyChanges or Ua.BrowseNames.Write or Ua.BrowseNames.CloseAndUpdate);

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ushort ns = context.NamespaceUris.GetIndexOrAppend(NamespaceUri);
            var device = new ProvisionableDeviceState(null);
            device.Create(context, new NodeId("Device", ns),
                new QualifiedName("Device", ns), new LocalizedText("Connected non-singleton DCA"), true);
            device.IsSingleton!.Value = false;
            device.RequestTickets!.OnCallMethod2Async = (_, _, _, _, outputs, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                outputs.Clear();
                ArrayOf<string> tickets = ["fixture-ticket"];
                outputs.Add(new Variant(tickets));
                return ValueTask.FromResult(ServiceResult.Good);
            };
            DeviceId = device.NodeId;
            SingletonId = device.IsSingleton.NodeId;
            device.AddReference(Ua.ReferenceTypeIds.Organizes, true, Ua.ObjectIds.ObjectsFolder);

            var application = new ApplicationConfigurationState(device);
            application.Create(context, new NodeId("ManagedApplication", ns),
                new QualifiedName("ManagedApplication", ns),
                new LocalizedText(m_managedConfiguration.ApplicationName!), true);
            application.AddGetCertificates(context).AddCancelChanges(context)
                .AddApplicationUri(context).AddProductUri(context).AddApplicationType(context)
                .AddApplicationNames(context);
            application.ApplicationUri!.Value = m_managedConfiguration.ApplicationUri!;
            application.ProductUri!.Value = m_managedConfiguration.ProductUri!;
            application.ApplicationType!.Value = ApplicationType.Server;
            application.ApplicationNames!.Value = [new LocalizedText(m_managedConfiguration.ApplicationName!)];
            application.Enabled!.Value = true;
            application.CertificateGroups!.DefaultApplicationGroup!.CertificateTypes!.Value =
                [Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType];
            application.AddReference(Ua.ReferenceTypeIds.HasComponent, true, device.NodeId);
            device.AddChild(application);
            BindForwarders(context, application, []);
            return ValueTask.FromResult(new NodeStateCollection { device });
        }

        /// <summary>
        /// Forwards the closing DCA session's outstanding trust handles and transactions to session disposal.
        /// The focused fixture admits only one processor attempt at a time.
        /// </summary>
        public override async ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            await RetireManagedSessionAsync().ConfigureAwait(false);
            await base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            await RetireManagedSessionAsync().ConfigureAwait(false);
            m_gate.Dispose();
            await base.DisposeAsyncCore().ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps the generated standard method signatures and translates only instance addresses.
        /// Typed generated handlers are unset, so dispatch falls through to their generic async handler.
        /// </summary>
        private void BindForwarders(ISystemContext context, NodeState node, ArrayOf<string> path)
        {
            m_paths.Add(node.NodeId, path);
            if (node is MethodState method)
            {
                method.OnCallMethod2Async = ForwardAsync;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                BindForwarders(context, child, path.AddItem(child.BrowseName.Name!));
            }
        }

        /// <summary>
        /// Routes the real Part 12 and File/TrustList operations through one transaction-owning secure session.
        /// After ApplyChanges, drains actual listener activation and reconnects instead of reusing a retired key.
        /// </summary>
        private async ValueTask<ServiceResult> ForwardAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs,
            CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                m_session ??= await m_connect(ct).ConfigureAwait(false);
                ISession session = m_session;
                NodeId remoteObject = await ResolveAsync(session, m_paths[objectId], ct).ConfigureAwait(false);
                NodeId remoteMethod = await ResolveAsync(session, m_paths[method.NodeId], ct).ConfigureAwait(false);
                Variant[] arguments = inputs.Span.ToArray();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (arguments[i].TryGetValue(out NodeId id) && m_paths.TryGetValue(id, out ArrayOf<string> path))
                    {
                        arguments[i] = new Variant(await ResolveAsync(session, path, ct).ConfigureAwait(false));
                    }
                }
                ArrayOf<Variant> result = await session.CallAsync(remoteObject, remoteMethod, ct, arguments)
                    .ConfigureAwait(false);
                outputs.Clear();
                for (int i = 0; i < result.Count; i++)
                {
                    outputs.Add(result[i]);
                }
                Calls.Add(method.BrowseName.Name!);
                if (method.BrowseName.Name == Ua.BrowseNames.ApplyChanges)
                {
                    await m_drain(ct).ConfigureAwait(false);
                    await session.DisposeAsync().ConfigureAwait(false);
                    m_session = null;
                }
                return ServiceResult.Good;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Resolves the counterpart instance below the managed server's actual standard configuration object.
        /// </summary>
        private static async ValueTask<NodeId> ResolveAsync(
            ISession session,
            ArrayOf<string> path,
            CancellationToken ct)
        {
            NodeId id = Ua.ObjectIds.ServerConfiguration;
            for (int index = 0; index < path.Count; index++)
            {
                id = await ClientModelAccess.ResolveAsync(session, id, Namespaces.OpcUa, path[index], ct)
                    .ConfigureAwait(false);
                if (id.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }
            }
            return id;
        }

        /// <summary>
        /// Serializes shutdown with forwarding and abandons the remote session's uncommitted changes.
        /// </summary>
        private async ValueTask RetireManagedSessionAsync()
        {
            await m_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_session != null)
                {
                    try
                    {
                        if (m_session.Connected)
                        {
                            await m_session.CloseAsync(5000, true).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await m_session.DisposeAsync().ConfigureAwait(false);
                        m_session = null;
                    }
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>The test-only namespace, distinct from the managed application and GDS namespaces.</summary>
        internal const string NamespaceUri = "urn:opcfoundation:test:connected-managed-bridge";

        /// <summary>The independent managed server's metadata.</summary>
        private readonly ApplicationConfiguration m_managedConfiguration;

        /// <summary>The real secure-session connector.</summary>
        private readonly Func<CancellationToken, ValueTask<ISession>> m_connect;

        /// <summary>The real standard configuration manager's deferred-effects drain.</summary>
        private readonly Func<CancellationToken, Task> m_drain;

        /// <summary>The generated instance address to standard relative browse path mapping.</summary>
        private readonly Dictionary<NodeId, ArrayOf<string>> m_paths = [];

        /// <summary>Serializes forwarding and remote session retirement.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>The owned remote session, retained only across one staged transaction.</summary>
        private ISession? m_session;
    }
}
#endif
