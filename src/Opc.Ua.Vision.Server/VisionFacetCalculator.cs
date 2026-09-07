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

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Computes the §11 facet URIs a Vision server can claim by inspecting
    /// the state recorded in the <see cref="VisionRegistry"/>.
    /// </summary>
    /// <remarks>
    /// A facet is added to the result only when every one of its
    /// requirements is present in the address space or in a bound
    /// provider — hosts that need to publish a facet the calculator
    /// cannot verify structurally (for example, a behavioural interop
    /// facet) can add it through
    /// <see cref="VisionServerOptions.AdditionalFacets"/>.
    /// </remarks>
    internal static class VisionFacetCalculator
    {
        public static ArrayOf<string> Compute(VisionRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            var facets = new HashSet<string>(StringComparer.Ordinal);
            foreach (SensorRegistration sensor in registry.SensorsByNodeId.Values)
            {
                foreach (string facet in sensor.Facets)
                {
                    facets.Add(facet);
                }
            }
            foreach (PipelineRegistration pipeline in registry.PipelinesByNodeId.Values)
            {
                foreach (string facet in pipeline.Facets)
                {
                    facets.Add(facet);
                }
            }
            var result = new List<string>(facets);
            result.Sort(StringComparer.Ordinal);
            return result.ToArrayOf();
        }

        public static ArrayOf<string> ComputeProfiles(ArrayOf<string> facets)
        {
            var lookup = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < facets.Count; ii++)
            {
                if (!string.IsNullOrEmpty(facets[ii]))
                {
                    lookup.Add(facets[ii]);
                }
            }
            var profiles = new List<string>();
            if (lookup.Contains(VisionConformanceUris.FacetNames.Base) &&
                lookup.Contains(VisionConformanceUris.FacetNames.MediaJpeg) &&
                lookup.Contains(VisionConformanceUris.FacetNames.MediaRtsp))
            {
                profiles.Add(VisionConformanceUris.Profiles.Basic);
            }
            if (lookup.Contains(VisionConformanceUris.FacetNames.ResultInspection) &&
                lookup.Contains(VisionConformanceUris.FacetNames.Feedback))
            {
                profiles.Add(VisionConformanceUris.Profiles.Inspection);
            }
            if (lookup.Contains(VisionConformanceUris.FacetNames.ResultDetection) &&
                lookup.Contains(VisionConformanceUris.FacetNames.Feedback))
            {
                profiles.Add(VisionConformanceUris.Profiles.Detection);
            }
            if (lookup.Contains(VisionConformanceUris.FacetNames.InferenceOnServer))
            {
                profiles.Add(VisionConformanceUris.Profiles.Inference);
            }
            return profiles.ToArrayOf();
        }
    }
}
