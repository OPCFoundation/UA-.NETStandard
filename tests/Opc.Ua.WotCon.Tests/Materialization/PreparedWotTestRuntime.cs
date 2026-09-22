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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    internal sealed class PreparedWotTestRuntime : IAsyncDisposable
    {
        private PreparedWotTestRuntime(
            string root, ServerFixture<ReferenceServer> fixture, ReferenceServer server)
        {
            m_root = root;
            m_fixture = fixture;
            m_server = server;
            Host = new LifecycleWotProjectionHost(server.NodeManagerLifecycle);
        }

        public LifecycleWotProjectionHost Host { get; }
        public NamespaceTable Namespaces => m_server.CurrentInstance.NamespaceUris;
        public INodeManagerLifecycle Lifecycle => m_server.NodeManagerLifecycle;
        public int Port => m_fixture.Port;

        public static async Task<PreparedWotTestRuntime> StartAsync()
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                nameof(PreparedWotTestRuntime), Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            ReferenceServer server = await fixture.StartAsync(Path.Combine(root, "pki")).ConfigureAwait(false);
            return new PreparedWotTestRuntime(root, fixture, server);
        }

        public async Task<WotRegistryService> CreateRegistryAsync(WotRegistryPersistenceBounds? bounds = null)
        {
            var store = new FileWotRegistryStore(Path.Combine(m_root, Guid.NewGuid().ToString("N")));
            var registry = new WotRegistryService(store, bounds);
            m_registries.Add((registry, store));
            await registry.InitializeAsync().ConfigureAwait(false);
            return registry;
        }

        public IWotInvocationProjectionHost Observe(Action<ArrayOf<WotProjectionChange>> published)
        {
            return new ObservedHost(Host, published);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                m_server.Dispose();
                foreach ((WotRegistryService registry, FileWotRegistryStore store) in m_registries)
                {
                    registry.Dispose();
                    store.Dispose();
                }
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, recursive: true);
                }
            }
        }

        private sealed class ObservedHost(
            LifecycleWotProjectionHost inner, Action<ArrayOf<WotProjectionChange>> published) : IWotInvocationProjectionHost
        {
            public bool SupportsPreparedPublication => inner.SupportsPreparedPublication;
            public ArrayOf<WoTAtomicityEnum> SupportedAtomicities => inner.SupportedAtomicities;

            public IWotProjectionPublicationCapture CapturePublication()
            {
                return new ObservedCapture(inner.CapturePublication(), published);
            }

            public async ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                ArrayOf<WotProjectionChange> changes, IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                IWotPreparedProjectionPublication unit = await inner.PrepareAsync(changes, views, cancellationToken)
                    .ConfigureAwait(false);
                return new ObservedUnit(unit, changes, published);
            }

            public ValueTask<WotProjectionHandle> AddAsync(
                WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                return inner.AddAsync(document, cancellationToken);
            }

            public ValueTask<WotProjectionHandle> ShadowReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return inner.ShadowReloadAsync(current, document, cancellationToken);
            }

            public ValueTask<WotProjectionHandle> ImmediateReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return inner.ImmediateReloadAsync(current, document, cancellationToken);
            }

            public ValueTask RemoveAsync(WotProjectionHandle handle, CancellationToken cancellationToken = default)
            {
                return inner.RemoveAsync(handle, cancellationToken);
            }
        }

        private sealed class ObservedCapture(
            IWotProjectionPublicationCapture inner, Action<ArrayOf<WotProjectionChange>> published)
            : IWotProjectionPublicationCapture
        {
            public async ValueTask<IWotProjectionPublication> BeginAsync(CancellationToken cancellationToken = default)
            {
                IWotProjectionPublication invocation = await inner.BeginAsync(cancellationToken).ConfigureAwait(false);
                return new ObservedInvocation(invocation, published);
            }
        }

        private sealed class ObservedInvocation(
            IWotProjectionPublication inner, Action<ArrayOf<WotProjectionChange>> published) : IWotProjectionPublication
        {
            public bool IsCurrent => inner.IsCurrent;

            public async ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                ArrayOf<WotProjectionChange> changes, IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                IWotPreparedProjectionPublication unit = await inner.PrepareAsync(changes, views, cancellationToken)
                    .ConfigureAwait(false);
                return new ObservedUnit(unit, changes, published);
            }

            public ValueTask DisposeAsync()
            {
                return inner.DisposeAsync();
            }
        }

        private sealed class ObservedUnit(
            IWotPreparedProjectionPublication inner,
            ArrayOf<WotProjectionChange> changes,
            Action<ArrayOf<WotProjectionChange>> published) : IWotPreparedProjectionPublication
        {
            public ArrayOf<WotProjectionHandle> Projections => inner.Projections;
            public WotPreparedViewGraphState? ViewGraph => inner.ViewGraph;
            public bool IsCommitted => inner.IsCommitted;
            public Exception? CleanupFailure => inner.CleanupFailure;

            public ValueTask CommitAsync(
                Func<CancellationToken, ValueTask> decideAsync, Action publishCommittedState,
                CancellationToken cancellationToken = default)
            {
                return inner.CommitAsync(decideAsync, () =>
                {
                    publishCommittedState();
                    published(changes);
                }, cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return inner.DisposeAsync();
            }
        }

        private readonly string m_root;
        private readonly ServerFixture<ReferenceServer> m_fixture;
        private readonly ReferenceServer m_server;
        private readonly List<(WotRegistryService Registry, FileWotRegistryStore Store)> m_registries = [];
    }
}
