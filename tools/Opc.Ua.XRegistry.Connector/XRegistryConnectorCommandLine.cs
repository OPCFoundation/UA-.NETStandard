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
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Connector
{
    /// <summary>
    /// Creates the connector's commands with an injectable execution interface.
    /// Parsing and full configuration validation complete before execution starts.
    /// </summary>
    public static class XRegistryConnectorCommandLine
    {
        public static RootCommand Create(
            Func<XRegistryConnectorSettings, CancellationToken, Task<int>> execute,
            TextWriter? error = null)
        {
            ArgumentNullException.ThrowIfNull(execute);
            error ??= Console.Error;
            return new RootCommand("xRegistry OPC UA <-> HTTP binding bridge (experimental, xRegistry 1.0-rc4)")
            {
                CreateCommand("http-gateway", "Serve HTTP over an authoritative OPC UA registry.",
                    XRegistryConnectorCommand.HttpGateway, execute, error),
                CreateCommand("opcua-gateway", "Expose an authoritative HTTP registry through OPC UA.",
                    XRegistryConnectorCommand.OpcUaGateway, execute, error),
                CreateCommand("sync", "Reconcile two writable registries using durable, guarded state.",
                    XRegistryConnectorCommand.Sync, execute, error),
                CreateCommand("inspect", "Read the configured registry's effective model and capability profile.",
                    XRegistryConnectorCommand.Inspect, execute, error),
                CreateCommand("conflicts", "List persisted conflicts without contacting either registry.",
                    XRegistryConnectorCommand.Conflicts, execute, error),
                CreateCommand("resolve", "Record a guarded resolution decision for a persisted conflict.",
                    XRegistryConnectorCommand.Resolve, execute, error)
            };
        }

        private static Command CreateCommand(
            string name,
            string description,
            XRegistryConnectorCommand kind,
            Func<XRegistryConnectorSettings, CancellationToken, Task<int>> execute,
            TextWriter error)
        {
            bool native = kind is XRegistryConnectorCommand.HttpGateway or XRegistryConnectorCommand.Sync
                or XRegistryConnectorCommand.Inspect;
            bool http = kind is XRegistryConnectorCommand.OpcUaGateway or XRegistryConnectorCommand.Sync
                or XRegistryConnectorCommand.Inspect;
            bool gateway = kind is XRegistryConnectorCommand.HttpGateway or XRegistryConnectorCommand.OpcUaGateway;
            bool network = native || http;
            bool state = kind is XRegistryConnectorCommand.Sync or XRegistryConnectorCommand.Conflicts
                or XRegistryConnectorCommand.Resolve;
            var opcua = new Option<string?>("--opcua") { Description = "Upstream OPC UA discovery URL." };
            var registryNode = new Option<string?>("--registry-node")
            {
                Description = "Explicit registry root NodeId (namespace-URI form recommended)."
            };
            var httpRoot = new Option<string?>("--http-root") { Description = "Authoritative HTTP registry root URL." };
            var listen = new Option<string?>("--listen") { Description = "Local listener URL.", Required = true };
            var publicRoot = new Option<string?>("--public-root")
            {
                Description = "Public HTTP registry root, including its base path.",
                Required = true
            };
            var config = new Option<string?>("--config") { Description = "Host configuration JSON file." };
            var profile = new Option<string>("--profile")
            {
                Description = "Operator credential profile name; never a password or token.",
                DefaultValueFactory = _ => "default"
            };
            var model = new Option<string?>("--model")
            {
                Description = "Explicit model JSON for a native registry without a Model document."
            };
            var statePath = new Option<string?>("--state")
            {
                Description = "Private persistent synchronization state directory.",
                Required = true
            };
            var job = new Option<string>("--job")
            {
                Description = "Stable synchronization job identity.",
                DefaultValueFactory = _ => "xregistry"
            };
            var policy = new Option<string>("--conflict-policy")
            {
                Description = "manual, prefer-opcua or prefer-http. Preferences still use concurrency guards.",
                DefaultValueFactory = _ => "manual"
            };
            var deletes = new Option<string>("--deletes")
            {
                Description = "on or off. Propagated deletes are always guarded.",
                DefaultValueFactory = _ => "on"
            };
            var once = new Option<bool>("--once") { Description = "Complete one reconciliation pass, then exit." };
            var dryRun = new Option<bool>("--dry-run")
            {
                Description = "Report planned changes without mutating registries or persistent state."
            };
            var interval = new Option<string>("--poll-interval")
            {
                Description = "Reconciliation interval (for example 00:00:05).",
                DefaultValueFactory = _ => "00:00:05"
            };
            var loopback = new Option<bool>("--allow-loopback-http")
            {
                Description = "Allow HTTP only on loopback for local development. Remote endpoints still require HTTPS."
            };
            var conflict = new Option<string?>("--conflict")
            {
                Description = "Persisted conflict identity.",
                Required = true
            };
            var resolution = new Option<string?>("--resolution")
            {
                Description = "prefer-opcua or prefer-http; applied only after revalidation by sync.",
                Required = true
            };

            var command = new Command(name, description);
            if (native)
            {
                command.Options.Add(opcua);
                command.Options.Add(registryNode);
                command.Options.Add(model);
            }
            if (http)
            {
                command.Options.Add(httpRoot);
            }
            if (network)
            {
                command.Options.Add(config);
                command.Options.Add(profile);
                command.Options.Add(loopback);
            }
            if (gateway)
            {
                command.Options.Add(listen);
            }
            if (kind == XRegistryConnectorCommand.HttpGateway)
            {
                command.Options.Add(publicRoot);
            }
            if (state)
            {
                command.Options.Add(statePath);
                command.Options.Add(job);
            }
            if (kind == XRegistryConnectorCommand.Sync)
            {
                command.Options.Add(policy);
                command.Options.Add(deletes);
                command.Options.Add(once);
                command.Options.Add(dryRun);
                command.Options.Add(interval);
            }
            if (kind == XRegistryConnectorCommand.Resolve)
            {
                command.Options.Add(conflict);
                command.Options.Add(resolution);
            }

            command.SetAction(async (result, cancellationToken) =>
            {
                XRegistryConnectorSettings settings;
                try
                {
                    string deleteSetting = kind == XRegistryConnectorCommand.Sync
                        ? result.GetValue(deletes)!
                        : "on";
                    if (deleteSetting is not ("on" or "off"))
                    {
                        throw new ArgumentException("--deletes must be on or off; there is no unguarded mode.");
                    }
                    settings = new XRegistryConnectorSettings
                    {
                        Command = kind,
                        OpcUaEndpoint = native ? ParseUri(result.GetValue(opcua), "--opcua") : null,
                        RegistryNodeId = native ? result.GetValue(registryNode) : null,
                        HttpRoot = http ? ParseUri(result.GetValue(httpRoot), "--http-root") : null,
                        ListenAddress = gateway ? ParseUri(result.GetValue(listen), "--listen") : null,
                        PublicHttpRoot = kind == XRegistryConnectorCommand.HttpGateway
                            ? ParseUri(result.GetValue(publicRoot), "--public-root") : null,
                        ConfigurationFile = network ? result.GetValue(config) : null,
                        CredentialProfile = network ? result.GetValue(profile)! : "default",
                        ModelFile = native ? result.GetValue(model) : null,
                        StateDirectory = state ? result.GetValue(statePath) : null,
                        JobId = state ? result.GetValue(job)! : "xregistry",
                        ConflictPolicy = kind == XRegistryConnectorCommand.Sync ? result.GetValue(policy)! : "manual",
                        PropagateDeletes = deleteSetting == "on",
                        Once = kind == XRegistryConnectorCommand.Sync && result.GetValue(once),
                        DryRun = kind == XRegistryConnectorCommand.Sync && result.GetValue(dryRun),
                        AllowLoopbackHttp = network && result.GetValue(loopback),
                        PollInterval = kind == XRegistryConnectorCommand.Sync
                            ? ParseInterval(result.GetValue(interval)!) : TimeSpan.FromSeconds(5),
                        ConflictId = kind == XRegistryConnectorCommand.Resolve ? result.GetValue(conflict) : null,
                        Resolution = kind == XRegistryConnectorCommand.Resolve ? result.GetValue(resolution) : null
                    };
                    settings.Validate();
                }
                catch (ArgumentException exception)
                {
                    await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                    return 2;
                }
                catch (IOException exception)
                {
                    await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                    return 2;
                }
                return await execute(settings, cancellationToken).ConfigureAwait(false);
            });
            return command;
        }

        private static Uri? ParseUri(string? value, string option)
        {
            return value is null ? null : Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out Uri? address)
                ? address : throw new ArgumentException($"Option '{option}' requires a valid URL.");
        }

        private static TimeSpan ParseInterval(string value)
        {
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan interval)
                ? interval : throw new ArgumentException("--poll-interval requires a TimeSpan such as 00:00:05.");
        }
    }
}
