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
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Registry;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Owns a running server and prepared registry storage for materialization tests.
    /// </summary>
    internal sealed class PreparedWotTestRuntime : IAsyncDisposable
    {
        private PreparedWotTestRuntime(
            string root, ServerFixture<ReferenceServer> fixture, ReferenceServer server, bool forceLeasedContent)
        {
            m_root = root;
            m_fixture = fixture;
            m_server = server;
            m_forceLeasedContent = forceLeasedContent;
            m_configuredStorage = new PreparedWotTestStorage(StorageFolder, forceLeasedContent);
            Host = new LifecycleWotProjectionHost(server.NodeManagerLifecycle);
        }

        /// <summary>
        /// Gets the host for <c>PreparedWotTestRuntime</c>.
        /// </summary>
        public LifecycleWotProjectionHost Host { get; }

        /// <summary>
        /// Gets the namespaces for <c>PreparedWotTestRuntime</c>.
        /// </summary>
        public NamespaceTable Namespaces => m_server.CurrentInstance.NamespaceUris;

        /// <summary>
        /// Gets the lifecycle for <c>PreparedWotTestRuntime</c>.
        /// </summary>
        public INodeManagerLifecycle Lifecycle => m_server.NodeManagerLifecycle;

        /// <summary>
        /// Gets the port for <c>PreparedWotTestRuntime</c>.
        /// </summary>
        public int Port => m_fixture.Port;

        /// <summary>
        /// Gets the configured registry storage directory within the test runtime.
        /// </summary>
        public string StorageFolder => Path.Combine(m_root, "configured-registry");

        /// <summary>
        /// Gets the configured resource store for <c>PreparedWotTestRuntime</c>.
        /// </summary>
        public IXRegistryResourceStore? ConfiguredResourceStore => m_configuredStorage.ContentStore;

        /// <summary>
        /// Starts a runtime that hosts the prepared WoT publication pipeline.
        /// </summary>
        /// <param name="forceLeasedContent">The force-leased-content flag supplied by the caller.</param>
        public static async Task<PreparedWotTestRuntime> StartAsync(bool forceLeasedContent = false)
        {
            string root = Path.Combine(Path.GetTempPath(),
                "pwr", Guid.NewGuid().ToString("N").Substring(0, 12));
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            ReferenceServer server = await fixture.StartAsync(Path.Combine(root, "pki")).ConfigureAwait(false);
            return new PreparedWotTestRuntime(root, fixture, server, forceLeasedContent);
        }

        /// <summary>
        /// Creates a registry service backed by the runtime's stores and projection host.
        /// </summary>
        /// <param name="bounds">The WoT registry persistence bounds.</param>
        public async Task<WotRegistryService> CreateRegistryAsync(WotRegistryPersistenceBounds? bounds = null)
        {
            var storage = new PreparedWotTestStorage(
                Path.Combine(m_root, Guid.NewGuid().ToString("N").Substring(0, 12)), m_forceLeasedContent);
            m_storages.Add(storage);
            FileWotRegistryStore store = storage.OpenStore();
            Assert.That(store.SupportsPreparedCommits, Is.True);
            var registry = new WotRegistryService(store, bounds);
            m_registries.Add((registry, store));
            await registry.InitializeAsync().ConfigureAwait(false);
            return registry;
        }

        /// <summary>
        /// Wraps the projection host with callbacks that observe preparation, validation, publication, and caller
        /// context.
        /// </summary>
        /// <param name="published">The callback observing committed projection changes.</param>
        /// <param name="validate">The optional callback observing publication validation.</param>
        /// <param name="prepare">The optional callback observing prepared projection changes.</param>
        /// <param name="contextObserved">
        /// The optional callback observing the caller context in each publication phase.
        /// </param>
        public IWotInvocationProjectionHost Observe(
            Action<ArrayOf<WotProjectionChange>> published,
            Action<ArrayOf<WotProjectionChange>>? validate = null,
            Action<ArrayOf<WotProjectionChange>>? prepare = null,
            Action<string, IOperationContext?>? contextObserved = null)
        {
            return new ObservedHost(Host, published, validate, prepare, contextObserved);
        }

        /// <inheritdoc />
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
                foreach (PreparedWotTestStorage storage in m_storages)
                {
                    storage.Dispose();
                }
                m_configuredStorage.Dispose();
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, recursive: true);
                }
            }
        }

        private sealed class ObservedHost(
            LifecycleWotProjectionHost inner,
            Action<ArrayOf<WotProjectionChange>> published,
            Action<ArrayOf<WotProjectionChange>>? validate,
            Action<ArrayOf<WotProjectionChange>>? prepare,
            Action<string, IOperationContext?>? contextObserved) : IWotInvocationProjectionHost
        {
            /// <summary>
            /// Gets the supports prepared publication for <c>ObservedHost</c>.
            /// </summary>
            public bool SupportsPreparedPublication => inner.SupportsPreparedPublication;

            /// <summary>
            /// Gets the supported atomicities for <c>ObservedHost</c>.
            /// </summary>
            public ArrayOf<WoTAtomicityEnum> SupportedAtomicities => inner.SupportedAtomicities;

            public IWotProjectionPublicationCapture CapturePublication()
            {
                return new ObservedCapture(inner.CapturePublication(), published, validate, prepare, contextObserved);
            }

            /// <inheritdoc/>
            /// <param name="views">The WoT prepared view publication.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                ArrayOf<WotProjectionChange> changes, IWotPreparedViewPublication? views,
                IOperationContext? callerContext, CancellationToken cancellationToken)
            {
                prepare?.Invoke(changes);
                contextObserved?.Invoke("prepare", callerContext);
                IWotPreparedProjectionPublication unit = await inner
                    .PrepareAsync(changes, views, callerContext, cancellationToken)
                    .ConfigureAwait(false);
                return new ObservedUnit(unit, changes, published);
            }

            /// <inheritdoc/>
            /// <param name="document">The WoT projection document.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<WotProjectionHandle> AddAsync(
                WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                return inner.AddAsync(document, cancellationToken);
            }

            /// <inheritdoc/>
            /// <param name="current">The WoT projection handle.</param>
            /// <param name="document">The WoT projection document.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<WotProjectionHandle> ShadowReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return inner.ShadowReloadAsync(current, document, cancellationToken);
            }

            /// <inheritdoc/>
            /// <param name="current">The WoT projection handle.</param>
            /// <param name="document">The WoT projection document.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<WotProjectionHandle> ImmediateReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return inner.ImmediateReloadAsync(current, document, cancellationToken);
            }

            /// <inheritdoc/>
            /// <param name="handle">The WoT projection handle.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask RemoveAsync(WotProjectionHandle handle, CancellationToken cancellationToken = default)
            {
                await inner.RemoveAsync(handle, cancellationToken).ConfigureAwait(false);
                published([WotProjectionChange.Remove(handle)]);
            }
        }

        private sealed class ObservedCapture(
            IWotProjectionPublicationCapture inner,
            Action<ArrayOf<WotProjectionChange>> published,
            Action<ArrayOf<WotProjectionChange>>? validate,
            Action<ArrayOf<WotProjectionChange>>? prepare,
            Action<string, IOperationContext?>? contextObserved)
            : IWotProjectionPublicationCapture
        {
            /// <inheritdoc/>
            /// <param name="callerContext">The executing request's context, if any.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotProjectionPublication> BeginAsync(
                IOperationContext? callerContext,
                CancellationToken cancellationToken)
            {
                contextObserved?.Invoke("begin", callerContext);
                IWotProjectionPublication invocation = await inner.BeginAsync(callerContext, cancellationToken)
                    .ConfigureAwait(false);
                return new ObservedInvocation(invocation, published, validate, prepare);
            }
        }

        private sealed class ObservedInvocation(
            IWotProjectionPublication inner,
            Action<ArrayOf<WotProjectionChange>> published,
            Action<ArrayOf<WotProjectionChange>>? validate,
            Action<ArrayOf<WotProjectionChange>>? prepare)
            : IWotProjectionValidationPublication
        {
            /// <summary>
            /// Gets the is current for <c>ObservedInvocation</c>.
            /// </summary>
            public bool IsCurrent => inner.IsCurrent;

            /// <inheritdoc/>
            /// <param name="inspectAsync">The inspect async.</param>
            /// <param name="views">The WoT prepared view publication.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask ValidateAsync(
                ArrayOf<WotProjectionChange> changes,
                Func<IWotPreparedProjectionPublication, CancellationToken, ValueTask> inspectAsync,
                IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                if (inner is not IWotProjectionValidationPublication validation)
                {
                    throw new NotSupportedException("The observed stock owner must support private validation.");
                }
                validate?.Invoke(changes);
                return validation.ValidateAsync(changes, inspectAsync, views, cancellationToken);
            }

            /// <inheritdoc/>
            /// <param name="views">The WoT prepared view publication.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                ArrayOf<WotProjectionChange> changes, IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                prepare?.Invoke(changes);
                IWotPreparedProjectionPublication unit = await inner.PrepareAsync(changes, views, cancellationToken)
                    .ConfigureAwait(false);
                return new ObservedUnit(unit, changes, published);
            }

            public ValueTask DisposeAsync()
            {
                return inner.DisposeAsync();
            }

            /// <inheritdoc/>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotPreparedProjectionPublication> PrepareReadImagesAsync(
                ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default)
            {
                IWotPreparedProjectionPublication unit = await inner.PrepareReadImagesAsync(images, cancellationToken)
                    .ConfigureAwait(false);
                return new ObservedUnit(unit, [], published);
            }
        }

        private sealed class ObservedUnit(
            IWotPreparedProjectionPublication inner,
            ArrayOf<WotProjectionChange> changes,
            Action<ArrayOf<WotProjectionChange>> published) : IWotPreparedProjectionPublication
        {
            /// <summary>
            /// Gets the projections for <c>ObservedUnit</c>.
            /// </summary>
            public ArrayOf<WotProjectionHandle> Projections => inner.Projections;

            /// <summary>
            /// Gets the view graph for <c>ObservedUnit</c>.
            /// </summary>
            public WotPreparedViewGraphState? ViewGraph => inner.ViewGraph;

            /// <summary>
            /// Gets the is committed for <c>ObservedUnit</c>.
            /// </summary>
            public bool IsCommitted => inner.IsCommitted;

            /// <summary>
            /// Gets the cleanup failure for <c>ObservedUnit</c>.
            /// </summary>
            public Exception? CleanupFailure => inner.CleanupFailure;

            public void BindReadImages(ArrayOf<INodeManagerReadImage> images)
            {
                inner.BindReadImages(images);
            }

            /// <inheritdoc/>
            /// <param name="decideAsync">The decide async.</param>
            /// <param name="publishCommittedState">The publish committed state.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
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
        private readonly bool m_forceLeasedContent;
        private readonly PreparedWotTestStorage m_configuredStorage;
        private readonly List<(WotRegistryService Registry, FileWotRegistryStore Store)> m_registries = [];
        private readonly List<PreparedWotTestStorage> m_storages = [];
    }
}
