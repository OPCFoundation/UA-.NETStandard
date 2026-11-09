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
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Reflection-free configuration of explicit host capabilities and exact caller/network grants.
    /// </summary>
    internal static class McpExecutionConfiguration
    {
        /// <summary>
        /// Reads host policy without granting a capability because its tool profile was selected.
        /// </summary>
        internal static McpExecutionOptions Read(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            IConfigurationSection section = configuration.GetSection("McpServer:Execution");
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = Capabilities(section.GetSection("EnabledCapabilities")),
                LocalCallerId = section["LocalCallerId"]
            };
            var grants = new List<McpCallerGrant>();
            foreach (IConfigurationSection grant in section.GetSection("CallerGrants").GetChildren())
            {
                IConfigurationSection identity = grant.GetSection("Caller");
                grants.Add(new McpCallerGrant
                {
                    Caller = new McpCallerIdentity(
                        Required(identity, "Subject"), Required(identity, "Issuer"), Boolean(identity, "IsLocal")),
                    Capabilities = Capabilities(grant.GetSection("Capabilities")),
                    SessionNames = Strings(grant.GetSection("SessionNames")),
                    SecretAliases = Strings(grant.GetSection("SecretAliases"))
                });
            }
            options.CallerGrants = grants.ToArray().ToArrayOf();
            var rules = new List<McpNetworkRule>();
            foreach (IConfigurationSection rule in section.GetSection("NetworkRules").GetChildren())
            {
                rules.Add(new McpNetworkRule
                {
                    Scheme = Required(rule, "Scheme"),
                    Host = Required(rule, "Host"),
                    Port = Integer(rule, "Port", 0),
                    Capability = Capability(Required(rule, "Capability")),
                    Networks = Strings(rule.GetSection("Networks")),
                    Operations = Strings(rule.GetSection("Operations")),
                    Resources = Strings(rule.GetSection("Resources"))
                });
            }
            options.NetworkRules = rules.ToArray().ToArrayOf();
            options.MaxRuntimes = Integer(section, "MaxRuntimes", options.MaxRuntimes);
            options.MaxRuntimesPerCaller = Integer(section, "MaxRuntimesPerCaller", options.MaxRuntimesPerCaller);
            options.MaxConcurrentOperationsPerRuntime = Integer(
                section, "MaxConcurrentOperationsPerRuntime", options.MaxConcurrentOperationsPerRuntime);
            options.RuntimeIdleTimeout = Duration(section, "RuntimeIdleTimeout", options.RuntimeIdleTimeout);
            options.RuntimeLeaseTimeout = Duration(section, "RuntimeLeaseTimeout", options.RuntimeLeaseTimeout);
            options.OperationTimeout = Duration(section, "OperationTimeout", options.OperationTimeout);
            options.Validate();
            return options;
        }

        /// <summary>
        /// Reads a finite explicit capability list.
        /// </summary>
        private static ArrayOf<McpCapability> Capabilities(IConfigurationSection section)
        {
            var values = new List<McpCapability>();
            foreach (string value in Strings(section))
            {
                values.Add(Capability(value));
            }
            return values.ToArray().ToArrayOf();
        }

        /// <summary>
        /// Rejects unsupported capability values instead of defaulting to the first enum member.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static McpCapability Capability(string value)
        {
            if (!Enum.TryParse(value, ignoreCase: true, out McpCapability capability) ||
                !Enum.IsDefined(capability))
            {
                throw new InvalidOperationException("The MCP host configuration contains an unknown capability.");
            }
            return capability;
        }

        /// <summary>
        /// Reads explicit array values and rejects malformed entries.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static ArrayOf<string> Strings(IConfigurationSection section)
        {
            var values = new List<string>();
            if (section.Value is not null)
            {
                throw new InvalidOperationException($"{section.Path} must be an array, not a scalar.");
            }
            foreach (IConfigurationSection item in section.GetChildren())
            {
                if (item.Value is not { } value)
                {
                    throw new InvalidOperationException($"{section.Path} contains a non-string entry.");
                }
                if (values.Count >= 4096)
                {
                    throw new InvalidOperationException($"{section.Path} exceeds the configuration entry limit.");
                }
                values.Add(value);
            }
            return values.ToArray().ToArrayOf();
        }

        /// <summary>
        /// Reads a required host identifier.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static string Required(IConfigurationSection section, string key)
        {
            string? value = section[key];
            return !string.IsNullOrWhiteSpace(value) ? value :
                throw new InvalidOperationException($"{section.Path}:{key} is required.");
        }

        /// <summary>
        /// Reads an explicit integer or preserves a documented default.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static int Integer(IConfigurationSection section, string key, int fallback)
        {
            string? value = section[key];
            if (value is null)
            {
                return fallback;
            }
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) ? result :
                throw new InvalidOperationException($"{section.Path}:{key} must be a nonnegative integer.");
        }

        /// <summary>
        /// Reads a duration or preserves the documented host default.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static TimeSpan Duration(IConfigurationSection section, string key, TimeSpan fallback)
        {
            string? value = section[key];
            if (value is null)
            {
                return fallback;
            }
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan result) ? result :
                throw new InvalidOperationException($"{section.Path}:{key} must be a duration.");
        }

        /// <summary>
        /// Reads an explicit boolean without a permissive fallback for malformed configuration.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static bool Boolean(IConfigurationSection section, string key)
        {
            string? value = section[key];
            if (value is null)
            {
                return false;
            }
            return bool.TryParse(value, out bool result) ? result :
                throw new InvalidOperationException($"{section.Path}:{key} must be true or false.");
        }
    }
}
