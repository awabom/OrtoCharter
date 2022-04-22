using GpxLibrary;
using MightyLittleGeodesy.Positions;
using OrtoAnalyzer;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

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
				north = double.Parse(args[0], IC);
				west = double.Parse(args[1], IC);
				south = double.Parse(args[2], IC);
				east = double.Parse(args[3], IC);
			}
			catch
			{
				Console.Error.WriteLine("Usage (coordinates in WGS84 decimal degrees): <north> <west> <south> <east> [/analyze] [/charts]");
				return 1;
			}


			var pathMyDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			var pathOrtoCharter = Path.Combine(pathMyDocuments, "OrtoCharter");
			Directory.CreateDirectory(pathOrtoCharter);

			var downloader = new OrtoAnalyzer.OrtoDownloader();
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
				const double PartDegree = 0.01;
				var outputPath = Path.Combine(pathOrtoCharter, "Charts");
				Directory.CreateDirectory(outputPath);
				var charter = new Charter(downloadPath, outputPath);
				charter.Create(northWest, southEast);

				/*
				for (double partNorth = northWest.Latitude; partNorth > southEast.Latitude; partNorth -= PartDegree)
				{
					for (double partWest = northWest.Longitude; partWest < southEast.Longitude; partWest += PartDegree)
					{
						double partSouth = Math.Max(southEast.Latitude, partNorth - PartDegree);
						double partEast = Math.Min(southEast.Longitude, partWest + PartDegree);

						var partNorthWest = new WGS84Position(partNorth, partWest);
						var partSouthEast = new WGS84Position(partSouth, partEast);

						charter.Create(partNorthWest, partSouthEast);
					}
				}
				*/
			}

			return 0;
		}

		
	}
}
