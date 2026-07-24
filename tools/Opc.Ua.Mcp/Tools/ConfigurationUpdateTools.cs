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

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Focused MCP tools for updating OPC UA client configuration.
    /// </summary>
    [McpServerToolType]
    public sealed class ConfigurationUpdateTools
    {
        /// <summary>
        /// Updates transport quotas for subsequent OPC UA connections.
        /// </summary>
        [McpServerTool(Name = "SetTransportConfiguration")]
        [Description(
            "Set in-memory transport quotas for subsequent OPC UA connections. " +
            "Use for message-size, timeout, buffer, and lifetime limits. Returns JSON " +
            "{success, changes, message}, or {success:false, error, changes, message}.")]
        public static Task<string> SetTransportConfigurationAsync(
            OpcUaSessionManager sessionManager,
            [Description("Operation timeout in milliseconds, e.g. 120000.")] int? operationTimeout = null,
            [Description("Maximum string length, e.g. 4194304.")] int? maxStringLength = null,
            [Description("Maximum byte-string length, e.g. 4194304.")] int? maxByteStringLength = null,
            [Description("Maximum array length, e.g. 65535.")] int? maxArrayLength = null,
            [Description("Maximum message size in bytes, e.g. 4194304.")] int? maxMessageSize = null,
            [Description("Maximum buffer size in bytes, e.g. 65535.")] int? maxBufferSize = null,
            [Description("Secure-channel lifetime in milliseconds, e.g. 300000.")] int? channelLifetime = null,
            [Description("Security-token lifetime in milliseconds, e.g. 3600000.")] int? securityTokenLifetime = null,
            CancellationToken ct = default)
        {
            return ConfigurationTools.SetConfigurationAsync(
                sessionManager,
                operationTimeout,
                maxStringLength,
                maxByteStringLength,
                maxArrayLength,
                maxMessageSize,
                maxBufferSize,
                channelLifetime,
                securityTokenLifetime,
                ct: ct);
        }

        /// <summary>
        /// Updates OPC UA client-session defaults.
        /// </summary>
        [McpServerTool(Name = "SetClientConfiguration")]
        [Description(
            "Set in-memory client-session defaults for subsequent OPC UA connections. " +
            "Use to change the default session timeout. Returns JSON {success, changes, message}, " +
            "or {success:false, error, changes, message}.")]
        public static Task<string> SetClientConfigurationAsync(
            OpcUaSessionManager sessionManager,
            [Description("Default session timeout in milliseconds, e.g. 60000.")] int? defaultSessionTimeout = null,
            CancellationToken ct = default)
        {
            return ConfigurationTools.SetConfigurationAsync(
                sessionManager,
                defaultSessionTimeout: defaultSessionTimeout,
                ct: ct);
        }

        /// <summary>
        /// Updates OPC UA certificate validation settings.
        /// </summary>
        [McpServerTool(Name = "SetSecurityConfiguration")]
        [Description(
            "Set in-memory security configuration for certificate validation on subsequent OPC UA connections. " +
            "Use only for explicit trust-policy changes. Returns JSON {success, changes, message}, " +
            "or {success:false, error, changes, message}.")]
        public static Task<string> SetSecurityConfigurationAsync(
            OpcUaSessionManager sessionManager,
            [Description("Whether to auto-accept untrusted certificates; false is the secure default.")]
            bool? autoAcceptUntrustedCertificates = null,
            [Description("Whether to reject SHA-1-signed certificates; true is the secure default.")]
            bool? rejectSha1SignedCertificates = null,
            [Description("Minimum certificate key size in bits, e.g. 2048 or 3072.")]
            int? minimumCertificateKeySize = null,
            CancellationToken ct = default)
        {
            return ConfigurationTools.SetConfigurationAsync(
                sessionManager,
                autoAcceptUntrustedCertificates: autoAcceptUntrustedCertificates,
                rejectSha1SignedCertificates: rejectSha1SignedCertificates,
                minimumCertificateKeySize: minimumCertificateKeySize,
                ct: ct);
        }
    }
}
