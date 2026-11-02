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

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// The arguments of the LADS <c>StartProgram</c> method, built fluently.
    /// </summary>
    /// <example>
    /// <code>
    /// LadsProgramRequest request = LadsProgramRequest.For("Measure")
    ///     .WithProperty("Temperature", "37")
    ///     .WithSample("Plate-1", "S-0001", position: "A1")
    ///     .WithJob("Job-7");
    /// </code>
    /// </example>
    /// <param name="TemplateId">The id of the program template.</param>
    public sealed record LadsProgramRequest(string TemplateId)
    {
        /// <summary>
        /// Gets the key value properties.
        /// </summary>
        public ArrayOf<KeyValueType> Properties { get; init; } = [];

        /// <summary>
        /// Gets the supervisory job id.
        /// </summary>
        public string? SupervisoryJobId { get; init; }

        /// <summary>
        /// Gets the supervisory task id.
        /// </summary>
        public string? SupervisoryTaskId { get; init; }

        /// <summary>
        /// Gets the samples.
        /// </summary>
        public ArrayOf<SampleInfoType> Samples { get; init; } = [];

        /// <summary>
        /// Creates a request for a program template.
        /// </summary>
        /// <param name="templateId">The id of the program template.</param>
        /// <returns>The request.</returns>
        /// <exception cref="ArgumentException">The template id is empty.</exception>
        public static LadsProgramRequest For(string templateId)
        {
            if (string.IsNullOrEmpty(templateId))
            {
                throw new ArgumentException("A template id is required.", nameof(templateId));
            }
            return new LadsProgramRequest(templateId);
        }

        /// <summary>
        /// Returns a copy of the request with an additional property.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>The new request.</returns>
        /// <exception cref="ArgumentException">The key is empty.</exception>
        public LadsProgramRequest WithProperty(string key, string? value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A property key is required.", nameof(key));
            }
            var properties = new List<KeyValueType>(Properties.Count + 1);
            properties.AddRange(Properties);
            properties.Add(new KeyValueType { Key = key, Value = value });
            return this with { Properties = properties.ToArrayOf() };
        }

        /// <summary>
        /// Returns a copy of the request with an additional sample.
        /// </summary>
        /// <param name="containerId">The id of the sample container.</param>
        /// <param name="sampleId">The id of the sample.</param>
        /// <param name="position">The position of the sample in the container.</param>
        /// <param name="customData">Custom data.</param>
        /// <returns>The new request.</returns>
        public LadsProgramRequest WithSample(
            string? containerId,
            string? sampleId,
            string? position = null,
            string? customData = null)
        {
            var samples = new List<SampleInfoType>(Samples.Count + 1);
            samples.AddRange(Samples);
            samples.Add(new SampleInfoType
            {
                ContainerId = containerId,
                SampleId = sampleId,
                Position = position,
                CustomData = customData
            });
            return this with { Samples = samples.ToArrayOf() };
        }

        /// <summary>
        /// Returns a copy of the request with a supervisory job id.
        /// </summary>
        /// <param name="jobId">The job id.</param>
        /// <returns>The new request.</returns>
        public LadsProgramRequest WithJob(string? jobId)
        {
            return this with { SupervisoryJobId = jobId };
        }

        /// <summary>
        /// Returns a copy of the request with a supervisory task id.
        /// </summary>
        /// <param name="taskId">The task id.</param>
        /// <returns>The new request.</returns>
        public LadsProgramRequest WithTask(string? taskId)
        {
            return this with { SupervisoryTaskId = taskId };
        }
    }
}
