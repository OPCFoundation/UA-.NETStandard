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

using System.Linq;
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Tests.ProtocolProvider.XRegistryProviderCoverage;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryQueryTests
    {
        [Test]
        public async Task PaginationPreservesSortingFilterAndInlineAcrossItsOpaqueCursorAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(Options(k_model) with { PageSize = 1 },
                new InMemoryXRegistryTransactionStore());
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse first = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups") with
            {
                Parameters = [new("sort", "age=desc"), new("filter", "age>=2"), new("inline", "schemas")]
            }).ConfigureAwait(false);
            Assert.That(first.StatusCode, Is.EqualTo(200), first.Error?.Detail);
            Assert.That(first.Links[0].Count, Is.EqualTo(3UL));
            Assert.That(first.Expires, Is.Not.Null);
            Assert.That(first.Metadata.EnumerateObject().Select(item => item.Name), Is.EqualTo(s_groupC));
            XRegistryResponse second = await endpoint.ExecuteAsync(Continue(first)).ConfigureAwait(false);
            Assert.That(second.Metadata.EnumerateObject().Select(item => item.Name), Is.EqualTo(s_groupB));
            XRegistryResponse third = await endpoint.ExecuteAsync(Continue(second)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(third.Metadata.EnumerateObject().Select(item => item.Name), Is.EqualTo(s_groupA));
                Assert.That(
                    third.Metadata.GetProperty("a").GetProperty("schemas").EnumerateObject().Count(), Is.EqualTo(2));
                Assert.That(third.Links.Count, Is.Zero);
            });
        }

        [TestCase("caller")]
        [TestCase("path")]
        [TestCase("view")]
        [TestCase("extra")]
        [TestCase("signature")]
        [TestCase("mutation")]
        public async Task ContinuationsRejectChangedScopeQueriesAndGenerationsAsync(string change)
        {
            using var endpoint = new XRegistryTransactionalEndpoint(Options(k_model) with { PageSize = 1 },
                new InMemoryXRegistryTransactionStore());
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse page = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups"))
                .ConfigureAwait(false);
            XRegistryRequest next = Continue(page);
            next = change switch
            {
                "caller" => next with { Context = new XRegistryCallContext("other") },
                "path" => new XRegistryRequest(XRegistryAction.Read, "/other")
                {
                    Context = next.Context,
                    View = next.View,
                    Parameters = next.Parameters
                },
                "view" => next with { View = XRegistryView.Default },
                "extra" => next with { Parameters = [next.Parameters[0], new("filter", "name=x")] },
                "signature" => next with { Parameters = [new("cursor", next.Parameters[0].Value + "a")] },
                _ => next
            };
            if (change == "mutation")
            {
                Assert.That((await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/groups/a", "{}"))
                    .ConfigureAwait(false)).StatusCode, Is.EqualTo(200));
            }
            XRegistryResponse rejected = await endpoint.ExecuteAsync(next).ConfigureAwait(false);
            Assert.That(rejected.Error?.Code, Is.EqualTo("bad_flag"), rejected.Error?.Detail);
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("18446744073709551616")]
        [TestCase("bad")]
        public async Task InvalidPaginationLimitsAreRejectedBeforeMutationAsync(string limit)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups") with
            {
                Parameters = [new("limit", limit)]
            }).ConfigureAwait(false);
            Assert.That(rejected.Error?.Code, Is.EqualTo("bad_flag"));
        }

        [TestCase("name=al*", "a")]
        [TestCase("name!=ALPHA", "b,c")]
        [TestCase("name<>ALPHA", "b,c")]
        [TestCase("missing=null", "a,b,c")]
        [TestCase("missing!=null", "")]
        [TestCase("missing!=value", "a,b,c")]
        [TestCase("age>=10", "b,c")]
        [TestCase("age<2", "")]
        [TestCase("info['a.b']=matched", "b")]
        [TestCase("info.list[*]=right", "b")]
        [TestCase("labels.*=PROD", "a")]
        [TestCase("name", "a,b,c")]
        public async Task FiltersUseTypedValuesAndQuotedOrWildcardPathsAsync(string filter, string ids)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse result = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups") with
            {
                Parameters = [new("filter", filter)]
            }).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(200), result.Error?.Detail);
            Assert.That(string.Join(",", result.Metadata.EnumerateObject().Select(item => item.Name)), Is.EqualTo(ids));
        }

        [Test]
        public async Task FilterAndOrPathsPruneDescendantsAndRecomputeCountsWithoutImplicitInliningAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse filtered = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/") with
            {
                Parameters =
                    [new("filter", "groups.groupid=a,groups.schemas.name=selected"), new("inline", "groups.schemas")]
            }).ConfigureAwait(false);
            Assert.That(filtered.StatusCode, Is.EqualTo(200), filtered.Error?.Detail);
            Assert.Multiple(() =>
            {
                Assert.That(filtered.Metadata.GetProperty("groupscount").GetInt32(), Is.EqualTo(1));
                Assert.That(filtered.Metadata.GetProperty("groups").EnumerateObject().Select(item => item.Name),
                    Is.EqualTo(s_groupA));
                Assert.That(filtered.Metadata.GetProperty("groups").GetProperty("a").GetProperty("schemascount")
                    .GetInt32(), Is.EqualTo(1));
                Assert.That(filtered.Metadata.GetProperty("groups").GetProperty("a").GetProperty("schemas")
                    .EnumerateObject().Select(item => item.Name), Is.EqualTo(s_resourceOne));
            });
            XRegistryResponse union = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/") with
            {
                Parameters = [new("filter", "groups.groupid=b"), new("filter", "groups.schemas.name=selected")]
            }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(union.Metadata.GetProperty("groupscount").GetInt32(), Is.EqualTo(2));
                Assert.That(union.Metadata.TryGetProperty("groups", out _), Is.False);
            });
        }

        [Test]
        public async Task ExactInlinePathDoesNotEnableUnrelatedCollectionsOrConfigurationAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/") with
            {
                Parameters = [new("inline", "groups.schemas.versions")]
            }).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(200), response.Error?.Detail);
            var resource =
                response.Metadata.GetProperty("groups").GetProperty("a").GetProperty("schemas").GetProperty("one");
            Assert.Multiple(() =>
            {
                Assert.That(resource.TryGetProperty("versions", out _), Is.True);
                Assert.That(resource.TryGetProperty("meta", out _), Is.False);
                Assert.That(resource.TryGetProperty("schemabase64", out _), Is.False);
                Assert.That(response.Metadata.TryGetProperty("other", out _), Is.False);
            });
            XRegistryResponse all = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/") with
            {
                Parameters = [new("inline", "*")]
            }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(all.Metadata.TryGetProperty("model", out _), Is.False);
                Assert.That(all.Metadata.TryGetProperty("modelsource", out _), Is.False);
                Assert.That(all.Metadata.TryGetProperty("capabilities", out _), Is.False);
                Assert.That(all.Metadata.GetProperty("groups").GetProperty("a").GetProperty("schemas")
                    .GetProperty("one").GetProperty("versions").GetProperty("v1").TryGetProperty("schemabase64", out _),
                    Is.True);
            });
        }

        [TestCase("missing")]
        [TestCase("*.schemas")]
        [TestCase("groups.name")]
        [TestCase("groups.*.schemas")]
        public async Task InvalidInlineCannotCommitAnOtherwiseValidMutationAsync(string inline)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """{"name":"must-not-commit"}""") with
            { Parameters = [new("inline", inline)] })
                .ConfigureAwait(false);
            XRegistryResponse root =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.Error?.Code, Is.EqualTo("bad_inline"));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(root.Metadata.TryGetProperty("name", out _), Is.False);
            });
        }

        [TestCase("age", "a,b,c")]
        [TestCase("age=desc", "c,b,a")]
        [TestCase("missing=desc", "c,b,a")]
        [TestCase("name=desc", "c,b,a")]
        public async Task SortingUsesNumericKeysAndSameDirectionIdentityTiesAsync(string sort, string ids)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            await SeedAsync(endpoint).ConfigureAwait(false);
            XRegistryResponse result = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups") with
            {
                Parameters = [new("sort", sort)]
            }).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(200), result.Error?.Detail);
            Assert.That(string.Join(",", result.Metadata.EnumerateObject().Select(item => item.Name)), Is.EqualTo(ids));
        }

        [Test]
        public async Task IgnoreAppliesToEntityFieldsWithoutErasingNestedDomainDataAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(k_model);
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/copy",
                /*lang=json,strict*/ """
                {"groupid":"original","epoch":99,"info":{"epoch":17,"id":"domain"},
                 "schemas":{"r":{"versionid":"v1","epoch":99,
                   "meta":{"epoch":99,"defaultversionid":"absent","defaultversionsticky":true}}}}
                """) with
            { Parameters = [new("ignore", "id,epoch,defaultversionid,defaultversionsticky")] })
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(201), response.Error?.Detail);
                Assert.That(response.Metadata.GetProperty("groupid").GetString(), Is.EqualTo("copy"));
                Assert.That(response.Metadata.GetProperty("info").GetProperty("epoch").GetInt32(), Is.EqualTo(17));
            });
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/copy",
                /*lang=json,strict*/ """{"schemas":{"r":{"schemaid":"different","versionid":"v1"}}}""") with
            {
                Parameters = [new("ignore", "id")]
            }).ConfigureAwait(false);
            Assert.That(rejected.Error?.Code, Is.EqualTo("mismatched_id"));
        }

        [TestCase("b", "b", true)]
        [TestCase("null", "c", false)]
        public async Task DefaultSelectionCanAccompanyDeletingTheStickyVersionAsync(
            string flag, string selected, bool sticky)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse created =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g/schemas/r",
                /*lang=json,strict*/ """{"versions":{"a":{},"b":{},"c":{}},"meta":{"defaultversionid":"a"}}"""))
                .ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse deleted = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Delete, "/groups/g/schemas/r/versions/a") with
                { Parameters = [new("setdefaultversionid", flag)] }).ConfigureAwait(false);
            XRegistryResponse meta =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g/schemas/r/meta"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(deleted.StatusCode, Is.EqualTo(204), deleted.Error?.Detail);
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo(selected));
                Assert.That(meta.Metadata.GetProperty("defaultversionsticky").GetBoolean(), Is.EqualTo(sticky));
            });
        }

        [Test]
        public async Task DefaultRequestSelectsAssignedVersionAndOverridesBodySelectionAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse response =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Create, "/groups/g/schemas/r",
                /*lang=json,strict*/ """{"meta":{"defaultversionid":"absent","defaultversionsticky":false}}""") with
                {
                    Parameters = [new("setdefaultversionid", "request")]
                }).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(201), response.Error?.Detail);
            XRegistryResponse meta =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g/schemas/r/meta"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("1"));
                Assert.That(meta.Metadata.GetProperty("defaultversionsticky").GetBoolean(), Is.True);
            });
        }

        [TestCase("1.0-RC4")]
        [TestCase("1.0.987654321-rc4")]
        public async Task SpecVersionComparisonIgnoresPatchAndSuffixCaseAsync(string version)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/") with
            {
                Parameters = [new("specversion", version)]
            }).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(200), response.Error?.Detail);
        }

        [TestCase(XRegistryAction.Delete, "inline", "absent", "bad_inline")]
        [TestCase(XRegistryAction.Delete, "sort", "name", "sort_noncollection")]
        [TestCase(XRegistryAction.Delete, "ignore", "absent", "bad_ignore")]
        [TestCase(XRegistryAction.Describe, "filter", "labels[", "bad_filter")]
        [TestCase(XRegistryAction.Merge, "specversion", "1.0-rc3", "unsupported_specversion")]
        public async Task InvalidFlagsRejectBeforeActionsWithNoBodyResponseAsync(
            XRegistryAction action, string flag, string value, string code)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g", "{}"))
                .ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201));
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(action, "/groups/g") with
            { Parameters = [new(flag, value)] }).ConfigureAwait(false);
            XRegistryResponse unchanged = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(400), rejected.Error?.Detail);
                Assert.That(rejected.Error?.Code, Is.EqualTo(code));
                Assert.That(unchanged.StatusCode, Is.EqualTo(200));
                Assert.That(unchanged.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [TestCase("stamp=2026-09-12T08:30:00Z", 1)]
        [TestCase("stamp>2026-09-12T09:00:00+01:00", 1)]
        [TestCase("stamp!=2026-09-12T10:30:00+02:00", 0)]
        [TestCase("text=2026-09-12T08:30:00Z", 0)]
        public async Task TimestampFiltersUseTheDeclaredTypeRatherThanTheAttributeNameAsync(string filter, int count)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","attributes":{
                  "stamp":{"type":"timestamp"},"text":{"type":"string"}
                }}}}
                """);
            XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                /*lang=json,strict*/ """
                {"stamp":"2026-09-12T10:30:00+02:00","text":"2026-09-12T10:30:00+02:00"}
                """)).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201));
            XRegistryResponse filtered = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups") with
            { Parameters = [new("filter", filter)] }).ConfigureAwait(false);
            Assert.That(filtered.StatusCode, Is.EqualTo(200), filtered.Error?.Detail);
            Assert.That(filtered.Metadata.EnumerateObject().Count(), Is.EqualTo(count));
        }

        [Test]
        public async Task SortingResourceMetaDoesNotRequireExposingItInTheResponseAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","resources":{"schemas":{
                  "singular":"schema","metaattributes":{"priority":{"type":"integer"}}
                }}}}}
                """);
            XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                /*lang=json,strict*/ """
                {"schemas":{"a":{"meta":{"priority":20}},"b":{"meta":{"priority":1}}}}
                """)).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse sorted =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g/schemas") with
                { Parameters = [new("sort", "meta.priority")] }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(sorted.StatusCode, Is.EqualTo(200), sorted.Error?.Detail);
                Assert.That(string.Join(",", sorted.Metadata.EnumerateObject().Select(property => property.Name)),
                    Is.EqualTo("b,a"));
                Assert.That(sorted.Metadata.GetProperty("a").TryGetProperty("meta", out _), Is.False);
            });
        }

        [TestCase("labels")]
        [TestCase("undefined")]
        [TestCase("labels[0]")]
        public async Task EmptyCollectionsStillRejectUndefinedOrNonScalarSortPathsAsync(string path)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups") with
            { Parameters = [new("sort", path)] }).ConfigureAwait(false);
            Assert.That(response.Error?.Code, Is.EqualTo("bad_sort"));
        }

        private static async Task SeedAsync(XRegistryTransactionalEndpoint endpoint)
        {
            XRegistryResponse response = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/",
                /*lang=json,strict*/ """
                {"groups":{
                  "a":{"name":"Alpha","age":2,"labels":{"stage":"prod"},
                       "schemas":{"one":{"versionid":"v1","name":"selected"},"two":{"versionid":"v1","name":"other"}}},
                  "b":{"name":"beta","age":10,"info":{"a.b":"matched","list":["left","right"]}},
                  "c":{"name":"gamma","age":1000000000000000000000000000000000000000}
                }}
                """)).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(200), response.Error?.Detail);
        }

        private static XRegistryRequest Continue(XRegistryResponse response)
        {
            string target = response.Links[0].Target;
            int separator = target.IndexOf("?cursor=", StringComparison.Ordinal);
            Assert.That(separator, Is.GreaterThan(0));
            return Request(XRegistryAction.Read, target[..separator]) with
            {
                Parameters = [new("cursor", target[(separator + 8)..])]
            };
        }

        private const string k_model = /*lang=json,strict*/ """
            {"groups":{
              "groups":{"singular":"group","attributes":{"*":{"type":"any"}},
                        "resources":{"schemas":{"singular":"schema"}}},
              "other":{"singular":"other","resources":{"schemas":{"singular":"schema"}}}
            }}
            """;

        private static readonly string[] s_groupA = ["a"];
        private static readonly string[] s_groupB = ["b"];
        private static readonly string[] s_groupC = ["c"];
        private static readonly string[] s_resourceOne = ["one"];
    }
}
