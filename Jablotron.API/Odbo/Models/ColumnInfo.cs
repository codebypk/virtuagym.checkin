namespace Jablotron.API.Odbo.Models
{
    /// <summary>
    /// Information about a database column (property).
    /// </summary>
    public class ColumnInfo
    {
        /// <summary>Property-Name.</summary>
        public string Name { get; set; }

        /// <summary>Jablotron Property-Typ (tkLString, tkInteger, tkInt64, tkEnumeration, tkSet, tkClass).</summary>
        public string TypeName { get; set; }
    }
}
