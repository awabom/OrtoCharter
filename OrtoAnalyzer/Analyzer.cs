using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shorthand.Geodesy;

namespace OrtoAnalyzer
{
    public class Analyzer
    {
		string ortoFolderPath;
		public Analyzer(string _ortoFolderPath)
		{
			ortoFolderPath = _ortoFolderPath;
		}

		public void Analyze()
		{
			//var files = new[] { Path.Combine(ortoFolderPath, "643645,6719691,644669,6720683,test.png") };
			//var files = new[] { Path.Combine(ortoFolderPath, "645693,6721707,646717,6722731.png") };
			var files = Directory.GetFiles(ortoFolderPath, "*.png").Where(x => !x.Contains("analyzed"));

			Parallel.ForEach(files, (file) =>
			{
				string analyzedFileName = GetAnalyzedFileName(file);
				AnalyzeSingle(file, analyzedFileName);
			});
		}

		private static string GetAnalyzedFileName(string sourceFilePath)
		{
			return Path.Combine(Path.GetDirectoryName(sourceFilePath), Path.GetFileNameWithoutExtension(sourceFilePath) + "_analyzed.png");
		}

		private IEnumerable<PointOfInterest> AnalyzeSingle(string imageFilePath, string outputFilePath)
		{
			const int RockSampleStep = 2; // 5x5
			const int SurroundSampleStep = 10; // 21x21
			const int LandClearStep = 10; // 21x21 
			const int TreeClearStep = 20; // 41x41
			const double BrightnessFactorLow = 1.05;
			const double BrightnessFactoryHigh = 1.08;
			const double LandBrightnessLevel = 0.37; // (100.0 / 255.0);
			const double GreenBlueTreeBrightnessLevel = 0.30; // trigger level for green-blue-factor
			const double GreenBlueTreeFactor = 1.09; // 9% more green than blue = tree
			const int MinDangerSize = 2; // 2 pixels minimum danger size
			const int MinRedLevelForDanger = 31; // if R < this number - no danger
			const float MaxHueForDanger = 195.0f; // If Hue more than this number - no danger
			const float HueDangerAreaLowMax = 170.0f;
			const float HueDangerAreaHighMax = 130.0f;

			var foundPoints = new List<PointOfInterest>();

			using (var image = new Bitmap(Image.FromFile(imageFilePath)))
			{
				Color ColorLand = Color.Black;
				Color ColorDangerHigh = Color.Red;
				Color ColorDangerLow = Color.DarkRed;
				Color ColorDangerAreaHigh = Color.Yellow;
				Color ColorDangerAreaLow = Color.Brown;
				Color ColorPOI = Color.Green;
				Color ColorTree = Color.DarkGreen;

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
						// Could already be filled in as land/tree - if so, it can't be a 'danger', but it can trigger 'more land' to be filled
						bool alreadyLandOrTree = analyzeArray[x, y] != Color.Empty;

						Color pixelColor = GetAveragePixel(imageArray, x, y, 0, width, height);

						Color dangerColor = Color.Empty;
						int fillAroundStep = 0;

						// Is this (probably) a tree?
						if (pixelColor.GetBrightness() >= GreenBlueTreeBrightnessLevel && (pixelColor.G / pixelColor.B) > GreenBlueTreeFactor)
						{
							fillAroundStep = TreeClearStep;
							dangerColor = ColorTree;
						}
						// Is this (probably) land?
						else if (pixelColor.GetBrightness() >= LandBrightnessLevel) // || sampleBrightness >= LandBrightnessLevel)
						{
							fillAroundStep = LandClearStep;
							dangerColor = ColorLand;
						}
						// Maybe water? (Don't check here if pixel already covered by land/tree)
						else if (!alreadyLandOrTree) 
						{
							fillAroundStep = 0;

							Color sample = GetAveragePixel(imageArray, x, y, RockSampleStep, width, height);
							float sampleHue = sample.GetHue();

							if (sampleHue < HueDangerAreaHighMax)
							{
								dangerColor = ColorDangerAreaHigh;
							}
							else if (sampleHue < HueDangerAreaLowMax)
							{
								dangerColor = ColorDangerAreaLow;
							}
							else if (/*sample.R > MinRedLevelForDanger && */sampleHue < MaxHueForDanger)
							{
								Color sample2 = GetAveragePixel(imageArray, x, y, SurroundSampleStep, width, height);

								// Brightness High/Low triggers for hidden rocks in water
								float brightnessFactor = sample.GetBrightness() / sample2.GetBrightness();
								if (brightnessFactor > BrightnessFactoryHigh)
								{
									dangerColor = ColorDangerHigh;
								}
								else if (brightnessFactor > BrightnessFactorLow)
								{
									dangerColor = ColorDangerLow;
								}
							}
						}

						// Is anything identified? Fill the box (or just a pixel if step = 0)
						if (dangerColor != Color.Empty)
						{
							int cxBegin = Math.Max(0, x - fillAroundStep);
							int cxEnd = Math.Min(width - 1, x + fillAroundStep);
							int cyBegin = Math.Max(0, y - fillAroundStep);
							int cyEnd = Math.Min(height - 1, y + fillAroundStep);

							for (int cx = cxBegin; cx <= cxEnd; cx++)
							{
								for (int cy = cyBegin; cy <= cyEnd; cy++)
								{
									analyzeArray[cx, cy] = dangerColor;
								}
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
								int size = Utils.FindSize(coordArray, x, y, out int centerX, out int centerY, out HashSet<Color> foundColors, (color) => color == ColorDangerHigh || color == ColorDangerLow);

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



		public enum ItemType
		{
			DangerHigh,
			DangerLow
		}

		public class PointOfInterest
		{
			public void SetSweRef99TM(double north, double east)
			{
				GridCoordinate gridCoordinate = new GridCoordinate { Projection = Shorthand.Geodesy.Projections.SwedishProjections.SWEREF99TM, X = east, Y = north };
				Coordinate = GaussKruger.GridToGeodetic(gridCoordinate);
			}

			public GeodeticCoordinate Coordinate { get; set; }

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
