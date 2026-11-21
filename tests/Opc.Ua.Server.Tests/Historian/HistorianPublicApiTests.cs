// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Client.Historian;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Verifies that public historian collection APIs use the stack's collection and byte-string abstractions.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable]
    public sealed class HistorianPublicApiTests
    {
        /// <summary>
        /// Verifies that public historian collections use ArrayOf and binary payloads use ByteString.
        /// </summary>
        [Test]
        public void PublicHistorianCollectionsUseArrayOfAndByteString()
        {
            var violations = new List<string>();
            InspectNamespace(
                typeof(IHistorianProvider).Assembly,
                "Opc.Ua.Server.Historian",
                violations);
            InspectNamespace(
                typeof(HistoryClient).Assembly,
                "Opc.Ua.Client.Historian",
                violations);
            InspectTypes(
                typeof(SharedKeyValueHistorianProvider).Assembly,
                static type =>
                    string.Equals(
                        type.Namespace,
                        "Opc.Ua.Redundancy.Server",
                        StringComparison.Ordinal) &&
                    (type.Name.Contains(
                        "Historian",
                        StringComparison.Ordinal) ||
                        type.Name.Contains(
                            "HistoryContinuation",
                            StringComparison.Ordinal)),
                violations);
            InspectType(
                typeof(HistoricalAccessProfileDescriptor),
                violations);

            Assert.That(violations, Is.Empty);
        }

        private static void InspectNamespace(
            Assembly assembly,
            string namespaceName,
            List<string> violations)
        {
            InspectTypes(
                assembly,
                type => string.Equals(
                    type.Namespace,
                    namespaceName,
                    StringComparison.Ordinal),
                violations);
        }

        private static void InspectTypes(
            Assembly assembly,
            Func<Type, bool> predicate,
            List<string> violations)
        {
            foreach (Type type in assembly.GetExportedTypes()
                .Where(predicate))
            {
                InspectType(type, violations);
            }
        }

        private static void InspectType(
            Type type,
            List<string> violations)
        {
            const BindingFlags declaredMembers =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly;
            foreach (ConstructorInfo constructor in
                type.GetConstructors(declaredMembers))
            {
                if (!IsVisible(constructor))
                {
                    continue;
                }
                foreach (ParameterInfo parameter in
                    constructor.GetParameters())
                {
                    CheckType(
                        parameter.ParameterType,
                        $"{type.FullName}.{constructor.Name}({parameter.Name})",
                        violations);
                }
            }
            foreach (MethodInfo method in type.GetMethods(declaredMembers))
            {
                if (!IsVisible(method) || method.IsSpecialName)
                {
                    continue;
                }
                CheckType(
                    method.ReturnType,
                    $"{type.FullName}.{method.Name} return",
                    violations);
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    CheckType(
                        parameter.ParameterType,
                        $"{type.FullName}.{method.Name}({parameter.Name})",
                        violations);
                }
            }
            foreach (PropertyInfo property in
                type.GetProperties(declaredMembers))
            {
                MethodInfo accessor =
                    (property.GetMethod ?? property.SetMethod)!;
                if (accessor != null && IsVisible(accessor))
                {
                    CheckType(
                        property.PropertyType,
                        $"{type.FullName}.{property.Name}",
                        violations);
                }
            }
            foreach (FieldInfo field in type.GetFields(declaredMembers))
            {
                if (field.IsPublic ||
                    field.IsFamily ||
                    field.IsFamilyOrAssembly)
                {
                    CheckType(
                        field.FieldType,
                        $"{type.FullName}.{field.Name}",
                        violations);
                }
            }
        }

        private static bool IsVisible(MethodBase method)
        {
            return method.IsPublic ||
                method.IsFamily ||
                method.IsFamilyOrAssembly;
        }

        private static void CheckType(
            Type type,
            string member,
            List<string> violations)
        {
            if (type.IsByRef || type.IsPointer)
            {
                CheckType(type.GetElementType()!, member, violations);
                return;
            }
            if (type.IsArray)
            {
                violations.Add($"{member} exposes {type}.");
                return;
            }
            if (type == typeof(ReadOnlyMemory<byte>))
            {
                violations.Add(
                    $"{member} exposes ReadOnlyMemory<byte> instead of ByteString.");
                return;
            }
            if (!type.IsGenericType)
            {
                return;
            }
            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(IList<>) ||
                definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IReadOnlyCollection<>) ||
                definition == typeof(IReadOnlyDictionary<,>) ||
                definition == typeof(Dictionary<,>))
            {
                violations.Add($"{member} exposes {type}.");
                return;
            }
            foreach (Type argument in type.GetGenericArguments())
            {
                CheckType(argument, member, violations);
            }
        }
    }
}
