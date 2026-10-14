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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host-owned capability grants and resource ceilings. Empty grants authorize no privileged operation.
    /// </summary>
    public sealed class McpExecutionOptions
    {
        /// <summary>
        /// Gets or sets the capabilities installed and enabled by the host.
        /// </summary>
        public ArrayOf<McpCapability> EnabledCapabilities { get; set; } = [];

        /// <summary>
        /// Gets or sets exact caller grants. A server-side UA role alone is not an MCP host grant.
        /// </summary>
        public ArrayOf<McpCallerGrant> CallerGrants { get; set; } = [];

        /// <summary>
        /// Gets or sets exact host endpoint, network, operation and resource rules for live runtimes.
        /// </summary>
        public ArrayOf<McpNetworkRule> NetworkRules { get; set; } = [];

        /// <summary>
        /// Gets or sets an explicit trusted in-process caller for direct embedding or isolated stdio.
        /// Network hosts must leave this unset and use authenticated request identity.
        /// </summary>
        public string? LocalCallerId { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of named runtimes owned by this host.
        /// </summary>
        public int MaxRuntimes { get; set; } = 8;

        /// <summary>
        /// Gets or sets the maximum number of named runtimes owned by one caller.
        /// </summary>
        public int MaxRuntimesPerCaller { get; set; } = 2;

        /// <summary>
        /// Gets or sets the idle lifetime of a named runtime.
        /// </summary>
        public TimeSpan RuntimeIdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the absolute maximum lease of a named runtime.
        /// </summary>
        public TimeSpan RuntimeLeaseTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Gets or sets the maximum simultaneous operations in one runtime.
        /// </summary>
        public int MaxConcurrentOperationsPerRuntime { get; set; } = 2;

        /// <summary>
        /// Gets or sets the deadline for a single live protocol operation.
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Rejects malformed configuration rather than silently relaxing a policy.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Validate()
        {
            if (EnabledCapabilities.IsNull || CallerGrants.IsNull || NetworkRules.IsNull)
            {
                throw new ArgumentException("Capability and caller-grant arrays must be explicit, not null.");
            }
            foreach (McpCapability capability in EnabledCapabilities)
            {
                if (!Enum.IsDefined(capability))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(EnabledCapabilities), capability, "Unknown MCP capability.");
                }
            }
            var callers = new HashSet<McpCallerIdentity>();
            foreach (McpCallerGrant grant in CallerGrants)
            {
                ArgumentNullException.ThrowIfNull(grant);
                grant.Validate();
                if (!callers.Add(grant.Caller))
                {
                    throw new ArgumentException("Caller grants must have distinct identities.", nameof(CallerGrants));
                }
            }
            if (LocalCallerId is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(LocalCallerId);
                if (LocalCallerId.Length > 4096)
                {
                    throw new ArgumentOutOfRangeException(nameof(LocalCallerId));
                }
            }
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxRuntimes, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxRuntimes, 1024);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxRuntimesPerCaller, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxRuntimesPerCaller, MaxRuntimes);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentOperationsPerRuntime, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxConcurrentOperationsPerRuntime, 32);
            if (RuntimeIdleTimeout <= TimeSpan.Zero ||
                RuntimeIdleTimeout > RuntimeLeaseTimeout ||
                RuntimeLeaseTimeout > TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(RuntimeLeaseTimeout), "Require 0 < idle timeout <= lease timeout <= one day.");
            }
            if (OperationTimeout <= TimeSpan.Zero || OperationTimeout > TimeSpan.FromMinutes(2))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(OperationTimeout), "The operation timeout must be positive and at most two minutes.");
            }
        }

        /// <summary>
        /// Creates an isolated policy snapshot before processing untrusted requests.
        /// </summary>
        internal McpExecutionOptions Snapshot()
        {
            Validate();
            return new McpExecutionOptions
            {
                EnabledCapabilities = [.. EnabledCapabilities],
                CallerGrants = CallerGrants.ConvertAll(grant => grant.Snapshot()),
                NetworkRules = NetworkRules.ConvertAll(rule => rule with
                {
                    Networks = [.. rule.Networks],
                    Operations = [.. rule.Operations],
                    Resources = [.. rule.Resources]
                }),
                LocalCallerId = LocalCallerId,
                MaxRuntimes = MaxRuntimes,
                MaxRuntimesPerCaller = MaxRuntimesPerCaller,
                RuntimeIdleTimeout = RuntimeIdleTimeout,
                RuntimeLeaseTimeout = RuntimeLeaseTimeout,
                MaxConcurrentOperationsPerRuntime = MaxConcurrentOperationsPerRuntime,
                OperationTimeout = OperationTimeout
            };
        }
    }

    /// <summary>
    /// Explicit authority assigned by the host to one trusted caller identity.
    /// </summary>
    public sealed class McpCallerGrant
    {
        /// <summary>
        /// Gets or sets the exact subject, issuer and local/remote identity to authorize.
        /// </summary>
        public required McpCallerIdentity Caller { get; set; }

        /// <summary>
        /// Gets or sets the subset of enabled host capabilities granted to this caller.
        /// </summary>
        public ArrayOf<McpCapability> Capabilities { get; set; } = [];

        /// <summary>
        /// Gets or sets the exact named UA sessions usable for privileged operations.
        /// An empty list grants no privileged session access.
        /// </summary>
        public ArrayOf<string> SessionNames { get; set; } = [];

        /// <summary>
        /// Gets or sets the exact preconfigured secret aliases this caller may consume.
        /// This does not grant arbitrary secret-store access.
        /// </summary>
        public ArrayOf<string> SecretAliases { get; set; } = [];

        /// <summary>
        /// Validates one non-wildcard authority statement.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal void Validate()
        {
            ArgumentNullException.ThrowIfNull(Caller);
            Caller.Validate();
            if (Capabilities.IsNull || SessionNames.IsNull || SecretAliases.IsNull)
            {
                throw new ArgumentException("Grant collections must be explicit, not null.");
            }
            foreach (McpCapability capability in Capabilities)
            {
                if (!Enum.IsDefined(capability))
                {
                    throw new ArgumentOutOfRangeException(nameof(Capabilities), capability, "Unknown MCP capability.");
                }
            }
            foreach (string name in SessionNames)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
            }
            foreach (string alias in SecretAliases)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(alias);
            }
        }

        /// <summary>
        /// Copies grant collections so later caller changes cannot mutate the active policy.
        /// </summary>
        internal McpCallerGrant Snapshot()
        {
            return new McpCallerGrant
            {
                Caller = Caller,
                Capabilities = [.. Capabilities],
                SessionNames = [.. SessionNames],
                SecretAliases = [.. SecretAliases]
            };
        }
    }
}
