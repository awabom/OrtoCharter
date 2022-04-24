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

	class Job
	{
		public string Name { get; set; }
		public decimal Lat0 { get; set; }
		public decimal Lon0 { get; set; }
		public decimal Lat1 { get; set; }
		public decimal Lon1 { get; set; }

		public bool Analyze { get; set; }

		public decimal PartLat { get; set; } = 0.02m;
		public decimal PartLon { get; set; } = 0.04m;

		public MakeCharts MakeCharts { get; set; }
	}

	class MakeCharts
	{
		public decimal PixelsPerMeter { get; set; } = 1;
		public FilterMode Filter { get; set; } = FilterMode.Natural;
		public PixelMode PixelMode { get; set; } = PixelMode.Mean;
	}

	class Program
	{
		static CultureInfo IC = CultureInfo.InvariantCulture;

		static int Main(string[] args)
		{
			List<Job> jobs = new List<Job>();
			
			jobs.Add(new Job
			{
				Name = "East",
				Lat0 = 60.65225553880787m,
				Lon0 = 17.576905151239853m,
				Lat1 = 60.510789930297705m,
				Lon1 = 17.793451993439962m,
				MakeCharts = new MakeCharts
				{
					Filter = FilterMode.Subsurface2,
					PixelsPerMeter = 2,
					PixelMode = PixelMode.Mean
				}
			});

			var east2 = new Job
			{
				Name = "East",
				Lat0 = 60.65225553880787m,
				Lon0 = 17.576905151239853m,
				Lat1 = 60.510789930297705m,
				Lon1 = 17.793451993439962m,
				MakeCharts = new MakeCharts
				{
					Filter = FilterMode.Natural,
					PixelsPerMeter = 0.5m
				}
			};
			east2.PartLat *= 4;
			east2.PartLon *= 4;

			jobs.Add(east2);

			var berga = new Job	{
				Name = "Aakersberga",
				Lat0 = 59.6m,
				Lon0 = 18.2m,
				Lat1 = 59.3m,
				Lon1 = 18.4m,
				MakeCharts = new MakeCharts
				{
					Filter = FilterMode.Natural,
					PixelsPerMeter = 1
				}
			};
			berga.PartLat *= 2;
			berga.PartLon *= 2;

			jobs.Add(berga);

			// Run all jobs
			foreach (var job in jobs)
			{
				RunJob(job);
			}

			return 0;
		}

		static void RunJob(Job job)
		{
			// TODO: Refactor stuff...

			// Get coordinates
			decimal north = Math.Max(job.Lat0, job.Lat1);
			decimal east = Math.Max(job.Lon0, job.Lon1);
			decimal south = Math.Min(job.Lat0, job.Lat1);
			decimal west = Math.Min(job.Lon0, job.Lon1);

			decimal PartLat = job.PartLat;
			decimal PartLon = job.PartLon;

			// Make the area align on our grid
			north = OrtoDownloader.NearestUp(north, PartLat);
			south = OrtoDownloader.NearestDown(south, PartLat);
			west = OrtoDownloader.NearestDown(west, PartLon);
			east = OrtoDownloader.NearestUp(east, PartLon);

			/*
			var pathMyDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			var pathOrtoCharter = Path.Combine(pathMyDocuments, "OrtoCharter");
			*/
			var pathOrtoCharter = @"P:\OrtoCharter";
			var downloader = new OrtoDownloader();
			string downloadPath = Path.Combine(pathOrtoCharter, "Download");
			Directory.CreateDirectory(downloadPath);

			// Get bounding SWEREF 99 box
			var northWest = new WGS84Position((double)north, (double)west);
			var southEast = new WGS84Position((double)south, (double)east);
			SweRefRegion sweRegion = OrtoDownloader.GetBoundingRegion(northWest, southEast);

			downloader.Download(sweRegion, downloadPath);

			// Analyze for rocks and other features?
			if (job.Analyze)
			{
				var analyzer = new OrtoAnalyzer.Analyzer(downloadPath);
				const double CombineDistanceMeters = 20.0;
				var pointsFound = analyzer.Analyze();
				var pointsToUse = analyzer.CombinePoints(pointsFound, CombineDistanceMeters);

				var gpxWriter = new GpxWriter();
				gpxWriter.Write(Path.Combine(pathOrtoCharter, "ortooutput.gpx"), pointsToUse.Select(x => new Waypoint { Latitude = x.Coordinate.Latitude, Longitude = x.Coordinate.Longitude, Symbol = "Hazard-Rock-Awash" }));
			}

			// Make Chart files?
			if (job.MakeCharts is MakeCharts makeCharts)
			{
				var PixelsPerMeter = makeCharts.PixelsPerMeter;
				var Filter = makeCharts.Filter;
				var PixelMode = makeCharts.PixelMode;

				string groupName = FormattableString.Invariant($"{job.Name}_{PixelsPerMeter}_{PixelMode}_{Filter}");

				Console.Out.WriteLine("Creating Chart Group: " + groupName);

				var outputPath = Path.Combine(pathOrtoCharter, "Charts");
				using (var charter = new Charter(downloadPath, outputPath))
				{
					// Build parts
					for (var partNorth = north; partNorth > south; partNorth -= PartLat)
					{
						for (var partWest = west; partWest < east; partWest += PartLon)
						{
							var partSouth = partNorth - PartLat;
							var partEast = partWest + PartLon;

							charter.Create(partNorth, partWest, partSouth, partEast, groupName, PixelsPerMeter, Filter, PixelMode);
						}
					}
				}
			}

		}

		
	}
}
