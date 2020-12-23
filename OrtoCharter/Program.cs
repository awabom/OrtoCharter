using GpxLibrary;
using System;
using System.IO;
using System.Linq;

namespace OrtoCharter
{
    class Program
    {
		static void Main(string[] args)
		{
			var pathMyDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			var pathOrtoCharter = Path.Combine(pathMyDocuments, "OrtoCharter");
			Directory.CreateDirectory(pathOrtoCharter);

			var downloader = new OrtoAnalyzer.OrtoDownloader();
			string downloadPath = Path.Combine(pathOrtoCharter, "Download");
			Directory.CreateDirectory(downloadPath);
			downloader.Download(6722731, 648365, 6719691, 643645, downloadPath);

			var analyzer = new OrtoAnalyzer.Analyzer(downloadPath);
			const double CombineDistanceMeters = 20.0;
			var pointsFound = analyzer.Analyze();
			var pointsToUse = analyzer.CombinePoints(pointsFound, CombineDistanceMeters);

			var gpxWriter = new GpxWriter();
			gpxWriter.Write(Path.Combine(pathOrtoCharter, "ortooutput.gpx"), pointsToUse.Select(x => new Waypoint { Latitude = x.Coordinate.Latitude, Longitude = x.Coordinate.Longitude, Symbol = "Hazard-Rock-Awash" }));
		}
    }
}
