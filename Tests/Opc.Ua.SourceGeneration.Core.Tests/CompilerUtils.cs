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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
#if NETFRAMEWORK
using System.Runtime.Serialization;
#endif

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Concrete in memory Additional text
    /// </summary>
    public sealed class EmbeddedText : AdditionalText
    {
        /// <summary>
        /// Create text
        /// </summary>
        private EmbeddedText(string path, string content)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            m_text = SourceText.From(content);
        }

        /// <inheritdoc/>
        public override string Path { get; }

        /// <inheritdoc/>
        public override SourceText GetText(
            CancellationToken cancellationToken = default)
        {
            return m_text;
        }

        /// <summary>
        /// Create additional text
        /// </summary>
        /// <exception cref="FileNotFoundException"></exception>
        public static AdditionalText From(string resourceName)
        {
            Assembly assembly = typeof(CompilerUtils).Assembly;
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.EndsWith(resourceName, StringComparison.Ordinal))
                {
                    continue;
                }
                using Stream stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream);
                return new EmbeddedText(resourceName, reader.ReadToEnd());
            }
            throw new FileNotFoundException("Resource not found");
        }

        private readonly SourceText m_text;
    }

    public sealed class AnalyzerOptions : AnalyzerConfigOptions
    {
        /// <summary>
        /// Create from dictionary
        /// </summary>
        public AnalyzerOptions(Dictionary<string, string> options)
        {
            Options = options.ToDictionary(k => k.Key, v => v.Value, KeyComparer);
        }

        /// <inheritdoc/>
        public Dictionary<string, string> Options { get; }

        /// <inheritdoc/>
        public override IEnumerable<string> Keys => Options.Keys;

        /// <inheritdoc/>
        public override bool TryGetValue(string key, out string value)
        {
            return Options.TryGetValue(key, out value);
        }
    }

    public sealed class AnalyzerOptionsProvider : AnalyzerConfigOptionsProvider
    {
        /// <summary>
        /// Create provider
        /// </summary>
        public AnalyzerOptionsProvider(Dictionary<string, string> options)
        {
            GlobalOptions = new AnalyzerOptions(options);
        }

        /// <inheritdoc/>
        public override AnalyzerConfigOptions GlobalOptions { get; }

        /// <inheritdoc/>
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return SyntaxOptions
                .Select(f => f(tree))
                .Where(o => o != null)
                .Select(o => new AnalyzerOptions(o))
                .FirstOrDefault();
        }

        /// <inheritdoc/>
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return TextOptions
                .TryGetValue(textFile.Path, out Dictionary<string, string> result) ?
                    new AnalyzerOptions(result) :
                    null;
        }

        public Dictionary<string, Dictionary<string, string>> TextOptions { get; } = [];
        public List<Func<SyntaxTree, Dictionary<string, string>>> SyntaxOptions { get; } = [];
    }

    public static class CompilerUtils
    {
        public static LanguageVersion[] SupportedLanguageVersions =>
        [
            LanguageVersion.CSharp8,
#if TEST_ALL_LANG_VERSIONS
            LanguageVersion.CSharp9,
            LanguageVersion.CSharp10,
#endif
            LanguageVersion.CSharp11,
            LanguageVersion.CSharp12,
            LanguageVersion.CSharp13
         // LanguageVersion.CSharp14,
        ];

        public static OptimizationLevel[] SupportedOptimizationLevels =>
        [
            OptimizationLevel.Debug,
            OptimizationLevel.Release
        ];

        /// <summary>
        /// Get trusted platform assembly references
        /// </summary>
        public static IEnumerable<MetadataReference> TrustedReferences
        {
            get
            {
                string[] trustedAssembliesPaths = ((string)AppContext
                    .GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                    .Split(Path.PathSeparator);
                if (trustedAssembliesPaths != null)
                {
                    return trustedAssembliesPaths
                        .Select(p => MetadataReference.CreateFromFile(p));
                }
                return [];
            }
        }

        public static MetadataReference[] DefaultReferences
        {
            get
            {
                string assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
                string binPath = Path.GetDirectoryName(typeof(CompilerUtils).Assembly.Location);
                MetadataReference[] defaultReferences =
                [
                    MetadataReference.CreateFromFile(Path.Combine(binPath, "Opc.Ua.Types.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Core.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Linq.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Xml.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "netstandard.dll")),
                    MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
#if NETFRAMEWORK
                    MetadataReference.CreateFromFile(typeof(DataContractAttribute).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ReadOnlySpan<>).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(List<>).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ValueTask<>).GetTypeInfo().Assembly.Location)
#else
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.Serialization.Primitives.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Collections.dll")),
                    // MetadataReference.CreateFromFile(Path.Combine(binPath, "System.Threading.Tasks.Extensions.dll"))
                    MetadataReference.CreateFromFile(typeof(ValueTask<>).GetTypeInfo().Assembly.Location)
#endif
                ];
                return defaultReferences;
            }
        }

        /// <summary>
        /// Create a compilation with default references
        /// </summary>
        /// <param name="optimizationLevel"></param>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        public static CSharpCompilation CreateCompilation(
            this OptimizationLevel optimizationLevel,
            string assemblyName = null)
        {
            assemblyName ??= Path.GetRandomFileName();
            CSharpCompilationOptions compileOptions = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary)
                    .WithOptimizationLevel(optimizationLevel)
                    .WithAllowUnsafe(false);
            return CSharpCompilation.Create(assemblyName)
                .WithOptions(compileOptions)
                .AddReferences(TrustedReferences)
                .AddReferences(DefaultReferences);
        }

        /// <summary>
        /// Add code files to compilation
        /// </summary>
        public static CSharpCompilation AddCode(
            this CSharpCompilation compilation,
            IEnumerable<KeyValuePair<string, string>> fileAndCode,
            LanguageVersion languageVersion)
        {
            CSharpParseOptions parseOptions = new CSharpParseOptions()
                .WithKind(SourceCodeKind.Regular)
                .WithLanguageVersion(languageVersion);
            SyntaxTree[] syntaxTrees =
                [.. fileAndCode
                    .Select(c => CSharpSyntaxTree.ParseText(c.Value, parseOptions, c.Key))];
            return compilation.AddSyntaxTrees(syntaxTrees);
        }

        /// <summary>
        /// Add core stubs
        /// </summary>
        public static IEnumerable<KeyValuePair<string, string>> WithOpcUaCoreStubs(
            this IEnumerable<KeyValuePair<string, string>> codeFiles)
        {
            return codeFiles.Append(new KeyValuePair<string, string>(
                nameof(OpcUaCoreStubs), OpcUaCoreStubs));
        }

        /// <summary>
        /// Add core stubs
        /// </summary>
        public static IEnumerable<KeyValuePair<string, string>> WithOpcUaGeneratedStack(
            this IEnumerable<KeyValuePair<string, string>> codeFiles)
        {
            return codeFiles.Append(new KeyValuePair<string, string>(
                nameof(OpcUa), OpcUa));
        }

        /// <summary>
        /// Check diagnostics
        /// </summary>
        public static void Check(
            this ImmutableArray<Diagnostic> diagnostics,
            TextWriter output,
            out int errorCount,
            out int warnCount,
            bool filterLinkerAndReferenceErrors = false)
        {
            errorCount = 0;
            warnCount = 0;
            for (int ii = 0; ii < diagnostics.Length; ii++)
            {
                Diagnostic diag = diagnostics[ii];
                if (filterLinkerAndReferenceErrors &&
                    (
                        // diag.Id == "CS0234" ||
                        diag.Id == "CS0246" ||
                        diag.Id == "CS1729" ||
                        diag.Id == "CS1501" ||
                        diag.Id == "CS0103" ||
                        diag.Id == "CS1503"
                    ))
                {
                    // ignore missing reference and symbols errors
                    continue;
                }

                string sev;
                int beforeAfter;
                switch (diag.Severity)
                {
                    case DiagnosticSeverity.Error:
                        sev = "ERR";
                        beforeAfter = 12;
                        errorCount++;
                        break;
                    case DiagnosticSeverity.Warning when diag.Id != "CS1701":
                        beforeAfter = 2;
                        sev = "WRN";
                        warnCount++;
                        break;
                    default:
                        beforeAfter = 1;
                        sev = "INF";
                        break;
                }
                if (diag.Id != "CS1701")
                {
                    // TODO: See how we can remove this diagnostic warning to be emitted
                    output.WriteLine();
                    output.WriteLine(diag.ToString());
                }
                TextLineCollection lines = diag.Location.SourceTree?.GetText().Lines;
                if (lines == null)
                {
                    continue;
                }
                FileLinePositionSpan span = diag.Location.GetLineSpan();
                int startLine = span.StartLinePosition.Line;
                int endLine = span.EndLinePosition.Line;
                for (int i = Math.Max(0, startLine - beforeAfter);
                    i <= Math.Min(endLine + beforeAfter, lines.Count - 1);
                    i++)
                {
                    // line error indicators are 0 based, but line positions are 1 based
                    output.Write("{0,4} ", i - 1);
                    output.Write(i >= startLine && i <= endLine ? sev + ">>>> " : "        ");
                    output.WriteLine(lines[i]);
                }
            }
        }

        public static CSharpCompilation WithAnalyzers(
            this CSharpCompilation compilation,
            bool withAnalyzers,
            out CompilationWithAnalyzers compilationWithAnalyzers)
        {
            if (withAnalyzers)
            {
                try
                {
                    Assembly dependencies = LoadFromNugetCache(
                        Path.Combine("microsoft.codeanalysis.workspaces.common", "4.14.0", "lib", "netstandard2.0"),
                        "Microsoft.CodeAnalysis.Workspaces.dll");
                    Assembly netAnalyzer = LoadFromNugetCache(
                        Path.Combine("microsoft.codeAnalysis.netanalyzers", "10.0.100", "analyzers", "dotnet"),
                        "Microsoft.CodeAnalysis.NetAnalyzers.dll");
                    if (netAnalyzer != null)
                    {
                        DiagnosticAnalyzer[] analyzers = [.. netAnalyzer.GetTypes()
                            .Where(t => t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
                            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t))];
                        compilationWithAnalyzers = compilation.WithAnalyzers(
                            ImmutableArray.Create(analyzers),
                            new CompilationWithAnalyzersOptions(null, null, true, true, true));
                        return (CSharpCompilation)compilationWithAnalyzers.Compilation;
                    }
                }
                catch
                {
                    // ignore errors loading analyzers
                }
            }
            compilationWithAnalyzers = null;
            return compilation;

            static Assembly LoadFromNugetCache(string path, string dll)
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string location = Path.Combine(
                    userProfile,
                    ".nuget",
                    "packages",
                    path);
                string file = Path.Combine(location, dll);
                if (!File.Exists(file))
                {
                    file = Path.Combine(location, "cs", dll);
                }
                if (File.Exists(file))
                {
                    return Assembly.LoadFrom(file);
                }
                return null;
            }
        }

        /// <summary>
        /// Helper to debug diagnostics returned by compilation
        /// </summary>
        /// <param name="emitResult"></param>
        /// <returns>Number or errors and warnings in the result</returns>
        public static bool Check(
            this EmitResult emitResult,
            TextWriter errorWriter,
            out int errorCount,
            out int warnCount)
        {
            emitResult.Diagnostics.Check(errorWriter, out errorCount, out warnCount);
            return emitResult.Success;
        }

        /// <summary>
        /// All stubs needed to compile generated code against Opc.Ua.Core without
        /// referencing the actual assembly.
        /// </summary>
        public const string OpcUaCoreStubs =
            """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using System.Threading;
            using System.Collections.Generic;
            using System.Reflection;

            [assembly: AssemblyVersionAttribute("4.3.2.1")]
            namespace Opc.Ua
            {
                public interface IServiceRequest
                {
                    RequestHeader? RequestHeader { get; set; }
                }
                public interface IServiceResponse
                {
                    ResponseHeader? ResponseHeader { get;}
                }
                public class SecureChannelContext {}
                public interface ITransportChannel
                {
                    ValueTask<IServiceResponse> SendRequestAsync(
                        IServiceRequest request,
                        CancellationToken ct = default);
                    [Obsolete("Use SendRequestAsync instead")]
                    IServiceResponse SendRequest(
                        IServiceRequest request);
                    [Obsolete("Use SendRequestAsync instead")]
                        IAsyncResult BeginSendRequest(
                        IServiceRequest request,
                        AsyncCallback callback,
                        object callbackData);
                    [Obsolete("Use SendRequestAsync instead")]
                    IServiceResponse EndSendRequest(
                        IAsyncResult result);
                }
                public interface IServerBase {}
                public interface IEndpointBase {}
                public interface IServiceHostBase {}
                public enum RequestEncoding { Binary, Xml }
                public class EndpointBase
                {
                    [Obsolete("No WCF support")]
                    protected EndpointBase() {}
                    protected EndpointBase(IServiceHostBase host) {}
                    protected EndpointBase(ServerBase serverBase) {}
                    protected IServerBase? ServerForContext => throw new NotSupportedException();
                    protected ServiceResult? ServerError { get; set; }
                    protected virtual void OnRequestReceived(IServiceRequest request) {}
                    protected virtual void OnResponseSent(IServiceResponse response) {}
                    protected Dictionary<ExpandedNodeId, ServiceDefinition> SupportedServices { get; set; } = new();
                    protected class ServiceDefinition
                    {
                        public ServiceDefinition(Type requestType, InvokeService asyncInvokeMethod) {}
                    }
                    protected delegate ValueTask<IServiceResponse> InvokeService(
                        IServiceRequest request,
                        SecureChannelContext secureChannelContext,
                        CancellationToken cancellationToken = default);
                }
                public class ServerBase : IServerBase
                {
                    public ServerBase(ITelemetryContext telemetry) {}
                    public ServiceResult? ServerError { get; protected set; }
                    protected virtual void ValidateRequest(RequestHeader? requestHeader) {}
                    protected virtual ResponseHeader CreateResponse(
                        RequestHeader requestHeader, uint statusCode)
                        => throw new NotSupportedException();
                }
                public partial class HistoryUpdateDetails
                {
                    public virtual NodeId NodeId { get; set; }
                }
                public interface IClientBase {}
                public class ClientBase : IClientBase
                {
                    public ClientBase(ITransportChannel channel, ITelemetryContext telemetry) { }
                    public ITransportChannel TransportChannel => throw new NotSupportedException();

                    protected static void ValidateResponse(
                        ResponseHeader? header) {}
                    protected virtual void UpdateRequestHeader(
                        IServiceRequest request, bool useDefaults, string serviceName) {}
                    protected virtual void RequestCompleted(
                        IServiceRequest request, IServiceResponse response,
                        string serviceName) {}
                }
                public class FolderState : BaseObjectState
                {
                    public FolderState(NodeState? parent) : base(parent) { }
                }
            }
            """;

        /// <summary>
        /// All stubs needed to compile models against Opc.Ua models.
        /// </summary>
        public const string OpcUa =
            """
            #nullable enable
            using System.Reflection;

            [assembly: AssemblyVersionAttribute("4.3.2.1")]
            namespace Opc.Ua
            {
                public static partial class StatusCodes
                {
                    public const uint Good = 0;
                }

                public static partial class DataTypes
                {
                    public const uint String = 0;
                    public const uint BaseDataType = 0;
                    public const uint Number = 0;
                }
                public static partial class Objects
                {
                    public const uint ModellingRule_Mandatory = 0;
                    public const uint ModellingRule_Optional = 0;
                    public const uint ModellingRule_ExposesItsArray = 0;
                    public const uint ModellingRule_OptionalPlaceholder = 0;
                    public const uint ModellingRule_MandatoryPlaceholder = 0;
                }
                public static partial class BrowseNames
                {
                    public const string FileSystem = "FileSystem";
                }
                public static partial class Namespaces
                {
                    public const string OpcUa = "http://opcfoundation.org/UA/";
                }
                public class Encodeable : IEncodeable
                {
                    public ExpandedNodeId TypeId => default;
                    public ExpandedNodeId BinaryEncodingId => default;
                    public ExpandedNodeId XmlEncodingId => default;
                    public ExpandedNodeId JsonEncodingId => default;
                    public void Encode(IEncoder encoder) { }
                    public void Decode(IDecoder decoder) { }
                    public bool IsEqual(IEncodeable encodeable) { return true; }
                    public object Clone() { return this; }
                }
                public class IdentityMappingRuleType : Encodeable { }
                public class Range : Encodeable { }
                public class EUInformation : Encodeable {}
                public class FileDirectoryState : BaseObjectState
                {
                    public FileDirectoryState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceCreateDirectory(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceCreateFile(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceDeleteFileSystemObject(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceMoveOrCopy(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class AnalogUnitState : BaseDataVariableState
                {
                    public AnalogUnitState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceEngineeringUnits(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class AnalogItemState : BaseDataVariableState
                {
                    public AnalogItemState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceEURange(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceEngineeringUnits(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class AnalogItemState<T> : AnalogItemState
                {
                    public AnalogItemState(NodeState? parent) : base(parent) { }
                    public static AnalogItemState<T> With<TBuilder>(
                        NodeState? parent = null)
                        where TBuilder : struct, IVariantBuilder<T>
                    {
                        return null!;
                    }
                }
                public class BaseInterfaceState : BaseObjectState
                {
                    public BaseInterfaceState(NodeState? parent) : base(parent) { }
                }
                public class BaseEventState : BaseObjectState
                {
                    public BaseEventState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceEventId(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceEventType(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceSourceNode(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceSourceName(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceTime(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceReceiveTime(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceMessage(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceSeverity(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceConditionClassId(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceConditionClassName(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceConditionName(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceBranchId(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceRetain(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceEnabledState(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceQuality(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceLastSeverity(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceComment(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceClientUserId(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceDisable(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceEnable(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceAddComment(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceAckedState(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceAcknowledge(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class ConditionState : BaseEventState
                {
                    public ConditionState(NodeState? parent) : base(parent) { }
                }
                public class AcknowledgeableConditionState : BaseEventState
                {
                    public AcknowledgeableConditionState(NodeState? parent) : base(parent) { }
                }
                public class FolderState : BaseObjectState
                {
                    public FolderState(NodeState? parent) : base(parent) { }
                }
                public class InstrumentDiagnosticAlarmState : BaseEventState
                {
                    public InstrumentDiagnosticAlarmState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceIterations(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceNewValueCount(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceSuppressedOrShelved(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceActiveState(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceInputNode(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceNormalState(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class TemporaryFileTransferState : BaseObjectState
                {
                    public TemporaryFileTransferState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceClientProcessingTimeout(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceGenerateFileForWrite(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceGenerateFileForRead(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceCloseAndCommit(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class TwoStateVariableState : BaseVariableState
                {
                    public TwoStateVariableState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceId(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class ConditionVariableState<T> : BaseVariableState
                {
                    public ConditionVariableState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceSourceTimestamp(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public static ConditionVariableState<T> With<TBuilder>(
                        NodeState? parent = null)
                        where TBuilder : struct, IVariantBuilder<T>
                    {
                        return null!;
                    }
                }
                public class FiniteStateVariableState : BaseVariableState
                {
                    public FiniteStateVariableState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceId(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class FiniteStateMachineState : BaseObjectState
                {
                    public FiniteStateMachineState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceCurrentState(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class DataTypeEncodingState : BaseObjectState
                {
                    public DataTypeEncodingState(NodeState? parent) : base(parent) { }
                }
                public class OpenMethodState : MethodState
                {
                    public OpenMethodState(NodeState? parent) : base(parent) { }
                }
                public class CloseMethodState : MethodState
                {
                    public CloseMethodState(NodeState? parent) : base(parent) { }
                }
                public class ReadMethodState : MethodState
                {
                    public ReadMethodState(NodeState? parent) : base(parent) { }
                }
                public class WriteMethodState : MethodState
                {
                    public WriteMethodState(NodeState? parent) : base(parent) { }
                }
                public class GetPositionMethodState : MethodState
                {
                    public GetPositionMethodState(NodeState? parent) : base(parent) { }
                }
                public class SetPositionMethodState : MethodState
                {
                    public SetPositionMethodState(NodeState? parent) : base(parent) { }
                }
                public class GenerateFileForReadMethodState : MethodState
                {
                    public GenerateFileForReadMethodState(NodeState? parent) : base(parent) { }
                }
                public class GenerateFileForWriteMethodState : MethodState
                {
                    public GenerateFileForWriteMethodState(NodeState? parent) : base(parent) { }
                }
                public class CreateDirectoryMethodState : MethodState
                {
                    public CreateDirectoryMethodState(NodeState? parent) : base(parent) { }
                }
                public class CreateFileMethodState : MethodState
                {
                    public CreateFileMethodState(NodeState? parent) : base(parent) { }
                }
                public class DeleteFileMethodState : MethodState
                {
                    public DeleteFileMethodState(NodeState? parent) : base(parent) { }
                }
                public class MoveOrCopyMethodState : MethodState
                {
                    public MoveOrCopyMethodState(NodeState? parent) : base(parent) { }
                }
                public class CloseAndCommitMethodState : MethodState
                {
                    public CloseAndCommitMethodState(NodeState? parent) : base(parent) { }
                }
                public class FileState : BaseObjectState
                {
                    public FileState(NodeState? parent) : base(parent) { }

                    public void CreateOrReplaceSize(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceWritable(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceUserWritable(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceOpenCount(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceOpen(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceClose(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceRead(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceWrite(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceGetPosition(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceSetPosition(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class StateMachineStateState : BaseObjectState
                {
                    public StateMachineStateState(NodeState? parent) : base(parent) { }
                }
                public class StateMachineInitialStateState : StateMachineStateState
                {
                    public StateMachineInitialStateState(NodeState? parent) : base(parent) { }
                }
                public class StateMachineTransitionState : StateMachineStateState
                {
                    public StateMachineTransitionState(NodeState? parent) : base(parent) { }
                }
                public class AddCommentMethodState : MethodState
                {
                    public AddCommentMethodState(NodeState? parent) : base(parent) { }
                }
                public class DataTypeDictionaryState : BaseDataVariableState
                {
                    public DataTypeDictionaryState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceNamespaceUri(
                        ISystemContext context, BaseInstanceState replacement) { }
                    public void CreateOrReplaceDeprecated(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class DataTypeDescriptionState : BaseDataVariableState
                {
                    public DataTypeDescriptionState(NodeState? parent) : base(parent) { }
                }
                public class RoleState : BaseObjectState
                {
                    public RoleState(NodeState? parent) : base(parent) { }
                    public void CreateOrReplaceIdentities(
                        ISystemContext context, BaseInstanceState replacement) { }
                }
                public class DeleteFileSystemObjectMethodState : MethodState
                {
                    public DeleteFileSystemObjectMethodState(NodeState? parent) : base(parent) { }
                }
            }
            """;
    }
}
