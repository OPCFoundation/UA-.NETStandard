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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.Hosting;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Http;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Connector
{
    public static partial class XRegistryConnectorHost
    {
        private static void Configure(
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
            services.AddSingleton(nativeOptions);
            IOpcUaBuilder opcua = services.AddOpcUa()
                .ConfigureApplication(options =>
                {
                    options.ApplicationName = "XRegistryConnector";
                    options.ApplicationUri = configuration["ApplicationUri"];
                    options.ProductUri = "urn:opcfoundation:xregistry:connector";
                    options.PkiRoot = configuration["PkiRoot"] ??
                        Path.Combine(
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
                    new XRegistryHttpOptions
                    {
                        AllowLoopbackHttp = settings.AllowLoopbackHttp,
                        IsQualifiedBinding = ReadBoolean(profile, "Http:IsQualifiedBinding", false),
                        MaximumBodyBytes = ReadPositiveInteger(profile, "Http:MaximumBodyBytes", 33_554_432),
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
                opcua.AddServer(options =>
                {
                    options.EndpointUrls.Add(settings.ListenAddress!.AbsoluteUri);
                    options.IncludeUnsecurePolicyNone = false;
                    options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Certificate });
                })
                    .AddDefaultIdentityAuthenticators(options =>
                    {
                        options.EnableAnonymous = false;
                        options.EnableUserNamePassword = false;
                        options.EnableX509 = true;
                        options.EnableJwt = false;
                    })
                    .AddNodeManager<XRegistryBridgeNodeManagerFactory>();
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
            string? value = configuration[name];
            return value is null ? fallback :
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                    ? parsed : throw new ArgumentException($"Configuration '{name}' must be a positive integer.");
        }
    }
}
