using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace GpxLibrary
{
    public class GpxWriter
    {
		static XNamespace nsGpx = "http://www.topografix.com/GPX/1/1";

		public void Write(string gpxFileName, IEnumerable<Waypoint> waypoints)
		{
			var gpx = new XElement(nsGpx + "gpx");

			foreach (var waypoint in waypoints)
			{
				XElement wpt = new XElement(nsGpx + "wpt");
				gpx.Add(wpt);

				wpt.SetAttributeValue("lat", waypoint.Latitude);
				wpt.SetAttributeValue("lon", waypoint.Longitude);

				if (waypoint.Name != null)
					wpt.SetElementValue(nsGpx + "name", waypoint.Name);
				if (waypoint.Symbol != null)
					wpt.SetElementValue(nsGpx + "sym", waypoint.Symbol);

				wpt.SetElementValue(nsGpx + "type", "WPT");
			}

			gpx.Save(gpxFileName);
		}
    }
}
