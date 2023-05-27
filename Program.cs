using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

// DONE tilemap c array
// DONE tileset bitmap
// DONE tileset c array
// palette c array


namespace bmp2background
{
    // Exception type with an extra return code for other command line tools to detect
    public class ReturnCodeException : Exception
    {
        public int ReturnCode { get; }

        public ReturnCodeException(string message, int returnCode) : base(message)
        {
            ReturnCode = returnCode;
        }
    }

    class ImageToTilesConverter
    {
        // return codes
        const int ReturnCode_Ok                     =  0;
        const int ReturnCode_NoParameters           = -1;
        const int ReturnCode_TileSizeDoesntFit      = -2;
        const int ReturnCode_TilesetIsEmpty         = -3;
        const int ReturnCode_Not4bppFormat          = -4;
        const int ReturnCode_FileDoesNotExist       = -5;
        const int ReturnCode_NoPaletteFound         = -6;
        const int ReturnCode_PaletteIsNot16Colors   = -7;

        static List<Bitmap> SplitBitmapIntoTiles(Bitmap image, Size tileSize)
        {
            List<Bitmap> tiles = new List<Bitmap>();

            int numTilesX = image.Width / tileSize.Width;
            int numTilesY = image.Height / tileSize.Height;

            for (int y = 0; y < numTilesY; y++)
            {
                for (int x = 0; x < numTilesX; x++)
                {
                    Rectangle tileBounds = new Rectangle(x * tileSize.Width, y * tileSize.Height, tileSize.Width, tileSize.Height);
                    Bitmap tile = image.Clone(tileBounds, image.PixelFormat);
                    tiles.Add(tile);
                }
            }

            return tiles;
        }

