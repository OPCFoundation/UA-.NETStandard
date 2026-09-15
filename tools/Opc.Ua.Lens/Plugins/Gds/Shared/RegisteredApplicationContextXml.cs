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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Opc.Ua;

namespace UaLens.Plugins.Gds;

/// <summary>
/// Bridges the internal <see cref="RegisteredApplicationContext"/>
/// record with its internal <see cref="RegisteredApplicationContextDto"/>
/// twin for JSON save/load operations, with a backward-compatible XML
/// load path for files produced by previous (XmlSerializer-based) builds.
/// </summary>
/// <remarks>
/// All file I/O goes through these helpers so the
/// <see cref="JsonSerializer"/> / <see cref="XDocument"/> call sites are
/// localised and the dialog code doesn't need to deal with stream /
/// encoding plumbing. The JSON path is driven by the source-generated
/// <see cref="RegisteredApplicationContextJsonContext"/>, keeping it
/// trim-safe and NativeAOT-friendly.
/// </remarks>
internal static class RegisteredApplicationContextXml
{
    /// <summary>Lowers the internal record to its DTO.</summary>
    public static RegisteredApplicationContextDto ToDto(RegisteredApplicationContext src)
    {
        ArgumentNullException.ThrowIfNull(src);
        var dto = new RegisteredApplicationContextDto
        {
            ApplicationId = src.ApplicationId.IsNull ? null : src.ApplicationId.ToString(),
            ApplicationUri = src.ApplicationUri,
            ApplicationName = src.ApplicationName,
            ProductUri = src.ProductUri,
            RegistrationType = src.RegistrationType.ToString(),
            DiscoveryUrls = new List<string>(src.DiscoveryUrls),
            ServerCapabilities = new List<string>(src.ServerCapabilities),
            Domains = src.Domains,
            CertificateStorePath = src.CertificateStorePath,
            CertificateSubjectName = src.CertificateSubjectName,
            CertificatePublicKeyPath = src.CertificatePublicKeyPath,
            CertificatePrivateKeyPath = src.CertificatePrivateKeyPath,
            TrustListStorePath = src.TrustListStorePath,
            IssuerListStorePath = src.IssuerListStorePath,
            HttpsCertificatePublicKeyPath = src.HttpsCertificatePublicKeyPath,
            HttpsCertificatePrivateKeyPath = src.HttpsCertificatePrivateKeyPath,
            HttpsTrustListStorePath = src.HttpsTrustListStorePath,
            HttpsIssuerListStorePath = src.HttpsIssuerListStorePath
        };
        if (src.PushEndpoint is { } ep)
        {
            dto.PushEndpointUrl = ep.EndpointUrl;
            dto.PushEndpointSecurityMode = ep.SecurityMode.ToString();
            dto.PushEndpointSecurityPolicyUri = ep.SecurityPolicyUri;
        }
        return dto;
    }

    /// <summary>Promotes a deserialised DTO back into the internal record.</summary>
    public static RegisteredApplicationContext ToRecord(RegisteredApplicationContextDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!Enum.TryParse(dto.RegistrationType, ignoreCase: true, out GdsRegistrationType regType))
        {
            regType = GdsRegistrationType.ClientPull;
        }

        NodeId appId;
        if (string.IsNullOrWhiteSpace(dto.ApplicationId)
            || !NodeId.TryParse(dto.ApplicationId, out appId))
        {
            appId = NodeId.Null;
        }

        EndpointDescription? push = null;
        if (!string.IsNullOrWhiteSpace(dto.PushEndpointUrl))
        {
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt;
            if (!string.IsNullOrEmpty(dto.PushEndpointSecurityMode)
                && Enum.TryParse(dto.PushEndpointSecurityMode, ignoreCase: true, out MessageSecurityMode parsed))
            {
                mode = parsed;
            }
            push = new EndpointDescription
            {
                EndpointUrl = dto.PushEndpointUrl,
                SecurityMode = mode,
                SecurityPolicyUri = dto.PushEndpointSecurityPolicyUri ?? string.Empty
            };
        }

