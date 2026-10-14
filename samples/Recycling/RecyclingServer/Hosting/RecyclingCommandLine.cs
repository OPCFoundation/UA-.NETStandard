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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Strict, bounded generic command-line configuration. No private scenario or credential is embedded.
    /// </summary>
    internal static class RecyclingCommandLine
    {
        /// <summary>
        /// Loads only explicitly identified scenario and role-mapping files.
        /// </summary>
        internal static async ValueTask<RecyclingHostOptions> LoadAsync(
            ArrayOf<string> arguments,
            CancellationToken cancellationToken = default)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < arguments.Count; i += 2)
            {
                if (i + 1 >= arguments.Count || !arguments[i].StartsWith("--", StringComparison.Ordinal) ||
                    !s_options.Contains(arguments[i]) || !values.TryAdd(arguments[i], arguments[i + 1]))
                {
                    throw new ArgumentException("Unknown, duplicate or incomplete command-line option.");
                }
            }
            bool isView = values.TryGetValue("--process", out string? selected);
            bool hasRole = values.TryGetValue("--role", out string? selectedRole);
            if (isView == hasRole || (hasRole && selectedRole != "coordinator"))
            {
                throw new ArgumentException("Select exactly --role coordinator or --process <one-of-ten>.");
            }
            RecyclingProcess process = isView ? ParseProcess(selected!) : RecyclingProcess.Collection;
            string role = isView ? selected! : "coordinator";
            string line = values.GetValueOrDefault("--line-id", "line-1");
            string root = Path.Combine(".recycling", line, role);
            string applicationName = "Recycling-" + line + "-" + role;
            string scenario = values.GetValueOrDefault("--scenario", "healthy");
            RecyclingConfiguration configuration;
            if (scenario.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                FileStream file = OpenBounded(scenario);
                await using (file.ConfigureAwait(false))
                {
                    configuration = await JsonSerializer.DeserializeAsync(
                        file, RecyclingJsonContext.Default.RecyclingConfiguration, cancellationToken)
                        .ConfigureAwait(false)
                        ?? throw new ArgumentException("Scenario document is null.");
                }
            }
            else
            {
                configuration = RecyclingConfiguration.ForScenario(scenario);
            }
            if (!values.TryGetValue("--identities", out string? identitiesPath))
            {
                throw new ArgumentException("--identities must identify a provisioned certificate-to-role mapping.");
            }
            RecyclingIdentityConfiguration identityConfiguration;
            FileStream identities = OpenBounded(identitiesPath);
            await using (identities.ConfigureAwait(false))
            {
                identityConfiguration = await JsonSerializer.DeserializeAsync(
                    identities, RecyclingJsonContext.Default.RecyclingIdentityConfiguration, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new ArgumentException("Identity configuration is null.");
            }
            int port = 62570 + (isView ? (int)process + 1 : 0);
            var options = new RecyclingHostOptions
            {
                Role = isView ? RecyclingHostRole.ProcessView : RecyclingHostRole.Coordinator,
                Process = process,
                LineId = line,
                ApplicationName = applicationName,
                ApplicationUri = "urn:localhost:OPCFoundation:" + applicationName,
                EndpointUrl = values.GetValueOrDefault(
                    "--endpoint", $"opc.tcp://localhost:{port.ToString(CultureInfo.InvariantCulture)}/Recycling"),
                CoordinatorUrl = values.GetValueOrDefault("--coordinator", string.Empty),
                PkiRoot = values.GetValueOrDefault("--pki-root", Path.Combine(root, "pki")),
                StateRoot = values.GetValueOrDefault("--state-root", Path.Combine(root, "state")),
                IdentityStorePath = values.GetValueOrDefault("--identity-store", string.Empty),
                IdentitySubject = values.GetValueOrDefault("--identity-subject", string.Empty),
                Identities = identityConfiguration.Identities,
                Simulation = configuration,
                TickPeriod = TimeSpan.FromMilliseconds(values.TryGetValue("--pace-ms", out string? pacing)
                    ? int.Parse(pacing, CultureInfo.InvariantCulture)
                    : 1000)
            };
            options.Validate();
            return options;
        }

        private static RecyclingProcess ParseProcess(string value)
        {
            return value switch
            {
                "collection" => RecyclingProcess.Collection,
                "sorting" => RecyclingProcess.Sorting,
                "shredding" => RecyclingProcess.Shredding,
                "washing" => RecyclingProcess.Washing,
                "density-separation" => RecyclingProcess.DensitySeparation,
                "drying" => RecyclingProcess.Drying,
                "extrusion" => RecyclingProcess.Extrusion,
                "melt-filtration" => RecyclingProcess.MeltFiltration,
                "pelletizing" => RecyclingProcess.Pelletizing,
                "bagging" => RecyclingProcess.Bagging,
                _ => throw new ArgumentException("Unknown process selection.", nameof(value))
            };
        }

        private static FileStream OpenBounded(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            if (stream.Length > 64 * 1024)
            {
                stream.Dispose();
                throw new ArgumentException("Scenario and identity documents must not exceed 64 KiB.", nameof(path));
            }
            return stream;
        }

        private static readonly HashSet<string> s_options =
        [
            "--role", "--process", "--line-id", "--endpoint", "--coordinator", "--pki-root", "--state-root",
            "--identity-store", "--identity-subject", "--identities", "--scenario", "--pace-ms"
        ];
    }

    /// <summary>
    /// External user-certificate role mapping; all secrets remain in certificate/secret stores.
    /// </summary>
    internal sealed record RecyclingIdentityConfiguration
    {
        /// <summary>
        /// Bounded unique identities.
        /// </summary>
        public ArrayOf<RecyclingIdentityBinding> Identities { get; init; } = [];
    }
}
