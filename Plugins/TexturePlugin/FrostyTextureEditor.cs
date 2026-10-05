using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using D3D11 = SharpDX.Direct3D11;
using Frosty.Controls;
using FrostySdk.Interfaces;
using Frosty.Core.Viewport;
using System.Collections.Generic;
using System.Linq;
using Frosty.Hash;
using Frosty.Core.Controls;
using Frosty.Core;
using Frosty.Core.Windows;
using Frosty.Core.Screens;
using System.Runtime.InteropServices;

namespace TexturePlugin
{
    #region -- Import / Export --
    public struct BlobData
    {
        public byte[] Data
        {
            get
            {
                byte[] outBuf = new byte[size];
                Marshal.Copy(data, outBuf, 0, (int)size);
                return outBuf;
            }
        }
        private IntPtr data;
        private long size;
    }

    public enum ImageFormat
    {
        PNG,
        TGA,
        HDR,
        DDS
    }

    public struct TextureImportOptions
    {
        public TextureType type;
        public SharpDX.DXGI.Format format;
        public bool generateMipmaps;
        public int mipmapsFilter;
        public bool resizeTexture;
        public int resizeFilter;
        public int resizeWidth;
        public int resizeHeight;
    };
    #endregion

    [TemplatePart(Name = PART_Renderer, Type = typeof(FrostyRenderImage))]
    [TemplatePart(Name = PART_TextureFormat, Type = typeof(TextBlock))]
    //[TemplatePart(Name = PART_TextureGroup, Type = typeof(Label))]
    [TemplatePart(Name = PART_DebugText, Type = typeof(TextBox))]
    [TemplatePart(Name = PART_MipsComboBox, Type = typeof(ComboBox))]
    [TemplatePart(Name = PART_SliceComboBox, Type = typeof(ComboBox))]
    [TemplatePart(Name = PART_SliceToolBarItem, Type = typeof(Border))]
    public class FrostyTextureEditor : FrostyAssetEditor
    {
        [DllImport("thirdparty/dxtex.dll", EntryPoint = "ConvertDDSToImage")]
        public static extern void ConvertDDSToImage(byte[] pData, long iDataSize, ImageFormat format, ref BlobData pOutData);
        [DllImport("thirdparty/dxtex.dll", EntryPoint = "ConvertDDSToImages")]
        public static extern void ConvertDDSToImages(byte[] pData, long iDataSize, ImageFormat format,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)]
            ref BlobData[] pOutDatas, int pOutCount);
        [DllImport("thirdparty/dxtex.dll", EntryPoint = "ConvertImageToDDS")]
        public static extern void ConvertImageToDDS(byte[] pData, long iDataSize, ImageFormat origFormat, TextureImportOptions options, ref BlobData pOutData);
        [DllImport("thirdparty/dxtex.dll", EntryPoint = "ConvertImagesToDDS")]
        public static extern void ConvertImagesToDDS(byte[] pData, long[] iDataSize, long iCount, ImageFormat origFormat, TextureImportOptions options, ref BlobData pOutData);
        [DllImport("thirdparty/dxtex.dll", EntryPoint = "ReleaseBlob")]
        public static extern void ReleaseBlob(BlobData pData);

        private const string PART_Renderer = "PART_Renderer";
        private const string PART_TextureFormat = "PART_TextureFormat";
        //private const string PART_TextureGroup = "PART_TextureGroup";
        private const string PART_DebugText = "PART_DebugText";
        private const string PART_MipsComboBox = "PART_MipsComboBox";
        private const string PART_SliceComboBox = "PART_SliceComboBox";
        private const string PART_SliceToolBarItem = "PART_SliceToolBarItem";

        #region -- GridVisible --
        public static readonly DependencyProperty GridVisibleProperty = DependencyProperty.Register("GridVisible", typeof(bool), typeof(FrostyTextureEditor), new FrameworkPropertyMetadata(true));
        public bool GridVisible
        {
            get => (bool)GetValue(GridVisibleProperty);
            set => SetValue(GridVisibleProperty, value);
        }
        #endregion

        private FrostyViewport renderer;
        private TextBlock textureFormatText;
        //private Label textureGroupText;
        private Texture textureAsset;
        private TextBox debugTextBox;
        private ComboBox mipsComboBox;
        private ComboBox sliceComboBox;
        private Border sliceToolBarItem;
        private bool textureIsSRGB;

        static FrostyTextureEditor()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FrostyTextureEditor), new FrameworkPropertyMetadata(typeof(FrostyTextureEditor)));
        }

        public FrostyTextureEditor(ILogger inLogger)
            : base(inLogger)
        {
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            renderer = GetTemplateChild(PART_Renderer) as FrostyViewport;
            if (renderer != null)
            {
                ulong resRid = ((dynamic)RootObject).Resource;
                try 
                {
                    textureAsset = App.AssetManager.GetResAs<Texture>(App.AssetManager.GetResEntry(resRid));
                    textureIsSRGB = textureAsset.PixelFormat.Contains("SRGB") || ((textureAsset.Flags & TextureFlags.SrgbGamma) != 0);
                    renderer.Screen = new TextureScreen(textureAsset);
                }
                catch 
                {
                    renderer.Screen = new TextureScreen();
                    return;
                }
            }

            textureFormatText = GetTemplateChild(PART_TextureFormat) as TextBlock;
            debugTextBox = GetTemplateChild(PART_DebugText) as TextBox;

            mipsComboBox = GetTemplateChild(PART_MipsComboBox) as ComboBox;
            mipsComboBox.SelectionChanged += MipsComboBox_SelectionChanged;

            sliceComboBox = GetTemplateChild(PART_SliceComboBox) as ComboBox;
            sliceComboBox.SelectionChanged += SliceComboBox_SelectionChanged;

            sliceToolBarItem = GetTemplateChild(PART_SliceToolBarItem) as Border;
            if (textureAsset.Depth == 1)
                sliceToolBarItem.Visibility = Visibility.Collapsed;

            UpdateControls();
        }

        public override List<ToolbarItem> RegisterToolbarItems()
        {
            return new List<ToolbarItem>()
            {
                new ToolbarItem("Export", "Export Texture", "Images/Export.png", new RelayCommand((object state) => { ExportButton_Click(this, new RoutedEventArgs(), false); })),
                new ToolbarItem("Export as Strip", "Export Texture", "Images/Export.png", new RelayCommand((object state) => { ExportButton_Click(this, new RoutedEventArgs(), true); })),
                new ToolbarItem("Import", "Import Texture", "Images/Import.png", new RelayCommand((object state) => { ImportButton_Click(this, new RoutedEventArgs()); })),
            };
        }

        private void SliceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TextureScreen screen = renderer.Screen as TextureScreen;
            screen.SliceLevel = sliceComboBox.SelectedIndex;
        }

        private void MipsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TextureScreen screen = renderer.Screen as TextureScreen;
            screen.MipLevel = mipsComboBox.SelectedIndex;
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            FrostyOpenFileDialog ofd = new FrostyOpenFileDialog("Import Texture", "PNG (*.png)|*.png|TGA (*.tga)|*.tga|HDR (*.hdr)|*.hdr|DDS (*.dds)|*.dds", "Texture");
            if (textureAsset.Type != TextureType.TT_2d)
            {
                ofd.Multiselect = true;
                ofd.Title = "Import Textures";
            }

            if (ofd.ShowDialog())
            {
                EbxAssetEntry assetEntry = AssetEntry as EbxAssetEntry;
                ulong resRid = ((dynamic)RootObject).Resource;

                TextureImporter importer = new TextureImporter();
                importer.Import(ofd.FileNames, ref textureAsset, resRid, assetEntry, false, (ImageFormat)ofd.FilterIndex - 1, out bool bFailed, out string errorMsg, out bool cancelled);

                string message = "Texture " + ofd.FileName + " failed to import: " + errorMsg;
                if (!bFailed && !cancelled)
                {
                    TextureScreen screen = renderer.Screen as TextureScreen;
                    screen.TextureAsset = textureAsset;

                    UpdateControls();
                    InvokeOnAssetModified();

                    message = "Texture " + ofd.FileName + " successfully imported";
                }

                if (!cancelled)
                {
                    logger.Log(message);
                }
            }
        }

        private void UpdateControls()
        {
            float newWidth = textureAsset.Width;
            float newHeight = textureAsset.Height;

            if (newWidth > 2048)
            {
                newWidth = 2048;
                newHeight = (newHeight * (newWidth / textureAsset.Width));
            }
            if (newHeight > 2048)
            {
                newHeight = 2048;
                newWidth = (newWidth * (newHeight / textureAsset.Height));
            }

            renderer.Width = newWidth;
            renderer.Height = newHeight;

            string pf = textureAsset.PixelFormat;
            if (pf.StartsWith("BC") && textureAsset.Flags.HasFlag(TextureFlags.SrgbGamma))
                pf = pf.Replace("UNORM", "SRGB");

            textureFormatText.Text = pf;
            //textureGroupText.Content = textureAsset.TextureGroup;
            //debugTextBox.Text = textureAsset.ToDebugString();

            ushort width = textureAsset.Width;
            ushort height = textureAsset.Height;

            mipsComboBox.Items.Clear();
            for (int i = 0; i < textureAsset.MipCount; i++)
            {
                mipsComboBox.Items.Add(string.Format("{0}x{1}", width, height));

                width >>= 1;
                height >>= 1;
            }
            mipsComboBox.SelectedIndex = 0;

            if (textureAsset.Depth > 1)
            {
                sliceComboBox.ItemsSource = null;
                if (textureAsset.Type == TextureType.TT_Cube)
                {
                    // give cube maps actual names for the slices
                    string[] cubeItems = new string[] { "X+", "X-", "Y+", "Y-", "Z+", "Z-" };
                    sliceComboBox.ItemsSource = cubeItems;
                }
                else
                {
                    // other textures just have numbered slices
                    string[] sliceItems = new string[textureAsset.Depth];
                    for (int i = 0; i < textureAsset.Depth; i++)
                        sliceItems[i] = i.ToString();
                    sliceComboBox.ItemsSource = sliceItems;
                }
                sliceComboBox.SelectedIndex = 0;
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e, bool exportAsStrip)
        {
            ImageFormat format = ImageFormat.PNG;
            bool bResult = false;

            FrostySaveFileDialog sfd = new FrostySaveFileDialog("Export Texture", "PNG (*.png)|*.png|TGA (*.tga)|*.tga|HDR (*.hdr)|*.hdr|DDS (*.dds)|*.dds", "Texture", AssetEntry.Filename, false);
            while (true)
            {
                string initialDir = sfd.InitialDirectory;
                bResult = sfd.ShowDialog();

                if (bResult)
                {
                    format = (ImageFormat)(sfd.FilterIndex - 1);

                    FileInfo fi = new FileInfo(sfd.FileName);
                    sfd.InitialDirectory = fi.DirectoryName;

                    if (textureAsset.Type == TextureType.TT_2d || format == ImageFormat.DDS)
                    {
                        if (fi.Exists)
                        {
                            if (FrostyMessageBox.Show(sfd.FileName + " already exists\r\nDo you want to replace it?", "Frosty Editor", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                                break;
                        }
                        else break;
                    }
                    else if (textureAsset.Type == TextureType.TT_2dArray || textureAsset.Type == TextureType.TT_Cube || textureAsset.Type == TextureType.TT_3d)
                    {
                        string[] filenames = null;
                        if (textureAsset.Type == TextureType.TT_Cube)
                        {
                            filenames = new string[6] { "px", "nx", "py", "ny", "pz", "nz" };
                            for (int i = 0; i < 6; i++)
                                filenames[i] = string.Format("{0}_{1}{2}", fi.FullName.Replace(fi.Extension, ""), filenames[i], fi.Extension);
                        }
                        else
                        {
                            filenames = new string[textureAsset.SliceCount];
                            for (int i = 0; i < textureAsset.SliceCount; i++)
                                filenames[i] = string.Format("{0}_{1}{2}", fi.FullName.Replace(fi.Extension, ""), i.ToString("D3"), fi.Extension);
                        }

                        bool bExists = false;
                        foreach(string filename in filenames)
                        {
                            if (File.Exists(filename))
                                bExists |= true;
                        }

                        if (bExists)
                        {
                            if (FrostyMessageBox.Show("One of more of the requested files already exists\r\nDo you want to replace them?", "Frosty Editor", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                                break;
                        }
                        else break;
                    }
                }
                else break;
            }

            if (!bResult)
                return;

            FrostyTaskWindow.Show("Exporting Texture", AssetEntry.Filename, (task) =>
            {
                string[] filters = new string[] { "*.png", "*.tga", "*.hdr", "*.dds" };

                TextureExporter exporter = new TextureExporter();
                exporter.Export(textureAsset, sfd.FileName, filters[sfd.FilterIndex - 1], exportAsStrip);
            });
            logger.Log("Texture successfully exported to " + sfd.FileName);
        }
    }
}
