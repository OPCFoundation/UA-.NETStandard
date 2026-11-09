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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Typed direct/DI facade over the shared owner-bound registry. Names never replace active runtimes.
    /// </summary>
    public sealed class McpPubSubRuntimeManager
    {
        /// <summary>
        /// Creates a manager over host-owned policy, registry, transport providers and secret stores.
        /// </summary>
        public McpPubSubRuntimeManager(
            IMcpOperationPolicy policy, McpRuntimeRegistry registry, McpPubSubOptions options,
            IMcpPubSubTransportProvider transports, McpSecretReferences secrets,
            ITelemetryContext telemetry, TimeProvider time, McpPubSubKeyReferences keyReferences)
        {
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_transports = transports ?? throw new ArgumentNullException(nameof(transports));
            m_secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_time = time ?? throw new ArgumentNullException(nameof(time));
            m_keys = keyReferences ?? throw new ArgumentNullException(nameof(keyReferences));
            options.Validate();
        }

        /// <summary>
        /// Discovers genuine backend capabilities without starting a transport or consuming a runtime slot.
        /// </summary>
        public JsonArray Capabilities()
        {
            return m_transports.Capabilities();
        }

        /// <summary>
        /// Validates schema and bounds without DNS, network or secret-store I/O.
        /// Validation does not grant activation or imply a policy-capable Kafka backend.
        /// </summary>
        public JsonObject Validate(PubSubApplicationInput input)
        {
            PubSubInputValidation.Validate(input, m_options);
            var source = new McpPubSubDataSetSource(
                input, m_options, ServiceMessageContext.CreateEmpty(m_telemetry), m_time);
            return new JsonObject
            {
                ["valid"] = true,
                ["started"] = false,
                ["transportProfileUri"] = PubSubInputValidation.Profile(input.Connection),
                ["metadata"] = McpCompanionJson.Encode(
                    source.BuildMetaData(), ServiceMessageContext.CreateEmpty(m_telemetry)),
                ["activationRequirements"] = input.Connection.Transport == PubSubTransportKind.Kafka
                    ? McpPubSubTransportProvider.KafkaLimitation
                    : "Explicit host/caller capability, exact endpoint/operation rules and any credential references"
            };
        }

        /// <summary>
        /// Creates a stopped named application under the shared host/caller quota and credential lease.
        /// </summary>
        public async ValueTask<JsonObject> CreateAsync(PubSubApplicationInput input, CancellationToken ct = default)
        {
            _ = m_policy.Require(McpCapability.PubSubRuntime);
            PubSubInputValidation.Validate(input, m_options);
            PubSubApplicationInput snapshot = Snapshot(input);
            ArrayOf<McpSecretDependency> dependencies = Dependencies(snapshot);
            McpRuntimeInfo info = await m_registry.OpenAsync(
                snapshot.Name, McpCapability.PubSubRuntime, dependencies,
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(CreateRuntime(snapshot));
                }, ct).ConfigureAwait(false);
            JsonObject result = Lease(info);
            result["started"] = false;
            result["legacyDefaultReplaced"] = false;
            return result;
        }

        /// <summary>
        /// Lists only this caller's PubSub application leases.
        /// </summary>
        public JsonArray List()
        {
            var result = new JsonArray();
            foreach (McpRuntimeInfo info in m_registry.List().ToList()
                .Where(info => info.Capability == McpCapability.PubSubRuntime))
            {
                result.Add((JsonNode)Lease(info));
            }
            return result;
        }

        /// <summary>
        /// Starts only the explicitly selected owned application.
        /// </summary>
        public async ValueTask<JsonObject> StartAsync(string handle, CancellationToken ct = default)
        {
            McpRuntimeUse<McpPubSubRuntime> use = await AcquireAsync(handle, ct).ConfigureAwait(false);
            await using (use.ConfigureAwait(false))
            {
                return await use.Runtime.StartAsync(use.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stops one application's transport resources without removing its configuration/lease.
        /// </summary>
        public async ValueTask<JsonObject> StopAsync(string handle, CancellationToken ct = default)
        {
            McpRuntimeUse<McpPubSubRuntime> use = await AcquireAsync(handle, ct).ConfigureAwait(false);
            await using (use.ConfigureAwait(false))
            {
                return await use.Runtime.StopAsync(use.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Removes one owned application, including one whose host activation grant was subsequently disabled.
        /// </summary>
        public async ValueTask<JsonObject> RemoveAsync(string handle, CancellationToken ct = default)
        {
            RequireHandleKind(handle, McpCapability.PubSubRuntime);
            await m_registry.CloseAsync(handle, ct).ConfigureAwait(false);
            return new JsonObject { ["removed"] = true, ["handle"] = handle };
        }

        /// <summary>
        /// Reads state, exact metadata, buffer limits and SDK counters without activation.
        /// </summary>
        public async ValueTask<JsonObject> StatusAsync(string handle, CancellationToken ct = default)
        {
            McpRuntimeUse<McpPubSubRuntime> use = await AcquireAsync(handle, ct).ConfigureAwait(false);
            await using (use.ConfigureAwait(false))
            {
                return await use.Runtime.StatusAsync(use.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Atomically updates a publisher sample while preserving field discriminators.
        /// </summary>
        public async ValueTask<JsonObject> PublishAsync(
            string handle, PubSubPublishInput input, CancellationToken ct = default)
        {
            McpRuntimeUse<McpPubSubRuntime> use = await AcquireAsync(handle, ct).ConfigureAwait(false);
            await using (use.ConfigureAwait(false))
            {
                return await use.Runtime.PublishAsync(input, use.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads a bounded page with explicit overflow and remaining-buffer metadata.
        /// </summary>
        public async ValueTask<JsonObject> ReadAsync(
            string handle, int maximum = 64, bool clear = false, CancellationToken ct = default)
        {
            McpRuntimeUse<McpPubSubRuntime> use = await AcquireAsync(handle, ct).ConfigureAwait(false);
            await using (use.ConfigureAwait(false))
            {
                return await use.Runtime.ReadAsync(maximum, clear, use.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Acquires the exact runtime type for adapter bindings without exposing synchronization.
        /// </summary>
        internal ValueTask<McpRuntimeUse<McpPubSubRuntime>> AcquireAsync(string handle, CancellationToken ct)
        {
            return m_registry.AcquireAsync<McpPubSubRuntime>(handle, McpCapability.PubSubRuntime, ct);
        }

        /// <summary>
        /// Creates one stopped resource for an admitted application or an owned declarative bridge.
        /// </summary>
        internal McpPubSubRuntime CreateRuntime(PubSubApplicationInput input, bool egressOnly = false)
        {
            return new McpPubSubRuntime(
                input, m_options, m_transports, m_secrets, m_telemetry, m_time, m_keys, egressOnly);
        }

        /// <summary>
        /// Captures credential leases before any admitted resource factory runs.
        /// Host aliases have no invalidation signal; their use is explicitly bounded by the runtime hard lease.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal ArrayOf<McpSecretDependency> Dependencies(PubSubApplicationInput input)
        {
            var dependencies = new List<McpSecretDependency>();
            if (input.Connection.CredentialReference is { } credential)
            {
                if (credential.StartsWith("alias:", StringComparison.Ordinal))
                {
                    _ = m_policy.RequireSecret(McpCapability.PubSubRuntime, credential["alias:".Length..]);
                }
                else
                {
                    dependencies.Add(m_secrets.GetDependency(
                        credential, input.Connection.Endpoint, McpCapability.PubSubRuntime));
                }
            }
            if (input.Security is { } security)
            {
                McpSecretDependency dependency = m_keys.Dependency(security);
                if (security.ExpiresAt > dependency.Reference.ExpiresAt ||
                    security.ExpiresAt <= m_time.GetUtcNow() ||
                    dependency.Reference.Purpose != "pubsub-key:" + security.GroupId ||
                    dependency.Reference.CredentialId != security.TokenId.ToString(CultureInfo.InvariantCulture))
                {
                    throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected,
                        "The SKS key reference does not match the requested group, token or expiry.");
                }
                dependencies.Add(new McpSecretDependency(
                    dependency.Reference with { ExpiresAt = security.ExpiresAt }, dependency.Invalidated));
            }
            return new ArrayOf<McpSecretDependency>(dependencies.ToArray());
        }

        /// <summary>
        /// Takes an independent bounded copy so a direct caller cannot mutate an admitted plan.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal PubSubApplicationInput Snapshot(PubSubApplicationInput input)
        {
            using var stream = new McpBoundedMemoryStream(m_options.MaxConfigurationBytes);
            JsonSerializer.Serialize(stream, input, McpPubSubExpansionJsonContext.Default.PubSubApplicationInput);
            stream.Position = 0;
            return JsonSerializer.Deserialize(stream, McpPubSubExpansionJsonContext.Default.PubSubApplicationInput) ??
                throw new InvalidOperationException("The PubSub input snapshot was empty.");
        }

        /// <summary>
        /// Checks owned handle type without acquiring a borrow that would deadlock explicit removal.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal void RequireHandleKind(string handle, McpCapability capability)
        {
            if (!m_registry.List().ToList().Any(info => info.Handle == handle && info.Capability == capability))
            {
                throw new ServiceResultException(StatusCodes.BadNotFound, "No owned PubSub runtime has that handle.");
            }
        }

        /// <summary>
        /// Projects only opaque handle and lease metadata.
        /// </summary>
        internal static JsonObject Lease(McpRuntimeInfo info)
        {
            return new JsonObject
            {
                ["handle"] = info.Handle,
                ["name"] = info.Name,
                ["state"] = info.State.ToString(),
                ["createdAt"] = info.CreatedAt,
                ["expiresAt"] = info.ExpiresAt,
                ["idleExpiresAt"] = info.IdleExpiresAt
            };
        }

        private readonly IMcpOperationPolicy m_policy;
        private readonly McpRuntimeRegistry m_registry;
        private readonly McpPubSubOptions m_options;
        private readonly IMcpPubSubTransportProvider m_transports;
        private readonly McpSecretReferences m_secrets;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_time;
        private readonly McpPubSubKeyReferences m_keys;
    }
}
