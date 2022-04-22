using MightyLittleGeodesy.Positions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace OrtoAnalyzer
{
	public class SweRefRegion
	{
		public int North { get; private set; }
		public int East { get; private set; }
		public int South { get; private set; }
		public int West { get; private set; }

		public SweRefRegion(int west, int north, int south, int east)
		{
			West = west;
			North = north;
			South = south;
			East = east;
		}

		public override string ToString()
		{
			return "" + North + ", " + West + ", " + South + ", " + East;
		}
	}

	public class OrtoDownloader
    {
		readonly static IFormatProvider IC = System.Globalization.CultureInfo.InvariantCulture;

		const int BlockSize = 1000;

		public static SweRefRegion GetBoundingRegion(WGS84Position northWest, WGS84Position southEast)
		{
			var northEast = new WGS84Position(northWest.Latitude, southEast.Longitude);
			var southWest = new WGS84Position(southEast.Latitude, northWest.Longitude);
			var corners = new[] { northWest, northEast, southWest, southEast };

			var cornersSweRef99 = corners.Select(wgs => new SWEREF99Position(wgs, SWEREF99Position.SWEREFProjection.sweref_99_tm)).ToList();

			var north99 = (int)Math.Ceiling(cornersSweRef99.Max(x => x.Latitude));
			var south99 = (int)Math.Floor(cornersSweRef99.Min(x => x.Latitude));
			var east99 = (int)Math.Ceiling(cornersSweRef99.Max(x => x.Longitude));
			var west99 = (int)Math.Floor(cornersSweRef99.Min(x => x.Longitude));

			var sweRegion = new SweRefRegion(north: north99, east: east99, south: south99, west: west99);
			return sweRegion;
		}

		public void Download(SweRefRegion region, string outputFolder)
		{
			WebClient webClient = new WebClient();

			// Go to nearest 'BlockSize' for all coordinates - so downloader can reuse images for different overlapping regions
			int north = (int)Math.Ceiling((decimal)region.North / BlockSize) * BlockSize;
			int east = (int)Math.Ceiling((decimal)region.East / BlockSize) * BlockSize;
			int south = (int)Math.Floor((decimal)region.South / BlockSize) * BlockSize;
			int west = (int)Math.Floor((decimal)region.West / BlockSize) * BlockSize;

			Console.Out.WriteLine("Downloading region: " + region);

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
					string fileName = GetFileNameFromSWEREF(new SweRefRegion(west: currWest, north: currNorth, south: currSouth, east: currEast));

					string localFileName = Path.Combine(outputFolder, fileName);
					if (!File.Exists(localFileName))
					{
						// Calculate needed image size for 0.25 m per pixel (4 pixels per meter)
						int height = (currNorth - currSouth) * 4;
						int width = (currEast - currWest) * 4;

						string fetchUrl = "https://minkarta.lantmateriet.se/map/ortofoto/?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image%2Fpng&TRANSPARENT=false&LAYERS=Ortofoto_0.5%2COrtofoto_0.4%2COrtofoto_0.25%2COrtofoto_0.16&TILED=true&STYLES=&SRS=EPSG%3A3006&BBOX=" + boundingBox + "&WIDTH=" + width.ToString(IC) + "&HEIGHT=" + height.ToString(IC);

						Console.Out.Write("Downloading file: " + localFileName);
						webClient.DownloadFile(fetchUrl, localFileName);
						Console.Out.WriteLine(".");
					}
				}
			}
		}

		internal static IEnumerable<string> GetUntouchedFiles(string ortoFolderPath)
		{
			return Directory.GetFiles(ortoFolderPath, "*.png").Where(x => !x.Contains(Analyzer.SuffixAnalyzed));
		}

		internal const char SplitChar = ',';

		private static string GetFileNameFromSWEREF(SweRefRegion region)
		{
			return region.West.ToString(IC) + SplitChar + region.South.ToString(IC) + SplitChar + region.East.ToString(IC) + SplitChar + region.North.ToString(IC) + ".png";
		}

		public static SweRefRegion ParseFileNameToSWEREF(string fileName)
		{
			var noExt = Path.GetFileNameWithoutExtension(fileName);
			var parts = noExt.Split(SplitChar);

			return new SweRefRegion(
					west: int.Parse(parts[0], IC),
					south: int.Parse(parts[1], IC),
					east: int.Parse(parts[2], IC),
					north: int.Parse(parts[3], IC)
				);
		}
	}
}
