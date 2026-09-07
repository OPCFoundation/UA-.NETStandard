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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.Registry;
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
    public sealed partial class WotConnectivityNodeManagerTests
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

        [Test]
        public async Task ConfigurationParameterWithoutInitialValueUsesTypedDefault()
        {
            using var harness = new ManagerHarness(_tempFolder);
            harness.Options.Configuration["VendorName"] = new WotConfigurationParameter
            {
                DataType = Ua.DataTypeIds.String
            };

            await harness.StartAsync().ConfigureAwait(false);

            Variant actual = GetConfigurationParameterValue(harness, "VendorName");
            Variant expected = TypeInfo.GetDefaultVariantValue(Ua.DataTypeIds.String, ValueRanks.Scalar);

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(actual, Is.Not.EqualTo(Variant.Null));
            Assert.That(actual.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.String));
        }

        [Test]
        public async Task ConfigurationParameterExplicitNullInitialValueStaysUaNull()
        {
            using var harness = new ManagerHarness(_tempFolder);
            harness.Options.Configuration["VendorName"] = new WotConfigurationParameter
            {
                DataType = Ua.DataTypeIds.String,
                InitialValue = Variant.Null
            };

            await harness.StartAsync().ConfigureAwait(false);

            Variant actual = GetConfigurationParameterValue(harness, "VendorName");

            Assert.That(actual, Is.EqualTo(Variant.Null));
        }

        [Test]
        public async Task ConfigurationParameterSpecifiedInitialValuePassesThrough()
        {
            using var harness = new ManagerHarness(_tempFolder);
            harness.Options.Configuration["VendorName"] = new WotConfigurationParameter
            {
                DataType = Ua.DataTypeIds.String,
                InitialValue = new Variant("Acme")
            };

            await harness.StartAsync().ConfigureAwait(false);

            Variant actual = GetConfigurationParameterValue(harness, "VendorName");

            Assert.That(actual.TryGetValue(out string? value), Is.True);
            Assert.That(value, Is.EqualTo("Acme"));
        }

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
            Assert.That(variable.DataType, Is.EqualTo(Ua.DataTypeIds.Double));
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
            Assert.That(tag.InputArguments[0].DataType, Is.EqualTo(Ua.DataTypeIds.Int64));
            Assert.That(tag.OutputArguments, Has.Count.EqualTo(1));
            Assert.That(method.InputArguments, Is.Not.Null);
            Assert.That(method.OutputArguments, Is.Not.Null);
            Assert.That(method.OnCallMethod2Async, Is.Not.Null,
                "OnCallMethod2Async must be wired so the action routes through the provider.");
        }

        [Test]
        public async Task ConditionActionCarriesTheStandardMethodDeclarationAndInvokesProviderAsync()
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
                    ["AcknowledgeOverheating"] = new WotAction
                    {
                        ConditionAction = "Acknowledge",
                        ActsOn = "Overheating",
                        Input = new WotActionSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, WotActionMember>
                            {
                                ["EventId"] = new WotActionMember { Type = "string" }
                            }
                        }
                    }
                }
            };

            await harness.Registry.RebuildAsync(entry, td, persistOnSuccess: false, CancellationToken.None)
                .ConfigureAwait(false);

            (MethodState method, WotActionTag tag) = entry.Actions.Values.First();
            Assert.That(
                method.MethodDeclarationId,
                Is.EqualTo(Ua.MethodIds.AcknowledgeableConditionType_Acknowledge));
            Assert.That(tag.ConditionAction, Is.EqualTo("Acknowledge"));
            Assert.That(tag.ActsOn, Is.EqualTo("Overheating"));

            var outputs = new List<Variant>();
            ServiceResult result = await method.OnCallMethod2Async!(
                harness.Manager.SystemContext,
                method,
                entry.Asset.NodeId,
                [new Variant("event-1")],
                outputs,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            var provider = (SimulatedWotAssetProvider)entry.Provider!;
            Assert.That(provider.Invocations, Has.Count.EqualTo(1));
            Assert.That(provider.Invocations[0].Name, Is.EqualTo("AcknowledgeOverheating"));
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

        /// <summary>
        /// <i>OPC UA — WoT Connectivity</i> §11 requires a Thing Description
        /// auto-generated from a caller-chosen endpoint to be treated as
        /// untrusted input subject to the <c>Wot-Con 1.02</c> format validation
        /// of §14, and §14 requires a document that fails that validation to
        /// materialize nothing and to return <c>Bad_DecodingError</c>. The
        /// discovery provider is pluggable and the endpoint it dialled was
        /// chosen by the caller, so neither is a trusted source.
        /// </summary>
        [Test]
        public async Task CreateAssetForEndpointRejectsAGeneratedThingDescriptionThatDoesNotIdentifyItself()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                discoveryProvider: new UnidentifiedThingDescriptionProvider());
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetForEndpointAsync("asset-x", "sim://endpoint", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(assetId.IsNull, Is.True);
            Assert.That(harness.Registry.AssetNames, Does.Not.Contain("asset-x"),
                "A document that fails format validation must materialize nothing, so the " +
                "asset created to hold it must be removed again.");
        }

        /// <summary>
        /// The counterpart to the test above: a generated Thing Description
        /// that does identify itself passes format validation and reaches the
        /// binding lookup, which this harness registers none for. Reaching
        /// <c>Bad_NotSupported</c> rather than <c>Bad_DecodingError</c> is what
        /// shows the validation gate let it through.
        /// </summary>
        [Test]
        public async Task CreateAssetForEndpointAcceptsAGeneratedThingDescriptionThatIdentifiesItself()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                discoveryProvider: new SimulatedWotDiscoveryProvider());
            await harness.StartAsync().ConfigureAwait(false);

            (ServiceResult status, NodeId assetId) = await harness.Registry
                .CreateAssetForEndpointAsync(
                    "asset-y", SimulatedWotDiscoveryProvider.CannedEndpoint, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(assetId.IsNull, Is.True);
        }

        /// <summary>
        /// A discovery provider whose generated Thing Description carries
        /// neither <c>name</c> nor <c>title</c>. Every member of
        /// <see cref="ThingDescription"/> is optional, so this deserializes
        /// and constructs happily - which is exactly why the format validation
        /// cannot be the deserialization.
        /// </summary>
        private sealed class UnidentifiedThingDescriptionProvider : IWotAssetDiscoveryProvider
        {
            public ValueTask<IReadOnlyList<string>> DiscoverAsync(CancellationToken ct)
            {
                return new(Array.Empty<string>());
            }

            public ValueTask<(bool Success, string Status)> TestAsync(
                string assetEndpoint,
                CancellationToken ct)
            {
                return new((true, "Healthy"));
            }

            public ValueTask<ThingDescription> CreateThingDescriptionAsync(
                string assetName,
                string assetEndpoint,
                CancellationToken ct)
            {
                return new(new ThingDescription { Base = assetEndpoint });
            }
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

        [Test]
        public async Task RebuildAsync_SkipsTdChildrenWithInvalidNames_AndMaterialisesValidOnes()
        {
            // Defence-in-depth: a hostile Thing Description that mixes
            // valid and invalid property / action names must materialise
            // only the safe entries — invalid names are skipped, never
            // turned into NodeIds / QualifiedNames.
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
                Properties = new Dictionary<string, WotProperty>(StringComparer.Ordinal)
                {
                    ["Voltage"] = new WotProperty { Type = "number" },
                    ["temp/erature"] = new WotProperty { Type = "number" }, // path sep
                    ["with.dot"] = new WotProperty { Type = "number" },     // browse-path token
                    ["\u202Eevil"] = new WotProperty { Type = "number" },   // RTL spoof
                    ["bad\u0001ctrl"] = new WotProperty { Type = "number" },// control char
                    [" leading"] = new WotProperty { Type = "number" },     // leading space
                    ["Set-Point"] = new WotProperty { Type = "number" }
                },
                Actions = new Dictionary<string, WotAction>(StringComparer.Ordinal)
                {
                    ["Reset"] = new WotAction { Title = "Reset" },
                    ["bad:ns"] = new WotAction(),
                    ["bad!ref"] = new WotAction()
                }
            };

            await harness.Registry.RebuildAsync(
                entry, td, persistOnSuccess: false, CancellationToken.None)
                .ConfigureAwait(false);

            string?[] propNames = [.. entry.Properties.Values.Select(v => v.Variable.BrowseName.Name)];
            Assert.That(propNames, Is.EquivalentTo(s_validPropertyAndActionMix_ExpectedProps));

            string?[] actionNames = [.. entry.Actions.Values.Select(a => a.Method.BrowseName.Name)];
            Assert.That(actionNames, Is.EquivalentTo(s_validPropertyAndActionMix_ExpectedActions));
        }

        [Test]
        public async Task RebuildAsync_TooLongChildName_IsSkipped()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;

            string longName = new('a', WotChildNameValidator.MaxLength + 1);
            var td = new ThingDescription
            {
                Name = "asset-001",
                Base = "sim://opcua.test/wot/asset-001",
                Properties = new Dictionary<string, WotProperty>(StringComparer.Ordinal)
                {
                    ["Voltage"] = new WotProperty { Type = "number" },
                    [longName] = new WotProperty { Type = "number" }
                }
            };

            await harness.Registry.RebuildAsync(
                entry, td, persistOnSuccess: false, CancellationToken.None)
                .ConfigureAwait(false);

            string?[] propNames = [.. entry.Properties.Values.Select(v => v.Variable.BrowseName.Name)];
            Assert.That(propNames, Is.EquivalentTo(s_tooLongChildName_ExpectedProps));
        }

        private static readonly string[] s_validPropertyAndActionMix_ExpectedProps =
            ["Voltage", "Set-Point"];

        private static readonly string[] s_validPropertyAndActionMix_ExpectedActions =
            ["Reset"];

        private static readonly string[] s_tooLongChildName_ExpectedProps =
            ["Voltage"];

        [Test]
        public async Task LegacyAssetIsMirroredIntoV2RegistryWithoutDivergence()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            using var registry = new WotRegistryService();
            harness.Options.RegistryBridge = registry;
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
                    ["Voltage"] = new WotProperty { Type = "number" }
                }
            };
            await harness.Registry.RebuildAsync(
                entry,
                td,
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            WotResource? resource = registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "asset-001");
            Assert.That(resource, Is.Not.Null,
                "A legacy asset must have a matching V2 registry resource.");

            byte[] expected = JsonSerializer.SerializeToUtf8Bytes(
                td,
                ThingDescriptionJsonContext.Default.ThingDescription);
            // The version carries the digest rather than the bytes, and a SHA-256
            // match is the same assertion: the mirrored document is byte-identical.
            Assert.That(
                resource!.DefaultVersion!.DigestHex,
                Is.EqualTo(WotContentDigest.ToHex(WotContentDigest.Compute(expected))),
                "The mirrored registry document must not diverge from the legacy document.");
            Assert.That(
                resource.DefaultVersion!.ContentLength,
                Is.EqualTo(expected.Length));

            ServiceResult delete = await harness.Registry
                .DeleteAssetAsync(assetId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(delete), Is.True);
            Assert.That(
                registry.Current.FindResource(WotRegistryGroups.ThingDescriptions, "asset-001"),
                Is.Null,
                "Deleting a legacy asset must remove its registry resource.");
        }

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

        private static Variant GetConfigurationParameterValue(ManagerHarness harness, string browseName)
        {
            BaseInstanceState parameter = harness.FindManagementChild("Configuration", browseName);
            var variable = (BaseVariableState)parameter;

            Assert.That(variable.Value, Is.TypeOf<Variant>());
            return (Variant)variable.Value;
        }

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
                // Test scheme: the simulated discovery provider returns
                // sim:// endpoints. Opt the harness's policy into that
                // scheme so the SSRF allow-list does not reject the
                // happy-path tests.
                Options.AssetEndpointPolicy.AllowedSchemes.Add("sim");
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
                typeTable.AddSubtype(Ua.ObjectTypeIds.BaseEventType, baseObject);

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

            public BaseInstanceState FindManagementChild(params string[] browseNames)
            {
                NodeState current = (NodeState)typeof(WotConnectivityNodeManager)
                    .GetField("m_managementObject",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(Manager)!;

                foreach (string browseName in browseNames)
                {
                    var children = new List<BaseInstanceState>();
                    current.GetChildren(m_serverSystemContext, children);
                    current = children.Single(c => c.BrowseName.Name == browseName);
                }

                return (BaseInstanceState)current;
            }

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
