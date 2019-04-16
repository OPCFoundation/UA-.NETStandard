/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Com
{
	/// <summary>
	/// A description of an interface version defined by an OPC specification.
	/// </summary>
	public struct Specification
	{
		#region Constructors
		/// <summary>
		/// Initializes the object with the description and a GUId as a string.
		/// </summary>
		public Specification(string id, string description)
		{
			m_id = id;
			m_description = description;
		}
		#endregion

		#region Public Properties
		/// <summary>
		/// The unique Identifier for the interface version. 
		/// </summary>
		public string Id
		{
			get { return m_id;  }
			internal set { m_id = value; }
		}

		/// <summary>
		/// The human readable description for the interface version.
		/// </summary>
		public string Description
		{
			get { return m_description; }
			internal set { m_description = value; }
		}
		#endregion

		#region Comparison Operators
		/// <summary>
		/// Determines if the object is equal to the specified value.
		/// </summary>
		public override bool Equals(object target)
		{
			if (target != null && target.GetType() == typeof(Specification))
			{
				return (Id == ((Specification)target).Id);
			}

			return false;
		}

		/// <summary>
		/// Converts the object to a string used for display.
		/// </summary>
		public override string ToString()
		{
			return Description;
		}
		
		/// <summary>
		/// Returns a suitable hash code for the result.
		/// </summary>
		public override int GetHashCode()
		{
			return (Id != null)?Id.GetHashCode():base.GetHashCode();
		}

		/// <summary>
		/// Returns true if the objects are equal.
		/// </summary>
		public static bool operator==(Specification a, Specification b) 
		{
			return a.Equals(b);
		}

		/// <summary>
		/// Returns true if the objects are not equal.
		/// </summary>
		public static bool operator!=(Specification a, Specification b) 
		{
			return !a.Equals(b);
		}
		#endregion

		#region Private Members
		private string m_id;
		private string m_description;
		#endregion

		#region Specification Constants
		/// <summary>
		/// A set of Specification objects for existing OPC specifications.
		/// </summary>
		public static readonly Specification Ae10    = new Specification("58E13251-AC87-11d1-84D5-00608CB8A7E9", "Alarms and Event 1.XX");
		/// <remarks/>
		public static readonly Specification Batch10 = new Specification("A8080DA0-E23E-11D2-AFA7-00C04F539421", "Batch 1.00");
		/// <remarks/>
		public static readonly Specification Batch20 = new Specification("843DE67B-B0C9-11d4-A0B7-000102A980B1", "Batch 2.00");
		/// <remarks/>
		public static readonly Specification Da10    = new Specification("63D5F430-CFE4-11d1-B2C8-0060083BA1FB", "Data Access 1.0a");
		/// <remarks/>
		public static readonly Specification Da20    = new Specification("63D5F432-CFE4-11d1-B2C8-0060083BA1FB", "Data Access 2.XX");
		/// <remarks/>
		public static readonly Specification Da30    = new Specification("CC603642-66D7-48f1-B69A-B625E73652D7", "Data Access 3.00");
		/// <remarks/>
		public static readonly Specification Dx10    = new Specification("A0C85BB8-4161-4fd6-8655-BB584601C9E0", "Data eXchange 1.00");
		/// <remarks/>
		public static readonly Specification Hda10   = new Specification("7DE5B060-E089-11d2-A5E6-000086339399", "Historical Data Access 1.XX");
		/// <remarks/>
		public static readonly Specification XmlDa10 = new Specification("3098EDA4-A006-48b2-A27F-247453959408", "XML Data Access 1.00");
		#endregion
	}
}
