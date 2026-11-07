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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Exercises retained identification through the shared asset binding and normal attribute-write pipeline.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class PumpsIdentificationTests
    {
        /// <summary>
        /// Restart uses a fresh file-store instance and owner, preserving locale and the declared field types.
        /// </summary>
        [TestCase("de-DE", "Kuehlwasserpumpe")]
        [TestCase("en-US", "1:2:Pump")]
        [TestCase("en-US", "")]
        [TestCase(null, "unlocalized:name")]
        [TestCase("", null)]
        [TestCase(null, null)]
        public async Task RestartRestoresCommittedIdentityAndLocaleOverConfiguredDefaultsAsync(
            string? locale, string? text)
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "pumps-identity-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new FileSystemAssetConfigurationStore(directory))
                await using (var first = new PumpsServerFixture())
                {
                    await first.StartAsync();
                    IPumpBuilder pump = await CreateAsync(first, "BeforeRestart");
                    await first.Manager.Runtime.WithRetainedIdentificationAsync(pump, store, Permissions);
                    await WriteAsync(first, pump, Opc.Ua.Di.BrowseNames.AssetId, Variant.From("changed-asset"));
                    await WriteAsync(first, pump, Opc.Ua.Di.BrowseNames.ComponentName,
                        Variant.From(new LocalizedText(locale, text)));
                    await WriteAsync(first, pump, Opc.Ua.Machinery.BrowseNames.Location, Variant.From("Skid 2"));
                    var manufacturer = (BaseVariableState)pump.Identification.Child(
                        Opc.Ua.Di.BrowseNames.Manufacturer)!;
                    Assert.That(manufacturer.AccessLevel & AccessLevels.CurrentWrite, Is.Zero);
                    var identity = (BaseVariableState)pump.Identification.Child(
                        Opc.Ua.Di.BrowseNames.ProductInstanceUri)!;
                    Assert.That(identity.AccessLevel & AccessLevels.CurrentWrite, Is.Zero);
                }
                using var restoredStore = new FileSystemAssetConfigurationStore(directory);
                await using var second = new PumpsServerFixture();
                await second.StartAsync();
                IPumpBuilder restored = await CreateAsync(second, "AfterRestart");
                await second.Manager.Runtime.WithRetainedIdentificationAsync(restored, restoredStore, Permissions);
                Assert.That(Value(restored, Opc.Ua.Di.BrowseNames.AssetId), Is.EqualTo(Variant.From("changed-asset")));
                Assert.That(Value(restored, Opc.Ua.Di.BrowseNames.ComponentName),
                    Is.EqualTo(Variant.From(new LocalizedText(locale, text))));
                Assert.That(Value(restored, Opc.Ua.Machinery.BrowseNames.Location), Is.EqualTo(Variant.From("Skid 2")));
                var name = (BaseVariableState)restored.Identification.Child(Opc.Ua.Di.BrowseNames.ComponentName)!;
                Assert.That(name.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.LocalizedText));
                Assert.That(name.RolePermissions, Is.EqualTo(Permissions));
                Assert.That(name.WrappedValue.TryGetValue(out LocalizedText localized), Is.True);
                Assert.That(localized.Locale, Is.EqualTo(string.IsNullOrEmpty(locale) ? null : locale));
                Assert.That(localized.Text, Is.EqualTo(string.IsNullOrEmpty(text) ? null : text));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// An unavailable durable store does not acknowledge or cache an uncommitted value.
        /// </summary>
        [Test]
        public async Task CommitFailureRejectsWriteAndKeepsPreviousIdentityAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "RejectedCommit");
            Mock<IAssetConfigurationStore> store = Store();
            store.Setup(value => value.SetValueAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("storage unavailable"));
            await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, Permissions);
            var variable = (BaseVariableState)pump.Identification.Child(Opc.Ua.Di.BrowseNames.AssetId)!;
            ServiceResult result = await variable.WriteAttributeAsync(fixture.Manager.SystemContext,
                Attributes.Value, NumericRange.Null, new DataValue(Variant.From("uncommitted")));
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadResourceUnavailable));
            Assert.That(variable.WrappedValue, Is.EqualTo(Variant.From("default-asset")));
            Assert.That((await variable.WriteAttributeAsync(fixture.Manager.SystemContext,
                Attributes.Value, NumericRange.Null, new DataValue(Variant.From(123)))).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
            Assert.That((await variable.WriteAttributeAsync(fixture.Manager.SystemContext,
                Attributes.Value, NumericRange.Null, new DataValue(Variant.Null))).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
        }

        /// <summary>
        /// Required string fields reject typed and untyped null without changing retained or live values.
        /// </summary>
        [TestCase(Opc.Ua.Di.BrowseNames.AssetId, false)]
        [TestCase(Opc.Ua.Di.BrowseNames.AssetId, true)]
        [TestCase(Opc.Ua.Machinery.BrowseNames.Location, false)]
        [TestCase(Opc.Ua.Machinery.BrowseNames.Location, true)]
        public async Task RequiredIdentityRejectsNullWithoutDeletingRetainedValueAsync(string field, bool typed)
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "RequiredIdentity");
            Mock<IAssetConfigurationStore> store = Store();
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), field, It.IsAny<CancellationToken>()))
                .ReturnsAsync("retained-value");
            await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, Permissions);
            var variable = (BaseVariableState)pump.Identification.Child(field)!;
            Variant written = typed ? Variant.From((string)null!) : Variant.Null;

            ServiceResult result = await variable.WriteAttributeAsync(fixture.Manager.SystemContext,
                Attributes.Value, NumericRange.Null, new DataValue(written));

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
            Assert.That(variable.WrappedValue, Is.EqualTo(Variant.From("retained-value")));
            store.Verify(value => value.SetValueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.That(await store.Object.GetValueAsync("urn:example:pump:stable", field, CancellationToken.None),
                Is.EqualTo("retained-value"));
        }

        /// <summary>
        /// Waits for each asynchronous state notification without blocking the binding caller.
        /// </summary>
        [Test]
        public async Task BindingAwaitsGatedStateNotificationsInFieldOrderAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "GatedNotifications");
            string[] fieldNames =
            [
                Opc.Ua.Di.BrowseNames.AssetId,
                Opc.Ua.Di.BrowseNames.ComponentName,
                Opc.Ua.Machinery.BrowseNames.Location
            ];
            var notifications = new System.Collections.Generic.List<string?>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocationReturned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAssetConfigurationStore> store = Store();
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<string?>((string?)null));
            foreach (string fieldName in fieldNames)
            {
                var variable = (BaseVariableState)pump.Identification.Child(fieldName)!;
                variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
                variable.OnStateChangedAsync = async (_, node, _, _) =>
                {
                    notifications.Add(((BaseInstanceState)node).BrowseName.Name);
                    if (notifications.Count == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.ConfigureAwait(false);
                    }
                };
            }

            Task<IPumpBuilder> binding = Task.Factory.StartNew(
                () =>
                {
                    ValueTask<IPumpBuilder> pending = fixture.Manager.Runtime.WithRetainedIdentificationAsync(
                        pump, store.Object, Permissions);
                    invocationReturned.TrySetResult(true);
                    return pending.AsTask();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
            try
            {
                Task readiness = Task.WhenAll(entered.Task, invocationReturned.Task);
                Task completed = await Task.WhenAny(readiness, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(completed, Is.SameAs(readiness),
                    "the notification must enter and the binding invocation must return while it is gated");
                Assert.That(release.Task.IsCompleted, Is.False, "the state sink must still be gated");
                Assert.That(binding.IsCompleted, Is.False, "the binding must remain pending while the sink is gated");
                for (int i = 0; i < fieldNames.Length; i++)
                {
                    var variable = (BaseVariableState)pump.Identification.Child(fieldNames[i])!;
                    Assert.That(variable.OnSimpleWriteValueAsync, Is.Not.Null);
                    Assert.That(variable.AccessLevel & AccessLevels.CurrentWrite,
                        Is.EqualTo(i == 0 ? AccessLevels.CurrentWrite : 0));
                    Assert.That(variable.UserAccessLevel & AccessLevels.CurrentWrite,
                        Is.EqualTo(i == 0 ? AccessLevels.CurrentWrite : 0));
                }
            }
            finally
            {
                release.TrySetResult(true);
                Task drained = await Task.WhenAny(binding, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(drained, Is.SameAs(binding), "the binding must finish after the sink is released");
            }

            Assert.That(await binding, Is.SameAs(pump));
            Assert.That(notifications, Is.EqualTo(fieldNames));
        }

        /// <summary>
        /// Characterizes retained callbacks and partial publication after a notification failure.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task NotificationFailurePreservesPublishedPrefixAndRejectsRebindingAsync(int failingField)
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "NotificationFailure");
            string[] fieldNames =
            [
                Opc.Ua.Di.BrowseNames.AssetId,
                Opc.Ua.Di.BrowseNames.ComponentName,
                Opc.Ua.Machinery.BrowseNames.Location
            ];
            Mock<IAssetConfigurationStore> store = Store();
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<string?>((string?)null));
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), Opc.Ua.Di.BrowseNames.AssetId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<string?>("restored-asset"));
            var failure = new IOException("notification failed");
            var notifications = new System.Collections.Generic.List<string?>();
            foreach (string fieldName in fieldNames)
            {
                var variable = (BaseVariableState)pump.Identification.Child(fieldName)!;
                variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
                variable.OnStateChangedAsync = (_, node, _, _) =>
                {
                    notifications.Add(((BaseInstanceState)node).BrowseName.Name);
                    if (notifications.Count == failingField + 1)
                    {
                        throw failure;
                    }
                    return default;
                };
            }

            IOException? observed = await Assert.ThrowsAsync<IOException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, Permissions));
            Assert.That(observed, Is.SameAs(failure));
            Assert.That(notifications, Is.EqualTo(new ArraySegment<string>(fieldNames, 0, failingField + 1)));
            Assert.That(Value(pump, Opc.Ua.Di.BrowseNames.AssetId), Is.EqualTo(Variant.From("restored-asset")));
            for (int i = 0; i < fieldNames.Length; i++)
            {
                var variable = (BaseVariableState)pump.Identification.Child(fieldNames[i])!;
                Assert.That(variable.OnSimpleWriteValueAsync, Is.Not.Null);
                Assert.That(variable.AccessLevel & AccessLevels.CurrentWrite,
                    Is.EqualTo(i <= failingField ? AccessLevels.CurrentWrite : 0));
                Assert.That(variable.UserAccessLevel & AccessLevels.CurrentWrite,
                    Is.EqualTo(i <= failingField ? AccessLevels.CurrentWrite : 0));
                if (i <= failingField)
                {
                    Assert.That(variable.RolePermissions, Is.EqualTo(Permissions));
                }
            }
            InvalidOperationException? retry = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, Permissions));
            Assert.That(retry!.Message, Is.EqualTo("An identification field is already bound."));
        }

        /// <summary>
        /// A failed restoration can be retried, without exposing a partly writable identification.
        /// </summary>
        [Test]
        public async Task RestoreFailureRollsBackBindingsAndAllowsRetryAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "Retry");
            Mock<IAssetConfigurationStore> store = Store();
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), Opc.Ua.Di.BrowseNames.AssetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync("persisted");
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), Opc.Ua.Di.BrowseNames.ComponentName, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("read failed"));
            await Assert.ThrowsAsync<IOException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, Permissions));
            var assetId = (BaseVariableState)pump.Identification.Child(Opc.Ua.Di.BrowseNames.AssetId)!;
            Assert.That(assetId.WrappedValue, Is.EqualTo(Variant.From("default-asset")));
            Assert.That(assetId.OnSimpleWriteValueAsync, Is.Null);
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), Opc.Ua.Di.BrowseNames.ComponentName, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
            await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, Permissions);
            Assert.That(assetId.WrappedValue, Is.EqualTo(Variant.From("persisted")));
            Assert.That(assetId.OnSimpleWriteValueAsync, Is.Not.Null);
        }

        /// <summary>
        /// Duplicate stable identities and non-persistent stores cannot select retained identity support.
        /// </summary>
        [Test]
        public async Task DuplicateIdentityAndVolatileStoreAreRejectedAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder first = await CreateAsync(fixture, "First");
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(
                    first, new MemoryAssetConfigurationStore(), Permissions));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(first, Store().Object, []));
            await fixture.Manager.Runtime.WithRetainedIdentificationAsync(first, Store().Object, Permissions);
            IPumpBuilder second = await CreateAsync(fixture, "Duplicate");
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(second, Store().Object, Permissions));
        }

        /// <summary>
        /// Cancellation is observed between fields even if a provider returns a value after cancellation.
        /// </summary>
        [Test]
        public async Task CancellationDuringRestoreRemovesPartialBindingsAndValuesAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "Cancelled");
            Mock<IAssetConfigurationStore> store = Store();
            using var cancellation = new CancellationTokenSource();
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), Opc.Ua.Di.BrowseNames.AssetId, It.IsAny<CancellationToken>()))
                .Callback(() => cancellation.Cancel())
                .ReturnsAsync("partly-restored");
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(
                    pump, store.Object, Permissions, cancellation.Token));
            var assetId = (BaseVariableState)pump.Identification.Child(Opc.Ua.Di.BrowseNames.AssetId)!;
            Assert.That(assetId.WrappedValue, Is.EqualTo(Variant.From("default-asset")));
            Assert.That(assetId.OnSimpleWriteValueAsync, Is.Null);
        }

        /// <summary>
        /// Runtime shutdown during a store read rolls back restored values without enabling identity writes.
        /// </summary>
        [Test]
        public async Task RuntimeShutdownDuringRestorePreventsWritablePublicationAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "StoppedDuringRestore");
            Mock<IAssetConfigurationStore> store = Store();
            Func<ValueTask<string?>> restore = async () =>
            {
                await fixture.Manager.Runtime.DisposeAsync();
                return "restored-after-shutdown";
            };
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), Opc.Ua.Di.BrowseNames.AssetId, It.IsAny<CancellationToken>()))
                .Returns(restore);

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, Permissions));

            foreach (string field in new[]
            {
                Opc.Ua.Di.BrowseNames.AssetId,
                Opc.Ua.Di.BrowseNames.ComponentName,
                Opc.Ua.Machinery.BrowseNames.Location
            })
            {
                var variable = (BaseVariableState)pump.Identification.Child(field)!;
                Assert.That(variable.OnSimpleWriteValueAsync, Is.Null);
                Assert.That(variable.AccessLevel & AccessLevels.CurrentWrite, Is.Zero);
            }
            Assert.That(Value(pump, Opc.Ua.Di.BrowseNames.AssetId), Is.EqualTo(Variant.From("default-asset")));
            Assert.That(Value(pump, Opc.Ua.Di.BrowseNames.ComponentName),
                Is.EqualTo(Variant.From(new LocalizedText("en", "default-name"))));
            Assert.That(Value(pump, Opc.Ua.Machinery.BrowseNames.Location),
                Is.EqualTo(Variant.From("default-location")));
        }

        /// <summary>
        /// Corrupted retained text is an error, not a silently substituted nameplate default.
        /// </summary>
        [TestCase("invalid")]
        [TestCase("999:1:a")]
        [TestCase("-2:1:x")]
        [TestCase("1:99:abc")]
        public async Task CorruptLocalizedTextRejectsActivationWithoutPartialBindingsAsync(string stored)
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "Corrupt");
            Mock<IAssetConfigurationStore> store = Store();
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), Opc.Ua.Di.BrowseNames.ComponentName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(stored);
            await Assert.ThrowsAsync<FormatException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, Permissions));
            Assert.That(Value(pump, Opc.Ua.Di.BrowseNames.ComponentName),
                Is.EqualTo(Variant.From(new LocalizedText("en", "default-name"))));
            Assert.That(((BaseVariableState)pump.Identification.Child(Opc.Ua.Di.BrowseNames.AssetId)!)
                .OnSimpleWriteValueAsync, Is.Null);
        }

        /// <summary>
        /// Invalid stable identities and role entries do not install handlers or contact storage.
        /// </summary>
        [TestCase("relative-uri")]
        [TestCase("null-role")]
        [TestCase("missing-role")]
        public async Task InvalidIdentityConfigurationLeavesFieldsUnboundAsync(string invalid)
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "InvalidIdentity");
            Mock<IAssetConfigurationStore> store = Store();
            ArrayOf<RolePermissionType> permissions = invalid switch
            {
                "null-role" => [null!],
                "missing-role" => [new RolePermissionType { RoleId = NodeId.Null }],
                _ => Permissions
            };
            if (invalid == "relative-uri")
            {
                pump.Identification.Set(Opc.Ua.Di.BrowseNames.ProductInstanceUri, Variant.From("relative"));
            }
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, permissions));
            var assetId = (BaseVariableState)pump.Identification.Child(Opc.Ua.Di.BrowseNames.AssetId)!;
            Assert.That(assetId.OnSimpleWriteValueAsync, Is.Null);
            Assert.That(assetId.AccessLevel & AccessLevels.CurrentWrite, Is.Zero);
            store.Verify(value => value.GetValueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Existing application write handlers are preserved rather than replaced by persistence.
        /// </summary>
        [Test]
        public async Task ExistingWriteHandlerPreventsRetainedBindingAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await CreateAsync(fixture, "ExistingBinding");
            var assetId = (BaseVariableState)pump.Identification.Child(Opc.Ua.Di.BrowseNames.AssetId)!;
            assetId.OnSimpleWriteValueAsync = static (_, _, _, _) =>
                new ValueTask<AttributeWriteResult>(new AttributeWriteResult(StatusCodes.BadNotWritable));
            var original = assetId.OnSimpleWriteValueAsync;
            Mock<IAssetConfigurationStore> store = Store();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Manager.Runtime.WithRetainedIdentificationAsync(pump, store.Object, Permissions));
            Assert.That(assetId.OnSimpleWriteValueAsync, Is.SameAs(original));
            store.Verify(value => value.GetValueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Creates a pump with stable persistence identity and explicit startup defaults.
        /// </summary>
        private static async ValueTask<IPumpBuilder> CreateAsync(PumpsServerFixture fixture, string name)
        {
            IPumpBuilder pump = await fixture.Manager.CreatePumpAsync(fixture.PumpName(name));
            pump.WithNameplate(new PumpNameplate
            {
                NodeId = NodeId.Null,
                ProductInstanceUri = "urn:example:pump:stable",
                Manufacturer = new LocalizedText("manufacturer"),
                AssetId = "default-asset",
                ComponentName = new LocalizedText("en", "default-name"),
                Location = "default-location"
            });
            return pump;
        }

        /// <summary>
        /// Reads an identity field without losing its declared Variant type.
        /// </summary>
        private static Variant Value(IPumpBuilder pump, string name)
        {
            return ((BaseVariableState)pump.Identification.Child(name)!).WrappedValue;
        }

        /// <summary>
        /// Commits an identity update through the normal attribute-write binding.
        /// </summary>
        private static async Task WriteAsync(
            PumpsServerFixture fixture, IPumpBuilder pump, string field, Variant value)
        {
            var variable = (BaseVariableState)pump.Identification.Child(field)!;
            ServiceResult result = await variable.WriteAttributeAsync(
                fixture.Manager.SystemContext, Attributes.Value, NumericRange.Null, new DataValue(value));
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        /// <summary>
        /// Supplies a strict persistent store with no previously retained values.
        /// </summary>
        private static Mock<IAssetConfigurationStore> Store()
        {
            var store = new Mock<IAssetConfigurationStore>(MockBehavior.Strict);
            store.SetupGet(value => value.IsPersistent).Returns(true);
            store.Setup(value => value.GetValueAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
            return store;
        }

        /// <summary>
        /// Grants identity editing only to the standard configuration-administrator role.
        /// </summary>
        private static ArrayOf<RolePermissionType> Permissions =>
        [
            new RolePermissionType
            {
                RoleId = Opc.Ua.ObjectIds.WellKnownRole_ConfigureAdmin,
                Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Write)
            }
        ];
    }
}
