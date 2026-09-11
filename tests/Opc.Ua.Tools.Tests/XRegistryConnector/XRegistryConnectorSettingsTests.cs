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

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Connector;

namespace Opc.Ua.Tools.Tests.XRegistryConnector
{
    [TestFixture]
    [Category("XRegistryConnector")]
    public sealed class XRegistryConnectorSettingsTests
    {
        [Test]
        public void DefaultsRequireManualConflictsGuardedDeletesAndExplicitExecutionFlags()
        {
            var settings = new XRegistryConnectorSettings();

            Assert.That(settings.ConflictPolicy, Is.EqualTo("manual"));
            Assert.That(settings.PropagateDeletes, Is.True);
            Assert.That(settings.DryRun, Is.False);
            Assert.That(settings.Once, Is.False);
            Assert.That(settings.AllowLoopbackHttp, Is.False);
            Assert.That(settings.CredentialProfile, Is.EqualTo("default"));
            Assert.That(settings.JobId, Is.EqualTo("xregistry"));
            Assert.That(settings.PollInterval, Is.EqualTo(TimeSpan.FromSeconds(5)));
        }

        [TestCase(XRegistryConnectorCommand.HttpGateway)]
        [TestCase(XRegistryConnectorCommand.OpcUaGateway)]
        [TestCase(XRegistryConnectorCommand.Sync)]
        [TestCase(XRegistryConnectorCommand.Inspect)]
        [TestCase(XRegistryConnectorCommand.Conflicts)]
        [TestCase(XRegistryConnectorCommand.Resolve)]
        public void EachCommandAcceptsItsRequiredConfigurationWithoutOpeningState(XRegistryConnectorCommand command)
        {
            string state = Path.Combine(Path.GetTempPath(), "xregistry-unused-" + Guid.NewGuid().ToString("N"));
            var settings = new XRegistryConnectorSettings
            {
                Command = command,
                OpcUaEndpoint = command is XRegistryConnectorCommand.HttpGateway or XRegistryConnectorCommand.Sync
                    ? new Uri("opc.tcp://localhost:4840") : null,
                RegistryNodeId = command is XRegistryConnectorCommand.HttpGateway or XRegistryConnectorCommand.Sync
                    ? "ns=2;s=Registry" : null,
                HttpRoot = command is XRegistryConnectorCommand.OpcUaGateway or XRegistryConnectorCommand.Sync
                    or XRegistryConnectorCommand.Inspect ? new Uri("https://registry.example/") : null,
                ListenAddress = command switch
                {
                    XRegistryConnectorCommand.HttpGateway => new Uri("https://localhost:8443/"),
                    XRegistryConnectorCommand.OpcUaGateway => new Uri("opc.tcp://localhost:4841"),
                    _ => null
                },
                PublicHttpRoot = command == XRegistryConnectorCommand.HttpGateway
                    ? new Uri("https://bridge.example/registries/public/") : null,
                StateDirectory = command is XRegistryConnectorCommand.Sync or XRegistryConnectorCommand.Conflicts
                    or XRegistryConnectorCommand.Resolve ? state : null,
                ConflictId = command == XRegistryConnectorCommand.Resolve ? "conflict-42" : null,
                Resolution = command == XRegistryConnectorCommand.Resolve ? "prefer-http" : null
            };

            Assert.That(settings.Validate, Throws.Nothing);
            Assert.That(Directory.Exists(state), Is.False);
        }

