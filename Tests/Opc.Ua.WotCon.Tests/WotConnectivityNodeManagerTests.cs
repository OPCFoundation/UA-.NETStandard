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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
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
                { Directory.Delete(_tempFolder, recursive: true); }
                catch { /* swallow */ }
            }
        }

        // ----------------------------------------------------------------
        // CreateAsset: name validation and dup detection.
        // ----------------------------------------------------------------

        [Test]
        public async Task CreateAssetWithEmptyNameReturnsBadInvalidArgument()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync();

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync(string.Empty, CancellationToken.None);

            Assert.That(status.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(assetId.IsNull, Is.True);
        }

        [Test]
        public async Task CreateAssetWithWhitespaceNameReturnsBadInvalidArgument()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync();

            (ServiceResult status, _) = await harness.Registry
                .CreateAssetAsync("   ", CancellationToken.None);

            Assert.That(status.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
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
            await harness.StartAsync();

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync(name, CancellationToken.None);

            Assert.That(status.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument),
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
            await harness.StartAsync();
            string longName = new('a', WotAssetNameValidator.MaxNameLength + 1);

            (ServiceResult status, _) = await harness.Registry
                .CreateAssetAsync(longName, CancellationToken.None);

            Assert.That(status.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreateAssetWithUniqueNameReturnsGoodAndNonNullNodeId()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync();

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(assetId.IsNull, Is.False);
            Assert.That(harness.Registry.AssetNames, Has.Member("asset-001"));
        }

        [Test]
        public async Task CreateAssetWithDuplicateNameReturnsBadBrowseNameDuplicated()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync();
            await harness.Registry.CreateAssetAsync("asset-001", CancellationToken.None);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);

            Assert.That(status.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
            Assert.That(assetId.IsNull, Is.True);
        }

        // ----------------------------------------------------------------
        // DeleteAsset: missing-id and happy path.
        // ----------------------------------------------------------------

        [Test]
        public async Task DeleteAssetReturnsBadNotFoundForUnknownId()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync();

            ServiceResult status = await harness.Registry
                .DeleteAssetAsync(new NodeId(99999u, 3), CancellationToken.None);

            Assert.That(status.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
        }

        [Test]
        public async Task DeleteAssetRemovesItFromTheRegistry()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);

            ServiceResult status = await harness.Registry
                .DeleteAssetAsync(assetId, CancellationToken.None);

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
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            var td = new ThingDescription
            {
                Name = "asset-001",
                Base = "http://example.com/asset/1" // no registered factory handles http
            };

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None);

            Assert.That(status.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task RebuildWithMatchingFactoryReturnsGoodAndPersistsTd()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            var td = new ThingDescription
            {
                Name = "asset-001",
                Base = "sim://opcua.test/wot/asset-001"
            };

            ServiceResult status = await harness.Registry
                .RebuildAsync(entry, td, persistOnSuccess: true, CancellationToken.None);

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
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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
                .RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None);

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
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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

            await harness.Registry.RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task RebuildSimpleReadValueDelegatesToProviderForReadablePropertyValues()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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
                CancellationToken.None);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            // Push a provider-side value the simulated provider remembers.
            ((SimulatedWotAssetProvider)entry.Provider!).SetValue("Voltage", new Variant(12.3));

            Assert.That(variable.OnSimpleReadValueAsync, Is.Not.Null);
            Assert.That(variable.OnSimpleReadValue, Is.Null,
                "Async hooks should replace the sync OnSimpleReadValue.");

            AttributeSimpleReadResult result = await variable.OnSimpleReadValueAsync!(
                harness.Manager.SystemContext, variable, CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result.Result), Is.True);
            Assert.That(result.Value.AsBoxedObject(), Is.EqualTo(12.3));
        }

        [Test]
        public async Task RebuildSimpleWriteValueDelegatesToProviderForWritableProperties()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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
                CancellationToken.None);
            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            Assert.That(variable.OnSimpleWriteValueAsync, Is.Not.Null,
                "OnSimpleWriteValueAsync must be wired for non-read-only properties.");
            Assert.That(variable.OnSimpleWriteValue, Is.Null,
                "Async hooks should replace the sync OnSimpleWriteValue.");

            AttributeWriteResult result = await variable.OnSimpleWriteValueAsync!(
                harness.Manager.SystemContext, variable, new Variant(42.5), CancellationToken.None);

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
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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
                CancellationToken.None);

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
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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
                CancellationToken.None);

            (BaseDataVariableState variable, _) = entry.Properties.Values.First();
            Assert.That(variable.OnSimpleReadValueAsync, Is.Not.Null);
            Assert.That(variable.OnSimpleReadValue, Is.Null);
            AttributeSimpleReadResult result = await variable.OnSimpleReadValueAsync!(
                harness.Manager.SystemContext, variable, CancellationToken.None);
            Assert.That((uint)result.Result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(result.Value.IsNull, Is.True);
        }

        [Test]
        public async Task RebuildMaterialisesActionMethodWithExpectedArguments()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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

            await harness.Registry.RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None);

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
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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
                persistOnSuccess: false, CancellationToken.None);
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
                persistOnSuccess: false, CancellationToken.None);

            Assert.That(entry.Properties, Has.Count.EqualTo(1));
            Assert.That(entry.Properties.Values.First().Tag.Name, Is.EqualTo("Power"));
        }

        [Test]
        public async Task FindByNodeIdReturnsNullForUnknownAsset()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync();

            Assert.That(harness.Registry.FindByNodeId(new NodeId(999u, 3)), Is.Null);
        }

        [Test]
        public async Task TryGetPropertyResolvesMaterialisedVariableByNodeId()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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
                persistOnSuccess: false, CancellationToken.None);

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
            await harness.StartAsync();
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None);
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
                persistOnSuccess: false, CancellationToken.None);

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
            await harness.StartAsync();

            (ServiceResult status, IReadOnlyList<string> endpoints) = await harness.Registry
                .DiscoverAssetsAsync(CancellationToken.None);

            Assert.That(status.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(endpoints, Is.Empty);
        }

        [Test]
        public async Task ConnectionTestReturnsBadNotSupportedWhenNoDiscoveryProvider()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync();

            (ServiceResult status, bool success, string text) = await harness.Registry
                .ConnectionTestAsync("sim://foo", CancellationToken.None);

            Assert.That(status.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(success, Is.False);
            Assert.That(text, Is.Empty);
        }

        [Test]
        public async Task CreateAssetForEndpointReturnsBadNotSupportedWhenNoDiscoveryProvider()
        {
            using var harness = new ManagerHarness(_tempFolder);
            await harness.StartAsync();

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetForEndpointAsync("asset-x", "sim://endpoint", CancellationToken.None);

            Assert.That(status.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(assetId.IsNull, Is.True);
        }

        [Test]
        public async Task DiscoverAssetsForwardsToConfiguredProvider()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                discoveryProvider: new SimulatedWotDiscoveryProvider());
            await harness.StartAsync();

            (ServiceResult status, IReadOnlyList<string> endpoints) = await harness.Registry
                .DiscoverAssetsAsync(CancellationToken.None);

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
            await harness.StartAsync();

            (ServiceResult status, bool success, _) = await harness.Registry
                .ConnectionTestAsync(SimulatedWotDiscoveryProvider.CannedEndpoint, CancellationToken.None);

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
            (NodeId originalAssetId, string assetName) = (NodeId.Null, "asset-001");
            using (var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory()))
            {
                await harness.StartAsync();
                (_, NodeId id) = await harness.Registry
                    .CreateAssetAsync(assetName, CancellationToken.None);
                originalAssetId = id;
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
                    CancellationToken.None);
            }

            Assert.That(File.Exists(Path.Combine(_tempFolder, assetName + ".jsonld")), Is.True);

            // Round 2: brand new manager pointing at the same folder must
            // restore the asset on startup so the same name resolves to a
            // live AssetEntry without a fresh CreateAsset call.
            using var reloaded = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await reloaded.StartAsync();

            Assert.That(reloaded.Registry.AssetNames, Has.Member(assetName));
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
                { Options.Bindings.Add(binding); }
                if (discoveryProvider != null)
                { Options.Discovery = discoveryProvider; }

                Manager = new WotConnectivityNodeManager(
                    MockServer.Object,
                    m_configuration,
                    Options);
            }

            // Pre-seed the standard UA base types that WotCon subtypes
            // reference, so AsyncCustomNodeManager.AddTypesToTypeTree can
            // succeed when loading the generated model without us having
            // to ship the entire standard NodeSet.
            private static void SeedStandardTypeTree(TypeTable typeTable)
            {
                NodeId baseObject = Opc.Ua.ObjectTypeIds.BaseObjectType;
                NodeId baseVariable = Opc.Ua.VariableTypeIds.BaseVariableType;
                NodeId baseDataVariable = Opc.Ua.VariableTypeIds.BaseDataVariableType;
                NodeId propertyType = Opc.Ua.VariableTypeIds.PropertyType;
                NodeId fileType = Opc.Ua.ObjectTypeIds.FileType;
                NodeId namespaceMetadataType = Opc.Ua.ObjectTypeIds.NamespaceMetadataType;
                NodeId baseInterfaceType = Opc.Ua.ObjectTypeIds.BaseInterfaceType;
                NodeId methodNodeType = NodeId.Null;

                typeTable.AddSubtype(baseObject, NodeId.Null);
                typeTable.AddSubtype(fileType, baseObject);
                typeTable.AddSubtype(namespaceMetadataType, baseObject);
                typeTable.AddSubtype(baseInterfaceType, baseObject);

                typeTable.AddSubtype(baseVariable, NodeId.Null);
                typeTable.AddSubtype(baseDataVariable, baseVariable);
                typeTable.AddSubtype(propertyType, baseVariable);

                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.References, NodeId.Null,
                    new QualifiedName("References"));
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    Opc.Ua.ReferenceTypeIds.References,
                    new QualifiedName("HierarchicalReferences"));
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.HasChild,
                    Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    new QualifiedName("HasChild"));
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.Aggregates,
                    Opc.Ua.ReferenceTypeIds.HasChild,
                    new QualifiedName("Aggregates"));
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.HasComponent,
                    Opc.Ua.ReferenceTypeIds.Aggregates,
                    new QualifiedName("HasComponent"));
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.HasProperty,
                    Opc.Ua.ReferenceTypeIds.Aggregates,
                    new QualifiedName("HasProperty"));
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.Organizes,
                    Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    new QualifiedName("Organizes"));
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.NonHierarchicalReferences,
                    Opc.Ua.ReferenceTypeIds.References,
                    new QualifiedName("NonHierarchicalReferences"));
                typeTable.AddReferenceSubtype(
                    Opc.Ua.ReferenceTypeIds.HasInterface,
                    Opc.Ua.ReferenceTypeIds.NonHierarchicalReferences,
                    new QualifiedName("HasInterface"));
                _ = methodNodeType;
            }

            public Mock<IServerInternal> MockServer { get; }
            public WotConnectivityServerOptions Options { get; }
            public WotConnectivityNodeManager Manager { get; }

            // Access the AssetRegistry via reflection — it's an internal
            // implementation detail of the node manager but the spec
            // behaviour we want to verify lives there.
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
                await Manager.CreateAddressSpaceAsync(externalReferences);
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
