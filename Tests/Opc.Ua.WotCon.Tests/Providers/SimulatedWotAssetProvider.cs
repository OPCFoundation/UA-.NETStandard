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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Tests.Providers
{
    /// <summary>
    /// Simulated WoT asset provider that pretends to talk to an asset
    /// via the <c>sim://</c> scheme. Holds property values in memory,
    /// pushes timer-driven value updates to subscribers, and echoes
    /// action invocations back as outputs.
    /// </summary>
    /// <remarks>
    /// This provider is the canonical example of the
    /// <see cref="IWotAssetProvider"/> contract and is used in every
    /// roundtrip integration test in the suite.
    /// </remarks>
    public sealed class SimulatedWotAssetProvider : IWotAssetProvider
    {
        public SimulatedWotAssetProvider(ThingDescription thingDescription)
        {
            ThingDescription = thingDescription
                ?? throw new ArgumentNullException(nameof(thingDescription));
            if (thingDescription.Properties != null)
            {
                foreach (KeyValuePair<string, WotProperty> kv in thingDescription.Properties)
                {
                    m_values[kv.Key] = DefaultForType(kv.Value);
                }
            }
        }

        public ThingDescription ThingDescription { get; }

        /// <summary>Snapshot of currently-known property values (test helper).</summary>
        public IReadOnlyDictionary<string, Variant> Values => m_values;

        /// <summary>Forces a value change so subscribers receive a notification.</summary>
        public void SetValue(string property, Variant value)
        {
            m_values[property] = value;
            if (m_subscriptions.TryGetValue(property, out List<Subscription>? subs))
            {
                Subscription[] snapshot;
                lock (subs)
                {
                    snapshot = [.. subs];
                }
                foreach (Subscription s in snapshot)
                {
                    s.Callback(s.Tag, value, StatusCodes.Good, DateTime.UtcNow);
                }
            }
        }

        public ValueTask<(ServiceResult Status, Variant Value)> ReadAsync(
            WotPropertyTag tag,
            CancellationToken ct)
        {
            if (m_values.TryGetValue(tag.Name, out Variant value))
            {
                return new ValueTask<(ServiceResult, Variant)>((ServiceResult.Good, value));
            }
            return new ValueTask<(ServiceResult, Variant)>(
                ((ServiceResult)StatusCodes.BadNoDataAvailable, Variant.Null));
        }

        public ValueTask<ServiceResult> WriteAsync(
            WotPropertyTag tag,
            Variant value,
            CancellationToken ct)
        {
            m_values[tag.Name] = value;
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        public ValueTask SubscribeAsync(
            WotPropertyTag tag,
            uint subscriberId,
            OnWotValueChange callback,
            CancellationToken ct)
        {
            List<Subscription> bucket = m_subscriptions.GetOrAdd(tag.Name, _ => []);
            lock (bucket)
            {
                bucket.Add(new Subscription(subscriberId, tag, callback));
            }
            return default;
        }

        public ValueTask UnsubscribeAsync(
            WotPropertyTag tag,
            uint subscriberId,
            CancellationToken ct)
        {
            if (m_subscriptions.TryGetValue(tag.Name, out List<Subscription>? bucket))
            {
                lock (bucket)
                {
                    bucket.RemoveAll(s => s.Id == subscriberId);
                }
            }
            return default;
        }

        public ValueTask<ServiceResult> InvokeActionAsync(
            WotActionTag action,
            IReadOnlyList<Variant> inputs,
            IList<Variant> outputs,
            CancellationToken ct)
        {
            // For tests we echo each input into the matching output and
            // record the call so tests can assert on it.
            var inputSnapshot = new Variant[inputs.Count];
            for (int i = 0; i < inputs.Count; i++)
            {
                inputSnapshot[i] = inputs[i];
            }
            m_invocations.Add(new ActionInvocation(action.Name, inputSnapshot));
            for (int i = 0; i < outputs.Count && i < inputs.Count; i++)
            {
                outputs[i] = inputs[i];
            }
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <summary>Returns the list of recorded action invocations (test helper).</summary>
        public IReadOnlyList<ActionInvocation> Invocations => [.. m_invocations];

        public ValueTask DisposeAsync()
        {
            m_subscriptions.Clear();
            return default;
        }

        private static Variant DefaultForType(WotProperty property)
        {
            return property.Type?.ToLowerInvariant() switch
            {
                "boolean" => new Variant(false),
                "number" => new Variant(0.0),
                "integer" => new Variant(0L),
                "string" => new Variant(string.Empty),
                _ => Variant.Null
            };
        }

        public sealed record ActionInvocation(string Name, Variant[] Inputs);

        private sealed record Subscription(uint Id, WotPropertyTag Tag, OnWotValueChange Callback);

        private readonly ConcurrentDictionary<string, Variant> m_values =
            new(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, List<Subscription>> m_subscriptions =
            new(StringComparer.Ordinal);

        private readonly ConcurrentBag<ActionInvocation> m_invocations = [];
    }

    /// <summary>
    /// Factory for <see cref="SimulatedWotAssetProvider"/>. Accepts TDs
    /// whose <c>base</c> URI uses the <c>sim://</c> scheme.
    /// </summary>
    public sealed class SimulatedWotAssetProviderFactory : IWotAssetProviderFactory
    {
        public const string BindingUri = "sim://opcua.test/wot";

        public IReadOnlyCollection<string> SupportedBindings { get; } = [BindingUri];

        public bool CanHandle(ThingDescription thingDescription)
        {
            return thingDescription?.Base != null &&
                Uri.TryCreate(thingDescription.Base, UriKind.Absolute, out Uri? baseUri) &&
                string.Equals(baseUri.Scheme, "sim", StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<IWotAssetProvider> ConnectAsync(
            ThingDescription thingDescription,
            CancellationToken ct)
        {
            return new(new SimulatedWotAssetProvider(thingDescription));
        }
    }
}
