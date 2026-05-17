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
using System.Xml;
using System.Xml.Serialization;
using Opc.Ua;

namespace UaLens.Plugins.Gds;

/// <summary>
/// Bridges the internal <see cref="RegisteredApplicationContext"/>
/// record with its public <see cref="RegisteredApplicationContextDto"/>
/// twin for XML save/load operations.
/// </summary>
/// <remarks>
/// All XML I/O goes through these helpers so the <see cref="XmlSerializer"/>
/// call site is localised and the dialog code doesn't need to deal with
/// stream / encoding plumbing. The DTO has to be public for
/// <see cref="XmlSerializer"/> to operate on it; this helper keeps the
/// internal record type out of any public surface.
/// </remarks>
internal static class RegisteredApplicationContextXml
{
    private static readonly XmlWriterSettings s_writerSettings = new()
    {
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\r\n",
        Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
    };

    /// <summary>Lowers the internal record to its public XML DTO.</summary>
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
    /// Serialises a context record to indented UTF-8 XML at <paramref name="path"/>,
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
        var serializer = new XmlSerializer(typeof(RegisteredApplicationContextDto));
        using FileStream fs = File.Create(path);
        using XmlWriter writer = XmlWriter.Create(fs, s_writerSettings);
        serializer.Serialize(writer, ToDto(record));
    }

    /// <summary>
    /// Deserialises a context record from XML at <paramref name="path"/>.
    /// Throws <see cref="InvalidDataException"/> when the file does not
    /// contain a recognisable <see cref="RegisteredApplicationContextDto"/>.
    /// </summary>
    public static RegisteredApplicationContext Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var serializer = new XmlSerializer(typeof(RegisteredApplicationContextDto));
        using FileStream fs = File.OpenRead(path);
        using XmlReader reader = XmlReader.Create(fs);
        object? raw = serializer.Deserialize(reader);
        if (raw is not RegisteredApplicationContextDto dto)
        {
            throw new InvalidDataException(
                $"File '{path}' is not a RegisteredApplicationContext XML document.");
        }
        return ToRecord(dto);
    }
}
