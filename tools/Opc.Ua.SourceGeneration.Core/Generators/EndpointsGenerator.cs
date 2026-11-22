// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
                    ServiceCategory.Attribute,
                    ServiceCategory.View,
                    ServiceCategory.Method,
                    ServiceCategory.MonitoredItem,
                    ServiceCategory.Subscription,
                    ServiceCategory.NodeManagement,
                    ServiceCategory.Query,
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
        private TemplateString? LoadTemplate_InvokeServiceAsyncParameters(ILoadContext context)
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

            if (serviceType.Request?.Fields is { Length: > 0 } fields)
            {
                foreach (Parameter field in fields)
                {
                    context.Out.WriteLine(",");
                    context.Out.Write("    request.{0}", field.Name);
                }
            }

            context.Out.WriteLine(",");
            context.Out.WriteLine("    requestLifetime).ConfigureAwait(false);");
            return null;
        }

        /// <summary>
        /// Writes a synchronous method declaration for known types.
        /// </summary>
        private TemplateString? LoadTemplate_KnownType(ILoadContext context)
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
