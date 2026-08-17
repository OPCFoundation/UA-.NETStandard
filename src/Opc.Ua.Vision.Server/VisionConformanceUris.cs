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

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// URIs and names of the conformance facets defined by OPC UA — Vision
    /// clause 11.
    /// </summary>
    public static class VisionConformanceUris
    {
        /// <summary>
        /// Base URI for Vision Server profiles.
        /// </summary>
        public const string ProfileBase = "http://opcfoundation.org/UA-Profile/Vision/Server/";

        /// <summary>
        /// Base URI for Vision facets.
        /// </summary>
        public const string FacetBase = "http://opcfoundation.org/UA-Profile/Vision/Facet/";

        /// <summary>
        /// Vision facet names from clause 11.2.
        /// </summary>
        public static class FacetNames
        {
            /// <summary>
            /// Mandatory facet requiring the well-known Vision root, a
            /// sensor with core members, and both mandatory media facets.
            /// </summary>
            public const string Base = "VIS-Base";

            /// <summary>
            /// Sensor parameters facet (§5.5).
            /// </summary>
            public const string SensorParams = "VIS-Sensor-Params";

            /// <summary>
            /// Optics and illumination facet (§5.7).
            /// </summary>
            public const string Optics = "VIS-Optics";

            /// <summary>
            /// RTSP stream endpoint facet (§6.2).
            /// </summary>
            public const string MediaRtsp = "VIS-Media-Rtsp";

            /// <summary>
            /// JPEG clip endpoint facet (§6.2).
            /// </summary>
            public const string MediaJpeg = "VIS-Media-Jpeg";

            /// <summary>
            /// Inline clip delivery facet (§6.4).
            /// </summary>
            public const string MediaInline = "VIS-Media-Inline";

            /// <summary>
            /// Data-channel media facet (§6.7).
            /// </summary>
            public const string MediaDataChannel = "VIS-Media-DataChannel";

            /// <summary>
            /// Stream configuration and selection facet (§6.5).
            /// </summary>
            public const string EndpointConfig = "VIS-Endpoint-Config";

            /// <summary>
            /// Coordinate frames and calibration facet (§5.8).
            /// </summary>
            public const string Calibration = "VIS-Calibration";

            /// <summary>
            /// Inspection result facet (§7.2).
            /// </summary>
            public const string ResultInspection = "VIS-Result-Inspection";

            /// <summary>
            /// Detection result facet (§7.3).
            /// </summary>
            public const string ResultDetection = "VIS-Result-Detection";

            /// <summary>
            /// Feedback and return-path facet (§9).
            /// </summary>
            public const string Feedback = "VIS-Feedback";

            /// <summary>
            /// On-server inference facet (§8.2).
            /// </summary>
            public const string InferenceOnServer = "VIS-Inference-OnServer";

            /// <summary>
            /// Off-server inference facet (§8.2).
            /// </summary>
            public const string InferenceOffServer = "VIS-Inference-OffServer";

            /// <summary>
            /// Simulated-sensor facet (§4.3, §10).
            /// </summary>
            public const string Simulation = "VIS-Simulation";

            /// <summary>
            /// Feedback-driven learning facet (§9.5.1).
            /// </summary>
            public const string Learning = "VIS-Learning";

            /// <summary>
            /// OpenUSD scene interop facet (Annex C).
            /// </summary>
            public const string InteropScene = "VIS-Interop-Scene";

            /// <summary>
            /// OPC 40100 interop facet (Annex D).
            /// </summary>
            public const string Interop40100 = "VIS-Interop-40100";

            /// <summary>
            /// Robot Intent interop facet (Annex I).
            /// </summary>
            public const string InteropRobotIntent = "VIS-Interop-RobotIntent";
        }

        /// <summary>
        /// Vision Server profile URIs.
        /// </summary>
        public static class Profiles
        {
            /// <summary>
            /// Baseline Vision Server profile — includes the mandatory
            /// Base and both mandatory media facets.
            /// </summary>
            public const string Basic = ProfileBase + "Basic";

            /// <summary>
            /// Inspection profile — Basic plus inspection results and feedback.
            /// </summary>
            public const string Inspection = ProfileBase + "Inspection";

            /// <summary>
            /// Detection profile — Basic plus detection results and feedback.
            /// </summary>
            public const string Detection = ProfileBase + "Detection";

            /// <summary>
            /// Inference profile — Basic plus on-server inference and results.
            /// </summary>
            public const string Inference = ProfileBase + "Inference";
        }

        /// <summary>
        /// Vision facet URIs, one per name in <see cref="FacetNames"/>.
        /// </summary>
        public static class Facets
        {
            /// <summary>
            /// Base facet.
            /// </summary>
            public const string Base = FacetBase + "Base";

            /// <summary>
            /// Sensor parameters facet.
            /// </summary>
            public const string SensorParams = FacetBase + "Sensor-Params";

            /// <summary>
            /// Optics facet.
            /// </summary>
            public const string Optics = FacetBase + "Optics";

            /// <summary>
            /// RTSP stream facet.
            /// </summary>
            public const string MediaRtsp = FacetBase + "Media-Rtsp";

            /// <summary>
            /// JPEG clip facet.
            /// </summary>
            public const string MediaJpeg = FacetBase + "Media-Jpeg";

            /// <summary>
            /// Inline media facet.
            /// </summary>
            public const string MediaInline = FacetBase + "Media-Inline";

            /// <summary>
            /// Data-channel media facet.
            /// </summary>
            public const string MediaDataChannel = FacetBase + "Media-DataChannel";

            /// <summary>
            /// Endpoint configuration facet.
            /// </summary>
            public const string EndpointConfig = FacetBase + "Endpoint-Config";

            /// <summary>
            /// Calibration facet.
            /// </summary>
            public const string Calibration = FacetBase + "Calibration";

            /// <summary>
            /// Inspection result facet.
            /// </summary>
            public const string ResultInspection = FacetBase + "Result-Inspection";

            /// <summary>
            /// Detection result facet.
            /// </summary>
            public const string ResultDetection = FacetBase + "Result-Detection";

            /// <summary>
            /// Feedback facet.
            /// </summary>
            public const string Feedback = FacetBase + "Feedback";

            /// <summary>
            /// On-server inference facet.
            /// </summary>
            public const string InferenceOnServer = FacetBase + "Inference-OnServer";

            /// <summary>
            /// Off-server inference facet.
            /// </summary>
            public const string InferenceOffServer = FacetBase + "Inference-OffServer";

            /// <summary>
            /// Simulation facet.
            /// </summary>
            public const string Simulation = FacetBase + "Simulation";

            /// <summary>
            /// Learning facet.
            /// </summary>
            public const string Learning = FacetBase + "Learning";

            /// <summary>
            /// OpenUSD scene interop facet.
            /// </summary>
            public const string InteropScene = FacetBase + "Interop-Scene";

            /// <summary>
            /// OPC 40100 interop facet.
            /// </summary>
            public const string Interop40100 = FacetBase + "Interop-40100";

            /// <summary>
            /// Robot Intent interop facet.
            /// </summary>
            public const string InteropRobotIntent = FacetBase + "Interop-RobotIntent";
        }

        /// <summary>
        /// Returns the ordered list of every facet name defined by the
        /// specification.
        /// </summary>
        public static ArrayOf<string> AllFacets => s_allFacets;

        internal static bool TryGetFacetUri(string facetName, out string facetUri)
        {
            const string facetNamePrefix = "VIS-";
            if (!string.IsNullOrEmpty(facetName) &&
                facetName.StartsWith(facetNamePrefix, StringComparison.Ordinal))
            {
#if NETSTANDARD || NETFRAMEWORK
                facetUri = FacetBase + facetName.Substring(facetNamePrefix.Length);
#else
                facetUri = string.Concat(FacetBase, facetName.AsSpan(facetNamePrefix.Length));
#endif
                return true;
            }
            facetUri = string.Empty;
            return false;
        }

        private static readonly ArrayOf<string> s_allFacets = new string[]
        {
            FacetNames.Base,
            FacetNames.SensorParams,
            FacetNames.Optics,
            FacetNames.MediaRtsp,
            FacetNames.MediaJpeg,
            FacetNames.MediaInline,
            FacetNames.MediaDataChannel,
            FacetNames.EndpointConfig,
            FacetNames.Calibration,
            FacetNames.ResultInspection,
            FacetNames.ResultDetection,
            FacetNames.Feedback,
            FacetNames.InferenceOnServer,
            FacetNames.InferenceOffServer,
            FacetNames.Simulation,
            FacetNames.Learning,
            FacetNames.InteropScene,
            FacetNames.Interop40100,
            FacetNames.InteropRobotIntent
        };
    }
}
