using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shorthand.Geodesy;
using System.Collections.Concurrent;

namespace OrtoAnalyzer
{
    public class Analyzer
    {
		string ortoFolderPath;
		public Analyzer(string _ortoFolderPath)
		{
			ortoFolderPath = _ortoFolderPath;
		}

		const string SuffixAnalyzed = "_analyzed";

		public IEnumerable<PointOfInterest> Analyze()
		{
			//var files = new[] { Path.Combine(ortoFolderPath, "643645,6719691,644669,6720683,test.png") };
			//var files = new[] { Path.Combine(ortoFolderPath, "645693,6721707,646717,6722731.png") };
			var files = Directory.GetFiles(ortoFolderPath, "*.png").Where(x => !x.Contains(SuffixAnalyzed));

			var pointsOfInterestBag = new ConcurrentBag<PointOfInterest>();

			Parallel.ForEach(files, (file) =>
			{
				string analyzedFileName = GetAnalyzedFileName(file);
				var result = AnalyzeSingle(file, analyzedFileName);
				foreach (var item in result)
				{
					pointsOfInterestBag.Add(item);
				}
			});

			return pointsOfInterestBag;
		}

		/// <summary>
		/// Minimize the number of points by combining nearby points
		/// </summary>
		/// <param name="pointsOfInterestBag"></param>
		/// <param name="combineDistanceMeters"></param>
		/// <returns></returns>
		public IEnumerable<PointOfInterest> CombinePoints(IEnumerable<PointOfInterest> pointsOfInterest, double combineDistanceMeters)
		{
			var combinedPoints = new List<PointOfInterest>();

			foreach (var pointGroup in pointsOfInterest.GroupBy(x => new Tuple<int, int>(Convert.ToInt32(x.SweRefEast / combineDistanceMeters), Convert.ToInt32(x.SweRefNorth / combineDistanceMeters))))
			{
				ItemType maxItemType = ItemType.DangerLow;

				int count = 0;
				double totalEast = 0;
				double totalNorth = 0;

				foreach (var point in pointGroup)
				{
					count++;
					totalEast += point.SweRefEast;
					totalNorth += point.SweRefNorth;

					if (point.ItemType > maxItemType)
						maxItemType = point.ItemType;
				}

				double midEast = totalEast / count;
				double midNorth = totalNorth / count;

				PointOfInterest midPoint = new PointOfInterest
				{
					ItemType = maxItemType
				};
				midPoint.SetSweRef99TM(midNorth, midEast);

				combinedPoints.Add(midPoint);
			}

			return combinedPoints;
		}

		private static string GetAnalyzedFileName(string sourceFilePath)
		{
			return Path.Combine(Path.GetDirectoryName(sourceFilePath), Path.GetFileNameWithoutExtension(sourceFilePath) + SuffixAnalyzed + ".png");
		}

