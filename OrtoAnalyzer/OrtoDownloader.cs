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

		const int BlockSize = 1000;

		public void Download(int north, int east, int south, int west, string outputFolder)
		{
			WebClient webClient = new WebClient();

			// Go to nearest 'BlockSize' for all coordinates - so downloader can reuse images for different overlapping regions
			north = (int)Math.Ceiling((decimal)north / BlockSize) * BlockSize;
			east = (int)Math.Ceiling((decimal)east / BlockSize) * BlockSize;
			south = (int)Math.Floor((decimal)south / BlockSize) * BlockSize;
			west = (int)Math.Floor((decimal)west / BlockSize) * BlockSize;

			// Start at 'west', step BlockSize meters east every loop
			for (int currWest = west; currWest < east; currWest += BlockSize)
			{
				// Start at 'north', step BlockSize meters south every loop
				for (int currNorth = north; currNorth > south; currNorth -= BlockSize)
				{
					// Create bounding box for this part
					int currSouth = currNorth - BlockSize;
					int currEast = currWest + BlockSize;

					string boundingBox = currWest.ToString(IC) + "%2C" + currSouth.ToString(IC) + "%2C" + currEast.ToString(IC) + "%2C" + currNorth.ToString(IC);
					string fileName = currWest.ToString(IC) + "," + currSouth.ToString(IC) + "," + currEast.ToString(IC) + "," + currNorth.ToString(IC);

					string localFileName = Path.Combine(outputFolder, fileName + ".png");
					if (!File.Exists(localFileName))
					{
						// Calculate needed image size for 0.25 m per pixel (4 pixels per meter)
						int height = (currNorth - currSouth) * 4;
						int width = (currEast - currWest) * 4;

						string fetchUrl = "https://minkarta.lantmateriet.se/map/ortofoto/?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image%2Fpng&TRANSPARENT=false&LAYERS=Ortofoto_0.5%2COrtofoto_0.4%2COrtofoto_0.25%2COrtofoto_0.16&TILED=true&STYLES=&SRS=EPSG%3A3006&BBOX=" + boundingBox + "&WIDTH=" + width.ToString(IC) + "&HEIGHT=" + height.ToString(IC);

						webClient.DownloadFile(fetchUrl, localFileName);
					}
				}
			}
		}
	}
}
