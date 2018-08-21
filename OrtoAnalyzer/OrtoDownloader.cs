using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace OrtoAnalyzer
{
    public class OrtoDownloader
    {
		readonly IFormatProvider IC = System.Globalization.CultureInfo.InvariantCulture;

		public void Download(int north, int east, int south, int west, string outputFolder)
		{
			// https://kso.etjanster.lantmateriet.se/karta/ortofoto/wms/v1.2?LAYERS=orto025&EXCEPTIONS=application%2Fvnd.ogc.se_xml&FORMAT=image%2Fpng&TRANSPARENT=TRUE&STYLES=default%2Cdefault&SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&SRS=EPSG%3A3006&BBOX=643645,6719691,648365,6722731&WIDTH=4096&HEIGHT=4096	

			// Start at 'west', step 1024 meters east every loop
			WebClient webClient = new WebClient();

			for (int currWest = west; currWest < east; currWest += 1024)
			{
				// Start at 'north', step 1024 meters south every loop
				for (int currNorth = north; currNorth > south; currNorth -= 1024)
				{
					// Create bounding box for this part
					int currSouth = Math.Max(currNorth - 1024, south);
					int currEast = Math.Min(currWest + 1024, east);

					string boundingBox = currWest.ToString(IC) + "," + currSouth.ToString(IC) + "," + currEast.ToString(IC) + "," + currNorth.ToString(IC);

					string localFileName = Path.Combine(outputFolder, boundingBox + ".png");
					if (!File.Exists(localFileName))
					{
						// Calculate needed image size for 0.25 m per pixel (4 pixels per meter)
						int height = (currNorth - currSouth) * 4;
						int width = (currEast - currWest) * 4;

						string fetchUrl = "https://kso.etjanster.lantmateriet.se/karta/ortofoto/wms/v1.2?LAYERS=orto025&EXCEPTIONS=application%2Fvnd.ogc.se_xml&FORMAT=image%2Fpng&TRANSPARENT=TRUE&STYLES=default%2Cdefault&SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&SRS=EPSG%3A3006&BBOX=" + boundingBox + "&WIDTH=" + width.ToString(IC) + "&HEIGHT=" + height.ToString(IC);

						webClient.DownloadFile(fetchUrl, localFileName);
					}
				}
			}
		}
	}
}
