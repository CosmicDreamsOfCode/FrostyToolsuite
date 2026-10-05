using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Viewport;
using Frosty.Core.Windows;
using Frosty.Hash;
using FrostySdk;
using FrostySdk.Attributes;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using SharpDX.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Documents;
using D3D11 = SharpDX.Direct3D11;


namespace TexturePlugin
{
    [EbxClassMeta(EbxFieldType.Struct)]
    public class FrostyTextureSettingsItem
    {
        [IsReadOnly]
        public CString Filename { get; set; }
    }
    public enum FrostyTextureCubeFace
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ,
    }
    [IsExpandedByDefault]
    [DisplayName("Texture Cube Face")]
    [EbxClassMeta(EbxFieldType.Struct)]
    public class FrostyTextureCubeItem : FrostyTextureSettingsItem
    {
        public FrostyTextureCubeFace Face { get; set; }
        public override string ToString()
        {
            string faceString = "";
            switch (Face)
            {
                case FrostyTextureCubeFace.PositiveX: faceString = "X+"; break;
                case FrostyTextureCubeFace.NegativeX: faceString = "X-"; break;
                case FrostyTextureCubeFace.PositiveY: faceString = "Y+"; break;
                case FrostyTextureCubeFace.NegativeY: faceString = "Y-"; break;
                case FrostyTextureCubeFace.PositiveZ: faceString = "Z+"; break;
                case FrostyTextureCubeFace.NegativeZ: faceString = "Z-"; break;
            }
            return "Cube Face (" + faceString + "): " + Filename;
        }
    }
    [IsExpandedByDefault]
    [DisplayName("Texture Array Slice")]
    [EbxClassMeta(EbxFieldType.Struct)]
    public class FrostyTextureArrayItem : FrostyTextureSettingsItem
    {
        public int Slice { get; set; }
        public override string ToString()
        {
            return "Array Slice (" + Slice + "): " + Filename;
        }
    }

    public class FrostyTextureImportSettings
    {
        [FixedSizeArray]
        [IsExpandedByDefault]
        [EbxFieldMeta(EbxFieldType.Array, arrayType: EbxFieldType.Struct)]
        public List<FrostyTextureSettingsItem> Textures { get; set; } = new List<FrostyTextureSettingsItem>();
    }

    public class TextureImporter
    {
        public void Import(string[] fileNames, ref Texture textureAsset, ulong resRid, EbxAssetEntry assetEntry, bool importArrayAsStrip, ImageFormat inFormat, out bool failed, out string errorMsg, out bool cancelled)
        {
            bool isDDS = inFormat == ImageFormat.DDS;
            MemoryStream memStream = null;
            BlobData blob = new BlobData();
            bool fail = false;
            string errMsg = "";
            failed = fail;
            errorMsg = errMsg;
            cancelled = false;
            Texture workingTexture = textureAsset;

            FrostyTextureImportSettings settings = null;

            if (!isDDS)
            {
                if (workingTexture.Type == TextureType.TT_Cube)
                {
                    if (fileNames.Length != 6)
                    {
                        // Error if not enough textures selected to create a cubemap
                        errorMsg = $"Failed to import. Cubemaps require 6 textures, only {fileNames.Length} were selected";
                        failed = true;
                        return;
                    }

                    // collect textures and assign faces
                    settings = new FrostyTextureImportSettings();
                    for (int i = 0; i < 6; i++)
                    {
                        FrostyTextureCubeItem face = new FrostyTextureCubeItem
                        {
                            Filename = fileNames[i],
                            Face = (FrostyTextureCubeFace)i
                        };
                        settings.Textures.Add(face);
                    }

                    while (true)
                    {
                        if (FrostyImportExportBox.Show("Import Textures", FrostyImportExportType.Import, settings) == MessageBoxResult.Cancel)
                        {
                            cancelled = true;
                            return;
                        }

                        bool[] faceExists = new bool[6];
                        foreach (FrostyTextureCubeItem item in settings.Textures)
                        {
                            // not duplicate
                            if (faceExists[(int)(item.Face)])
                                break;
                            faceExists[(int)(item.Face)] = true;
                        }

                        // exit loop if all faces exist
                        if (faceExists.All((bool a) => a == true))
                            break;

                        FrostyMessageBox.Show("There was a duplicate or missing face defined, please try again", "Frosty Editor", MessageBoxButton.OK);
                    }

                    // sort the textures based on cube face
                    settings.Textures.Sort((a, b) => (((FrostyTextureCubeItem)a).Face < ((FrostyTextureCubeItem)b).Face) ? -1 : 1);
                }
                else if (workingTexture.Type == TextureType.TT_2dArray || workingTexture.Type == TextureType.TT_3d)
                {
                    // collect textures and assign slices
                    settings = new FrostyTextureImportSettings();
                    for (int i = 0; i < fileNames.Length; i++)
                    {
                        FrostyTextureArrayItem slice = new FrostyTextureArrayItem
                        {
                            Filename = fileNames[i],
                            Slice = i
                        };
                        settings.Textures.Add(slice);
                    }

                    while (true)
                    {
                        if (FrostyImportExportBox.Show("Import Textures", FrostyImportExportType.Import, settings) == MessageBoxResult.Cancel)
                        {
                            cancelled = true;
                            return;
                        }

                        bool[] faceExists = new bool[settings.Textures.Count];
                        foreach (FrostyTextureArrayItem item in settings.Textures)
                        {
                            // within valid range
                            if (item.Slice >= faceExists.Length || item.Slice < 0)
                                break;
                            // not duplicate
                            if (faceExists[item.Slice])
                                break;
                            faceExists[item.Slice] = true;
                        }

                        // exit loop if all faces exist
                        if (faceExists.All((bool a) => a == true))
                            break;

                        FrostyMessageBox.Show("There was a duplicate or missing slice defined, please try again", "Frosty Editor", MessageBoxButton.OK);
                    }

                    // sort the textures based on slice
                    settings.Textures.Sort((a, b) => (((FrostyTextureArrayItem)a).Slice < ((FrostyTextureArrayItem)b).Slice) ? -1 : 1);
                }
            }


            FrostyTaskWindow.Show("Importing Texture", "", (task) =>
            {
                if (isDDS) // Already DDS
                {
                    // DDS is simple enough, hardcoded to only use the first selected file as DDS supports arrays in its spec
                    byte[] buf = NativeReader.ReadInStream(new FileStream(fileNames[0], FileMode.Open, FileAccess.Read));
                    memStream = new MemoryStream(buf);
                }
                else // Convert to DDS internally first
                {
                    TextureImportOptions importOptions = new TextureImportOptions
                    {
                        type = workingTexture.Type,
                        format = TextureUtils.ToShaderFormat(workingTexture.PixelFormat, (workingTexture.Flags & TextureFlags.SrgbGamma) != 0),
                        generateMipmaps = workingTexture.MipCount > 1,
                        mipmapsFilter = 0,
                        resizeTexture = false,
                        resizeFilter = 0,
                        resizeHeight = 0,
                        resizeWidth = 0
                    };

                    if (workingTexture.Type == TextureType.TT_2d)
                    {
                        // one image to one DDS
                        byte[] buf = NativeReader.ReadInStream(new FileStream(fileNames[0], FileMode.Open, FileAccess.Read));
                        FrostyTextureEditor.ConvertImageToDDS(buf, buf.Length, inFormat, importOptions, ref blob);
                    }
                    else
                    {
                        string[] fileNamesSorted = new string[settings.Textures.Count];
                        for (int i = 0; i < settings.Textures.Count; i++)
                            fileNamesSorted[i] = settings.Textures[i].Filename;

                        // multiple images to one DDS
                        byte[] buf = new byte[0];
                        long[] sizes = new long[fileNamesSorted.Length];

                        for (int i = 0; i < fileNames.Length; i++)
                        {
                            byte[] tmpBuf = NativeReader.ReadInStream(new FileStream(fileNamesSorted[i], FileMode.Open, FileAccess.Read));
                            sizes[i] = tmpBuf.Length;

                            Array.Resize<byte>(ref buf, buf.Length + tmpBuf.Length);
                            Array.Copy(tmpBuf, 0, buf, buf.Length - tmpBuf.Length, tmpBuf.Length);
                        }

                        FrostyTextureEditor.ConvertImagesToDDS(buf, sizes, sizes.Length, inFormat, importOptions, ref blob);
                    }
                    memStream = new MemoryStream(blob.Data);
                }

                Process(memStream, ref workingTexture, resRid, assetEntry, out fail, out errMsg);

                FrostyTextureEditor.ReleaseBlob(blob);
            });

            textureAsset = workingTexture;
            failed = fail;
            errorMsg = errMsg;
        }

        public void Process(MemoryStream memStream, ref Texture textureAsset, ulong resRid, EbxAssetEntry assetEntry, out bool failed, out string errorMsg)
        {
            bool fail = false;
            string errMsg = "";
            TextureUtils.DDSHeader header = new TextureUtils.DDSHeader();

            using (NativeReader reader = new NativeReader(memStream))
            {
                if (header.Read(reader))
                {
                    TextureType type = TextureType.TT_2d;
                    if (header.HasExtendedHeader)
                    {
                        if (header.ExtendedHeader.resourceDimension == D3D11.ResourceDimension.Texture2D)
                        {
                            if ((header.ExtendedHeader.miscFlag & 4) != 0)
                                type = TextureType.TT_Cube;
                            else if (header.ExtendedHeader.arraySize > 1)
                                type = TextureType.TT_2dArray;
                        }
                        else if (header.ExtendedHeader.resourceDimension == D3D11.ResourceDimension.Texture3D)
                            type = TextureType.TT_3d;
                    }
                    else
                    {
                        if ((header.dwCaps2 & TextureUtils.DDSCaps2.CubeMap) != 0)
                            type = TextureType.TT_Cube;
                        else if ((header.dwCaps2 & TextureUtils.DDSCaps2.Volume) != 0)
                            type = TextureType.TT_3d;
                    }

                    if (type != textureAsset.Type)
                    {
                        errMsg = $"Imported texture must match original texture type. Original texture type is {textureAsset.Type}. Imported texture type is {type}";
                        fail = true;
                    }

                    bool textureIsSRGB = textureAsset.PixelFormat.Contains("SRGB") || ((textureAsset.Flags & TextureFlags.SrgbGamma) != 0);

                    if (!fail)
                    {
                        if (textureAsset.Type == TextureType.TT_2dArray)
                        {
                            // @todo: additional validation
                        }
                        else if (textureAsset.Type == TextureType.TT_3d)
                        {
                            // @todo: additional validation
                        }
                        else if (textureAsset.Type == TextureType.TT_Cube)
                        {
                            // @todo: additional validation
                        }

                        if (!fail && textureIsSRGB)
                        {
                            // dont allow changing of SRGB to non SRGB
                            if (!header.HasExtendedHeader || !header.ExtendedHeader.dxgiFormat.ToString().ToLower().Contains("srgb"))
                            {
                                errMsg = "Format must be SRGB variant";
                                fail = true;
                            }
                        }
                    }

                    GetPixelFormat(header, textureAsset, out string pixelFormat, out TextureFlags baseFlags);

                    // make sure texture mip maps can be generated
                    if (TextureUtils.IsCompressedFormat(pixelFormat) && textureAsset.MipCount > 1)
                    {
                        if (header.dwWidth % 4 != 0 || header.dwHeight % 4 != 0)
                        {
                            errMsg = "Texture width/height must be divisible by 4 for compressed formats requiring mip maps";
                            fail = true;
                        }
                    }

                    if (!fail)
                    {
                        ResAssetEntry resEntry = App.AssetManager.GetResEntry(resRid);
                        ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(textureAsset.ChunkId);

                        // revert any modifications
                        //App.AssetManager.RevertAsset(resEntry, dataOnly: true);

                        byte[] buffer = new byte[reader.Length - reader.Position];
                        reader.Read(buffer, 0, (int)(reader.Length - reader.Position));

                        ushort depth = (header.HasExtendedHeader && header.ExtendedHeader.resourceDimension == D3D11.ResourceDimension.Texture2D)
                                ? (ushort)header.ExtendedHeader.arraySize
                                : (ushort)1;

                        // cubemaps are just 6 slice arrays
                        if ((header.dwCaps2 & TextureUtils.DDSCaps2.CubeMap) != 0)
                            depth = 6;
                        if ((header.dwCaps2 & TextureUtils.DDSCaps2.Volume) != 0)
                            depth = (ushort)header.dwDepth;

                        Texture newTextureAsset = new Texture(textureAsset.Type, pixelFormat, (ushort)header.dwWidth, (ushort)header.dwHeight, depth) { FirstMip = textureAsset.FirstMip };
                        if (header.dwMipMapCount <= textureAsset.FirstMip)
                            newTextureAsset.FirstMip = 0;

                        newTextureAsset.TextureGroup = textureAsset.TextureGroup;
                        newTextureAsset.CalculateMipData((byte)header.dwMipMapCount, TextureUtils.GetFormatBlockSize(pixelFormat), TextureUtils.IsCompressedFormat(pixelFormat), (uint)buffer.Length);
                        newTextureAsset.Flags = baseFlags;

                        // just copy old flags (minus gamma) to new texture
                        TextureFlags oldFlags = textureAsset.Flags & ~(TextureFlags.SrgbGamma);
                        newTextureAsset.Flags |= oldFlags;

                        // rejig mips/slices
                        if (newTextureAsset.Type == TextureType.TT_Cube || newTextureAsset.Type == TextureType.TT_2dArray)
                        {
                            MemoryStream srcStream = new MemoryStream(buffer);
                            MemoryStream dstStream = new MemoryStream();

                            int sliceCount = 6;
                            if (newTextureAsset.Type == TextureType.TT_2dArray)
                                sliceCount = newTextureAsset.Depth;

                            // Need to rejig order of faces and mips
                            uint[] mipOffsets = new uint[newTextureAsset.MipCount];
                            for (int i = 0; i < newTextureAsset.MipCount - 1; i++)
                                mipOffsets[i + 1] = mipOffsets[i] + (uint)(newTextureAsset.MipSizes[i] * sliceCount);

                            byte[] tmpBuf = new byte[newTextureAsset.MipSizes[0]];

                            for (int slice = 0; slice < sliceCount; slice++)
                            {
                                for (int mip = 0; mip < newTextureAsset.MipCount; mip++)
                                {
                                    int mipSize = (int)newTextureAsset.MipSizes[mip];

                                    srcStream.Read(tmpBuf, 0, mipSize);
                                    dstStream.Position = mipOffsets[mip] + (mipSize * slice);
                                    dstStream.Write(tmpBuf, 0, mipSize);
                                }
                            }

                            buffer = dstStream.ToArray();
                        }

                        // modify chunk
                        if (ProfilesLibrary.MustAddChunks && chunkEntry.Bundles.Count == 0 && !chunkEntry.IsAdded)
                        {
                            // DAI requires adding new chunks if in chunks bundle
                            textureAsset.ChunkId = App.AssetManager.AddChunk(buffer, null, (newTextureAsset.Flags & TextureFlags.OnDemandLoaded) != 0 ? null : newTextureAsset);
                            chunkEntry = App.AssetManager.GetChunkEntry(textureAsset.ChunkId);
                        }
                        else
                        {
                            // other games just modify
                            App.AssetManager.ModifyChunk(textureAsset.ChunkId, buffer, ((newTextureAsset.Flags & TextureFlags.OnDemandLoaded) != 0 || newTextureAsset.Type != TextureType.TT_2d) ? null : newTextureAsset);
                        }

                        for (int i = 0; i < 4; i++)
                            newTextureAsset.Unknown3[i] = textureAsset.Unknown3[i];
                        newTextureAsset.SetData(textureAsset.ChunkId, App.AssetManager);
                        newTextureAsset.AssetNameHash = (uint)Fnv1.HashString(resEntry.Name);

                        textureAsset.Dispose();
                        textureAsset = newTextureAsset;

                        // modify resource
                        App.AssetManager.ModifyRes(resRid, newTextureAsset);

                        // update linkage
                        resEntry.LinkAsset(chunkEntry);
                        assetEntry.LinkAsset(resEntry);
                    }
                }
                else
                {
                    errMsg = "Invalid DDS format";
                    fail = true;
                }
            }

            failed = fail;
            errorMsg = errMsg;
        }

        private void GetPixelFormat(TextureUtils.DDSHeader header, Texture textureAsset, out string pixelFormat, out TextureFlags flags)
        {
            pixelFormat = "Unknown";
            flags = 0;

            if (ProfilesLibrary.DataVersion == (int)ProfileVersion.DragonAgeInquisition || ProfilesLibrary.DataVersion == (int)ProfileVersion.Battlefield4 || ProfilesLibrary.DataVersion == (int)ProfileVersion.PlantsVsZombiesGardenWarfare)
            {
                // DXT1
                if (header.ddspf.dwFourCC == 0x31545844)
                {
                    pixelFormat = "BC1_UNORM";
                    if (textureAsset.PixelFormat.Contains("Normal"))
                        pixelFormat = textureAsset.PixelFormat;
                    else if (textureAsset.PixelFormat.StartsWith("BC1A"))
                        pixelFormat = textureAsset.PixelFormat;
                }

                // ATI2 or BC5U
                else if (header.ddspf.dwFourCC == 0x32495441 || header.ddspf.dwFourCC == 0x55354342)
                    pixelFormat = "NormalDXN";

                // DXT3
                else if (header.ddspf.dwFourCC == 0x33545844)
                    pixelFormat = "BC2_UNORM";

                // DXT5
                else if (header.ddspf.dwFourCC == 0x35545844)
                    pixelFormat = "BC3_UNORM";

                // ATI1
                else if (header.ddspf.dwFourCC == 0x31495441)
                    pixelFormat = "BC3A_UNORM";

                // All others
                else if (header.HasExtendedHeader)
                {
                    switch (header.ExtendedHeader.dxgiFormat)
                    {
                        case SharpDX.DXGI.Format.R32G32B32A32_Float: pixelFormat = "ARGB32F"; break;
                        case SharpDX.DXGI.Format.R9G9B9E5_Sharedexp: pixelFormat = "R9G9B9E5F"; break;
                        case SharpDX.DXGI.Format.R8_UNorm: pixelFormat = "L8"; break;
                        case SharpDX.DXGI.Format.R16_UNorm: pixelFormat = "L16"; break;
                        case SharpDX.DXGI.Format.R8G8B8A8_UNorm: pixelFormat = "ARGB8888"; break;
                        case SharpDX.DXGI.Format.BC1_UNorm:
                            pixelFormat = "BC1_UNORM";
                            if (textureAsset.PixelFormat.Contains("Normal") || textureAsset.PixelFormat.StartsWith("BC1A"))
                                pixelFormat = textureAsset.PixelFormat;
                            break;
                        case SharpDX.DXGI.Format.BC2_UNorm: pixelFormat = "BC2_UNORM"; break;
                        case SharpDX.DXGI.Format.BC3_UNorm: pixelFormat = "BC3_UNORM"; break;
                        case SharpDX.DXGI.Format.BC5_UNorm: pixelFormat = "NormalDXN"; break;
                        case SharpDX.DXGI.Format.BC7_UNorm: pixelFormat = "BC7_UNORM"; break;
                        case SharpDX.DXGI.Format.BC1_UNorm_SRgb: pixelFormat = "BC1_UNORM"; flags = TextureFlags.SrgbGamma; break;
                        case SharpDX.DXGI.Format.BC2_UNorm_SRgb: pixelFormat = "BC2_UNORM"; flags = TextureFlags.SrgbGamma; break;
                        case SharpDX.DXGI.Format.BC3_UNorm_SRgb:
                            pixelFormat = (textureAsset.PixelFormat == "BC3A_UNORM") ? textureAsset.PixelFormat : "BC3_UNORM";
                            flags = TextureFlags.SrgbGamma;
                            break;
                        case SharpDX.DXGI.Format.BC7_UNorm_SRgb: pixelFormat = "BC7_UNORM"; flags = TextureFlags.SrgbGamma; break;
                    }
                }
            }
            else
            {
                // Newer format PixelFormats
                if (header.ddspf.dwFourCC == 0)
                {
                    if (header.ddspf.dwRBitMask == 0x000000FF && header.ddspf.dwGBitMask == 0x0000FF00 && header.ddspf.dwBBitMask == 0x00FF0000 && header.ddspf.dwABitMask == 0xFF000000)
                        pixelFormat = "R8G8B8A8_UNORM";
                }

                // DXT1
                else if (header.ddspf.dwFourCC == 0x31545844)
                {
                    pixelFormat = "BC1_UNORM";
                    if (textureAsset.PixelFormat == "BC1A_UNORM")
                        pixelFormat = "BC1A_UNORM";
                }

                // DXT5
                else if (header.ddspf.dwFourCC == 0x35545844)
                    pixelFormat = "BC3_UNORM";

                // ATI1
                else if (header.ddspf.dwFourCC == 0x31495441)
                    pixelFormat = "BC4_UNORM";

                // ATI2 or BC5U
                else if (header.ddspf.dwFourCC == 0x32495441 || header.ddspf.dwFourCC == 0x55354342)
                    pixelFormat = "BC5_UNORM";

                // All others
                else if (header.HasExtendedHeader)
                {
                    if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC1_UNorm)
                    {
                        pixelFormat = "BC1_UNORM";
                        if (textureAsset.PixelFormat == "BC1A_UNORM")
                            pixelFormat = "BC1A_UNORM";
                    }
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC3_UNorm)
                        pixelFormat = "BC3_UNORM";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC4_UNorm)
                        pixelFormat = "BC4_UNORM";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC5_UNorm)
                        pixelFormat = "BC5_UNORM";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC1_UNorm_SRgb && textureAsset.PixelFormat == "BC1A_SRGB")
                        pixelFormat = "BC1A_SRGB";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC1_UNorm_SRgb)
                        pixelFormat = "BC1_SRGB";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC3_UNorm_SRgb)
                        pixelFormat = "BC3_SRGB";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC6H_Uf16)
                        pixelFormat = "BC6U_FLOAT";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC7_UNorm)
                        pixelFormat = "BC7_UNORM";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.BC7_UNorm_SRgb)
                        pixelFormat = "BC7_SRGB";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.R8_UNorm)
                        pixelFormat = "R8_UNORM";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.R16G16B16A16_Float)
                        pixelFormat = "R16G16B16A16_FLOAT";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.R32G32B32A32_Float)
                        pixelFormat = "R32G32B32A32_FLOAT";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.R9G9B9E5_Sharedexp)
                        pixelFormat = "R9G9B9E5_FLOAT";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.R8G8B8A8_UNorm)
                        pixelFormat = "R8G8B8A8_UNORM";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb)
                        pixelFormat = "R8G8B8A8_SRGB";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.R10G10B10A2_UNorm)
                        pixelFormat = "R10G10B10A2_UNORM";
                    else if (header.ExtendedHeader.dxgiFormat == SharpDX.DXGI.Format.R16_UNorm)
                    {
                        pixelFormat = "R16_UNORM";
                        if (textureAsset.PixelFormat == "D16_UNORM")
                            pixelFormat = "D16_UNORM";
                    }
                }
            }
        }
    }
}
