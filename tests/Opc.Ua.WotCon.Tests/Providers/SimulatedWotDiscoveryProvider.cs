// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Tests.Providers
{
    /// <summary>
    /// In-memory discovery provider: returns a single canned endpoint
    /// and synthesises a flat TD with two properties + one action.
    /// </summary>
    public sealed class SimulatedWotDiscoveryProvider : IWotAssetDiscoveryProvider
    {
        public const string CannedEndpoint = "sim://opcua.test/wot/asset-001";

        public ValueTask<IReadOnlyList<string>> DiscoverAsync(CancellationToken ct)
        {
            return new([CannedEndpoint]);
        }

        public ValueTask<(bool Success, string Status)> TestAsync(
            string assetEndpoint,
            CancellationToken ct)
        {
            return new((assetEndpoint == CannedEndpoint, "Healthy"));
        }

        public ValueTask<ThingDescription> CreateThingDescriptionAsync(
            string assetName,
            string assetEndpoint,
            CancellationToken ct)
        {
            return new ValueTask<ThingDescription>(new ThingDescription
            {
                Name = assetName,
                Title = assetName,
                Base = assetEndpoint,
                Properties = new Dictionary<string, WotProperty>
                {
                    ["Voltage"] = new WotProperty
                    {
                        Type = "number",
                        Title = "Voltage",
                        Unit = "V",
                        ReadOnly = true,
                        Observable = true
                    },
                    ["Status"] = new WotProperty
                    {
                        Type = "string",
                        Title = "Status",
                        ReadOnly = true,
                        Observable = false
                    }
                },
                Actions = new Dictionary<string, WotAction>
                {
                    ["Reset"] = new WotAction { Title = "Reset" }
                }
            });
        }
    }
}
