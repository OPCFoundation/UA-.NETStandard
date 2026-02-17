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
    /// Generates server API code for the stack.
    /// </summary>
    internal sealed class ServerApiGenerator : IGenerator
    {
        /// <summary>
        /// Create server API generator.
        /// </summary>
        public ServerApiGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            List<ServiceSet> serviceSets =
            [
                new ServiceSet("Session", ServiceCategory.Discovery, ServiceCategory.Session, ServiceCategory.Test),
                new ServiceSet("Discovery", ServiceCategory.Discovery, ServiceCategory.Registration)
            ];

            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.ServerBase.g.cs", Constants.CoreNamespacePrefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, ServerApiTemplates.File);

            template.AddReplacement(Tokens.Prefix, Constants.CoreNamespacePrefix);
            template.AddReplacement(Tokens.Namespace, Constants.CoreNamespace);

            template.AddReplacement(
                Tokens.ServiceSets,
                ServerApiTemplates.ServiceSet,
                serviceSets,
                WriteTemplate_ServerApiServiceSet);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Writes the server API service set.
        /// </summary>
        private bool WriteTemplate_ServerApiServiceSet(IWriteContext context)
        {
            if (context.Target is not ServiceSet serviceSet)
            {
                return false;
            }

            Service[] serviceTypes = m_context.ModelDesign.GetListOfServices(serviceSet.Categories);
            if (serviceTypes.Length == 0)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.ServiceSet, serviceSet.Name);

            context.Template.AddReplacement(
                Tokens.ServerApi,
                ServerApiTemplates.InterfaceMethod,
                serviceTypes,
                WriteTemplate_InterfaceMethod);

            context.Template.AddReplacement(
                Tokens.ServerStubs,
                ServerApiTemplates.Method,
                serviceTypes,
                WriteTemplate_ServerApiMethod);

            return context.Template.Render();
        }

        /// <summary>
        /// Writes an interface method declaration.
        /// </summary>
        private bool WriteTemplate_InterfaceMethod(IWriteContext context)
        {
            if (context.Target is not Service serviceType)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.Name, serviceType.Name);

            context.Template.AddReplacement(
                Tokens.ServerMethodAsync,
                [serviceType],
                context => LoadTemplate_AsyncParameters(
                    context,
                    isInterface: true));

            return context.Template.Render();
        }

        /// <summary>
        /// Writes a server API method.
        /// </summary>
        private bool WriteTemplate_ServerApiMethod(IWriteContext context)
        {
            if (context.Target is not Service serviceType)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.Name, serviceType.Name);
            context.Template.AddReplacement(Tokens.Namespace, Constants.CoreNamespace);

            context.Template.AddReplacement(
                Tokens.ServerMethodAsync,
                [serviceType],
                context => LoadTemplate_AsyncParameters(
                    context,
                    isInterface: false));

            return context.Template.Render();
        }

        /// <summary>
        /// Writes an asynchronous method declaration.
        /// </summary>
        private TemplateString LoadTemplate_AsyncParameters(
            ILoadContext context,
            bool isInterface)
        {
            if (context.Target is not Service serviceType)
            {
                return null;
            }

            List<string> types = [];
            List<string> names = [];

            types.Add("global::Opc.Ua.SecureChannelContext");
            names.Add("secureChannelContext");

            CollectParameters(serviceType.Request, types, names);

            types.Add("global::System.Threading.CancellationToken");
            names.Add("ct");

            // write method type if not writing an interface declaration.
            if (!isInterface)
            {
                context.Out.Write("public virtual async ");
            }

            context.Out.WriteLine(
                "global::System.Threading.Tasks.ValueTask<{0}Response> {1}Async(",
                serviceType.Name,
                serviceType.Name);

            WriteParameters(context, types, names);

            // write closing semicolon for interface.
            if (isInterface)
            {
                context.Out.WriteLine(";");
            }

            return null;
        }

        /// <summary>
        /// Collects the parameters to write.
        /// </summary>
        private void CollectParameters(
            DataTypeDesign dataType,
            List<string> types,
            List<string> names)
        {
            Parameter[] fields = dataType?.Fields;
            if (fields != null)
            {
                foreach (Parameter field in fields)
                {
                    DataTypeDesign datatype = field.DataTypeNode;
                    string typeName = datatype.GetDotNetTypeName(
                        field.ValueRank,
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces,
                        nullable: NullableAnnotation.Nullable,
                        useArrayTypeInsteadOfCollection: true);

                    types.Add(typeName);
                    names.Add(field.Name.ToLowerCamelCase());
                }
            }
        }

        /// <summary>
        /// Writes a set of method parameters.
        /// </summary>
        private static void WriteParameters(
            ILoadContext context,
            List<string> types,
            List<string> names)
        {
            for (int ii = 0; ii < types.Count; ii++)
            {
                string typeName = types[ii];

                context.Out.Write("    {0} {1}", typeName, names[ii]);

                if (ii < types.Count - 1)
                {
                    context.Out.WriteLine(",");
                }
                else
                {
                    context.Out.Write(")");
                }
            }
        }

        /// <summary>
        /// A set of services that are grouped into a single interface.
        /// </summary>
        private sealed record class ServiceSet(string Name, params ServiceCategory[] Categories);

        private readonly IGeneratorContext m_context;
    }
}
