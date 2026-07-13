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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the Optional OPC 10000-12 §7.10.3 <c>ServerConfigurationType</c>
    /// surface bound by <see cref="ConfigurationNodeManager"/>: identity
    /// Properties, <c>HasSecureElement</c>/<c>InApplicationSetup</c>, the
    /// <c>ResetToServerDefaults</c> Method security/transaction rules, and the
    /// <c>ConfigurationFile</c> read/update flow.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [NonParallelizable]
    [Parallelizable(ParallelScope.None)]
    public class ServerConfigurationSurfaceTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();
        private static readonly ByteString s_initialConfig = ByteString.From([0x10, 0x20, 0x30, 0x40]);

        /// <summary>
        /// The effective maximum configuration file size enforced by
        /// <see cref="ApplicationConfigurationFile"/>, fetched via reflection
        /// from the private <c>kMaxConfigurationFileSize</c> constant so the
        /// boundary tests stay correct even if the limit is retuned.
        /// </summary>
        private static readonly long s_maxConfigurationFileSize = GetMaxConfigurationFileSize();

        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task IdentityPropertiesAlwaysExposedAndValuedAsync()
        {
            Harness harness = await CreateHarnessAsync(new ServerConfigurationOptions()).ConfigureAwait(false);
            using (harness.Manager)
            {
                ServerConfigurationState node = harness.Node;
                Assert.Multiple(() =>
                {
                    Assert.That(node.ApplicationUri, Is.Not.Null);
                    Assert.That(node.ProductUri, Is.Not.Null);
                    Assert.That(node.ApplicationType, Is.Not.Null);
                    Assert.That(node.ApplicationNames, Is.Not.Null);
                });

                Assert.That(node.ApplicationUri!.Value, Is.EqualTo(m_fixture.Config.ApplicationUri));
                Assert.That(node.ApplicationType!.Value, Is.EqualTo(m_fixture.Config.ApplicationType));
                Assert.That(node.ApplicationNames!.Value.Count, Is.EqualTo(1));
                Assert.That(node.ApplicationNames.Value[0].Text, Is.EqualTo(m_fixture.Config.ApplicationName));
            }
        }

        [Test]
        public async Task OptionalMembersSuppressedWhenNotConfiguredAsync()
        {
            Harness harness = await CreateHarnessAsync(new ServerConfigurationOptions()).ConfigureAwait(false);
            using (harness.Manager)
            {
                ServerConfigurationState node = harness.Node;
                Assert.Multiple(() =>
                {
                    Assert.That(node.HasSecureElement, Is.Null);
                    Assert.That(node.InApplicationSetup, Is.Null);
                    Assert.That(node.ResetToServerDefaults, Is.Null);
                    Assert.That(node.ConfigurationFile, Is.Null);
                });
            }
        }

        [Test]
        public async Task HasSecureElementExposedWithValueWhenConfiguredAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { HasSecureElement = true }).ConfigureAwait(false);
            using (harness.Manager)
            {
                Assert.That(harness.Node.HasSecureElement, Is.Not.Null);
                Assert.That(harness.Node.HasSecureElement!.Value, Is.True);
            }
        }

        [Test]
        public async Task InApplicationSetupExposedWithValueWhenConfiguredAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { InApplicationSetup = false }).ConfigureAwait(false);
            using (harness.Manager)
            {
                Assert.That(harness.Node.InApplicationSetup, Is.Not.Null);
                Assert.That(harness.Node.InApplicationSetup!.Value, Is.False);
            }
        }

        [Test]
        public async Task ResetToServerDefaultsExposedOnlyWhenProviderConfiguredAsync()
        {
            Harness withProvider = await CreateHarnessAsync(
                new ServerConfigurationOptions { ResetProvider = new FakeResetProvider() }).ConfigureAwait(false);
            using (withProvider.Manager)
            {
                Assert.That(withProvider.Node.ResetToServerDefaults, Is.Not.Null);
                Assert.That(withProvider.Node.ResetToServerDefaults!.OnCallMethod2Async, Is.Not.Null);
            }

            Harness without = await CreateHarnessAsync(new ServerConfigurationOptions()).ConfigureAwait(false);
            using (without.Manager)
            {
                Assert.That(without.Node.ResetToServerDefaults, Is.Null);
            }
        }

        [Test]
        public async Task ConfigurationFileExposedWithChildrenWhenProviderConfiguredAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = new FakeConfigurationFileProvider(s_initialConfig) })
                .ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState? file = harness.Node.ConfigurationFile;
                Assert.That(file, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(file!.Open, Is.Not.Null);
                    Assert.That(file.Read, Is.Not.Null);
                    Assert.That(file.Write, Is.Not.Null);
                    Assert.That(file.Close, Is.Not.Null);
                    Assert.That(file.CloseAndUpdate, Is.Not.Null);
                    Assert.That(file.ConfirmUpdate, Is.Not.Null);
                    Assert.That(file.Open!.OnCallAsync, Is.Not.Null);
                    Assert.That(file.CloseAndUpdate!.OnCallAsync, Is.Not.Null);
                });
            }
        }

        [Test]
        public async Task ConfigurationFilePropertiesSeededFromProviderAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig) { CurrentVersion = 7 };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 12345.0
                }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                Assert.Multiple(() =>
                {
                    Assert.That(file.CurrentVersion!.Value, Is.EqualTo(7u));
                    Assert.That(file.ActivityTimeout!.Value, Is.EqualTo(12345.0));
                    Assert.That(file.SupportedDataType!.Value, Is.EqualTo(DataTypeIds.ApplicationConfigurationDataType));
                    Assert.That(file.Writable!.Value, Is.True);
                });
            }
        }

        [Test]
        public async Task ConfigurationFileReadReturnsProviderContentAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(1, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 1024, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(read.ServiceResult), Is.True);
                Assert.That(read.Data.ToArray(), Is.EqualTo(s_initialConfig.ToArray()));

                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    ctx, file.Close, file.NodeId, open.FileHandle, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(close.ServiceResult), Is.True);
                Assert.That(provider.ApplyCount, Is.Zero);
            }
        }

        [Test]
        public async Task ConfigurationFileWriteThenCloseAndUpdateAppliesAtomicallyAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(2, 1));
                var updated = ByteString.From([0xAA, 0xBB, 0xCC]);

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True,
                    "opening the ConfigurationFile for writing must block new transactions/ApplyChanges");

                WriteMethodStateResult write = await file.Write!.OnCallAsync!(
                    ctx, file.Write, file.NodeId, open.FileHandle, updated, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(write.ServiceResult), Is.True);

                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                    Assert.That(provider.ApplyCount, Is.EqualTo(1));
                    Assert.That(provider.LastApplied.ToArray(), Is.EqualTo(updated.ToArray()));
                    Assert.That(result.NewVersion, Is.EqualTo(2u));
                    Assert.That(result.UpdateId.Guid, Is.EqualTo(Guid.Empty));
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False,
                        "committing the update must clear the write-open flag");
                });
            }
        }

        [Test]
        public async Task ConfigurationFileCloseDiscardsChangesWithoutApplyingAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(3, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x01]), CancellationToken.None).ConfigureAwait(false);

                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    ctx, file.Close, file.NodeId, open.FileHandle, CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(close.ServiceResult), Is.True);
                Assert.That(provider.ApplyCount, Is.Zero);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
            }
        }

        [Test]
        public async Task ConfigurationFileCloseAndUpdateWithInvalidConfigRollsBackAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig) { ValidateShouldThrow = true };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(4, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x99]), CancellationToken.None).ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                    Assert.That(provider.ApplyCount, Is.Zero, "no change may be applied when validation fails");
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);
                });
            }
        }

        [Test]
        public async Task ConfigurationFileApplyFailureLeavesNoPartialUpdateAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig) { ApplyShouldThrow = true };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(5, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x77]), CancellationToken.None).ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(result.NewVersion, Is.Zero);
                    Assert.That(provider.CurrentVersion, Is.EqualTo(1u), "the active version must be unchanged");
                });
            }
        }

        [Test]
        public async Task ConfigurationFileCloseAndUpdateWrongVersionReturnsBadInvalidStateAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(6, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                ConfigurationFileCloseAndUpdateMethodStateResult result = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 999).ConfigureAwait(false);

                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(provider.ApplyCount, Is.Zero);
            }
        }

        [Test]
        public async Task ConfigurationFileOpenUnsupportedModeReturnsBadInvalidArgumentAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(7, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Write | OpenFileMode.EraseExisting)
                    .ConfigureAwait(false);
                Assert.That(open.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            }
        }

        [Test]
        public async Task ConfigurationFileReadWithWrongHandleReturnsBadInvalidArgumentAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(8, 1));

                await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, 987654u, 16, CancellationToken.None).ConfigureAwait(false);
                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            }
        }

        [Test]
        public async Task ConfigurationFileReadAtExactMaxBoundaryReturnsGoodAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(101, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // Requesting exactly the maximum is allowed: length == remaining
                // must not be rejected.
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(read.ServiceResult), Is.True);
                Assert.That(read.Data.ToArray(), Is.EqualTo(s_initialConfig.ToArray()));
            }
        }

        [Test]
        public async Task ConfigurationFileReadOneByteOverMaxBoundaryReturnsBadEncodingLimitsExceededAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(102, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // One byte past the boundary must be rejected before any
                // buffer is allocated.
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize + 1,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(read.Data.IsNull, Is.True);
            }
        }

        [Test]
        public async Task ConfigurationFileReadWithIntMaxValueLengthReturnsBadEncodingLimitsExceededAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(103, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // int.MaxValue exceeds Array.MaxLength for byte[] and would
                // throw OutOfMemoryException if allocated before the size
                // check. The fix must reject it up-front with
                // BadEncodingLimitsExceeded instead of allocating and
                // truncating.
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, int.MaxValue,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(read.Data.IsNull, Is.True);
            }
        }

        [Test]
        public async Task ConfigurationFileReadCumulativeWithinRemainingSucceedsAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(104, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // Normal remaining-length behavior: a first, small Read
                // consumes part of the quota; a second Read that fits inside
                // what is left must still succeed and return the rest of the
                // content.
                ReadMethodStateResult first = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 2, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
                Assert.That(first.Data.ToArray(), Is.EqualTo(new byte[] { 0x10, 0x20 }));

                ReadMethodStateResult second = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize - 2,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(second.ServiceResult), Is.True);
                Assert.That(second.Data.ToArray(), Is.EqualTo(new byte[] { 0x30, 0x40 }));
            }
        }

        [Test]
        public async Task ConfigurationFileReadCumulativeExceedingRemainingReturnsBadEncodingLimitsExceededAsync()
        {
            var content = ByteString.From(new byte[s_maxConfigurationFileSize]);
            var provider = new FakeConfigurationFileProvider(content);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(105, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);

                // Consume all but the last 5 bytes of the quota with real
                // data, then request 6 more: the cumulative total (already
                // processed + requested) exceeds the maximum even though
                // neither call individually looks oversized.
                ReadMethodStateResult first = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, (int)s_maxConfigurationFileSize - 5,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
                Assert.That(first.Data.Length, Is.EqualTo(s_maxConfigurationFileSize - 5));

                ReadMethodStateResult second = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 6, CancellationToken.None).ConfigureAwait(false);
                Assert.That(second.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(second.Data.IsNull, Is.True);

                // The boundary itself (exactly the remaining 5 bytes) must
                // still succeed.
                ReadMethodStateResult third = await file.Read.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 5, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(third.ServiceResult), Is.True);
                Assert.That(third.Data.Length, Is.EqualTo(5));
            }
        }

        [Test]
        public async Task ConfigurationFileConfirmUpdateFlowAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig) { RequiresConfirmation = true };
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(9, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                await file.Write!.OnCallAsync!(ctx, file.Write, file.NodeId, open.FileHandle,
                    ByteString.From([0x42]), CancellationToken.None).ConfigureAwait(false);

                ConfigurationFileCloseAndUpdateMethodStateResult update = await CloseAndUpdateAsync(
                    file, ctx, open.FileHandle, versionToUpdate: 1, revertAfterTime: 0, restartDelayTime: 0)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(update.ServiceResult), Is.True);
                Assert.That(update.UpdateId.Guid, Is.Not.EqualTo(Guid.Empty),
                    "a confirmation-required update returns a non-empty UpdateId");

                ConfigurationFileConfirmUpdateMethodStateResult wrong = await file.ConfirmUpdate!.OnCallAsync!(
                    ctx, file.ConfirmUpdate, file.NodeId, Uuid.NewUuid(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(wrong.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));

                ConfigurationFileConfirmUpdateMethodStateResult confirm = await file.ConfirmUpdate.OnCallAsync!(
                    ctx, file.ConfirmUpdate, file.NodeId, update.UpdateId, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(confirm.ServiceResult), Is.True);
                Assert.That(provider.ConfirmCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ConfigurationFileUpdateNonAdminThrowsBadUserAccessDeniedAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext anonymous = CreateContext(UserTokenType.Anonymous, MessageSecurityMode.SignAndEncrypt);

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await OpenAsync(file, anonymous, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task ConfigurationFileOpenWriteWhileOtherSessionTransactionActiveReturnsBadTransactionPendingAsync()
        {
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ConfigurationFileProvider = provider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;

                var otherSession = new NodeId(4242, 1);
                harness.Coordinator.Stage(otherSession, new PushConfigurationOperation
                {
                    CommitAsync = _ => Task.CompletedTask
                });

                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(1, 1));
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write).ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));

                harness.Coordinator.CancelChanges(otherSession);
            }
        }

        [Test]
        public async Task ResetToServerDefaultsNonAdminThrowsBadUserAccessDeniedAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ResetProvider = new FakeResetProvider() }).ConfigureAwait(false);
            using (harness.Manager)
            {
                MethodState reset = harness.Node.ResetToServerDefaults!;
                SessionSystemContext ctx = CreateContext(UserTokenType.Anonymous, MessageSecurityMode.SignAndEncrypt);

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await reset.OnCallMethod2Async!(ctx, reset, harness.Node.NodeId, ArrayOf<Variant>.Empty,
                        new List<Variant>(), CancellationToken.None).ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task ResetToServerDefaultsUnauthenticatedChannelThrowsBadSecurityModeInsufficientAsync()
        {
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ResetProvider = new FakeResetProvider() }).ConfigureAwait(false);
            using (harness.Manager)
            {
                MethodState reset = harness.Node.ResetToServerDefaults!;
                SessionSystemContext ctx = CreateContext(
                    UserTokenType.UserName, MessageSecurityMode.None, ObjectIds.WellKnownRole_SecurityAdmin);

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await reset.OnCallMethod2Async!(ctx, reset, harness.Node.NodeId, ArrayOf<Variant>.Empty,
                        new List<Variant>(), CancellationToken.None).ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            }
        }

        [Test]
        public async Task ResetToServerDefaultsWhileOtherSessionTransactionActiveReturnsBadTransactionPendingAsync()
        {
            var resetProvider = new FakeResetProvider();
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions { ResetProvider = resetProvider }).ConfigureAwait(false);
            using (harness.Manager)
            {
                var otherSession = new NodeId(7777, 1);
                harness.Coordinator.Stage(otherSession, new PushConfigurationOperation
                {
                    CommitAsync = _ => Task.CompletedTask
                });

                MethodState reset = harness.Node.ResetToServerDefaults!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(1, 1));

                ServiceResult result = await reset.OnCallMethod2Async!(
                    ctx, reset, harness.Node.NodeId, ArrayOf<Variant>.Empty, new List<Variant>(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));
                Assert.That(resetProvider.InvocationCount, Is.Zero);

                harness.Coordinator.CancelChanges(otherSession);
            }
        }

        [Test]
        public async Task ConfigurationFileStaleInactivityCallbackDoesNotCloseRefreshedHandleAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(2001, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True);

                ControllableTimer[] armed = ActivityTimers(timeProvider);
                Assert.That(armed, Has.Length.EqualTo(1), "Open must arm exactly one inactivity timer");
                ControllableTimer t1 = armed[0];

                // Activity supersedes T1 and arms T2.
                GetPositionMethodStateResult pos = await file.GetPosition!.OnCallAsync!(
                    ctx, file.GetPosition, file.NodeId, open.FileHandle, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(pos.ServiceResult), Is.True);
                Assert.That(t1.IsDisposed, Is.True, "activity must dispose the superseded timer");

                // A callback still queued by the superseded T1 must not touch the
                // live handle refreshed by the activity above.
                t1.Fire();

                Assert.Multiple(() =>
                {
                    Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1),
                        "a stale timer callback must not close the refreshed handle");
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True,
                        "a stale timer callback must not clear the write-open flag");
                });

                // The current timer callback still closes the (now inactive) handle.
                ControllableTimer t2 = ActivityTimers(timeProvider)[^1];
                t2.Fire();

                Assert.Multiple(() =>
                {
                    Assert.That(file.OpenCount!.Value, Is.Zero,
                        "the current timer must close the inactive handle");
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False,
                        "closing on inactivity must clear the write-open flag");
                });
            }
        }

        [Test]
        public async Task ConfigurationFileStaleInactivityCallbackDoesNotCloseOtherSessionHandleAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext sessionA = CreateAdminContextForSession(new NodeId(3001, 1));
                SessionSystemContext sessionB = CreateAdminContextForSession(new NodeId(3002, 1));

                OpenMethodStateResult openA = await OpenAsync(file, sessionA, OpenFileMode.Read)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(openA.ServiceResult), Is.True);
                ControllableTimer t1 = ActivityTimers(timeProvider)[^1];

                // Session B opens; last-open-wins evicts A's handle and arms a new
                // timer for B.
                OpenMethodStateResult openB = await OpenAsync(file, sessionB, OpenFileMode.Read)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(openB.ServiceResult), Is.True);
                Assert.That(t1.IsDisposed, Is.True);

                // Session A's stale timer callback must not close Session B's handle.
                t1.Fire();

                ReadMethodStateResult readB = await file.Read!.OnCallAsync!(
                    sessionB, file.Read, file.NodeId, openB.FileHandle, 4, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1),
                        "Session A's stale callback must not close Session B's handle");
                    Assert.That(ServiceResult.IsGood(readB.ServiceResult), Is.True,
                        "Session B's handle must stay usable after Session A's stale callback");
                });
            }
        }

        [Test]
        public async Task ConfigurationFileCurrentInactivityCallbackClosesHandleAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(4001, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.True);

                ControllableTimer current = ActivityTimers(timeProvider)[^1];
                current.Fire();

                Assert.Multiple(() =>
                {
                    Assert.That(file.OpenCount!.Value, Is.Zero,
                        "the current inactivity timer must close the open handle");
                    Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False,
                        "inactivity close must clear the coordinator write-open flag");
                });

                // The handle is gone: a Read with the old handle now fails.
                ReadMethodStateResult read = await file.Read!.OnCallAsync!(
                    ctx, file.Read, file.NodeId, open.FileHandle, 4, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
        }

        [Test]
        public async Task ConfigurationFileCloseInvalidatesQueuedInactivityCallbackAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(5001, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                    .ConfigureAwait(false);
                ControllableTimer t1 = ActivityTimers(timeProvider)[^1];

                CloseMethodStateResult close = await file.Close!.OnCallAsync!(
                    ctx, file.Close, file.NodeId, open.FileHandle, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(close.ServiceResult), Is.True);
                Assert.That(t1.IsDisposed, Is.True);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);

                // A callback queued before Close must be an inert no-op.
                Assert.That(() => t1.Fire(), Throws.Nothing);
                Assert.That(file.OpenCount!.Value, Is.Zero);
                Assert.That(harness.Coordinator.HasOpenTrustListWriter, Is.False);

                // Reopen: the superseded callback must not close the new handle.
                OpenMethodStateResult reopen = await OpenAsync(file, ctx, OpenFileMode.Read)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(reopen.ServiceResult), Is.True);

                t1.Fire();
                Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1),
                    "a callback queued before Close must not close a later reopened handle");
            }
        }

        [Test]
        public async Task ConfigurationFileDisposeInvalidatesQueuedInactivityCallbackAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);

            ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
            SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(6001, 1));

            OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read | OpenFileMode.Write)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(open.ServiceResult), Is.True);
            ControllableTimer t1 = ActivityTimers(timeProvider)[^1];

            // Disposing the manager disposes the ApplicationConfigurationFile.
            harness.Manager.Dispose();
            Assert.That(t1.IsDisposed, Is.True);

            // A callback queued before Dispose must be a harmless no-op.
            Assert.That(() => t1.Fire(), Throws.Nothing);
        }

        [Test]
        public async Task ConfigurationFileRepeatedActivityDoesNotLeakTimersOrChangeOpenCountAsync()
        {
            var timeProvider = new ControllableTimeProvider();
            var provider = new FakeConfigurationFileProvider(s_initialConfig);
            Harness harness = await CreateHarnessAsync(
                new ServerConfigurationOptions
                {
                    ConfigurationFileProvider = provider,
                    ConfigurationFileActivityTimeout = 1000.0
                },
                timeProvider).ConfigureAwait(false);
            using (harness.Manager)
            {
                ApplicationConfigurationFileState file = harness.Node.ConfigurationFile!;
                SessionSystemContext ctx = CreateAdminContextForSession(new NodeId(7001, 1));

                OpenMethodStateResult open = await OpenAsync(file, ctx, OpenFileMode.Read).ConfigureAwait(false);
                Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1));

                for (int i = 0; i < 5; i++)
                {
                    GetPositionMethodStateResult pos = await file.GetPosition!.OnCallAsync!(
                        ctx, file.GetPosition, file.NodeId, open.FileHandle, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(pos.ServiceResult), Is.True);
                }

                ControllableTimer[] activity = ActivityTimers(timeProvider);
                int undisposed = activity.Count(t => !t.IsDisposed);

                Assert.Multiple(() =>
                {
                    Assert.That(activity, Has.Length.EqualTo(6),
                        "each Open/activity arms exactly one timer (1 Open + 5 activities)");
                    Assert.That(undisposed, Is.EqualTo(1),
                        "only the most recently armed timer may remain live - no timer leak");
                    Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1),
                        "repeated activity must not change OpenCount");
                });
            }
        }

        private static ControllableTimer[] ActivityTimers(ControllableTimeProvider timeProvider)
        {
            // The ApplicationConfigurationFile arms its inactivity timer with an
            // ActivityTimerState; filter on it so any unrelated node-manager
            // timer created through the same provider is ignored.
            return [.. timeProvider.Timers.Where(
                t => string.Equals(t.State?.GetType().Name, "ActivityTimerState", StringComparison.Ordinal))];
        }

        private static ValueTask<OpenMethodStateResult> OpenAsync(
            ApplicationConfigurationFileState file, ISystemContext ctx, OpenFileMode mode)
        {
            return file.Open!.OnCallAsync!(ctx, file.Open, file.NodeId, (byte)mode, CancellationToken.None);
        }

        private static long GetMaxConfigurationFileSize()
        {
            FieldInfo? field = typeof(ApplicationConfigurationFile).GetField(
                "kMaxConfigurationFileSize", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "kMaxConfigurationFileSize field not found via reflection");
            return (long)field!.GetRawConstantValue()!;
        }

        private static ValueTask<ConfigurationFileCloseAndUpdateMethodStateResult> CloseAndUpdateAsync(
            ApplicationConfigurationFileState file,
            ISystemContext ctx,
            uint fileHandle,
            uint versionToUpdate,
            double revertAfterTime = 0,
            double restartDelayTime = 0)
        {
            return file.CloseAndUpdate!.OnCallAsync!(
                ctx,
                file.CloseAndUpdate,
                file.NodeId,
                fileHandle,
                versionToUpdate,
                ArrayOf<ConfigurationUpdateTargetType>.Empty,
                revertAfterTime,
                restartDelayTime,
                CancellationToken.None);
        }

        private async Task<Harness> CreateHarnessAsync(
            ServerConfigurationOptions options,
            TimeProvider? timeProvider = null)
        {
            IServerInternal serverInternal = m_server.CurrentInstance;
            var coordinator = new PushConfigurationTransactionCoordinator(serverInternal.Telemetry);
            var manager = new ConfigurationNodeManager(
                serverInternal,
                m_fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: timeProvider,
                coordinator,
                pendingKeyStore: null,
                keyGenerator: null,
                trustListEffectHandler: null,
                serverConfigurationOptions: options);

            IDictionary<NodeId, IList<IReference>> externalReferences =
                new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None).ConfigureAwait(false);
            manager.CreateServerConfiguration(serverInternal.DefaultSystemContext, m_fixture.Config);

            var node = manager.FindPredefinedNode<ServerConfigurationState>(ObjectIds.ServerConfiguration);
            Assert.That(node, Is.Not.Null, "ServerConfiguration node not materialised");
            return new Harness(manager, node, coordinator);
        }

        private static SessionSystemContext CreateContext(
            UserTokenType tokenType,
            MessageSecurityMode securityMode,
            params NodeId[] grantedRoles)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(tokenType);
            identity.Setup(i => i.DisplayName).Returns(tokenType.ToString());
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(grantedRoles));
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                identity.Object);
            return new SessionSystemContext(operationContext, s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private static SessionSystemContext CreateAdminContextForSession(NodeId sessionId)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(nameof(UserTokenType.UserName));
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(ObjectIds.WellKnownRole_SecurityAdmin));

            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);
            session.Setup(s => s.PreferredLocales).Returns([]);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                session.Object);
            return new SessionSystemContext(operationContext, s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private sealed class Harness(
            ConfigurationNodeManager manager,
            ServerConfigurationState node,
            PushConfigurationTransactionCoordinator coordinator)
        {
            public ConfigurationNodeManager Manager { get; } = manager;
            public ServerConfigurationState Node { get; } = node;
            public PushConfigurationTransactionCoordinator Coordinator { get; } = coordinator;
        }

        private sealed class FakeConfigurationFileProvider : IApplicationConfigurationFileProvider
        {
            private ByteString m_content;

            public FakeConfigurationFileProvider(ByteString initial)
            {
                m_content = initial;
                CurrentVersion = 1;
                LastUpdateTime = DateTime.UtcNow;
            }

            public uint CurrentVersion { get; set; }
            public DateTime LastUpdateTime { get; private set; }
            public bool RequiresConfirmation { get; set; }
            public bool ValidateShouldThrow { get; set; }
            public bool ApplyShouldThrow { get; set; }
            public int ApplyCount { get; private set; }
            public int ConfirmCount { get; private set; }
            public int RevertCount { get; private set; }
            public ByteString LastApplied { get; private set; }

            public ValueTask<ByteString> ReadConfigurationAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<ByteString>(m_content);
            }

            public ValueTask ValidateConfigurationAsync(ByteString configuration, CancellationToken cancellationToken = default)
            {
                if (ValidateShouldThrow)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "invalid configuration");
                }
                return default;
            }

            public ValueTask ApplyConfigurationAsync(ByteString configuration, CancellationToken cancellationToken = default)
            {
                if (ApplyShouldThrow)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "apply failed");
                }
                ApplyCount++;
                LastApplied = configuration;
                m_content = configuration;
                CurrentVersion++;
                LastUpdateTime = DateTime.UtcNow;
                return default;
            }

            public ValueTask ConfirmUpdateAsync(CancellationToken cancellationToken = default)
            {
                ConfirmCount++;
                return default;
            }

            public ValueTask RevertUpdateAsync(CancellationToken cancellationToken = default)
            {
                RevertCount++;
                return default;
            }
        }

        private sealed class FakeResetProvider : IServerConfigurationResetProvider
        {
            public int InvocationCount { get; private set; }

            public ValueTask ResetToServerDefaultsAsync(CancellationToken cancellationToken = default)
            {
                InvocationCount++;
                return default;
            }
        }

        /// <summary>
        /// A <see cref="TimeProvider"/> whose timers never fire on their own:
        /// every created timer is recorded and only fires when a test invokes
        /// <see cref="ControllableTimer.Fire"/>. This makes inactivity-timer
        /// behaviour fully deterministic and lets a test deliberately fire a
        /// superseded or disposed timer to simulate a callback that was already
        /// queued when the timer was replaced.
        /// </summary>
        private sealed class ControllableTimeProvider : TimeProvider
        {
            private readonly List<ControllableTimer> m_timers = [];
            private readonly Lock m_sync = new();

            public IReadOnlyList<ControllableTimer> Timers
            {
                get
                {
                    lock (m_sync)
                    {
                        return [.. m_timers];
                    }
                }
            }

            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                var timer = new ControllableTimer(callback, state);
                lock (m_sync)
                {
                    m_timers.Add(timer);
                }
                return timer;
            }
        }

        /// <summary>
        /// A test <see cref="ITimer"/> that captures its callback and state and
        /// fires only when a test calls <see cref="Fire"/>, deliberately even
        /// after <see cref="Dispose"/>, so a queued callback from a superseded
        /// timer can be simulated.
        /// </summary>
        private sealed class ControllableTimer : ITimer
        {
            private readonly TimerCallback m_callback;

            public ControllableTimer(TimerCallback callback, object? state)
            {
                m_callback = callback;
                State = state;
            }

            public object? State { get; }

            public bool IsDisposed { get; private set; }

            public void Fire()
            {
                m_callback(State);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                return !IsDisposed;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }

            public ValueTask DisposeAsync()
            {
                IsDisposed = true;
                return default;
            }
        }
    }
}
