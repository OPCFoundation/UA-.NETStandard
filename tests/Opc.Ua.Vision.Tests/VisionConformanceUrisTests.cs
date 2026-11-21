// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Locks down the <see cref="VisionConformanceUris"/> surface: every
    /// facet name has a matching URI, <see cref="VisionConformanceUris.AllFacets"/>
    /// enumerates them in the declared order without dropping any, and the
    /// internal <c>TryGetFacetUri</c> only ever accepts the VIS- prefix.
    /// </summary>
    [TestFixture]
    public sealed class VisionConformanceUrisTests
    {
        [Test]
        public void FacetBaseAndProfileBaseAreTheAdvertisedUris()
        {
            Assert.Multiple(() =>
            {
                Assert.That(VisionConformanceUris.FacetBase,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/Vision/Facet/"));
                Assert.That(VisionConformanceUris.ProfileBase,
                    Is.EqualTo("http://opcfoundation.org/UA-Profile/Vision/Server/"));
            });
        }

        [Test]
        public void AllFacetsEnumeratesEveryNameConstantExactlyOnceAndInDeclarationOrder()
        {
            IReadOnlyList<string> allFacets = MaterializeAllFacets();
            IReadOnlyList<string> declared = ReflectFacetNames();

            Assert.Multiple(() =>
            {
                Assert.That(allFacets, Is.EquivalentTo(declared),
                    "AllFacets must list every VisionConformanceUris.FacetNames constant exactly once; " +
                    "if this fails a name was added without extending the ordered AllFacets array.");
                Assert.That(allFacets.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(allFacets.Count),
                    "AllFacets must not contain duplicates.");
            });
        }

        [Test]
        public void EveryFacetNameHasAMatchingFacetUri()
        {
            IReadOnlyList<string> declared = ReflectFacetNames();
            IReadOnlyDictionary<string, string> uriByShort = ReflectFacetUrisByShortName();
            foreach (string name in declared)
            {
                Assert.That(uriByShort.ContainsKey(name),
                    $"Facet name '{name}' is declared under FacetNames but no matching FacetsUri constant exposes '{VisionConformanceUris.FacetBase}<Suffix>'.");
                Assert.That(uriByShort[name], Is.EqualTo(VisionConformanceUris.FacetBase + name["VIS-".Length..]),
                    $"The URI constant for '{name}' must be FacetBase + '{name["VIS-".Length..]}'.");
            }
        }

        [Test]
        public void TryGetFacetUriBuildsUriFromVisPrefixedShortName()
        {
            bool ok = InvokeTryGetFacetUri(VisionConformanceUris.FacetNames.Base, out string uri);

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(uri, Is.EqualTo(VisionConformanceUris.Facets.Base));
            });
        }

        [Test]
        public void TryGetFacetUriRejectsInputsWithoutVisPrefix()
        {
            bool ok = InvokeTryGetFacetUri("Foo-Base", out string uri);

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.False);
                Assert.That(uri, Is.EqualTo(string.Empty));
            });
        }

        [Test]
        public void TryGetFacetUriRejectsNullOrEmptyInput()
        {
            bool okNull = InvokeTryGetFacetUri(null!, out string uriNull);
            bool okEmpty = InvokeTryGetFacetUri(string.Empty, out string uriEmpty);

            Assert.Multiple(() =>
            {
                Assert.That(okNull, Is.False);
                Assert.That(uriNull, Is.EqualTo(string.Empty));
                Assert.That(okEmpty, Is.False);
                Assert.That(uriEmpty, Is.EqualTo(string.Empty));
            });
        }

        [Test]
        public void ProfileUrisAreDistinctAndUseTheProfileBaseConstant()
        {
            var uris = new[]
            {
                VisionConformanceUris.Profiles.Basic,
                VisionConformanceUris.Profiles.Inspection,
                VisionConformanceUris.Profiles.Detection,
                VisionConformanceUris.Profiles.Inference
            };

            Assert.Multiple(() =>
            {
                foreach (string uri in uris)
                {
                    Assert.That(uri, Does.StartWith(VisionConformanceUris.ProfileBase));
                }
                Assert.That(uris.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(uris.Length));
            });
        }

        private static IReadOnlyList<string> MaterializeAllFacets()
        {
            var result = new List<string>();
            ArrayOf<string> facets = VisionConformanceUris.AllFacets;
            for (int i = 0; i < facets.Count; i++)
            {
                result.Add(facets[i]);
            }
            return result;
        }

        private static IReadOnlyList<string> ReflectFacetNames()
        {
            var result = new List<string>();
            foreach (FieldInfo field in typeof(VisionConformanceUris.FacetNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly))
            {
                object? raw = field.GetRawConstantValue();
                if (raw is string s)
                {
                    result.Add(s);
                }
            }
            return result;
        }

        private static IReadOnlyDictionary<string, string> ReflectFacetUrisByShortName()
        {
            var byName = typeof(VisionConformanceUris.FacetNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly)
                .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);
            var byUri = typeof(VisionConformanceUris.Facets)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly)
                .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in byName)
            {
                if (byUri.TryGetValue(pair.Key, out string? uri))
                {
                    result[pair.Value] = uri;
                }
            }
            return result;
        }

        private static bool InvokeTryGetFacetUri(string name, out string facetUri)
        {
            MethodInfo? method = typeof(VisionConformanceUris).GetMethod(
                "TryGetFacetUri",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: [typeof(string), typeof(string).MakeByRefType()],
                modifiers: null);
            Assert.That(method, Is.Not.Null, "VisionConformanceUris.TryGetFacetUri must exist.");
            object?[] args = [name, string.Empty];
            bool ok = (bool)method!.Invoke(null, args)!;
            facetUri = (string)args[1]!;
            return ok;
        }
    }
}
