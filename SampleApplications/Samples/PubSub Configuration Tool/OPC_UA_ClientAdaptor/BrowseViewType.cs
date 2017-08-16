using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientAdaptor
{
    public enum BrowseViewType
    {
        /// <summary>
        ///     All nodes and references in the address space.
        /// </summary>
        All,

        /// <summary>
        ///     The object instance hierarchy.
        /// </summary>
        Objects,

        /// <summary>
        ///     The type hierarchies.
        /// </summary>
        Types,

        /// <summary>
        ///     The object type hierarchies.
        /// </summary>
        ObjectTypes,

        /// <summary>
        ///     The event type hierarchies.
        /// </summary>
        EventTypes,

        /// <summary>
        ///     The data type hierarchies.
        /// </summary>
        DataTypes,

        /// <summary>
        ///     The reference type hierarchies.
        /// </summary>
        ReferenceTypes,

        /// <summary>
        ///     A server defined view.
        /// </summary>
        ServerDefinedView
    }
}
