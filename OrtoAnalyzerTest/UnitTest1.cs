using System;
using System.Drawing;
using System.Linq;
using Xunit;

namespace OrtoAnalyzerTest
{
    public class UtilTest
    {
		[Fact]
		public void TestFindSize()
		{
			var pixelArray = new Color[4, 4];

			// Test pixel-array
			// .R..
			// YB.Y
			// .BGB
			pixelArray[1, 0] = Color.Red;
			pixelArray[0, 1] = Color.Yellow;
			pixelArray[1, 1] = Color.Blue;
			pixelArray[3, 1] = Color.Yellow;
			pixelArray[1, 2] = Color.Blue;
			pixelArray[2, 2] = Color.Green;
			pixelArray[3, 2] = Color.Blue;

			{
				// Run test on the existing area
				int x = 0;
				int y = 1;

				int size = OrtoAnalyzer.Utils.FindSizeAndRemove(pixelArray, x, y, out int centerX, out int centerY, out var foundColors, out var outsideColors, (color) => color != Color.Empty);

				Assert.Equal(7, size);
				Assert.Equal(1, centerX);
				Assert.Equal(1, centerY);

				Assert.Equal(Color.Empty, outsideColors.Single());
			}

			// Run test on empty area
			{
				int x = 2;
				int y = 1;

				int size = OrtoAnalyzer.Utils.FindSizeAndRemove(pixelArray, x, y, out int centerX, out int centerY, out var foundColors, out var outsideColors, (color) => color != Color.Empty);

				Assert.Equal(0, size);
				Assert.Equal(x, centerX);
				Assert.Equal(y, centerY);

				Assert.Equal(Color.Empty, outsideColors.Single());
			}
		}
	}
}
