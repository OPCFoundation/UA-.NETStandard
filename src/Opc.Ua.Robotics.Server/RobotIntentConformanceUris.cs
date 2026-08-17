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

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Robot Intent profile and facet URI constants from clause 12.4.
    /// </summary>
    public static class RobotIntentConformanceUris
    {
        /// <summary>
        /// Base URI for Robot Intent Server profiles.
        /// </summary>
        public const string ProfileBase = "http://opcfoundation.org/UA-Profile/RobotIntent/Server/";

        /// <summary>
        /// Base URI for Robot Intent facets.
        /// </summary>
        public const string FacetBase = "http://opcfoundation.org/UA-Profile/RobotIntent/Facet/";

        /// <summary>
        /// Robot Intent facet names from clause 12.2.
        /// </summary>
        public static class FacetNames
        {
            public const string Base = "RI-Base";
            public const string MotionJoint = "RI-Motion-Joint";
            public const string MotionLinear = "RI-Motion-Linear";
            public const string MotionCircular = "RI-Motion-Circular";
            public const string Trajectory = "RI-Trajectory";
            public const string Path = "RI-Path";
            public const string Force = "RI-Force";
            public const string RealTimeChannel = "RI-RealTimeChannel";
            public const string Safety = "RI-Safety";
            public const string Description = "RI-Description";
            public const string ProcessArcWeld = "RI-Process-ArcWeld";
            public const string ProcessSpotWeld = "RI-Process-SpotWeld";
            public const string ProcessDispense = "RI-Process-Dispense";
            public const string ProcessFasten = "RI-Process-Fasten";
            public const string ProcessPalletise = "RI-Process-Palletise";
            public const string ProcessSurfaceFinish = "RI-Process-SurfaceFinish";
            public const string Grasp = "RI-Grasp";
            public const string PickPlace = "RI-PickPlace";
            public const string ToolChange = "RI-ToolChange";
            public const string Output = "RI-Output";
            public const string Program = "RI-Program";
            public const string Wait = "RI-Wait";
            public const string Queue = "RI-Queue";
            public const string Blending = "RI-Blending";
            public const string Pause = "RI-Pause";
            public const string Retry = "RI-Retry";
            public const string Mission = "RI-Mission";
            public const string MissionHorizon = "RI-Mission-Horizon";
            public const string MissionBranching = "RI-Mission-Branching";
            public const string Interop40010 = "RI-Interop-40010";
            public const string InteropVision = "RI-Interop-Vision";
        }

        /// <summary>
        /// Robot Intent Server profile URIs.
        /// </summary>
        public static class Profiles
        {
            public const string Motion = ProfileBase + "Motion";
            public const string Handling = ProfileBase + "Handling";
            public const string Path = ProfileBase + "Path";
            public const string Mission = ProfileBase + "Mission";
        }

        /// <summary>
        /// Robot Intent facet URIs.
        /// </summary>
        public static class Facets
        {
            public const string Base = FacetBase + "Base";
            public const string MotionJoint = FacetBase + "Motion-Joint";
            public const string MotionLinear = FacetBase + "Motion-Linear";
            public const string MotionCircular = FacetBase + "Motion-Circular";
            public const string Trajectory = FacetBase + "Trajectory";
            public const string Path = FacetBase + "Path";
            public const string Force = FacetBase + "Force";
            public const string RealTimeChannel = FacetBase + "RealTimeChannel";
            public const string Safety = FacetBase + "Safety";
            public const string Description = FacetBase + "Description";
            public const string ProcessArcWeld = FacetBase + "Process-ArcWeld";
            public const string ProcessSpotWeld = FacetBase + "Process-SpotWeld";
            public const string ProcessDispense = FacetBase + "Process-Dispense";
            public const string ProcessFasten = FacetBase + "Process-Fasten";
            public const string ProcessPalletise = FacetBase + "Process-Palletise";
            public const string ProcessSurfaceFinish = FacetBase + "Process-SurfaceFinish";
            public const string Grasp = FacetBase + "Grasp";
            public const string PickPlace = FacetBase + "PickPlace";
            public const string ToolChange = FacetBase + "ToolChange";
            public const string Output = FacetBase + "Output";
            public const string Program = FacetBase + "Program";
            public const string Wait = FacetBase + "Wait";
            public const string Queue = FacetBase + "Queue";
            public const string Blending = FacetBase + "Blending";
            public const string Pause = FacetBase + "Pause";
            public const string Retry = FacetBase + "Retry";
            public const string Mission = FacetBase + "Mission";
            public const string MissionHorizon = FacetBase + "Mission-Horizon";
            public const string MissionBranching = FacetBase + "Mission-Branching";
            public const string Interop40010 = FacetBase + "Interop-40010";
            public const string InteropVision = FacetBase + "Interop-Vision";
        }

        internal static bool TryGetFacetUri(string facetName, out string facetUri)
        {
            const string facetNamePrefix = "RI-";
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
    }
}
