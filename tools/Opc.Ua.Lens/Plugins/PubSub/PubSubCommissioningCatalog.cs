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
using Opc.Ua;

namespace UaLens.Plugins.PubSub;

internal sealed record PubSubTransportChoice(string ProviderId, PubSubProfile Profile)
{
    public string Label => Profile + " / " + ProviderId;
}

internal sealed record PubSubKeyProviderChoice(string Id, PubSubKeySource Source, string Endpoint);

internal sealed record PubSubAdapterChoice(string Id, bool UsesPrimarySession);

internal sealed record PubSubProviderCatalog(
    ArrayOf<PubSubTransportChoice> Transports,
    ArrayOf<PubSubKeyProviderChoice> Keys,
    ArrayOf<PubSubAdapterChoice> Adapters)
{
    public static PubSubProviderCatalog Empty { get; } = new([], [], []);
}

internal interface IPubSubCommissioningCatalog
{
    PubSubProviderCatalog Catalog { get; }
}

internal sealed record PubSubPreset(string Name, string Detail, PubSubConfiguration Configuration);

/// <summary>
/// Offline templates are offered only for registered profiles. They never choose a
/// destination, credentials, interface, key provider, UA target or authorization.
/// </summary>
internal static class PubSubPresets
{
    public static ArrayOf<PubSubPreset> Create(PubSubProviderCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var presets = new List<PubSubPreset>();
        foreach (PubSubTransportChoice choice in catalog.Transports)
        {
            var configuration = new PubSubConfiguration
            {
                Profile = choice.Profile,
                TransportProviderId = choice.ProviderId,
                SecurityMode = choice.Profile is PubSubProfile.MqttJson or PubSubProfile.KafkaJson
                    ? MessageSecurityMode.None
                    : MessageSecurityMode.SignAndEncrypt
            };
            PubSubConfigurationValidation.RequireValid(configuration, requireEndpoint: false);
            const string Prerequisites =
                "Choose the endpoint/interface or broker, matching identities/schema " +
                "and security before explicit Start.";
            presets.Add(new PubSubPreset(choice.Label + " - observe", Prerequisites, configuration));
            presets.Add(new PubSubPreset(choice.Label + " - bounded synthetic publish",
                Prerequisites + " Publication additionally requires fresh consent.",
                configuration with { Publication = PubSubPublication.Synthetic, ReceiveEnabled = false }));
        }
        return [.. presets];
    }
}
