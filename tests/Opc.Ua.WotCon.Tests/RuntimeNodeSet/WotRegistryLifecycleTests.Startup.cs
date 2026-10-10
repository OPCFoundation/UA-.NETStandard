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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.RuntimeNodeSet
{
    public sealed partial class WotRegistryLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task StoredRegistryMaterializesBeforeRegistrationReturnsAsync(bool populated)
        {
            await m_server.NodeManagerLifecycle.RemoveAsync(
                m_registryRegistration, callerContext: null).ConfigureAwait(false);
            m_coordinator.Dispose();
            m_registry.Dispose();
            int registrationsBefore = m_server.NodeManagerLifecycle.Registrations.Count;

            using (FileWotRegistryStore store = m_testStorage!.OpenStore())
            using (var writer = new WotRegistryService(store, m_options.Bounds, m_options.IdentityBindings))
            {
                await writer.InitializeAsync().ConfigureAwait(false);
                if (populated)
                {
                    await writer.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = "sensor",
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(SensorConverter.BuildContent(1))
                    }).ConfigureAwait(false);
                }
            }

            m_startupStore = m_testStorage!.OpenStore();
            m_registry = new WotRegistryService(m_startupStore, m_options.Bounds, m_options.IdentityBindings);
            await m_registry.InitializeAsync().ConfigureAwait(false);
            Assert.That(m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "sensor") is not null, Is.EqualTo(populated));

            var host = new DeadlineProjectionHost(new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle));
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, host, documentConverter: new SensorConverter());
            var factory = new WotRegistryNodeManagerFactory(m_options, m_registry, m_coordinator);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            m_registryRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(factory, callerContext: null, cancellation.Token).ConfigureAwait(false);

            Assert.That(host.DeadlineExpired, Is.False,
                "Startup must not wait for its own lifecycle registration gate.");
            Assert.That(host.AddCalls, Is.EqualTo(populated ? 1 : 0));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.Count,
                Is.EqualTo(registrationsBefore + (populated ? 2 : 1)));
            if (populated)
            {
                int index = m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
                Assert.That(index, Is.GreaterThan(0));
                var nodeId = new NodeId(kValueNodeId, checked((ushort)index));
                DataValue value = await ReadValueAsync(nodeId).ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.GetValue<int>(-1), Is.EqualTo(1),
                    "The persisted generation must be readable without an explicit Refresh call.");
            }
        }

        private NodeManagerRegistration m_registryRegistration = null!;
        private FileWotRegistryStore? m_startupStore;

        private sealed class DeadlineProjectionHost(IWotInvocationProjectionHost inner) : IWotInvocationProjectionHost
        {
            public int AddCalls => Volatile.Read(ref m_addCalls);

            public bool DeadlineExpired => Volatile.Read(ref m_deadlineExpired) != 0;

            public bool SupportsPreparedPublication => inner.SupportsPreparedPublication;

            public ArrayOf<WoTAtomicityEnum> SupportedAtomicities => inner.SupportedAtomicities;

            public IWotProjectionPublicationCapture CapturePublication()
            {
                return new DeadlineCapture(this, inner.CapturePublication());
            }

            public async ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                ArrayOf<WotProjectionChange> changes,
                IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                RecordAdditions(changes);
                IWotPreparedProjectionPublication prepared = await WithDeadlineAsync(
                    token => inner.PrepareAsync(changes, views, token), cancellationToken).ConfigureAwait(false);
                return new DeadlinePublication(this, prepared);
            }

            public ValueTask<WotProjectionHandle> AddAsync(
                WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_addCalls);
                return WithDeadlineAsync(token => inner.AddAsync(document, token), cancellationToken);
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

            private void RecordAdditions(ArrayOf<WotProjectionChange> changes)
            {
                foreach (WotProjectionChange change in changes)
                {
                    if (change.Current is null && change.Document is not null)
                    {
                        Interlocked.Increment(ref m_addCalls);
                    }
                }
            }

            private async ValueTask<T> WithDeadlineAsync<T>(
                Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    return await action(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref m_deadlineExpired, 1);
                    throw;
                }
            }

            private int m_addCalls;
            private int m_deadlineExpired;

            private sealed class DeadlineCapture(
                DeadlineProjectionHost owner, IWotProjectionPublicationCapture capture)
                : IWotProjectionPublicationCapture
            {
                public async ValueTask<IWotProjectionPublication> BeginAsync(
                    CancellationToken cancellationToken = default)
                {
                    IWotProjectionPublication invocation = await owner.WithDeadlineAsync(
                        capture.BeginAsync, cancellationToken).ConfigureAwait(false);
                    return new DeadlineInvocation(owner, invocation);
                }
            }

            private sealed class DeadlineInvocation(
                DeadlineProjectionHost owner, IWotProjectionPublication inner) : IWotProjectionValidationPublication
            {
                public bool IsCurrent => inner.IsCurrent;

                public async ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                    ArrayOf<WotProjectionChange> changes,
                    IWotPreparedViewPublication? views = null,
                    CancellationToken cancellationToken = default)
                {
                    owner.RecordAdditions(changes);
                    IWotPreparedProjectionPublication prepared = await owner.WithDeadlineAsync(
                        token => inner.PrepareAsync(changes, views, token), cancellationToken).ConfigureAwait(false);
                    return new DeadlinePublication(owner, prepared);
                }

                public async ValueTask<IWotPreparedProjectionPublication> PrepareReadImagesAsync(
                    ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default)
                {
                    IWotPreparedProjectionPublication prepared = await owner.WithDeadlineAsync(
                        token => inner.PrepareReadImagesAsync(images, token), cancellationToken).ConfigureAwait(false);
                    return new DeadlinePublication(owner, prepared);
                }

                public async ValueTask ValidateAsync(
                    ArrayOf<WotProjectionChange> changes,
                    Func<IWotPreparedProjectionPublication, CancellationToken, ValueTask> inspectAsync,
                    IWotPreparedViewPublication? views = null,
                    CancellationToken cancellationToken = default)
                {
                    if (inner is not IWotProjectionValidationPublication validation)
                    {
                        throw new NotSupportedException("The wrapped source owner does not support private validation.");
                    }
                    await owner.WithDeadlineAsync(async token =>
                    {
                        await validation.ValidateAsync(changes, inspectAsync, views, token).ConfigureAwait(false);
                        return true;
                    }, cancellationToken).ConfigureAwait(false);
                }

                public ValueTask DisposeAsync()
                {
                    return inner.DisposeAsync();
                }
            }

            private sealed class DeadlinePublication(
                DeadlineProjectionHost owner, IWotPreparedProjectionPublication inner) : IWotPreparedProjectionPublication
            {
                public ArrayOf<WotProjectionHandle> Projections => inner.Projections;
                public WotPreparedViewGraphState? ViewGraph => inner.ViewGraph;
                public bool IsCommitted => inner.IsCommitted;
                public Exception? CleanupFailure => inner.CleanupFailure;

                public void BindReadImages(ArrayOf<INodeManagerReadImage> images)
                {
                    inner.BindReadImages(images);
                }

                public async ValueTask CommitAsync(
                    Func<CancellationToken, ValueTask> decideAsync, Action publishCommittedState,
                    CancellationToken cancellationToken = default)
                {
                    await owner.WithDeadlineAsync(async token =>
                    {
                        await inner.CommitAsync(decideAsync, publishCommittedState, token).ConfigureAwait(false);
                        return true;
                    }, cancellationToken).ConfigureAwait(false);
                }

                public ValueTask DisposeAsync()
                {
                    return inner.DisposeAsync();
                }
            }
        }
    }
}
