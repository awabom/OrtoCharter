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
			//var files = new[] { Path.Combine(ortoFolderPath, "small.png") };
			var files = Directory.GetFiles(ortoFolderPath, "*.png").Where(x => !x.Contains("analyzed"));

			Parallel.ForEach(files, (file) =>
			{
				string analyzedFileName = GetAnalyzedFileName(file);
				//if (!File.Exists(analyzedFileName))
				{
					AnalyzeSingle(file, analyzedFileName);
				}
			});
		}

		private static string GetAnalyzedFileName(string sourceFilePath)
		{
			return Path.Combine(Path.GetDirectoryName(sourceFilePath), Path.GetFileNameWithoutExtension(sourceFilePath) + "_analyzed.png");
		}

		private void AnalyzeSingle(string imageFilePath, string outputFilePath)
		{
			const int RockSampleStep = 2; // 5x5
			const int SurroundSampleStep = 10; // 21x21
			const int LandClearStep = 5; // 11x11
			//const double BlueGreenFactor = 0.93;
			const double BrightnessFactorLow = 1.05;
			const double BrightnessFactoryHigh = 1.08;
			const double LandBrightnessLevel = 0.39;

			using (var image = new Bitmap(Image.FromFile(imageFilePath)))
			{
				using (Bitmap output = (Bitmap)image.Clone())
				{
					Color Land = Color.Black;
					Color DangerHigh = Color.Red;
					Color DangerLow = Color.Yellow;
					Color Waypoint = Color.Magenta;

					var imageArray = new Color[image.Width, image.Height];
					for (int x = 0; x < image.Width; x++)
						for (int y = 0; y < image.Height; y++)
							imageArray[x, y] = image.GetPixel(x, y);
					var width = image.Width;
					var height = image.Height;
					var analyzeArray = new Color[image.Width, image.Height];

					for (int x = 0; x < width; x++)
					{
						for (int y = 0; y < height; y++)
						{
							Color pixelColor = GetAveragePixel(imageArray, x, y, 0, width, height);
							Color sample = GetAveragePixel(imageArray, x, y, RockSampleStep, width, height);
							float sampleBrightness = sample.GetBrightness();

							Color dangerColor = Color.Transparent;

							// Is this (probably) land?
							if (pixelColor.GetBrightness() >= LandBrightnessLevel || sampleBrightness >= LandBrightnessLevel)
							{
								dangerColor = Land;
							}
							else
							{
								Color sample2 = GetAveragePixel(imageArray, x, y, SurroundSampleStep, width, height);

								// Brightness High trigger
								float brightnessFactor = sampleBrightness / sample2.GetBrightness();
								if (brightnessFactor > BrightnessFactoryHigh)
								{
									dangerColor = DangerHigh;
								}
								else if (brightnessFactor > BrightnessFactorLow)
								{
									dangerColor = DangerLow;
								}

								// Green/Blue factor checking
								/*
								float greenBlueFactor = sample.G / sample.B;

								bool trigGreenBlue =  > BlueGreenFactor;
								*/
							}

							// Is anything identified?
							if (dangerColor != Color.Transparent)
							{
								analyzeArray[x, y] = dangerColor;
							}
						}
					}

					// Remove stuff close to 'land'
					var landFilterArray = (Color[,])analyzeArray.Clone();
					for (int x = 0; x < width; x++)
					{
						for (int y = 0; y < height; y++)
						{
							if (landFilterArray[x, y] == Land)
							{
								int cxBegin = Math.Max(0, x - LandClearStep);
								int cxEnd = Math.Min(width, x + LandClearStep);
								int cyBegin = Math.Max(0, y - LandClearStep);
								int cyEnd = Math.Min(height, y + LandClearStep);

								for (int cx = cxBegin; cx < cxEnd; cx++)
								{
									for (int cy = cyBegin; cy < cyEnd; cy++)
									{
										analyzeArray[cx, cy] = Color.Transparent;
									}
								}
							}
						}
					}

					// Remove single-pixel dangers
					for (int x = 1; x < width - 1; x++)
					{
						for (int y = 1; y < height - 1; y++)
						{
							if (analyzeArray[x, y] != Color.Transparent)
							{
								bool foundAnother = false;
								for (int xc = x - 1; xc <= x + 1 && !foundAnother; xc++)
								{
									for (int yc = y - 1; yc <= y + 1 && !foundAnother; yc++)
									{
										if (xc != x && yc != y && analyzeArray[xc, yc] != Color.Transparent)
											foundAnother = true;
									}
								}

								// Remove danger single-pixel
								if (!foundAnother)
								{
									analyzeArray[x, y] = imageArray[x, y];
								}
							}
						}
					}

					// Build coordinate output
					try
					{
						string[] parts = Path.GetFileNameWithoutExtension(imageFilePath).Split(',');
						int upperLeftNorth = int.Parse(parts[3], CultureInfo.InvariantCulture);
						int upperLeftEast = int.Parse(parts[0], CultureInfo.InvariantCulture);
						for (int x = 0; x < width; x++)
						{
							for (int y = 0; y < height; y++)
							{
								if (analyzeArray[x,y] != Color.Transparent)
								{

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
					for (int x = 0; x < image.Width; x++)
					{
						for (int y = 0; y < image.Height; y++)
						{
							Color outputColor = analyzeArray[x, y];
							if (outputColor == Color.Transparent)
							{
								outputColor = imageArray[x, y];
							}
							output.SetPixel(x, y, outputColor);
						}
					}

					output.Save(outputFilePath, System.Drawing.Imaging.ImageFormat.Png);
				}
			}
		}

		public enum ItemType
		{
			DangerHigh,
			DangerLow
		}

		public class FoundStuff
		{
			public void SetSweRef99TM(int north, int east)
			{
				GridCoordinate gridCoordinate = new GridCoordinate { Projection = Shorthand.Geodesy.Projections.SwedishProjections.SWEREF99TM, X = east, Y = north };
				Coordinate = GaussKruger.GridToGeodetic(gridCoordinate);
			}

			GeodeticCoordinate Coordinate { get; set; }

			ItemType ItemType { get; set; }
			
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
