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
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates strongly typed asynchronous wrappers for every OPC UA
    /// <c>ObjectType</c>. Each emitted proxy class derives from the
    /// proxy of its parent ObjectType (forming an inheritance chain that
    /// mirrors the OPC UA type hierarchy) and exposes one wrapper method
    /// per declared <c>MethodDesign</c> child.
    /// </summary>
    /// <remarks>
    /// This generator runs by default for every model; consumers can
    /// suppress it by setting
    /// <see cref="GeneratorOptions.OmitObjectTypeProxies"/> to
    /// <c>true</c>. The output namespace defaults to the model's target
    /// namespace prefix and can be overridden via
    /// <see cref="GeneratorOptions.ObjectTypeProxyNamespace"/>. When a
    /// proxy must derive from a parent proxy emitted in a different
    /// assembly the
    /// <see cref="GeneratorOptions.ObjectTypeProxyExternalNamespaces"/>
    /// dictionary is consulted to resolve the parent's CLR namespace
    /// (the standard UA namespace
    /// <c>http://opcfoundation.org/UA/</c> always maps to
    /// <c>Opc.Ua.Client</c>).
    /// </remarks>
    internal sealed class ObjectTypeProxyGenerator : IGenerator
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ObjectTypeProxyGenerator"/> class.
        /// </summary>
        public ObjectTypeProxyGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            List<ObjectTypeDesign> types = GetEmittedObjectTypes();
            if (types.Count == 0)
            {
                return [];
            }

            string outputNamespace = GetOutputNamespace();

            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format(
                    "{0}.TypeProxies.g.cs",
                    m_context.ModelDesign.TargetNamespace.Prefix));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, ObjectTypeProxyTemplates.File);

            template.AddReplacement(Tokens.Namespace, outputNamespace);
            template.AddReplacement(
                Tokens.ListOfTypes,
                ObjectTypeProxyTemplates.ProxyClass,
                types,
                WriteTemplate_ProxyClass);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Selects every non-excluded <see cref="ObjectTypeDesign"/> in
        /// the model. A proxy class is emitted for each one regardless
        /// of whether it declares any methods so that the generated
        /// inheritance chain mirrors the full OPC UA type hierarchy.
        /// </summary>
        private List<ObjectTypeDesign> GetEmittedObjectTypes()
        {
            var result = new List<ObjectTypeDesign>();
            foreach (NodeDesign node in m_context.ModelDesign.GetNodeDesigns())
            {
                if (node is not ObjectTypeDesign objectType)
                {
                    continue;
                }
                if (m_context.ModelDesign.IsExcluded(objectType))
                {
                    continue;
                }
                result.Add(objectType);
            }
            // Make output order deterministic across runs.
            result.Sort(static (a, b) => string.CompareOrdinal(
                a.SymbolicName?.Name,
                b.SymbolicName?.Name));
            return result;
        }

        /// <summary>
        /// Returns the methods declared directly on the given type.
        /// Inherited methods coming from supertypes are intentionally
        /// excluded — they are surfaced via the generated inheritance
        /// chain instead.
        /// </summary>
        private List<MethodDesign> GetDeclaredMethods(ObjectTypeDesign objectType)
        {
            var methods = new List<MethodDesign>();
            InstanceDesign[] children = objectType.Children?.Items;
            if (children == null)
            {
                return methods;
            }
            foreach (InstanceDesign child in children)
            {
                if (child is not MethodDesign method)
                {
                    continue;
                }
                if (m_context.ModelDesign.IsExcluded(method))
                {
                    continue;
                }
                if (method.MethodDeclarationNode != null &&
                    m_context.ModelDesign.IsExcluded(method.MethodDeclarationNode))
                {
                    continue;
                }
                methods.Add(method);
            }
            methods.Sort(static (a, b) => string.CompareOrdinal(
                a.SymbolicName?.Name,
                b.SymbolicName?.Name));
            return methods;
        }

        /// <summary>
        /// Resolves the C# namespace for the generated proxy classes.
        /// Falls back to the model's target namespace prefix when the
        /// option is not set.
        /// </summary>
        private string GetOutputNamespace()
        {
            string @override = m_context.Options?.ObjectTypeProxyNamespace;
            return string.IsNullOrWhiteSpace(@override)
                ? m_context.ModelDesign.TargetNamespace.Prefix
                : @override;
        }

        /// <summary>
        /// Resolves the fully qualified base-class name for a generated
        /// proxy class. Walks <see cref="TypeDesign.BaseTypeNode"/>: when
        /// the parent is itself an <see cref="ObjectTypeDesign"/> the
        /// emitted parent proxy is used; otherwise the shared abstract
        /// <c>global::Opc.Ua.ObjectTypeClient</c> base is
        /// returned.
        /// </summary>
        private string ResolveBaseClassName(ObjectTypeDesign objectType)
        {
            if (objectType.BaseTypeNode is not ObjectTypeDesign parent)
            {
                return RootBaseClass;
            }

            string parentName = parent.SymbolicName?.Name;
            if (string.IsNullOrEmpty(parentName))
            {
                return RootBaseClass;
            }

            string parentNamespace = ResolveProxyNamespaceForType(parent);
            return CoreUtils.Format(
                "global::{0}.{1}Client",
                parentNamespace,
                parentName);
        }

        /// <summary>
        /// Returns the C# namespace into which the proxy for
        /// <paramref name="type"/> is (or would be) emitted. Internal
        /// types use the configured output namespace; external types are
        /// looked up via the standard UA → <c>Opc.Ua</c> default
        /// and the user-supplied
        /// <see cref="GeneratorOptions.ObjectTypeProxyExternalNamespaces"/>
        /// override; otherwise the model's namespace prefix is used as a
        /// last resort.
        /// </summary>
        private string ResolveProxyNamespaceForType(TypeDesign type)
        {
            string typeUri = type.SymbolicName?.Namespace;
            string targetUri = m_context.ModelDesign.TargetNamespace?.Value;

            if (!string.IsNullOrEmpty(typeUri) &&
                string.Equals(typeUri, targetUri, StringComparison.Ordinal))
            {
                return GetOutputNamespace();
            }

            if (!string.IsNullOrEmpty(typeUri))
            {
                IDictionary<string, string> overrides =
                    m_context.Options?.ObjectTypeProxyExternalNamespaces;
                if (overrides != null &&
                    overrides.TryGetValue(typeUri, out string mapped) &&
                    !string.IsNullOrWhiteSpace(mapped))
                {
                    return mapped;
                }

                if (string.Equals(typeUri, StandardUaNamespaceUri, StringComparison.Ordinal))
                {
                    return StandardUaProxyNamespace;
                }

                Namespace[] namespaces = m_context.ModelDesign.Namespaces;
                if (namespaces != null)
                {
                    foreach (Namespace ns in namespaces)
                    {
                        if (string.Equals(ns?.Value, typeUri, StringComparison.Ordinal) &&
                            !string.IsNullOrWhiteSpace(ns.Prefix))
                        {
                            return ns.Prefix;
                        }
                    }
                }
            }

            return GetOutputNamespace();
        }

        /// <summary>
        /// Walks the supertype chain of <paramref name="objectType"/> and
        /// returns the union of the browse names of every method
        /// declared anywhere above it. Used to decide which generated
        /// methods need the C# <c>new</c> modifier (method shadowing).
        /// </summary>
        private HashSet<string> CollectInheritedMethodNames(ObjectTypeDesign objectType)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            TypeDesign current = objectType.BaseTypeNode;
            while (current is ObjectTypeDesign parent)
            {
                foreach (MethodDesign method in GetDeclaredMethods(parent))
                {
                    string name = method.SymbolicName?.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }
                }
                current = parent.BaseTypeNode;
            }
            return names;
        }

        /// <summary>
        /// Renders one proxy class for a given <see cref="ObjectTypeDesign"/>.
        /// </summary>
        private bool WriteTemplate_ProxyClass(IWriteContext context)
        {
            if (context.Target is not ObjectTypeDesign objectType)
            {
                return false;
            }

            string typeName = objectType.SymbolicName.Name;
            string className = CoreUtils.Format("{0}Client", typeName);
            string baseClassName = ResolveBaseClassName(objectType);

            // Stash inherited method names so LoadTemplate_Method can
            // emit the C# 'new' modifier on shadowed methods.
            m_inheritedMethodNames = CollectInheritedMethodNames(objectType);

            context.Template.AddReplacement(Tokens.SymbolicName, typeName);
            context.Template.AddReplacement(Tokens.ClassName, className);
            context.Template.AddReplacement(Tokens.BaseClassName, baseClassName);

            List<MethodDesign> methods = GetDeclaredMethods(objectType);
            context.Template.AddReplacement(
                Tokens.MethodList,
                methods,
                LoadTemplate_Method);

            return context.Template.Render();
        }

        /// <summary>
        /// Streams the entire async wrapper for one method (XML doc,
        /// signature and body) into the surrounding template at the
        /// position of the <see cref="Tokens.MethodList"/> placeholder.
        /// </summary>
        private TemplateString LoadTemplate_Method(ILoadContext context)
        {
            if (context.Target is not MethodDesign method)
            {
                return null;
            }

            string targetNamespace = m_context.ModelDesign.TargetNamespace.Value;
            Namespace[] namespaces = m_context.ModelDesign.Namespaces;

            string methodName = method.SymbolicName.Name;
            Parameter[] inputs = method.InputArguments ?? [];
            Parameter[] outputs = method.OutputArguments ?? [];

            string methodIdConstant = CoreUtils.Format(
                "global::{0}.MethodIds.{1}",
                m_context.ModelDesign.TargetNamespace.Prefix,
                method.SymbolicId.Name);

            // Compute the strongly typed return signature.
            string returnTypeAnnotation = GetReturnTypeAnnotation(
                outputs, targetNamespace, namespaces);

            bool isShadow = m_inheritedMethodNames != null &&
                m_inheritedMethodNames.Contains(methodName);

            // ----------------------------------------------------------------
            // XML doc and signature
            // ----------------------------------------------------------------
            context.Out.WriteLine();
            context.Out.WriteLine("/// <summary>");
            context.Out.WriteLine(
                "/// Invokes the <c>{0}</c> method on the wrapped object.",
                methodName);
            context.Out.WriteLine("/// </summary>");
            for (int ii = 0; ii < inputs.Length; ii++)
            {
                context.Out.WriteLine(
                    "/// <param name=\"{0}\">Input argument {1}.</param>",
                    GetParameterName(inputs[ii]),
                    ii);
            }
            context.Out.WriteLine(
                "/// <param name=\"ct\">Token used to cancel the invocation.</param>");

            context.Out.Write(
                "public {0}async global::System.Threading.Tasks.ValueTask{1} {2}Async(",
                isShadow ? "new " : string.Empty,
                returnTypeAnnotation,
                methodName);

            for (int ii = 0; ii < inputs.Length; ii++)
            {
                context.Out.WriteLine();
                context.Out.Write(
                    "    {0} {1},",
                    inputs[ii].DataTypeNode.GetMethodArgumentTypeAsCode(
                        inputs[ii].ValueRank,
                        targetNamespace,
                        namespaces,
                        inputs[ii].IsOptional),
                    GetParameterName(inputs[ii]));
            }
            context.Out.WriteLine();
            context.Out.WriteLine(
                "    global::System.Threading.CancellationToken ct = default)");
            context.Out.WriteLine("{");

            // ----------------------------------------------------------------
            // Argument null checks for non-nullable reference inputs.
            // String inputs are intentionally NOT null-checked here so
            // that callers can pass a null subjectName / privateKeyFormat
            // through to the server (preserving the OPC UA wire
            // semantics for optional string inputs).
            // ----------------------------------------------------------------
            foreach (Parameter input in inputs)
            {
                if (RequiresNullCheck(input))
                {
                    context.Out.WriteLine(
                        "    if ({0} is null) throw new global::System.ArgumentNullException(nameof({0}));",
                        GetParameterName(input));
                }
            }

            // ----------------------------------------------------------------
            // Invoke the inherited CallMethodAsync helper with boxed inputs.
            // ----------------------------------------------------------------
            if (outputs.Length > 0)
            {
                context.Out.WriteLine(
                    "    global::Opc.Ua.ArrayOf<global::Opc.Ua.Variant> _outputArguments = await CallMethodAsync(");
            }
            else
            {
                context.Out.WriteLine(
                    "    _ = await CallMethodAsync(");
            }
            context.Out.WriteLine(
                "        global::Opc.Ua.ExpandedNodeId.ToNodeId({0}, Session.MessageContext.NamespaceUris),",
                methodIdConstant);
            context.Out.Write("        ct");
            for (int ii = 0; ii < inputs.Length; ii++)
            {
                context.Out.WriteLine(",");
                context.Out.Write(
                    "        {0}",
                    BoxInputArgument(inputs[ii]));
            }
            context.Out.WriteLine(").ConfigureAwait(false);");

            // ----------------------------------------------------------------
            // Unpack the output arguments and produce the typed return value.
            // ----------------------------------------------------------------
            EmitOutputUnpacking(
                context,
                outputs,
                methodName,
                methodIdConstant,
                targetNamespace,
                namespaces);

            context.Out.WriteLine("}");
            return null;
        }

        /// <summary>
        /// Returns the suffix appended to <c>ValueTask</c> in the method
        /// signature: empty for void methods, <c>&lt;T&gt;</c> for a single
        /// output, or a tuple for multiple outputs.
        /// </summary>
        private static string GetReturnTypeAnnotation(
            Parameter[] outputs,
            string targetNamespace,
            Namespace[] namespaces)
        {
            if (outputs.Length == 0)
            {
                return string.Empty;
            }
            if (outputs.Length == 1)
            {
                return CoreUtils.Format(
                    "<{0}>",
                    outputs[0].DataTypeNode.GetMethodArgumentTypeAsCode(
                        outputs[0].ValueRank,
                        targetNamespace,
                        namespaces,
                        outputs[0].IsOptional));
            }

            var builder = new System.Text.StringBuilder();
            builder.Append("<(");
            for (int ii = 0; ii < outputs.Length; ii++)
            {
                if (ii > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(outputs[ii].DataTypeNode.GetMethodArgumentTypeAsCode(
                    outputs[ii].ValueRank,
                    targetNamespace,
                    namespaces,
                    outputs[ii].IsOptional));
                builder.Append(' ');
                builder.Append(GetParameterName(outputs[ii]));
            }
            builder.Append(")>");
            return builder.ToString();
        }

        /// <summary>
        /// Writes the result-unpacking section of a method body.
        /// </summary>
        private static void EmitOutputUnpacking(
            ILoadContext context,
            Parameter[] outputs,
            string methodName,
            string methodIdConstant,
            string targetNamespace,
            Namespace[] namespaces)
        {
            if (outputs.Length == 0)
            {
                return;
            }

            context.Out.WriteLine(
                "    if (_outputArguments.Count < {0})",
                outputs.Length);
            context.Out.WriteLine("    {");
            context.Out.WriteLine(
                "        throw new global::Opc.Ua.ServiceResultException(");
            context.Out.WriteLine(
                "            global::Opc.Ua.StatusCodes.BadUnexpectedError,");
            context.Out.WriteLine(
                "            \"Method '{0}' returned fewer output arguments than expected.\");",
                methodName);
            context.Out.WriteLine("    }");

            for (int ii = 0; ii < outputs.Length; ii++)
            {
                EmitOutputDeclaration(
                    context,
                    outputs[ii],
                    ii,
                    methodName,
                    targetNamespace,
                    namespaces);
            }

            // Build the return expression.
            if (outputs.Length == 1)
            {
                context.Out.WriteLine(
                    "    return {0};",
                    GetLocalVariableName(outputs[0]));
                return;
            }

            context.Out.Write("    return (");
            for (int ii = 0; ii < outputs.Length; ii++)
            {
                if (ii > 0)
                {
                    context.Out.Write(", ");
                }
                context.Out.Write(GetLocalVariableName(outputs[ii]));
            }
            context.Out.WriteLine(");");
        }

        /// <summary>
        /// Emits a single <c>var</c> declaration that unpacks one output
        /// argument from the <c>_outputArguments</c> collection. Falls back
        /// to <c>BadUnexpectedError</c> when conversion fails.
        /// </summary>
        private static void EmitOutputDeclaration(
            ILoadContext context,
            Parameter parameter,
            int index,
            string methodName,
            string targetNamespace,
            Namespace[] namespaces)
        {
            string typeName = parameter.DataTypeNode.GetMethodArgumentTypeAsCode(
                parameter.ValueRank,
                targetNamespace,
                namespaces,
                parameter.IsOptional);
            string localName = GetLocalVariableName(parameter);
            string parameterName = GetParameterName(parameter);

            switch (parameter.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    context.Out.WriteLine(
                        "    if (!_outputArguments[{0}].TryGetStructure(out {1} {2}))",
                        index,
                        typeName,
                        localName);
                    EmitConversionFailure(context, methodName, parameterName);
                    break;
                case BasicDataType.BaseDataType when parameter.ValueRank == ValueRank.Scalar:
                    // The argument is itself a Variant; assign directly.
                    context.Out.WriteLine(
                        "    {0} {1} = _outputArguments[{2}];",
                        typeName,
                        localName,
                        index);
                    break;
                default:
                    context.Out.WriteLine(
                        "    if (!_outputArguments[{0}].TryGetValue(out {1} {2}))",
                        index,
                        typeName,
                        localName);
                    EmitConversionFailure(context, methodName, parameterName);
                    break;
            }
        }

        /// <summary>
        /// Emits the <c>BadUnexpectedError</c> branch used when an output
        /// argument cannot be converted to the declared CLR type.
        /// </summary>
        private static void EmitConversionFailure(
            ILoadContext context,
            string methodName,
            string parameterName)
        {
            context.Out.WriteLine("    {");
            context.Out.WriteLine(
                "        throw new global::Opc.Ua.ServiceResultException(");
            context.Out.WriteLine(
                "            global::Opc.Ua.StatusCodes.BadUnexpectedError,");
            context.Out.WriteLine(
                "            \"Method '{0}' returned an unexpected value for output '{1}'.\");",
                methodName,
                parameterName);
            context.Out.WriteLine("    }");
        }

        /// <summary>
        /// Returns the C# expression used to box an input argument into a
        /// <see cref="global::Opc.Ua.Variant"/>.
        /// </summary>
        private static string BoxInputArgument(Parameter parameter)
        {
            string name = GetParameterName(parameter);
            switch (parameter.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    return CoreUtils.Format(
                        "global::Opc.Ua.Variant.FromStructure({0})",
                        name);
                case BasicDataType.BaseDataType when parameter.ValueRank == ValueRank.Scalar:
                    return name;
                default:
                    return CoreUtils.Format(
                        "global::Opc.Ua.Variant.From({0})",
                        name);
            }
        }

        /// <summary>
        /// Returns true when the parameter requires a non-null check at
        /// runtime (only non-nullable reference-typed inputs other than
        /// <c>String</c>).
        /// </summary>
        private static bool RequiresNullCheck(Parameter parameter)
        {
            if (parameter.IsOptional)
            {
                return false;
            }
            // ArrayOf<T> is a non-nullable value type in v16; arrays never need a null check.
            if (parameter.ValueRank != ValueRank.Scalar)
            {
                return false;
            }
            // Most BasicDataType-mapped scalars are value types in v16
            // (NodeId, ExpandedNodeId, ByteString, QualifiedName, LocalizedText,
            // DataValue, DiagnosticInfo, StatusCode, Variant, primitives, enums, etc.).
            // Strings are intentionally NOT null-checked here so that the
            // caller can pass a null subjectName / privateKeyFormat through to
            // the server (preserving the OPC UA wire semantics for optional
            // string inputs).
            return parameter.DataTypeNode.BasicDataType == BasicDataType.UserDefined;
        }

        /// <summary>
        /// Returns a lowerCamelCase parameter name for the given UA argument.
        /// Reserved C# identifiers are escaped with the standard '@' prefix.
        /// </summary>
        private static string GetParameterName(Parameter parameter)
        {
            string name = parameter.Name;
            if (string.IsNullOrEmpty(name))
            {
                return "value";
            }
            string camel = char.ToLowerInvariant(name[0]) + name[1..];
            return s_csharpKeywords.Contains(camel) ? "@" + camel : camel;
        }

        /// <summary>
        /// Returns the local variable name used inside the method body for
        /// an output argument. Always prefixed to avoid colliding with
        /// input parameter names.
        /// </summary>
        private static string GetLocalVariableName(Parameter parameter)
        {
            return "_" + GetParameterName(parameter).TrimStart('@');
        }

        // C# 12 reserved keywords that, when reused as parameter names,
        // must be escaped with an '@' prefix. Kept narrow on purpose;
        // contextual keywords (e.g. "value", "var") are intentionally
        // omitted because they are valid identifiers.
        private static readonly System.Collections.Generic.HashSet<string> s_csharpKeywords =
        [
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
            "char", "checked", "class", "const", "continue", "decimal", "default",
            "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach",
            "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
            "lock", "long", "namespace", "new", "null", "object", "operator",
            "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this",
            "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        ];

        private const string StandardUaNamespaceUri = "http://opcfoundation.org/UA/";
        private const string StandardUaProxyNamespace = "Opc.Ua";
        private const string RootBaseClass = "global::Opc.Ua.ObjectTypeClient";

        private readonly IGeneratorContext m_context;
        private HashSet<string> m_inheritedMethodNames;
    }
}