        // TODO: compile hashes of tiles and compare those instead as this is very heavy
        static bool AreTilesEqual(Bitmap tile1, Bitmap tile2)
        {
            if (tile1.Width != tile2.Width || tile1.Height != tile2.Height)
            {
                return false;
            }

            for (int y = 0; y < tile1.Height; y++)
            {
                for (int x = 0; x < tile1.Width; x++)
                {
                    if (tile1.GetPixel(x, y) != tile2.GetPixel(x, y))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        static void ExportTileMap(string tilemapName, List<int> tileMap, int tilemapWidth, int tilemapHeight, string destinationFolder)
        {
            // .h file
            var header = new StringBuilder();

            header.AppendLine($"#ifndef {tilemapName.ToUpper()}_INCLUDE_H");
            header.AppendLine($"#define {tilemapName.ToUpper()}_INCLUDE_H");
            header.AppendLine();
            header.AppendLine($"extern const unsigned short {tilemapName}[{tilemapWidth * tilemapHeight}];");
            header.AppendLine();
            header.AppendLine($"#endif // {tilemapName.ToUpper()}_INCLUDE_H");
            File.WriteAllText(destinationFolder + tilemapName + ".h", header.ToString());

            // .c file
            var source = new StringBuilder();

            source.AppendLine($"#include \"{tilemapName}.h\"");
            source.AppendLine();

            source.AppendLine($"// tilemap width: {tilemapWidth}, tilemap height {tilemapHeight}");
            source.AppendLine($"const unsigned short {tilemapName}[{tilemapWidth * tilemapHeight}] = ");
            source.AppendLine("{");

            int counter = 0;

            source.Append("    ");

            foreach (var tileIndex in tileMap)
            {
                source.Append($"0x{tileIndex.ToString("X4")}, ");

                counter++;
                if (counter % tilemapWidth == 0)
                {
                    source.AppendLine();
                    if (counter < tileMap.Count)
                        source.Append("    ");
                }
            }

            source.AppendLine("};");

            File.WriteAllText(destinationFolder + tilemapName + ".c", source.ToString());
        }

        private static (List<Bitmap>, List<int>) CreateOptimizedTileset(List<Bitmap> tileset)
        {
            var optimizedTileset = new List<Bitmap>();
            var tileMap = new List<int>();

            // go through the tileset, adding unique tiles to an optimized tileset
            // and generating a new tilemap
            // TODO: check for tiles flipped on X and Y

            int tilesetIndex = 0;
            foreach (var tile in tileset)
            {
                int optimizedTileIndex = 0;
                bool foundOptimizedTile = false;

                foreach (var optimizedTile in optimizedTileset)
                {
                    if (AreTilesEqual(tile, optimizedTile))
                    {
                        foundOptimizedTile = true;
                        break;
                    }

                    optimizedTileIndex++;
                }

                if (!foundOptimizedTile)
                {
                    optimizedTileset.Add(tile);
                }

                tileMap.Add(optimizedTileIndex);

                tilesetIndex++;
            }

            return (optimizedTileset, tileMap);
        }

        private static Bitmap CreateCombinedBitmap(List<Bitmap> tileset)
        {
            if (tileset.Count == 0)
                return null;

            Bitmap firstTile = tileset.First();

            // Create a bitmap that's a long column of tiles
            Bitmap combinedBitmap = new Bitmap(firstTile.Width, firstTile.Height * tileset.Count);

            // Create a Graphics object from the combined bitmap
            using (Graphics g = Graphics.FromImage(combinedBitmap))
            {
                int counter = 0;

                foreach (var tile in tileset)
                {
                    // Draw the first bitmap onto the combined bitmap at position (0, 0)
                    g.DrawImage(tile, 0, counter * tile.Height);
                    counter++;
                }
            }

            return combinedBitmap;
        }

        static Bitmap LoadSourceImage(string sourceFilename, Size tileSize)
        {
            if (!File.Exists(sourceFilename))
            {
                throw new ReturnCodeException($"Can't find file {sourceFilename}.", ReturnCode_FileDoesNotExist);
            }

            Bitmap sourceImage = new Bitmap(sourceFilename);

            // check that the image is 4bit per pixel
            if (sourceImage.PixelFormat != System.Drawing.Imaging.PixelFormat.Format4bppIndexed)
            {
                throw new ReturnCodeException($"Error: image isn't an indexed 4 bits per pixel format. Only 16 color images are supported.", 
                                              ReturnCode_Not4bppFormat);
            }

            // check if the tilesize fits
            if (sourceImage.Width % tileSize.Width != 0)
            {
                throw new ReturnCodeException($"Error: Tile width {tileSize.Width} doesn't fit given image width {sourceImage.Width}.",
                                              ReturnCode_TileSizeDoesntFit);
            }

            if (sourceImage.Height % tileSize.Height != 0)
            {
                throw new ReturnCodeException($"Error: Tile height {tileSize.Height} doesn't fit given image height {sourceImage.Height}.",
                                              ReturnCode_TileSizeDoesntFit);
            }

            return sourceImage;
        }

        private static void ExportPalette(string paletteName, Bitmap tilesetBitmap, string destinationFolder)
        {
            var palette = tilesetBitmap.Palette;

            if (palette == null)
                throw new ReturnCodeException("No palette found.", ReturnCode_NoPaletteFound);

            if (palette.Entries.Length != 16)
                throw new ReturnCodeException("Palette doesn't contain 16 entries.", ReturnCode_PaletteIsNot16Colors);

            var paletteValues = new List<ushort>();

            // Convert palette to Genesis format
            foreach (var color in palette.Entries)
            {
                ushort red = (ushort)((float)color.R / 256 * 8);
                ushort green = (ushort)((float)color.G / 256 * 8);
                ushort blue = (ushort)((float)color.B / 256 * 8);
                ushort paletteValue = (ushort)((red << 1) | (green << 5) | (blue << 9));
                paletteValues.Add(paletteValue);
            }

            // export .h file
            var header = new StringBuilder();

            header.AppendLine($"#ifndef {paletteName.ToUpper()}_INCLUDE_H");
            header.AppendLine($"#define {paletteName.ToUpper()}_INCLUDE_H");
            header.AppendLine();
            header.AppendLine($"extern const unsigned short {paletteName}[16];");
            header.AppendLine();
            header.AppendLine($"#endif // {paletteName.ToUpper()}_INCLUDE_H");
            File.WriteAllText(destinationFolder + paletteName + ".h", header.ToString());

            // export .c file
            StringBuilder source = new StringBuilder();

            source.AppendLine("#include \"" + paletteName + ".h\"");
            source.AppendLine();
            
            source.AppendLine("const unsigned short " + paletteName + "[16] =");
            source.AppendLine("{");

            foreach (var paletteValue in paletteValues)
            {
                source.AppendLine("    0x" + paletteValue.ToString("x") + ",");
            }

            source.AppendLine("};");
            File.WriteAllText(destinationFolder + paletteName + ".c", source.ToString());
        }

        private static void ExportTileset(string tilesetName, Bitmap tilesetBitmap, string destinationFolder)
        {
            // export .h file
            var header = new StringBuilder();

            header.AppendLine($"#ifndef {tilesetName.ToUpper()}_INCLUDE_H");
            header.AppendLine($"#define {tilesetName.ToUpper()}_INCLUDE_H");
            header.AppendLine();

            int arraySize = (tilesetBitmap.Width * tilesetBitmap.Height / 2);
            header.Append($"extern const unsigned char {tilesetName}[{arraySize}];");
            header.AppendLine($" // {arraySize / 32} tiles");
            header.AppendLine();
            header.AppendLine($"#endif // {tilesetName.ToUpper()}_INCLUDE_H");
            File.WriteAllText(destinationFolder + tilesetName + ".h", header.ToString());
            
            // export .c file
            var bitmapData = GetBitmapData(tilesetBitmap);

            var source = new StringBuilder();
           
            source.AppendLine($"#include \"{tilesetName}.h\"");
            source.AppendLine();
            ExportTilesetArray(source, tilesetName, bitmapData, tilesetBitmap.Width, tilesetBitmap.Height);
            File.WriteAllText(destinationFolder + tilesetName + ".c", source.ToString());
        }

        static byte[] GetBitmapData(Bitmap bitmap)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format4bppIndexed);

            int byteCount = Math.Abs(bitmapData.Stride) * bitmapData.Height;
            byte[] data = new byte[byteCount];
            System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, data, 0, byteCount);

            bitmap.UnlockBits(bitmapData);

            return data;
        }

