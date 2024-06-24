using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using KGySoft;
using KGySoft.CoreLibraries;
using KGySoft.Drawing.Imaging;

namespace kaboom;

class Program
{
    static void Main(string[] args)
    {
        using IReadWriteBitmapData? bitmapData = GenerateAlphaGradientBitmapData(new Size(255*2, 255));

        IEnumerable<IReadableBitmapData> FramesIterator()
        {
            using IReadWriteBitmapData currentFrame =
                BitmapDataFactory.CreateBitmapData(new Size(bitmapData.Width, bitmapData.Height * 2));

            IQuantizer quantizer = PredefinedColorsQuantizer.Rgb888(Color.White);
            for (int y = bitmapData.Height - 1; y >= 0; y--)
            {
                bitmapData.CopyTo(currentFrame, new Rectangle(0, y, bitmapData.Width, 1),
                    new Point(0, bitmapData.Height - y), quantizer);
                yield return currentFrame;
            }

            quantizer = PredefinedColorsQuantizer.Rgb888(Color.Black);
            for (int y = 0; y < bitmapData.Height; y++)
            {
                bitmapData.CopyTo(currentFrame, new Rectangle(0, y, bitmapData.Width, 1),
                    new Point(0, y + bitmapData.Height), quantizer);
                yield return currentFrame;
            }
        }

        IEnumerable<TimeSpan> DelaysIterator()
        {
            for (int i = 0; i < bitmapData.Height * 2 - 1; i++)
                yield return TimeSpan.FromMilliseconds(20);
            yield return TimeSpan.FromSeconds(3);
        }

        using var ms = new MemoryStream();
        var config = new AnimatedGifConfiguration(FramesIterator(), DelaysIterator())
        {
            Quantizer = OptimizedPaletteQuantizer.Octree()
        };

        EncodeAnimatedGif(config, false);

        static void EncodeAnimatedGif(AnimatedGifConfiguration config, bool performCompare = true,
            string streamName = null, [CallerMemberName] string testName = null)
        {
            using var ms = new MemoryStream();
            GifEncoder.EncodeAnimation(config, ms);
            SaveStream(streamName, ms, testName: testName);
            var x = config.GetType();
            var framesProp = x.GetProperty("Frames", BindingFlags.NonPublic | BindingFlags.Instance);
            var frames = framesProp.GetValue(config) as IEnumerable<IReadableBitmapData>;
            IReadableBitmapData[] sourceFrames = frames.ToArray(); // actually 2nd enumeration
            ms.Position = 0;

            using Bitmap restored = new Bitmap(ms);
            Bitmap[] actualFrames = ExtractBitmaps(restored);
            try
            {
                int expectedLength = sourceFrames.Length + (config.AnimationMode == AnimationMode.PingPong
                    ? Math.Max(0, sourceFrames.Length - 2)
                    : 0);
#if WINDOWS
                if (!performCompare)
                    return;

                var size = restored.Size;
                var quantizer = config.Quantizer ?? OptimizedPaletteQuantizer.Wu();
                for (int i = 0; i < actualFrames.Length; i++)
                {
                    IReadableBitmapData sourceFrame = sourceFrames[i];
                    if (sourceFrame.IsDisposed)
                        continue;
                    Console.Write($"Frame #{i}: ");
                    BitmapData bitmapData =
 actualFrames[i].LockBits(new Rectangle(Point.Empty, size), ImageLockMode.ReadOnly, actualFrames[i].PixelFormat);
                    using IReadableBitmapData actualFrame =
 BitmapDataFactory.CreateBitmapData(bitmapData.Scan0, size, bitmapData.Stride, KnownPixelFormat.Format32bppArgb, disposeCallback: () => actualFrames[i].UnlockBits(bitmapData));
                    IReadWriteBitmapData expectedFrame;
                    if (sourceFrame.Size == actualFrame.Size)
                        expectedFrame =
 sourceFrames[i].Clone(KnownPixelFormat.Format8bppIndexed, quantizer, config.Ditherer);
                    else
                    {
                        Assert.AreNotEqual(AnimationFramesSizeHandling.ErrorIfDiffers, config.SizeHandling);
                        expectedFrame = BitmapDataFactory.CreateBitmapData(actualFrame.Size);
                        if (config.SizeHandling == AnimationFramesSizeHandling.Resize)
                            sourceFrame.DrawInto(expectedFrame, new Rectangle(Point.Empty, expectedFrame.Size), quantizer, config.Ditherer);
                        else
                            sourceFrame.DrawInto(expectedFrame, new Point(expectedFrame.Width / 2 - sourceFrame.Width / 2, expectedFrame.Height / 2 - expectedFrame.Width / 2), quantizer, config.Ditherer);
                    }

                    try
                    {
                        AssertAreEqual(expectedFrame, actualFrame, true);
                    }
                    finally
                    {
                        expectedFrame.Dispose();
                    }

                    Console.WriteLine("Equals");
                }
#endif
            }
            finally
            {
                actualFrames.ForEach(f => f.Dispose());
            }
        }

        static void SaveStream(string streamName, MemoryStream ms, string extension = "gif",
            [CallerMemberName] string testName = null)
        {
            string dir = Path.Combine(Files.GetExecutingPath(), "TestResults");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string fileName = Path.Combine(dir,
                $"{testName}{(streamName == null ? null : $"_{streamName}")}.{DateTime.Now:yyyyMMddHHmmssffff}.{extension}");
            using (var fs = File.Create(fileName))
                ms.WriteTo(fs);
        }

        static Bitmap[] ExtractBitmaps(Bitmap image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image), PublicResources.ArgumentNull);

