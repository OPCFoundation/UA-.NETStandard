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
using System.Collections.Immutable;

namespace Opc.Ua.WotCon.Binding.Planners
{
    /// <summary>
    /// The W3C WoT MQTT binding planner (Editor's Draft). It validates the
    /// <c>mqtt</c> / <c>mqtts</c> href scheme and the <c>mqv:</c> vocabulary
    /// (<c>controlPacket</c>, <c>topic</c>, <c>qos</c>, <c>retain</c>), checks
    /// <c>op</c> compatibility, content type, QoS and topic bounds, and compiles
    /// the form into immutable publish / subscribe metadata. It is executable when
    /// the MQTT executor is registered.
    /// </summary>
    public sealed class MqttBindingPlanner : WotProtocolBinderBase
    {
        /// <summary>The MQTT binding vocabulary URI.</summary>
        public const string BindingUri = "https://www.w3.org/2019/wot/mqtt";

        private static readonly string[] s_schemes = { "mqtt", "mqtts" };
        private static readonly HashSet<string> s_controlPackets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "connect", "publish", "subscribe", "unsubscribe", "disconnect"
        };

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.mqtt", "1.0-ed", BindingUri, "W3C WoT MQTT Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "W3C WoT MQTT Binding (Editor's Draft)",
            WotBindingSources.Mqtt,
            new[]
            {
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty,
                WoTBindingCapabilityEnum.InvokeAction,
                WoTBindingCapabilityEnum.SubscribeEvent,
                WoTBindingCapabilityEnum.UnsubscribeEvent
            },
            new[] { "application/json", "text/plain", "application/octet-stream" },
            isExecutable: true);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
            => MatchStandard(form, context, "mqv:");

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();
            if (!RequireHref(form, context, diagnostics, out string href))
            {
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            if (!TryParseUri(href, out Uri uri) ||
                (!string.Equals(uri.Scheme, "mqtt", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, "mqtts", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.InvalidHref,
                    "The href is not a valid absolute mqtt(s) URI.", form.Pointer("href")));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            string topic = form.TryGetString("mqv:topic", out string explicitTopic)
                ? explicitTopic
                : uri.AbsolutePath.TrimStart('/');
            if (string.IsNullOrEmpty(topic))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "No MQTT topic could be resolved from mqv:topic or the href path.",
                    form.Pointer("mqv:topic"), "mqv:topic"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            if (topic.Length > context.Bounds.MaxTopicLength)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.BoundsExceeded,
                    $"The MQTT topic exceeds the maximum length of {context.Bounds.MaxTopicLength}.",
                    form.Pointer("mqv:topic"), "mqv:topic"));
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }

            int qos = 0;
            if (form.TryGetInt32("mqv:qos", out int parsedQos))
            {
                if (parsedQos is < 0 or > 2)
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.InvalidFieldValue,
                        "mqv:qos must be 0, 1 or 2.", form.Pointer("mqv:qos"), "mqv:qos"));
                    return WotBindingCompilation.Unsupported(diagnostics.ToArray());
                }
                qos = parsedQos;
            }

            bool retain = form.TryGetBoolean("mqv:retain", out bool retainValue) && retainValue;

            string? controlPacket = null;
            if (form.TryGetString("mqv:controlPacket", out string packet))
            {
                if (!s_controlPackets.Contains(packet))
                {
                    diagnostics.Add(WotBindingDiagnostic.Warning(
                        WotBindingDiagnosticCode.UnknownVocabularyTerm,
                        $"'{packet}' is not a recognized MQTT control packet.",
                        form.Pointer("mqv:controlPacket"), "mqv:controlPacket"));
                }
                controlPacket = packet;
            }

            ResolveCodec(form, context, out WotPayloadDescriptor payload);
            WotEndpointDescriptor endpoint = MakeEndpoint(uri);
            var addressing = new WotAddressingDescriptor(topic,
                ImmutableDictionary<string, string>.Empty
                    .Add("qos", qos.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Add("retain", retain ? "true" : "false"));
            ImmutableArray<WotCredentialReference> security =
                ResolveSecurity(form, context, uri.GetLeftPart(UriPartial.Authority), diagnostics);

            var entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                string method = controlPacket ?? DefaultControlPacket(capability);
                var operation = new WotOperationDescriptor(capability, op, method);
                entries.Add(new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer, capability, op,
                    endpoint, addressing, operation, payload, security, Capability.IsExecutable));
            }

            if (entries.Count == 0)
            {
                return WotBindingCompilation.Unsupported(diagnostics.ToArray());
            }
            return WotBindingCompilation.Supported(entries.ToImmutable(), diagnostics.ToImmutableArray());
        }

        private static string DefaultControlPacket(WoTBindingCapabilityEnum operation)
        {
            return operation switch
            {
                WoTBindingCapabilityEnum.WriteProperty => "publish",
                WoTBindingCapabilityEnum.InvokeAction => "publish",
                _ => "subscribe"
            };
        }
    }
}
