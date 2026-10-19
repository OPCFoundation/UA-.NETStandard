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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// The executing request's operation context reaches the generic lifecycle
    /// admission and preparation seams for refresh, delete and disable, while
    /// callers outside a request pass no context.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotMaterializationCallerContextTests
    {
        private readonly List<(string Phase, IOperationContext? Context)> m_observed = [];
        private WotRegistryService m_registry = null!;
        private FakeWotProjectionHost m_host = null!;
        private WotMaterializationCoordinator m_coordinator = null!;
        private PreparedWotTestRuntime? m_runtime;
        private IDisposable? m_lifecycleRegistration;

        [SetUp]
        public async Task SetUpAsync()
        {
            ClearObserved();
            m_runtime = await PreparedWotTestRuntime.StartAsync().ConfigureAwait(false);
            m_registry = await m_runtime.CreateRegistryAsync().ConfigureAwait(false);
            m_host = new FakeWotProjectionHost();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry,
                m_runtime.Observe(
                    m_host.RecordCommitted,
                    contextObserved: (phase, context) =>
                    {
                        lock (m_observed)
                        {
                            m_observed.Add((phase, context));
                        }
                    }),
                documentConverter: new FakeWotDocumentConverter());
            m_lifecycleRegistration = m_registry.RegisterLifecycleCoordinator(m_coordinator);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_lifecycleRegistration?.Dispose();
            m_lifecycleRegistration = null;
            m_coordinator?.Dispose();
            if (m_runtime is not null)
            {
                await m_runtime.DisposeAsync().ConfigureAwait(false);
                m_runtime = null;
            }
            m_coordinator = null!;
        }

        /// <summary>
        /// A refresh carrying a request context hands that same instance to the capture admission.
        /// </summary>
        [Test]
        public async Task RefreshPassesTheRequestContextToCaptureAdmissionAsync()
        {
            var context = new Mock<IOperationContext>().Object;
            await RegisterTmAsync("tm-a").ConfigureAwait(false);

            await m_coordinator.RefreshAsync(new WotRefreshRequest { CallerContext = context })
                .ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.SameAs(context));
        }

        /// <summary>
        /// A refresh from outside a request admits with no context.
        /// </summary>
        [Test]
        public async Task RefreshWithoutRequestContextAdmitsWithNullAsync()
        {
            await RegisterTmAsync("tm-a").ConfigureAwait(false);

            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.Null);
        }

        /// <summary>
        /// A delete carrying a request context hands that same instance to the capture admission.
        /// </summary>
        [Test]
        public async Task DeletePassesTheRequestContextToCaptureAdmissionAsync()
        {
            var context = new Mock<IOperationContext>().Object;
            await RegisterTmAsync("tm-a").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            ClearObserved();

            await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", null, context, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.SameAs(context));
        }

        /// <summary>
        /// The registry delete overload used outside a request admits with no context.
        /// </summary>
        [Test]
        public async Task DeleteWithoutRequestContextAdmitsWithNullAsync()
        {
            await RegisterTmAsync("tm-a").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            ClearObserved();

            await m_registry.DeleteResourceAsync(WotRegistryGroups.ThingModels, "tm-a").ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.Null);
        }

        /// <summary>
        /// Disabling a loaded resource from a request hands the request context to the capture admission.
        /// </summary>
        [Test]
        public async Task SetEnabledFalsePassesTheRequestContextToCaptureAdmissionAsync()
        {
            var context = new Mock<IOperationContext>().Object;
            await RegisterTmAsync("tm-a").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            ClearObserved();

            await m_coordinator.SetEnabledAsync(
                WotRegistryGroups.ThingModels, "tm-a", false, null, context, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.SameAs(context));
        }

        /// <summary>
        /// Registry disable from a request hands the request context to the capture admission.
        /// </summary>
        [Test]
        public async Task RegistrySetEnabledFalsePassesTheRequestContextToCaptureAdmissionAsync()
        {
            var context = new Mock<IOperationContext>().Object;
            await LoadTmAsync().ConfigureAwait(false);

            await m_registry.SetEnabledAsync(
                WotRegistryGroups.ThingModels, "tm-a", false, null, context, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.SameAs(context));
        }

        /// <summary>
        /// Registry disable from outside a request admits with no context.
        /// </summary>
        [Test]
        public async Task RegistrySetEnabledFalseWithoutRequestContextAdmitsWithNullAsync()
        {
            await LoadTmAsync().ConfigureAwait(false);

            await m_registry.SetEnabledAsync(WotRegistryGroups.ThingModels, "tm-a", false).ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.Null);
        }

        /// <summary>
        /// A group delete from a request hands the request context to the capture admission.
        /// </summary>
        [Test]
        public async Task DeleteGroupPassesTheRequestContextToCaptureAdmissionAsync()
        {
            var context = new Mock<IOperationContext>().Object;
            await LoadTmAsync().ConfigureAwait(false);

            await m_registry.DeleteGroupAsync(WotRegistryGroups.ThingModels, null, context, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.SameAs(context));
        }

        /// <summary>
        /// A group delete from outside a request admits with no context.
        /// </summary>
        [Test]
        public async Task DeleteGroupWithoutRequestContextAdmitsWithNullAsync()
        {
            await LoadTmAsync().ConfigureAwait(false);

            await m_registry.DeleteGroupAsync(WotRegistryGroups.ThingModels).ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.Null);
        }

        /// <summary>
        /// A Version delete from a request hands the request context to the capture admission.
        /// </summary>
        [Test]
        public async Task DeleteVersionPassesTheRequestContextToCaptureAdmissionAsync()
        {
            var context = new Mock<IOperationContext>().Object;
            await LoadTmAsync().ConfigureAwait(false);

            await m_registry.DeleteVersionAsync(
                WotRegistryGroups.ThingModels, "tm-a", "v1", null, context, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.SameAs(context));
        }

        /// <summary>
        /// A Version delete from outside a request admits with no context.
        /// </summary>
        [Test]
        public async Task DeleteVersionWithoutRequestContextAdmitsWithNullAsync()
        {
            await LoadTmAsync().ConfigureAwait(false);

            await m_registry.DeleteVersionAsync(WotRegistryGroups.ThingModels, "tm-a", "v1").ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.Null);
        }

        /// <summary>
        /// A projected-entity delete of the logical resource or of one Version hands the request context
        /// to the capture admission.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DeleteProjectedEntityPassesTheRequestContextToCaptureAdmissionAsync(bool logical)
        {
            var context = new Mock<IOperationContext>().Object;
            await LoadTmAsync().ConfigureAwait(false);

            await m_registry.DeleteProjectedEntityAsync(
                WotRegistryGroups.ThingModels, "tm-a", "v1", logical, null, context, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.SameAs(context));
        }

        /// <summary>
        /// A projected-entity delete from outside a request admits with no context.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DeleteProjectedEntityWithoutRequestContextAdmitsWithNullAsync(bool logical)
        {
            await LoadTmAsync().ConfigureAwait(false);

            await m_registry.DeleteProjectedEntityAsync(WotRegistryGroups.ThingModels, "tm-a", "v1", logical)
                .ConfigureAwait(false);

            Assert.That(Phases("begin"), Is.Not.Empty);
            Assert.That(Phases("begin"), Has.All.Null);
        }

        private async Task LoadTmAsync()
        {
            await RegisterTmAsync("tm-a").ConfigureAwait(false);
            await m_coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);
            ClearObserved();
        }

        private List<IOperationContext?> Phases(string phase)
        {
            lock (m_observed)
            {
                return m_observed.Where(item => item.Phase == phase).Select(item => item.Context).ToList();
            }
        }

        private void ClearObserved()
        {
            lock (m_observed)
            {
                m_observed.Clear();
            }
        }

        private Task<WotRegistryMutationResult> RegisterTmAsync(string resourceId)
        {
            return m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = resourceId,
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(TestMaterialization.Tm("urn:" + resourceId))
            }).AsTask();
        }
    }
}
