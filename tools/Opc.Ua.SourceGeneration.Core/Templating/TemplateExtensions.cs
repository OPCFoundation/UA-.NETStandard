/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.SourceGeneration;

namespace Opc.Ua
{
    internal static class TemplateExtensions
    {
        /// <summary>
        /// Add a template for substitution.
        /// </summary>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="ArgumentException"></exception>
        public static void AddReplacement(
            this Template template,
            string replacement,
            TemplateString templateString,
            IEnumerable targets,
            WriteTemplateEventHandler onWrite)
        {
            template.AddReplacement(
                replacement,
                templateString,
                targets,
                null,
                onWrite);
        }

        /// <summary>
        /// Add a template for substitution.
        /// </summary>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="ArgumentException"></exception>
        public static void AddReplacement(
            this Template template,
            string replacement,
            IEnumerable targets,
            LoadTemplateEventHandler onLoad,
            WriteTemplateEventHandler? onWrite = null)
        {
            template.AddReplacement(
                replacement,
                TemplateString.Empty,
                targets,
                onLoad,
                onWrite);
        }

        /// <summary>
        /// Initializes a template to use for substitution.
        /// </summary>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="ArgumentException"></exception>
        public static void AddReplacement(
            this Template template,
            string replacement,
            TemplateString templateString,
            IEnumerable targets,
            LoadTemplateEventHandler? onLoad = null,
            WriteTemplateEventHandler? onWrite = null)
        {
            object[] targetArray;
            if (targets is string str)
            {
                targetArray = [str];
            }
            else if (targets is null)
            {
                targetArray = [];
            }
            else
            {
                targetArray = [.. targets.Cast<object>()];
            }
            template.AddReplacement(
                replacement,
                templateString,
                targetArray,
                onLoad,
                onWrite);
        }

        /// <summary>
        /// Add a template for substitution.
        /// </summary>
        /// <exception cref="ArgumentNullException" />
        public static void AddReplacement(
            this Template template,
            string replacement,
            TemplateString templateString,
            IReadOnlyList<object> targets,
            WriteTemplateEventHandler onWrite)
        {
            template.AddReplacement(
                replacement,
                templateString,
                targets,
                null,
                onWrite);
        }

        /// <summary>
        /// Add a template for substitution.
        /// </summary>
        /// <exception cref="ArgumentNullException" />
        public static void AddReplacement(
            this Template template,
            string replacement,
            IReadOnlyList<object> targets,
            LoadTemplateEventHandler onLoad,
            WriteTemplateEventHandler? onWrite = null)
        {
            template.AddReplacement(
                replacement,
                TemplateString.Empty,
                targets,
                onLoad,
                onWrite);
        }

        /// <summary>
        /// Initializes a template to use for substitution.
        /// </summary>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="ArgumentException"></exception>
        public static void AddReplacement(
            this Template template,
            string replacement,
            TemplateString templateString,
            IReadOnlyList<object> targets,
            LoadTemplateEventHandler? onLoad = null,
            WriteTemplateEventHandler? onWrite = null)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }
            if (string.IsNullOrEmpty(replacement))
            {
                throw new ArgumentNullException(nameof(replacement));
            }
            if (templateString is null && onLoad is null)
            {
                throw new ArgumentException(
                    "A template loader must be passed if template string is null",
                    nameof(onLoad));
            }
            var definition = new TemplateDefinition
            {
                TemplateString = templateString,
                Targets = targets ?? [],
                OnTemplateLoad = onLoad,
                OnTemplateWrite = onWrite
            };
            template.AddReplacement(replacement, definition);
        }
    }
}
