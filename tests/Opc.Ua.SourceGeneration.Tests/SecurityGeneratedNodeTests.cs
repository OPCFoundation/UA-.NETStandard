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
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Compiles and instantiates generated factories against the real stack rather than permissive test stubs.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [NonParallelizable]
    public sealed class SecurityGeneratedNodeTests
    {
        /// <summary>
        /// A narrower nonzero user mask denies escalation while legitimate attribute and value writes still work.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF33GeneratedUserMaskCannotEscalateValueWrite(bool referencedModel)
        {
            await RunProbeAsync("AttributeWrites", referencedModel).ConfigureAwait(false);
        }

        /// <summary>
        /// All four executable combinations reach the real method Call guard with a wired handler.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF34GeneratedDeniedMethodNeverInvokesHandler(bool referencedModel)
        {
            await RunProbeAsync("MethodCalls", referencedModel).ConfigureAwait(false);
        }

        /// <summary>
        /// Explicit restrictions on overrides remain narrower than the inherited declaration.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF33F34InheritedOverridesRetainDenials(bool referencedModel)
        {
            await RunProbeAsync("InheritedOverrides", referencedModel).ConfigureAwait(false);
        }

        /// <summary>
        /// Each distinct method factory template retains an explicit user denial.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF34OptionalPlaceholderAndStandaloneFactoriesRetainDenial()
        {
            await RunProbeAsync("OtherMethodFactories", referencedModel: false).ConfigureAwait(false);
        }

        /// <summary>
        /// The actual server role evaluator denies outsiders and permits each undeclared runtime role.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF35UnknownRolesCannotEraseGeneratedAcl(bool referencedModel)
        {
            await RunProbeAsync("RolePermissions", referencedModel).ConfigureAwait(false);
        }

        /// <summary>
        /// A genuinely unrepresentable symbolic role fails generation instead of emitting an open factory.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF35UnresolvableSymbolicRoleReportsStandardDiagnostic()
        {
            const string design =
                """
                <opc:ModelDesign xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                    xmlns:ua="http://opcfoundation.org/UA/" xmlns="urn:test:bad-security"
                    TargetNamespace="urn:test:bad-security" TargetXmlNamespace="urn:test:bad-security:types">
                  <opc:Namespaces>
                    <opc:Namespace Name="BadSecurity" Prefix="BadSecurity"
                        XmlNamespace="urn:test:bad-security:types"
                        XmlPrefix="BadSecurity">urn:test:bad-security</opc:Namespace>
                    <opc:Namespace Name="OpcUa" Prefix="Opc.Ua"
                        XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd"
                        XmlPrefix="OpcUa">http://opcfoundation.org/UA/</opc:Namespace>
                  </opc:Namespaces>
                  <opc:Object SymbolicName="Protected" TypeDefinition="ua:BaseObjectType" NumericId="5000">
                    <opc:RolePermissions>
                      <opc:RolePermission Role="MissingRole"><opc:Permission>Read</opc:Permission></opc:RolePermission>
                    </opc:RolePermissions>
                  </opc:Object>
                </opc:ModelDesign>
                """;
            GeneratorDriver driver = CreateDriver(EmbeddedText.Create("BadSecurity.xml", design));
            driver = driver.RunGeneratorsAndUpdateCompilation(
                ModelDependencyScannerTests.CreateStackCompilation("P19BadSecurity"),
                out _,
                out ImmutableArray<Diagnostic> diagnostics);
            Diagnostic error = diagnostics.Single(diagnostic => diagnostic.Id == "MODELGEN003");

            Assert.Multiple(() =>
            {
                Assert.That(error.Severity, Is.EqualTo(DiagnosticSeverity.Error));
                Assert.That(
                    error.GetMessage(CultureInfo.InvariantCulture),
                    Does.Contain("RolePermissions").And.Contain("MissingRole"));
                Assert.That(driver.GetRunResult().Results[0].GeneratedSources, Is.Empty);
            });
        }

        /// <summary>
        /// Runs an emitted probe with normal stack dependencies and removes the test-owned assembly resolver.
        /// </summary>
        private static async Task RunProbeAsync(string methodName, bool referencedModel)
        {
            GeneratedModels models = s_models.Value;
            CSharpCompilation compilation = referencedModel ? models.Consumer : models.Local;
            var references = compilation.References
                .OfType<PortableExecutableReference>()
                .Where(reference => reference.FilePath != null)
                .GroupBy(reference => Path.GetFileNameWithoutExtension(reference.FilePath)!,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().FilePath!, StringComparer.OrdinalIgnoreCase);
            Assembly? producer = null;
            Assembly? resolver(object? _, ResolveEventArgs args)
            {
                string name = new AssemblyName(args.Name).Name!;
                if (name == models.ProducerName)
                {
                    return producer ??= Assembly.Load(models.ProducerImage);
                }
                return references.TryGetValue(name, out string? path) ? Assembly.LoadFrom(path) : null;
            }
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            try
            {
                var assembly = Assembly.Load(Emit(compilation));
                MethodInfo method = assembly.GetType("SecurityProbe")!.GetMethod(methodName)!;
                var operation = (Task)method.Invoke(null, null)!;
                await operation.ConfigureAwait(false);
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            }
        }

        /// <summary>
        /// Generates a local model and a consumer that has only its referenced producer's dependency payload.
        /// </summary>
        private static GeneratedModels CreateModels()
        {
            CSharpCompilation producer = Generate(
                CreateRuntimeCompilation("P19SecurityProducer"),
                EmbeddedText.From("SecurityAttributes.NodeSet2.xml"));
            byte[] image = Emit(producer);
            CSharpCompilation local = producer
                .WithAssemblyName("P19SecurityLocal")
                .AddSyntaxTrees(ParseProbe(
                    "context.CreateInstanceOfSecurityDeviceType()",
                    "context.CreateInstanceOfOverrideDeviceType()"));
            CSharpCompilation consumer = Generate(
                CreateRuntimeCompilation("P19SecurityConsumer")
                    .AddReferences(MetadataReference.CreateFromImage(image)),
                EmbeddedText.From("SecurityAttributesConsumer.NodeSet2.xml"))
                .AddSyntaxTrees(ParseProbe(
                    "context.CreateConsumerDevice()",
                    "context.CreateConsumerOverride()",
                    consumer: true));
            return new GeneratedModels(local, consumer, producer.AssemblyName!, image);
        }

        /// <summary>
        /// Adds the assertion assembly explicitly on frameworks without a trusted-platform assembly list.
        /// </summary>
        private static CSharpCompilation CreateRuntimeCompilation(string assemblyName)
        {
            CSharpCompilation compilation = ModelDependencyScannerTests.CreateStackCompilation(assemblyName);
            string assertions = typeof(Assert).Assembly.Location;
            if (!compilation.References.Any(reference =>
                string.Equals(reference.Display, assertions, StringComparison.OrdinalIgnoreCase)))
            {
                compilation = compilation.AddReferences(MetadataReference.CreateFromFile(assertions));
            }
            return compilation;
        }

        /// <summary>
        /// Parses identical behavioral probes for the two model-loading paths.
        /// </summary>
        private static SyntaxTree ParseProbe(string factory, string overrideFactory, bool consumer = false)
        {
            return CSharpSyntaxTree.ParseText(
                kProbe.Replace("__FACTORY__", factory, StringComparison.Ordinal)
                    .Replace("__OVERRIDE_FACTORY__", overrideFactory, StringComparison.Ordinal)
                    .Replace("__CONSUMER_IMPORT__", consumer ? "using SecurityConsumer;" : string.Empty,
                        StringComparison.Ordinal),
                new CSharpParseOptions(LanguageVersion.CSharp13));
        }

        /// <summary>
        /// Uses the normal source generator pipeline with deterministic namespace identities.
        /// </summary>
        private static CSharpCompilation Generate(CSharpCompilation compilation, AdditionalText text)
        {
            CreateDriver(text).RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation output,
                out ImmutableArray<Diagnostic> diagnostics);
            Assert.That(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty, string.Join(Environment.NewLine, diagnostics));
            return (CSharpCompilation)output;
        }

        /// <summary>
        /// Supplies only model metadata; all security metadata comes from the input model or dependency payload.
        /// </summary>
        private static GeneratorDriver CreateDriver(AdditionalText text)
        {
            var options = new AnalyzerOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true"
            });
            bool consumer = Path.GetFileName(text.Path) == "SecurityAttributesConsumer.NodeSet2.xml";
            options.TextOptions[text.Path] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] =
                    consumer ? "SecurityConsumer" : "SecurityNodes",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] =
                    consumer ? "SecurityConsumer" : "SecurityNodes"
            };
            return CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.CSharp13))
                .WithUpdatedAnalyzerConfigOptions(options)
                .AddAdditionalTexts([text]);
        }

        /// <summary>
        /// Emits the actual generated code, checking compiler diagnostics rather than source fragments.
        /// </summary>
        private static byte[] Emit(CSharpCompilation compilation)
        {
            using var stream = new MemoryStream();
            EmitResult result = compilation.Emit(stream);
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
            return stream.ToArray();
        }

        /// <summary>
        /// Keeps the producer's identity and image together with its two independently compiled consumers.
        /// </summary>
        private sealed record GeneratedModels(
            CSharpCompilation Local,
            CSharpCompilation Consumer,
            string ProducerName,
            byte[] ProducerImage);

        private static readonly Lazy<GeneratedModels> s_models = new(CreateModels);

        private const string kProbe =
            """
            using System;
            using System.Collections.Generic;
            using System.Reflection;
            using System.Threading.Tasks;
            using Microsoft.Extensions.Logging;
            using NUnit.Framework;
            using Opc.Ua;
            using Opc.Ua.Server;
            using SecurityNodes;
            __CONSUMER_IMPORT__

            public static class SecurityProbe
            {
                public static async Task AttributeWrites()
                {
                    SystemContext context = CreateContext();
                    BaseObjectState root = CreateRoot(context);
                    BaseVariableState restricted = Variable(context, root, "Restricted");
                    ServiceResult result = await restricted.WriteAttributeAsync(context, Attributes.UserAccessLevel,
                        default, new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(restricted.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                    result = await restricted.WriteAttributeAsync(context, Attributes.Value,
                        default, new DataValue(new Variant(99)));
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(restricted.WrappedValue.GetInt32(), Is.EqualTo(7));
                    result = await restricted.WriteAttributeAsync(context, Attributes.DisplayName,
                        default, new DataValue(new Variant(new LocalizedText("Permitted rename"))));
                    Assert.That(ServiceResult.IsGood(result), Is.True);
                    Assert.That(restricted.DisplayName.Text, Is.EqualTo("Permitted rename"));
                    foreach (string name in new[] { "Permitted", "LegacyMask", "ZeroMask" })
                    {
                        BaseVariableState permitted = Variable(context, root, name);
                        result = await permitted.WriteAttributeAsync(context, Attributes.UserAccessLevel,
                            default, new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
                        Assert.That(ServiceResult.IsGood(result), Is.True, name);
                        result = await permitted.WriteAttributeAsync(context, Attributes.Value,
                            default, new DataValue(new Variant(42)));
                        Assert.That(ServiceResult.IsGood(result), Is.True, name);
                        Assert.That(permitted.WrappedValue.GetInt32(), Is.EqualTo(42), name);
                    }
                }

                public static async Task MethodCalls()
                {
                    SystemContext context = CreateContext();
                    BaseObjectState root = CreateRoot(context);
                    await CheckCall(
                        context, root, Method(context, root, "UserDenied"), StatusCodes.BadUserAccessDenied);
                    await CheckCall(context, root, Method(context, root, "Allowed"), StatusCodes.Good);
                    await CheckCall(context, root, Method(context, root, "ServerDenied"), StatusCodes.BadNotExecutable);
                    await CheckCall(context, root, Method(context, root, "FullyDenied"), StatusCodes.BadNotExecutable);
                }

                public static async Task InheritedOverrides()
                {
                    SystemContext context = CreateContext();
                    BaseObjectState root = __OVERRIDE_FACTORY__;
                    BaseVariableState restricted = Variable(context, root, "Permitted");
                    ServiceResult result = await restricted.WriteAttributeAsync(context, Attributes.UserAccessLevel,
                        default, new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    result = await restricted.WriteAttributeAsync(context, Attributes.Value,
                        default, new DataValue(new Variant(99)));
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(restricted.WrappedValue.GetInt32(), Is.EqualTo(7));
                    await CheckCall(context, root, Method(context, root, "Allowed"), StatusCodes.BadUserAccessDenied);
                    await CheckCall(
                        context, root, Method(context, root, "UserDenied"), StatusCodes.BadUserAccessDenied);
                }

                public static async Task OtherMethodFactories()
                {
                    SystemContext context = CreateContext();
                    BaseObjectState root = CreateRoot(context);
                    ushort index = context.NamespaceUris.GetIndexOrAppend("urn:test:security-nodes");
                    MethodInfo optionalFactory = typeof(SecurityNodesExtensions).GetMethod(
                        "CreateSecurityDeviceType_OptionalDenied", BindingFlags.Static | BindingFlags.NonPublic);
                    MethodInfo placeholderFactory = typeof(SecurityNodesExtensions).GetMethod(
                        "CreateSecurityDeviceType_Denied_Placeholder", BindingFlags.Static | BindingFlags.NonPublic);
                    Assert.That(optionalFactory, Is.Not.Null);
                    Assert.That(placeholderFactory, Is.Not.Null);
                    var optional = (MethodState)optionalFactory.Invoke(null, new object[] { context, root, true });
                    var placeholder = (MethodState)placeholderFactory.Invoke(null, new object[]
                    {
                        context, root, new QualifiedName("RuntimeDenied", index), true
                    });
                    MethodInfo standaloneFactory = typeof(SecurityNodesExtensions).GetMethod(
                        "CreateInstanceOfStandaloneDeniedMethodType", BindingFlags.Static | BindingFlags.Public);
                    Assert.That(standaloneFactory, Is.Not.Null);
                    var standalone = (MethodState)standaloneFactory.Invoke(null, new object[]
                    {
                        context, root, QualifiedName.Null
                    });
                    await CheckCall(context, root, optional, StatusCodes.BadUserAccessDenied);
                    await CheckCall(context, root, placeholder, StatusCodes.BadUserAccessDenied);
                    await CheckCall(context, root, standalone, StatusCodes.BadUserAccessDenied);
                }

                public static async Task RolePermissions()
                {
                    SystemContext context = CreateContext();
                    BaseObjectState root = CreateRoot(context);
                    BaseVariableState variable = Variable(context, root, "RoleProtected");
                    BaseVariableState runtimeOnly = Variable(context, root, "RuntimeOnly");
                    Assert.That(variable.RolePermissions.Count, Is.EqualTo(9));
                    Assert.That(runtimeOnly.RolePermissions.Count, Is.EqualTo(1));
                    using (OperationContext outsider = Actor(context, Role.Anonymous))
                    {
                        Assert.That(Authorize(outsider, variable, PermissionType.Read).StatusCode,
                            Is.EqualTo(StatusCodes.BadUserAccessDenied));
                        Assert.That(Authorize(outsider, runtimeOnly, PermissionType.Read).StatusCode,
                            Is.EqualTo(StatusCodes.BadUserAccessDenied));
                        Assert.That(ServiceResult.IsGood(Authorize(
                            outsider, Variable(context, root, "LegacyMask"), PermissionType.Read)), Is.True);
                        Assert.That(ServiceResult.IsGood(Authorize(
                            outsider, Variable(context, root, "EmptyAcl"), PermissionType.Read)), Is.True);
                    }
                    using (OperationContext observer = Actor(context, Role.Observer))
                    {
                        Assert.That(
                            ServiceResult.IsGood(Authorize(observer, variable, PermissionType.Browse)), Is.True);
                        Assert.That(Authorize(observer, variable, PermissionType.Read).StatusCode,
                            Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    }
                    ushort roleIndex = context.NamespaceUris.GetIndexOrAppend("urn:test:runtime-roles");
                    NodeId[] roleIds =
                    {
                        new NodeId(7001u, roleIndex),
                        new NodeId("Runtime.Readers", roleIndex),
                        new NodeId(new Guid("09087e75-8e5e-499b-954f-f2a9603db28a"), roleIndex),
                        new NodeId(ByteString.FromBase64("AQID"), roleIndex),
                        new NodeId(0u, roleIndex),
                        new NodeId(string.Empty, roleIndex),
                        new NodeId(Guid.Empty, roleIndex),
                        new NodeId(ByteString.Empty, roleIndex)
                    };
                    foreach (NodeId expected in roleIds)
                    {
                        Assert.That(expected.IsNull, Is.False);
                        Assert.That(
                            expected.NamespaceIndex, Is.Not.EqualTo(2), "The runtime table is deliberately rebased.");
                        bool found = false;
                        foreach (RolePermissionType permission in variable.RolePermissions)
                        {
                            found |= permission.RoleId == expected &&
                                permission.Permissions == (uint)PermissionType.Read;
                        }
                        Assert.That(found, Is.True, expected.ToString());
                        using (OperationContext reader = Actor(context, new Role(expected, expected.ToString())))
                        {
                            Assert.That(
                                ServiceResult.IsGood(Authorize(reader, variable, PermissionType.Read)), Is.True);
                            Assert.That(Authorize(reader, variable, PermissionType.Write).StatusCode,
                                Is.EqualTo(StatusCodes.BadUserAccessDenied));
                            bool soleReader = expected.TryGetValue(out uint numeric) && numeric == 7001;
                            Assert.That(Authorize(reader, runtimeOnly, PermissionType.Read).StatusCode,
                                Is.EqualTo(soleReader ? StatusCodes.Good : StatusCodes.BadUserAccessDenied));
                            var read = await variable.ReadAttributeAsync(
                                context, Attributes.Value, default, default, default);
                            Assert.That(ServiceResult.IsGood(read.Result), Is.True);
                            Assert.That(read.Value.WrappedValue.GetInt32(), Is.EqualTo(7));
                            if (soleReader)
                            {
                                read = await runtimeOnly.ReadAttributeAsync(
                                    context, Attributes.Value, default, default, default);
                                Assert.That(ServiceResult.IsGood(read.Result), Is.True);
                                Assert.That(read.Value.WrappedValue.GetInt32(), Is.EqualTo(7));
                            }
                        }
                    }
                }

                private static SystemContext CreateContext()
                {
                    var context = new SystemContext(null);
                    context.NamespaceUris = new NamespaceTable();
                    context.TypeTable = new TypeTable(context.NamespaceUris);
                    context.NamespaceUris.GetIndexOrAppend("urn:test:unrelated-a");
                    context.NamespaceUris.GetIndexOrAppend("urn:test:unrelated-b");
                    context.NamespaceUris.GetIndexOrAppend("urn:test:security-nodes");
                    context.NamespaceUris.GetIndexOrAppend("urn:test:security-consumer");
                    return context;
                }

                private static BaseObjectState CreateRoot(SystemContext context)
                {
                    return __FACTORY__;
                }

                private static BaseVariableState Variable(SystemContext context, NodeState root, string name)
                {
                    var variable = root.FindChild(context,
                        new QualifiedName(name, context.NamespaceUris.GetIndexOrAppend("urn:test:security-nodes")))
                        as BaseVariableState;
                    Assert.That(variable, Is.Not.Null, name);
                    return variable;
                }

                private static MethodState Method(SystemContext context, NodeState root, string name)
                {
                    var method = root.FindChild(context,
                        new QualifiedName(name, context.NamespaceUris.GetIndexOrAppend("urn:test:security-nodes")))
                        as MethodState;
                    Assert.That(method, Is.Not.Null, name);
                    return method;
                }

                private static async Task CheckCall(
                    SystemContext context, NodeState root, MethodState method, StatusCode expectedStatus)
                {
                    int callbacks = 0;
                    method.OnCallMethod2Async = (callContext, target, objectId, inputs, outputs, cancellationToken) =>
                    {
                        callbacks++;
                        outputs.Add(new Variant(42));
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    };
                    var errors = new List<ServiceResult>();
                    var outputs = new List<Variant>();
                    ServiceResult result = await method.CallAsync(context, root.NodeId, [], errors, outputs);
                    Assert.That(result.StatusCode, Is.EqualTo(expectedStatus), method.BrowseName.ToString());
                    Assert.That(callbacks, Is.EqualTo(expectedStatus == StatusCodes.Good ? 1 : 0));
                    Assert.That(errors, Is.Empty);
                    if (expectedStatus == StatusCodes.Good)
                    {
                        Assert.That(outputs.Count, Is.EqualTo(1));
                        Assert.That(outputs[0].GetInt32(), Is.EqualTo(42));
                    }
                    else
                    {
                        Assert.That(outputs, Is.Empty);
                    }
                }

                private static OperationContext Actor(SystemContext context, Role role)
                {
                    return new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None,
                        new RoleBasedIdentity(new UserIdentity(), new[] { role }, context.NamespaceUris));
                }

                private static ServiceResult Authorize(
                    OperationContext actor, NodeState node, PermissionType permission)
                {
                    var metadata = new NodeMetadata(node, node.NodeId)
                    {
                        NodeClass = node.NodeClass,
                        RolePermissions = node.RolePermissions
                    };
                    MethodInfo guard = typeof(MasterNodeManager).GetMethod("ValidateRolePermissions",
                        BindingFlags.Static | BindingFlags.NonPublic, null,
                        new[]
                        {
                            typeof(OperationContext), typeof(NodeMetadata), typeof(PermissionType), typeof(ILogger)
                        },
                        null);
                    Assert.That(guard, Is.Not.Null);
                    return (ServiceResult)guard.Invoke(null, new object[] { actor, metadata, permission, null });
                }
            }
            """;
    }
}
