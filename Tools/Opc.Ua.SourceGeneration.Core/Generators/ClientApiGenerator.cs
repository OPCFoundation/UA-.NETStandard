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
    /// Generates client API code for the services defined in the model.
    /// </summary>
    internal sealed class ClientApiGenerator : IGenerator
    {
        /// <summary>
        /// Create client api generator.
        /// </summary>
        public ClientApiGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Returns the service sets available.
        /// </summary>
        private static List<ServiceSet> ServiceSets =>
        [
            new ServiceSet("Session", ServiceCategory.Session, ServiceCategory.Test),
            new ServiceSet("Discovery", ServiceCategory.Discovery),
            new ServiceSet("Registration", ServiceCategory.Registration)
        ];

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            List<ServiceSet> serviceSets = ServiceSets;
            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.Client.g.cs", Constants.CoreNamespacePrefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, ClientApiTemplates.File);

            template.AddReplacement(Tokens.Prefix, Constants.CoreNamespacePrefix);
            template.AddReplacement(Tokens.Namespace, Constants.CoreNamespace);

            template.AddReplacement(
                Tokens.ServiceSets,
                ClientApiTemplates.ServiceSet,
                serviceSets,
                WriteTemplate_ClientApiServiceSet);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Writes the client API service set.
        /// </summary>
        private bool WriteTemplate_ClientApiServiceSet(IWriteContext context)
        {
            if (context.Target is not ServiceSet serviceSet)
            {
                return false;
            }

            // get datatypes.
            Service[] serviceTypes = m_context.ModelDesign.GetListOfServices(serviceSet.Categories);
            if (serviceTypes.Length == 0)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.ServiceSet, serviceSet.Name);

            context.Template.AddReplacement(
                Tokens.ClientMethod,
                ClientApiTemplates.InterfaceMethods,
                serviceTypes,
                context => WriteTemplate_ClientApiMethod(
                    context,
                    isInterface: true));

            context.Template.AddReplacement(
                Tokens.ClientApi,
                ClientApiTemplates.Methods,
                serviceTypes,
                context => WriteTemplate_ClientApiMethod(
                    context,
                    isInterface: false));

            return context.Template.Render();
        }

        /// <summary>
        /// Writes a client API method.
        /// </summary>
        private bool WriteTemplate_ClientApiMethod(IWriteContext context, bool isInterface)
        {
            if (context.Target is not Service serviceType)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.Name, serviceType.Name);
            context.Template.AddReplacement(Tokens.Namespace, Constants.CoreNamespace);

            context.Template.AddReplacement(
                Tokens.ClientMethodSync,
                [serviceType],
                context => LoadTemplate_SyncParameters(
                    context,
                    isInterface));

            context.Template.AddReplacement(
                Tokens.ClientMethodAsync,
                [serviceType],
                context => LoadTemplate_AsyncParameters(
                    context,
                    isInterface));

            context.Template.AddReplacement(
                Tokens.ClientMethodBegin,
                [serviceType],
                context => LoadTemplate_BeginAsyncParameters(
                    context,
                    isInterface));

            context.Template.AddReplacement(
                Tokens.ClientMethodEnd,
                [serviceType],
                context => LoadTemplate_EndAsyncParameters(
                    context,
                    isInterface));

            context.Template.AddReplacement(
                Tokens.RequestParameters,
                [serviceType],
                LoadTemplate_RequestParameters);

            context.Template.AddReplacement(
                Tokens.ResponseParameters,
                [serviceType],
                LoadTemplate_ResponseParameters);

            return context.Template.Render();
        }

        /// <summary>
        /// Writes a synchronous method declaration.
        /// </summary>
        private TemplateString LoadTemplate_SyncParameters(
            ILoadContext context,
            bool isInterface)
        {
            if (context.Target is not Service serviceType)
            {
                return null;
            }

            List<string> types = [];
            List<string> names = [];

            CollectParameters(serviceType.Request, false, types, names);
            CollectParameters(serviceType.Response, true, types, names);

            // write method type if not writing an interface declaration.
            if (!isInterface)
            {
                context.Out.Write("public virtual ");
            }

            context.Out.WriteLine("{0} {1}(", GetReturnType(serviceType), serviceType.Name);

            WriteParameters(context, types, names);

            // write closing semicolon for interface.
            if (isInterface)
            {
                context.Out.WriteLine(";");
            }

            return null;
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

            CollectParameters(serviceType.Request, false, types, names);

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
        /// Writes a begin asynchronous method declaration.
        /// </summary>
        private TemplateString LoadTemplate_BeginAsyncParameters(
            ILoadContext context,
            bool isInterface)
        {
            if (context.Target is not Service serviceType)
            {
                return null;
            }

            List<string> types = [];
            List<string> names = [];

            CollectParameters(serviceType.Request, false, types, names);

            types.Add("AsyncCallback");
            names.Add("callback");
            types.Add("object");
            names.Add("asyncState");

            if (!isInterface)
            {
                context.Out.Write("public virtual ");
            }

            context.Out.WriteLine("IAsyncResult Begin{0}(", serviceType.Name);

            WriteParameters(context, types, names);

            // write closing semicolon for interface.
            if (isInterface)
            {
                context.Out.WriteLine(";");
            }

            return null;
        }

        /// <summary>
        /// Writes an end asynchronous method declaration.
        /// </summary>
        private TemplateString LoadTemplate_EndAsyncParameters(
            ILoadContext context,
            bool isInterface)
        {
            if (context.Target is not Service serviceType)
            {
                return null;
            }

            List<string> types = [];
            List<string> names = [];

            types.Add("IAsyncResult");
            names.Add("result");

            CollectParameters(serviceType.Response, true, types, names);

            if (!isInterface)
            {
                context.Out.Write("public virtual ");
            }

            context.Out.WriteLine("{0} End{1}(", GetReturnType(serviceType), serviceType.Name);

            WriteParameters(context, types, names);

            // write closing semicolon for interface.
            if (isInterface)
            {
                context.Out.WriteLine(";");
            }

            return null;
        }

        /// <summary>
        /// Copies the request paramaters into the request object.
        /// </summary>
        private TemplateString LoadTemplate_RequestParameters(ILoadContext context)
        {
            if (context.Target is not Service serviceType)
            {
                return null;
            }

            if (serviceType.Request?.Fields != null)
            {
                foreach (Parameter field in serviceType.Request.Fields)
                {
                    context.Out.Write("request.");
                    context.Out.Write(field.Name);
                    context.Out.Write(" = ");
                    context.Out.Write(field.Name.ToLowerCamelCase());
                    context.Out.WriteLine(";");
                }
            }

            return null;
        }

        /// <summary>
        /// Copies the response paramaters into the request object.
        /// </summary>
        private TemplateString LoadTemplate_ResponseParameters(ILoadContext context)
        {
            if (context.Target is not Service serviceType)
            {
                return null;
            }

            if (serviceType.Response?.Fields != null)
            {
                bool first = true;

                foreach (Parameter field in serviceType.Response.Fields)
                {
                    if (first)
                    {
                        first = false;
                        continue;
                    }
                    context.Out.Write(field.Name.ToLowerCamelCase());
                    context.Out.Write(" = response.");
                    context.Out.Write(field.Name);
                    context.Out.WriteLine(";");
                }
            }

            return null;
        }

        /// <summary>
        /// Collects the parameters to write.
        /// </summary>
        private void CollectParameters(
            DataTypeDesign dataType,
            bool output,
            List<string> types,
            List<string> names)
        {
            Parameter[] fields = dataType?.Fields;
            if (fields != null)
            {
                bool first = true;

                foreach (Parameter field in fields)
                {
                    // first parameter is the return parameter.
                    if (first && output)
                    {
                        first = false;
                        continue;
                    }

                    DataTypeDesign datatype = field.DataTypeNode;
                    string typeName = datatype.GetDotNetTypeName(
                        field.ValueRank,
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces,
                        nullable: NullableAnnotation.Nullable,
                        useArrayTypeInsteadOfCollection: true);

                    // prefix out parameters.
                    if (output)
                    {
                        typeName = "out " + typeName;
                    }

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
        /// Gets the return type for the service.
        /// </summary>
        private string GetReturnType(Service serviceType)
        {
            string returnType = "void";

            if (serviceType.Response?.Fields != null &&
                serviceType.Response.Fields.Length > 0)
            {
                DataTypeDesign datatype = serviceType.Response.Fields[0].DataTypeNode;
                if (datatype != null)
                {
                    returnType = datatype.GetDotNetTypeName(
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces,
                        nullable: NullableAnnotation.Nullable,
                        false); // Use raw types as return type.
                }
            }
            return returnType;
        }

        /// <summary>
        /// A set of services that are grouped into a single interface.
        /// </summary>
        private sealed record class ServiceSet(string Name, params ServiceCategory[] Categories);

        private readonly IGeneratorContext m_context;
    }
}
