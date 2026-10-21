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
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Guards that sensitive parameters of the stack's source-generated log
    /// messages carry a data classification attribute.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class LogClassificationCoverageTests
    {
        private static readonly Type[] s_sensitiveTypes =
        [
            typeof(Uri),
            typeof(UriBuilder),
            typeof(EndPoint),
            typeof(IPAddress),
            typeof(PhysicalAddress),
            typeof(X509Certificate2)
        ];

        private static readonly HashSet<string> s_sensitiveNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "url",
            "endpointUrl",
            "discoveryUrl",
            "proxyUrl",
            "clientUrl",
            "serverUrl",
            "idnHost",
            "host",
            "hostName",
            "ipAddress",
            "remoteEndpoint",
            "localEndpoint",
            "port",
            "thumbprint",
            "subject",
            "subjectName",
            "userName",
            "storePath",
            "filePath",
            "applicationUri"
        };

        [Test]
        public void SensitiveLogParametersAreClassified()
        {
            var missing = new List<string>();
            foreach (Assembly assembly in LoadStackAssemblies())
            {
                foreach (MethodInfo method in GetLogMethods(assembly))
                {
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        if (IsSensitive(parameter) &&
                            parameter.GetCustomAttribute<DataClassificationAttribute>() == null)
                        {
                            missing.Add(
                                $"{assembly.GetName().Name}: {method.DeclaringType?.Name}." +
                                $"{method.Name}({parameter.ParameterType.Name} {parameter.Name})");
                        }
                    }
                }
            }

            Assert.That(missing, Is.Empty, string.Join(Environment.NewLine, missing));
        }

        [Test]
        public void StackAssembliesAreScanned()
        {
            Assert.That(
                LoadStackAssemblies().Select(a => a.GetName().Name),
                Does.Contain("Opc.Ua.Core"));
        }

        private static bool IsSensitive(ParameterInfo parameter)
        {
            if (typeof(Exception).IsAssignableFrom(parameter.ParameterType) ||
                typeof(ILogger).IsAssignableFrom(parameter.ParameterType))
            {
                return false;
            }
            Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            return s_sensitiveTypes.Any(t => t.IsAssignableFrom(type)) ||
                (parameter.Name != null && s_sensitiveNames.Contains(parameter.Name));
        }

        private static IEnumerable<MethodInfo> GetLogMethods(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = [.. e.Types.Where(t => t != null)!];
            }

            return types
                .SelectMany(t => t.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttribute<LoggerMessageAttribute>() != null);
        }

        private static List<Assembly> LoadStackAssemblies()
        {
            var assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(AppContext.BaseDirectory, "Opc.Ua.*.dll"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".TestFramework", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Opc.Ua.Test.", StringComparison.OrdinalIgnoreCase) ||
                    name is "Opc.Ua.Types" or "Opc.Ua.Core.Types")
                {
                    continue;
                }
                try
                {
                    assemblies[name] = Assembly.Load(new AssemblyName(name));
                }
                catch (Exception e) when (e is FileLoadException or BadImageFormatException)
                {
                    // not a loadable stack assembly
                }
            }
            return [.. assemblies.Values];
        }
    }
}