		private IEnumerable<PointOfInterest> AnalyzeSingle(string imageFilePath, string outputFilePath)
		{
			const int RockSampleStep = 2; // 5x5
			const int SurroundSampleStep = 10; // 21x21
			const double BrightnessFactorLow = 1.05;
			const double BrightnessFactoryHigh = 1.08;
			const double LandBrightnessLevel = 0.38;
			const int MinDangerSize = 2; // 2 pixels minimum danger size
			const int MinRedLevelForDanger = 31; // if R < this number - no danger
			const float MaxHueForDanger = 195.0f; // If Hue more than this number - no danger
			const float HueDangerAreaLowMax = 166.0f;
			const float HueDangerAreaHighMax = 130.0f;

			Color ColorSafe = Color.FromArgb(255, 0, 220);

			var foundPoints = new List<PointOfInterest>();

			using (var image = new Bitmap(Image.FromFile(imageFilePath)))
			{
				Color ColorLandOrSeagull = Color.Black;
				Color ColorLand = Color.Tan;
				Color ColorDangerHigh = Color.Red;
				Color ColorDangerLow = Color.DarkRed;
				Color ColorDangerAreaHigh = Color.LightBlue;
				Color ColorDangerAreaLow = Color.Blue;
				Color ColorPOI = Color.Green;
				Color ColorTree = Color.DarkGreen;
				Color ColorSeagull = Color.Aquamarine;

				// Copy image into color array to avoid many calls to SetPixel() (it's slow)
				var imageArray = new Color[image.Width, image.Height];
				for (int x = 0; x < image.Width; x++)
					for (int y = 0; y < image.Height; y++)
						imageArray[x, y] = image.GetPixel(x, y);
				var width = image.Width;
				var height = image.Height;
				var analyzeArray = new Color[image.Width, image.Height];

				// The algorithm
				for (int x = 0; x < width; x++)
				{
					for (int y = 0; y < height; y++)
					{
						Color pixelColor = GetAveragePixel(imageArray, x, y, 0, width, height);
						Color analyzedColor = Color.Empty;

						// Marked manually as safe?
						if (pixelColor == ColorSafe)
						{

						}
						// Is this (probably) land?
						else if (pixelColor.GetBrightness() >= LandBrightnessLevel)
						{
							analyzedColor = ColorLandOrSeagull;
						}
						// Maybe water?
						else
						{
							Color sample = GetAveragePixel(imageArray, x, y, RockSampleStep, width, height);
							float sampleHue = sample.GetHue();

							if (sample.R > MinRedLevelForDanger && sampleHue < MaxHueForDanger)
							{
								Color sample2 = GetAveragePixel(imageArray, x, y, SurroundSampleStep, width, height);

								// Brightness High/Low triggers for hidden rocks in water
								float brightnessFactor = sample.GetBrightness() / sample2.GetBrightness();
								if (brightnessFactor > BrightnessFactoryHigh)
								{
									analyzedColor = ColorDangerHigh;
								}
								else if (brightnessFactor > BrightnessFactorLow)
								{
									analyzedColor = ColorDangerLow;
								}
							}

							if (analyzedColor == Color.Empty)
							{
								if (sampleHue < HueDangerAreaHighMax)
								{
									analyzedColor = ColorDangerAreaHigh;
								}
								else if (sampleHue < HueDangerAreaLowMax)
								{
									analyzedColor = ColorDangerAreaLow;
								}
							}
						}

						// Store the resulting color in the 'image' (color can be 'Empty')
						analyzeArray[x, y] = analyzedColor;
					}
				}


				// Seagull remover
				const int DangerousLandMaxSize = 9; // Land that is small must be marked (in case of high sea level)
				const int SeagullMaxSize = 6; // Seagull is land of max 3 pixels
				for (int x = 0; x < width; x++)
				{
					for (int y = 0; y < height; y++)
					{
						if (analyzeArray[x,y] == ColorLandOrSeagull)
						{
							// Fill land and seagulls with land color
							int landSize = Utils.FindSizeAndFill(analyzeArray, x, y, out int centerX, out int centerY, out var foundColors, out var outsideColors, (color) => color == ColorLandOrSeagull, ColorLand);
							// If land was small and surrounded by nothing, replace with 'nothing' (it's a seagull, probably)
							if (landSize <= SeagullMaxSize && outsideColors.All(color => color == Color.Empty)) // seagull
							{
								// Fill all previously found 'land', mark as seagull
								Utils.Fill(analyzeArray, x, y, (Color) => Color == ColorLand, ColorSeagull);
								// Also draw a rectangle to hide any non-land pixels (aliased pixels from the seagull, probably)
								//Utils.Rectangle(analyzeArray, centerX, centerY, SeagullStep, (Color) => Color != ColorSeagull, Color.Empty);
							}
							else if (landSize <= DangerousLandMaxSize)
							{
								// Fill all previously found 'land', mark as danger high
								Utils.Fill(analyzeArray, x, y, (Color) => Color == ColorLand, ColorDangerHigh);
							}
						}
					}
				}

				// Build coordinate output
				var coordArray = (Color[,])analyzeArray.Clone();
				try
				{
					string[] parts = Path.GetFileNameWithoutExtension(imageFilePath).Split(',');
					int upperLeftNorth = int.Parse(parts[3], CultureInfo.InvariantCulture);
					int upperLeftEast = int.Parse(parts[0], CultureInfo.InvariantCulture);

					for (int x = 0; x < width; x++)
					{
						for (int y = 0; y < height; y++)
						{
							if (coordArray[x,y] != Color.Empty)
							{
								// Found something, find the pixel-size (area) of it
								int size = Utils.FindSizeAndRemove(coordArray, x, y, out int centerX, out int centerY, out var foundColors, out var outsideColors, (color) => color == ColorDangerHigh || color == ColorDangerLow);

								// Is this danger 'large enough' ?
								if (size >= MinDangerSize)
								{
									// Add waypoint 
									var foundThing = new PointOfInterest();

									foundThing.CenterPixelX = centerX;
									foundThing.CenterPixelY = centerY;

									if (foundColors.Contains(ColorDangerHigh))
									{
										foundThing.ItemType = ItemType.DangerHigh;
									}
									else if (foundColors.Contains(ColorDangerLow))
									{
										foundThing.ItemType = ItemType.DangerLow;
									}
									else if (foundColors.Contains(ColorLand))
									{
										foundThing = null;
									}
									else
									{
										throw new Exception("Unknown found color");
									}
									foundThing.SetSweRef99TM(upperLeftNorth - (centerY / 4.0), upperLeftEast + (centerX / 4.0));

									foundPoints.Add(foundThing);
								}
							}
						}
					}
				}
				catch
				{
					// Just skip outputting coordinates for now
					// TODO: Crash
				}

				// Write the result file

				// Overlay 'analyzed' image on top of original image
				for (int x = 0; x < image.Width; x++)
				{
					for (int y = 0; y < image.Height; y++)
					{
						Color outputColor = analyzeArray[x, y];
						if (outputColor != Color.Empty)
						{
							image.SetPixel(x, y, outputColor);
						}
					}
				}
				// Set Waypoint pixels
				foreach (var stuff in foundPoints)
				{
					image.SetPixel(stuff.CenterPixelX, stuff.CenterPixelY, ColorPOI);
				}

				image.Save(outputFilePath, System.Drawing.Imaging.ImageFormat.Png);
			}

			return foundPoints;
		}



