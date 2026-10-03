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

namespace Opc.Ua.AMB.Server.Maintenance
{
    /// <summary>
    /// What a maintenance activity publishes besides its state: the message
    /// and the optional members of <c>IMaintenanceEventType</c>
    /// (OPC 10000-110 §12.2).
    /// </summary>
    /// <remarks>
    /// A member left unset is not published; once set it is published from
    /// then on. The values may change in every state - the event history
    /// tells planned from actual values.
    /// </remarks>
    public sealed class MaintenanceActivityDetails
    {
        /// <summary>
        /// Gets or sets the description of the activity, which becomes the
        /// <c>Message</c> of its events (§12.1).
        /// </summary>
        public LocalizedText Description { get; set; }

        /// <summary>
        /// Gets or sets the date the activity is scheduled for.
        /// </summary>
        public DateTimeUtc? PlannedDate { get; set; }

        /// <summary>
        /// Gets or sets how long executing the activity will take.
        /// </summary>
        public TimeSpan? EstimatedDowntime { get; set; }

        /// <summary>
        /// Gets or sets the supplier that is to execute, executes or executed
        /// the activity.
        /// </summary>
        public NameNodeIdDataType? MaintenanceSupplier { get; set; }

        /// <summary>
        /// Gets or sets the qualification of the personnel.
        /// </summary>
        public NameNodeIdDataType? QualificationOfPersonnel { get; set; }

        /// <summary>
        /// Gets or sets the parts of the asset that are to be, are being or
        /// were replaced; not published while null.
        /// </summary>
        public ArrayOf<NameNodeIdDataType> PartsOfAssetReplaced { get; set; }

        /// <summary>
        /// Gets or sets the parts of the asset that are to be, are being or
        /// were serviced; not published while null.
        /// </summary>
        public ArrayOf<NameNodeIdDataType> PartsOfAssetServiced { get; set; }

        /// <summary>
        /// Gets or sets whether the activity is executed locally or remotely.
        /// </summary>
        public MaintenanceMethodEnum? MaintenanceMethod { get; set; }

        /// <summary>
        /// Gets or sets whether the configuration of the asset is to be, or
        /// was, changed.
        /// </summary>
        public bool? ConfigurationChanged { get; set; }

        /// <summary>
        /// Copies the details.
        /// </summary>
        internal MaintenanceActivityDetails Clone()
        {
            return (MaintenanceActivityDetails)MemberwiseClone();
        }

        /// <summary>
        /// Applies the members an update changed - the difference between
        /// <paramref name="before"/> and <paramref name="after"/> - to a copy
        /// of these details.
        /// </summary>
        internal MaintenanceActivityDetails WithChanges(
            MaintenanceActivityDetails before,
            MaintenanceActivityDetails after)
        {
            MaintenanceActivityDetails merged = Clone();
            if (!after.Description.Equals(before.Description))
            {
                merged.Description = after.Description;
            }
            if (after.PlannedDate != before.PlannedDate)
            {
                merged.PlannedDate = after.PlannedDate;
            }
            if (after.EstimatedDowntime != before.EstimatedDowntime)
            {
                merged.EstimatedDowntime = after.EstimatedDowntime;
            }
            if (!ReferenceEquals(after.MaintenanceSupplier, before.MaintenanceSupplier))
            {
                merged.MaintenanceSupplier = after.MaintenanceSupplier;
            }
            if (!ReferenceEquals(after.QualificationOfPersonnel, before.QualificationOfPersonnel))
            {
                merged.QualificationOfPersonnel = after.QualificationOfPersonnel;
            }
            if (!after.PartsOfAssetReplaced.Equals(before.PartsOfAssetReplaced))
            {
                merged.PartsOfAssetReplaced = after.PartsOfAssetReplaced;
            }
            if (!after.PartsOfAssetServiced.Equals(before.PartsOfAssetServiced))
            {
                merged.PartsOfAssetServiced = after.PartsOfAssetServiced;
            }
            if (after.MaintenanceMethod != before.MaintenanceMethod)
            {
                merged.MaintenanceMethod = after.MaintenanceMethod;
            }
            if (after.ConfigurationChanged != before.ConfigurationChanged)
            {
                merged.ConfigurationChanged = after.ConfigurationChanged;
            }
            return merged;
        }
    }
}
