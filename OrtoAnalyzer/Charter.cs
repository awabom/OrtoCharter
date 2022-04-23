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

		Bitmap _image;
		public Bitmap Image
		{
			get
			{
				LoadImage();
				return _image;
			}
		}

		private int? _imageWidth;
		public int ImageWidth
		{
			get
			{
				if (_imageWidth == null)
					LoadImage();
				return _imageWidth.Value;
			}
		}
		private int? _imageHeight;
		public int ImageHeight
		{
			get
			{
				if (_imageHeight == null)
					LoadImage();
				return _imageHeight.Value;
			}
		}

		private void LoadImage()
		{
			if (_image == null)
			{
				//Console.Out.WriteLine("Loading image: " + FileName);
				_image = new Bitmap(System.Drawing.Image.FromFile(FileName));
				_imageHeight = _image.Height;
				_imageWidth = _image.Width;
			}
		}

		public void FreeImage()
		{
			_image?.Dispose();
			_image = null;
		}

		public string FileName { get; private set; }

		public void Dispose()
		{
			FreeImage();
		}

		public ImageItem(SweRefRegion region, string fileName)
		{
			Region = region;
			FileName = fileName;
		}
	}
	class ImageCache : IDisposable
	{
		List<ImageItem> Images { get; set; } = new List<ImageItem>();

		public void AddImage(ImageItem image)
		{
			Images.Add(image);
		}

		List<ImageItem> loadedImages = new List<ImageItem>();

		private static bool RegionContainsPosition(SweRefRegion region, SWEREF99Position pos99)
		{
			return region.North >= pos99.Latitude
				&& region.South < pos99.Latitude
				&& region.West <= pos99.Longitude
				&& region.East > pos99.Longitude;
		}

		ImageItem match = null;
		public Color? GetPixel(WGS84Position position)
		{
			SWEREF99Position pos99 = new SWEREF99Position(position, SWEREF99Position.SWEREFProjection.sweref_99_tm);

			if (match == null || !RegionContainsPosition(match.Region, pos99))
			{
				match = Images.FirstOrDefault(checkImage => RegionContainsPosition(checkImage.Region, pos99));
			}

			if (match == null)
			{
				return null;
			}

			// Handle cache size (remove images if too large)
			if (loadedImages.LastOrDefault() != match)
			{
				if (loadedImages.Contains(match))
				{
					loadedImages.Remove(match);
				}
				loadedImages.Add(match);

				while (loadedImages.Count > 25)
				{
					//Console.Out.WriteLine("Removing from cache: " + loadedImages[0].FileName);
					loadedImages[0].FreeImage();
					loadedImages.RemoveAt(0);
				}
			}

			// Get the pixel for this position from the source image
			var dLon = pos99.Longitude - match.Region.West;
			var dLat = match.Region.North - pos99.Latitude;
			var regionHeight = match.Region.North - match.Region.South;
			var regionWidth = match.Region.East - match.Region.West;

			var x = (int)(dLon / regionWidth * match.ImageWidth);
			var y = (int)(dLat / regionHeight * match.ImageHeight);

			// Check that the coordinate is valid
			Debug.Assert(!(x < 0 || x >= match.ImageWidth || y < 0 || y >= match.ImageHeight));

			var sourcePixel = match.Image.GetPixel(x, y);
			if (sourcePixel.A == 0)
			{
				throw new Exception("File possibly damaged: " + match.FileName);
			}

			return sourcePixel;
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
				_imageCache.AddImage(new ImageItem(region, file));
			}
		}

		const decimal MetersPerLatitudeDegree = 111330;

		public enum FilterMode
		{
			Natural,
			Subsurface
		}

		public void Create(decimal lat0, decimal lon0, decimal lat1, decimal lon1, string subFolderName, decimal pixelsPerMeter, FilterMode filterMode)
		{
			string subFolderPath = Path.Combine(outputPath, subFolderName);
			Directory.CreateDirectory(subFolderPath);
			string name = FormattableString.Invariant($"{lat0:0.##########}_{lon0:0.##########}_{lat1:0.##########}_{lon1:0.##########}.kap");
			string fileNameKap = Path.Combine(subFolderPath, name);

			if (File.Exists(fileNameKap))
				return;

			Load();

			string tempImage = fileNameKap + ".png";

			// Build chart image in Mercator projection

			decimal dLat = lat0 - lat1;
			decimal dLon = lon1 - lon0;
			
			decimal latLength = dLat * MetersPerLatitudeDegree;
			decimal lonFactor = (decimal)Math.Cos((double)lat1 * Math.PI / 180);
			decimal lonLength = (dLon * lonFactor) * MetersPerLatitudeDegree;
			
			int width = (int)(lonLength * pixelsPerMeter);
			int height = (int)(latLength * pixelsPerMeter);

			// To narrow area for making a chart?
			if (width == 0 || height == 0)
				return;

			Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
			var bitmapBits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

			var stride = bitmapBits.Stride;
			if (stride < 0)
				throw new NotImplementedException("Negative Stride not implemented, stride = " + stride);

			var bytesPerRow = stride;
			var bytes = new byte[height * bytesPerRow];

			Console.Out.WriteLine("Building chart: " + fileNameKap);

			var dLatHeight = dLat / height;
			var dLonWidth = dLon / width;

			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					var lat = lat0 - dLatHeight * y;
					var lon = lon0 + dLonWidth * x;
					WGS84Position position = new WGS84Position((double)lat, (double)lon);
					var color = _imageCache.GetPixel(position);
					if (color != null)
					{
						var value = color.Value;

						byte red = value.R;
						byte green = value.G;
						byte blue = value.B;

						const int HighLevel = 80;
						const int HighLevelSmudge = 5;

						// Always create fewer colors in the light pixels (had some palette issues with imgkap before this)
						if (red > HighLevel && green > HighLevel && blue > HighLevel)
						{
							red -= (byte)(red % HighLevelSmudge);
							green -= (byte)(green % HighLevelSmudge);
							blue -= (byte)(blue % HighLevelSmudge);
						}

						// Subsurface: Raise red, green - lower blue
						if (filterMode == FilterMode.Subsurface)
						{
							red = ToByte(red * 2.5);
							green = ToByte(green * 1.5);
							blue = ToByte(blue * 0.5);
						}

						int pixelStart = y * stride + 3 * x;
						bytes[pixelStart + 2] = red;
						bytes[pixelStart + 1] = green;
						bytes[pixelStart] = blue;
					}
				}
			}

			System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmapBits.Scan0, bytes.Length);
			bitmap.UnlockBits(bitmapBits);

			bitmap.Save(tempImage);

			// Convert mercator image to chart
			Convert(tempImage, fileNameKap, lat0, lon0, lat1, lon1);

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

		private void Convert(string file, string outputKapFileName, decimal lat0, decimal lon0, decimal lat1, decimal lon1)
		{
			const string PathImgKap = @"C:\Utility\imgkap\imgkap.exe";

			var projection = "MERCATOR";

			var startInfo = new ProcessStartInfo()
			{
				FileName = PathImgKap,
				Arguments = FormattableString.Invariant($"-j \"{projection}\" \"{file}\" {lat0} {lon0} {lat1} {lon1} \"{outputKapFileName}\""),
				UseShellExecute = false
			};
			Console.Out.WriteLine("Running: imgkap " + startInfo.Arguments);
			var process = Process.Start(startInfo);
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				File.Delete(outputKapFileName);
			}
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