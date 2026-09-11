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
using System.IO;

namespace Opc.Ua.XRegistry.Connector
{
    /// <summary>
    /// Connector deployment and read-only administration commands.
    /// </summary>
    public enum XRegistryConnectorCommand
    {
        HttpGateway,
        OpcUaGateway,
        Sync,
        Inspect,
        Conflicts,
        Resolve
    }

    /// <summary>
    /// Validated command-line configuration. Credentials are profile references,
    /// never password or access-token values.
    /// </summary>
    public sealed record XRegistryConnectorSettings
    {
        public XRegistryConnectorCommand Command { get; init; }

        public Uri? OpcUaEndpoint { get; init; }

        public string? RegistryNodeId { get; init; }

        public Uri? HttpRoot { get; init; }

        public Uri? ListenAddress { get; init; }

        public Uri? PublicHttpRoot { get; init; }

        public string? ConfigurationFile { get; init; }

        public string CredentialProfile { get; init; } = "default";

        public string? ModelFile { get; init; }

        public string? StateDirectory { get; init; }

        public string JobId { get; init; } = "xregistry";

        public string ConflictPolicy { get; init; } = "manual";

        public bool PropagateDeletes { get; init; } = true;

        public bool Once { get; init; }

        public bool DryRun { get; init; }

        public bool AllowLoopbackHttp { get; init; }

        public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

        public string? ConflictId { get; init; }

        public string? Resolution { get; init; }

        /// <summary>
        /// Validates the complete deployment before opening state, listeners or connections.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The command or polling interval is outside its supported range.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// A required setting is missing or a configured value is invalid for the selected command.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// A specified configuration or model file does not exist.
        /// </exception>
        public void Validate()
        {
            if (Command is < XRegistryConnectorCommand.HttpGateway or > XRegistryConnectorCommand.Resolve)
            {
                throw new ArgumentOutOfRangeException(nameof(Command));
            }
            ValidateName(JobId, nameof(JobId));
            ValidateName(CredentialProfile, nameof(CredentialProfile));
            if (ConflictPolicy is not ("manual" or "prefer-opcua" or "prefer-http"))
            {
                throw new ArgumentException("Conflict policy must be manual, prefer-opcua or prefer-http.",
                    nameof(ConflictPolicy));
            }
            if (PollInterval <= TimeSpan.Zero || PollInterval > TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(nameof(PollInterval),
                    "The polling interval must be greater than zero and no more than one day.");
            }
            if (OpcUaEndpoint is not null)
            {
                ValidateUri(OpcUaEndpoint, nameof(OpcUaEndpoint));
                if (OpcUaEndpoint.Scheme is not ("opc.tcp" or "opc.wss" or "https" or "opc.quic"))
                {
                    throw new ArgumentException("The OPC UA endpoint must use a supported secure-capable transport.",
                        nameof(OpcUaEndpoint));
                }
            }
            if (HttpRoot is not null)
            {
                ValidateHttp(HttpRoot, nameof(HttpRoot));
            }
            if (PublicHttpRoot is not null)
            {
                ValidateHttp(PublicHttpRoot, nameof(PublicHttpRoot));
            }
            if (Command is XRegistryConnectorCommand.HttpGateway or XRegistryConnectorCommand.Sync)
            {
                Require(OpcUaEndpoint, "--opcua is required.");
                Require(RegistryNodeId, "--registry-node is required.");
            }
            if (Command is XRegistryConnectorCommand.OpcUaGateway or XRegistryConnectorCommand.Sync)
            {
                Require(HttpRoot, "--http-root is required.");
            }
            if (Command is XRegistryConnectorCommand.HttpGateway or XRegistryConnectorCommand.OpcUaGateway)
            {
                Require(ListenAddress, "--listen is required.");
                ValidateUri(ListenAddress!, nameof(ListenAddress));
                if (Command == XRegistryConnectorCommand.HttpGateway)
                {
                    ValidateHttp(ListenAddress!, nameof(ListenAddress));
                    if (ListenAddress!.AbsolutePath != "/")
                    {
                        throw new ArgumentException("Use --public-root for a base path; --listen binds an origin.",
                            nameof(ListenAddress));
                    }
                    Require(PublicHttpRoot, "--public-root is required.");
                }
                else if (ListenAddress!.Scheme != "opc.tcp")
                {
                    throw new ArgumentException("The OPC UA gateway listener must use opc.tcp.", nameof(ListenAddress));
                }
            }
            if (Command is XRegistryConnectorCommand.Sync or XRegistryConnectorCommand.Conflicts
                or XRegistryConnectorCommand.Resolve)
            {
                Require(StateDirectory, "--state is required.");
                _ = Path.GetFullPath(StateDirectory!);
            }
            if (Command == XRegistryConnectorCommand.Inspect)
            {
                if (OpcUaEndpoint is null && HttpRoot is null)
                {
                    throw new ArgumentException("Inspect requires --opcua or --http-root.");
                }
                if (OpcUaEndpoint is not null)
                {
                    Require(RegistryNodeId, "--registry-node is required for OPC UA inspection.");
                }
            }
            if (Command == XRegistryConnectorCommand.Resolve)
            {
                Require(ConflictId, "--conflict is required.");
                if (Resolution is not ("prefer-opcua" or "prefer-http"))
                {
                    throw new ArgumentException("Resolution must be prefer-opcua or prefer-http.", nameof(Resolution));
                }
            }
            if (ConfigurationFile is not null && !File.Exists(ConfigurationFile))
            {
                throw new FileNotFoundException("The specified configuration file does not exist.", ConfigurationFile);
            }
            if (ModelFile is not null && !File.Exists(ModelFile))
            {
                throw new FileNotFoundException("The specified model file does not exist.", ModelFile);
            }
        }

        private void ValidateHttp(Uri address, string parameter)
        {
            ValidateUri(address, parameter);
            if (address.Scheme != Uri.UriSchemeHttps &&
                !(AllowLoopbackHttp && address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
            {
                throw new ArgumentException(
                    "HTTP endpoints require HTTPS. --allow-loopback-http permits only local development HTTP.",
                    parameter);
            }
        }

        private static void ValidateUri(Uri address, string parameter)
        {
            if (!address.IsAbsoluteUri ||
                !string.IsNullOrEmpty(address.UserInfo) ||
                !string.IsNullOrEmpty(address.Query) ||
                !string.IsNullOrEmpty(address.Fragment))
            {
                throw new ArgumentException("Endpoints must be absolute and have no credentials, query or fragment.",
                    parameter);
            }
        }

        private static void ValidateName(string name, string parameter)
        {
            if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            {
                throw new ArgumentException("A nonempty profile or job name is required.", parameter);
            }
            foreach (char character in name)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
                {
                    throw new ArgumentException(
                        "Profile and job names may contain only letters, digits, '.', '_' or '-'.",
                        parameter);
                }
            }
        }

        private static void Require(Uri? value, string message)
        {
            if (value is null)
            {
                throw new ArgumentException(message);
            }
        }

        private static void Require(string? value, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(message);
            }
        }
    }
}
