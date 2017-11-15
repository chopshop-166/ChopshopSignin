using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace ChopshopSignin
{
    sealed internal class Scan : IEquatable<Scan>
    {
        public enum LocationType { Invalid, In, Out, Uncounted }

        public LocationType Direction { get; private set; }
        public DateTime ScanTime { get; private set; }

        private Scan()
        {
            ScanTime = DateTime.Now;
        }

        public Scan(LocationType scannedIn)
            : this()
        {
            Direction = scannedIn;
        }

        public Scan(XElement xmlScan)
        {
            ScanTime = (DateTime)xmlScan.Attribute("timestamp");
            Direction = ParseEnum<LocationType>((string)xmlScan.Attribute("direction"));
        }

        public static void SaveScans(IEnumerable<Scan> scans, string file)
        {
            new XElement("Scans", scans.Select(x => x.ToXml())).Save(file);
        }

        public static IEnumerable<Scan> LoadScans(string file)
        {
            return XElement.Load(file)
                           .Elements()
                           .Select(x => new Scan(x));
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", Direction.ToString(), ScanTime);
        }

        public XElement ToXml()
        {
            return new XElement("Scan",
                        new XAttribute("direction", Direction),
                        new XAttribute("timestamp", ScanTime));
        }

        public override int GetHashCode()
        {
            return Direction.GetHashCode() + 13 * ScanTime.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as Scan);
        }

        public bool Equals(Scan other)
        {
            if (object.ReferenceEquals(this, other)) return true;

            if (other != null)
                if (Direction == other.Direction)
                    if (ScanTime == other.ScanTime)
                        return true;

            return false;
        }

        private static T ParseEnum<T>(string enumString) where T : struct
        {
            T result;
            if (Enum.TryParse<T>(enumString, true, out result))
                return result;

            return default(T);
        }
    }
}
