// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The relationship between an action DataSchema and native argument positions.
    /// </summary>
    public enum WotMethodArgumentLayoutKind
    {
        /// <summary>
        /// No native arguments are declared.
        /// </summary>
        None,

        /// <summary>
        /// The complete DataSchema describes one native argument.
        /// </summary>
        Single,

        /// <summary>
        /// The schema's named properties describe ordered native arguments.
        /// </summary>
        Named,

        /// <summary>
        /// The schema names arguments, but the native Method order must be resolved from the endpoint.
        /// </summary>
        NamedUnresolved
    }

    /// <summary>
    /// An immutable action input or output layout resolved by the converter's
    /// argument mapping, shared by transport planners and payload adapters.
    /// </summary>
    public sealed class WotMethodArgumentLayout
    {
        /// <summary>
        /// Initializes a new <c>WotMethodArgumentLayout</c> instance.
        /// </summary>
        /// <param name="kind">The kind used to initialize the instance.</param>
        /// <param name="schema">The JSON element used to initialize the instance.</param>
        /// <param name="fieldOrder">The field order used to initialize the instance.</param>
        /// <param name="defaultName">The default name used to initialize the instance.</param>
        /// <param name="declaredNames">The schema-declared argument names.</param>
        internal WotMethodArgumentLayout(
            WotMethodArgumentLayoutKind kind,
            JsonElement schema,
            ArrayOf<string> fieldOrder,
            string defaultName,
            ArrayOf<string> declaredNames = default)
        {
            Kind = kind;
            Schema = schema.ValueKind == JsonValueKind.Undefined ? default : schema.Clone();
            FieldOrder = fieldOrder;
            m_defaultName = defaultName;
            DeclaredNames = declaredNames.IsNull
                ? fieldOrder
                : declaredNames;
        }

        /// <summary>
        /// Gets whether the schema declares zero, one whole value, or named arguments.
        /// </summary>
        public WotMethodArgumentLayoutKind Kind { get; }

        /// <summary>
        /// Gets the complete DataSchema, or an undefined element when it is absent.
        /// </summary>
        public JsonElement Schema { get; }

        /// <summary>
        /// Gets the positional property names for a named argument layout.
        /// A single value's own Structure fields are not argument positions.
        /// </summary>
        public ArrayOf<string> FieldOrder { get; }

        /// <summary>
        /// Gets all schema-declared argument names, including when native order is unresolved.
        /// </summary>
        public ArrayOf<string> DeclaredNames { get; }

        /// <summary>
        /// Gets the number of declared native argument positions.
        /// </summary>
        public int ArgumentCount => Kind switch
        {
            WotMethodArgumentLayoutKind.Single => 1,
            WotMethodArgumentLayoutKind.Named => FieldOrder.Count,
            WotMethodArgumentLayoutKind.NamedUnresolved => DeclaredNames.Count,
            _ => 0
        };

        /// <summary>
        /// Gets the DataSchema for one native argument position.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public JsonElement GetArgumentSchema(int index)
        {
            if (Kind == WotMethodArgumentLayoutKind.NamedUnresolved)
            {
                throw new InvalidOperationException(
                    "The native Method order must be resolved before reading argument schemas by position.");
            }
            return GetArgumentSchema(Schema, index);
        }

        /// <summary>
        /// Gets the native argument name, including the converter's default for a single value.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public string GetArgumentName(int index)
        {
            if (Kind == WotMethodArgumentLayoutKind.NamedUnresolved)
            {
                throw new InvalidOperationException(
                    "The native Method order must be resolved before reading argument names by position.");
            }
            JsonElement schema = GetArgumentSchema(index);
            return Kind == WotMethodArgumentLayoutKind.Single
                ? WotNodeSetConverter.ReadArgumentName(schema, m_defaultName)
                : FieldOrder[index];
        }

        /// <summary>
        /// Gets the schema of one method argument.
        /// </summary>
        /// <param name="sourceSchema">The source schema.</param>
        /// <param name="index">The zero-based index of the method argument.</param>
        internal JsonElement GetArgumentSchema(JsonElement sourceSchema, int index)
        {
            if (Kind == WotMethodArgumentLayoutKind.NamedUnresolved)
            {
                throw new InvalidOperationException(
                    "The native Method order must be resolved before reading argument schemas by position.");
            }
            if (index < 0 || index >= ArgumentCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return Kind == WotMethodArgumentLayoutKind.Single
                ? sourceSchema
                : sourceSchema.GetProperty("properties").GetProperty(FieldOrder[index]);
        }

        /// <summary>
        /// Resolves an unordered named schema against the native Method argument order.
        /// </summary>
        /// <param name="nativeNames">The ordered names read from the Method.</param>
        /// <returns>The resolved positional layout.</returns>
        /// <exception cref="ArgumentException">The native names do not exactly match the schema names.</exception>
        public WotMethodArgumentLayout ResolveNativeOrder(ArrayOf<string> nativeNames)
        {
            if (Kind != WotMethodArgumentLayoutKind.NamedUnresolved)
            {
                return this;
            }
            if (nativeNames.Count != DeclaredNames.Count)
            {
                throw new ArgumentException(
                    "The native Method argument names do not match the declared schema.", nameof(nativeNames));
            }
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in DeclaredNames)
            {
                declared.Add(name);
            }
            var ordered = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in nativeNames)
            {
                if (!declared.Contains(name) || !ordered.Add(name))
                {
                    throw new ArgumentException(
                        "The native Method argument names do not exactly match the declared schema.",
                        nameof(nativeNames));
                }
            }
            return new WotMethodArgumentLayout(
                WotMethodArgumentLayoutKind.Named, Schema, nativeNames, m_defaultName);
        }

        private readonly string m_defaultName;
    }
}
