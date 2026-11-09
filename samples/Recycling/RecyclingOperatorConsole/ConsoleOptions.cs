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

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// Explicit workstation configuration; private credentials are external files, never browser inputs.
/// </summary>
internal sealed record ConsoleOptions
{
    /// <summary>
    /// Reads validated settings without reflection-based configuration binding.
    /// </summary>
    internal static ConsoleOptions Read(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("RecyclingConsole");
        string Required(string name) => section[name] is { Length: > 0 } value
            ? value : throw new InvalidOperationException("RecyclingConsole:" + name + " is required.");
        string role = section["Role"] ?? "Observer";
        if (role is not ("Observer" or "Operator" or "SimulatorAdministrator"))
        {
            role = "Observer";
        }
        string host = section["AllowedHost"] ?? "localhost:8080";
        if (!Uri.TryCreate("http://" + host, UriKind.Absolute, out Uri? origin) ||
            origin.PathAndQuery != "/" || origin.UserInfo.Length != 0 ||
            (!origin.IsLoopback && section["ContainerBind"] != "true"))
        {
            throw new InvalidOperationException("AllowedHost must be an explicit local HTTP authority.");
        }
        string endpoint = Required("Endpoint");
        ValidateEndpoint(endpoint);
        var endpoints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (RecyclingProcess process in Enum.GetValues<RecyclingProcess>())
        {
            string? value = section["ProcessEndpoints:" + process];
            if (!string.IsNullOrEmpty(value))
            {
                ValidateEndpoint(value);
                endpoints.Add(process.ToString(), value);
            }
        }
        return new ConsoleOptions
        {
            Endpoint = endpoint,
            LineId = Required("LineId"),
            Role = role,
            AllowedHost = host,
            Origin = origin.GetLeftPart(UriPartial.Authority),
            Port = origin.Port,
            ContainerBind = section["ContainerBind"] == "true",
            BootstrapKeyFile = Required("BootstrapKeyFile"),
            UserCertificatePath = Required("UserCertificatePath"),
            UserCertificateSubject = Required("UserCertificateSubject"),
            UserCertificatePasswordFile = section["UserCertificatePasswordFile"] ?? string.Empty,
            ApplicationCertificateStore = Required("ApplicationCertificateStore"),
            TrustedPeerStore = Required("TrustedPeerStore"),
            TrustedIssuerStore = Required("TrustedIssuerStore"),
            RejectedCertificateStore = Required("RejectedCertificateStore"),
            ProcessEndpoints = endpoints
        };
    }

    /// <summary>
    /// Rejects discovery URLs that cannot provide the sample's encrypted UA transport.
    /// </summary>
    private static void ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != "opc.tcp" || uri.UserInfo.Length != 0)
        {
            throw new InvalidOperationException("An explicit opc.tcp endpoint without credentials is required.");
        }
    }

    /// <summary>
    /// Coordinator discovery endpoint.
    /// </summary>
    internal string Endpoint { get; init; } = string.Empty;
    /// <summary>
    /// Configured line identity.
    /// </summary>
    internal string LineId { get; init; } = string.Empty;
    /// <summary>
    /// Conservative local permission ceiling.
    /// </summary>
    internal string Role { get; init; } = "Observer";
    /// <summary>
    /// Exact browser HTTP authority.
    /// </summary>
    internal string AllowedHost { get; init; } = string.Empty;
    /// <summary>
    /// Exact same-origin command origin.
    /// </summary>
    internal string Origin { get; init; } = string.Empty;
    /// <summary>
    /// Browser port derived from the explicit allowed authority; defaults to 8080.
    /// </summary>
    internal int Port { get; init; } = 8080;
    /// <summary>
    /// Explicit opt-in to container interface binding.
    /// </summary>
    internal bool ContainerBind { get; init; }
    /// <summary>
    /// External 256-bit hexadecimal bootstrap credential.
    /// </summary>
    internal string BootstrapKeyFile { get; init; } = string.Empty;
    /// <summary>
    /// External directory user-certificate store.
    /// </summary>
    internal string UserCertificatePath { get; init; } = string.Empty;
    /// <summary>
    /// User certificate subject selector in the external directory store.
    /// </summary>
    internal string UserCertificateSubject { get; init; } = string.Empty;
    /// <summary>
    /// External private-key password file, if protected.
    /// </summary>
    internal string UserCertificatePasswordFile { get; init; } = string.Empty;
    /// <summary>
    /// External application certificate directory.
    /// </summary>
    internal string ApplicationCertificateStore { get; init; } = string.Empty;
    /// <summary>
    /// Explicit trusted application peer directory.
    /// </summary>
    internal string TrustedPeerStore { get; init; } = string.Empty;
    /// <summary>
    /// Explicit trusted issuer directory.
    /// </summary>
    internal string TrustedIssuerStore { get; init; } = string.Empty;
    /// <summary>
    /// Rejected peer directory.
    /// </summary>
    internal string RejectedCertificateStore { get; init; } = string.Empty;
    /// <summary>
    /// Explicit, independent process endpoints.
    /// </summary>
    internal Dictionary<string, string> ProcessEndpoints { get; init; } = new(StringComparer.Ordinal);
}
