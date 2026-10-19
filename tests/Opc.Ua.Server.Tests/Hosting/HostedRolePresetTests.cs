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

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Verifies that hosted presets retain role configuration and explicit role-manager ownership.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    public sealed class HostedRolePresetTests
    {
        /// <summary>
        /// Applies either form of role configuration after selecting a hosted preset.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void PresetAppliesConfiguredRoleIdentityRules(bool secure, bool configurationSection)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaServerBuilder server = AddPreset(services, secure);
            if (configurationSection)
            {
                IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Roles:0:Name"] = BrowseNames.WellKnownRole_Operator,
                        ["Roles:0:Identities:0:CriteriaType"] = nameof(IdentityCriteriaType.UserName),
                        ["Roles:0:Identities:0:Criteria"] = "preset-operator"
                    }).Build();
                server.ConfigureRoles(configuration);
            }
            else
            {
                server.ConfigureRoles(AddOperatorRule);
            }
            using ServiceProvider provider = services.BuildServiceProvider();

            RequireOperatorRule(provider.GetRequiredService<IRoleManager>());
            Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(IRoleManager)), Is.EqualTo(1));
        }

        /// <summary>
        /// Resolves previously registered options lazily even when no subsequent ConfigureRoles call is made.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PresetConsumesPreviouslyRegisteredRoleOptionsOnce(bool secure)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            int configured = 0;
            services.Configure<RoleConfigurationOptions>(options =>
            {
                configured++;
                AddOperatorRule(options);
            });
            AddPreset(services, secure);
            Assert.That(configured, Is.Zero);
            using ServiceProvider provider = services.BuildServiceProvider();

            IRoleManager roles = provider.GetRequiredService<IRoleManager>();

            RequireOperatorRule(roles);
            Assert.That(provider.GetRequiredService<IRoleManager>(), Is.SameAs(roles));
            Assert.That(configured, Is.EqualTo(1));
        }

        /// <summary>
        /// Keeps an explicit manager authoritative regardless of its position relative to the preset.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void PresetPreservesExplicitRoleManager(bool secure, bool registerBeforePreset)
        {
            var manager = new Mock<IRoleManager>(MockBehavior.Strict);
            var services = new ServiceCollection();
            services.AddLogging();
            if (registerBeforePreset)
            {
                services.AddSingleton(manager.Object);
            }
            IOpcUaServerBuilder server = AddPreset(services, secure);
            if (!registerBeforePreset)
            {
                server.AddRoleManager(manager.Object);
            }
            server.ConfigureRoles(AddOperatorRule);
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<IRoleManager>(), Is.SameAs(manager.Object));
            Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(IRoleManager)), Is.EqualTo(1));
            manager.VerifyNoOtherCalls();
        }

        private static IOpcUaServerBuilder AddPreset(IServiceCollection services, bool secure)
        {
            IOpcUaBuilder builder = services.AddOpcUa();
            return secure
                ? builder.AddSecureServer(static _ => { })
                : builder.AddReferenceServer();
        }

        private static void AddOperatorRule(RoleConfigurationOptions options)
        {
            options.Roles.Add(new RoleDefinitionOptions
            {
                Name = BrowseNames.WellKnownRole_Operator,
                Identities =
                {
                    new RoleIdentityMappingOptions
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = "preset-operator"
                    }
                }
            });
        }

        private static void RequireOperatorRule(IRoleManager roles)
        {
            RoleEntry role = roles.GetRole(ObjectIds.WellKnownRole_Operator) ??
                throw new AssertionException("The well-known Operator role is missing.");
            Assert.That(role.Identities, Has.Exactly(1).Matches<IdentityMappingRuleType>(rule =>
                rule is not null && rule.CriteriaType == IdentityCriteriaType.UserName && rule.Criteria == "preset-operator"));
        }
    }
}
