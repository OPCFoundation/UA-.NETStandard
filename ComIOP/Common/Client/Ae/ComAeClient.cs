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
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using OpcRcw.Comn;
using OpcRcw.Ae;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Provides access to a COM DA server.
    /// </summary>
    public class ComAeClient : ComClient
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComAeClient"/> class.
        /// </summary>
        /// <param name="configuration"></param>
        public ComAeClient(ComAeClientConfiguration configuration) : base(configuration)
        {
            m_configuration = configuration;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates a new instance of the client with the same configuration.
        /// </summary>
        /// <returns>The copy of the client.</returns>
        public ComAeClient CloneClient()
        {
            return new ComAeClient(m_configuration);
        }

        /// <summary>
        /// Reads the status from the server.
        /// </summary>
        public OPCEVENTSERVERSTATUS? GetStatus()
        {
            string methodName = "IOPCEventServer.GetStatus";

            try
            {
                IOPCEventServer server = BeginComCall<IOPCEventServer>(methodName, true);

                IntPtr ppServerStatus;
                server.GetStatus(out ppServerStatus);

                OPCEVENTSERVERSTATUS pStatus = (OPCEVENTSERVERSTATUS)Marshal.PtrToStructure(ppServerStatus, typeof(OPCEVENTSERVERSTATUS));

                Marshal.DestroyStructure(ppServerStatus, typeof(OPCEVENTSERVERSTATUS));
                Marshal.FreeCoTaskMem(ppServerStatus);

                return pStatus;
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates the area browser.
        /// </summary>
        /// <returns>An object which browses areas and sources.</returns>
        public IOPCEventAreaBrowser CreateAreaBrowser()
        {
            object unknown = null;

            string methodName = "IOPCEventServer.CreateAreaBrowser";

            try
            {
                IOPCEventServer server = BeginComCall<IOPCEventServer>(methodName, true);
                Guid riid = typeof(IOPCEventAreaBrowser).GUID;
                server.CreateAreaBrowser(ref riid, out unknown);
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL, ResultIds.E_NOTIMPL))
                {
                    ComCallError(methodName, e);
                }

                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            return (IOPCEventAreaBrowser)unknown;
        }

        /// <summary>
        /// Creates an event subscription.
        /// </summary>
        /// <returns>An object which manages a subscription.</returns>
        public IOPCEventSubscriptionMgt CreateEventSubscription()
        {
            object unknown = null;

            string methodName = "IOPCEventServer.CreateEventSubscription";

            try
            {
                IOPCEventServer server = BeginComCall<IOPCEventServer>(methodName, true);
                Guid riid = typeof(IOPCEventSubscriptionMgt).GUID;

                int revisedBufferTime = 0;
                int revisedMaxSize = 0;

                server.CreateEventSubscription(
                    1,
                    0,
                    0,
                    0,
                    ref riid,
                    out unknown,
                    out revisedBufferTime,
                    out revisedMaxSize);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            return (IOPCEventSubscriptionMgt)unknown;
        }

        /// <summary>
        /// Acknowledges the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="eventId">The event id.</param>
        /// <param name="comment">The comment.</param>
        /// <returns></returns>
        public uint Acknowledge(
            ServerSystemContext context,
            byte[] eventId,
            LocalizedText comment)
        {
            // get the user name from the context.
            string userName = String.Empty;

            if (context.UserIdentity != null)
            {
                userName = context.UserIdentity.DisplayName;
            }

            // get the comment.
            string commentText = String.Empty;

            if (comment != null)
            {
                commentText = comment.Text;
            }

            System.Runtime.InteropServices.ComTypes.FILETIME ftActiveTime;

            // unpack the event id.
            ServiceMessageContext messageContext = new ServiceMessageContext();

            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            messageContext.Factory = context.EncodeableFactory;

            BinaryDecoder decoder = new BinaryDecoder(eventId, messageContext);

            string source = decoder.ReadString(null);
            string conditionName = decoder.ReadString(null);
            ftActiveTime.dwHighDateTime = decoder.ReadInt32(null);
            ftActiveTime.dwLowDateTime = decoder.ReadInt32(null);
            int cookie = decoder.ReadInt32(null);

            decoder.Close();

            string methodName = "IOPCEventServer.AckCondition";

            IntPtr pErrors = IntPtr.Zero;
            
            try
            {
                IOPCEventServer server = BeginComCall<IOPCEventServer>(methodName, true);

                server.AckCondition(
                    1,
                    userName,
                    commentText,
                    new string[] { source },
                    new string[] { conditionName },
                    new System.Runtime.InteropServices.ComTypes.FILETIME[] { ftActiveTime },
                    new int[] { cookie },
                    out pErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal results.
            int[] errors = ComUtils.GetInt32s(ref pErrors, 1, true);
                        
            if (errors[0] == ResultIds.S_ALREADYACKED)
            {
                return StatusCodes.BadConditionBranchAlreadyAcked;
            }
            else if (errors[0] < 0)
            {
                return StatusCodes.BadEventIdUnknown;
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Gets the event categories.
        /// </summary>
        /// <param name="eventType">Type of the event.</param>
        /// <param name="categories">The categories.</param>
        /// <param name="descriptions">The descriptions.</param>
        public void GetEventCategories(int eventType, out int[] categories, out string[] descriptions)
        {
            string methodName = "IOPCEventServer.QueryEventCategories";

            int count = 0;
            IntPtr pCategories = IntPtr.Zero;
            IntPtr pDescriptions = IntPtr.Zero;

            try
            {
                IOPCEventServer server = BeginComCall<IOPCEventServer>(methodName, true);

                server.QueryEventCategories(
                    eventType,
                    out count,
                    out pCategories,
                    out pDescriptions);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal results.
            categories = ComUtils.GetInt32s(ref pCategories, count, true);
            descriptions = ComUtils.GetUnicodeStrings(ref pDescriptions, count, true);
        }

        /// <summary>
        /// Returns the condition names for the event category.
        /// </summary>
        public void GetConditionNames(int eventCategory, out string[] conditionNames)
        {
            conditionNames = null;
            string methodName = "IOPCEventServer.QueryConditionNames";

            int count = 0;
            IntPtr pConditionNames = IntPtr.Zero;

            try
            {
                IOPCEventServer server = BeginComCall<IOPCEventServer>(methodName, true);

                server.QueryConditionNames(
                    eventCategory,
                    out count,
                    out pConditionNames);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal results.
            conditionNames = ComUtils.GetUnicodeStrings(ref pConditionNames, count, true);
        }

        /// <summary>
        /// Returns the sub-condition names for the event condition.
        /// </summary>
        public void GetSubConditionNames(string conditionName, out string[] subConditionNames)
        {
            subConditionNames = null;
            string methodName = "IOPCEventServer.QuerySubConditionNames";

            int count = 0;
            IntPtr pSubConditionNames = IntPtr.Zero;

            try
            {
                IOPCEventServer server = BeginComCall<IOPCEventServer>(methodName, true);

                server.QuerySubConditionNames(
                    conditionName,
                    out count,
                    out pSubConditionNames);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal results.
            subConditionNames = ComUtils.GetUnicodeStrings(ref pSubConditionNames, count, true);
        }

        /// <summary>
        /// Gets the event attributes.
        /// </summary>
        /// <param name="categoryId">The category id.</param>
        /// <param name="attributeIds">The attribute ids.</param>
        /// <param name="descriptions">The descriptions.</param>
        /// <param name="datatypes">The datatypes.</param>
        public bool GetEventAttributes(
            int categoryId, 
            out int[] attributeIds,
            out string[] descriptions,
            out short[] datatypes)
        {
            string methodName = "IOPCEventServer.QueryEventAttributes";

            int count = 0;
            IntPtr pAttributeIds = IntPtr.Zero;
            IntPtr pDescriptions = IntPtr.Zero;
            IntPtr pDataTypes = IntPtr.Zero;

            try
            {
                IOPCEventServer server = BeginComCall<IOPCEventServer>(methodName, true);

                server.QueryEventAttributes(
                    categoryId,
                    out count,
                    out pAttributeIds,
                    out pDescriptions,
                    out pDataTypes);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal results.
            attributeIds = ComUtils.GetInt32s(ref pAttributeIds, count, true);
            descriptions = ComUtils.GetUnicodeStrings(ref pDescriptions, count, true);
            datatypes = ComUtils.GetInt16s(ref pDataTypes, count, true);

            // remove the AREAS attribute which is never exposed.
            for (int ii = 0; ii < count; ii++)
            {
                if (String.Compare(descriptions[ii], "AREAS", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    int[] attributeIds2 = new int[count-1];
                    string[] descriptions2 = new string[count-1];
                    short[] datatypes2 = new short[count-1];

                    if (ii > 0)
                    {
                        Array.Copy(attributeIds, attributeIds2, ii);
                        Array.Copy(descriptions, descriptions2, ii);
                        Array.Copy(datatypes, datatypes2, ii);
                    }

                    if (ii < count-1)
                    {
                        Array.Copy(attributeIds, ii+1, attributeIds2, ii, count-ii-1);
                        Array.Copy(descriptions, ii+1, descriptions2, ii, count-ii-1);
                        Array.Copy(datatypes, ii+1, datatypes2, ii, count-ii-1);
                    }

                    attributeIds = attributeIds2;
                    descriptions = descriptions2;
                    datatypes = datatypes2;
                    break;
                }
            }

            return count > 0;
        }
        #endregion

        #region Private Fields
        private ComAeClientConfiguration m_configuration;
        #endregion
    }
}
