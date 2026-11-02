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
using System.Globalization;
using System.Text;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The outcome of a program run.
    /// </summary>
    public enum LadsProgramOutcome
    {
        /// <summary>
        /// The run executed all of its steps.
        /// </summary>
        Completed,

        /// <summary>
        /// The run was stopped before it completed.
        /// </summary>
        Stopped,

        /// <summary>
        /// The run was aborted or failed.
        /// </summary>
        Aborted
    }

    /// <summary>
    /// A setpoint a program step applies to a function of its functional unit.
    /// </summary>
    public sealed class LadsSetpoint
    {
        /// <summary>
        /// Creates a setpoint.
        /// </summary>
        /// <param name="functionName">The browse name of the function within the functional unit.</param>
        /// <param name="value">The target value applied to the function.</param>
        /// <exception cref="ArgumentException"><paramref name="functionName"/> is empty.</exception>
        public LadsSetpoint(string functionName, Variant value)
        {
            if (string.IsNullOrWhiteSpace(functionName))
            {
                throw new ArgumentException("A setpoint requires a function name.", nameof(functionName));
            }
            FunctionName = functionName;
            Value = value;
        }

        /// <summary>
        /// The browse name of the function within the functional unit.
        /// </summary>
        public string FunctionName { get; }

        /// <summary>
        /// The target value applied to the function.
        /// </summary>
        public Variant Value { get; }
    }

    /// <summary>
    /// A step of a program template.
    /// </summary>
    public sealed class LadsProgramStep
    {
        /// <summary>
        /// Creates a step.
        /// </summary>
        /// <param name="name">The name of the step.</param>
        /// <param name="duration">How long the step runs after its setpoints were applied.</param>
        /// <param name="setpoints">The setpoints the step applies when it starts.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is empty or the duration is negative.
        /// </exception>
        public LadsProgramStep(string name, TimeSpan duration, ArrayOf<LadsSetpoint> setpoints = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A program step requires a name.", nameof(name));
            }
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentException("A program step cannot have a negative duration.", nameof(duration));
            }
            Name = name;
            Duration = duration;
            Setpoints = setpoints;
        }

        /// <summary>
        /// The name of the step.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// How long the step runs after its setpoints were applied.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// The setpoints the step applies when it starts.
        /// </summary>
        public ArrayOf<LadsSetpoint> Setpoints { get; }
    }

    /// <summary>
    /// A program template of a functional unit: the recipe a program run executes.
    /// </summary>
    public sealed class LadsProgramTemplate
    {
        /// <summary>
        /// Creates a template.
        /// </summary>
        /// <param name="id">The device template identifier, also used as browse name.</param>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        public LadsProgramTemplate(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A program template requires an identifier.", nameof(id));
            }
            Id = id;
        }

        /// <summary>
        /// The device template identifier, also used as browse name of the template node.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The description of the template.
        /// </summary>
        public LocalizedText Description { get; set; }

        /// <summary>
        /// The author of the template.
        /// </summary>
        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// The version of the template.
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// The supervisory system's identifier of the template, if any.
        /// </summary>
        public string? SupervisoryTemplateId { get; set; }

        /// <summary>
        /// When the template was created. Defaults to the time it was added to the program manager.
        /// </summary>
        public DateTimeUtc Created { get; set; }

        /// <summary>
        /// When the template was last modified. Defaults to the time it was added to the program manager.
        /// </summary>
        public DateTimeUtc Modified { get; set; }

        /// <summary>
        /// The steps of the template.
        /// </summary>
        public ArrayOf<LadsProgramStep> Steps => m_steps.ToArray();

        /// <summary>
        /// The estimated runtime: the sum of the step durations.
        /// </summary>
        public TimeSpan EstimatedRuntime
        {
            get
            {
                TimeSpan total = TimeSpan.Zero;
                foreach (LadsProgramStep step in m_steps)
                {
                    total += step.Duration;
                }
                return total;
            }
        }

        /// <summary>
        /// Sets the description and returns the template.
        /// </summary>
        /// <param name="description">The description.</param>
        /// <returns>The template.</returns>
        public LadsProgramTemplate WithDescription(LocalizedText description)
        {
            Description = description;
            return this;
        }

        /// <summary>
        /// Sets the author and version and returns the template.
        /// </summary>
        /// <param name="author">The author.</param>
        /// <param name="version">The version.</param>
        /// <returns>The template.</returns>
        public LadsProgramTemplate WithAuthor(string author, string version = "1.0")
        {
            Author = author ?? string.Empty;
            Version = version ?? string.Empty;
            return this;
        }

        /// <summary>
        /// Appends a step and returns the template.
        /// </summary>
        /// <param name="name">The name of the step.</param>
        /// <param name="duration">How long the step runs.</param>
        /// <param name="setpoints">The setpoints the step applies when it starts.</param>
        /// <returns>The template.</returns>
        public LadsProgramTemplate Step(string name, TimeSpan duration, params LadsSetpoint[] setpoints)
        {
            m_steps.Add(new LadsProgramStep(name, duration, setpoints ?? []));
            return this;
        }

        /// <summary>
        /// Appends a step and returns the template.
        /// </summary>
        /// <param name="step">The step.</param>
        /// <returns>The template.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="step"/> is <c>null</c>.</exception>
        public LadsProgramTemplate Step(LadsProgramStep step)
        {
            m_steps.Add(step ?? throw new ArgumentNullException(nameof(step)));
            return this;
        }

        /// <summary>
        /// Renders the steps as the text content exchanged by the Download and Upload methods.
        /// </summary>
        /// <returns>The UTF-8 encoded content.</returns>
        public ByteString ToContent()
        {
            var text = new StringBuilder();
            foreach (LadsProgramStep step in m_steps)
            {
                text.Append("step;")
                    .Append(step.Name)
                    .Append(';')
                    .Append(((long)step.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
                foreach (LadsSetpoint setpoint in step.Setpoints)
                {
                    text.Append("set;")
                        .Append(setpoint.FunctionName)
                        .Append(';')
                        .Append(FormatValue(setpoint.Value))
                        .Append('\n');
                }
            }
            return ByteString.From(Encoding.UTF8.GetBytes(text.ToString()));
        }

        /// <summary>
        /// Parses the content written by <see cref="ToContent"/> into a template.
        /// </summary>
        /// <param name="id">The template identifier.</param>
        /// <param name="content">The UTF-8 encoded content.</param>
        /// <returns>The template.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidArgument"/> when the content is malformed.
        /// </exception>
        public static LadsProgramTemplate Parse(string id, ByteString content)
        {
            var template = new LadsProgramTemplate(id);
            string text = content.IsEmpty ? string.Empty : Encoding.UTF8.GetString(content.ToArray());
            string? stepName = null;
            TimeSpan duration = TimeSpan.Zero;
            var setpoints = new List<LadsSetpoint>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                string[] parts = line.Split(';');
                if (parts.Length != 3)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "The program content line '{0}' is malformed.",
                        line);
                }
                if (parts[0] == "step")
                {
                    if (stepName != null)
                    {
                        template.Step(new LadsProgramStep(stepName, duration, setpoints.ToArray()));
                    }
                    if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms) ||
                        ms < 0)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidArgument,
                            "The program step duration '{0}' is invalid.",
                            parts[2]);
                    }
                    stepName = parts[1];
                    duration = TimeSpan.FromMilliseconds(ms);
                    setpoints.Clear();
                }
                else if (parts[0] == "set" && stepName != null)
                {
                    setpoints.Add(new LadsSetpoint(parts[1], ParseValue(parts[2])));
                }
                else
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "The program content line '{0}' is malformed.",
                        line);
                }
            }
            if (stepName != null)
            {
                template.Step(new LadsProgramStep(stepName, duration, setpoints.ToArray()));
            }
            return template;
        }

        /// <summary>
        /// Formats a setpoint value for the program content.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The text.</returns>
        private static string FormatValue(Variant value)
        {
            if (value.TryGetValue(out bool flag))
            {
                return flag ? "true" : "false";
            }
            if (value.TryGetValue(out uint index))
            {
                return "u" + index.ToString(CultureInfo.InvariantCulture);
            }
            if (value.TryGetValue(out double number))
            {
                return number.ToString("R", CultureInfo.InvariantCulture);
            }
            return string.Empty;
        }

        /// <summary>
        /// Parses a setpoint value of the program content.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The value.</returns>
        private static Variant ParseValue(string text)
        {
            if (text == "true" || text == "false")
            {
                return Variant.From(text == "true");
            }
            if (text.StartsWith('u') &&
                uint.TryParse(text.Remove(0, 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint index))
            {
                return Variant.From(index);
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                return Variant.From(number);
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidArgument,
                "The setpoint value '{0}' is invalid.",
                text);
        }

        private readonly List<LadsProgramStep> m_steps = [];
    }

    /// <summary>
    /// A file of a program result.
    /// </summary>
    public sealed class LadsResultFile
    {
        /// <summary>
        /// Creates a result file.
        /// </summary>
        /// <param name="name">The file name, also used as browse name.</param>
        /// <param name="mimeType">The MIME type of the content.</param>
        /// <param name="content">The content.</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        public LadsResultFile(string name, string mimeType, ByteString content)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A result file requires a name.", nameof(name));
            }
            Name = name;
            MimeType = string.IsNullOrEmpty(mimeType) ? "application/octet-stream" : mimeType;
            Content = content;
        }

        /// <summary>
        /// The file name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The MIME type of the content.
        /// </summary>
        public string MimeType { get; }

        /// <summary>
        /// The content.
        /// </summary>
        public ByteString Content { get; }
    }
}
