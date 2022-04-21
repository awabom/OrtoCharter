using GpxLibrary;
using System;
using System.IO;
using System.Linq;

namespace OrtoCharter
{
    class Program
    {
		static int Main(string[] args)
		{
			// Parse command line for coordinates
			int north, east, south, west;
			try
			{
				north = int.Parse(args[0]);
				east = int.Parse(args[1]);
				south = int.Parse(args[2]);
				west = int.Parse(args[3]);
			}
			catch
			{
				Console.Error.WriteLine("Usage (coordinates in SWEREF 99 TM): <north> <east> <south> <west> [/analyze] [/charts]");
				return 1;
			}


			var pathMyDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			var pathOrtoCharter = Path.Combine(pathMyDocuments, "OrtoCharter");
			Directory.CreateDirectory(pathOrtoCharter);

			var downloader = new OrtoAnalyzer.OrtoDownloader();
			string downloadPath = Path.Combine(pathOrtoCharter, "Download");
			Directory.CreateDirectory(downloadPath);
			downloader.Download(north, east, south, west, downloadPath);

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
				// TODO
			}

			return 0;
		}
    }
}
