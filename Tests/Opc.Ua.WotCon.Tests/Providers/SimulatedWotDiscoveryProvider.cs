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
