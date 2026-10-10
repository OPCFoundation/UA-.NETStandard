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
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.XRegistry.Bridge;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Http;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Connector
{
    public static partial class XRegistryConnectorHost
    {
        internal static void Configure(
            IServiceCollection services,
            ConfigurationManager configuration,
            ILoggingBuilder logging,
            XRegistryConnectorSettings settings)
        {
            if (settings.ConfigurationFile is not null)
            {
                configuration.AddJsonFile(
                    Path.GetFullPath(settings.ConfigurationFile), optional: false, reloadOnChange: false);
            }
            configuration.AddEnvironmentVariables("XREGISTRY_");
            logging.ClearProviders();
            logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            logging.SetMinimumLevel(LogLevel.Warning);
            services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));

            IConfiguration profile = configuration.GetSection("Profiles").GetSection(settings.CredentialProfile);
            if (settings.CredentialProfile != "default" && !profile.GetChildren().Any())
            {
                throw new ArgumentException("The named operator profile is not configured.");
            }
            services.AddSingleton<ISecretRegistry>(_ =>
                new SecretRegistry(new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"))));
            services.AddSingleton<XRegistryConnectorNativeIdentity>();
            XRegistryCallContext operatorContext = OperatorContext(settings.CredentialProfile);
            var subjects = new HashSet<string>(
                configuration.GetSection("NativeGateway:AllowedSubjects").GetChildren()
                    .Select(entry => entry.Value ?? throw new ArgumentException("A native subject cannot be null.")),
                StringComparer.Ordinal);
            var nativeOptions = new XRegistryBridgeNativeOptions
            {
                ProjectionContext = operatorContext,
                AuthorizeCallerAsync = (context, _, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ValueTask<bool>(IsAllowedNativeCaller(context, subjects));
                },
                ContextFactory = context =>
                {
                    var session = context as ISessionSystemContext;
                    string? sessionId = session?.SessionId.ToString();
                    return IsAllowedNativeCaller(context, subjects)
                        ? operatorContext with { SessionId = sessionId }
                        : XRegistryCallContext.Anonymous with { SessionId = sessionId };
                }
            };
            nativeOptions = nativeOptions with
            {
                SpoolDirectory = configuration["NativeGateway:SpoolDirectory"]
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OPC Foundation", "XRegistryConnector", "spool"),
                MemoryBufferThreshold = ReadInteger(configuration, "NativeGateway:MemoryBufferThreshold",
                    nativeOptions.MemoryBufferThreshold, 0),
                MaxSpoolBytes =
                    ReadPositiveLong(configuration, "NativeGateway:MaxSpoolBytes", nativeOptions.MaxSpoolBytes),
                PreparedOperationTimeout = ReadTimeout(configuration, "NativeGateway:PreparedOperationTimeout",
                    nativeOptions.PreparedOperationTimeout),
                CleanupTimeout =
                    ReadTimeout(configuration, "NativeGateway:CleanupTimeout", nativeOptions.CleanupTimeout),
                AttributeMappings = ReadAttributeMappings(configuration),
                BasePropertyNamespaceUris = [.. configuration.GetSection("NativeGateway:BasePropertyNamespaceUris")
                    .GetChildren().Select(part => part.Value ??
                        throw new ArgumentException("A base-property namespace URI is missing."))],
                MaxMappedProperties = ReadPositiveInteger(
                    configuration, "NativeGateway:MaxMappedProperties", nativeOptions.MaxMappedProperties)
            };
            nativeOptions.Validate();
            services.AddSingleton(provider => nativeOptions with
            {
                AuthorizeCallerAsync = async (context, _, cancellationToken) =>
                {
                    if (!IsAllowedNativeCaller(context, subjects) ||
                        context is not ISessionSystemContext { UserIdentity: { } identity })
                    {
                        return false;
                    }
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(nativeOptions.PreparedOperationTimeout);
                    if (!await provider.GetRequiredService<XRegistryConnectorNativeIdentity>()
                        .IsCurrentAsync(identity, deadline.Token).ConfigureAwait(false))
                    {
                        return false;
                    }
                    if (identity.TokenType == UserTokenType.Certificate)
                    {
                        if (identity.TokenHandler is not X509IdentityTokenHandler token ||
                            token.Token is not X509IdentityToken wire ||
                            wire.CertificateData.IsEmpty)
                        {
                            return false;
                        }
                        ApplicationConfiguration current = await provider
                            .GetRequiredService<IOpcUaApplicationConfigurationProvider>().GetAsync(deadline.Token)
                            .ConfigureAwait(false);
                        if (current.CertificateManager is not ICertificateValidatorEx validator)
                        {
                            throw new InvalidOperationException(
                                "Native certificate authorization requires a managed validator.");
                        }
                        using var certificate = Certificate.FromRawData(wire.CertificateData);
                        CertificateValidationResult result = await validator.ValidateAsync(
                            certificate, TrustListIdentifier.Users, deadline.Token).ConfigureAwait(false);
                        return result.IsValid;
                    }
                    return true;
                }
            });
            IOpcUaBuilder opcua = services.AddOpcUa()
                .ConfigureApplication(options =>
                {
                    options.ApplicationName = "XRegistryConnector";
                    options.ApplicationUri = configuration["ApplicationUri"];
                    options.ProductUri = "urn:opcfoundation:xregistry:connector";
                    options.PkiRoot = configuration["PkiRoot"]
                        ?? Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "OPC Foundation", "XRegistryConnector", settings.CredentialProfile, "pki");
                    options.AutoAcceptUntrustedCertificates = false;
                    options.RejectSHA1SignedCertificates = true;
                    options.MinimumCertificateKeySize = 2048;
                })
                .AddOpcTcpTransport();
            if (settings.OpcUaEndpoint is not null)
            {
                opcua.AddClient(profile.GetSection("OpcUa"))
                    .AddIdentityProvider(profile.GetSection("OpcUa:Identity"))
                    .AddDiscoveryAndConnect(options =>
                    {
                        options.DiscoveryUrl = settings.OpcUaEndpoint.AbsoluteUri;
                        options.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                        options.SecurityPolicyUri =
                            profile["OpcUa:SecurityPolicyUri"] ?? SecurityPolicies.Basic256Sha256;
                    });
            }
            if (settings.HttpRoot is not null)
            {
                var httpOptions = new XRegistryHttpOptions
                {
                    AllowLoopbackHttp = settings.AllowLoopbackHttp,
                    IsQualifiedBinding = ReadBoolean(profile, "Http:IsQualifiedBinding", false),
                    MaximumBodyBytes = ReadPositiveInteger(profile, "Http:MaximumBodyBytes", 33_554_432),
                    ShortLinkPrefix = profile["Http:ShortLinkPrefix"]
                };
                httpOptions.Validate();
                string? secretName = profile["Http:BearerSecret"];
                if (secretName is not null && settings.HttpRoot.Scheme != Uri.UriSchemeHttps)
                {
                    throw new ArgumentException("HTTP operator credentials require HTTPS, including on loopback.");
                }
                services.AddHttpClient("xregistry-operator", client => client.Timeout = Timeout.InfiniteTimeSpan)
                    .ConfigurePrimaryHttpMessageHandler(provider =>
                    {
                        var transport = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
                        return secretName is null ? transport : new XRegistryBearerHandler(
                            provider.GetRequiredService<ISecretRegistry>(),
                            new SecretIdentifier(secretName, profile["Http:SecretStoreType"] ?? "Environment"),
                            settings.HttpRoot, transport);
                    });
                services.AddSingleton(provider => new XRegistryHttpEndpoint(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("xregistry-operator"),
                    settings.HttpRoot,
                    httpOptions with
                    {
                        Telemetry = provider.GetRequiredService<ITelemetryContext>()
                    }));
            }
            if (settings.Command == XRegistryConnectorCommand.OpcUaGateway)
            {
                if (subjects.Count == 0)
                {
                    throw new ArgumentException(
                        "NativeGateway:AllowedSubjects must explicitly authorize at least one native user identity.");
                }
                services.AddSingleton<IXRegistryEndpoint>(provider =>
                    provider.GetRequiredService<XRegistryHttpEndpoint>());
                IConfigurationSection[] users = [.. configuration.GetSection("NativeGateway:Users").GetChildren()];
                var userNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (IConfigurationSection user in users)
                {
                    if (string.IsNullOrWhiteSpace(user["UserName"]) ||
                        !userNames.Add(user["UserName"]!) ||
                        string.IsNullOrWhiteSpace(user["PasswordSecret"]))
                    {
                        throw new ArgumentException(
                            "NativeGateway:Users requires distinct UserName and PasswordSecret references.");
                    }
                    _ = ReadBoolean(user, "Enabled", true);
                }
                IConfigurationSection[] issuers = [.. configuration.GetSection("NativeGateway:Issuers").GetChildren()];
                var readiness = new NativeReadiness();
                services.AddSingleton(readiness);
                IOpcUaServerBuilder server = opcua.AddServer(options =>
                {
                    options.EndpointUrls.Add(settings.ListenAddress!.AbsoluteUri);
                    options.IncludeUnsecurePolicyNone = false;
                    options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Certificate });
                    if (users.Length != 0)
                    {
                        options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                    }
                    if (issuers.Length != 0)
                    {
                        options.UserTokenPolicies.Add(
                            new OpcUaUserTokenPolicy { TokenType = UserTokenType.IssuedToken });
                    }
                })
                    .AddDefaultIdentityAuthenticators(options =>
                    {
                        options.EnableAnonymous = false;
                        options.EnableUserNamePassword = false;
                        options.EnableX509 = true;
                        options.EnableJwt = issuers.Length != 0;
                    })
                    .AddNodeManager<XRegistryBridgeNodeManagerFactory>()
                    .AddStartupTask((_, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        readiness.MarkStarted();
                        return default;
                    });
                if (users.Length != 0)
                {
                    server.AddIdentityAuthenticator<UserNamePasswordAuthenticator>();
                    services.Replace(ServiceDescriptor.Singleton(provider => new UserNamePasswordAuthenticator(
                        provider.GetRequiredService<XRegistryConnectorNativeIdentity>().VerifyUserAsync)));
                }
                foreach (IConfigurationSection issuer in issuers)
                {
                    if (string.IsNullOrWhiteSpace(issuer["Audience"]) ||
                        !Uri.TryCreate(issuer["JwksUri"], UriKind.Absolute, out Uri? jwks) ||
                        jwks.Scheme != Uri.UriSchemeHttps ||
                        jwks.UserInfo.Length != 0)
                    {
                        throw new ArgumentException(
                            "Each native JWT issuer requires an Audience and credential-free HTTPS JwksUri.");
                    }
                    server.AddJwtIssuer(issuer);
                }
                services.AddSingleton<IXRegistryBridgeProjection>(provider =>
                    provider.GetRequiredService<XRegistryBridgeNodeManagerFactory>());
            }
        }

        private static bool IsAllowedNativeCaller(ISystemContext context, HashSet<string> subjects)
        {
            return context is ISessionSystemContext { UserIdentity: { } identity } &&
                identity.TokenType != UserTokenType.Anonymous &&
                subjects.Contains(identity.DisplayName);
        }

        private static XRegistryCallContext OperatorContext(string profile)
        {
            return new XRegistryCallContext("operator:" + profile)
            {
                Authority = "xregistry-connector",
                IsAuthenticated = true,
                Roles = ["xregistry.write"]
            };
        }

        private static bool ReadBoolean(IConfiguration configuration, string name, bool fallback)
        {
            string? value = configuration[name];
            return value is null ? fallback : bool.TryParse(value, out bool parsed) ? parsed :
                throw new ArgumentException($"Configuration '{name}' must be true or false.");
        }

        private static int ReadPositiveInteger(IConfiguration configuration, string name, int fallback)
        {
            return ReadInteger(configuration, name, fallback, 1);
        }

        private static int ReadInteger(IConfiguration configuration, string name, int fallback, int minimum)
        {
            string? value = configuration[name];
            return value is null ? fallback :
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
                parsed >= minimum
                    ? parsed : throw new ArgumentException($"Configuration '{name}' must be an integer >= {minimum}.");
        }

        private static long ReadPositiveLong(ConfigurationManager configuration, string name, long fallback)
        {
            string? value = configuration[name];
            return value is null ? fallback :
                long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) && parsed > 0
                    ? parsed : throw new ArgumentException($"Configuration '{name}' must be a positive integer.");
        }

        private static ArrayOf<XRegistryVersionCorrespondence> ReadVersionCorrespondences(IConfiguration configuration)
        {
            return [.. configuration.GetSection("Sync:VersionCorrespondences").GetChildren().Select(section =>
                new XRegistryVersionCorrespondence(
                    section["CanonicalPath"] ?? throw new ArgumentException("A canonical Version path is required."),
                    section["OpcUaPath"] ?? throw new ArgumentException("An OPC UA Version path is required."),
                    section["HttpPath"] ?? throw new ArgumentException("An HTTP Version path is required.")))];
        }

        private static ArrayOf<XRegistryNativeAttributeMapping> ReadAttributeMappings(
            ConfigurationManager configuration)
        {
            return [.. configuration.GetSection("NativeGateway:AttributeMappings").GetChildren().Select(section =>
                new XRegistryNativeAttributeMapping(
                    section["ModelPath"] ?? throw new ArgumentException("Native mappings require ModelPath."),
                    ReadMappingEnum<XRegistryNativeAttributeScope>(section, "Scope"),
                    [.. section.GetSection("AttributePath").GetChildren().Select(part =>
                        part.Value ?? throw new ArgumentException("A logical attribute path segment is missing."))],
                    [.. section.GetSection("BrowsePath").GetChildren().Select(part => new XRegistryNativeBrowseName(
                        part["NamespaceUri"] ?? throw new ArgumentException("A native path requires NamespaceUri."),
                        part["Name"] ?? throw new ArgumentException("A native path requires Name.")))])
                {
                    Encoding = ReadMappingEnum<XRegistryNativeAttributeEncoding>(section, "Encoding",
                        XRegistryNativeAttributeEncoding.Typed),
                    NativeType = ReadMappingEnum<BuiltInType>(section, "NativeType", BuiltInType.Null),
                    Writable = ReadBoolean(section, "Writable", false)
                })];
        }

        private static TEnum ReadMappingEnum<TEnum>(IConfiguration configuration, string name, TEnum? fallback = null)
            where TEnum : struct, Enum
        {
            string? value = configuration[name];
            if (value is null && fallback.HasValue)
            {
                return fallback.Value;
            }
            return Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed)
                ? parsed : throw new ArgumentException($"Mapping '{name}' requires a declared enum value.");
        }

        private static TimeSpan ReadTimeout(ConfigurationManager configuration, string name, TimeSpan fallback)
        {
            string? value = configuration[name];
            return value is null ? fallback :
                TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed) &&
                parsed > TimeSpan.Zero &&
                parsed.TotalMilliseconds <= uint.MaxValue - 1
                    ? parsed : throw new ArgumentException(
                        $"Configuration '{name}' must be a positive duration no larger than 4294967294 milliseconds.");
        }
    }
}
