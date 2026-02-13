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
    /// Generates endpoint code based on a UA Type Dictionary.
    /// </summary>
    internal sealed class EndpointsGenerator : IGenerator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EndpointsGenerator"/> class.
        /// </summary>
        public EndpointsGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            List<ServiceSet> serviceSets =
            [
                new ServiceSet(
                    "Session",
                    ServiceCategory.Discovery,
                    ServiceCategory.Session,
                    ServiceCategory.Test),
                new ServiceSet(
                    "Discovery",
                    ServiceCategory.Discovery,
                    ServiceCategory.Registration)
            ];

            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.Endpoints.g.cs", Constants.CoreNamespacePrefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, EndpointsTemplates.File);

            template.AddReplacement(Tokens.Prefix, Constants.CoreNamespacePrefix);
            template.AddReplacement(Tokens.Namespace, Constants.CoreNamespace);

            template.AddReplacement(
                Tokens.ServiceSets,
                EndpointsTemplates.ServiceSet,
                serviceSets,
                WriteTemplate_EndpointServiceSet);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Writes the endpoint service set.
        /// </summary>
        private bool WriteTemplate_EndpointServiceSet(IWriteContext context)
        {
            if (context.Target is not ServiceSet serviceSet)
            {
                return false;
            }

            // get datatypes.
            Service[] datatypes = m_context.ModelDesign.GetListOfServices(serviceSet.Categories);
            if (datatypes.Length == 0)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.ServiceSet, serviceSet.Name);

            context.Template.AddReplacement(
                Tokens.MethodList,
                EndpointsTemplates.Method,
                datatypes,
                WriteTemplate_EndpointMethod);

            context.Template.AddReplacement(
                Tokens.AddKnownType,
                datatypes,
                LoadTemplate_KnownType);

            return context.Template.Render();
        }

        /// <summary>
        /// Writes an endpoint method.
        /// </summary>
        private bool WriteTemplate_EndpointMethod(IWriteContext context)
        {
            if (context.Target is not Service serviceType)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.Name, serviceType.Name);

            context.Template.AddReplacement(
                Tokens.InvokeServiceAsync,
                [serviceType],
                LoadTemplate_InvokeServiceAsyncParameters);

            return context.Template.Render();
        }

        /// <summary>
        /// Writes an asynchronous method declaration.
        /// </summary>
        private TemplateString LoadTemplate_InvokeServiceAsyncParameters(ILoadContext context)
        {
            if (context.Target is not Service serviceType)
            {
                return null;
            }

            // write method declaration.
            context.Out.WriteLine(
                "response = await ServerInstance.{0}Async(",
                serviceType.Name);
            context.Out.Write("    secureChannelContext");

            if (serviceType.Request != null || serviceType.Request.Fields.Length > 0)
            {
                foreach (Parameter field in serviceType.Request.Fields)
                {
                    context.Out.WriteLine(",");
                    context.Out.Write("    request.{0}", field.Name);
                }
            }

            context.Out.WriteLine(",");
            context.Out.WriteLine("cancellationToken).ConfigureAwait(false);");
            return null;
        }

        /// <summary>
        /// Writes a synchronous method declaration for known types.
        /// </summary>
        private TemplateString LoadTemplate_KnownType(ILoadContext context)
        {
            if (context.Target is not Service serviceType)
            {
                return null;
            }

            // write method declaration.
            context.Out.WriteLine(
                "SupportedServices.Add(DataTypeIds.{0}Request, new ServiceDefinition(typeof({0}Request), new InvokeService({0}Async)));",
                serviceType.Name);
            context.Out.WriteLine();

            return null;
        }

        /// <summary>
        /// A set of services that are grouped into a single interface.
        /// </summary>
        private sealed record class ServiceSet(string Name, params ServiceCategory[] Categories);

        private readonly IGeneratorContext m_context;
    }
}
