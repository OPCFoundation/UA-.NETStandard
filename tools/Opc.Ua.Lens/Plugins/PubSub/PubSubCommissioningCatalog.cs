// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