        static void ExportTilesetArray(StringBuilder sb, string tilesetName, byte[] data, int width, int height)
        {
            int bytesPerRow = (width + 1) / 2; // Number of bytes per row in the bitmap data

            int arraySize = (width * height / 2);

            sb.AppendLine($" // {arraySize / 32} tiles");
            sb.AppendLine($"const unsigned char {tilesetName}[" + arraySize + "] = ");
            sb.AppendLine("{");

            int tileCounter = 0;

            for (int row = 0; row < height; row++)
            {
                if (tileCounter % 8 == 0)
                    sb.AppendLine(" // tile " + tileCounter / 8);

                int startIndex = row * bytesPerRow;
                int endIndex = startIndex + bytesPerRow;

                string rowCode = "";

                for (int i = startIndex; i < endIndex; i++)
                {
                    rowCode += $"0x{data[i]:X2}, ";
                }

                // Add a space every 8th line
                if (row > 0 && (row + 1) % 8 == 0)
                {
                    sb.AppendLine("    " + rowCode.TrimEnd(',', ' ') + ",");
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine("    " + rowCode.TrimEnd(',', ' ') + ",");
                }

                tileCounter++;
            }

            sb.AppendLine("};");
        }

        private static (string, string) ProcessArgs(string[] args)
        {
            string sourceFilename;
            string destinationFolder;

            if (args.Length >= 2)
            {
                sourceFilename = args[0];
                destinationFolder = args[1];
            }
            else if (args.Length == 1)
            {
                sourceFilename = args[0];
                destinationFolder = Directory.GetCurrentDirectory();
            }
            else
            {
                throw new ReturnCodeException("bmp2gb.exe <source .bmp> <destination folder>", ReturnCode_NoParameters);
            }

            return (sourceFilename, destinationFolder);
        }

        static int Main(string[] args)
        {
            // Set global exception handler
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

            // TODO ideas for flags
            // - tileWidth<N> specify tile width
            // - tileHeight<N> specify tile height
            // - force create destination dir
            // - export palette only
            // - export tilemap only
            // - export tileset only
            // - export tileset bmp only
            // - specify destination tileset columns count, or create square bitmap

            // default values
            int tileWidth = 8;
            int tileHeight = 8;

            (string sourceFilename, string destinationFolder) = ProcessArgs(args);

            // Define the tile size
            Size tileSize = new Size(tileWidth, tileHeight);

            Bitmap sourceImage = LoadSourceImage(sourceFilename, tileSize);

            // Build export names
            string rootName = Path.GetFileNameWithoutExtension(sourceFilename);
            string tilemapName = rootName + "_tilemap";
            string tilesetName = rootName + "_tileset";
            string paletteName = rootName + "_palette";

            // ensure the destinationFolder has an ending slash
            if (!String.IsNullOrEmpty(destinationFolder) && !destinationFolder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                destinationFolder += Path.DirectorySeparatorChar;
            }

            Console.WriteLine("Exporting to " + destinationFolder);

            // get tilemap dimensions
            int tilemapWidth = sourceImage.Width / tileWidth;
            int tilemapHeight = sourceImage.Height / tileHeight;

            // Split the image into tiles
            List<Bitmap> tiles = SplitBitmapIntoTiles(sourceImage, tileSize);

            // Remove duplicates from tiles, generate a new tilemap using the optimized tiles
            (List<Bitmap> optimizedTileset, List<int> tileMap) = CreateOptimizedTileset(tiles);

            // export tilemap source files to C
            ExportTileMap(tilemapName, tileMap, tilemapWidth, tilemapHeight, destinationFolder);

            Console.WriteLine("Exported " + tilemapName + " .c/.h");

            // export tileset bitmap
            Bitmap tilesetBitmap = CreateCombinedBitmap(optimizedTileset);
            if (tilesetBitmap == null)
            {
                throw new ReturnCodeException("Error generating tileset. No tiles were found.", ReturnCode_TilesetIsEmpty);
            }

            tilesetBitmap.Save(destinationFolder + tilesetName + ".bmp");
            Console.WriteLine("Exported " + tilesetName + ".bmp");

            // export tileset bitmap data to C
            ExportTileset(tilesetName, tilesetBitmap, destinationFolder);
            Console.WriteLine("Exported " + tilesetName + " .c/.h");

            // export palette data to C
            ExportPalette(paletteName, sourceImage, destinationFolder);
            Console.WriteLine("Exported " + paletteName + " .c/.h");

            Console.WriteLine("Export done.");

            return ReturnCode_Ok;
        }

        // global exception handler
        static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                Console.WriteLine(exception.Message);
            }

            int returnCode = 0;

            if (e.ExceptionObject is ReturnCodeException returnCodeException)
            {
                returnCode = returnCodeException.ReturnCode;
            }

            Environment.Exit(returnCode);
        }
    }

}


