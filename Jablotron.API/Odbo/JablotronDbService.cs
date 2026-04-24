using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using Jablotron.API.Odbo.Models;

namespace Jablotron.API.Odbo
{
    /// <summary>
    /// Service for reading the Jablotron ODBO-Link database (.fdb).
    /// The file format is not Firebird but a zlib-compressed XML file
    /// with a proprietary header (3 null bytes + "ODBO-Link database file" + 0xFF + 16-byte hash).
    /// </summary>
    public class JablotronDbService : IDisposable
    {
        private readonly XmlDocument _doc;
        private readonly XmlNode _root;
        private bool _disposed;

        /// <summary>File format info.</summary>
        public OdboFileInfo FileInfo { get; private set; }

        /// <summary>
        /// Creates a new JablotronDbService.
        /// </summary>
        /// <param name="fdbPath">Path to the .fdb file.</param>
        public JablotronDbService(string fdbPath)
        {
            if (!File.Exists(fdbPath))
                throw new FileNotFoundException("Jablotron file not found.", fdbPath);

            var raw = File.ReadAllBytes(fdbPath);

            // Validate header: Bytes 3..25 = "ODBO-Link database file"
            string headerStr = System.Text.Encoding.ASCII.GetString(raw, 3, 23);
            if (!headerStr.StartsWith("ODBO-Link"))
                throw new InvalidOperationException(
                    $"Not a valid ODBO-Link file. Header: \"{headerStr}\"");

            // Byte 28 = 0xFF, Bytes 29-30 = zlib header (78 01), deflate ab Offset 31
            const int deflateOffset = 31;
            const int hashPrefixLength = 16;

            byte[] decompressed;
            using (var ms = new MemoryStream(raw, deflateOffset, raw.Length - deflateOffset))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                ds.CopyTo(outMs);
                decompressed = outMs.ToArray();
            }

            // First 16 bytes = hash (MD5), then the XML follows
            string xmlStr = System.Text.Encoding.UTF8.GetString(decompressed, hashPrefixLength, decompressed.Length - hashPrefixLength);

            _doc = new XmlDocument();
            _doc.LoadXml(xmlStr);
            _root = _doc.DocumentElement;

            // Database name from root attribute or tag name
            string dbName = _root.Attributes["name"]?.Value ?? _root.Name;
            string dbVersion = null;

            var versionNode = _root.SelectSingleNode("class[@name='JAVersion']/class/property[@name='DBVersion']");
            if (versionNode != null)
                dbVersion = versionNode.InnerText.Trim();

            FileInfo = new OdboFileInfo
            {
                DatabaseName = dbName,
                DatabaseVersion = dbVersion,
                CompressedSize = raw.Length,
                DecompressedSize = decompressed.Length,
                SectionCount = _root.ChildNodes.Cast<XmlNode>().Count(n => n.Name == "class")
            };
        }

        /// <summary>
        /// Returns the names of all sections (top-level "class" elements).
        /// These correspond to the "tables" in the Jablotron database.
        /// </summary>
        public List<string> GetSectionNames()
        {
            var names = new List<string>();
            foreach (XmlNode child in _root.ChildNodes)
            {
                if (child.Name == "class")
                {
                    string name = child.Attributes["name"]?.Value ?? child.Name;
                    if (!names.Contains(name))
                        names.Add(name);
                    else
                    {
                        int idx = 2;
                        while (names.Contains($"{name} ({idx})")) idx++;
                        names.Add($"{name} ({idx})");
                    }
                }
            }
            return names;
        }

        /// <summary>
        /// Returns the column information of a section.
        /// Determines the properties from the first item.
        /// </summary>
        public List<ColumnInfo> GetColumns(string sectionName)
        {
            var columns = new List<ColumnInfo>();
            var sectionNode = FindSection(sectionName);
            if (sectionNode == null) return columns;

            var innerClass = sectionNode.SelectSingleNode("class");
            if (innerClass == null) return columns;

            var firstItem = innerClass.SelectSingleNode("item");
            if (firstItem == null)
            {
                foreach (XmlNode prop in innerClass.SelectNodes("property"))
                {
                    columns.Add(new ColumnInfo
                    {
                        Name = prop.Attributes["name"]?.Value ?? "?",
                        TypeName = prop.Attributes["type"]?.Value ?? "?"
                    });
                }
                return columns;
            }

            foreach (XmlNode prop in firstItem.ChildNodes)
            {
                if (prop.Name == "property")
                {
                    columns.Add(new ColumnInfo
                    {
                        Name = prop.Attributes["name"]?.Value ?? "?",
                        TypeName = prop.Attributes["type"]?.Value ?? "?"
                    });
                }
            }

            return columns;
        }

