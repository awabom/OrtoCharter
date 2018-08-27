using GpxLibrary;
using System;
using System.Linq;

namespace OrtoCharter
{
    class Program
    {
		static void Main(string[] args)
		{
			var downloader = new OrtoAnalyzer.OrtoDownloader();
			string ortoFolderPath = @"C:\Users\oak\Documents\ortotest";
			downloader.Download(6722731, 648365, 6719691, 643645, ortoFolderPath);

			var analyzer = new OrtoAnalyzer.Analyzer(ortoFolderPath);
			const double CombineDistanceMeters = 10.0;
			var pointsFound = analyzer.Analyze();
			var pointsToUse = analyzer.CombinePoints(pointsFound, CombineDistanceMeters);

			var gpxWriter = new GpxWriter();
			gpxWriter.Write(@"C:\Users\oak\Documents\ortotest\test.gpx", pointsToUse.Select(x => new Waypoint { Latitude = x.Coordinate.Latitude, Longitude = x.Coordinate.Longitude, Symbol = "Hazard-Rock-Awash" }));
		}
    }
}
