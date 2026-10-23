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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private void PrepareQuery(Transaction transaction, XRegistryModelRules model, XRegistryTarget target)
        {
            if (transaction.QueryPrepared)
            {
                return;
            }
            transaction.QueryPrepared = true;
            ValidateInlinePaths(transaction.Request, model, target);
            if (HasFlag(transaction.Request, "limit") && !target.IsCollection)
            {
                throw new XRegistryRejectionException("bad_flag", "Pagination requires a collection.");
            }
            ValidateSortTarget(model, target, transaction.Request);
            XRegistryParameter[] filters =
                [.. transaction.Request.Parameters.ToList().Where(item => item.Name == "filter")];
            if (filters.Length == 0)
            {
                return;
            }
            if (filters.Any(item => item.Value == "excludeall"))
            {
                if (filters.Length != 1)
                {
                    throw new XRegistryRejectionException(
                        "bad_filter", "excludeall cannot be combined with other filters.");
                }
                transaction.QueryPaths = new HashSet<string>(StringComparer.Ordinal);
                return;
            }
            var clean = new Transaction(transaction.Snapshot, transaction.Snapshot,
                new XRegistryRequest(XRegistryAction.Read, target.Path) { View = XRegistryView.Metadata },
                m_time.GetUtcNow().UtcDateTime);
            var nodes = new List<QueryNode>();
            JsonObject completeModel = model.CompleteModel();
            List<QueryNode> roots = target.IsCollection
                ? Children(clean, target.Path).ConvertAll(
                    path => BuildQueryNode(clean, model, model.Resolve(path), completeModel, nodes))
                : [BuildQueryNode(clean, model, target.Kind == XRegistryEntityKind.Special
                    ? model.Resolve("/") : target, completeModel, nodes)];
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            int expressions = 0;
            foreach (XRegistryParameter filter in filters)
            {
                string text = filter.Value ?? string.Empty;
                if (text.Length is 0 or > 16384)
                {
                    throw new XRegistryRejectionException("bad_filter", "A bounded nonempty filter is required.");
                }
                HashSet<string>? conjunction = null;
                foreach (string expression in XRegistryQueryPath.Split(text, ',', "bad_filter"))
                {
                    if (++expressions > 128)
                    {
                        throw new XRegistryRejectionException(
                            "bad_filter", "The filter expression limit was exceeded.");
                    }
                    QueryExpression parsed = ParseExpression(expression);
                    var matches = new HashSet<string>(StringComparer.Ordinal);
                    foreach (QueryNode root in roots)
                    {
                        FindQueryMatches(root, parsed, 0, matches);
                    }
                    var expanded = new HashSet<string>(StringComparer.Ordinal);
                    foreach (QueryNode node in nodes)
                    {
                        if (matches.Any(match => node.Path == match ||
                            node.Path.StartsWith(match.TrimEnd('/') + "/", StringComparison.Ordinal)))
                        {
                            expanded.Add(node.Path);
                        }
                    }
                    if (conjunction is null)
                    {
                        conjunction = expanded;
                    }
                    else
                    {
                        conjunction.IntersectWith(expanded);
                    }
                }
                allowed.UnionWith(conjunction ?? []);
            }
            foreach (string path in allowed.ToArray())
            {
                string parent = path;
                while (parent != "/")
                {
                    parent = Parent(parent);
                    allowed.Add(parent);
                }
            }
            transaction.QueryPaths = allowed;
        }

        private QueryNode BuildQueryNode(
            Transaction transaction, XRegistryModelRules model, XRegistryTarget target,
            JsonObject completeModel, List<QueryNode> all)
        {
            var node = new QueryNode(target.Path, Represent(transaction, model, target, false),
                target.Kind == XRegistryEntityKind.Meta ? Parent(target.Path) : target.Path,
                QueryAttributes(completeModel, target));
            all.Add(node);
            if (target.Kind is XRegistryEntityKind.Registry or XRegistryEntityKind.Group)
            {
                string kind = target.Kind == XRegistryEntityKind.Registry ? "groups" : "resources";
                foreach ((string name, _) in XRegistryModelRules.Object(target.Definition[kind]))
                {
                    node.Children[name] = Children(transaction, Child(target.Path, name))
                        .ConvertAll(
                            path => BuildQueryNode(transaction, model, model.Resolve(path), completeModel, all));
                }
            }
            else if (target.Kind == XRegistryEntityKind.Resource)
            {
                node.Children["meta"] =
                    [BuildQueryNode(transaction, model, model.Resolve(target.Path + "/meta"), completeModel, all)];
                node.Children["versions"] = Children(transaction, target.Path + "/versions")
                    .ConvertAll(path => BuildQueryNode(transaction, model, model.Resolve(path), completeModel, all));
            }
            return node;
        }

        private static void FindQueryMatches(
            QueryNode node, QueryExpression expression, int index, HashSet<string> matches)
        {
            XRegistryQueryStep step = expression.Path[index];
            if (!step.Array && !step.Wildcard && node.Children.TryGetValue(step.Name, out List<QueryNode>? children))
            {
                if (index == expression.Path.Count - 1)
                {
                    bool present = children.Count != 0;
                    if ((expression.Operator.Length == 0 && present) ||
                        (expression.Operator == "=" && expression.Value == "null" && !present) ||
                        (expression.Operator is "!=" or "<>" && expression.Value == "null" && present))
                    {
                        matches.Add(node.Scope);
                    }
                    return;
                }
                foreach (QueryNode child in children)
                {
                    FindQueryMatches(child, expression, index + 1, matches);
                }
                return;
            }
            List<JsonNode> values = XRegistryQueryPath.Values(node.Metadata, expression.Path, index).ToList();
            string? type = QueryType(node.Attributes, node.Metadata, expression.Path, index);
            bool equal = values.Any(value => QueryMatchesValue(value, expression, type));
            bool match = expression.Operator switch
            {
                "" => values.Count != 0,
                "=" when expression.Value == "null" => values.Count == 0,
                "!=" or "<>" when expression.Value == "null" => values.Count != 0,
                "!=" or "<>" => !equal,
                _ => equal
            };
            if (match)
            {
                matches.Add(node.Scope);
            }
        }

        private static bool QueryMatchesValue(JsonNode actual, QueryExpression expression, string? type)
        {
            if (expression.Value == "null")
            {
                return false;
            }
            string value = expression.Value;
            JsonNode? expected;
            JsonValueKind kind = actual.GetValueKind();
            if (kind == JsonValueKind.String)
            {
                string text = actual.GetValue<string>();
                if (type == "timestamp" &&
                    XRegistryModelRules.TryTimestamp(text, out DateTimeOffset timestamp) &&
                    XRegistryModelRules.TryTimestamp(value, out DateTimeOffset expectedTime))
                {
                    int compared = timestamp.CompareTo(expectedTime);
                    return CompareQuery(compared, expression.Operator);
                }
                if (expression.Operator is "=" or "!=" or "<>")
                {
                    return XRegistryQueryPath.WildcardMatch(text, value);
                }
                if (value.Contains('*', StringComparison.Ordinal))
                {
                    throw new XRegistryRejectionException("bad_filter", "Wildcards require an equality operator.");
                }
                expected = JsonValue.Create(value);
            }
            else if (kind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                if (value == "*")
                {
                    return expression.Operator is "=" or "!=" or "<>";
                }
                try
                {
                    expected = JsonNode.Parse(value);
                }
                catch (JsonException)
                {
                    return false;
                }
                if (expected is null ||
                    (kind == JsonValueKind.Number && expected.GetValueKind() != JsonValueKind.Number) ||
                    (kind is JsonValueKind.True or JsonValueKind.False &&
                        expected.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)))
                {
                    return false;
                }
            }
            else
            {
                if (expression.Operator.Length != 0)
                {
                    throw new XRegistryRejectionException(
                        "bad_filter", "A non-null comparison requires a scalar value.");
                }
                return true;
            }
            int comparison = XRegistryQueryPath.Compare(actual, expected);
            return CompareQuery(comparison, expression.Operator);
        }

        private static bool CompareQuery(int comparison, string operation)
        {
            return operation switch
            {
                "=" or "!=" or "<>" => comparison == 0,
                ">" => comparison > 0,
                ">=" => comparison >= 0,
                "<" => comparison < 0,
                "<=" => comparison <= 0,
                _ => true
            };
        }

        private static QueryExpression ParseExpression(string expression)
        {
            int depth = 0;
            char quote = '\0';
            int index = 0;
            for (; index < expression.Length; index++)
            {
                char character = expression[index];
                if (character == '\\')
                {
                    index++;
                    continue;
                }
                if (quote != '\0')
                {
                    if (character == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                if (character is '\'' or '"' && depth != 0)
                {
                    quote = character;
                }
                else if (character == '[')
                {
                    depth++;
                }
                else if (character == ']')
                {
                    depth--;
                }
                else if (depth == 0 && character is '=' or '!' or '<' or '>')
                {
                    break;
                }
            }
            string name = expression[..Math.Min(index, expression.Length)];
            string operation = string.Empty;
            string value = string.Empty;
            if (index < expression.Length)
            {
                int length = index + 1 < expression.Length &&
                    (expression[index + 1] == '=' || (expression[index] == '<' && expression[index + 1] == '>'))
                        ? 2 : 1;
                operation = expression.Substring(index, length);
                value = expression[(index + length)..];
                if (operation == "!" || (value == "null" && operation is not ("=" or "!=" or "<>")))
                {
                    throw new XRegistryRejectionException(
                        "bad_filter", "The filter operator or null comparison is invalid.");
                }
            }
            return new QueryExpression(XRegistryQueryPath.Parse(name), operation, value);
        }

        private static void ValidateInlinePaths(
            XRegistryRequest request, XRegistryModelRules model, XRegistryTarget target)
        {
            foreach (XRegistryParameter flag in request.Parameters)
            {
                if (flag.Name != "inline")
                {
                    continue;
                }
                foreach (string text in XRegistryQueryPath.Split(flag.Value is null or "" ? "*" : flag.Value,
                    ',', "bad_inline"))
                {
                    List<XRegistryQueryStep> path = XRegistryQueryPath.Parse(text, "bad_inline");
                    XRegistryTarget current = target;
                    if (current.Kind == XRegistryEntityKind.Special && current.Singular == "export")
                    {
                        current = model.Resolve("/");
                    }
                    for (int index = 0; index < path.Count; index++)
                    {
                        XRegistryQueryStep step = path[index];
                        if (step.Array || (step.Wildcard && index != path.Count - 1))
                        {
                            throw new XRegistryRejectionException(
                                "bad_inline", "Only a final whole-segment wildcard is valid.");
                        }
                        if (step.Wildcard)
                        {
                            break;
                        }
                        JsonObject? children = current.Kind is XRegistryEntityKind.Registry
                            ? current.Definition["groups"] as JsonObject
                            : current.Kind is XRegistryEntityKind.Group or XRegistryEntityKind.Groups
                                ? current.Definition["resources"] as JsonObject : null;
                        if (children?[step.Name] is JsonObject child)
                        {
                            current = new XRegistryTarget(string.Empty,
                                current.Kind == XRegistryEntityKind.Registry
                                    ? XRegistryEntityKind.Group : XRegistryEntityKind.Resource,
                                child, XRegistryModelRules.Text(child["singular"]));
                        }
                        else if (current.Kind is XRegistryEntityKind.Resource or XRegistryEntityKind.Resources &&
                            step.Name == "versions")
                        {
                            current = current with { Kind = XRegistryEntityKind.Version };
                        }
                        else if (index == path.Count - 1 &&
                            ((current.Kind == XRegistryEntityKind.Registry &&
                                step.Name is "model" or "modelsource" or "capabilities") ||
                                (current.Kind is XRegistryEntityKind.Resource or XRegistryEntityKind.Resources &&
                                    step.Name == "meta") ||
                                (current.Kind is XRegistryEntityKind.Resource or XRegistryEntityKind.Resources
                                    or XRegistryEntityKind.Version or XRegistryEntityKind.Versions &&
                                    step.Name == current.Singular)))
                        {
                            break;
                        }
                        else
                        {
                            throw new XRegistryRejectionException(
                                "bad_inline", "The path does not name an inlineable attribute.");
                        }
                    }
                }
            }
        }

        private static List<string> RelativeModelPath(string requestPath, string targetPath)
        {
            ArrayOf<string> root = XRegistryPath.GetSegments(requestPath == "/export" ? "/" : requestPath);
            ArrayOf<string> target = XRegistryPath.GetSegments(targetPath);
            var result = new List<string>();
            for (int index = root.Count; index < target.Count; index++)
            {
                if (index is 0 or 2 or 4)
                {
                    result.Add(target[index]);
                }
            }
            return result;
        }

        private static bool Inline(Transaction transaction, XRegistryTarget target, string attribute)
        {
            if (HasFlag(transaction.Request, "collections"))
            {
                return target.Kind != XRegistryEntityKind.Registry ||
                    attribute is not ("model" or "modelsource" or "capabilities");
            }
            List<string> current = RelativeModelPath(transaction.Request.Path, target.Path);
            current.Add(attribute);
            foreach (XRegistryParameter parameter in transaction.Request.Parameters)
            {
                if (parameter.Name != "inline")
                {
                    continue;
                }
                foreach (string selection in XRegistryQueryPath.Split(
                    parameter.Value is null or "" ? "*" : parameter.Value, ',', "bad_inline"))
                {
                    List<XRegistryQueryStep> parts = XRegistryQueryPath.Parse(selection, "bad_inline");
                    int count = Math.Min(parts.Count, current.Count);
                    bool match = true;
                    for (int index = 0; index < count; index++)
                    {
                        if (parts[index].Wildcard)
                        {
                            return !(target.Kind == XRegistryEntityKind.Registry &&
                                attribute is "model" or "modelsource" or "capabilities");
                        }
                        if (parts[index].Name != current[index])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match && current.Count <= parts.Count)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private JsonObject SortCollection(
            JsonObject collection, Transaction transaction, XRegistryModelRules model, XRegistryTarget target)
        {
            XRegistryRequest request = transaction.Request;
            QuerySort? sort = ParseSort(request);
            if (sort is null)
            {
                return new JsonObject(collection.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
            }
            List<XRegistryQueryStep> key = sort.Path;
            var values = collection.ToList();
            var keys = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach ((string id, JsonNode? entity) in values)
            {
                JsonNode? valueNode;
                if (target.Kind == XRegistryEntityKind.Resources && key[0].Name == "meta" && key.Count > 1)
                {
                    JsonObject meta =
                        Represent(transaction, model, model.Resolve(Child(target.Path, id) + "/meta"), false);
                    valueNode = XRegistryQueryPath.Values(meta, key, 1).FirstOrDefault();
                }
                else
                {
                    valueNode = XRegistryQueryPath.Values(entity, key).FirstOrDefault();
                }
                if (valueNode is JsonObject or JsonArray)
                {
                    throw new XRegistryRejectionException("bad_sort", "A sort key must be scalar.");
                }
                keys[id] = valueNode;
            }
            values.Sort((left, right) =>
            {
                JsonNode? first = keys[left.Key];
                JsonNode? second = keys[right.Key];
                int comparison = XRegistryQueryPath.Compare(first, second);
                if (comparison == 0)
                {
                    comparison = StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key);
                    if (comparison == 0)
                    {
                        comparison = StringComparer.Ordinal.Compare(left.Key, right.Key);
                    }
                }
                return sort.Descending ? -comparison : comparison;
            });
            return new JsonObject(
                values.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
        }

        private static void ValidateSortTarget(
            XRegistryModelRules model, XRegistryTarget target, XRegistryRequest request)
        {
            if (!HasFlag(request, "sort"))
            {
                return;
            }
            if (!target.IsCollection)
            {
                throw new XRegistryRejectionException("sort_noncollection", "Sorting requires a collection.");
            }
            QuerySort sort = ParseSort(request)!;
            string? type = QueryType(QueryAttributes(model.CompleteModel(), target), null, sort.Path, 0);
            if (type is "object" or "array" or "map" || type is null)
            {
                throw new XRegistryRejectionException(
                    "bad_sort", "The sort path must identify a model-defined scalar.");
            }
            if ((target.Kind == XRegistryEntityKind.Groups &&
                target.Definition["resources"] is JsonObject resources &&
                resources.ContainsKey(sort.Path[0].Name)) ||
                (target.Kind == XRegistryEntityKind.Resources && sort.Path[0].Name == "versions"))
            {
                throw new XRegistryRejectionException("bad_sort", "Sort cannot traverse a nested collection.");
            }
        }

        private static QuerySort? ParseSort(XRegistryRequest request)
        {
            XRegistryParameter[] flags = [.. request.Parameters.ToList().Where(item => item.Name == "sort")];
            if (flags.Length == 0)
            {
                return null;
            }
            if (flags.Length != 1 || string.IsNullOrEmpty(flags[0].Value))
            {
                throw new XRegistryRejectionException("bad_sort", "One nonempty sort key is required.");
            }
            string value = flags[0].Value!;
            List<string> parts = XRegistryQueryPath.Split(value, '=', "bad_sort");
            bool descending = parts.Count == 2 && parts[1] == "desc";
            if (parts.Count > 2 || (parts.Count == 2 && parts[1] is not ("asc" or "desc")))
            {
                throw new XRegistryRejectionException("bad_sort", "The sort direction must be asc or desc.");
            }
            List<XRegistryQueryStep> key = XRegistryQueryPath.Parse(parts[0], "bad_sort");
            if (key.Any(step => step.Wildcard) || key[0].Name == "versions")
            {
                throw new XRegistryRejectionException("bad_sort", "A sort key cannot cross an xRegistry collection.");
            }
            return new QuerySort(key, descending);
        }

        private static JsonObject QueryAttributes(JsonObject model, XRegistryTarget target)
        {
            ArrayOf<string> path = XRegistryPath.GetSegments(target.Path);
            JsonObject definition = path.Count == 0 ? model :
                path.Count <= 2 ? XRegistryModelRules.Object(model["groups"]?[path[0]]) :
                XRegistryModelRules.Object(model["groups"]?[path[0]]?["resources"]?[path[2]]);
            JsonObject attributes = XRegistryModelRules.Object(
                definition[target.Kind == XRegistryEntityKind.Meta ? "metaattributes" : "attributes"]);
            if (target.Kind is XRegistryEntityKind.Resource or XRegistryEntityKind.Resources)
            {
                attributes = (JsonObject)attributes.DeepClone();
                foreach ((string name, JsonNode? value) in XRegistryModelRules.Object(definition["resourceattributes"]))
                {
                    attributes[name] = value?.DeepClone();
                }
            }
            return attributes;
        }

        private static string? QueryType(
            JsonObject attributes, JsonObject? metadata, List<XRegistryQueryStep> path, int start)
        {
            JsonObject? rules = attributes;
            JsonObject? rule = null;
            JsonNode? actual = metadata;
            for (int index = start; index < path.Count; index++)
            {
                XRegistryQueryStep step = path[index];
                if (rules is not null)
                {
                    if (step.Array)
                    {
                        return null;
                    }
                    rules = actual is JsonObject data ? XRegistryModelRules.EffectiveAttributes(rules, data) : rules;
                    rule = rules?[step.Name] as JsonObject ?? rules?["*"] as JsonObject;
                }
                else if (rule?["type"] is JsonNode collection &&
                    XRegistryModelRules.Text(collection) is "map" or "array")
                {
                    if ((XRegistryModelRules.Text(collection) == "array") != step.Array)
                    {
                        return null;
                    }
                    rule = rule["item"] as JsonObject;
                }
                if (rule is null)
                {
                    return null;
                }
                string type = XRegistryModelRules.Text(rule["type"]);
                if (type == "any" || index == path.Count - 1)
                {
                    return type;
                }
                actual = step.Array && actual is JsonArray array && step.Index >= 0 && step.Index < array.Count
                    ? array[step.Index] : !step.Array && actual is JsonObject obj ? obj[step.Name] : null;
                rules = type == "object" ? rule["attributes"] as JsonObject : null;
                if (type is not ("object" or "map" or "array"))
                {
                    return null;
                }
            }
            return null;
        }

        private sealed record QuerySort(List<XRegistryQueryStep> Path, bool Descending);

        private sealed record QueryExpression(List<XRegistryQueryStep> Path, string Operator, string Value);

        private sealed record QueryNode(string Path, JsonObject Metadata, string Scope, JsonObject Attributes)
        {
            public Dictionary<string, List<QueryNode>> Children { get; } = new(StringComparer.Ordinal);
        }
    }
}
