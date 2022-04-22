using GpxLibrary;
using MightyLittleGeodesy.Positions;
using OrtoAnalyzer;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static OrtoAnalyzer.Charter;

namespace OrtoCharter
{
    class Program
    {
		static CultureInfo IC = CultureInfo.InvariantCulture;

		static int Main(string[] args)
		{
			// Parse command line for coordinates
			double north, east, south, west;
			try
			{
				double lat0 = double.Parse(args[0], IC);
				double lon0 = double.Parse(args[1], IC);
				double lat1 = double.Parse(args[2], IC);
				double lon1 = double.Parse(args[3], IC);

				north = Math.Max(lat0, lat1);
				east = Math.Max(lon0, lon1);
				south = Math.Min(lat0, lat1);
				west = Math.Min(lon0, lon1);
			}
			catch
			{
				Console.Error.WriteLine("Usage (coordinates in WGS84 decimal degrees): <lat0> <lon0> <lat1> <lon1> [/analyze] [/charts]");
				return 1;
			}

			const double PartLat = 0.02;
			const double PartLon = PartLat * 2;

			// Make the area align on our grid
			north = north + (PartLat - north % PartLat);
			south = south - south % PartLat;
			west = west - west % PartLon;
			east = east + (PartLon - east % PartLon);
			
			var pathMyDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			var pathOrtoCharter = Path.Combine(pathMyDocuments, "OrtoCharter");
			Directory.CreateDirectory(pathOrtoCharter);

			var downloader = new OrtoDownloader();
			string downloadPath = Path.Combine(pathOrtoCharter, "Download");
			Directory.CreateDirectory(downloadPath);

			// Get bounding SWEREF 99 box
			var northWest = new WGS84Position(north, west);
			var southEast = new WGS84Position(south, east);
			SweRefRegion sweRegion = OrtoDownloader.GetBoundingRegion(northWest, southEast);

			downloader.Download(sweRegion, downloadPath);

			// Analyze for rocks and other features?
			if (args.Contains("/analyze"))
			{
				var analyzer = new OrtoAnalyzer.Analyzer(downloadPath);
				const double CombineDistanceMeters = 20.0;
				var pointsFound = analyzer.Analyze();
				var pointsToUse = analyzer.CombinePoints(pointsFound, CombineDistanceMeters);

				var gpxWriter = new GpxWriter();
				gpxWriter.Write(Path.Combine(pathOrtoCharter, "ortooutput.gpx"), pointsToUse.Select(x => new Waypoint { Latitude = x.Coordinate.Latitude, Longitude = x.Coordinate.Longitude, Symbol = "Hazard-Rock-Awash" }));
			}

			// Make Chart files?
			if (args.Contains("/charts"))
			{
				for (int i = 0; i < 2; i++)
				{
					double PixelsPerMeter = i == 0 ? 0.5 : 3;
					FilterMode Filter = i == 0 ? FilterMode.Natural : FilterMode.Subsurface;

					string subFolderName = FormattableString.Invariant($"{PixelsPerMeter}_{Filter}");

					Console.Out.WriteLine("Creating Chart Group: " + subFolderName);

					var outputPath = Path.Combine(pathOrtoCharter, "Charts");
					using (var charter = new Charter(downloadPath, outputPath))
					{
						// Build parts
						for (double partNorth = north; partNorth > south; partNorth = Math.Round(partNorth - PartLat, 5))
						{
							for (double partWest = west; partWest < east; partWest = Math.Round(partWest + PartLon, 5))
							{
								double partSouth = partNorth - PartLat;
								double partEast = partWest + PartLon;

								var partNorthWest = new WGS84Position(partNorth, partWest);
								var partSouthEast = new WGS84Position(partSouth, partEast);

								charter.Create(partNorthWest, partSouthEast, subFolderName, PixelsPerMeter, Filter);
							}
						}
					}
				}
			}

			return 0;
		}

		
	}
}