        return new RegisteredApplicationContext(
            ApplicationId: appId,
            ApplicationUri: dto.ApplicationUri ?? string.Empty,
            ApplicationName: dto.ApplicationName ?? string.Empty,
            ProductUri: dto.ProductUri ?? string.Empty,
            RegistrationType: regType,
            DiscoveryUrls: (dto.DiscoveryUrls ?? new List<string>()).AsReadOnly(),
            ServerCapabilities: (dto.ServerCapabilities ?? new List<string>()).AsReadOnly(),
            Domains: dto.Domains,
            CertificateStorePath: dto.CertificateStorePath,
            CertificateSubjectName: dto.CertificateSubjectName,
            CertificatePublicKeyPath: dto.CertificatePublicKeyPath,
            CertificatePrivateKeyPath: dto.CertificatePrivateKeyPath,
            TrustListStorePath: dto.TrustListStorePath,
            IssuerListStorePath: dto.IssuerListStorePath,
            HttpsCertificatePublicKeyPath: dto.HttpsCertificatePublicKeyPath,
            HttpsCertificatePrivateKeyPath: dto.HttpsCertificatePrivateKeyPath,
            HttpsTrustListStorePath: dto.HttpsTrustListStorePath,
            HttpsIssuerListStorePath: dto.HttpsIssuerListStorePath,
            PushEndpoint: push);
    }

    /// <summary>
    /// Serialises a context record to indented UTF-8 JSON at <paramref name="path"/>,
    /// creating parent directories on demand.
    /// </summary>
    public static void Save(RegisteredApplicationContext record, string path)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        using FileStream fs = File.Create(path);
        JsonSerializer.Serialize(
            fs,
            ToDto(record),
            RegisteredApplicationContextJsonContext.Default.RegisteredApplicationContextDto);
    }

    /// <summary>
    /// Deserialises a context record from <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// JSON (the new on-disk format) is attempted first. If parsing fails
    /// with a <see cref="JsonException"/> the file is treated as a legacy
    /// XmlSerializer-produced document and parsed via <see cref="XDocument"/>,
    /// so registrations saved by previous builds continue to load. Throws
    /// <see cref="InvalidDataException"/> when neither format matches.
    /// </remarks>
    public static RegisteredApplicationContext Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] bytes = File.ReadAllBytes(path);

        try
        {
            RegisteredApplicationContextDto? dto = JsonSerializer.Deserialize(
                bytes,
                RegisteredApplicationContextJsonContext.Default.RegisteredApplicationContextDto);
            if (dto is null)
            {
                throw new InvalidDataException(
                    $"File '{path}' did not yield a RegisteredApplicationContext payload.");
            }
            return ToRecord(dto);
        }
        catch (JsonException)
        {
            return ToRecord(LoadLegacyXmlDto(bytes, path));
        }
    }

    /// <summary>
    /// Parses the legacy XmlSerializer-flavoured XML format directly via
    /// <see cref="XDocument"/>. The element names mirror the previous
    /// <c>[XmlRoot]</c> / <c>[XmlArray]</c> / <c>[XmlArrayItem]</c>
    /// decorations that have since been dropped from the DTO; using
    /// <see cref="XDocument"/> here keeps the fallback trim- and AOT-safe
    /// (XmlSerializer requires public types and emits reflection-heavy
    /// dynamic code that is incompatible with the Lens NativeAOT build).
    /// </summary>
    private static RegisteredApplicationContextDto LoadLegacyXmlDto(byte[] bytes, string path)
    {
        XDocument doc;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            doc = XDocument.Load(ms);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            throw new InvalidDataException(
                $"File '{path}' is not a valid RegisteredApplicationContext JSON or XML document.",
                ex);
        }

        XElement? root = doc.Root;
        if (root is null
            || !string.Equals(root.Name.LocalName, "RegisteredApplicationContext", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"File '{path}' is not a RegisteredApplicationContext document.");
        }

        var dto = new RegisteredApplicationContextDto
        {
            ApplicationId = ElementValue(root, "ApplicationId"),
            ApplicationUri = ElementValue(root, "ApplicationUri") ?? string.Empty,
            ApplicationName = ElementValue(root, "ApplicationName") ?? string.Empty,
            ProductUri = ElementValue(root, "ProductUri") ?? string.Empty,
            RegistrationType = ElementValue(root, "RegistrationType") ?? "ClientPull",
            Domains = ElementValue(root, "Domains"),
            CertificateStorePath = ElementValue(root, "CertificateStorePath"),
            CertificateSubjectName = ElementValue(root, "CertificateSubjectName"),
            CertificatePublicKeyPath = ElementValue(root, "CertificatePublicKeyPath"),
            CertificatePrivateKeyPath = ElementValue(root, "CertificatePrivateKeyPath"),
            TrustListStorePath = ElementValue(root, "TrustListStorePath"),
            IssuerListStorePath = ElementValue(root, "IssuerListStorePath"),
            HttpsCertificatePublicKeyPath = ElementValue(root, "HttpsCertificatePublicKeyPath"),
            HttpsCertificatePrivateKeyPath = ElementValue(root, "HttpsCertificatePrivateKeyPath"),
            HttpsTrustListStorePath = ElementValue(root, "HttpsTrustListStorePath"),
            HttpsIssuerListStorePath = ElementValue(root, "HttpsIssuerListStorePath"),
            PushEndpointUrl = ElementValue(root, "PushEndpointUrl"),
            PushEndpointSecurityMode = ElementValue(root, "PushEndpointSecurityMode"),
            PushEndpointSecurityPolicyUri = ElementValue(root, "PushEndpointSecurityPolicyUri")
        };

        CollectStringList(root, "DiscoveryUrls", "Url", dto.DiscoveryUrls);
        CollectStringList(root, "ServerCapabilities", "Capability", dto.ServerCapabilities);

        return dto;
    }

    private static string? ElementValue(XElement root, string localName)
    {
        XElement? el = root.Element(localName);
        return el is null ? null : el.Value;
    }

    private static void CollectStringList(XElement root, string container, string item, List<string> sink)
    {
        XElement? containerEl = root.Element(container);
        if (containerEl is null)
        {
            return;
        }
        foreach (XElement child in containerEl.Elements(item))
        {
            string value = child.Value;
            if (!string.IsNullOrEmpty(value))
            {
                sink.Add(value);
            }
        }
    }
}
