using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace OrtoAnalyzer
{
    public class Utils
    {
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
		/// Find all pixels contiguous to the starting pixel and identify this as a single danger.
		/// </summary>
		/// <param name="coordArray"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="left"></param>
		/// <param name="right"></param>
		/// <param name="top"></param>
		/// <param name="bottom"></param>
		public static int FindSize(Color[,] coordArray, int x, int y, out int centerX, out int centerY, out HashSet<Color> foundColors, Func<Color, bool> MatchColorFunc)
		{
			foundColors = new HashSet<Color>();

			int size = 0;

			int width = coordArray.GetLength(0);
			int height = coordArray.GetLength(1);

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
				if (MatchColorFunc(coordArray[px, py]))
				{
					// Count the pixel and store the color
					size++;
					foundColors.Add(coordArray[px, py]);

					// Count x and y for "centerpoint" calculation
					totalX += px;
					totalY += py;

					// Remove this used pixel
					coordArray[px, py] = Color.Empty;

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
