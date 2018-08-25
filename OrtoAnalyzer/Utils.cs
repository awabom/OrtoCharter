using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace OrtoAnalyzer
{
    public class Utils
    {
		public static void Rectangle(Color[,] imageArray, int centerX, int centerY, int step, Func<Color, bool> MatchColorFunc, Color color)
		{
			int width = imageArray.GetLength(0);
			int height = imageArray.GetLength(1);

			int cxBegin = Math.Max(0, centerX - step);
			int cxEnd = Math.Min(width - 1, centerX + step);
			int cyBegin = Math.Max(0, centerY - step);
			int cyEnd = Math.Min(height - 1, centerY + step);

			for (int cx = cxBegin; cx <= cxEnd; cx++)
			{
				for (int cy = cyBegin; cy <= cyEnd; cy++)
				{
					if (MatchColorFunc(imageArray[cx, cy]))
					{
						imageArray[cx, cy] = color;
					}
				}
			}
		}


		class XY
		{
			public int X, Y;

			public XY(int x, int y)
			{
				X = x;
				Y = y;
			}
		}

		/// <summary>
		/// Fills matching contiguous colors with a new color (NOTE: The fill color cannot be a matching color!)
		/// </summary>
		/// <param name="imageArray"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="MatchColorFunc"></param>
		/// <param name="fillColor"></param>
		public static void Fill(Color[,] imageArray, int x, int y, Func<Color, bool> MatchColorFunc, Color fillColor)
		{
			FindSizeAndFill(imageArray, x, y, out var centerX, out var centerY, out var foundColors, out var outsideColors, MatchColorFunc, fillColor);
		}

		/// <summary>
		/// Find all pixels contiguous to the starting pixel and identify this as a single danger.
		/// </summary>
		/// <param name="imageArray"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="left"></param>
		/// <param name="right"></param>
		/// <param name="top"></param>
		/// <param name="bottom"></param>
		public static int FindSizeAndRemove(Color[,] imageArray, int x, int y, out int centerX, out int centerY, out HashSet<Color> foundColors, out HashSet<Color> outsideColors, Func<Color, bool> MatchColorFunc)
		{
			return FindSizeAndFill(imageArray, x, y, out centerX, out centerY, out foundColors, out outsideColors, MatchColorFunc, Color.Empty);
		}

		public static int FindSizeAndFill(Color[,] imageArray, int x, int y, out int centerX, out int centerY, out HashSet<Color> foundColors, out HashSet<Color> outsideColors, Func<Color, bool> MatchColorFunc, Color fillColor)
		{
			if (MatchColorFunc(fillColor))
				throw new ArgumentException("The fill color can't be a matching color!");

			foundColors = new HashSet<Color>();
			outsideColors = new HashSet<Color>();

			int size = 0;

			int width = imageArray.GetLength(0);
			int height = imageArray.GetLength(1);

			int totalX = 0;
			int totalY = 0;

			// Find all contiguous pixels, stating at x,y
			Queue<XY> checkPixels = new Queue<XY>();
			checkPixels.Enqueue(new XY(x, y));

			// Loop until all enqueued pixels are processed
			while (checkPixels.Count > 0)
			{
				XY pixel = checkPixels.Dequeue();
				int px = pixel.X;
				int py = pixel.Y;

				// Is this pixel a match?
				Color checkColor = imageArray[px, py];
				if (MatchColorFunc(checkColor))
				{
					// Count the pixel and store the color
					size++;
					foundColors.Add(checkColor);

					// Count x and y for "centerpoint" calculation
					totalX += px;
					totalY += py;

					// Fill this visited pixel
					imageArray[px, py] = fillColor;

					// Enqueue pixels in all 4 directions for check (diagonals not checked)
					if (px > 0)
					{
						checkPixels.Enqueue(new XY(px - 1, py));
					}
					if (px < width - 1)
					{
						checkPixels.Enqueue(new XY(px + 1, py));
					}
					if (py > 0)
					{
						checkPixels.Enqueue(new XY(px, py - 1));
					}
					if (py < height - 1)
					{
						checkPixels.Enqueue(new XY(px, py + 1));
					}
				}
				else
				{
					outsideColors.Add(checkColor);
				}
			}

			// Calculate 'mean pixel' to use as center
			if (size > 0)
			{
				centerX = totalX / size;
				centerY = totalY / size;
			}
			else
			{
				centerX = x;
				centerY = y;
			}

			return size;
		}
	}
}
