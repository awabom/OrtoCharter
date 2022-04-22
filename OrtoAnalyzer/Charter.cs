using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using MightyLittleGeodesy.Positions;
using Shorthand.Geodesy;

namespace OrtoAnalyzer
{
	class ImageItem : IDisposable
	{
		public SweRefRegion Region { get; }
		public Bitmap Image { get; private set; }

		public void Dispose()
		{
			Image?.Dispose();
			Image = null;
		}

		public ImageItem(SweRefRegion region, Image image)
		{
			Region = region;
			Image = new Bitmap(image);
		}
	}
	class ImageCache : IDisposable
	{
		List<ImageItem> Images { get; set; } = new List<ImageItem>();

		public void AddImage(ImageItem image)
		{
			Images.Add(image);
		}

		public Color? GetPixel(WGS84Position position)
		{
			SWEREF99Position pos99 = new SWEREF99Position(position, SWEREF99Position.SWEREFProjection.sweref_99_tm);

			var match = Images.FirstOrDefault(x => x.Region.North > pos99.Latitude 
				&& x.Region.South < pos99.Latitude 
				&& x.Region.West < pos99.Longitude 
				&& x.Region.East > pos99.Longitude);
			if (match != null)
			{
				var dLon = pos99.Longitude - match.Region.West;
				var dLat = match.Region.North - pos99.Latitude;
				var regionHeight = match.Region.North - match.Region.South;
				var regionWidth = match.Region.East - match.Region.West;

				var x = (int)(dLon / regionWidth * match.Image.Width);
				var y = (int)(dLat / regionHeight * match.Image.Height);

				var sourcePixel = match.Image.GetPixel(x, y);
				return sourcePixel;
			}

			return null;
		}

		public void Dispose()
		{
			Images.ForEach(x => x.Dispose());
			Images.Clear();
		}
	}

	public class Charter : IDisposable
	{
		private string ortoFolderPath;
		private string outputPath;
		private ImageCache _imageCache;

		public Charter(string ortoFolderPath, string outputPath)
		{
			this.ortoFolderPath = ortoFolderPath;
			this.outputPath = outputPath;
		}

		private void Load()
		{
			if (_imageCache != null)
				return;

			_imageCache = new ImageCache();

			// Build source map
			foreach (var file in OrtoDownloader.GetUntouchedFiles(ortoFolderPath))
			{
				SweRefRegion region = OrtoDownloader.ParseFileNameToSWEREF(file);
				var image = Image.FromFile(file);
				_imageCache.AddImage(new ImageItem(region, image));
			}
		}

		const double MetersPerLatitudeDegree = 111330;

		public void Create(WGS84Position northWest, WGS84Position southEast)
		{
			Load();

			string fileNameKap = Path.Combine(outputPath, northWest.LatitudeToString(WGS84Position.WGS84Format.Degrees) +
				northWest.LongitudeToString(WGS84Position.WGS84Format.Degrees) +
				southEast.LatitudeToString(WGS84Position.WGS84Format.Degrees) +
				southEast.LongitudeToString(WGS84Position.WGS84Format.Degrees) + ".kap");
			string tempImage = fileNameKap + ".png";

			// Build chart image in Mercator projection
			double lat0 = northWest.Latitude;
			double lon0 = northWest.Longitude;
			double lat1 = southEast.Latitude;
			double lon1 = southEast.Longitude;

			double dLat = lat0 - lat1;
			double dLon = lon1 - lon0;
			
			double latLength = dLat * MetersPerLatitudeDegree;
			double lonFactor = Math.Cos(lat1 * Math.PI / 180);
			double lonLength = (dLon * lonFactor) * MetersPerLatitudeDegree;
			
			int width = (int)(lonLength * 3);
			int height = (int)(latLength * 3);

			Bitmap bitmap = new Bitmap(width, height);

			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					double lat = lat0 - dLat * ((double)y / height);
					double lon = lon0 + dLon * ((double)x / width);
					WGS84Position position = new WGS84Position(lat, lon);
					var color = _imageCache.GetPixel(position);
					if (color != null)
					{
						var value = color.Value;
						var red = ToByte(value.R * 2.5);
						var green = ToByte(value.G * 1.5);
						var blue = ToByte(value.B * 0.5);
						var result = Color.FromArgb(red, green, blue);
												
						bitmap.SetPixel(x, y, result);
					}
				}
			}

			bitmap.Save(tempImage);

			// Convert mercator image to chart
			Convert(tempImage, fileNameKap, northWest, southEast);

			// Delete mercator image
			// TODO ENABLE File.Delete(tempImage);
		}

		private byte ToByte(double d)
		{
			var round = Math.Round(d);

			if (round < 0)
				return 0;
			else if (round > byte.MaxValue)
				return byte.MaxValue;

			return (byte)round;
		}

		public void Dispose()
		{
			_imageCache?.Dispose();
			_imageCache = null;
		}

		private void Convert(string file, string outputKapFileName, WGS84Position northWest, WGS84Position southEast)
		{
			const string PathImgKap = @"C:\Utility\imgkap\imgkap.exe";

			var coord0 = northWest;
			var coord1 = southEast;

			var lat0 = coord0.Latitude;
			var lon0 = coord0.Longitude;
			var lat1 = coord1.Latitude;
			var lon1 = coord1.Longitude;

			var latCenter = (lat0 + lat1) / 2;
			var lonFactor = Math.Cos(latCenter * (Math.PI / 180.0));
			var imageWidthFactor = lonFactor / (Math.Abs(lat0 - lat1) / Math.Abs(lon1 - lon0));

			var projection = "MERCATOR";

			var startInfo = new ProcessStartInfo()
			{
				FileName = PathImgKap,
				Arguments = FormattableString.Invariant($"-j \"{projection}\" \"{file}\" {lat0} {lon0} {lat1} {lon1} \"{outputKapFileName}\""),
				UseShellExecute = false
			};
			Console.Out.WriteLine("Running: imgkap " + startInfo.Arguments);
			Process.Start(startInfo).WaitForExit();
		}

		/*

		public void Resize(string imageFile, string outputFile, double widthFactor, double heightFactor)
		{
			using (var srcImage = Image.FromFile(imageFile))
			{
				var newWidth = (int)Math.Round(srcImage.Width * widthFactor);
				var newHeight = (int)Math.Round(srcImage.Height * heightFactor);
				using (var outImage = new Bitmap(newWidth, newHeight))
				using (var graphics = Graphics.FromImage(outImage))
				{
					graphics.SmoothingMode = SmoothingMode.HighQuality;
					graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
					graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
					graphics.DrawImage(srcImage, new Rectangle(0, 0, newWidth, newHeight));
					outImage.Save(outputFile);
				}
			}
		}
		*/
	}
}