		/// <summary>
		/// Item type in ascending danger level (important for some code!)
		/// </summary>
		public enum ItemType
		{
			DangerLow,
			DangerHigh
		}

		public class PointOfInterest
		{
			public void SetSweRef99TM(double north, double east)
			{
				SweRefNorth = north;
				SweRefEast = east;
				GridCoordinate gridCoordinate = new GridCoordinate { Projection = Shorthand.Geodesy.Projections.SwedishProjections.SWEREF99TM, X = north, Y = east };
				Coordinate = GaussKruger.GridToGeodetic(gridCoordinate);
			}

			public double SweRefNorth { get; private set; }
			public double SweRefEast { get; private set; }
			public GeodeticCoordinate Coordinate { get; private set; }

			public ItemType ItemType { get; set; }

			public int CenterPixelX { get; set; }
			public int CenterPixelY { get; set; }
		}

		private static Color GetAveragePixel(Color[,] b, int x, int y, int sampleStep, int width, int height)
		{
			// 3x3 average
			int xlim1 = Math.Max(0, x - sampleStep);
			int xlim2 = Math.Min(width-1, x + sampleStep);
			int ylim1 = Math.Max(0, y - sampleStep);
			int ylim2 = Math.Min(height-1, y + sampleStep);

			// Get pixels (handles border cases)
			// Calculate 'average color' from all found pixels
			int red = 0, green = 0, blue = 0;
			int colorCount = 0;
			for (int xs = xlim1; xs <= xlim2; xs++)
			{
				for (int ys = ylim1; ys <= ylim2; ys++)
				{
					var color = b[xs, ys];

					red += color.R;
					green += color.G;
					blue += color.B;

					colorCount++;
				}
			}

			red /= colorCount;
			green /= colorCount;
			blue /= colorCount;

			return Color.FromArgb(red, green, blue);
		}

    }
}
