using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ClientCore;
using DTAClient.DXGUI.Multiplayer.GameLobby;
using Rampastring.Tools;
using lzo.net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DTAClient.Domain.Multiplayer
{
    /// <summary>
    /// A helper class for extracting preview images from maps.
    /// </summary>
    public static class MapPreviewExtractor
    {
        private const string HiddenPreviewValue = "yAsAIAXQ5PDQ5PDQ6JQATAEE6PDQ4PDI4JgBTAFEAkgAJyAATAG0AydEAEABpAJIA0wBVA";

        /// <summary>
        /// Extracts map preview image as a bitmap.
        /// </summary>
        /// <param name="mapIni">Map file.</param>
        /// <returns>Bitmap of map preview image, or null if preview could not be extracted.</returns>
        public static Image ExtractMapPreview(IniFile mapIni)
        {
            string baseFilename = mapIni.FileName.Replace(ProgramConstants.GamePath, "");

            if (!TryGetPreviewData(mapIni, out int previewWidth, out int previewHeight, out string previewPackData,
                out bool isHiddenPreview, out string errorMessage))
            {
                Logger.Log("MapPreviewExtractor: " + baseFilename + " - " + errorMessage);
                return null;
            }

            return ExtractMapPreview(baseFilename, previewWidth, previewHeight, previewPackData, isHiddenPreview);
        }

        /// <summary>
        /// Extracts a map preview image directly from a map file without constructing an IniFile.
        /// </summary>
        public static Image ExtractMapPreview(string mapFilePath)
        {
            string baseFilename = mapFilePath.Replace(ProgramConstants.GamePath, "");

            if (!TryReadPreviewData(mapFilePath, out int previewWidth, out int previewHeight, out string previewPackData,
                out bool isHiddenPreview, out string errorMessage))
            {
                Logger.Log("MapPreviewExtractor: " + baseFilename + " - " + errorMessage);
                return null;
            }

            return ExtractMapPreview(baseFilename, previewWidth, previewHeight, previewPackData, isHiddenPreview);
        }

        private static Image ExtractMapPreview(string baseFilename, int previewWidth, int previewHeight,
            string previewPackData, bool isHiddenPreview)
        {
            if (isHiddenPreview)
            {
                Logger.Log("MapPreviewExtractor: " + baseFilename + " - Hidden preview detected, not extracting preview.");
                return null;
            }

            byte[] dataSource;

            try
            {
                dataSource = Convert.FromBase64String(previewPackData);
            }
            catch (Exception)
            {
                Logger.Log("MapPreviewExtractor: " + baseFilename + " - [PreviewPack] is malformed, unable to extract preview.");
                return null;
            }

            byte[] dataDest = DecompressPreviewData(dataSource, previewWidth * previewHeight * 3, out string errorMessage);

            if (errorMessage != null)
            {
                Logger.Log("MapPreviewExtractor: " + baseFilename + " - " + errorMessage);
                return null;
            }

            Image bitmap = CreatePreviewBitmapFromImageData(previewWidth, previewHeight, dataDest, out errorMessage);

            if (errorMessage != null)
            {
                Logger.Log("MapPreviewExtractor: " + baseFilename + " - " + errorMessage);
                return null;
            }

            return bitmap;
        }

        private static bool TryGetPreviewData(IniFile mapIni, out int previewWidth, out int previewHeight,
            out string previewPackData, out bool isHiddenPreview, out string errorMessage)
        {
            IniSection previewPackSection = mapIni.GetSection("PreviewPack");
            if (previewPackSection == null || previewPackSection.Keys.Count == 0)
            {
                previewWidth = -1;
                previewHeight = -1;
                previewPackData = null;
                isHiddenPreview = false;
                errorMessage = "no [PreviewPack] exists, unable to extract preview.";
                return false;
            }

            isHiddenPreview = previewPackSection.GetStringValue("1", string.Empty) == HiddenPreviewValue;
            previewPackData = ConcatenatePreviewPack(previewPackSection.Keys);

            if (!TryParsePreviewSize(mapIni.GetStringValue("Preview", "Size", string.Empty), out previewWidth, out previewHeight))
            {
                errorMessage = "[Preview] Size value is invalid, unable to extract preview.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private static bool TryReadPreviewData(string mapFilePath, out int previewWidth, out int previewHeight,
            out string previewPackData, out bool isHiddenPreview, out string errorMessage)
        {
            previewWidth = -1;
            previewHeight = -1;
            previewPackData = null;
            isHiddenPreview = false;

            try
            {
                using FileStream stream = SafePath.GetFile(mapFilePath).OpenRead();
                using var reader = new StreamReader(stream, MapCodeHelper.GetMapEncoding(mapFilePath));

                PreviewSection currentSection = PreviewSection.None;
                var previewPackBuilder = new StringBuilder();

                while (!reader.EndOfStream)
                {
                    string currentLine = reader.ReadLine();

                    int commentStartIndex = currentLine.IndexOf(';');
                    if (commentStartIndex > -1)
                        currentLine = currentLine.Substring(0, commentStartIndex);

                    if (string.IsNullOrWhiteSpace(currentLine))
                        continue;

                    if (currentLine[0] == '[')
                    {
                        int sectionNameEndIndex = currentLine.IndexOf(']');
                        if (sectionNameEndIndex == -1)
                        {
                            errorMessage = "Invalid INI section definition encountered while reading preview.";
                            return false;
                        }

                        string sectionName = currentLine.Substring(1, sectionNameEndIndex - 1);
                        currentSection = sectionName switch
                        {
                            "Preview" => PreviewSection.Preview,
                            "PreviewPack" => PreviewSection.PreviewPack,
                            _ => PreviewSection.Other
                        };

                        if (previewPackBuilder.Length > 0 &&
                            currentSection != PreviewSection.PreviewPack &&
                            previewWidth > 0 &&
                            previewHeight > 0)
                        {
                            break;
                        }

                        continue;
                    }

                    GetKeyAndValue(currentLine, out string key, out string value);

                    switch (currentSection)
                    {
                        case PreviewSection.Preview when key == "Size":
                            if (!TryParsePreviewSize(value, out previewWidth, out previewHeight))
                            {
                                errorMessage = "[Preview] Size value is invalid, unable to extract preview.";
                                return false;
                            }
                            break;
                        case PreviewSection.PreviewPack:
                            if (key == "1" && value == HiddenPreviewValue)
                                isHiddenPreview = true;

                            previewPackBuilder.Append(value);
                            break;
                    }
                }

                if (previewPackBuilder.Length == 0)
                {
                    errorMessage = "no [PreviewPack] exists, unable to extract preview.";
                    return false;
                }

                if (previewWidth < 1 || previewHeight < 1)
                {
                    errorMessage = "[Preview] Size value is invalid, unable to extract preview.";
                    return false;
                }

                previewPackData = previewPackBuilder.ToString();
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "Error encountered reading preview data. Message: " + ex.Message;
                return false;
            }
        }

        private static string ConcatenatePreviewPack(List<KeyValuePair<string, string>> previewPackKeys)
        {
            int totalLength = 0;
            foreach (var (_, value) in previewPackKeys)
                totalLength += value.Length;

            var builder = new StringBuilder(totalLength);
            foreach (var (_, value) in previewPackKeys)
                builder.Append(value);

            return builder.ToString();
        }

        private static void GetKeyAndValue(string line, out string key, out string value)
        {
            int equalsIndex = line.IndexOf('=');

            if (equalsIndex == -1)
            {
                key = line.Trim();
                value = string.Empty;
                return;
            }

            key = line.Substring(0, equalsIndex).Trim();
            value = line.Substring(equalsIndex + 1).Trim();
        }

        private static bool TryParsePreviewSize(string sizeValue, out int previewWidth, out int previewHeight)
        {
            previewWidth = -1;
            previewHeight = -1;

            string[] previewSizes = sizeValue.Split(',');
            if (previewSizes.Length <= 3)
                return false;

            return int.TryParse(previewSizes[2], out previewWidth) &&
                   int.TryParse(previewSizes[3], out previewHeight) &&
                   previewWidth > 0 &&
                   previewHeight > 0;
        }

        /// <summary>
        /// Decompresses map preview image data.
        /// </summary>
        /// <param name="dataSource">Array of compressed map preview image data.</param>
        /// <param name="decompressedDataSize">Size of decompressed preview image data.</param>
        /// <param name="errorMessage">Will be set to error message if something went wrong, otherwise null.</param>
        /// <returns>Array of decompressed preview image data if successfully decompressed, otherwise null.</returns>
        private static byte[] DecompressPreviewData(byte[] dataSource, int decompressedDataSize, out string errorMessage)
        {
            try
            {
                byte[] dataDest = new byte[decompressedDataSize];
                int readBytes = 0, writtenBytes = 0;

                while (true)
                {
                    if (readBytes >= dataSource.Length)
                        break;

                    ushort sizeCompressed = BinaryPrimitives.ReadUInt16LittleEndian(dataSource.AsSpan(readBytes));
                    readBytes += 2;
                    ushort sizeUncompressed = BinaryPrimitives.ReadUInt16LittleEndian(dataSource.AsSpan(readBytes));
                    readBytes += 2;

                    if (sizeCompressed == 0 || sizeUncompressed == 0)
                        break;

                    if (readBytes + sizeCompressed > dataSource.Length ||
                        writtenBytes + sizeUncompressed > dataDest.Length)
                    {
                        errorMessage = "Preview data does not match preview size or the data is corrupted, unable to extract preview.";
                        return null;
                    }

                    using var stream = new LzoStream(new MemoryStream(dataSource, readBytes, sizeCompressed), CompressionMode.Decompress);
                    stream.Read(dataDest, writtenBytes, sizeUncompressed);
                    readBytes += sizeCompressed;
                    writtenBytes += sizeUncompressed;
                }

                errorMessage = null;
                return dataDest;
            }
            catch (Exception ex)
            {
                errorMessage = "Error encountered decompressing preview data. Message: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Creates a preview bitmap based on a provided dimensions and raw image pixel data in 24-bit RGB format.
        /// </summary>
        /// <param name="width">Width of the bitmap.</param>
        /// <param name="height">Height of the bitmap.</param>
        /// <param name="imageData">Raw image pixel data in 24-bit RGB format.</param>
        /// <param name="errorMessage">Will be set to error message if something went wrong, otherwise null.</param>
        /// <returns>Bitmap based on the provided dimensions and raw image data, or null if length of image data does not match the provided dimensions or if something went wrong.</returns>
        private static Image CreatePreviewBitmapFromImageData(int width, int height, byte[] imageData, out string errorMessage)
        {
            const int pixelFormatByteCount = 3;

            if (imageData.Length != width * height * pixelFormatByteCount)
            {
                errorMessage = "Provided preview image dimensions do not match preview image data length.";
                return null;
            }

            try
            {
                errorMessage = null;
                return Image.LoadPixelData<Rgb24>(imageData, width, height);
            }
            catch (Exception ex)
            {
                errorMessage = "Error encountered creating preview bitmap. Message: " + ex.Message;
                return null;
            }
        }

        private enum PreviewSection
        {
            None,
            Preview,
            PreviewPack,
            Other
        }
    }
}