        [TestCase(XRegistryConnectorCommand.HttpGateway, "OpcUaEndpoint", "--opcua")]
        [TestCase(XRegistryConnectorCommand.HttpGateway, "RegistryNodeId", "--registry-node")]
        [TestCase(XRegistryConnectorCommand.HttpGateway, "ListenAddress", "--listen")]
        [TestCase(XRegistryConnectorCommand.HttpGateway, "PublicHttpRoot", "--public-root")]
        [TestCase(XRegistryConnectorCommand.OpcUaGateway, "HttpRoot", "--http-root")]
        [TestCase(XRegistryConnectorCommand.OpcUaGateway, "ListenAddress", "--listen")]
        [TestCase(XRegistryConnectorCommand.Sync, "OpcUaEndpoint", "--opcua")]
        [TestCase(XRegistryConnectorCommand.Sync, "RegistryNodeId", "--registry-node")]
        [TestCase(XRegistryConnectorCommand.Sync, "HttpRoot", "--http-root")]
        [TestCase(XRegistryConnectorCommand.Sync, "StateDirectory", "--state")]
        [TestCase(XRegistryConnectorCommand.Inspect, "RegistryNodeId", "--registry-node")]
        [TestCase(XRegistryConnectorCommand.Conflicts, "StateDirectory", "--state")]
        [TestCase(XRegistryConnectorCommand.Resolve, "StateDirectory", "--state")]
        [TestCase(XRegistryConnectorCommand.Resolve, "ConflictId", "--conflict")]
        public void ValidationRejectsEachMissingModeRequirement(
            XRegistryConnectorCommand command,
            string missing,
            string diagnostic)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = command,
                OpcUaEndpoint = new Uri("opc.tcp://localhost:4840"),
                RegistryNodeId = "ns=2;s=Registry",
                HttpRoot = new Uri("https://registry.example/"),
                ListenAddress = command == XRegistryConnectorCommand.OpcUaGateway
                    ? new Uri("opc.tcp://localhost:4841") : new Uri("https://localhost:8443/"),
                PublicHttpRoot = new Uri("https://bridge.example/"),
                StateDirectory = "xregistry-unit-state",
                ConflictId = "conflict-42",
                Resolution = "prefer-http"
            };
            settings = missing switch
            {
                "OpcUaEndpoint" => settings with { OpcUaEndpoint = null },
                "RegistryNodeId" => settings with { RegistryNodeId = null },
                "HttpRoot" => settings with { HttpRoot = null },
                "ListenAddress" => settings with { ListenAddress = null },
                "PublicHttpRoot" => settings with { PublicHttpRoot = null },
                "StateDirectory" => settings with { StateDirectory = null },
                "ConflictId" => settings with { ConflictId = null },
                _ => throw new ArgumentOutOfRangeException(nameof(missing))
            };

