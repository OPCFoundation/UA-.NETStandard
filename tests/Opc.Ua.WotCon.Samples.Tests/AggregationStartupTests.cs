/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using AggregationServer;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// Tests aggregation sample target frameworks and protocol executor registration.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    public sealed class AggregationStartupTests
    {
        /// <summary>
        /// Verifies that sample targets only executable aggregation frameworks.
        /// </summary>
        [Test]
        public void SampleTargetsOnlyExecutableAggregationFrameworks()
        {
            XDocument server = LoadProject("AggregationServer");
            Assert.That(
                ReadProperty(server, "TargetFrameworks", "'$(CustomTestTarget)' == ''"),
                Is.EqualTo("net10.0;net11.0"));
            Assert.That(
                ReadProperty(server, "RestrictForLegacyTfm", null),
                Is.EqualTo("true"));

            Assert.That(
                ReadProperty(LoadProject("AggregationClient"), "TargetFrameworks", null),
                Is.EqualTo("$(AppTargetFrameworks)"));
            Assert.That(
                ReadProperty(LoadProject("FlatTagServer"), "TargetFrameworks", null),
                Is.EqualTo("$(AppTargetFrameworks)"));
        }

        /// <summary>
        /// Verifies that the aggregation host registers an OPC UA binding executor.
        /// </summary>
        [Test]
        public void AggregationHostRegistersOpcUaExecutor()
        {
            using IHost host = AggregationServerHost.Build(
                new AggregationServerOptions
                {
                    EndpointUrl = "opc.tcp://127.0.0.1:62550/AggregationServerStartupTest",
                    ApplicationName = "AggregationServerStartupTest"
                });
            var executorIds = new List<string>();
            foreach (IWotBindingExecutor executor
                in host.Services.GetServices<IWotBindingExecutor>())
            {
                executorIds.Add(executor.Identity.Id);
            }

            Assert.That(
                executorIds,
                Does.Contain("opc.opcua"),
                "The documented OPC UA mappings must have a runtime executor.");
        }

        private static XDocument LoadProject(string projectName)
        {
            return XDocument.Load(
                Path.Combine(
                    FindRepositoryRoot(),
                    "samples",
                    "WotCon",
                    projectName,
                    projectName + ".csproj"));
        }

        private static string ReadProperty(
            XDocument project,
            string propertyName,
            string? condition)
        {
            foreach (XElement property in project.Descendants(propertyName))
            {
                string? actualCondition = property.Attribute("Condition")?.Value;
                if (string.Equals(actualCondition, condition, StringComparison.Ordinal))
                {
                    return property.Value;
                }
            }
            throw new AssertionException(
                $"Property '{propertyName}' with condition '{condition}' was not found.");
        }

        private static string FindRepositoryRoot()
        {
            string? configuredRoot = Environment.GetEnvironmentVariable("OPCUA_TEST_REPOSITORY_ROOT");
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                if (!File.Exists(Path.Combine(configuredRoot, "UA.slnx")))
                {
                    throw new DirectoryNotFoundException("The configured repository root has no UA.slnx.");
                }
                return configuredRoot;
            }
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("The repository root was not found.");
        }
    }
}