            var dimension = FrameDimension.Time;
            int frameCount = image.FrameDimensionsList.Length == 0 ? 1 : image.GetFrameCount(dimension);

            if (frameCount <= 1)
                return new Bitmap[] { image.Clone(new Rectangle(Point.Empty, image.Size), image.PixelFormat) };

            // extracting frames
            Bitmap[] result = new Bitmap[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                image.SelectActiveFrame(dimension, frame);
                result[frame] = image.Clone(new Rectangle(Point.Empty, image.Size), image.PixelFormat);
            }

            // selecting first frame again
            image.SelectActiveFrame(dimension, 0);

            return result;
        }

        static IReadWriteBitmapData GenerateAlphaGradientBitmapData(Size size)
        {
            var result = BitmapDataFactory.CreateBitmapData(size);
            GenerateAlphaGradient(result);
            return result;
        }

        static void GenerateAlphaGradient(IReadWriteBitmapData bitmapData)
        {
            var firstRow = bitmapData.FirstRow;
            float ratio = 255f / (bitmapData.Width / 6f);
            float limit = bitmapData.Width / 6f;

            for (int x = 0; x < bitmapData.Width; x++)
            {
                // red -> yellow
                if (x < limit)
                    firstRow[x] = new Color32(255, ((int)(x * ratio)).ClipToByte(), 0);
                // yellow -> green
                else if (x < limit * 2)
                    firstRow[x] = new Color32(((int)(255 - (x - limit) * ratio)).ClipToByte(), 255, 0);
                // green -> cyan
                else if (x < limit * 3)
                    firstRow[x] = new Color32(0, 255, ((int)((x - limit * 2) * ratio)).ClipToByte());
                // cyan -> blue
                else if (x < limit * 4)
                    firstRow[x] = new Color32(0, ((int)(255 - (x - limit * 3) * ratio)).ClipToByte(), 255);
                // blue -> magenta
                else if (x < limit * 5)
                    firstRow[x] = new Color32(((int)((x - limit * 4) * ratio)).ClipToByte(), 0, 255);
                // magenta -> red
                else
                    firstRow[x] = new Color32(255, 0, ((int)(255 - (x - limit * 5) * ratio)).ClipToByte());
            }

            if (bitmapData.Height < 2)
                return;

            var row = bitmapData.GetMovableRow(1);
            ratio = 255f / bitmapData.Height;
            do
            {
                byte a = ((int)(255 - row.Index * ratio)).ClipToByte();
                for (int x = 0; x < bitmapData.Width; x++)
                    row[x] = Color32.FromArgb(a, firstRow[x]);

            } while (row.MoveNextRow());
        }


    }
}