        /// <summary>
        /// Reads all items of a section as a DataTable.
        /// </summary>
        public DataTable ReadSection(string sectionName, int maxRows = 10000)
        {
            var dt = new DataTable(sectionName);
            var sectionNode = FindSection(sectionName);
            if (sectionNode == null) return dt;

            var innerClass = sectionNode.SelectSingleNode("class");
            if (innerClass == null) return dt;

            var items = innerClass.SelectNodes("item");
            if (items == null || items.Count == 0)
            {
                var props = innerClass.SelectNodes("property");
                if (props != null && props.Count > 0)
                {
                    foreach (XmlNode prop in props)
                    {
                        string colName = prop.Attributes["name"]?.Value ?? "?";
                        if (!dt.Columns.Contains(colName))
                            dt.Columns.Add(colName, typeof(string));
                    }

                    var row = dt.NewRow();
                    foreach (XmlNode prop in props)
                    {
                        string colName = prop.Attributes["name"]?.Value ?? "?";
                        row[colName] = GetPropertyValue(prop);
                    }
                    dt.Rows.Add(row);
                }
                return dt;
            }

            bool columnsCreated = false;
            int rowCount = 0;

            foreach (XmlNode item in items)
            {
                if (rowCount >= maxRows) break;

                if (!columnsCreated)
                {
                    foreach (XmlNode prop in item.ChildNodes)
                    {
                        if (prop.Name == "property")
                        {
                            string colName = prop.Attributes["name"]?.Value ?? "?";
                            if (!dt.Columns.Contains(colName))
                                dt.Columns.Add(colName, typeof(string));
                        }
                    }
                    columnsCreated = true;
                }

                var dataRow = dt.NewRow();
                foreach (XmlNode prop in item.ChildNodes)
                {
                    if (prop.Name == "property")
                    {
                        string colName = prop.Attributes["name"]?.Value ?? "?";
                        if (dt.Columns.Contains(colName))
                            dataRow[colName] = GetPropertyValue(prop);
                    }
                }
                dt.Rows.Add(dataRow);
                rowCount++;
            }

            return dt;
        }

        /// <summary>
        /// Returns the number of items in a section.
        /// </summary>
        public int GetItemCount(string sectionName)
        {
            var sectionNode = FindSection(sectionName);
            if (sectionNode == null) return 0;

            var innerClass = sectionNode.SelectSingleNode("class");
            if (innerClass == null) return 0;

            var items = innerClass.SelectNodes("item");
            if (items == null || items.Count == 0)
            {
                var props = innerClass.SelectNodes("property");
                return (props != null && props.Count > 0) ? 1 : 0;
            }
            return items.Count;
        }

        /// <summary>
        /// Returns the complete XML document as a string.
        /// </summary>
        public string GetRawXml()
        {
            using (var sw = new StringWriter())
            using (var xw = XmlWriter.Create(sw, new XmlWriterSettings { Indent = true }))
            {
                _doc.WriteTo(xw);
                xw.Flush();
                return sw.ToString();
            }
        }

        /// <summary>
        /// Reads all users from the JA100UsersSetup section.
        /// Returns only entries where IsNull=False.
        /// </summary>
        public List<JablotronUser> GetUsers()
        {
            var users = new List<JablotronUser>();
            var sectionNode = FindSection("JA100UsersSetup");
            if (sectionNode == null) return users;

            var innerClass = sectionNode.SelectSingleNode("class");
            if (innerClass == null) return users;

            var items = innerClass.SelectNodes("item");
            if (items == null) return users;

            foreach (XmlNode item in items)
            {
                var user = new JablotronUser();

                foreach (XmlNode prop in item.ChildNodes)
                {
                    if (prop.Name != "property") continue;
                    string name = prop.Attributes["name"]?.Value;
                    string val = prop.InnerText.Trim();

                    switch (name)
                    {
                        case "ID": int.TryParse(val, out int id); user.ID = id; break;
                        case "Name": user.Name = val; break;
                        case "Code": user.Code = val; break;
                        case "AccessCard1": long.TryParse(val, out long c1); user.AccessCard1 = c1; break;
                        case "AccessCard2": long.TryParse(val, out long c2); user.AccessCard2 = c2; break;
                        case "IsNull": user.IsNull = val.Equals("True", StringComparison.OrdinalIgnoreCase); break;
                        case "IsBlocked": user.IsBlocked = val.Equals("True", StringComparison.OrdinalIgnoreCase); break;
                        case "Permissions": user.Permissions = val; break;
                    }
                }

                if (!user.IsNull)
                    users.Add(user);
            }

            return users;
        }