            Assert.That(settings.Validate, Throws.ArgumentException.With.Message.Contains(diagnostic));
        }

        [TestCase("", "", "")]
        [TestCase(" ", "\t", " \t")]
        public void RequiredStringInputsRejectEmptyAndWhitespaceValues(
            string registryNode,
            string state,
            string conflict)
        {
            var inspect = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Inspect,
                OpcUaEndpoint = new Uri("opc.tcp://localhost:4840"),
                RegistryNodeId = registryNode
            };
            var conflicts = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Conflicts,
                StateDirectory = state
            };
            var resolve = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Resolve,
                StateDirectory = "xregistry-unit-state",
                ConflictId = conflict,
                Resolution = "prefer-http"
            };

            Assert.That(inspect.Validate, Throws.ArgumentException.With.Message.Contains("--registry-node"));
            Assert.That(conflicts.Validate, Throws.ArgumentException.With.Message.Contains("--state"));
            Assert.That(resolve.Validate, Throws.ArgumentException.With.Message.Contains("--conflict"));
        }

        [TestCase(-1)]
        [TestCase(6)]
        public void UnknownCommandsAreRejected(int command)
        {
            var settings = new XRegistryConnectorSettings { Command = (XRegistryConnectorCommand)command };

            Assert.That(settings.Validate,
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("Command"));
        }

        [Test]
        public void InspectRequiresAtLeastOneEndpoint()
        {
            var settings = new XRegistryConnectorSettings { Command = XRegistryConnectorCommand.Inspect };

            Assert.That(settings.Validate,
                Throws.ArgumentException.With.Message.Contains("Inspect requires --opcua or --http-root"));
        }

        [TestCase("opc.tcp://localhost:4840")]
        [TestCase("opc.wss://registry.example/")]
        [TestCase("https://registry.example/")]
        [TestCase("opc.quic://registry.example:4840")]
        public void InspectAcceptsEachSecureCapableNativeTransport(string endpoint)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Inspect,
                OpcUaEndpoint = new Uri(endpoint),
                RegistryNodeId = "ns=2;s=Registry"
            };

            Assert.That(settings.Validate, Throws.Nothing);
        }

        [TestCase("http://localhost:4840")]
        [TestCase("ftp://registry.example/")]
        [TestCase("file:///C:/registry")]
        public void NativeEndpointRejectsUnsupportedTransportsEvenWithLoopbackOverride(string endpoint)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Inspect,
                OpcUaEndpoint = new Uri(endpoint),
                RegistryNodeId = "ns=2;s=Registry",
                AllowLoopbackHttp = true
            };

            Assert.That(settings.Validate,
                Throws.ArgumentException.With.Property("ParamName").EqualTo("OpcUaEndpoint"));
        }

        [Test]
        public void EveryEndpointRejectsCredentialsQueryFragmentAndRelativeUris(
            [Values("OpcUaEndpoint", "HttpRoot", "ListenAddress", "PublicHttpRoot")] string field,
            [Values("/relative", "https://operator@registry.example/", "https://registry.example/?q=1",
                "https://registry.example/#fragment")] string address)
        {
            var invalid = new Uri(address, UriKind.RelativeOrAbsolute);
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.HttpGateway,
                OpcUaEndpoint = new Uri("opc.tcp://localhost:4840"),
                RegistryNodeId = "ns=2;s=Registry",
                HttpRoot = new Uri("https://registry.example/"),
                ListenAddress = new Uri("https://localhost:8443/"),
                PublicHttpRoot = new Uri("https://bridge.example/")
            };
            settings = field switch
            {
                "OpcUaEndpoint" => settings with { OpcUaEndpoint = invalid },
                "HttpRoot" => settings with { HttpRoot = invalid },
                "ListenAddress" => settings with { ListenAddress = invalid },
                "PublicHttpRoot" => settings with { PublicHttpRoot = invalid },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };

            Assert.That(settings.Validate, Throws.ArgumentException.With.Property("ParamName").EqualTo(field));
        }

        [Test]
        public void LoopbackHttpRequiresOptInOnEveryHttpAddress(
            [Values("HttpRoot", "ListenAddress", "PublicHttpRoot")] string field,
            [Values("http://localhost:8080/", "http://127.0.0.1:8080/", "http://[::1]:8080/")] string address,
            [Values(false, true)] bool allowLoopback)
        {
            var local = new Uri(address);
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.HttpGateway,
                OpcUaEndpoint = new Uri("opc.tcp://localhost:4840"),
                RegistryNodeId = "ns=2;s=Registry",
                HttpRoot = new Uri("https://registry.example/"),
                ListenAddress = new Uri("https://localhost:8443/"),
                PublicHttpRoot = new Uri("https://bridge.example/"),
                AllowLoopbackHttp = allowLoopback
            };
            settings = field switch
            {
                "HttpRoot" => settings with { HttpRoot = local },
                "ListenAddress" => settings with { ListenAddress = local },
                "PublicHttpRoot" => settings with { PublicHttpRoot = local },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };

            if (allowLoopback)
            {
                Assert.That(settings.Validate, Throws.Nothing);
            }
            else
            {
                Assert.That(settings.Validate, Throws.ArgumentException.With.Property("ParamName").EqualTo(field));
            }
        }

        [Test]
        public void LoopbackOptInNeverPermitsRemotePlaintextHttp(
            [Values("HttpRoot", "ListenAddress", "PublicHttpRoot")] string field)
        {
            var remote = new Uri("http://registry.example/");
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.HttpGateway,
                OpcUaEndpoint = new Uri("opc.tcp://localhost:4840"),
                RegistryNodeId = "ns=2;s=Registry",
                HttpRoot = new Uri("https://registry.example/"),
                ListenAddress = new Uri("https://localhost:8443/"),
                PublicHttpRoot = new Uri("https://bridge.example/"),
                AllowLoopbackHttp = true
            };
            settings = field switch
            {
                "HttpRoot" => settings with { HttpRoot = remote },
                "ListenAddress" => settings with { ListenAddress = remote },
                "PublicHttpRoot" => settings with { PublicHttpRoot = remote },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };

            Assert.That(settings.Validate,
                Throws.ArgumentException.With.Message.Contains("HTTPS").And.Property("ParamName").EqualTo(field));
        }

        [Test]
        public void LoopbackOverrideDoesNotPermitNonHttpSchemesOnHttpAddresses(
            [Values("HttpRoot", "ListenAddress", "PublicHttpRoot")] string field)
        {
            var invalid = new Uri("ftp://localhost/");
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.HttpGateway,
                OpcUaEndpoint = new Uri("opc.tcp://localhost:4840"),
                RegistryNodeId = "ns=2;s=Registry",
                HttpRoot = new Uri("https://registry.example/"),
                ListenAddress = new Uri("https://localhost:8443/"),
                PublicHttpRoot = new Uri("https://bridge.example/"),
                AllowLoopbackHttp = true
            };
            settings = field switch
            {
                "HttpRoot" => settings with { HttpRoot = invalid },
                "ListenAddress" => settings with { ListenAddress = invalid },
                "PublicHttpRoot" => settings with { PublicHttpRoot = invalid },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };

            Assert.That(settings.Validate, Throws.ArgumentException.With.Property("ParamName").EqualTo(field));
        }

        [Test]
        public void HttpGatewayListenerMustBeAnOriginWhilePublicRootMayHaveABasePath()
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.HttpGateway,
                OpcUaEndpoint = new Uri("opc.tcp://localhost:4840"),
                RegistryNodeId = "ns=2;s=Registry",
                ListenAddress = new Uri("https://localhost:8443/"),
                PublicHttpRoot = new Uri("https://bridge.example/registries/public/")
            };

            Assert.That(settings.Validate, Throws.Nothing);
            Assert.That((settings with { ListenAddress = new Uri("https://localhost:8443/base/") }).Validate,
                Throws.ArgumentException.With.Property("ParamName").EqualTo("ListenAddress"));
        }

        [TestCase("https://localhost:4841/")]
        [TestCase("http://localhost:4841/")]
        [TestCase("opc.wss://localhost:4841/")]
        public void OpcUaGatewayListenerMustUseOpcTcp(string listener)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.OpcUaGateway,
                HttpRoot = new Uri("https://registry.example/"),
                ListenAddress = new Uri(listener),
                AllowLoopbackHttp = true
            };

            Assert.That(settings.Validate,
                Throws.ArgumentException.With.Property("ParamName").EqualTo("ListenAddress"));
        }

        [TestCase("manual")]
        [TestCase("prefer-opcua")]
        [TestCase("prefer-http")]
        public void OnlyDocumentedConflictPoliciesAreAccepted(string policy)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Inspect,
                HttpRoot = new Uri("https://registry.example/"),
                ConflictPolicy = policy
            };

            Assert.That(settings.Validate, Throws.Nothing);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Manual")]
        [TestCase("last-writer-wins")]
        [TestCase("prefer-latest")]
        public void UnsupportedConflictPoliciesAreRejected(string? policy)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Inspect,
                HttpRoot = new Uri("https://registry.example/"),
                ConflictPolicy = policy!
            };

            Assert.That(settings.Validate,
                Throws.ArgumentException.With.Property("ParamName").EqualTo("ConflictPolicy"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("manual")]
        [TestCase("last-writer-wins")]
        public void ResolutionRequiresAnExplicitSidePreference(string? resolution)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Resolve,
                StateDirectory = "xregistry-unit-state",
                ConflictId = "conflict-42",
                Resolution = resolution
            };

            Assert.That(settings.Validate, Throws.ArgumentException.With.Property("ParamName").EqualTo("Resolution"));
        }

        [TestCase(-1L, false)]
        [TestCase(0L, false)]
        [TestCase(1L, true)]
        [TestCase(864000000000L, true)]
        [TestCase(864000000001L, false)]
        public void PollIntervalUsesExclusiveZeroAndInclusiveOneDayBounds(long ticks, bool valid)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Inspect,
                HttpRoot = new Uri("https://registry.example/"),
                PollInterval = TimeSpan.FromTicks(ticks)
            };

            if (valid)
            {
                Assert.That(settings.Validate, Throws.Nothing);
            }
            else
            {
                Assert.That(settings.Validate,
                    Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("PollInterval"));
            }
        }

        [Test]
        public void ProfileAndJobNamesRejectTraversalWhitespaceAndNonAsciiCharacters(
            [Values("CredentialProfile", "JobId")] string field,
            [Values(null, "", ".", "..", " ", "a/b", "a\\b", "a b", "caf\u00e9")] string? invalidName)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Inspect,
                HttpRoot = new Uri("https://registry.example/"),
                CredentialProfile = field == "CredentialProfile" ? invalidName! : "default",
                JobId = field == "JobId" ? invalidName! : "xregistry"
            };

            Assert.That(settings.Validate, Throws.ArgumentException.With.Property("ParamName").EqualTo(field));
        }

        [TestCase("a")]
        [TestCase("Profile_12-a.b")]
        [TestCase(".operator")]
        public void ProfileAndJobNamesAcceptDocumentedAsciiAlphabet(string name)
        {
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Inspect,
                HttpRoot = new Uri("https://registry.example/"),
                CredentialProfile = name,
                JobId = name
            };

            Assert.That(settings.Validate, Throws.Nothing);
        }

        [TestCase("ConfigurationFile")]
        [TestCase("ModelFile")]
        public void MissingConfigurationOrModelFileIsReportedExplicitly(string field)
        {
            string missing = Path.Combine(
                Path.GetTempPath(), "xregistry-missing-" + Guid.NewGuid().ToString("N"), "x.json");
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Inspect,
                HttpRoot = new Uri("https://registry.example/"),
                ConfigurationFile = field == "ConfigurationFile" ? missing : null,
                ModelFile = field == "ModelFile" ? missing : null
            };

            Assert.That(settings.Validate,
                Throws.TypeOf<FileNotFoundException>().With.Property("FileName").EqualTo(missing));
        }

        [Test]
        public async Task ExistingConfigurationAndModelFilesAreNotModifiedDuringValidationAsync()
        {
            string directory = Path.Combine(Path.GetTempPath(), "xregistry-settings-" + Guid.NewGuid().ToString("N"));
            string configuration = Path.Combine(directory, "configuration file.json");
            string model = Path.Combine(directory, "model file.json");
            string state = Path.Combine(directory, "state");
            _ = Directory.CreateDirectory(directory);
            try
            {
                await File.WriteAllTextAsync(configuration, """{"profiles":{}}""").ConfigureAwait(false);
                await File.WriteAllTextAsync(model, """{"groups":{}}""").ConfigureAwait(false);
                var settings = new XRegistryConnectorSettings
                {
                    Command = XRegistryConnectorCommand.Conflicts,
                    StateDirectory = state,
                    ConfigurationFile = configuration,
                    ModelFile = model
                };

                Assert.That(settings.Validate, Throws.Nothing);
                Assert.That(await File.ReadAllTextAsync(configuration).ConfigureAwait(false),
                    Is.EqualTo("""{"profiles":{}}"""));
                Assert.That(await File.ReadAllTextAsync(model).ConfigureAwait(false), Is.EqualTo("""{"groups":{}}"""));
                Assert.That(Directory.Exists(state), Is.False);
            }
            finally
            {
                File.Delete(configuration);
                File.Delete(model);
                Directory.Delete(directory);
            }
        }
    }
}
#endif
