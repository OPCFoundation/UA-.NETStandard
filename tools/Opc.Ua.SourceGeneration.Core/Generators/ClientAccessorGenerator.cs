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
using System.IO;
using System.Linq;
using Opc.Ua.Schema.Model;
using Opc.Ua.SourceGeneration.Templating;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Emits opt-in variable and repeated-object extensions over a referenced model's client proxies.
    /// </summary>
    internal sealed class ClientAccessorGenerator : IGenerator
    {
        /// <summary>
        /// Creates an emitter using the validated model and its referenced namespace mappings.
        /// </summary>
        public ClientAccessorGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_proxies = new ObjectTypeProxyGenerator(context);
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            Namespace target = m_context.ModelDesign.TargetNamespace;
            string outputNamespace = string.IsNullOrWhiteSpace(m_context.Options?.ClientAccessorNamespace)
                ? target.Prefix + ".Client" : m_context.Options!.ClientAccessorNamespace!;
            string className = string.IsNullOrWhiteSpace(m_context.Options?.ClientAccessorClassName)
                ? target.Name + "ClientMembers" : m_context.Options!.ClientAccessorClassName!;
            string fileName = Path.Combine(m_context.OutputFolder, target.Prefix + ".ClientMembers.g.cs");
            ObjectTypeDesign[] types = [.. m_context.ModelDesign.GetNodeDesigns()
                .OfType<ObjectTypeDesign>()
                .Where(type => !m_context.ModelDesign.IsExcluded(type))
                .OrderBy(type => type.SymbolicName.Name, StringComparer.Ordinal)];

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, s_file);
            template.AddReplacement(Tokens.Namespace, outputNamespace);
            template.AddReplacement(Tokens.ClassName, className);
            template.AddReplacement(Tokens.ListOfTypes, types, onLoad: context =>
            {
                EmitMembers(context.Out, (ObjectTypeDesign)context.Target);
                return null;
            });
            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Emits declared members only; proxy inheritance supplies inherited accessors.
        /// </summary>
        private void EmitMembers(ITemplateWriter writer, ObjectTypeDesign owner)
        {
            string receiver = ProxyName(owner);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (InstanceDesign child in (owner.Children?.Items ?? [])
                .OrderBy(child => child.SymbolicName.Name, StringComparer.Ordinal))
            {
                if (m_context.ModelDesign.IsExcluded(child) ||
                    child.ModellingRule is not (
                        ModellingRule.Mandatory or ModellingRule.Optional or
                        ModellingRule.MandatoryPlaceholder or ModellingRule.OptionalPlaceholder))
                {
                    continue;
                }
                string browseName = string.IsNullOrEmpty(child.BrowseName)
                    ? child.SymbolicName.Name : child.BrowseName;
                string name = MemberName(browseName);
                string methodName;
                if (child is VariableDesign variable &&
                    child.ModellingRule is ModellingRule.Mandatory or ModellingRule.Optional)
                {
                    methodName = name + "Variable";
                    RequireUnique(emitted, receiver, methodName);
                    EmitVariable(writer, receiver, variable, browseName, methodName);
                }
                else if (child is ObjectDesign { TypeDefinitionNode: ObjectTypeDesign type } &&
                    child.ModellingRule is ModellingRule.MandatoryPlaceholder or ModellingRule.OptionalPlaceholder &&
                    m_proxies.HasProxy(type))
                {
                    methodName = "Enumerate" + name.TrimEnd('_') + "Async";
                    RequireUnique(emitted, receiver, methodName);
                    EmitPlaceholder(writer, receiver, type, methodName);
                }
            }
        }

        /// <summary>
        /// Emits a typed value view with its original BrowseName, namespace and write permission.
        /// </summary>
        private void EmitVariable(
            ITemplateWriter writer,
            string receiver,
            VariableDesign variable,
            string browseName,
            string methodName)
        {
            DataTypeDesign datatype = variable.DataTypeNode ?? throw new InvalidOperationException(
                $"Client variable '{receiver}.{methodName}' has no resolved data type.");
            string typeName = datatype.GetMethodArgumentTypeAsCode(
                variable.ValueRank, string.Empty, m_context.ModelDesign.Namespaces, false);
            bool variant = typeName == "global::Opc.Ua.Variant";
            bool structure = !variant && datatype.BasicDataType == BasicDataType.UserDefined;
            string namespaceUri = variable.SymbolicName.Namespace;
            bool writable = variable.AccessLevel is AccessLevel.Write or AccessLevel.ReadWrite;

            writer.WriteLine("/// <summary>");
            writer.WriteLine("/// Accesses the declared value while preserving quality and source timestamps.");
            writer.WriteLine("/// </summary>");
            writer.WriteLine("public static global::Opc.Ua.Client.ClientVariable<{0}> {1}(", typeName, methodName);
            writer.WriteLine("    this {0} proxy)", receiver);
            writer.WriteLine("{");
            writer.WriteLine("    return new global::Opc.Ua.Client.ClientVariable<{0}>(", typeName);
            writer.WriteLine("        proxy, {0}, {1}, {2},",
                namespaceUri.AsStringLiteral(), browseName.AsStringLiteral(), writable ? "true" : "false");
            if (variant)
            {
                writer.WriteLine("        static (value, context) => value,");
            }
            else
            {
                writer.WriteLine("        static (value, context) =>");
                writer.WriteLine("        {");
                writer.WriteLine("            {0} decoded;", typeName);
                writer.WriteLine("            return value.TryGetValue(out decoded!{0})",
                    structure ? ", context" : string.Empty);
                writer.WriteLine("                ? decoded : throw new global::Opc.Ua.ServiceResultException(");
                writer.WriteLine("                    global::Opc.Ua.StatusCodes.BadTypeMismatch);");
                writer.WriteLine("        },");
            }
            writer.WriteLine("        static value => global::Opc.Ua.Variant.{0}(value));",
                structure ? "FromStructure" : "From");
            writer.WriteLine("}");
            writer.WriteLine();
        }

        /// <summary>
        /// Emits a concrete placeholder enumeration through the shared bounded client browse implementation.
        /// </summary>
        private void EmitPlaceholder(
            ITemplateWriter writer,
            string receiver,
            ObjectTypeDesign type,
            string methodName)
        {
            string proxy = ProxyName(type);
            string typeNamespace = type.SymbolicName.Namespace;
            string identifierNamespace = m_context.ModelDesign.Namespaces
                .First(ns => ns.Value == typeNamespace).Prefix;
            writer.WriteLine("/// <summary>");
            writer.WriteLine("/// Enumerates concrete instances of the declared placeholder type.");
            writer.WriteLine("/// </summary>");
            writer.WriteLine("public static global::System.Collections.Generic.IAsyncEnumerable<{0}> {1}(",
                proxy, methodName);
            writer.WriteLine("    this {0} parent,", receiver);
            writer.WriteLine("    global::System.Threading.CancellationToken cancellationToken = default)");
            writer.WriteLine("{");
            writer.WriteLine("    return global::Opc.Ua.Client.ClientModelAccess.EnumerateAsync(");
            writer.WriteLine("        parent, global::{0}.ObjectTypeIds.{1},",
                identifierNamespace, type.SymbolicName.Name);
            writer.WriteLine("        static (session, id, telemetry) => new {0}(session, id, telemetry),", proxy);
            writer.WriteLine("        cancellationToken);");
            writer.WriteLine("}");
            writer.WriteLine();
        }

        /// <summary>
        /// Resolves a proxy through the same cross-assembly mapping as the proxy emitter.
        /// </summary>
        private string ProxyName(ObjectTypeDesign type)
        {
            return "global::" + m_proxies.ResolveProxyNamespaceForType(type) + "." +
                type.SymbolicName.Name + "Client";
        }

        /// <summary>
        /// Derives a stable member name while preserving the literal BrowseName separately.
        /// </summary>
        private static string MemberName(string browseName)
        {
            string name = string.Concat(browseName.Replace("<Nr>", string.Empty, StringComparison.Ordinal)
                .Where(character => char.IsLetterOrDigit(character) || character == '_'));
            return name.Length == 0 || char.IsDigit(name[0]) ? "_" + name : name;
        }

        /// <summary>
        /// Reports ambiguous C# accessors rather than silently dropping a model member.
        /// </summary>
        private static void RequireUnique(HashSet<string> names, string receiver, string method)
        {
            if (!names.Add(method))
            {
                throw new InvalidOperationException($"Client accessor '{receiver}.{method}' is ambiguous.");
            }
        }

        /// <summary>The validated model generation context.</summary>
        private readonly IGeneratorContext m_context;

        /// <summary>The existing proxy namespace and exclusion resolver.</summary>
        private readonly ObjectTypeProxyGenerator m_proxies;

        /// <summary>The generated extension file, sharing the standard generated-code header.</summary>
        private static readonly TemplateString s_file = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            #nullable enable

            namespace {{Tokens.Namespace}}
            {
                /// <summary>
                /// Model-derived variable and placeholder accessors over existing generated proxies.
                /// </summary>
                public static partial class {{Tokens.ClassName}}
                {
                    {{Tokens.ListOfTypes}}
                }
            }

            """);
    }
}
