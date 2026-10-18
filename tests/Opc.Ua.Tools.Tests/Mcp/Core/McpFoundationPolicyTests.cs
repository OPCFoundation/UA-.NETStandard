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

#if NET10_0
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    [TestFixture]
    public sealed class McpFoundationPolicyTests
    {
        [Test]
        public void CapabilityValuesMatchTheTwelveDefinedContracts()
        {
            string[] expectedNames =
            [
                "DirectoryAdministration", "CertificateAdministration", "CredentialIssuance",
                "Onboarding", "IdentityAdministration", "PubSubRuntime", "PubSubConfiguration",
                "PubSubBridge", "PubSubSecurity", "WotRuntime", "WotWrite", "WotInvoke"
            ];
            int[] expectedValues = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

            Assert.That(Enum.GetNames<McpCapability>(), Is.EqualTo(expectedNames));
            Assert.That(
                Array.ConvertAll(Enum.GetValues<McpCapability>(), capability => (int)capability),
                Is.EqualTo(expectedValues));
            Assert.That(default(McpCapability), Is.EqualTo(McpCapability.DirectoryAdministration));
        }

        [Test]
        public void OptionsDefaultsAreValidAndDenyCapabilities()
        {
            var options = new McpExecutionOptions();
            var callers = new McpCallerContext();

            Assert.That(options.Validate, Throws.Nothing);
            Assert.That(options.EnabledCapabilities.IsNull, Is.False);
            Assert.That(options.EnabledCapabilities.Count, Is.Zero);
            Assert.That(options.CallerGrants.IsNull, Is.False);
            Assert.That(options.CallerGrants.Count, Is.Zero);
            Assert.That(options.LocalCallerId, Is.Null);
            Assert.That(options.MaxRuntimes, Is.EqualTo(8));
            Assert.That(options.MaxRuntimesPerCaller, Is.EqualTo(2));
            Assert.That(options.MaxConcurrentOperationsPerRuntime, Is.EqualTo(2));
            Assert.That(options.RuntimeIdleTimeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(options.RuntimeLeaseTimeout, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(options.OperationTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));

            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable scope = callers.EnterTrustedCaller(new McpCallerIdentity("operator", "urn:issuer-a"));

            Assert.That(policy.RequireCaller(), Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            Assert.That(callers.HasRequestScope, Is.True);
            Assert.That(policy.IsEnabled(McpCapability.DirectoryAdministration), Is.False);
            Assert.That(
                () => policy.Require(McpCapability.DirectoryAdministration),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(
                () => policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-A"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(
                () => policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyA"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
        }

        [TestCase(McpCapability.DirectoryAdministration)]
        [TestCase(McpCapability.CertificateAdministration)]
        [TestCase(McpCapability.CredentialIssuance)]
        [TestCase(McpCapability.Onboarding)]
        [TestCase(McpCapability.IdentityAdministration)]
        [TestCase(McpCapability.PubSubRuntime)]
        [TestCase(McpCapability.PubSubConfiguration)]
        [TestCase(McpCapability.PubSubBridge)]
        [TestCase(McpCapability.PubSubSecurity)]
        [TestCase(McpCapability.WotRuntime)]
        [TestCase(McpCapability.WotWrite)]
        [TestCase(McpCapability.WotInvoke)]
        public void ConfigurationAcceptsEveryDefinedCapability(McpCapability capability)
        {
            var options = new McpExecutionOptions { EnabledCapabilities = [capability] };
            var grant = new McpCallerGrant
            {
                Caller = new McpCallerIdentity("operator", "urn:issuer-a"),
                Capabilities = [capability]
            };

            Assert.That(options.Validate, Throws.Nothing);
            Assert.That(grant.Validate, Throws.Nothing);
            Assert.That(options.Snapshot().EnabledCapabilities.ToArray(), Is.EqualTo([capability]));
            Assert.That(grant.Snapshot().Capabilities.ToArray(), Is.EqualTo([capability]));
        }

        [TestCase(nameof(McpExecutionOptions.EnabledCapabilities), -1)]
        [TestCase(nameof(McpExecutionOptions.EnabledCapabilities), 12)]
        [TestCase(nameof(McpExecutionOptions.EnabledCapabilities), int.MaxValue)]
        [TestCase(nameof(McpCallerGrant.Capabilities), -1)]
        [TestCase(nameof(McpCallerGrant.Capabilities), 12)]
        [TestCase(nameof(McpCallerGrant.Capabilities), int.MaxValue)]
        public void ConfigurationRejectsUndefinedCapability(string setting, int value)
        {
            var options = new McpExecutionOptions();
            var grant = new McpCallerGrant { Caller = new McpCallerIdentity("operator", "urn:issuer-a") };

            if (setting == nameof(McpExecutionOptions.EnabledCapabilities))
            {
                options.EnabledCapabilities = [McpCapability.DirectoryAdministration, (McpCapability)value];
                Assert.That(
                    options.Validate,
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Property(nameof(ArgumentException.ParamName)).EqualTo(setting));
                Assert.That(options.CallerGrants.Count, Is.Zero);
            }
            else
            {
                grant.Capabilities = [McpCapability.DirectoryAdministration, (McpCapability)value];
                options.CallerGrants = [grant];
                Assert.That(
                    grant.Validate,
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Property(nameof(ArgumentException.ParamName)).EqualTo(setting));
                Assert.That(
                    options.Validate,
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Property(nameof(ArgumentException.ParamName)).EqualTo(setting));
            }
        }

        [TestCase(nameof(McpExecutionOptions.EnabledCapabilities), true)]
        [TestCase(nameof(McpExecutionOptions.EnabledCapabilities), false)]
        [TestCase(nameof(McpExecutionOptions.CallerGrants), true)]
        [TestCase(nameof(McpExecutionOptions.CallerGrants), false)]
        [TestCase(nameof(McpCallerGrant.Capabilities), true)]
        [TestCase(nameof(McpCallerGrant.Capabilities), false)]
        [TestCase(nameof(McpCallerGrant.SessionNames), true)]
        [TestCase(nameof(McpCallerGrant.SessionNames), false)]
        [TestCase(nameof(McpCallerGrant.SecretAliases), true)]
        [TestCase(nameof(McpCallerGrant.SecretAliases), false)]
        public void ValidationDistinguishesDefaultAndEmptyCollections(string setting, bool useDefault)
        {
            var grant = new McpCallerGrant { Caller = new McpCallerIdentity("operator", "urn:issuer-a") };
            var options = new McpExecutionOptions { CallerGrants = [grant] };
            switch (setting)
            {
                case nameof(McpExecutionOptions.EnabledCapabilities):
                    options.EnabledCapabilities = useDefault ? default : [];
                    break;
                case nameof(McpExecutionOptions.CallerGrants):
                    options.CallerGrants = useDefault ? default : [];
                    break;
                case nameof(McpCallerGrant.Capabilities):
                    grant.Capabilities = useDefault ? default : [];
                    break;
                case nameof(McpCallerGrant.SessionNames):
                    grant.SessionNames = useDefault ? default : [];
                    break;
                case nameof(McpCallerGrant.SecretAliases):
                    grant.SecretAliases = useDefault ? default : [];
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }

            bool grantCollection = setting is nameof(McpCallerGrant.Capabilities)
                or nameof(McpCallerGrant.SessionNames) or nameof(McpCallerGrant.SecretAliases);
            if (useDefault)
            {
                Assert.That(options.Validate, Throws.InstanceOf<ArgumentException>());
                if (grantCollection)
                {
                    Assert.That(grant.Validate, Throws.InstanceOf<ArgumentException>());
                }
                Assert.That(grant.Caller, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            }
            else
            {
                Assert.That(options.Validate, Throws.Nothing);
                Assert.That(grant.Validate, Throws.Nothing);
                McpExecutionOptions snapshot = options.Snapshot();
                Assert.That(snapshot.EnabledCapabilities.IsNull, Is.False);
                Assert.That(snapshot.EnabledCapabilities.Count, Is.Zero);
                Assert.That(snapshot.CallerGrants.IsNull, Is.False);
                Assert.That(
                    snapshot.CallerGrants.Count,
                    Is.EqualTo(setting == nameof(McpExecutionOptions.CallerGrants) ? 0 : 1));
                Assert.That(grant.Snapshot().Capabilities.IsNull, Is.False);
                Assert.That(grant.Snapshot().SessionNames.IsNull, Is.False);
                Assert.That(grant.Snapshot().SecretAliases.IsNull, Is.False);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void OptionsRejectNullAndDuplicateGrantEntries(bool nullEntry)
        {
            var first = new McpCallerGrant { Caller = new McpCallerIdentity("operator", "urn:issuer-a") };
            var duplicate = new McpCallerGrant { Caller = new McpCallerIdentity("operator", "urn:issuer-a") };
            var options = new McpExecutionOptions
            {
                CallerGrants = nullEntry ? [first, null!] : [first, duplicate]
            };

            if (nullEntry)
            {
                Assert.That(options.Validate, Throws.TypeOf<ArgumentNullException>());
            }
            else
            {
                Assert.That(
                    options.Validate,
                    Throws.TypeOf<ArgumentException>()
                        .With.Property(nameof(ArgumentException.ParamName))
                        .EqualTo(nameof(McpExecutionOptions.CallerGrants)));
            }
            Assert.That(options.CallerGrants.Count, Is.EqualTo(2));
            Assert.That(first.Validate, Throws.Nothing);
            Assert.That(duplicate, Is.Not.SameAs(first));
        }

        [TestCase("urn:issuer-b", false)]
        [TestCase("urn:issuer-a", true)]
        [TestCase("urn:Issuer-a", false)]
        public void OptionsAcceptDistinctAuthorityGrantRecords(string issuer, bool isLocal)
        {
            var first = new McpCallerIdentity("operator", "urn:issuer-a");
            var second = new McpCallerIdentity("operator", issuer, isLocal);
            var options = new McpExecutionOptions
            {
                CallerGrants =
                [
                    new McpCallerGrant { Caller = first },
                    new McpCallerGrant { Caller = second }
                ]
            };

            Assert.That(options.Validate, Throws.Nothing);
            McpExecutionOptions snapshot = options.Snapshot();
            Assert.That(snapshot.CallerGrants.Count, Is.EqualTo(2));
            Assert.That(snapshot.CallerGrants[0].Caller, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            Assert.That(
                snapshot.CallerGrants[1].Caller,
                Is.EqualTo(new McpCallerIdentity("operator", issuer, isLocal)));
            Assert.That(snapshot.CallerGrants[1].Caller, Is.Not.EqualTo(snapshot.CallerGrants[0].Caller));
        }

        [TestCaseSource(nameof(InvalidGrantCallers))]
        public void GrantRejectsMissingOrMalformedCaller(McpCallerIdentity? caller, Type exceptionType)
        {
            var grant = new McpCallerGrant { Caller = caller! };

            Assert.That(grant.Validate, Throws.TypeOf(exceptionType));

            grant.Caller = new McpCallerIdentity("operator", "urn:issuer-a");
            Assert.That(grant.Validate, Throws.Nothing);
            Assert.That(grant.Capabilities.IsNull, Is.False);
            Assert.That(grant.Capabilities.Count, Is.Zero);
        }

        [TestCase(nameof(McpCallerGrant.SessionNames), null)]
        [TestCase(nameof(McpCallerGrant.SessionNames), "")]
        [TestCase(nameof(McpCallerGrant.SessionNames), " \t ")]
        [TestCase(nameof(McpCallerGrant.SecretAliases), null)]
        [TestCase(nameof(McpCallerGrant.SecretAliases), "")]
        [TestCase(nameof(McpCallerGrant.SecretAliases), " \t ")]
        public void GrantRejectsBlankSessionNamesAndSecretAliases(string setting, string? invalid)
        {
            var grant = new McpCallerGrant
            {
                Caller = new McpCallerIdentity("operator", "urn:issuer-a"),
                Capabilities = [McpCapability.DirectoryAdministration],
                SessionNames = setting == nameof(McpCallerGrant.SessionNames) ? ["Prod-A", invalid!] : ["Prod-A"],
                SecretAliases = setting == nameof(McpCallerGrant.SecretAliases)
                    ? ["Vault/KeyA", invalid!] : ["Vault/KeyA"]
            };

            Assert.That(
                grant.Validate,
                Throws.TypeOf(invalid is null ? typeof(ArgumentNullException) : typeof(ArgumentException)));
            Assert.That(grant.Capabilities[0], Is.EqualTo(McpCapability.DirectoryAdministration));
            grant.SessionNames = ["Prod-A"];
            grant.SecretAliases = ["Vault/KeyA"];
            Assert.That(grant.Validate, Throws.Nothing);
        }

        [TestCase(nameof(McpCallerGrant.SessionNames))]
        [TestCase(nameof(McpCallerGrant.SecretAliases))]
        public void GrantAcceptsDuplicateAndLiteralAsteriskNames(string setting)
        {
            var grant = new McpCallerGrant
            {
                Caller = new McpCallerIdentity("operator", "urn:issuer-a"),
                SessionNames = setting == nameof(McpCallerGrant.SessionNames) ? ["Prod-A", "Prod-A", "*"] : [],
                SecretAliases = setting == nameof(McpCallerGrant.SecretAliases) ? ["Vault/KeyA", "Vault/KeyA", "*"] : []
            };

            Assert.That(grant.Validate, Throws.Nothing);
            McpCallerGrant snapshot = grant.Snapshot();
            string[] expectedSessions = setting == nameof(McpCallerGrant.SessionNames) ? ["Prod-A", "Prod-A", "*"] : [];
            string[] expectedAliases = setting == nameof(McpCallerGrant.SecretAliases)
                ? ["Vault/KeyA", "Vault/KeyA", "*"] : [];
            Assert.That(snapshot.SessionNames.ToArray(), Is.EqualTo(expectedSessions));
            Assert.That(snapshot.SecretAliases.ToArray(), Is.EqualTo(expectedAliases));
            Assert.That(snapshot.Caller, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
        }

        [TestCaseSource(nameof(LocalCallerIds))]
        public void OptionsValidateOptionalLocalCallerId(string? localId, bool valid)
        {
            var options = new McpExecutionOptions { LocalCallerId = localId };
            var callers = new McpCallerContext();
            if (!valid)
            {
                Assert.That(
                    options.Validate,
                    Throws.TypeOf<ArgumentException>()
                        .With.Property(nameof(ArgumentException.ParamName))
                        .EqualTo(nameof(McpExecutionOptions.LocalCallerId)));
                Assert.That(options.CallerGrants.Count, Is.Zero);
                return;
            }

            Assert.That(options.Validate, Throws.Nothing);
            Assert.That(options.Snapshot().LocalCallerId, Is.EqualTo(localId));
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            if (localId is null)
            {
                Assert.That(
                    () => policy.RequireCaller(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            else
            {
                Assert.That(
                    policy.RequireCaller(),
                    Is.EqualTo(new McpCallerIdentity(localId, "urn:opcfoundation:mcp:local-host", true)));
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            Assert.That(
                () => policy.Require(McpCapability.DirectoryAdministration),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
        }

        [TestCase(4097)]
        [TestCase(4098)]
        public void OptionsRejectOverlongLocalCallerId(int length)
        {
            var options = new McpExecutionOptions { LocalCallerId = new string('h', length) };

            Assert.That(
                options.Validate,
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentException.ParamName))
                    .EqualTo(nameof(McpExecutionOptions.LocalCallerId)));
            Assert.That(
                () => options.Snapshot(),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentException.ParamName))
                    .EqualTo(nameof(McpExecutionOptions.LocalCallerId)));
        }

        [TestCase(nameof(McpExecutionOptions.MaxRuntimes), 1)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimes), 2)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimes), 8)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimes), 1023)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimes), 1024)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimesPerCaller), 1)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimesPerCaller), 2)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimesPerCaller), 3)]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime), 1)]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime), 2)]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime), 8)]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime), 31)]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime), 32)]
        public void OptionsAcceptIntegerBoundaries(string setting, int value)
        {
            var options = new McpExecutionOptions { MaxRuntimes = 3, MaxRuntimesPerCaller = 1 };
            switch (setting)
            {
                case nameof(McpExecutionOptions.MaxRuntimes):
                    options.MaxRuntimes = value;
                    break;
                case nameof(McpExecutionOptions.MaxRuntimesPerCaller):
                    options.MaxRuntimesPerCaller = value;
                    break;
                case nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime):
                    options.MaxConcurrentOperationsPerRuntime = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }

            Assert.That(options.Validate, Throws.Nothing);
            McpExecutionOptions snapshot = options.Snapshot();
            Assert.That(snapshot.MaxRuntimes, Is.EqualTo(setting == nameof(options.MaxRuntimes) ? value : 3));
            Assert.That(
                snapshot.MaxRuntimesPerCaller,
                Is.EqualTo(setting == nameof(options.MaxRuntimesPerCaller) ? value : 1));
            Assert.That(
                snapshot.MaxConcurrentOperationsPerRuntime,
                Is.EqualTo(setting == nameof(options.MaxConcurrentOperationsPerRuntime) ? value : 2));
        }

        [Test]
        public void OptionsAcceptMatchingMinimumRuntimeQuotas()
        {
            var options = new McpExecutionOptions { MaxRuntimes = 1, MaxRuntimesPerCaller = 1 };

            Assert.That(options.Validate, Throws.Nothing);
            McpExecutionOptions snapshot = options.Snapshot();
            Assert.That(snapshot.MaxRuntimes, Is.EqualTo(1));
            Assert.That(snapshot.MaxRuntimesPerCaller, Is.EqualTo(1));
            Assert.That(snapshot.MaxConcurrentOperationsPerRuntime, Is.EqualTo(2));
        }

        [TestCase(nameof(McpExecutionOptions.MaxRuntimes), -1)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimes), 0)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimes), 1025)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimes), 1026)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimesPerCaller), -1)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimesPerCaller), 0)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimesPerCaller), 4)]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimesPerCaller), 5)]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime), -1)]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime), 0)]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime), 33)]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime), 34)]
        public void OptionsRejectIntegerBoundaries(string setting, int value)
        {
            var options = new McpExecutionOptions { MaxRuntimes = 3, MaxRuntimesPerCaller = 1 };
            switch (setting)
            {
                case nameof(McpExecutionOptions.MaxRuntimes):
                    options.MaxRuntimes = value;
                    break;
                case nameof(McpExecutionOptions.MaxRuntimesPerCaller):
                    options.MaxRuntimesPerCaller = value;
                    break;
                case nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime):
                    options.MaxConcurrentOperationsPerRuntime = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }

            Assert.That(
                options.Validate,
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentException.ParamName)).EqualTo(setting)
                    .And.Property(nameof(ArgumentOutOfRangeException.ActualValue)).EqualTo(value));
            Assert.That(
                () => options.Snapshot(),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentException.ParamName)).EqualTo(setting));
        }

        [TestCase(nameof(McpExecutionOptions.RuntimeIdleTimeout), 1L)]
        [TestCase(nameof(McpExecutionOptions.RuntimeIdleTimeout), 2L)]
        [TestCase(nameof(McpExecutionOptions.RuntimeIdleTimeout), (30 * TimeSpan.TicksPerMinute) - 1)]
        [TestCase(nameof(McpExecutionOptions.RuntimeIdleTimeout), 30 * TimeSpan.TicksPerMinute)]
        [TestCase(nameof(McpExecutionOptions.RuntimeLeaseTimeout), 1L)]
        [TestCase(nameof(McpExecutionOptions.RuntimeLeaseTimeout), 2L)]
        [TestCase(nameof(McpExecutionOptions.RuntimeLeaseTimeout), TimeSpan.TicksPerDay - 1)]
        [TestCase(nameof(McpExecutionOptions.RuntimeLeaseTimeout), TimeSpan.TicksPerDay)]
        [TestCase(nameof(McpExecutionOptions.OperationTimeout), 1L)]
        [TestCase(nameof(McpExecutionOptions.OperationTimeout), 2L)]
        [TestCase(nameof(McpExecutionOptions.OperationTimeout), (2 * TimeSpan.TicksPerMinute) - 1)]
        [TestCase(nameof(McpExecutionOptions.OperationTimeout), 2 * TimeSpan.TicksPerMinute)]
        public void OptionsAcceptTimeoutBoundaries(string setting, long ticks)
        {
            var options = new McpExecutionOptions { RuntimeIdleTimeout = TimeSpan.FromTicks(1) };
            var value = TimeSpan.FromTicks(ticks);
            switch (setting)
            {
                case nameof(McpExecutionOptions.RuntimeIdleTimeout):
                    options.RuntimeIdleTimeout = value;
                    break;
                case nameof(McpExecutionOptions.RuntimeLeaseTimeout):
                    options.RuntimeLeaseTimeout = value;
                    break;
                case nameof(McpExecutionOptions.OperationTimeout):
                    options.OperationTimeout = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }

            Assert.That(options.Validate, Throws.Nothing);
            McpExecutionOptions snapshot = options.Snapshot();
            Assert.That(
                snapshot.RuntimeIdleTimeout,
                Is.EqualTo(setting == nameof(options.RuntimeIdleTimeout) ? value : TimeSpan.FromTicks(1)));
            Assert.That(
                snapshot.RuntimeLeaseTimeout,
                Is.EqualTo(setting == nameof(options.RuntimeLeaseTimeout) ? value : TimeSpan.FromMinutes(30)));
            Assert.That(
                snapshot.OperationTimeout,
                Is.EqualTo(setting == nameof(options.OperationTimeout) ? value : TimeSpan.FromSeconds(30)));
        }

        [TestCase(nameof(McpExecutionOptions.RuntimeIdleTimeout), -1L)]
        [TestCase(nameof(McpExecutionOptions.RuntimeIdleTimeout), 0L)]
        [TestCase(nameof(McpExecutionOptions.RuntimeIdleTimeout), (30 * TimeSpan.TicksPerMinute) + 1)]
        [TestCase(nameof(McpExecutionOptions.RuntimeIdleTimeout), (30 * TimeSpan.TicksPerMinute) + 2)]
        [TestCase(nameof(McpExecutionOptions.RuntimeLeaseTimeout), -1L)]
        [TestCase(nameof(McpExecutionOptions.RuntimeLeaseTimeout), 0L)]
        [TestCase(nameof(McpExecutionOptions.RuntimeLeaseTimeout), TimeSpan.TicksPerDay + 1)]
        [TestCase(nameof(McpExecutionOptions.RuntimeLeaseTimeout), TimeSpan.TicksPerDay + 2)]
        [TestCase(nameof(McpExecutionOptions.OperationTimeout), -1L)]
        [TestCase(nameof(McpExecutionOptions.OperationTimeout), 0L)]
        [TestCase(nameof(McpExecutionOptions.OperationTimeout), (2 * TimeSpan.TicksPerMinute) + 1)]
        [TestCase(nameof(McpExecutionOptions.OperationTimeout), (2 * TimeSpan.TicksPerMinute) + 2)]
        public void OptionsRejectTimeoutBoundaries(string setting, long ticks)
        {
            var options = new McpExecutionOptions { RuntimeIdleTimeout = TimeSpan.FromTicks(1) };
            var value = TimeSpan.FromTicks(ticks);
            switch (setting)
            {
                case nameof(McpExecutionOptions.RuntimeIdleTimeout):
                    options.RuntimeIdleTimeout = value;
                    break;
                case nameof(McpExecutionOptions.RuntimeLeaseTimeout):
                    options.RuntimeLeaseTimeout = value;
                    break;
                case nameof(McpExecutionOptions.OperationTimeout):
                    options.OperationTimeout = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }

            string expectedParameter = setting == nameof(McpExecutionOptions.OperationTimeout)
                ? nameof(McpExecutionOptions.OperationTimeout) : nameof(McpExecutionOptions.RuntimeLeaseTimeout);
            Assert.That(
                options.Validate,
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentException.ParamName)).EqualTo(expectedParameter));
            Assert.That(
                () => options.Snapshot(),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentException.ParamName)).EqualTo(expectedParameter));
        }

        [TestCase(nameof(McpExecutionOptions.LocalCallerId))]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimes))]
        [TestCase(nameof(McpExecutionOptions.MaxRuntimesPerCaller))]
        [TestCase(nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime))]
        [TestCase(nameof(McpExecutionOptions.RuntimeIdleTimeout))]
        [TestCase(nameof(McpExecutionOptions.RuntimeLeaseTimeout))]
        [TestCase(nameof(McpExecutionOptions.OperationTimeout))]
        public void OptionsSnapshotCopiesEveryConfigurationValue(string setting)
        {
            var options = new McpExecutionOptions
            {
                LocalCallerId = "host-A",
                MaxRuntimes = 12,
                MaxRuntimesPerCaller = 3,
                MaxConcurrentOperationsPerRuntime = 4,
                RuntimeIdleTimeout = TimeSpan.FromMinutes(7),
                RuntimeLeaseTimeout = TimeSpan.FromMinutes(47),
                OperationTimeout = TimeSpan.FromSeconds(42)
            };
            McpExecutionOptions snapshot = options.Snapshot();
            switch (setting)
            {
                case nameof(McpExecutionOptions.LocalCallerId):
                    options.LocalCallerId = "host-B";
                    break;
                case nameof(McpExecutionOptions.MaxRuntimes):
                    options.MaxRuntimes = 20;
                    break;
                case nameof(McpExecutionOptions.MaxRuntimesPerCaller):
                    options.MaxRuntimesPerCaller = 6;
                    break;
                case nameof(McpExecutionOptions.MaxConcurrentOperationsPerRuntime):
                    options.MaxConcurrentOperationsPerRuntime = 9;
                    break;
                case nameof(McpExecutionOptions.RuntimeIdleTimeout):
                    options.RuntimeIdleTimeout = TimeSpan.FromMinutes(13);
                    break;
                case nameof(McpExecutionOptions.RuntimeLeaseTimeout):
                    options.RuntimeLeaseTimeout = TimeSpan.FromMinutes(71);
                    break;
                case nameof(McpExecutionOptions.OperationTimeout):
                    options.OperationTimeout = TimeSpan.FromSeconds(73);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }

            Assert.That(snapshot, Is.Not.SameAs(options));
            Assert.That(snapshot.LocalCallerId, Is.EqualTo("host-A"));
            Assert.That(snapshot.MaxRuntimes, Is.EqualTo(12));
            Assert.That(snapshot.MaxRuntimesPerCaller, Is.EqualTo(3));
            Assert.That(snapshot.MaxConcurrentOperationsPerRuntime, Is.EqualTo(4));
            Assert.That(snapshot.RuntimeIdleTimeout, Is.EqualTo(TimeSpan.FromMinutes(7)));
            Assert.That(snapshot.RuntimeLeaseTimeout, Is.EqualTo(TimeSpan.FromMinutes(47)));
            Assert.That(snapshot.OperationTimeout, Is.EqualTo(TimeSpan.FromSeconds(42)));
            Assert.That(snapshot.Validate, Throws.Nothing);
        }

        [TestCase("Caller")]
        [TestCase("CapabilitiesProperty")]
        [TestCase("CapabilitiesStorage")]
        [TestCase("SessionNamesProperty")]
        [TestCase("SessionNamesStorage")]
        [TestCase("SecretAliasesProperty")]
        [TestCase("SecretAliasesStorage")]
        public void GrantSnapshotPreservesCallerAndBackingCollections(string mutation)
        {
            McpCapability[] capabilities =
                [McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration];
            string[] sessions = ["Prod-A", "Prod-C"];
            string[] aliases = ["Vault/KeyA", "Vault/KeyC"];
            var grant = new McpCallerGrant
            {
                Caller = new McpCallerIdentity("operator", "urn:issuer-a"),
                Capabilities = ArrayOf.Wrapped(capabilities),
                SessionNames = ArrayOf.Wrapped(sessions),
                SecretAliases = ArrayOf.Wrapped(aliases)
            };

            McpCallerGrant snapshot = grant.Snapshot();
            switch (mutation)
            {
                case "Caller":
                    grant.Caller = new McpCallerIdentity("other", "urn:issuer-b", true);
                    break;
                case "CapabilitiesProperty":
                    grant.Capabilities = [McpCapability.WotWrite];
                    break;
                case "CapabilitiesStorage":
                    capabilities[0] = McpCapability.WotWrite;
                    Assert.That(grant.Capabilities[0], Is.EqualTo(McpCapability.WotWrite));
                    break;
                case "SessionNamesProperty":
                    grant.SessionNames = ["Dev-B"];
                    break;
                case "SessionNamesStorage":
                    sessions[0] = "Dev-B";
                    Assert.That(grant.SessionNames[0], Is.EqualTo("Dev-B"));
                    break;
                case "SecretAliasesProperty":
                    grant.SecretAliases = ["Vault/KeyB"];
                    break;
                case "SecretAliasesStorage":
                    aliases[0] = "Vault/KeyB";
                    Assert.That(grant.SecretAliases[0], Is.EqualTo("Vault/KeyB"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }

            Assert.That(snapshot, Is.Not.SameAs(grant));
            Assert.That(snapshot.Caller, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            Assert.That(
                snapshot.Capabilities.ToArray(),
                Is.EqualTo([McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration]));
            Assert.That(snapshot.SessionNames.ToArray(), Is.EqualTo(s_originalSessionNames));
            Assert.That(snapshot.SecretAliases.ToArray(), Is.EqualTo(s_originalSecretAliases));
        }

        [TestCase("HostCapabilitiesProperty")]
        [TestCase("HostCapabilitiesStorage")]
        [TestCase("CallerGrantsProperty")]
        [TestCase("CallerGrantsStorage")]
        [TestCase("Caller")]
        [TestCase("CapabilitiesProperty")]
        [TestCase("CapabilitiesStorage")]
        [TestCase("SessionNamesProperty")]
        [TestCase("SessionNamesStorage")]
        [TestCase("SecretAliasesProperty")]
        [TestCase("SecretAliasesStorage")]
        public void OptionsSnapshotPreservesNestedAuthorityCollections(string mutation)
        {
            McpCapability[] enabled = [McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration];
            McpCapability[] capabilities =
                [McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration];
            string[] sessions = ["Prod-A", "Prod-C"];
            string[] aliases = ["Vault/KeyA", "Vault/KeyC"];
            var grant = new McpCallerGrant
            {
                Caller = new McpCallerIdentity("operator", "urn:issuer-a"),
                Capabilities = ArrayOf.Wrapped(capabilities),
                SessionNames = ArrayOf.Wrapped(sessions),
                SecretAliases = ArrayOf.Wrapped(aliases)
            };
            var replacement = new McpCallerGrant { Caller = new McpCallerIdentity("other", "urn:issuer-b", true) };
            McpCallerGrant[] grants =
            [
                grant,
                new McpCallerGrant { Caller = new McpCallerIdentity("auditor", "urn:issuer-a") }
            ];
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = ArrayOf.Wrapped(enabled),
                CallerGrants = ArrayOf.Wrapped(grants)
            };

            McpExecutionOptions snapshot = options.Snapshot();
            switch (mutation)
            {
                case "HostCapabilitiesProperty":
                    options.EnabledCapabilities = [McpCapability.WotWrite];
                    break;
                case "HostCapabilitiesStorage":
                    enabled[0] = McpCapability.WotWrite;
                    Assert.That(options.EnabledCapabilities[0], Is.EqualTo(McpCapability.WotWrite));
                    break;
                case "CallerGrantsProperty":
                    options.CallerGrants = [replacement];
                    break;
                case "CallerGrantsStorage":
                    grants[0] = replacement;
                    Assert.That(options.CallerGrants[0], Is.SameAs(replacement));
                    break;
                case "Caller":
                    grant.Caller = new McpCallerIdentity("other", "urn:issuer-b", true);
                    break;
                case "CapabilitiesProperty":
                    grant.Capabilities = [McpCapability.WotWrite];
                    break;
                case "CapabilitiesStorage":
                    capabilities[0] = McpCapability.WotWrite;
                    Assert.That(grant.Capabilities[0], Is.EqualTo(McpCapability.WotWrite));
                    break;
                case "SessionNamesProperty":
                    grant.SessionNames = ["Dev-B"];
                    break;
                case "SessionNamesStorage":
                    sessions[0] = "Dev-B";
                    Assert.That(grant.SessionNames[0], Is.EqualTo("Dev-B"));
                    break;
                case "SecretAliasesProperty":
                    grant.SecretAliases = ["Vault/KeyB"];
                    break;
                case "SecretAliasesStorage":
                    aliases[0] = "Vault/KeyB";
                    Assert.That(grant.SecretAliases[0], Is.EqualTo("Vault/KeyB"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }

            Assert.That(
                snapshot.EnabledCapabilities.ToArray(),
                Is.EqualTo([McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration]));
            Assert.That(snapshot.CallerGrants.Count, Is.EqualTo(2));
            Assert.That(snapshot.CallerGrants[0], Is.Not.SameAs(grant));
            Assert.That(
                snapshot.CallerGrants[0].Caller,
                Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            Assert.That(
                snapshot.CallerGrants[1].Caller,
                Is.EqualTo(new McpCallerIdentity("auditor", "urn:issuer-a")));
            Assert.That(
                snapshot.CallerGrants[0].Capabilities.ToArray(),
                Is.EqualTo([McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration]));
            Assert.That(snapshot.CallerGrants[0].SessionNames.ToArray(), Is.EqualTo(s_originalSessionNames));
            Assert.That(
                snapshot.CallerGrants[0].SecretAliases.ToArray(),
                Is.EqualTo(s_originalSecretAliases));
        }

        [TestCase("options")]
        [TestCase("callers")]
        [TestCase("telemetry")]
        public void PolicyConstructorRejectsNullDependencies(string parameter)
        {
            var callers = new McpCallerContext();
            McpExecutionOptions? options = parameter == "options" ? null : new McpExecutionOptions();
            McpCallerContext? selectedCallers = parameter == "callers" ? null : callers;
            ITelemetryContext? telemetry = parameter == "telemetry" ? null : CreateTelemetry();

            Assert.That(
                () => new McpExecutionPolicy(options!, selectedCallers!, telemetry!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentException.ParamName)).EqualTo(parameter));
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
        }

        [TestCase(4097)]
        [TestCase(4098)]
        public void PolicyConstructionRejectsOverlongLocalCallerId(int length)
        {
            var options = new McpExecutionOptions { LocalCallerId = new string('h', length) };
            var callers = new McpCallerContext();

            Assert.That(
                () => new McpExecutionPolicy(options, callers, CreateTelemetry()),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentException.ParamName))
                    .EqualTo(nameof(McpExecutionOptions.LocalCallerId)));
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        [TestCase(6, false)]
        [TestCase(7, false)]
        [TestCase(8, false)]
        [TestCase(9, false)]
        [TestCase(10, false)]
        [TestCase(11, true)]
        [TestCase(-1, false)]
        [TestCase(12, false)]
        [TestCase(int.MaxValue, false)]
        public void IsEnabledReflectsOnlyDefinedHostCapabilities(int value, bool enabled)
        {
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.DirectoryAdministration, McpCapability.WotInvoke]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());

            Assert.That(policy.IsEnabled((McpCapability)value), Is.EqualTo(enabled));
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            Assert.That(
                () => policy.Require((McpCapability)value),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
        }

        [TestCase(false, true, true, true, false)]
        [TestCase(true, false, true, true, false)]
        [TestCase(true, true, false, true, false)]
        [TestCase(true, true, true, false, false)]
        [TestCase(true, true, true, true, true)]
        public void RequireNeedsBothAuthorityLayers(
            bool hostEnabled,
            bool hasGrant,
            bool matchingCapability,
            bool trustedCaller,
            bool allowed)
        {
            var caller = new McpCallerIdentity("operator", "urn:issuer-a");
            var callers = new McpCallerContext();
            var grant = new McpCallerGrant
            {
                Caller = caller,
                Capabilities = matchingCapability
                    ? [McpCapability.DirectoryAdministration] : [McpCapability.WotWrite],
                SessionNames = ["Prod-A"],
                SecretAliases = ["Vault/KeyA"]
            };
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = hostEnabled ? [McpCapability.DirectoryAdministration] : [],
                CallerGrants = hasGrant ? [grant] : []
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable? scope = trustedCaller ? callers.EnterTrustedCaller(caller) : null;

            Assert.That(policy.IsEnabled(McpCapability.DirectoryAdministration), Is.EqualTo(hostEnabled));
            Assert.That(callers.HasRequestScope, Is.EqualTo(trustedCaller));
            Assert.That(callers.Current, Is.EqualTo(trustedCaller ? caller : null));
            if (allowed)
            {
                Assert.That(
                    policy.Require(McpCapability.DirectoryAdministration),
                    Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
                Assert.That(policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-A"), Is.EqualTo(caller));
                Assert.That(
                    policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyA"),
                    Is.EqualTo(caller));
            }
            else
            {
                Assert.That(
                    () => policy.Require(McpCapability.DirectoryAdministration),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(
                    () => policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-A"),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(
                    () => policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyA"),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
        }

        [TestCase(McpCapability.DirectoryAdministration)]
        [TestCase(McpCapability.CertificateAdministration)]
        [TestCase(McpCapability.CredentialIssuance)]
        [TestCase(McpCapability.Onboarding)]
        [TestCase(McpCapability.IdentityAdministration)]
        [TestCase(McpCapability.PubSubRuntime)]
        [TestCase(McpCapability.PubSubConfiguration)]
        [TestCase(McpCapability.PubSubBridge)]
        [TestCase(McpCapability.PubSubSecurity)]
        [TestCase(McpCapability.WotRuntime)]
        [TestCase(McpCapability.WotWrite)]
        [TestCase(McpCapability.WotInvoke)]
        public void RequireAcceptsEveryExactlyGrantedCapability(McpCapability capability)
        {
            var caller = new McpCallerIdentity("operator", "urn:issuer-a");
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = [capability],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = caller,
                        Capabilities = [capability],
                        SessionNames = ["Prod-A"],
                        SecretAliases = ["Vault/KeyA"]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable scope = callers.EnterTrustedCaller(caller);

            Assert.That(policy.IsEnabled(capability), Is.True);
            Assert.That(policy.Require(capability), Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            Assert.That(policy.RequireSession(capability, "Prod-A"), Is.EqualTo(caller));
            Assert.That(policy.RequireSecret(capability, "Vault/KeyA"), Is.EqualTo(caller));
            Assert.That(policy.RequireCaller(), Is.EqualTo(caller));
            Assert.That(callers.Current, Is.EqualTo(caller));
            Assert.That(callers.HasRequestScope, Is.True);
        }

        [TestCase(-1)]
        [TestCase(12)]
        [TestCase(int.MaxValue)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(11)]
        public void RequireRejectsUndefinedOrDisabledCapability(int value)
        {
            var caller = new McpCallerIdentity("operator", "urn:issuer-a");
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.PubSubRuntime],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = caller,
                        Capabilities =
                        [
                            McpCapability.PubSubRuntime, McpCapability.DirectoryAdministration,
                            McpCapability.CertificateAdministration, McpCapability.WotInvoke
                        ]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable scope = callers.EnterTrustedCaller(caller);

            Assert.That(policy.IsEnabled((McpCapability)value), Is.False);
            Assert.That(
                () => policy.Require((McpCapability)value),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(policy.Require(McpCapability.PubSubRuntime), Is.EqualTo(caller));
            Assert.That(callers.HasRequestScope, Is.True);
        }

        [TestCase("Subject")]
        [TestCase("Issuer")]
        [TestCase("IsLocal")]
        [TestCase("SubjectCase")]
        [TestCase("IssuerCase")]
        [TestCase("SubjectWhitespace")]
        [TestCase("IssuerWhitespace")]
        public void RequireRejectsEachIdentityMismatch(string difference)
        {
            var intended = new McpCallerIdentity("operator", "urn:issuer-a");
            McpCallerIdentity other = difference switch
            {
                "Subject" => intended with { Subject = "other" },
                "Issuer" => intended with { Issuer = "urn:issuer-b" },
                "IsLocal" => intended with { IsLocal = true },
                "SubjectCase" => intended with { Subject = "Operator" },
                "IssuerCase" => intended with { Issuer = "urn:Issuer-a" },
                "SubjectWhitespace" => intended with { Subject = " operator " },
                "IssuerWhitespace" => intended with { Issuer = " urn:issuer-a " },
                _ => throw new ArgumentOutOfRangeException(nameof(difference))
            };
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = intended,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());

            using (callers.EnterTrustedCaller(other))
            {
                Assert.That(policy.RequireCaller(), Is.EqualTo(other));
                Assert.That(callers.Current, Is.EqualTo(other));
                Assert.That(callers.HasRequestScope, Is.True);
                Assert.That(
                    () => policy.Require(McpCapability.DirectoryAdministration),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            using (callers.EnterTrustedCaller(intended))
            {
                Assert.That(
                    policy.Require(McpCapability.DirectoryAdministration),
                    Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
                Assert.That(callers.Current, Is.EqualTo(intended));
                Assert.That(callers.HasRequestScope, Is.True);
            }
        }

        [Test]
        public void RequireCallerDoesNotConferCapabilityPermission()
        {
            var caller = new McpCallerIdentity("operator", "urn:issuer-a");
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions { EnabledCapabilities = [McpCapability.DirectoryAdministration] };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable scope = callers.EnterTrustedCaller(caller);

            Assert.That(policy.RequireCaller(), Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            Assert.That(policy.IsEnabled(McpCapability.DirectoryAdministration), Is.True);
            Assert.That(
                () => policy.Require(McpCapability.DirectoryAdministration),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(callers.Current, Is.EqualTo(caller));
            Assert.That(callers.HasRequestScope, Is.True);
        }

        [TestCase("Prod-A", "Prod-A", true)]
        [TestCase("Prod-A", "prod-a", true)]
        [TestCase("Prod-A", "PROD-A", true)]
        [TestCase("Prod-A", "Prod", false)]
        [TestCase("Prod-A", "Prod-A-Backup", false)]
        [TestCase("Prod-A", "XProd-A", false)]
        [TestCase("Prod-A", "Prod-B", false)]
        [TestCase("Prod-A", "Prod-*", false)]
        [TestCase("Prod-A", "*", false)]
        [TestCase("Prod-A", " Prod-A", false)]
        [TestCase("Prod-A", "Prod-A ", false)]
        [TestCase(" Prod-A ", "Prod-A", false)]
        [TestCase("Prod-A", "Prod-\0A", false)]
        [TestCase("Prod-\0A", "Prod-A", false)]
        [TestCase("Prod-\0A", "prod-\0a", true)]
        [TestCase("Pr\u00f6d-A", "PR\u00d6D-a", true)]
        [TestCase(null, "Prod-A", false)]
        [TestCase("*", "*", true)]
        [TestCase("*", "Prod-A", false)]
        [TestCase("*", "Prod-*", false)]
        public void SessionNamesUseOnlyOrdinalIgnoreCaseExactMatching(
            string? grantedName,
            string requestedName,
            bool allowed)
        {
            var caller = new McpCallerIdentity("operator", "urn:issuer-a");
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = caller,
                        Capabilities = [McpCapability.DirectoryAdministration],
                        SessionNames = grantedName is null ? [] : [grantedName],
                        SecretAliases = ["Vault/KeyA"]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable scope = callers.EnterTrustedCaller(caller);

            if (allowed)
            {
                Assert.That(
                    policy.RequireSession(McpCapability.DirectoryAdministration, requestedName),
                    Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            }
            else
            {
                Assert.That(
                    () => policy.RequireSession(McpCapability.DirectoryAdministration, requestedName),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            Assert.That(policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyA"), Is.EqualTo(caller));
            Assert.That(callers.Current, Is.EqualTo(caller));
            Assert.That(callers.HasRequestScope, Is.True);
        }

        [TestCase("Vault/KeyA", "Vault/KeyA", true)]
        [TestCase("Vault/KeyA", "vault/KeyA", false)]
        [TestCase("Vault/KeyA", "Vault/keyA", false)]
        [TestCase("Vault/KeyA", "Vault/Key", false)]
        [TestCase("Vault/KeyA", "Vault/KeyA-Backup", false)]
        [TestCase("Vault/KeyA", "XVault/KeyA", false)]
        [TestCase("Vault/KeyA", "Vault/KeyB", false)]
        [TestCase("Vault/KeyA", "Vault/*", false)]
        [TestCase("Vault/KeyA", "*", false)]
        [TestCase("Vault/KeyA", " Vault/KeyA", false)]
        [TestCase("Vault/KeyA", "Vault/KeyA ", false)]
        [TestCase(" Vault/KeyA ", "Vault/KeyA", false)]
        [TestCase("Vault/KeyA", "Vault/\0KeyA", false)]
        [TestCase("Vault/\0KeyA", "Vault/KeyA", false)]
        [TestCase("Vault/\0KeyA", "Vault/\0KeyA", true)]
        [TestCase(null, "Vault/KeyA", false)]
        [TestCase("*", "*", true)]
        [TestCase("*", "Vault/KeyA", false)]
        [TestCase("*", "Vault/*", false)]
        public void SecretAliasesUseOnlyOrdinalExactMatching(
            string? grantedAlias,
            string requestedAlias,
            bool allowed)
        {
            var caller = new McpCallerIdentity("operator", "urn:issuer-a");
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = caller,
                        Capabilities = [McpCapability.DirectoryAdministration],
                        SessionNames = ["Prod-A"],
                        SecretAliases = grantedAlias is null ? [] : [grantedAlias]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable scope = callers.EnterTrustedCaller(caller);

            if (allowed)
            {
                Assert.That(
                    policy.RequireSecret(McpCapability.DirectoryAdministration, requestedAlias),
                    Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            }
            else
            {
                Assert.That(
                    () => policy.RequireSecret(McpCapability.DirectoryAdministration, requestedAlias),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            Assert.That(policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-A"), Is.EqualTo(caller));
            Assert.That(callers.Current, Is.EqualTo(caller));
            Assert.That(callers.HasRequestScope, Is.True);
        }

        [TestCase("Session", null)]
        [TestCase("Session", "")]
        [TestCase("Session", " \t ")]
        [TestCase("Secret", null)]
        [TestCase("Secret", "")]
        [TestCase("Secret", " \t ")]
        public void SessionAndSecretChecksRejectBlankArgumentsBeforeAuthority(string operation, string? value)
        {
            var callers = new McpCallerContext();
            var policy = new McpExecutionPolicy(
                new McpExecutionOptions(), callers, CreateTelemetry());
            Type expectedException = value is null ? typeof(ArgumentNullException) : typeof(ArgumentException);

            if (operation == "Session")
            {
                Assert.That(
                    () => policy.RequireSession(McpCapability.DirectoryAdministration, value!),
                    Throws.TypeOf(expectedException)
                        .With.Property(nameof(ArgumentException.ParamName)).EqualTo("sessionName"));
            }
            else
            {
                Assert.That(
                    () => policy.RequireSecret(McpCapability.DirectoryAdministration, value!),
                    Throws.TypeOf(expectedException)
                        .With.Property(nameof(ArgumentException.ParamName)).EqualTo("alias"));
            }
            Assert.That(policy.IsEnabled(McpCapability.DirectoryAdministration), Is.False);
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SecretDenialDoesNotEchoSensitiveAlias(bool hostEnabled)
        {
            const string kSensitiveAlias = "private-fixture-alias-7e4c/Key";
            var caller = new McpCallerIdentity("operator", "urn:issuer-a");
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = hostEnabled ? [McpCapability.DirectoryAdministration] : [],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = caller,
                        Capabilities = [McpCapability.DirectoryAdministration],
                        SecretAliases = hostEnabled ? ["Vault/KeyA"] : [kSensitiveAlias]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable scope = callers.EnterTrustedCaller(caller);

            Assert.That(
                () => policy.RequireSecret(McpCapability.DirectoryAdministration, kSensitiveAlias),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code)
                    .And.Property(nameof(Exception.Message)).Not.Contains(kSensitiveAlias));
            Assert.That(policy.IsEnabled(McpCapability.DirectoryAdministration), Is.EqualTo(hostEnabled));
            Assert.That(policy.RequireCaller(), Is.EqualTo(caller));
            Assert.That(callers.HasRequestScope, Is.True);
        }

        [TestCase("HostCapabilitiesProperty")]
        [TestCase("HostCapabilitiesStorage")]
        [TestCase("CallerGrantsProperty")]
        [TestCase("CallerGrantsStorage")]
        [TestCase("Caller")]
        [TestCase("CapabilitiesProperty")]
        [TestCase("CapabilitiesStorage")]
        [TestCase("SessionNamesProperty")]
        [TestCase("SessionNamesStorage")]
        [TestCase("SecretAliasesProperty")]
        [TestCase("SecretAliasesStorage")]
        [TestCase("LocalCallerIdNullToA")]
        [TestCase("LocalCallerIdAToB")]
        [TestCase("LocalCallerIdAToNull")]
        public void PolicySnapshotCannotGainAuthority(string mutation)
        {
            bool localFallback = mutation is "LocalCallerIdNullToA" or "LocalCallerIdAToB" or "LocalCallerIdAToNull";
            bool hostMutation = mutation is "HostCapabilitiesProperty" or "HostCapabilitiesStorage";
            McpCallerIdentity intended = localFallback
                ? new McpCallerIdentity("host-A", "urn:opcfoundation:mcp:local-host", true)
                : new McpCallerIdentity("operator", "urn:issuer-a");
            McpCallerIdentity candidate = localFallback
                ? new McpCallerIdentity("host-B", "urn:opcfoundation:mcp:local-host", true)
                : new McpCallerIdentity("other", "urn:issuer-a");
            McpCapability[] enabled = hostMutation
                ? [McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration]
                : [McpCapability.DirectoryAdministration, McpCapability.WotWrite];
            McpCapability[] capabilities = hostMutation
                ? [McpCapability.DirectoryAdministration, McpCapability.WotWrite]
                : [McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration];
            string[] sessions = ["Prod-A", "Prod-C"];
            string[] aliases = ["Vault/KeyA", "Vault/KeyC"];
            var grant = new McpCallerGrant
            {
                Caller = intended,
                Capabilities = ArrayOf.Wrapped(capabilities),
                SessionNames = ArrayOf.Wrapped(sessions),
                SecretAliases = ArrayOf.Wrapped(aliases)
            };
            var candidateGrant = new McpCallerGrant
            {
                Caller = candidate,
                Capabilities = [McpCapability.DirectoryAdministration],
                SessionNames = ["Prod-B"],
                SecretAliases = ["Vault/KeyB"]
            };
            McpCallerGrant[] grants =
            [
                grant,
                localFallback ? candidateGrant
                    : new McpCallerGrant { Caller = new McpCallerIdentity("auditor", "urn:issuer-a") }
            ];
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = ArrayOf.Wrapped(enabled),
                CallerGrants = ArrayOf.Wrapped(grants),
                LocalCallerId = localFallback && mutation != "LocalCallerIdNullToA" ? "host-A" : null
            };
            var callers = new McpCallerContext();
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable? scope = localFallback ? null : callers.EnterTrustedCaller(intended);

            if (mutation == "LocalCallerIdNullToA")
            {
                Assert.That(
                    () => policy.RequireCaller(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            else
            {
                Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(intended));
                Assert.That(
                    policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-A"),
                    Is.EqualTo(intended));
                Assert.That(
                    policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyA"),
                    Is.EqualTo(intended));
            }

            switch (mutation)
            {
                case "HostCapabilitiesProperty":
                    options.EnabledCapabilities = [McpCapability.DirectoryAdministration, McpCapability.WotWrite];
                    break;
                case "HostCapabilitiesStorage":
                    enabled[1] = McpCapability.WotWrite;
                    Assert.That(options.EnabledCapabilities[1], Is.EqualTo(McpCapability.WotWrite));
                    break;
                case "CallerGrantsProperty":
                    options.CallerGrants = [grant, candidateGrant];
                    break;
                case "CallerGrantsStorage":
                    grants[1] = candidateGrant;
                    Assert.That(options.CallerGrants[1], Is.SameAs(candidateGrant));
                    break;
                case "Caller":
                    grant.Caller = candidate;
                    break;
                case "CapabilitiesProperty":
                    grant.Capabilities = [McpCapability.DirectoryAdministration, McpCapability.WotWrite];
                    break;
                case "CapabilitiesStorage":
                    capabilities[1] = McpCapability.WotWrite;
                    Assert.That(grant.Capabilities[1], Is.EqualTo(McpCapability.WotWrite));
                    break;
                case "SessionNamesProperty":
                    grant.SessionNames = ["Prod-A", "Prod-B"];
                    break;
                case "SessionNamesStorage":
                    sessions[1] = "Prod-B";
                    Assert.That(grant.SessionNames[1], Is.EqualTo("Prod-B"));
                    break;
                case "SecretAliasesProperty":
                    grant.SecretAliases = ["Vault/KeyA", "Vault/KeyB"];
                    break;
                case "SecretAliasesStorage":
                    aliases[1] = "Vault/KeyB";
                    Assert.That(grant.SecretAliases[1], Is.EqualTo("Vault/KeyB"));
                    break;
                case "LocalCallerIdNullToA":
                    options.LocalCallerId = "host-A";
                    break;
                case "LocalCallerIdAToB":
                    options.LocalCallerId = "host-B";
                    break;
                case "LocalCallerIdAToNull":
                    options.LocalCallerId = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }

            Assert.That(policy.IsEnabled(McpCapability.DirectoryAdministration), Is.True);
            Assert.That(policy.IsEnabled(McpCapability.WotWrite), Is.EqualTo(!hostMutation));
            Assert.That(callers.Current, Is.EqualTo(localFallback ? null : intended));
            Assert.That(callers.HasRequestScope, Is.EqualTo(!localFallback));
            if (mutation == "LocalCallerIdNullToA")
            {
                Assert.That(
                    () => policy.RequireCaller(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(
                    () => policy.Require(McpCapability.DirectoryAdministration),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                using (callers.EnterTrustedCaller(intended))
                {
                    Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(intended));
                    Assert.That(callers.Current, Is.EqualTo(intended));
                    Assert.That(callers.HasRequestScope, Is.True);
                }
                Assert.That(callers.Current, Is.Null);
                Assert.That(callers.HasRequestScope, Is.False);
            }
            else
            {
                Assert.That(policy.RequireCaller(), Is.EqualTo(intended));
                Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(intended));
                Assert.That(
                    policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-A"),
                    Is.EqualTo(intended));
                Assert.That(
                    policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyA"),
                    Is.EqualTo(intended));
            }
            Assert.That(
                () => policy.Require(McpCapability.WotWrite),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(
                () => policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-B"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(
                () => policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyB"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));

            using (callers.EnterTrustedCaller(candidate))
            {
                Assert.That(callers.Current, Is.EqualTo(candidate));
                Assert.That(callers.HasRequestScope, Is.True);
                if (localFallback)
                {
                    Assert.That(
                        policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-B"),
                        Is.EqualTo(new McpCallerIdentity("host-B", "urn:opcfoundation:mcp:local-host", true)));
                    Assert.That(
                        policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyB"),
                        Is.EqualTo(candidate));
                }
                else
                {
                    Assert.That(
                        () => policy.Require(McpCapability.DirectoryAdministration),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.Code))
                            .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                }
            }
            Assert.That(callers.Current, Is.EqualTo(localFallback ? null : intended));
            Assert.That(callers.HasRequestScope, Is.EqualTo(!localFallback));
        }

        [TestCase("HostCapabilitiesProperty")]
        [TestCase("HostCapabilitiesStorage")]
        [TestCase("CallerGrantsProperty")]
        [TestCase("CallerGrantsStorage")]
        [TestCase("Caller")]
        [TestCase("CapabilitiesProperty")]
        [TestCase("CapabilitiesStorage")]
        [TestCase("SessionNamesProperty")]
        [TestCase("SessionNamesStorage")]
        [TestCase("SecretAliasesProperty")]
        [TestCase("SecretAliasesStorage")]
        [TestCase("LocalCallerIdNullToA")]
        [TestCase("LocalCallerIdAToB")]
        [TestCase("LocalCallerIdAToNull")]
        public void PolicySnapshotCannotLoseAuthority(string mutation)
        {
            bool localFallback = mutation is "LocalCallerIdNullToA" or "LocalCallerIdAToB" or "LocalCallerIdAToNull";
            bool hostMutation = mutation is "HostCapabilitiesProperty" or "HostCapabilitiesStorage";
            McpCallerIdentity intended = localFallback
                ? new McpCallerIdentity("host-A", "urn:opcfoundation:mcp:local-host", true)
                : new McpCallerIdentity("operator", "urn:issuer-a");
            McpCallerIdentity candidate = localFallback
                ? new McpCallerIdentity("host-B", "urn:opcfoundation:mcp:local-host", true)
                : new McpCallerIdentity("other", "urn:issuer-a");
            McpCapability[] enabled = hostMutation
                ? [McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration]
                : [McpCapability.DirectoryAdministration, McpCapability.WotWrite];
            McpCapability[] capabilities = hostMutation
                ? [McpCapability.DirectoryAdministration, McpCapability.WotWrite]
                : [McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration];
            string[] sessions = ["Prod-A", "Prod-C"];
            string[] aliases = ["Vault/KeyA", "Vault/KeyC"];
            var grant = new McpCallerGrant
            {
                Caller = intended,
                Capabilities = ArrayOf.Wrapped(capabilities),
                SessionNames = ArrayOf.Wrapped(sessions),
                SecretAliases = ArrayOf.Wrapped(aliases)
            };
            var candidateGrant = new McpCallerGrant
            {
                Caller = candidate,
                Capabilities = [McpCapability.DirectoryAdministration],
                SessionNames = ["Prod-B"],
                SecretAliases = ["Vault/KeyB"]
            };
            var auditorGrant = new McpCallerGrant { Caller = new McpCallerIdentity("auditor", "urn:issuer-a") };
            McpCallerGrant[] grants = [grant, localFallback ? candidateGrant : auditorGrant];
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = ArrayOf.Wrapped(enabled),
                CallerGrants = ArrayOf.Wrapped(grants),
                LocalCallerId = localFallback && mutation != "LocalCallerIdNullToA" ? "host-A" : null
            };
            var callers = new McpCallerContext();
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable? scope = localFallback ? null : callers.EnterTrustedCaller(intended);

            if (mutation == "LocalCallerIdNullToA")
            {
                Assert.That(
                    () => policy.RequireCaller(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            else
            {
                Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(intended));
                Assert.That(
                    policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-A"),
                    Is.EqualTo(intended));
                Assert.That(
                    policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyA"),
                    Is.EqualTo(intended));
            }

            switch (mutation)
            {
                case "HostCapabilitiesProperty":
                    options.EnabledCapabilities = [McpCapability.WotWrite];
                    break;
                case "HostCapabilitiesStorage":
                    enabled[0] = McpCapability.WotWrite;
                    Assert.That(options.EnabledCapabilities[0], Is.EqualTo(McpCapability.WotWrite));
                    break;
                case "CallerGrantsProperty":
                    options.CallerGrants = [candidateGrant, auditorGrant];
                    break;
                case "CallerGrantsStorage":
                    grants[0] = candidateGrant;
                    Assert.That(options.CallerGrants[0], Is.SameAs(candidateGrant));
                    break;
                case "Caller":
                    grant.Caller = candidate;
                    break;
                case "CapabilitiesProperty":
                    grant.Capabilities = [McpCapability.WotWrite];
                    break;
                case "CapabilitiesStorage":
                    capabilities[0] = McpCapability.WotWrite;
                    Assert.That(grant.Capabilities[0], Is.EqualTo(McpCapability.WotWrite));
                    break;
                case "SessionNamesProperty":
                    grant.SessionNames = ["Prod-B"];
                    break;
                case "SessionNamesStorage":
                    sessions[0] = "Prod-B";
                    Assert.That(grant.SessionNames[0], Is.EqualTo("Prod-B"));
                    break;
                case "SecretAliasesProperty":
                    grant.SecretAliases = ["Vault/KeyB"];
                    break;
                case "SecretAliasesStorage":
                    aliases[0] = "Vault/KeyB";
                    Assert.That(grant.SecretAliases[0], Is.EqualTo("Vault/KeyB"));
                    break;
                case "LocalCallerIdNullToA":
                    options.LocalCallerId = "host-A";
                    break;
                case "LocalCallerIdAToB":
                    options.LocalCallerId = "host-B";
                    break;
                case "LocalCallerIdAToNull":
                    options.LocalCallerId = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }

            Assert.That(policy.IsEnabled(McpCapability.DirectoryAdministration), Is.True);
            Assert.That(policy.IsEnabled(McpCapability.WotWrite), Is.EqualTo(!hostMutation));
            Assert.That(callers.Current, Is.EqualTo(localFallback ? null : intended));
            Assert.That(callers.HasRequestScope, Is.EqualTo(!localFallback));
            if (mutation == "LocalCallerIdNullToA")
            {
                Assert.That(
                    () => policy.RequireCaller(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(
                    () => policy.Require(McpCapability.DirectoryAdministration),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                using (callers.EnterTrustedCaller(intended))
                {
                    Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(intended));
                    Assert.That(callers.Current, Is.EqualTo(intended));
                    Assert.That(callers.HasRequestScope, Is.True);
                }
                Assert.That(callers.Current, Is.Null);
                Assert.That(callers.HasRequestScope, Is.False);
            }
            else
            {
                Assert.That(policy.RequireCaller(), Is.EqualTo(intended));
                Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(intended));
                Assert.That(
                    policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-A"),
                    Is.EqualTo(intended));
                Assert.That(
                    policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyA"),
                    Is.EqualTo(intended));
            }
            Assert.That(
                () => policy.Require(McpCapability.WotWrite),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(
                () => policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-B"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(
                () => policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyB"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));

            using (callers.EnterTrustedCaller(candidate))
            {
                Assert.That(callers.Current, Is.EqualTo(candidate));
                Assert.That(callers.HasRequestScope, Is.True);
                if (localFallback)
                {
                    Assert.That(
                        policy.RequireSession(McpCapability.DirectoryAdministration, "Prod-B"),
                        Is.EqualTo(new McpCallerIdentity("host-B", "urn:opcfoundation:mcp:local-host", true)));
                    Assert.That(
                        policy.RequireSecret(McpCapability.DirectoryAdministration, "Vault/KeyB"),
                        Is.EqualTo(candidate));
                }
                else
                {
                    Assert.That(
                        () => policy.Require(McpCapability.DirectoryAdministration),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.Code))
                            .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                }
            }
            Assert.That(callers.Current, Is.EqualTo(localFallback ? null : intended));
            Assert.That(callers.HasRequestScope, Is.EqualTo(!localFallback));
        }

        [TestCase(McpCapability.DirectoryAdministration, "Prod-A", "Vault/KeyA")]
        [TestCase(McpCapability.WotInvoke, "Prod-B", "Vault/KeyB")]
        public void RequireFindsNonLeadingGrantsCapabilitiesAndNames(
            McpCapability capability,
            string session,
            string alias)
        {
            var caller = new McpCallerIdentity("operator", "urn:issuer-a");
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities =
                [
                    McpCapability.CertificateAdministration, McpCapability.DirectoryAdministration,
                    McpCapability.WotInvoke
                ],
                CallerGrants =
                [
                    new McpCallerGrant { Caller = new McpCallerIdentity("auditor", "urn:issuer-a") },
                    new McpCallerGrant
                    {
                        Caller = caller,
                        Capabilities =
                        [
                            McpCapability.CertificateAdministration, McpCapability.DirectoryAdministration,
                            McpCapability.WotInvoke
                        ],
                        SessionNames = ["Prod-C", "Prod-A", "Prod-B"],
                        SecretAliases = ["Vault/KeyC", "Vault/KeyA", "Vault/KeyB"]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable scope = callers.EnterTrustedCaller(caller);

            Assert.That(policy.IsEnabled(capability), Is.True);
            Assert.That(policy.Require(capability), Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
            Assert.That(policy.RequireSession(capability, session), Is.EqualTo(caller));
            Assert.That(policy.RequireSecret(capability, alias), Is.EqualTo(caller));
            Assert.That(
                () => policy.RequireSession(capability, "Prod-D"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(
                () => policy.RequireSecret(capability, "Vault/KeyD"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(callers.Current, Is.EqualTo(caller));
            Assert.That(callers.HasRequestScope, Is.True);
        }

        [Test]
        public void DuplicateCapabilitiesDoNotBroadenAuthority()
        {
            var caller = new McpCallerIdentity("operator", "urn:issuer-a");
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities =
                [
                    McpCapability.DirectoryAdministration, McpCapability.DirectoryAdministration,
                    McpCapability.WotWrite
                ],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = caller,
                        Capabilities = [McpCapability.DirectoryAdministration, McpCapability.DirectoryAdministration]
                    }
                ]
            };
            Assert.That(options.Validate, Throws.Nothing);
            McpExecutionOptions snapshot = options.Snapshot();
            Assert.That(
                snapshot.EnabledCapabilities.ToArray(),
                Is.EqualTo(
                [
                    McpCapability.DirectoryAdministration, McpCapability.DirectoryAdministration, McpCapability.WotWrite
                ]));
            Assert.That(
                snapshot.CallerGrants[0].Capabilities.ToArray(),
                Is.EqualTo([McpCapability.DirectoryAdministration, McpCapability.DirectoryAdministration]));
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable scope = callers.EnterTrustedCaller(caller);

            Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(caller));
            Assert.That(policy.IsEnabled(McpCapability.WotWrite), Is.True);
            Assert.That(
                () => policy.Require(McpCapability.WotWrite),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(callers.Current, Is.EqualTo(caller));
            Assert.That(callers.HasRequestScope, Is.True);
        }

        private static IEnumerable<TestCaseData> InvalidGrantCallers()
        {
            yield return new TestCaseData(null, typeof(ArgumentNullException))
                .SetName($"{nameof(GrantRejectsMissingOrMalformedCaller)}(NullCaller)");
            (string Label, string? Value, Type ExceptionType)[] invalidValues =
            [
                ("Null", null, typeof(ArgumentNullException)),
                ("Empty", string.Empty, typeof(ArgumentException)),
                ("Whitespace", " \t ", typeof(ArgumentException)),
                ("4097", new string('x', 4097), typeof(ArgumentOutOfRangeException)),
                ("4098", new string('x', 4098), typeof(ArgumentOutOfRangeException))
            ];
            foreach ((string label, string? value, Type exceptionType) in invalidValues)
            {
                yield return new TestCaseData(new McpCallerIdentity(value!, "urn:issuer-a"), exceptionType)
                    .SetName($"{nameof(GrantRejectsMissingOrMalformedCaller)}(Subject{label})");
                yield return new TestCaseData(new McpCallerIdentity("operator", value!), exceptionType)
                    .SetName($"{nameof(GrantRejectsMissingOrMalformedCaller)}(Issuer{label})");
            }
        }

        private static IEnumerable<TestCaseData> LocalCallerIds()
        {
            yield return new TestCaseData(null, true)
                .SetName($"{nameof(OptionsValidateOptionalLocalCallerId)}(Null)");
            yield return new TestCaseData("h", true)
                .SetName($"{nameof(OptionsValidateOptionalLocalCallerId)}(OneCharacter)");
            yield return new TestCaseData("host-A", true)
                .SetName($"{nameof(OptionsValidateOptionalLocalCallerId)}(NamedHost)");
            yield return new TestCaseData(" Host-A ", true)
                .SetName($"{nameof(OptionsValidateOptionalLocalCallerId)}(PreserveWhitespaceAndCase)");
            yield return new TestCaseData(new string('h', 4095), true)
                .SetName($"{nameof(OptionsValidateOptionalLocalCallerId)}(Length4095)");
            yield return new TestCaseData(new string('h', 4096), true)
                .SetName($"{nameof(OptionsValidateOptionalLocalCallerId)}(Length4096)");
            yield return new TestCaseData(string.Empty, false)
                .SetName($"{nameof(OptionsValidateOptionalLocalCallerId)}(Empty)");
            yield return new TestCaseData(" \t ", false)
                .SetName($"{nameof(OptionsValidateOptionalLocalCallerId)}(Whitespace)");
        }

        private static ITelemetryContext CreateTelemetry()
        {
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(context => context.LoggerFactory).Returns(NullLoggerFactory.Instance);
            return telemetry.Object;
        }

        private static readonly string[] s_originalSessionNames = ["Prod-A", "Prod-C"];
        private static readonly string[] s_originalSecretAliases = ["Vault/KeyA", "Vault/KeyC"];
    }
}
#endif
