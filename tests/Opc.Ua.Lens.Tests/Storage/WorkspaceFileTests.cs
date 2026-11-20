// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Storage
{
    [TestFixture]
    public sealed class WorkspaceFileTests
    {
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(Path.GetTempPath(), "UaLens-workspace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_directory);
            m_path = Path.Combine(m_directory, "workspace.json");
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(m_directory, recursive: true);
        }

        [Test]
        public async Task WorkspaceRoundTripPreservesPolicyDocumentsSelectionAndPipeline()
        {
            var file = new SessionFile
            {
                Version = "2",
                EndpointUrl = "opc.tcp://localhost:62541/Server",
                Profile = new ConnectionProfile
                {
                    EndpointUrl = "opc.tcp://localhost:62541/Server",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    IdentityType = UserTokenType.UserName,
                    IdentityName = "operator",
                    UserTokenPolicyId = "user"
                },
                PublishingPipeline = new SessionPublishingSettings(4, 12),
                SelectedDocument = 1,
                Inspector = SidePanelMode.AttrsAndRefs,
                ShowAddressSpace = false,
                Documents =
                [
                    Document(PluginKind.Subscription, "Pressure"),
                    Document(PluginKind.CertificateManager, "Local certificates")
                ]
            };
            await SessionFile.SaveAsync(file, m_path).ConfigureAwait(false);
            SessionFile? restored = await SessionFile.LoadAsync(m_path).ConfigureAwait(false);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Profile, Is.EqualTo(file.Profile));
            Assert.That(restored.PublishingPipeline, Is.EqualTo(new SessionPublishingSettings(4, 12)));
            Assert.That(restored.SelectedDocument, Is.EqualTo(1));
            Assert.That(restored.Documents[1].Title, Is.EqualTo("Local certificates"));
            Assert.That(restored.Inspector, Is.EqualTo(SidePanelMode.AttrsAndRefs));
            Assert.That(restored.ShowAddressSpace, Is.False);
            string json = await File.ReadAllTextAsync(m_path).ConfigureAwait(false);
            Assert.That(
                json, Does.Not.Contain("password").And.Not.Contain("currentSession").And.Not.Contain("privateKey"));
        }

        [Test]
        public async Task LegacyFileRetainsSubscriptionAndFractionalSamplingWithoutInventingSecurity()
        {
            var file = new SessionFile
            {
                EndpointUrl = "opc.tcp://localhost:62541/Server",
                Tabs =
                [
                    new SessionFile.TabSnapshot
                    {
                        Title = "Legacy",
                        Items =
                        [
                            new SessionFile.ItemSnapshot
                            {
                                NodeId = "i=2258",
                                SamplingInterval = new SessionFile.TimeSpanMs(0.125)
                            }
                        ]
                    }
                ]
            };
            await SessionFile.SaveAsync(file, m_path).ConfigureAwait(false);
            SessionFile? restored = await SessionFile.LoadAsync(m_path).ConfigureAwait(false);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Version, Is.EqualTo("1"));
            Assert.That(restored.Profile, Is.Null);
            Assert.That(restored.Tabs[0].Items[0].SamplingInterval.Milliseconds, Is.EqualTo(0.125));
        }

        [TestCase("99", "Subscription")]
        [TestCase("2", "UnknownTool")]
        public async Task UnsupportedWorkspaceIsRejectedWithoutRewritingInput(string version, string kind)
        {
            string json = "{\"version\":\"" +
                version +
                "\",\"documents\":[{\"kind\":\"" +
                kind +
                "\",\"title\":\"Preserve me\",\"settings\":{\"version\":1}}]}";
            await File.WriteAllTextAsync(m_path, json).ConfigureAwait(false);

            await Assert.ThatAsync(() => SessionFile.LoadAsync(m_path), Throws.InstanceOf<JsonException>())
                .ConfigureAwait(false);
            Assert.That(await File.ReadAllTextAsync(m_path).ConfigureAwait(false), Is.EqualTo(json));
        }

        [Test]
        public void MonitorConfigurationRetainsFilterAndDisplayChoices()
        {
            var tab = new SessionFile.TabSnapshot
            {
                Title = "Filtered monitor",
                DisplayModeIndex = 1,
                ShowLegend = true,
                ShowXAxis = true,
                AnimationMode = "Signal",
                Items =
                [
                    new SessionFile.ItemSnapshot
                    {
                        NodeId = "i=2258",
                        Filter = new SessionFile.FilterSnapshot
                        {
                            Trigger = DataChangeTrigger.StatusValueTimestamp,
                            DeadbandType = (uint)DeadbandType.Absolute,
                            DeadbandValue = 0.25
                        }
                    }
                ]
            };

            SessionFile.TabSnapshot restored = SubscriptionDocumentState.Import(tab).Export();

            Assert.That(restored.DisplayModeIndex, Is.EqualTo(1));
            Assert.That(restored.ShowLegend, Is.True);
            Assert.That(restored.ShowXAxis, Is.True);
            Assert.That(restored.Items[0].Filter, Is.Not.Null);
            Assert.That(restored.Items[0].Filter!.DeadbandValue, Is.EqualTo(0.25));
            Assert.That(restored.Items[0].Filter!.Trigger, Is.EqualTo(DataChangeTrigger.StatusValueTimestamp));
        }

        private static SessionFile.DocumentSnapshot Document(PluginKind kind, string title)
        {
            using var settings = JsonDocument.Parse("{\"version\":1}");
            return new SessionFile.DocumentSnapshot
            {
                Kind = kind.ToString(),
                Title = title,
                Settings = settings.RootElement.Clone()
            };
        }

        private string m_directory = string.Empty;
        private string m_path = string.Empty;
    }
}
