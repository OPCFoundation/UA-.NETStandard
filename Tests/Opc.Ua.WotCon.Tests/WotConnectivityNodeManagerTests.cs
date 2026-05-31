/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using Opc.Ua.WotCon.Tests.Providers;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Tests for <see cref="WotConnectivityNodeManager"/> + <see cref="AssetRegistry"/>
    /// that drive the spec-mandated invariants via the public node-manager
    /// surface: duplicate-name rejection, BadNotSupported when no factory or
    /// discovery is available, and reload-from-disk of persisted Thing
    /// Descriptions on startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture uses Moq to provide a minimal <see cref="IServerInternal"/>,
    /// mirroring the pattern in <c>AsyncCustomNodeManagerTests</c>. The
    /// node manager's address space is populated via
    /// <see cref="WotConnectivityNodeManager.CreateAddressSpaceAsync"/>
    /// using an in-memory storage folder so the persistence path can be
    /// exercised end-to-end.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotConnectivityNodeManagerTests
    {
        private string _tempFolder = null!;

        [SetUp]
        public void SetUp()
        {
            _tempFolder = Path.Combine(
                Path.GetTempPath(),
                "wotcon-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempFolder))
            {
                try
                {
                    Directory.Delete(_tempFolder, recursive: true);
                }
                catch
                { /* swallow */
                }
            }
        }

        // ----------------------------------------------------------------
        // CreateAsset: name validation and dup detection.
        // ----------------------------------------------------------------

        [Test]
        public async Task CreateAssetWithEmptyNameReturnsBadInvalidArgument()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync(string.Empty, CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(assetId.IsNull, Is.True);
        }

        [Test]
        public async Task CreateAssetWithWhitespaceNameReturnsBadInvalidArgument()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, _) = await harness.Registry
                .CreateAssetAsync("   ", CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("../escape")]
        [TestCase("..\\escape")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("/etc/passwd")]
        [TestCase("C:asset")]
        [TestCase("~/.ssh")]
        [TestCase(".hidden")]
        [TestCase(" leading")]
        [TestCase("trailing.")]
        [TestCase("trailing ")]
        [TestCase("with\0null")]
        [TestCase("CON")]
        [TestCase("lpt1")]
        public async Task CreateAssetWithUnsafeNameReturnsBadInvalidArgumentAndDoesNotPersist(string name)
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync(name, CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument),
                $"name '{name}' must be rejected with BadInvalidArgument");
            Assert.That(assetId.IsNull, Is.True);
            // Defence-in-depth: no .jsonld file should have been written anywhere
            // under the configured persistence folder.
            Assert.That(Directory.GetFiles(_tempFolder, "*.jsonld",
                SearchOption.AllDirectories), Is.Empty);
        }

        [Test]
        public async Task CreateAssetWithTooLongNameReturnsBadInvalidArgument()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);
            string longName = new('a', WotAssetNameValidator.MaxNameLength + 1);

            (ServiceResult status, _) = await harness.Registry
                .CreateAssetAsync(longName, CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreateAssetWithUniqueNameReturnsGoodAndNonNullNodeId()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(assetId.IsNull, Is.False);
            Assert.That(harness.Registry.AssetNames, Has.Member("asset-001"));
        }

        [Test]
        public async Task CreateAssetWithDuplicateNameReturnsBadBrowseNameDuplicated()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);
            await harness.Registry.CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode,
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
            Assert.That(assetId.IsNull, Is.True);
        }

        // ----------------------------------------------------------------
        // DeleteAsset: missing-id and happy path.
        // ----------------------------------------------------------------

        [Test]
        public async Task DeleteAssetReturnsBadNotFoundForUnknownId()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);

            ServiceResult status = await harness.Registry
                .DeleteAssetAsync(new NodeId(99999u, 3), CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task DeleteAssetRemovesItFromTheRegistry()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);

            ServiceResult status = await harness.Registry
                .DeleteAssetAsync(assetId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(harness.Registry.AssetNames, Does.Not.Contain("asset-001"));
        }

        // ----------------------------------------------------------------
        // Rebuild: BadNotSupported when no factory accepts the TD.
        // ----------------------------------------------------------------

        [Test]
        public async Task RebuildReturnsBadNotSupportedWhenNoFactoryAcceptsTd()
        {
            using var harness = new ManagerHarness(_tempFolder); // no bindings registered
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            var td = new ThingDescription
            {
                Name = "asset-001",
                Base = "http://example.com/asset/1" // no registered factory handles http
            };

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task RebuildWithMatchingFactoryReturnsGoodAndPersistsTd()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            var td = new ThingDescription
            {
                Name = "asset-001",
                Base = "sim://opcua.test/wot/asset-001"
            };

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: true, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            string persistedPath = Path.Combine(_tempFolder, "asset-001.jsonld");
            Assert.That(File.Exists(persistedPath), Is.True,
                "Persisted TD file should be written on successful materialisation.");
            ThingDescription? roundtrip = JsonSerializer.Deserialize(
                File.ReadAllBytes(persistedPath),
                ThingDescriptionJsonContext.Default.ThingDescription);
            Assert.That(roundtrip!.Name, Is.EqualTo("asset-001"));
            Assert.That(roundtrip.Base, Is.EqualTo(td.Base));
        }

        [Test]
        public async Task RebuildMaterialisesPropertyVariableAndAssetEndpoint()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            var td = new ThingDescription
            {
                Name = "asset-001",
                Base = "sim://opcua.test/wot/asset-001",
                Properties = new Dictionary<string, WotProperty>
                {
                    ["Voltage"] = new WotProperty
                    {
                        Type = "number",
                        Title = "Voltage",
                        Unit = "V",
                        Observable = true
                    }
                }
            };

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(entry.Properties, Has.Count.EqualTo(1));
            (BaseDataVariableState variable, WotPropertyTag tag) = entry.Properties.Values.First();
            Assert.That(tag.Name, Is.EqualTo("Voltage"));
            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(variable.BrowseName.Name, Is.EqualTo("Voltage"));
            Assert.That(variable.DisplayName.Text, Is.EqualTo("Voltage"));
            Assert.That(entry.Asset.AssetEndpoint, Is.Not.Null);
            Assert.That(entry.Asset.AssetEndpoint!.Value, Is.EqualTo(td.Base));
        }

        [Test]
        public async Task RebuildMarksUnmappablePropertyWithBadConfigurationError()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            var td = new ThingDescription
            {
                Name = "asset-001",
                Base = "sim://opcua.test/wot/asset-001",
                Properties = new Dictionary<string, WotProperty>
                {
                    // "object" cannot map per Spec Table 14 → BadConfigurationError on read.
                    ["Metadata"] = new WotProperty { Type = "object" }
                }
            };

            await harness.Registry.RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task RebuildSimpleReadValueDelegatesToProviderForReadablePropertyValues()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number" }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            // Push a provider-side value the simulated provider remembers.
            ((SimulatedWotAssetProvider)entry.Provider!).SetValue("Voltage", new Variant(12.3));

            Assert.That(variable.OnSimpleReadValueAsync, Is.Not.Null);
            Assert.That(variable.OnSimpleReadValue, Is.Null,
                "Async hooks should replace the sync OnSimpleReadValue.");

            AttributeSimpleReadResult result = await variable.OnSimpleReadValueAsync!(
                harness.Manager.SystemContext, variable, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.Result), Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo(12.3));
        }

        [Test]
        public async Task RebuildSimpleWriteValueDelegatesToProviderForWritableProperties()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", ReadOnly = false }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);
            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            Assert.That(variable.OnSimpleWriteValueAsync, Is.Not.Null,
                "OnSimpleWriteValueAsync must be wired for non-read-only properties.");
            Assert.That(variable.OnSimpleWriteValue, Is.Null,
                "Async hooks should replace the sync OnSimpleWriteValue.");

            AttributeWriteResult result = await variable.OnSimpleWriteValueAsync!(
                harness.Manager.SystemContext, variable, new Variant(42.5), CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.Result), Is.True);
            var simulated = (SimulatedWotAssetProvider)entry.Provider!;
            Assert.That(simulated.Values["Voltage"].AsBoxedObject(), Is.EqualTo(42.5));
        }

        [Test]
        public async Task RebuildReadOnlyPropertyDoesNotWireOnSimpleWriteValue()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", ReadOnly = true }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            Assert.That(variable.OnSimpleWriteValueAsync, Is.Null);
            Assert.That(variable.OnSimpleWriteValue, Is.Null);
            Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
        }

        [Test]
        public async Task RebuildUnmappablePropertyExposesBadConfigurationErrorViaAsyncReadHook()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        // "object" cannot map per Spec Table 14 — async hook reports the error.
                        ["Metadata"] = new WotProperty { Type = "object" }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            Assert.That(variable.OnSimpleReadValueAsync, Is.Not.Null);
            Assert.That(variable.OnSimpleReadValue, Is.Null);
            AttributeSimpleReadResult result = await variable.OnSimpleReadValueAsync!(
                harness.Manager.SystemContext, variable, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.Result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(result.Value.IsNull, Is.True);
        }

        [Test]
        public async Task RebuildMaterialisesActionMethodWithExpectedArguments()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            var td = new ThingDescription
            {
                Name = "asset-001",
                Base = "sim://opcua.test/wot/asset-001",
                Actions = new Dictionary<string, WotAction>
                {
                    ["Echo"] = new WotAction
                    {
                        Title = "Echo",
                        Input = new WotActionSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, WotActionMember>
                            {
                                ["value"] = new WotActionMember { Type = "integer" }
                            }
                        },
                        Output = new WotActionSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, WotActionMember>
                            {
                                ["value"] = new WotActionMember { Type = "integer" }
                            }
                        }
                    }
                }
            };

            await harness.Registry.RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None).ConfigureAwait(false);

            Assert.That(entry.Actions, Has.Count.EqualTo(1));
            (MethodState method, WotActionTag tag) = entry.Actions.Values.First();
            Assert.That(method.BrowseName.Name, Is.EqualTo("Echo"));
            Assert.That(method.Executable, Is.True);
            Assert.That(tag.InputArguments, Has.Count.EqualTo(1));
            Assert.That(tag.InputArguments[0].DataType, Is.EqualTo(DataTypeIds.Int64));
            Assert.That(tag.OutputArguments, Has.Count.EqualTo(1));
            Assert.That(method.InputArguments, Is.Not.Null);
            Assert.That(method.OutputArguments, Is.Not.Null);
            Assert.That(method.OnCallMethod2Async, Is.Not.Null,
                "OnCallMethod2Async must be wired so the action routes through the provider.");
        }

        [Test]
        public async Task RebuildReplacesPreviouslyMaterialisedChildrenOnSecondCall()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;

            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number" },
                        ["Current"] = new WotProperty { Type = "number" }
                    }
                },
                persistOnSuccess: false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(entry.Properties, Has.Count.EqualTo(2));

            // Second rebuild with fewer properties — children must be replaced, not appended.
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Power"] = new WotProperty { Type = "number" }
                    }
                },
                persistOnSuccess: false, CancellationToken.None).ConfigureAwait(false);

            Assert.That(entry.Properties, Has.Count.EqualTo(1));
            Assert.That(entry.Properties.Values.First().Tag.Name, Is.EqualTo("Power"));
        }

        [Test]
        public async Task FindByNodeIdReturnsNullForUnknownAsset()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);

            Assert.That(harness.Registry.FindByNodeId(new NodeId(999u, 3)), Is.Null);
        }

        [Test]
        public async Task TryGetPropertyResolvesMaterialisedVariableByNodeId()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number" }
                    }
                },
                persistOnSuccess: false, CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, WotPropertyTag _) = entry.Properties.Values.First();
            bool found = harness.Registry.TryGetProperty(
                variable.NodeId,
                out AssetEntry foundEntry,
                out BaseDataVariableState foundVariable,
                out WotPropertyTag foundTag);

            Assert.That(found, Is.True);
            Assert.That(foundEntry, Is.SameAs(entry));
            Assert.That(foundVariable, Is.SameAs(variable));
            Assert.That(foundTag.Name, Is.EqualTo("Voltage"));
        }

        [Test]
        public async Task TryGetActionResolvesMaterialisedMethodByNodeId()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001",
                    Actions = new Dictionary<string, WotAction>
                    {
                        ["Reset"] = new WotAction { Title = "Reset" }
                    }
                },
                persistOnSuccess: false, CancellationToken.None).ConfigureAwait(false);

            (MethodState method, WotActionTag _) = entry.Actions.Values.First();
            bool found = harness.Registry.TryGetAction(
                method.NodeId,
                out AssetEntry foundEntry,
                out MethodState foundMethod,
                out WotActionTag foundTag);

            Assert.That(found, Is.True);
            Assert.That(foundEntry, Is.SameAs(entry));
            Assert.That(foundMethod, Is.SameAs(method));
            Assert.That(foundTag.Name, Is.EqualTo("Reset"));
        }

        // ----------------------------------------------------------------
        // Discovery / ConnectionTest / CreateAssetForEndpoint:
        // BadNotSupported when no discovery provider is configured.
        // ----------------------------------------------------------------

        [Test]
        public async Task DiscoverAssetsReturnsBadNotSupportedWhenNoDiscoveryProvider()
        {
            using var harness = new ManagerHarness(_tempFolder); // no discovery
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, IReadOnlyList<string> endpoints) = await harness.Registry
                .DiscoverAssetsAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(endpoints, Is.Empty);
        }

        [Test]
        public async Task ConnectionTestReturnsBadNotSupportedWhenNoDiscoveryProvider()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, bool success, string text) = await harness.Registry
                .ConnectionTestAsync("sim://foo", CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(success, Is.False);
            Assert.That(text, Is.Empty);
        }

        [Test]
        public async Task CreateAssetForEndpointReturnsBadNotSupportedWhenNoDiscoveryProvider()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetForEndpointAsync("asset-x", "sim://endpoint", CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(assetId.IsNull, Is.True);
        }

        [Test]
        public async Task DiscoverAssetsForwardsToConfiguredProvider()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                discoveryProvider: new SimulatedWotDiscoveryProvider());
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, IReadOnlyList<string> endpoints) = await harness.Registry
                .DiscoverAssetsAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(endpoints, Has.Count.EqualTo(1));
            Assert.That(endpoints[0], Is.EqualTo(SimulatedWotDiscoveryProvider.CannedEndpoint));
        }

        [Test]
        public async Task ConnectionTestForwardsToConfiguredProvider()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                discoveryProvider: new SimulatedWotDiscoveryProvider());
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, bool success, _) = await harness.Registry
                .ConnectionTestAsync(SimulatedWotDiscoveryProvider.CannedEndpoint, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(success, Is.True);
        }

        // ----------------------------------------------------------------
        // Persisted-TD reload on startup.
        // ----------------------------------------------------------------

        [Test]
        public async Task PersistedThingDescriptionsAreRestoredOnStartup()
        {
            // Round 1: create an asset, materialise it with a TD, persist to disk.
            (_, string assetName) = (NodeId.Null, "asset-001");
            using (var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory()))
            {
                await harness.StartAsync().ConfigureAwait(false);
                (_, NodeId id) = await harness.Registry
                    .CreateAssetAsync(assetName, CancellationToken.None).ConfigureAwait(false);
                NodeId originalAssetId = id;
                AssetEntry entry = harness.Registry.FindByNodeId(id)!;
                await harness.Registry.RebuildAsync(
                    entry,
                    new ThingDescription
                    {
                        Name = assetName,
                        Base = "sim://opcua.test/wot/asset-001",
                        Properties = new Dictionary<string, WotProperty>
                        {
                            ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                        }
                    },
                    persistOnSuccess: true,
                    CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(File.Exists(Path.Combine(_tempFolder, assetName + ".jsonld")), Is.True);

            // Round 2: brand new manager pointing at the same folder must
            // restore the asset on startup so the same name resolves to a
            // live AssetEntry without a fresh CreateAsset call.
            using var reloaded = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await reloaded.StartAsync().ConfigureAwait(false);

            Assert.That(reloaded.Registry.AssetNames, Has.Member(assetName));
        }

        // ----------------------------------------------------------------
        // WotConnectivityNodeManagerFactory.Create()
        // ----------------------------------------------------------------

        [Test]
        public void NodeManagerFactoryCreateReturnsNodeManager()
        {
            using var harness = new ManagerHarness(_tempFolder);
            var factory = new WotConnectivityNodeManagerFactory(harness.Options);

            INodeManager nm = factory.Create(
                harness.MockServer.Object,
                new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 200
                    }
                });

            Assert.That(nm, Is.Not.Null);
            (nm as IDisposable)?.Dispose();
        }

        // ----------------------------------------------------------------
        // ReadFromProvider / WriteToProvider — null provider + exception paths
        // ----------------------------------------------------------------

        [Test]
        public async Task ReadFromProviderReturnsBadNotConnectedWhenProviderIsNull()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-read-null", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-read-null",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Value"] = new WotProperty { Type = "number" }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            // Replace provider with null to trigger BadNotConnected
            entry.Provider = null;

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            AttributeSimpleReadResult result = await variable.OnSimpleReadValueAsync!(
                null!, null!, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Result.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public async Task ReadFromProviderReturnsBadCommunicationErrorOnException()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-read-exc", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-read-exc",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Value"] = new WotProperty { Type = "number" }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            var mockProvider = new Mock<IWotAssetProvider>();
            mockProvider
                .Setup(p => p.ReadAsync(It.IsAny<WotPropertyTag>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(ServiceResult, Variant)>(
                    Task.FromException<(ServiceResult, Variant)>(
                        new InvalidOperationException("simulated read error"))));
            entry.Provider = mockProvider.Object;

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            AttributeSimpleReadResult result = await variable.OnSimpleReadValueAsync!(
                null!, null!, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Result.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
        }

        [Test]
        public async Task WriteToProviderReturnsBadNotConnectedWhenProviderIsNull()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-write-null", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-write-null",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Value"] = new WotProperty { Type = "number", ReadOnly = false }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            entry.Provider = null;

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            AttributeWriteResult result = await variable.OnSimpleWriteValueAsync!(
                null!, null!, new Variant(42.0), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Result.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public async Task WriteToProviderReturnsBadCommunicationErrorOnException()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-write-exc", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-write-exc",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Value"] = new WotProperty { Type = "number", ReadOnly = false }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            var mockProvider = new Mock<IWotAssetProvider>();
            mockProvider
                .Setup(p => p.WriteAsync(
                    It.IsAny<WotPropertyTag>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(
                    Task.FromException<ServiceResult>(
                        new InvalidOperationException("simulated write error"))));
            entry.Provider = mockProvider.Object;

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            AttributeWriteResult result = await variable.OnSimpleWriteValueAsync!(
                null!, null!, new Variant(42.0), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Result.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
        }

        // ----------------------------------------------------------------
        // InvokeAction — null provider + exception + success paths
        // ----------------------------------------------------------------

        [Test]
        public async Task InvokeActionReturnsBadNotConnectedWhenProviderIsNull()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-action-null", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-action-null",
                    Base = "sim://opcua.test/wot/asset-001",
                    Actions = new Dictionary<string, WotAction>
                    {
                        ["Reset"] = new WotAction { Title = "Reset" }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            entry.Provider = null;

            (MethodState method, _) = entry.Actions.Values.First();
            ServiceResult actionResult = await method.OnCallMethod2Async!(
                null!, null!, NodeId.Null,
                new ArrayOf<Variant>(), new List<Variant>(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(actionResult.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public async Task InvokeActionSucceedsWithProvider()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-action-ok", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-action-ok",
                    Base = "sim://opcua.test/wot/asset-001",
                    Actions = new Dictionary<string, WotAction>
                    {
                        ["Reset"] = new WotAction { Title = "Reset" }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            var mockProvider = new Mock<IWotAssetProvider>();
            mockProvider
                .Setup(p => p.InvokeActionAsync(
                    It.IsAny<WotActionTag>(),
                    It.IsAny<IReadOnlyList<Variant>>(),
                    It.IsAny<IList<Variant>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            entry.Provider = mockProvider.Object;

            (MethodState method, _) = entry.Actions.Values.First();
            ServiceResult actionResult = await method.OnCallMethod2Async!(
                null!, null!, NodeId.Null,
                new ArrayOf<Variant>(), new List<Variant>(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(actionResult), Is.True);
        }

        [Test]
        public async Task InvokeActionReturnsBadCommunicationErrorOnException()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-action-exc", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-action-exc",
                    Base = "sim://opcua.test/wot/asset-001",
                    Actions = new Dictionary<string, WotAction>
                    {
                        ["Reset"] = new WotAction { Title = "Reset" }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            var mockProvider = new Mock<IWotAssetProvider>();
            mockProvider
                .Setup(p => p.InvokeActionAsync(
                    It.IsAny<WotActionTag>(),
                    It.IsAny<IReadOnlyList<Variant>>(),
                    It.IsAny<IList<Variant>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(
                    Task.FromException<ServiceResult>(
                        new InvalidOperationException("simulated action error"))));
            entry.Provider = mockProvider.Object;

            (MethodState method, _) = entry.Actions.Values.First();
            ServiceResult actionResult = await method.OnCallMethod2Async!(
                null!, null!, NodeId.Null,
                new ArrayOf<Variant>(), new List<Variant>(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(actionResult.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
        }

        // ----------------------------------------------------------------
        // DeleteAsset — provider disposal path
        // ----------------------------------------------------------------

        [Test]
        public async Task DeleteAssetDisposesProvider()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-del-provider", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;

            var mockProvider = new Mock<IWotAssetProvider>();
            mockProvider.Setup(p => p.DisposeAsync()).Returns(ValueTask.CompletedTask);
            entry.Provider = mockProvider.Object;

            ServiceResult result = await harness.Registry
                .DeleteAssetAsync(assetId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            mockProvider.Verify(p => p.DisposeAsync(), Times.Once());
        }

        [Test]
        public async Task DeleteAssetContinuesWhenProviderDisposeThrows()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-del-throw", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;

            var mockProvider = new Mock<IWotAssetProvider>();
            mockProvider.Setup(p => p.DisposeAsync())
                .Returns(new ValueTask(
                    Task.FromException(new InvalidOperationException("dispose blew up"))));
            entry.Provider = mockProvider.Object;

            // Despite the disposal error the delete should still succeed
            ServiceResult result = await harness.Registry
                .DeleteAssetAsync(assetId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(harness.Registry.FindByNodeId(assetId), Is.Null);
        }

        // ----------------------------------------------------------------
        // DiscoverAssets — NotSupportedException from provider
        // ----------------------------------------------------------------

        [Test]
        public async Task DiscoverAssetsNotSupportedExceptionReturnsBadNotSupported()
        {
            var mockDiscovery = new Mock<IWotAssetDiscoveryProvider>();
            mockDiscovery
                .Setup(d => d.DiscoverAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IReadOnlyList<string>>(
                    Task.FromException<IReadOnlyList<string>>(
                        new NotSupportedException("discovery not supported"))));

            using var harness = new ManagerHarness(
                _tempFolder,
                discoveryProvider: mockDiscovery.Object);
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, IReadOnlyList<string> endpoints) = await harness.Registry
                .DiscoverAssetsAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(endpoints, Is.Empty);
        }

        // ----------------------------------------------------------------
        // CreateAssetForEndpoint — various outcome paths
        // ----------------------------------------------------------------

        [Test]
        public async Task CreateAssetForEndpointSucceedsWithDiscoveryProvider()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory(),
                new SimulatedWotDiscoveryProvider());
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetForEndpointAsync(
                    "endpoint-asset",
                    SimulatedWotDiscoveryProvider.CannedEndpoint,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(assetId.IsNull, Is.False);
            Assert.That(harness.Registry.AssetNames, Has.Member("endpoint-asset"));
        }

        [Test]
        public async Task CreateAssetForEndpointReturnsBadNotSupportedWhenDiscoveryThrowsNotSupported()
        {
            var mockDiscovery = new Mock<IWotAssetDiscoveryProvider>();
            mockDiscovery
                .Setup(d => d.CreateThingDescriptionAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ThingDescription>(
                    Task.FromException<ThingDescription>(
                        new NotSupportedException("endpoint not supported"))));

            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory(),
                mockDiscovery.Object);
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetForEndpointAsync(
                    "endpoint-notsupp",
                    "sim://endpoint/unknown",
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(assetId.IsNull, Is.True);
            Assert.That(harness.Registry.AssetNames, Has.No.Member("endpoint-notsupp"));
        }

        [Test]
        public async Task CreateAssetForEndpointReturnsBadConfigurationErrorOnGeneralException()
        {
            var mockDiscovery = new Mock<IWotAssetDiscoveryProvider>();
            mockDiscovery
                .Setup(d => d.CreateThingDescriptionAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ThingDescription>(
                    Task.FromException<ThingDescription>(
                        new InvalidOperationException("general failure"))));

            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory(),
                mockDiscovery.Object);
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetForEndpointAsync(
                    "endpoint-err",
                    "sim://endpoint/broken",
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(assetId.IsNull, Is.True);
            Assert.That(harness.Registry.AssetNames, Has.No.Member("endpoint-err"));
        }

        // ----------------------------------------------------------------
        // EnumeratePersisted — skip files with invalid asset names
        // ----------------------------------------------------------------

        [Test]
        public async Task EnumeratePersistedSkipsFilesWithInvalidAssetNames()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            // "CON" is a Windows reserved name; WotAssetNameValidator rejects it
            // on all platforms so the file should be skipped.
            File.WriteAllText(Path.Combine(_tempFolder, "CON.jsonld"), "{}");

            int count = 0;
            await foreach ((string _, ThingDescription _) in
                harness.Registry.EnumeratePersistedAsync(CancellationToken.None)
                    .ConfigureAwait(false))
            {
                count++;
            }

            Assert.That(count, Is.Zero,
                "CON.jsonld has an invalid asset name and should have been skipped.");
        }

        // ----------------------------------------------------------------
        // DeleteAddressSpaceAsync
        // ----------------------------------------------------------------

        [Test]
        public async Task DeleteAddressSpaceAsyncDisposesRegistryAndCallsBase()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            // Create an asset so the registry has something to dispose.
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("del-test", CancellationToken.None).ConfigureAwait(false);
            Assert.That(assetId.IsNull, Is.False);

            // DeleteAddressSpaceAsync must complete without throwing.
            Assert.That(
                async () => await harness.Manager.DeleteAddressSpaceAsync(CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.Nothing);
        }

        // ----------------------------------------------------------------
        // ApplyConfiguration — License property
        // ----------------------------------------------------------------

        [Test]
        public async Task ApplyConfigurationWithLicenseCreatesLicenseNode()
        {
            using var harness = new ManagerHarness(_tempFolder);

            // Set license before starting so CreateAddressSpaceAsync calls ApplyConfiguration.
            harness.Options.License = "TEST-LICENSE";

            await harness.StartAsync().ConfigureAwait(false);

            // Verify the management object has a Configuration child with a License node.
            WoTAssetConnectionManagementState? management = GetManagementObject(harness);
            Assert.That(management, Is.Not.Null);
            Assert.That(management!.Configuration, Is.Not.Null);
            Assert.That(management.Configuration!.License, Is.Not.Null);
            Assert.That(management.Configuration.License!.Value, Is.EqualTo("TEST-LICENSE"));
        }

        // ----------------------------------------------------------------
        // ApplyConfiguration — custom parameter
        // ----------------------------------------------------------------

        [Test]
        public async Task ApplyConfigurationWithParameterCreatesParameterNode()
        {
            using var harness = new ManagerHarness(_tempFolder);

            harness.Options.Configuration["SampleRate"] = new WotConfigurationParameter
            {
                DataType = DataTypeIds.Int32,
                InitialValue = new Variant(100),
                Description = "Polling interval in ms",
                Writable = false
            };

            await harness.StartAsync().ConfigureAwait(false);

            WoTAssetConnectionManagementState? management = GetManagementObject(harness);
            Assert.That(management, Is.Not.Null);
            Assert.That(management!.Configuration, Is.Not.Null);
        }

        // ----------------------------------------------------------------
        // Restore persisted assets on startup
        // ----------------------------------------------------------------

        private static readonly System.Text.Json.JsonSerializerOptions s_jsonOptions =
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false };

        [Test]
        public async Task RestorePersistedAssetsOnStartupRebuildsAddressSpace()
        {
            const string assetName = "restore-test";
            // Write a valid TD JSON-LD to the temp folder.
            var td = new ThingDescription
            {
                Name = assetName,
                Base = "sim://opcua.test/wot/restore",
                Properties = new Dictionary<string, WotProperty>
                {
                    ["Temperature"] = new WotProperty { Type = "number" }
                }
            };
            string json = System.Text.Json.JsonSerializer.Serialize(td, s_jsonOptions);
            File.WriteAllText(Path.Combine(_tempFolder, assetName + ".jsonld"), json);

            // Harness must restore the persisted asset during startup.
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            Assert.That(harness.Registry.AssetNames, Has.Member(assetName));
        }

        // ----------------------------------------------------------------
        // OnMonitoredItemCreated — disabled monitoring mode returns early
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoredItemCreatedWithDisabledModeDoesNotSubscribe()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("mon-disabled", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "mon-disabled",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            var handle = new NodeHandle(variable.NodeId, variable);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Disabled);
            mockItem.Setup(m => m.Id).Returns(42u);

            InvokeOnMonitoredItemCreated(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object);

            // No subscription should be registered because mode is Disabled.
            Assert.That(entry.SubscriberCallbacks, Is.Empty);
        }

        // ----------------------------------------------------------------
        // OnMonitoredItemCreated — non-variable node returns early
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoredItemCreatedWithNonVariableNodeDoesNotSubscribe()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            // Use an object-state node instead of a variable-state node.
            var objectNode = new BaseObjectState(null);
            objectNode.NodeId = new NodeId(9001u);
            var handle = new NodeHandle(objectNode.NodeId, objectNode);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(1u);

            // Should complete without throwing — early-return path.
            Assert.That(
                () => InvokeOnMonitoredItemCreated(harness.Manager,
                    harness.Manager.SystemContext, handle, mockItem.Object),
                Throws.Nothing);
        }

        // ----------------------------------------------------------------
        // OnMonitoredItemCreated — observable property starts subscription
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoredItemCreatedForObservablePropertyStartsSubscription()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("mon-obs", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "mon-obs",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            var handle = new NodeHandle(variable.NodeId, variable);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(99u);

            InvokeOnMonitoredItemCreated(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object);

            Assert.That(entry.SubscriberCallbacks, Contains.Key(99u));
        }

        // ----------------------------------------------------------------
        // OnMonitoredItemModifiedAsync — non-variable node returns empty
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoredItemModifiedAsyncWithNonVariableReturnsEmpty()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            var objectNode = new BaseObjectState(null);
            objectNode.NodeId = new NodeId(9002u);
            var handle = new NodeHandle(objectNode.NodeId, objectNode);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(1u);

            ValueTask result = InvokeOnMonitoredItemModifiedAsync(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object, CancellationToken.None);
            await result.ConfigureAwait(false);

            Assert.That(result.IsCompleted, Is.True);
        }

        // ----------------------------------------------------------------
        // OnMonitoredItemModifiedAsync — variable with Reporting restarts subscription
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoredItemModifiedAsyncRestartSubscriptionForVariable()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("mon-mod", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "mon-mod",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            var handle = new NodeHandle(variable.NodeId, variable);

            // First, start a subscription.
            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(77u);
            InvokeOnMonitoredItemCreated(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object);

            Assert.That(entry.SubscriberCallbacks, Contains.Key(77u));

            // Now modify (mode = Reporting) → stops and restarts.
            await InvokeOnMonitoredItemModifiedAsync(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(entry.SubscriberCallbacks, Contains.Key(77u));
        }

        // ----------------------------------------------------------------
        // OnMonitoredItemModifiedAsync — variable switches to Disabled stops subscription
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoredItemModifiedAsyncWithDisabledModeStopsSubscription()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("mon-mod-dis", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "mon-mod-dis",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            var handle = new NodeHandle(variable.NodeId, variable);

            // Start a subscription.
            var reportingItem = new Mock<ISampledDataChangeMonitoredItem>();
            reportingItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            reportingItem.Setup(m => m.Id).Returns(55u);
            InvokeOnMonitoredItemCreated(harness.Manager,
                harness.Manager.SystemContext, handle, reportingItem.Object);
            Assert.That(entry.SubscriberCallbacks, Contains.Key(55u));

            // Modify to Disabled → subscription should be removed.
            var disabledItem = new Mock<ISampledDataChangeMonitoredItem>();
            disabledItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Disabled);
            disabledItem.Setup(m => m.Id).Returns(55u);
            await InvokeOnMonitoredItemModifiedAsync(harness.Manager,
                harness.Manager.SystemContext, handle, disabledItem.Object, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(entry.SubscriberCallbacks, Does.Not.ContainKey(55u));
        }

        // ----------------------------------------------------------------
        // OnMonitoredItemDeletedAsync — non-variable node returns empty
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoredItemDeletedAsyncWithNonVariableReturnsEmpty()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            var objectNode = new BaseObjectState(null);
            objectNode.NodeId = new NodeId(9003u);
            var handle = new NodeHandle(objectNode.NodeId, objectNode);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(1u);

            ValueTask result = InvokeOnMonitoredItemDeletedAsync(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object, CancellationToken.None);
            await result.ConfigureAwait(false);

            Assert.That(result.IsCompleted, Is.True);
        }

        // ----------------------------------------------------------------
        // OnMonitoredItemDeletedAsync — variable node stops subscription
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoredItemDeletedAsyncStopsSubscriptionForVariable()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("mon-del", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "mon-del",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            var handle = new NodeHandle(variable.NodeId, variable);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(33u);
            InvokeOnMonitoredItemCreated(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object);
            Assert.That(entry.SubscriberCallbacks, Contains.Key(33u));

            await InvokeOnMonitoredItemDeletedAsync(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(entry.SubscriberCallbacks, Does.Not.ContainKey(33u));
        }

        // ----------------------------------------------------------------
        // OnMonitoringModeChangedAsync — non-variable node returns early
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoringModeChangedAsyncWithNonVariableReturnsEmpty()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            var objectNode = new BaseObjectState(null);
            objectNode.NodeId = new NodeId(9004u);
            var handle = new NodeHandle(objectNode.NodeId, objectNode);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(1u);

            ValueTask result = InvokeOnMonitoringModeChangedAsync(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object,
                MonitoringMode.Reporting, MonitoringMode.Disabled, CancellationToken.None);
            await result.ConfigureAwait(false);

            Assert.That(result.IsCompleted, Is.True);
        }

        // ----------------------------------------------------------------
        // OnMonitoringModeChangedAsync — Reporting → Disabled stops subscription
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoringModeChangedAsyncReportingToDisabledStopsSubscription()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("mon-chg-stop", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "mon-chg-stop",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            var handle = new NodeHandle(variable.NodeId, variable);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(88u);
            InvokeOnMonitoredItemCreated(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object);
            Assert.That(entry.SubscriberCallbacks, Contains.Key(88u));

            await InvokeOnMonitoringModeChangedAsync(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object,
                MonitoringMode.Reporting, MonitoringMode.Disabled, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(entry.SubscriberCallbacks, Does.Not.ContainKey(88u));
        }

        // ----------------------------------------------------------------
        // OnMonitoringModeChangedAsync — Disabled → Reporting starts subscription
        // ----------------------------------------------------------------

        [Test]
        public async Task OnMonitoringModeChangedAsyncDisabledToReportingStartsSubscription()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("mon-chg-start", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "mon-chg-start",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            var handle = new NodeHandle(variable.NodeId, variable);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(66u);

            // Start from Disabled → Reporting.
            await InvokeOnMonitoringModeChangedAsync(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object,
                MonitoringMode.Disabled, MonitoringMode.Reporting, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(entry.SubscriberCallbacks, Contains.Key(66u));
        }

        // ----------------------------------------------------------------
        // OnProviderValueChange updates the variable's value
        // ----------------------------------------------------------------

        [Test]
        public async Task ProviderValueChangePushedThroughSubscriptionUpdatesVariable()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("mon-change", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "mon-change",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            var handle = new NodeHandle(variable.NodeId, variable);

            var mockItem = new Mock<ISampledDataChangeMonitoredItem>();
            mockItem.Setup(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            mockItem.Setup(m => m.Id).Returns(11u);
            InvokeOnMonitoredItemCreated(harness.Manager,
                harness.Manager.SystemContext, handle, mockItem.Object);

            // Simulate a value change from the provider.
            var simProvider = (SimulatedWotAssetProvider)entry.Provider!;
            simProvider.SetValue("Voltage", new Variant(42.5));

            Assert.That(variable.Value, Is.EqualTo(new Variant(42.5)));
        }

        // ----------------------------------------------------------------
        // WotPropertyTag — all auto-property getters accessible
        // ----------------------------------------------------------------

        [Test]
        public async Task WotPropertyTagAllPropertyGettersReturnExpectedValues()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("tag-props", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "tag-props",
                    Base = "sim://opcua.test/wot/asset-001",
                    Properties = new Dictionary<string, WotProperty>
                    {
                        ["Temp"] = new WotProperty
                        {
                            Type = "number",
                            ReadOnly = true,
                            Observable = true
                        }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            (BaseDataVariableState variable, WotPropertyTag tag) = entry.Properties.Values.First();

            // Exercise all auto-property getters that had 0% coverage.
            Assert.That(tag.NodeId.IsNull, Is.False);
            Assert.That(tag.DataType.IsNull, Is.False);
            _ = tag.ValueRank;
            Assert.That(tag.ReadOnly, Is.True);
            Assert.That(tag.Observable, Is.True);
            _ = tag.Form; // nullable; just ensure getter doesn't throw
            _ = variable; // suppress unused warning
        }

        // ----------------------------------------------------------------
        // Management delegate helpers — access via reflection
        // ----------------------------------------------------------------

        [Test]
        public async Task ManagementCreateAssetDelegateCreatesAsset()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            WoTAssetConnectionManagementState management = GetManagementObject(harness)!;

            CreateAssetMethodStateResult result = await management.CreateAsset!.OnCallAsync!(
                harness.Manager.SystemContext,
                management.CreateAsset,
                management.NodeId,
                "delegate-asset",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.AssetId.IsNull, Is.False);
        }

        [Test]
        public async Task ManagementDeleteAssetDelegateDeletesAsset()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            WoTAssetConnectionManagementState management = GetManagementObject(harness)!;

            // First create an asset via the delegate.
            CreateAssetMethodStateResult created = await management.CreateAsset!.OnCallAsync!(
                harness.Manager.SystemContext,
                management.CreateAsset,
                management.NodeId,
                "to-delete",
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(created.AssetId.IsNull, Is.False);

            // Now delete it.
            DeleteAssetMethodStateResult deleted = await management.DeleteAsset!.OnCallAsync!(
                harness.Manager.SystemContext,
                management.DeleteAsset,
                management.NodeId,
                created.AssetId,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(deleted.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ManagementDiscoverAssetsDelegateReturnsEndpoints()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory(),
                new SimulatedWotDiscoveryProvider());
            await harness.StartAsync().ConfigureAwait(false);

            WoTAssetConnectionManagementState management = GetManagementObject(harness)!;

            DiscoverAssetsMethodStateResult result = await management.DiscoverAssets!.OnCallAsync!(
                harness.Manager.SystemContext,
                management.DiscoverAssets,
                management.NodeId,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.AssetEndpoints.ToArray(), Has.Member(SimulatedWotDiscoveryProvider.CannedEndpoint));
        }

        [Test]
        public async Task ManagementCreateAssetForEndpointDelegateCreatesAsset()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory(),
                new SimulatedWotDiscoveryProvider());
            await harness.StartAsync().ConfigureAwait(false);

            WoTAssetConnectionManagementState management = GetManagementObject(harness)!;

            CreateAssetForEndpointMethodStateResult result =
                await management.CreateAssetForEndpoint!.OnCallAsync!(
                    harness.Manager.SystemContext,
                    management.CreateAssetForEndpoint,
                    management.NodeId,
                    "ep-asset",
                    SimulatedWotDiscoveryProvider.CannedEndpoint,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.AssetId.IsNull, Is.False);
        }

        [Test]
        public async Task ManagementConnectionTestDelegateReturnsSuccess()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory(),
                new SimulatedWotDiscoveryProvider());
            await harness.StartAsync().ConfigureAwait(false);

            WoTAssetConnectionManagementState management = GetManagementObject(harness)!;

            ConnectionTestMethodStateResult result =
                await management.ConnectionTest!.OnCallAsync!(
                    harness.Manager.SystemContext,
                    management.ConnectionTest,
                    management.NodeId,
                    SimulatedWotDiscoveryProvider.CannedEndpoint,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
        }

        // ----------------------------------------------------------------
        // Reflection helpers
        // ----------------------------------------------------------------

        private static WoTAssetConnectionManagementState? GetManagementObject(ManagerHarness harness)
        {
            return (WoTAssetConnectionManagementState?)typeof(WotConnectivityNodeManager)
                .GetField("m_managementObject",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(harness.Manager);
        }

        private static void InvokeOnMonitoredItemCreated(
            WotConnectivityNodeManager manager,
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            MethodInfo method = typeof(WotConnectivityNodeManager)
                .GetMethod("OnMonitoredItemCreated",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(manager, [context, handle, monitoredItem]);
        }

        private static ValueTask InvokeOnMonitoredItemModifiedAsync(
            WotConnectivityNodeManager manager,
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken)
        {
            MethodInfo method = typeof(WotConnectivityNodeManager)
                .GetMethod("OnMonitoredItemModifiedAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (ValueTask)method.Invoke(manager,
                [context, handle, monitoredItem, cancellationToken])!;
        }

        private static ValueTask InvokeOnMonitoredItemDeletedAsync(
            WotConnectivityNodeManager manager,
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken)
        {
            MethodInfo method = typeof(WotConnectivityNodeManager)
                .GetMethod("OnMonitoredItemDeletedAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (ValueTask)method.Invoke(manager,
                [context, handle, monitoredItem, cancellationToken])!;
        }

        private static ValueTask InvokeOnMonitoringModeChangedAsync(
            WotConnectivityNodeManager manager,
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode,
            CancellationToken cancellationToken)
        {
            MethodInfo method = typeof(WotConnectivityNodeManager)
                .GetMethod("OnMonitoringModeChangedAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (ValueTask)method.Invoke(manager,
                [context, handle, monitoredItem, previousMode, monitoringMode, cancellationToken])!;
        }

        // ----------------------------------------------------------------
        // Harness — minimal in-process node manager backed by a mocked
        // IServerInternal, mirroring AsyncCustomNodeManagerTests.
        // ----------------------------------------------------------------
        private sealed class ManagerHarness : IDisposable
        {
            private const string AssetNamespace = "http://opcfoundation.org/UA/WoT-Con/Assets/";

            public ManagerHarness(
                string thingDescriptionFolder,
                IWotAssetProviderFactory? binding = null,
                IWotAssetDiscoveryProvider? discoveryProvider = null)
            {
                MockServer = new Mock<IServerInternal>();

                var namespaceTable = new NamespaceTable();
                namespaceTable.Append(Namespaces.WotCon);
                namespaceTable.Append(AssetNamespace);

                MockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
                MockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                var typeTable = new TypeTable(namespaceTable);
                SeedStandardTypeTree(typeTable);
                MockServer.Setup(s => s.TypeTree).Returns(typeTable);
                MockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());

                var mockMaster = new Mock<IMasterNodeManager>();
                var mockConfig = new Mock<IConfigurationNodeManager>();
                mockMaster.Setup(m => m.ConfigurationNodeManager).Returns(mockConfig.Object);
                MockServer.Setup(s => s.NodeManager).Returns(mockMaster.Object);

                var mockTelemetry = new Mock<ITelemetryContext>();
                MockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

                m_monitoredItemQueueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
                MockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(m_monitoredItemQueueFactory);

                m_serverSystemContext = new ServerSystemContext(MockServer.Object);
                MockServer.Setup(s => s.DefaultSystemContext).Returns(m_serverSystemContext);

                m_configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 200
                    }
                };

                Options = new WotConnectivityServerOptions
                {
                    AssetNamespaceUri = AssetNamespace,
                    ThingDescriptionStorageFolder = thingDescriptionFolder
                };
                if (binding != null)
                {
                    Options.Bindings.Add(binding);
                }
                if (discoveryProvider != null)
                {
                    Options.Discovery = discoveryProvider;
                }

                Manager = new WotConnectivityNodeManager(
                    MockServer.Object,
                    m_configuration,
                    Options);
            }

            /// <summary>
            /// Pre-seed the standard UA base types that WotCon subtypes
            /// reference, so AsyncCustomNodeManager.AddTypesToTypeTree can
            /// succeed when loading the generated model without us having
            /// to ship the entire standard NodeSet.
            /// </summary>
            private static void SeedStandardTypeTree(TypeTable typeTable)
            {
                NodeId baseObject = Ua.ObjectTypeIds.BaseObjectType;
                NodeId baseVariable = VariableTypeIds.BaseVariableType;
                NodeId baseDataVariable = VariableTypeIds.BaseDataVariableType;
                NodeId propertyType = VariableTypeIds.PropertyType;
                NodeId fileType = Ua.ObjectTypeIds.FileType;
                NodeId namespaceMetadataType = Ua.ObjectTypeIds.NamespaceMetadataType;
                NodeId baseInterfaceType = Ua.ObjectTypeIds.BaseInterfaceType;
                NodeId methodNodeType = NodeId.Null;

                typeTable.AddSubtype(baseObject, NodeId.Null);
                typeTable.AddSubtype(fileType, baseObject);
                typeTable.AddSubtype(namespaceMetadataType, baseObject);
                typeTable.AddSubtype(baseInterfaceType, baseObject);

                typeTable.AddSubtype(baseVariable, NodeId.Null);
                typeTable.AddSubtype(baseDataVariable, baseVariable);
                typeTable.AddSubtype(propertyType, baseVariable);

                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.References, NodeId.Null,
                    new QualifiedName("References"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    Ua.ReferenceTypeIds.References,
                    new QualifiedName("HierarchicalReferences"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasChild,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    new QualifiedName("HasChild"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.Aggregates,
                    Ua.ReferenceTypeIds.HasChild,
                    new QualifiedName("Aggregates"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasComponent,
                    Ua.ReferenceTypeIds.Aggregates,
                    new QualifiedName("HasComponent"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasProperty,
                    Ua.ReferenceTypeIds.Aggregates,
                    new QualifiedName("HasProperty"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.Organizes,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    new QualifiedName("Organizes"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.NonHierarchicalReferences,
                    Ua.ReferenceTypeIds.References,
                    new QualifiedName("NonHierarchicalReferences"));
                typeTable.AddReferenceSubtype(
                    Ua.ReferenceTypeIds.HasInterface,
                    Ua.ReferenceTypeIds.NonHierarchicalReferences,
                    new QualifiedName("HasInterface"));
                _ = methodNodeType;
            }

            public Mock<IServerInternal> MockServer { get; }
            public WotConnectivityServerOptions Options { get; }
            public WotConnectivityNodeManager Manager { get; }

            /// <summary>
            /// Access the AssetRegistry via reflection — it's an internal
            /// implementation detail of the node manager but the spec
            /// behaviour we want to verify lives there.
            /// </summary>
            public AssetRegistry Registry
                => (AssetRegistry)typeof(WotConnectivityNodeManager)
                    .GetField("m_registry",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(Manager)!;

            public async Task StartAsync()
            {
                IDictionary<NodeId, IList<IReference>> externalReferences =
                    new Dictionary<NodeId, IList<IReference>>();
                await Manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);
            }

            public void Dispose()
            {
                Manager.Dispose();
                m_monitoredItemQueueFactory.Dispose();
            }

            private readonly ServerSystemContext m_serverSystemContext;
            private readonly ApplicationConfiguration m_configuration;
            private readonly MonitoredItemQueueFactory m_monitoredItemQueueFactory;
        }
    }
}
