using MightyLittleGeodesy.Positions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OrtoAnalyzer
{
	class MemoryImage
    {
		readonly byte[] _bytes;
		readonly int _rowSize;
		const int BytesPerPixel = 4;

		const int A = 3;
		const int R = 2;
		const int G = 1;
		const int B = 0;

		public MemoryImage(byte[] bytes, int rowSize, int width, int height)
        {
			_bytes = bytes;
			_rowSize = rowSize;
			Width = width;
			Height = height;
        }

		public int Width { get; }
		public int Height { get; }

		public Color GetPixel(int x, int y)
        {
			int offset = y * _rowSize + x * BytesPerPixel;
			return Color.FromArgb(_bytes[offset + A], _bytes[offset + R], _bytes[offset + G], _bytes[offset + B]);
        }
    }

    class ImageItem : IDisposable
	{
		const double WidthHeight = 4000;

		public SweRefRegion Region { get; }
		public string FileName { get; }

		public double LonFactor { get; }
		public double LatFactor { get; }

		public ImageItem(SweRefRegion region, string fileName)
		{
			Region = region;
			FileName = fileName;

			LonFactor = WidthHeight / (Region.East - Region.West);
			LatFactor = WidthHeight / (Region.North - Region.South);

			ResetImage();
		}

		Lazy<MemoryImage> _image;

		private void ResetImage()
        {
			_image = new Lazy<MemoryImage>(() => LoadImage(FileName));
        }

		public MemoryImage Image
		{
			get
			{
				return _image.Value;
			}
		}



		private static MemoryImage LoadImage(string fileName)
		{
			Console.Out.WriteLine("Loading image: " + fileName);
			using (var bitmap = new Bitmap(System.Drawing.Image.FromFile(fileName)))
			{
				var imageHeight = bitmap.Height;
				var imageWidth = bitmap.Width;

				if (imageHeight != WidthHeight || imageWidth != WidthHeight)
                {
					throw new Exception("Image is not square with size: " + WidthHeight);
                }

				var bitmapData = bitmap.LockBits(new Rectangle(0, 0, imageWidth, imageHeight), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

				int rowSize = Math.Abs(bitmapData.Stride);
				var data = new byte[imageHeight * rowSize];
				var ptr = bitmapData.Scan0;
				var rowIndex = 0;

				for (int y = 0; y < imageHeight; y++)
				{
					Marshal.Copy(ptr, data, rowIndex, rowSize);
					ptr = IntPtr.Add(ptr, bitmapData.Stride);
					rowIndex += rowSize;
				}
				bitmap.UnlockBits(bitmapData);

				return new MemoryImage(data, rowSize, imageWidth, imageHeight);
			}
		}

		public void FreeImage()
		{
			if (_image.IsValueCreated)
			{
				Console.Out.WriteLine("Freeing image: " + FileName);
			}
			ResetImage();
		}

		public void Dispose()
		{
			// Nothing to do here anymore - keeping in case we need it again
		}
	}
	class ImageCache : IDisposable
	{
		List<ImageItem> _images = new List<ImageItem>();

		public void AddImage(ImageItem image)
		{
			_images.Add(image);
		}
		public void FreeAll()
        {
			foreach (var image in _images)
            {
				image.FreeImage();
            }
        }

		private static bool ContainsPosition(SweRefRegion region, SWEREF99Position pos99)
        {
			return region.North >= pos99.Latitude &&
				region.South < pos99.Latitude &&
				region.West <= pos99.Longitude &&
				region.East > pos99.Longitude;
        }

		internal Color? GetPixel(WGS84Position position)
		{
			SWEREF99Position pos99 = new SWEREF99Position(position, SWEREF99Position.SWEREFProjection.sweref_99_tm);
			return GetPixel(pos99);
		}

		internal IEnumerable<Color> GetPixelsInArea(WGS84Position pos1, WGS84Position pos2)
		{
			const double StepPerPixel = 0.25;
			SWEREF99Position pos99_1 = new SWEREF99Position(pos1, SWEREF99Position.SWEREFProjection.sweref_99_tm);
			SWEREF99Position pos99_2 = new SWEREF99Position(pos2, SWEREF99Position.SWEREFProjection.sweref_99_tm);

			var lowLat = pos99_2.Latitude;
			var highLat = pos99_1.Latitude;
			var lowLon = pos99_1.Longitude;
			var highLon = pos99_2.Longitude;

			SWEREF99Position pos99 = new SWEREF99Position(lowLat, lowLon);

			for (; pos99.Latitude <= highLat; pos99.Latitude += StepPerPixel)
			{ 
				for (pos99.Longitude = lowLon; pos99.Longitude <= highLon; pos99.Longitude += StepPerPixel)
				{
					var pixel = GetPixel(pos99);
					if (pixel != null)
						yield return pixel.Value;
				}
			}
		}

		ThreadLocal<ImageItem> localMatch = new ThreadLocal<ImageItem>();
		private Color? GetPixel(SWEREF99Position pos99)
		{ 
			// Get the correct block image for this position
			var match = localMatch.Value;
			if (match == null || !ContainsPosition(match.Region, pos99))
			{
				match = _images.FirstOrDefault(x => ContainsPosition(x.Region, pos99));
				if (match == null)
				{
					throw new Exception("Source image not found");
				}
				localMatch.Value = match;
			}

			// Get the pixel for this position from the source image
			var dLon = pos99.Longitude - match.Region.West;
			var dLat = match.Region.North - pos99.Latitude;

			var x = (int)(dLon * match.LonFactor);
			var y = (int)(dLat * match.LatFactor);

			// Check that the coordinate is valid
			Debug.Assert(!(x < 0 || x >= match.Image.Width || y < 0 || y >= match.Image.Height));

			var sourcePixel = match.Image.GetPixel(x, y);
			if (sourcePixel.A == 0)
			{
				throw new Exception("File possibly damaged: " + match.FileName);
			}

			return sourcePixel;
		}

		public void Dispose()
		{
			foreach (var image in _images)
			{
				image.Dispose();
			}
			_images.Clear();
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
			Subsurface,
			Subsurface2
		}

		public enum PixelMode
        {
			Nearest,
			Lightest,
			Mean
        }

		public class ImageRegion
		{
			public int x1;
			public int x2excl;
			public int y1;
			public int y2excl;

			public override string ToString()
			{
				return FormattableString.Invariant($"({x1}, {y1}) - ({x2excl}, {y2excl})");
			}

			public static IEnumerable<ImageRegion> GetRegions(int totalWidth, int totalHeight, int partWidth, int partHeight)
			{
				for (int y = 0; y < totalHeight; y += partHeight)
				{
					for (int x = 0; x < totalWidth; x += partWidth)
					{
						int x2excl = x + partWidth;
						if (x2excl > totalWidth) x2excl = totalWidth;
						int y2excl = y + partHeight;
						if (y2excl > totalHeight) y2excl = totalHeight;

						yield return new ImageRegion { x1 = x, y1 = y, x2excl = x2excl, y2excl = y2excl };
					}
				}
			}
		}

		public void Create(decimal lat0, decimal lon0, decimal lat1, decimal lon1, string groupName, decimal pixelsPerMeter, FilterMode filterMode, PixelMode pixelMode)
		{
			string subFolderPath = Path.Combine(outputPath, groupName);
			Directory.CreateDirectory(subFolderPath);
			string name = FormattableString.Invariant($"{groupName}_{lat0:0.##########}_{lon0:0.##########}_{lat1:0.##########}_{lon1:0.##########}.kap");
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

			var latPerPixel = dLat / height;
			var lonPerPixel = dLon / width;
			var latPerHalfPixel = latPerPixel / 2;
			var lonPerHalfPixel = lonPerPixel / 2;

			var partWidth = (int)Math.Round(1.2m * width * (pixelsPerMeter / 4));
			var partHeight = (int)Math.Round(1.2m * height * (pixelsPerMeter / 4));

			var imageRegions = ImageRegion.GetRegions(width, height, partWidth, partHeight);

			foreach (var imageRegion in imageRegions)
			{
				int x2excl = imageRegion.x2excl;
				int y2excl = imageRegion.y2excl;

                Parallel.For(imageRegion.y1, y2excl, y =>
				//for (int y = imageRegion.y1; y < y2excl; y++)
				{
					var lat = lat0 - latPerPixel * y;

					for (int x = imageRegion.x1; x < x2excl; x++)
					{
						var lon = lon0 + lonPerPixel * x;

						Color? color = GetPixel(lat, lon, pixelMode, latPerHalfPixel, lonPerHalfPixel);
						if (color != null)
                        {
                            var value = color.Value;

                            byte red = value.R;
                            byte green = value.G;
                            byte blue = value.B;

                            int highLevel;
                            int highLevelSmudge;
					
							// Subsurface: Raise red, green - lower blue
							if (filterMode == FilterMode.Subsurface || filterMode == FilterMode.Subsurface2)
                            {
								highLevel = 80;
								highLevelSmudge = 8;

								red = ToByte(red * 2.5);
                                green = ToByte(green * 1.5);
                                blue = ToByte(blue * 0.5);

                                if (filterMode == FilterMode.Subsurface2)
                                {
                                    if (red < 50) // Everything with red less than 50 is 'safe'
                                    {
                                        red = green = blue = 0;
                                    }
                                }
                            }
                            else
                            {
								highLevel = 0;
								highLevelSmudge = 3;
                            }

							LowerColors(ref red, ref green, ref blue, highLevel, highLevelSmudge);

							int pixelStart = y * stride + 3 * x;
                            bytes[pixelStart + 2] = red;
                            bytes[pixelStart + 1] = green;
                            bytes[pixelStart] = blue;
                        }
                    }
				}
				);

				// Free any images used when making this chart region/part
				_imageCache.FreeAll();
			}

			Marshal.Copy(bytes, 0, bitmapBits.Scan0, bytes.Length);
			bitmap.UnlockBits(bitmapBits);

			bitmap.Save(tempImage);

			// Convert mercator image to chart
			Convert(tempImage, fileNameKap, lat0, lon0, lat1, lon1);

			// Delete mercator image
			// TODO ENABLE File.Delete(tempImage);
		}

        private static void LowerColors(ref byte red, ref byte green, ref byte blue, int HighLevel, int HighLevelSmudge)
        {
            // Always create fewer colors in the light pixels (had some palette issues with imgkap before this)
            if (red > HighLevel && green > HighLevel && blue > HighLevel)
            {
                red -= (byte)(red % HighLevelSmudge);
                green -= (byte)(green % HighLevelSmudge);
                blue -= (byte)(blue % HighLevelSmudge);
            }
        }

        private Color? GetPixel(decimal lat, decimal lon, PixelMode pixelMode, decimal latPerHalfPixel, decimal lonPerHalfPixel)
        {
			if (pixelMode == PixelMode.Nearest)
			{
				return GetPixelSingle(lat, lon);
			}
			if (pixelMode == PixelMode.Mean)
            {
				var pixels = GetPixelsInArea(lat + latPerHalfPixel, lon - lonPerHalfPixel, lat - latPerHalfPixel, lon + lonPerHalfPixel);
				return AverageRGB(pixels);
			}
			if (pixelMode == PixelMode.Lightest)
            {
				var pixels = GetPixelsInArea(lat + latPerHalfPixel, lon - lonPerHalfPixel, lat - latPerHalfPixel, lon + lonPerHalfPixel);
				Color? result = null;
				float lightestBrightness = 0;

				foreach (var checkColor in pixels)
				{ 
					float checkBrightness = checkColor.GetBrightness();

					if (result == null || checkBrightness > lightestBrightness)
                    {
						result = checkColor;
						lightestBrightness = checkBrightness;
                    }
	             }

				return result;

            }
			throw new NotImplementedException("PixelMode not implenented: " + pixelMode);
		}

        private static Color? AverageRGB(IEnumerable<Color> pixels)
        {
			int num = 0;
			int rs = 0, gs = 0, bs = 0;
			foreach (var color in pixels)
            {
				num++;

				rs += color.R * color.R;
				gs += color.G * color.G;
				bs += color.B * color.B;
			}

			return num == 0 ? null : Color.FromArgb((int)Math.Sqrt(rs/num), (int)Math.Sqrt(gs/num), (int)Math.Sqrt(bs/num));
        }

        private Color? GetPixelSingle(decimal lat, decimal lon)
        {
            WGS84Position position = new WGS84Position((double)lat, (double)lon);
            return _imageCache.GetPixel(position);
        }
		private IEnumerable<Color> GetPixelsInArea(decimal lat0, decimal lon0, decimal lat1, decimal lon1)
        {
			WGS84Position pos1 = new WGS84Position((double)lat0, (double)lon0);
			WGS84Position pos2 = new WGS84Position((double)lat1, (double)lon1);
			return _imageCache.GetPixelsInArea(pos1, pos2);
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