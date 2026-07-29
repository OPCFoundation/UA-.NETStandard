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
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using AggregationServer;

namespace Opc.Ua.WotCon.Samples.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    public sealed class AggregationStartupTests
    {
        [Test]
        public void SampleTargetsOnlyExecutableAggregationFrameworks()
        {
            XDocument server = LoadProject("AggregationServer");
            Assert.That(
                ReadProperty(server, "TargetFrameworks", "'$(CustomTestTarget)' == ''"),
                Is.EqualTo("net8.0;net9.0;net10.0"));
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
