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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for viewing and modifying OPC UA client configuration settings
    /// for the current MCP server session. Changes are in-memory only and
    /// do not persist to the XML configuration file.
    /// </summary>
    [McpServerToolType]
    public sealed class ConfigurationTools
    {
        /// <summary>
        /// Get the current OPC UA client configuration settings.
        /// </summary>
        [McpServerTool(Name = "GetConfiguration")]
        [Description(
            "Get the current OPC UA client configuration settings, including transport quotas, security settings, and client configuration. Changes made with SetConfiguration are reflected here but are in-memory only (not saved to disk).")]
        public static async Task<string> GetConfigurationAsync(
            OpcUaSessionManager sessionManager,
            CancellationToken ct = default)
        {
            try
            {
                ApplicationConfiguration config = await sessionManager.EnsureConfigurationAsync(ct)
                    .ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["applicationName"] = config.ApplicationName,
                    ["applicationUri"] = config.ApplicationUri,
                    ["productUri"] = config.ProductUri,
                    ["applicationType"] = config.ApplicationType.ToString(),
                    ["transportQuotas"] = GetTransportQuotas(config),
                    ["security"] = GetSecuritySettings(config),
                    ["clientConfiguration"] = GetClientConfiguration(config)
                });
            }
            catch (Exception ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Modify OPC UA client configuration settings for the current session.
        /// </summary>
        [McpServerTool(Name = "SetConfiguration")]
        [Description(
            "Modify OPC UA client configuration settings for the current session. Changes are in-memory only and apply to subsequent connections. Use GetConfiguration to see current values. Provide only the settings you want to change.")]
        public static async Task<string> SetConfigurationAsync(
            OpcUaSessionManager sessionManager,
            [Description("Operation timeout in milliseconds (e.g. 120000)")] int? operationTimeout = null,
            [Description("Max string length for OPC UA messages (e.g. 4194304)")] int? maxStringLength = null,
            [Description("Max byte string length (e.g. 4194304)")] int? maxByteStringLength = null,
            [Description("Max array length (e.g. 65535)")] int? maxArrayLength = null,
            [Description("Max message size in bytes (e.g. 4194304)")] int? maxMessageSize = null,
            [Description("Max buffer size in bytes (e.g. 65535)")] int? maxBufferSize = null,
            [Description("Channel lifetime in milliseconds (e.g. 300000)")] int? channelLifetime = null,
            [Description("Security token lifetime in milliseconds (e.g. 3600000)")] int? securityTokenLifetime = null,
            [Description("Default session timeout in milliseconds (e.g. 60000)")] int? defaultSessionTimeout = null,
            [Description("Auto-accept untrusted certificates (true/false)")] bool? autoAcceptUntrustedCertificates = null,
            [Description("Reject SHA1 signed certificates (true/false)")] bool? rejectSha1SignedCertificates = null,
            [Description("Minimum certificate key size in bits (e.g. 2048)")] int? minimumCertificateKeySize = null,
            CancellationToken ct = default)
        {
            try
            {
                ApplicationConfiguration config = await sessionManager.EnsureConfigurationAsync(ct)
                    .ConfigureAwait(false);

                var changes = new List<string>();

                // Transport quotas
                if (config.TransportQuotas != null)
                {
                    if (operationTimeout.HasValue)
                    {
                        config.TransportQuotas.OperationTimeout = operationTimeout.Value;
                        changes.Add($"OperationTimeout={operationTimeout.Value}");
                    }
                    if (maxStringLength.HasValue)
                    {
                        config.TransportQuotas.MaxStringLength = maxStringLength.Value;
                        changes.Add($"MaxStringLength={maxStringLength.Value}");
                    }
                    if (maxByteStringLength.HasValue)
                    {
                        config.TransportQuotas.MaxByteStringLength = maxByteStringLength.Value;
                        changes.Add($"MaxByteStringLength={maxByteStringLength.Value}");
                    }
                    if (maxArrayLength.HasValue)
                    {
                        config.TransportQuotas.MaxArrayLength = maxArrayLength.Value;
                        changes.Add($"MaxArrayLength={maxArrayLength.Value}");
                    }
                    if (maxMessageSize.HasValue)
                    {
                        config.TransportQuotas.MaxMessageSize = maxMessageSize.Value;
                        changes.Add($"MaxMessageSize={maxMessageSize.Value}");
                    }
                    if (maxBufferSize.HasValue)
                    {
                        config.TransportQuotas.MaxBufferSize = maxBufferSize.Value;
                        changes.Add($"MaxBufferSize={maxBufferSize.Value}");
                    }
                    if (channelLifetime.HasValue)
                    {
                        config.TransportQuotas.ChannelLifetime = channelLifetime.Value;
                        changes.Add($"ChannelLifetime={channelLifetime.Value}");
                    }
                    if (securityTokenLifetime.HasValue)
                    {
                        config.TransportQuotas.SecurityTokenLifetime = securityTokenLifetime.Value;
                        changes.Add($"SecurityTokenLifetime={securityTokenLifetime.Value}");
                    }
                }

                // Client configuration
                if (config.ClientConfiguration != null && defaultSessionTimeout.HasValue)
                {
                    config.ClientConfiguration.DefaultSessionTimeout = defaultSessionTimeout.Value;
                    changes.Add($"DefaultSessionTimeout={defaultSessionTimeout.Value}");
                }

                // Security configuration
                if (config.SecurityConfiguration != null)
                {
                    if (autoAcceptUntrustedCertificates.HasValue)
                    {
                        config.SecurityConfiguration.AutoAcceptUntrustedCertificates =
                            autoAcceptUntrustedCertificates.Value;
                        changes.Add(
                            $"AutoAcceptUntrustedCertificates={autoAcceptUntrustedCertificates.Value}");
                    }
                    if (rejectSha1SignedCertificates.HasValue)
                    {
                        config.SecurityConfiguration.RejectSHA1SignedCertificates =
                            rejectSha1SignedCertificates.Value;
                        changes.Add(
                            $"RejectSHA1SignedCertificates={rejectSha1SignedCertificates.Value}");
                    }
                    if (minimumCertificateKeySize.HasValue)
                    {
                        config.SecurityConfiguration.MinimumCertificateKeySize =
                            (ushort)minimumCertificateKeySize.Value;
                        changes.Add(
                            $"MinimumCertificateKeySize={minimumCertificateKeySize.Value}");
                    }
                }

                if (changes.Count == 0)
                {
                    return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                    {
                        ["message"] = "No changes specified. Provide at least one setting to modify."
                    });
                }

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["message"] = "Configuration updated for current session (in-memory only, not saved to disk). " +
                                  "Disconnect and reconnect for transport quota changes to take effect.",
                    ["changes"] = changes
                });
            }
            catch (Exception ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["message"] = ex.Message
                });
            }
        }

        private static Dictionary<string, object?> GetTransportQuotas(ApplicationConfiguration config)
        {
            TransportQuotas? tq = config.TransportQuotas;
            if (tq == null)
            {
                return new Dictionary<string, object?> { ["configured"] = false };
            }

            return new Dictionary<string, object?>
            {
                ["operationTimeout"] = tq.OperationTimeout,
                ["maxStringLength"] = tq.MaxStringLength,
                ["maxByteStringLength"] = tq.MaxByteStringLength,
                ["maxArrayLength"] = tq.MaxArrayLength,
                ["maxMessageSize"] = tq.MaxMessageSize,
                ["maxBufferSize"] = tq.MaxBufferSize,
                ["channelLifetime"] = tq.ChannelLifetime,
                ["securityTokenLifetime"] = tq.SecurityTokenLifetime
            };
        }

        private static Dictionary<string, object?> GetSecuritySettings(ApplicationConfiguration config)
        {
            SecurityConfiguration? sc = config.SecurityConfiguration;
            if (sc == null)
            {
                return new Dictionary<string, object?> { ["configured"] = false };
            }

            return new Dictionary<string, object?>
            {
                ["autoAcceptUntrustedCertificates"] = sc.AutoAcceptUntrustedCertificates,
                ["rejectSHA1SignedCertificates"] = sc.RejectSHA1SignedCertificates,
                ["rejectUnknownRevocationStatus"] = sc.RejectUnknownRevocationStatus,
                ["minimumCertificateKeySize"] = sc.MinimumCertificateKeySize,
                ["sendCertificateChain"] = sc.SendCertificateChain,
                ["addAppCertToTrustedStore"] = sc.AddAppCertToTrustedStore
            };
        }

        private static Dictionary<string, object?> GetClientConfiguration(ApplicationConfiguration config)
        {
            ClientConfiguration? cc = config.ClientConfiguration;
            if (cc == null)
            {
                return new Dictionary<string, object?> { ["configured"] = false };
            }

            return new Dictionary<string, object?>
            {
                ["defaultSessionTimeout"] = cc.DefaultSessionTimeout,
                ["minSubscriptionLifetime"] = cc.MinSubscriptionLifetime
            };
        }
    }
}