        private XmlNode FindSection(string sectionName)
        {
            string baseName = sectionName;
            int occurrence = 1;

            int parenIdx = sectionName.LastIndexOf('(');
            if (parenIdx > 0 && sectionName.EndsWith(")"))
            {
                baseName = sectionName.Substring(0, parenIdx).Trim();
                string numStr = sectionName.Substring(parenIdx + 1, sectionName.Length - parenIdx - 2);
                if (int.TryParse(numStr, out int n))
                    occurrence = n;
            }

            int current = 0;
            foreach (XmlNode child in _root.ChildNodes)
            {
                if (child.Name == "class" && (child.Attributes["name"]?.Value ?? child.Name) == baseName)
                {
                    current++;
                    if (current == occurrence)
                        return child;
                }
            }
            return null;
        }

        private string GetPropertyValue(XmlNode propertyNode)
        {
            string type = propertyNode.Attributes["type"]?.Value;

            if (type == "tkClass")
            {
                var innerNode = propertyNode.SelectSingleNode("class");
                if (innerNode != null)
                {
                    string text = innerNode.InnerText.Trim();
                    if (!string.IsNullOrEmpty(text))
                        return $"[{innerNode.Attributes["name"]?.Value}: {text}]";
                    return $"[{innerNode.Attributes["name"]?.Value}]";
                }
            }

            return propertyNode.InnerText.Trim();
        }

        /// <summary>
        /// Reads the access history from the JA100EventHistory section.
        /// Returns only entries with access events.
        /// </summary>
        public List<JablotronAccessEvent> GetAccessHistory(int maxRows = 10000)
        {
            var events = new List<JablotronAccessEvent>();

            // Try different section names for events
            string[] candidateSections = { "JA100EventHistory", "JA100EventLog", "JA100Events" };
            System.Data.DataTable dt = null;

            foreach (var sectionName in candidateSections)
            {
                var sectionNode = FindSection(sectionName);
                if (sectionNode != null)
                {
                    dt = ReadSection(sectionName, maxRows);
                    break;
                }
            }

            if (dt == null || dt.Rows.Count == 0)
                return events;

            // Detect columns dynamically
            bool hasTime = dt.Columns.Contains("Time");
            bool hasDate = dt.Columns.Contains("Date");
            bool hasEventText = dt.Columns.Contains("EventText");
            bool hasSource = dt.Columns.Contains("Source");
            bool hasUserName = dt.Columns.Contains("UserName");
            bool hasUserId = dt.Columns.Contains("UserID");
            bool hasCardCode = dt.Columns.Contains("CardCode");
            bool hasSegment = dt.Columns.Contains("Segment");

            foreach (System.Data.DataRow row in dt.Rows)
            {
                var evt = new JablotronAccessEvent
                {
                    Time = hasTime ? row["Time"]?.ToString() ?? "" : "",
                    Date = hasDate ? row["Date"]?.ToString() ?? "" : "",
                    EventText = hasEventText ? row["EventText"]?.ToString() ?? "" : "",
                    Source = hasSource ? row["Source"]?.ToString() ?? "" : "",
                    UserName = hasUserName ? row["UserName"]?.ToString() ?? "" : "",
                    Segment = hasSegment ? row["Segment"]?.ToString() ?? "" : "",
                };

                if (hasUserId)
                {
                    int uid;
                    int.TryParse(row["UserID"]?.ToString(), out uid);
                    evt.UserID = uid;
                }
                if (hasCardCode)
                {
                    long cc;
                    long.TryParse(row["CardCode"]?.ToString(), out cc);
                    evt.CardCode = cc;
                }

                events.Add(evt);
            }

            return events;
        }

        /// <summary>
        /// Returns the names of all available event sections (for diagnostics).
        /// </summary>
        public List<string> GetEventSectionNames()
        {
            var allSections = GetSectionNames();
            return allSections.Where(s =>
                s.IndexOf("Event", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("History", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Access", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Log", StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
