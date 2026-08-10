using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Controls.Editors;
using Frosty.Core.Screens;
using Frosty.Core.Viewport;
using Frosty.Core.Windows;
using Frosty.Hash;
using FrostySdk;
using FrostySdk.Attributes;
using FrostySdk.Ebx;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using MeshSetPlugin.Editors;
using MeshSetPlugin.Fbx;
using MeshSetPlugin.Render;
using MeshSetPlugin.Resources;
using MeshSetPlugin.Screens;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MeshSetPlugin
{

    #region -- Settings/Fields --

    [EbxClassMeta(EbxFieldType.Struct)]
    public class MeshSetVariationEntryDetails
    {
        [EbxFieldMeta(EbxFieldType.Pointer, "MeshVariationDatabase")]
        public PointerRef VariationDb { get; set; }
        public int Index { get; set; }
    }

    [DisplayName("Material Variation")]
    [Description("This class shows all the resources that are present on a single variation of the current mesh. This includes the materials, material parameters, textures from both the material itself and any mesh variation databases. It also shows in which mesh variation databases this variation can be found.")]
    [EbxClassMeta(EbxFieldType.Struct)]
    public class MeshSetVariationDetails
    {
        [IsHidden]
        public string Name { get; set; }
        [Editor(typeof(FrostyAutoDisableBooleanEditor))]
        [EbxFieldMeta(0x0, 0, typeof(object), false, 0)]
        public bool Preview { get; set; }
        [EbxFieldMeta(EbxFieldType.Pointer, "ObjectVariation")]
        public PointerRef Variation { get; set; }
        [EbxFieldMeta(0x02, 0, typeof(object), false, 0)]
        public MeshMaterialCollection.Container MaterialCollection { get; set; }
        [EbxFieldMeta(0x02, 0, typeof(object), true, 0)]
        public List<MeshSetVariationEntryDetails> MeshVariationDbs { get; set; } = new List<MeshSetVariationEntryDetails>();
    }

    [EbxClassMeta(EbxFieldType.Struct)]
    public class PreviewMeshData
    {
        [EbxFieldMeta(EbxFieldType.Pointer, "MeshAsset")]
        public PointerRef Mesh { get; set; }
        [EbxFieldMeta(EbxFieldType.Pointer, "ObjectVariation")]
        public PointerRef Variation { get; set; }
        [EbxFieldMeta(EbxFieldType.Struct)]
        public dynamic Transform { get; set; }
        public int MeshId = -1;
        public EbxAsset Asset;

        public PreviewMeshData()
        {
            Transform = TypeLibrary.CreateObject("LinearTransform");
        }
    }

    [EbxClassMeta(EbxFieldType.Struct)]
    public class PreviewLightData
    {
        [EbxFieldMeta(EbxFieldType.Struct)]
        public /* Vec3 */ dynamic Color { get; set; }

        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0, 200000.0f, 10.0f, 100.0f, true)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float Intensity { get; set; } = 1000.0f;

        [EbxFieldMeta(EbxFieldType.Struct)]
        public /* LinearTransform */ dynamic Transform { get; set; }

        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0, 1000.0f, 1.0f, 10.0f, true)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float AttenuationRadius { get; set; } = 1.0f;

        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0, 100.0f, 0.1f, 1.0f, true)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float SphereRadius { get; set; } = 0.0f;

        public int LightId = -1;

        public PreviewLightData()
        {
            Color = TypeLibrary.CreateObject("Vec3");
            Color.x = 1.0f;
            Color.y = 1.0f;
            Color.z = 1.0f;
            Transform = TypeLibrary.CreateObject("LinearTransform");
        }
    }

    [DisplayName("SectionData")]
    [EbxClassMeta(EbxFieldType.Struct)]
    public class PreviewMeshSectionData
    {
        [IsReadOnly]
        [EbxFieldMeta(EbxFieldType.Pointer, "MeshMaterial")]
        public PointerRef MaterialId { get; set; }
        [EbxFieldMeta(EbxFieldType.Boolean)]
        public bool Highlight { get; set; }
        [EbxFieldMeta(EbxFieldType.Boolean)]
        public bool Visible { get; set; }
        [IsReadOnly]
        [EbxFieldMeta(EbxFieldType.Int32)]
        public int MaxBonesPerVertex { get; set; }
        [IsReadOnly]
        [EbxFieldMeta(EbxFieldType.Int32)]
        public int NumUVChannels { get; set; }
        [IsReadOnly]
        [EbxFieldMeta(EbxFieldType.Int32)]
        public int NumColorChannels { get; set; }
        [EbxFieldMeta(EbxFieldType.Array, arrayType: EbxFieldType.CString)]
        public List<CString> AdditionalChannels { get; set; } = new List<CString>();
    }

    [DisplayName("LodData")]
    [EbxClassMeta(EbxFieldType.Struct)]
    public class PreviewMeshLodData
    {
        //[EbxFieldMeta(EbxFieldType.Boolean)]
        //[Editor("FrostyAutoDisableBooleanEditor")]
        //public bool Preview { get; set; }
        [IsReadOnly]
        [EbxFieldMeta(EbxFieldType.CString)]
        public CString Name { get; set; }

        [EbxFieldMeta(EbxFieldType.Struct)]
        public List<PreviewMeshSectionData> Sections { get; set; } = new List<PreviewMeshSectionData>();
    }

    [DisplayName("Mesh Settings")]
    [EbxClassMeta(EbxFieldType.Struct)]
    public class MeshSetMeshSettings
    {
        [Category("Mesh")]
        [EbxFieldMeta(EbxFieldType.Struct, "", EbxFieldType.Inherited)]
        public MeshAssetBoundingBox BoundingBox { get; set; } = new MeshAssetBoundingBox();

        [Category("Mesh")]
        [EbxFieldMeta(EbxFieldType.Struct)]
        public List<PreviewMeshLodData> Lods { get; set; } = new List<PreviewMeshLodData>();
    }

    [DisplayName("BoundingBox")]
    [EbxClassMeta(EbxFieldType.Struct)]
    public class MeshAssetBoundingBox
    {
        [IsReadOnly]
        [EbxFieldMeta(EbxFieldType.Struct, "", EbxFieldType.Inherited)]
        public Vec3 Minimum { get; set; }

        [IsReadOnly]
        [EbxFieldMeta(EbxFieldType.Struct, "", EbxFieldType.Inherited)]
        public Vec3 Maximum { get; set; }
    }

    [DisplayName("Preview Settings")]
    [EbxClassMeta(EbxFieldType.Struct)]
    public class MeshSetPreviewSettings
    {
        // Lights

        [Category("Lights")]
        [DisplayName("Sun Position")]
        [EbxFieldMeta(EbxFieldType.Struct)]
        public dynamic SunPosition { get; set; }

        [Category("Lights")]
        [DisplayName("Sun Intensity")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0, 2000000.0f, 100.0f, 1000.0f, true)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float SunIntensity { get; set; } = 1000.0f;

        [Category("Lights")]
        [DisplayName("Sun Angular Radius")]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float SunAngularRadius { get; set; } = 0.029f;

        [Category("Lights")]
        [DisplayName("Additional Lights")]
        [EbxFieldMeta(EbxFieldType.Struct)]
        public List<PreviewLightData> PreviewLights { get; set; } = new List<PreviewLightData>();

        // Meshes

        [Category("Meshes")]
        [DisplayName("Additional Meshes")]
        [EbxFieldMeta(EbxFieldType.Struct)]
        public List<PreviewMeshData> PreviewMeshes { get; set; } = new List<PreviewMeshData>();

        // Scene

        [Category("Scene")]
        [DisplayName("Light Probe Texture")]
        [EbxFieldMeta(EbxFieldType.Pointer, "TextureAsset")]
        public PointerRef LightProbeTexture { get; set; }

        [Category("Scene")]
        [DisplayName("Light Probe Intensity")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0, 1000.0f, 0.01f, 0.1f, true)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float LightProbeIntensity { get; set; } = 1.0f;

        [Category("Scene")]
        [DisplayName("Color Lookup Table")]
        [EbxFieldMeta(EbxFieldType.Pointer, "TextureAsset")]
        public PointerRef ColorLookupTable { get; set; }

        // Camera

        [Category("Camera")]
        [DisplayName("Speed Multiplier")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(1.0f, 8.0f, 1.0f, 1.0f, true)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float CameraSpeedMultiplier { get; set; } = 1.0f;

#if FROSTY_DEVELOPER
        // Camera
        [Category("Camera (Developer)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(-10.0f, 20.0f, 1.0f, 2.0f, true)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float MinEV100 { get; set; } = 8.0f;

        [Category("Camera")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(-10.0f, 20.0f, 1.0f, 2.0f, true)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float MaxEV100 { get; set; } = 20.0f;
#endif

#if FROSTY_DEVELOPER
        [Category("Meshes (Developer)")]
        [Editor(typeof(FrostyImagePathEditor))]
        [Extension("dat", "Anim Files")]
        public string Animation { get; set; } = "";
#endif

#if FROSTY_DEVELOPER
        [Category("Shadows (Developer)")]
        public int iDepthBias { get; set; } = 100;

        [Category("Shadows")]
        public float fSlopeScaledDepthBias { get; set; } = 5;

        [Category("Shadows")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0, 1.0f, 0.0000001f, 0.000001f, false)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float fDistanceBiasMin { get; set; } = 0.00000001f;

        [Category("Shadows")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0, 1.0f, 0.0000001f, 0.000001f, false)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float fDistanceBiasFactor { get; set; } = 0.00000001f;

        [Category("Shadows")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0, 1000000.0f, 1.0f, 10.0f, false)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float fDistanceBiasThreshold { get; set; } = 700.0f;

        [Category("Shadows")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0, 30000.0f, 0.1f, 1.0f, false)]
        [EbxFieldMeta(EbxFieldType.Float32)]
        public float fDistanceBiasPower { get; set; } = 0.3f;
#endif

        public MeshSetPreviewSettings()
        {
            SunPosition = TypeLibrary.CreateObject("Vec3");
            SunPosition.x = 10.0f;
            SunPosition.y = 20.0f;
            SunPosition.z = 20.0f;
        }
    } 
    #endregion

    public class MeshSetMaterialDetails
    {
        public object TextureParameters { get; set; } = new List<dynamic>();
    }

    class VariationToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            uint variation = (uint)value;
            return variation.ToString("X8");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return uint.TryParse((string)value, NumberStyles.HexNumber, null, out uint variation) ? variation : 0;
        }
    }

    class MeshSetElementItemToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            FrostyMeshSetEditor.MeshSetElementItem item = (FrostyMeshSetEditor.MeshSetElementItem)value;
            return (item.Name != "") ? item.Name : "<<unnamed>>";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    [TemplatePart(Name = PART_LodComboBox, Type = typeof(ComboBox))]
    [TemplatePart(Name = PART_RenderModeComboBox, Type = typeof(ComboBox))]
    [TemplatePart(Name = PART_Renderer, Type = typeof(FrostyRenderImage))]
    [TemplatePart(Name = PART_DebugTextBox, Type = typeof(TextBox))]
    [TemplatePart(Name = PART_VariationsListBox, Type = typeof(ListBox))]
    [TemplatePart(Name = PART_Details, Type = typeof(FrostyPropertyGrid))]
    [TemplatePart(Name = PART_ExtractMaterialInfoButton, Type = typeof(Button))]
    [TemplatePart(Name = PART_VariationComboBox, Type = typeof(ComboBox))]
    [TemplatePart(Name = PART_PreviewSettings, Type = typeof(FrostyPropertyGrid))]
    [TemplatePart(Name = PART_MeshSettings, Type = typeof(FrostyPropertyGrid))]
    [TemplatePart(Name = PART_MeshTabContent, Type = typeof(FrostyDetachedTabControl))]
    [TemplatePart(Name = PART_MeshTabControl, Type = typeof(FrostyTabControl))]
#if FROSTY_DEVELOPER
    [TemplatePart(Name = PART_RenderDocButton, Type = typeof(Button))]
#endif

    public class FrostyMeshSetEditor : FrostyAssetEditor
    {
        public struct MeshSetElementItem
        {
            public string Name { get; set; }
            public int SectionIndex { get; set; }
            public int MaterialId { get; set; }
            public MeshSubsetCategoryFlags Categories { get; set; }
        }

        private const string PART_LodComboBox = "PART_LodComboBox";
        private const string PART_RenderModeComboBox = "PART_RenderModeComboBox";
        private const string PART_Renderer = "PART_Renderer";
        private const string PART_SectionListBox = "PART_SectionListBox";
        private const string PART_DebugTextBox = "PART_DebugTextBox";
        private const string PART_VariationsListBox = "PART_VariationsListBox";
        private const string PART_VariationComboBox = "PART_VariationComboBox";
        private const string PART_Details = "PART_Details";
        private const string PART_ExtractMaterialInfoButton = "PART_ExtractMaterialInfoButton";
        private const string PART_PreviewSettings = "PART_PreviewSettings";
        private const string PART_MeshSettings = "PART_MeshSettings";
        private const string PART_AssetPropertyGrid = "PART_AssetPropertyGrid";
        private const string PART_MeshTabControl = "PART_MeshTabControl";
        private const string PART_MeshTabContent = "PART_MeshTabContent";

#if FROSTY_DEVELOPER
        private const string PART_RenderDocButton = "PART_RenderDocButton";
#endif

        private ComboBox lodComboBox;
        private ComboBox renderModeComboBox;
        private MeshSet meshSet;
        private Guid meshGuid;
        private FrostyViewport viewport;
        private MultiMeshPreviewScreen screen = new MultiMeshPreviewScreen();
        private FrostyPropertyGrid pgDetails;
        private Button extractButton;
        private ComboBox variationsComboBox;
        private FrostyPropertyGrid pgPreviewSettings;
        private FrostyPropertyGrid pgAsset;
        private FrostyPropertyGrid pgMeshSettings;
        private FrostyDetachedTabControl meshTabContent;
        private FrostyTabControl meshTabControl;
#if FROSTY_DEVELOPER
        private Button renderDocButton;
#endif

        private bool firstTimeLoad = true;
        private static Dictionary<uint, EbxAssetEntry> objectVariationMapping = new Dictionary<uint, EbxAssetEntry>();
        private MeshSetPreviewSettings previewSettings = new MeshSetPreviewSettings();
        private MeshSetMeshSettings meshSettings = new MeshSetMeshSettings();
        private List<MeshSetVariationDetails> variations;
        
        private int selectedPreviewIndex = 0;
        private int selectedVariationsIndex = 0;

        private List<ShaderBlockDepot> shaderBlockDepots = new List<ShaderBlockDepot>();

        private string lastImportedMesh = "";

        static FrostyMeshSetEditor()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FrostyMeshSetEditor), new FrameworkPropertyMetadata(typeof(FrostyMeshSetEditor)));
        }

        public FrostyMeshSetEditor()
            : base(null)
        {
        }

        public FrostyMeshSetEditor(ILogger inLogger)
            : base(inLogger)
        {
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Loaded += FrostyMeshSetEditor_Loaded;

            extractButton = GetTemplateChild(PART_ExtractMaterialInfoButton) as Button;
            extractButton.Click += ExtractButton_Click;

            viewport = GetTemplateChild(PART_Renderer) as FrostyViewport;

            lodComboBox = GetTemplateChild(PART_LodComboBox) as ComboBox;
            lodComboBox.SelectionChanged += LodComboBox_SelectionChanged;

            renderModeComboBox = GetTemplateChild(PART_RenderModeComboBox) as ComboBox;
            renderModeComboBox.SelectionChanged += RenderModeComboBox_SelectionChanged;

            pgDetails = GetTemplateChild(PART_Details) as FrostyPropertyGrid;
            pgDetails.OnModified += PgDetails_OnModified;
            pgDetails.OnPreModified += PgDetails_OnPreModified;

            pgPreviewSettings = GetTemplateChild(PART_PreviewSettings) as FrostyPropertyGrid;
            pgPreviewSettings.OnModified += PgPreviewSettings_OnModified;

            pgMeshSettings = GetTemplateChild(PART_MeshSettings) as FrostyPropertyGrid;
            pgMeshSettings.OnPreModified += PgMeshSettings_OnPreModified;
            pgMeshSettings.OnModified += PgMeshSettings_OnModified;

            pgAsset = GetTemplateChild(PART_AssetPropertyGrid) as FrostyPropertyGrid;
            pgAsset.OnModified += PgAsset_OnModified;

            variationsComboBox = GetTemplateChild(PART_VariationComboBox) as ComboBox;
            variationsComboBox.SelectionChanged += VariationsComboBox_SelectionChanged;

            meshTabContent = GetTemplateChild(PART_MeshTabContent) as FrostyDetachedTabControl;
            meshTabControl = GetTemplateChild(PART_MeshTabControl) as FrostyTabControl;

            meshTabContent.HeaderControl = meshTabControl;

#if FROSTY_DEVELOPER
            renderDocButton = GetTemplateChild(PART_RenderDocButton) as Button;
            renderDocButton.Click += RenderDocButton_Click;
#endif

            viewport.Screen = screen;
        }

        private void PgMeshSettings_OnModified(object sender, ItemModifiedEventArgs e)
        {
            if (e.Item.Path.Contains("Sections"))
            {
                int lodIdx = getArrayIndexFromPath("Lods", e.Item.Path);
                int sectionIdx = getArrayIndexFromPath("Sections", e.Item.Path);

                if (e.Item.Name == "Highlight")
                {
                    screen.SetMeshSectionSelected(0, lodIdx, sectionIdx, (bool)e.NewValue);
                }
                else if (e.Item.Name == "Visible")
                {
                    screen.SetMeshSectionVisible(0, lodIdx, sectionIdx, (bool)e.NewValue);
                }
            }
        }

        private void PgMeshSettings_OnPreModified(object sender, ItemPreModifiedEventArgs e)
        {
            if (e.Item.Path.EndsWith("Sections") || e.Item.Path.EndsWith("Lods"))
            {
                e.Ignore = true;
            }
        }

        private void PgAsset_OnModified(object sender, ItemModifiedEventArgs e)
        {
            if (ProfilesLibrary.DataVersion == (int)ProfileVersion.StarWarsBattlefrontII)
            {
                if (e.Item.Path.Contains("BoolParameters") || e.Item.Path.Contains("VectorParameters") || e.Item.Path.Contains("TextureParameters") || e.Item.Path.Contains("ConditionalParameters"))
                {
                    dynamic ebxData = RootObject;

                    foreach (var sbd in shaderBlockDepots)
                    {
                        for (int lodIndex = 0; lodIndex < meshSet.Lods.Count; lodIndex++)
                        {
                            MeshSetLod lod = meshSet.Lods[lodIndex];
                            ShaderBlockEntry sbe = sbd.GetSectionEntry(lodIndex);

                            int index = 0;
                            foreach (MeshSetSection section in lod.Sections)
                            {
                                dynamic material = ebxData.Materials[section.MaterialId].Internal;
                                ShaderPersistentParamDbBlock texturesBlock = sbe.GetTextureParams(index);
                                ShaderPersistentParamDbBlock paramsBlock = sbe.GetParams(index);
                                index++;

                                foreach (dynamic param in material.Shader.BoolParameters)
                                {
                                    string paramName = param.ParameterName;
                                    bool value = param.Value;

                                    paramsBlock.SetParameterValue(paramName, value);
                                }
                                foreach (dynamic param in material.Shader.VectorParameters)
                                {
                                    string paramName = param.ParameterName;
                                    dynamic vec = param.Value;

                                    paramsBlock.SetParameterValue(paramName, new float[] { vec.x, vec.y, vec.z, vec.w });
                                }
                                foreach (dynamic param in material.Shader.ConditionalParameters)
                                {
                                    string value = param.Value;
                                    PointerRef assetRef = param.ConditionalAsset;

                                    if (assetRef.Type == PointerRefType.External)
                                    {
                                        EbxAsset asset = App.AssetManager.GetEbx(App.AssetManager.GetEbxEntry(assetRef.External.FileGuid));
                                        dynamic conditionalAsset = asset.RootObject;

                                        string conditionName = conditionalAsset.ConditionName;
                                        byte idx = (byte)conditionalAsset.Branches.IndexOf(value);

                                        paramsBlock.SetParameterValue(conditionName, idx);
                                    }
                                }
                                foreach (dynamic param in material.Shader.TextureParameters)
                                {
                                    string paramName = param.ParameterName;
                                    PointerRef value = param.Value;

                                    texturesBlock.SetParameterValue(paramName, value.External.ClassGuid);
                                }

                                texturesBlock.IsModified = true;
                                paramsBlock.IsModified = true;
                            }
                        }

                        ulong resRid = ((dynamic)RootObject).MeshSetResource;
                        ResAssetEntry resEntry = App.AssetManager.GetResEntry(resRid);

                        App.AssetManager.ModifyRes(sbd.ResourceId, sbd);
                        AssetEntry.LinkAsset(resEntry);
                    }
                }
            }
        }

        private void PgDetails_OnPreModified(object sender, ItemPreModifiedEventArgs e)
        {
            if (!e.Item.Path.Contains("Preview"))
                e.Ignore = true;
        }

        public override List<ToolbarItem> RegisterToolbarItems()
        {
            return new List<ToolbarItem>()
            {
                new ToolbarItem("Export", "Export MeshSet", "Images/Export.png", new RelayCommand((object state) => { ExportButton_Click(this, new RoutedEventArgs()); })),
                new ToolbarItem("Import", "Import MeshSet", "Images/Import.png", new RelayCommand((object state) => { ImportButton_Click(this, new RoutedEventArgs()); })),
                new ToolbarItem("Reimport Previous", "Reimport MeshSet", "Images/Import.png", new RelayCommand((object state) => { ReimportButton_Click(this, new RoutedEventArgs()); })),
            };
        }

#if FROSTY_DEVELOPER
        private void RenderDocButton_Click(object sender, RoutedEventArgs e)
        {
            // begin frame capture on the next frame
            screen.CaptureNextFrame();
        }
#endif

        private int getArrayIndexFromPath(string arrayName, string path)
        {
            int idx = path.IndexOf(arrayName + ".");

            if (idx == -1)
                return -1;

            path = path.Substring(idx + arrayName.Length + 1);
            path = path.Substring(0, path.IndexOf(']')).Trim('[', ']');

            return int.Parse(path);
        }

        private void PgPreviewSettings_OnModified(object sender, ItemModifiedEventArgs e)
        {
            if (e.Item.Name.Contains("SunPosition"))
            {
                screen.SunPosition = SharpDXUtils.FromVec3(previewSettings.SunPosition);
            }
            else if (e.Item.Name.Contains("SunIntensity"))
            {
                screen.SunIntensity = previewSettings.SunIntensity;
            }
            else if (e.Item.Name.Contains("SunAngularRadius"))
            {
                screen.SunAngularRadius = previewSettings.SunAngularRadius;
            }
#if FROSTY_DEVELOPER
            else if (e.Item.Name.Contains("EV100"))
            {
                screen.MinEV100 = previewSettings.MinEV100;
                screen.MaxEV100 = previewSettings.MaxEV100;
            }
            else if (e.Item.Path.Contains("Animation"))
            {
                screen.SetAnimation(LoadAnim(previewSettings.Animation));
            }
            else if (e.Item.Name.Contains("iDepthBias") || e.Item.Name.Contains("fSlopeScaledDepthBias") || e.Item.Name.Contains("fDistanceBiasMin") || e.Item.Name.Contains("fDistanceBiasFactor") || e.Item.Name.Contains("fDistanceBiasThreshold") || e.Item.Name.Contains("fDistanceBiasPower"))
            {
                screen.iDepthBias = previewSettings.iDepthBias;
                screen.fSlopeScaledDepthBias = previewSettings.fSlopeScaledDepthBias;
                screen.fDistanceBiasMin = previewSettings.fDistanceBiasMin;
                screen.fDistanceBiasFactor = previewSettings.fDistanceBiasFactor;
                screen.fDistanceBiasThreshold = previewSettings.fDistanceBiasThreshold;
                screen.fDistanceBiasPower = previewSettings.fDistanceBiasPower;
            }
#endif
            else if (e.Item.Name.Contains("CameraSpeedMultiplier"))
            {
                screen.camera.SetMoveScaler((float)Math.Pow(previewSettings.CameraSpeedMultiplier, 5));
            }
            else if (e.Item.Name.Contains("ColorLookupTable"))
            {
                EbxAssetEntry entry = App.AssetManager.GetEbxEntry(previewSettings.ColorLookupTable.External.FileGuid);
                screen.SetLookupTableTexture(entry);
            }
            else if (e.Item.Path.Contains("PreviewLights"))
            {
                string path = e.Item.Path;
                int idx = getArrayIndexFromPath("PreviewLights", path);
                PreviewLightData lightEntity = null;

                if (e.OldValue == null)
                {
                    // new light
                    lightEntity = e.NewValue as PreviewLightData;
                }
                else if (e.NewValue == null)
                {
                    // light being removed
                    lightEntity = e.OldValue as PreviewLightData;
                    screen.RemoveLight(lightEntity.LightId);
                    return;
                }
                else if (e.NewValue is int count)
                {
                    if (count == 0)
                    {
                        screen.ClearLights();
                        return;
                    }
                }
                else
                {
                    if (idx == -1)
                        return;
                    lightEntity = previewSettings.PreviewLights[idx];
                }

                Matrix transform = SharpDXUtils.FromLinearTransform(lightEntity.Transform);
                Vector3 color = SharpDXUtils.FromVec3(lightEntity.Color);

                if (lightEntity.LightId == -1)
                {
                    // add new light to renderer
                    lightEntity.LightId = screen.AddLight(LightRenderType.Sphere, transform, color, lightEntity.Intensity, lightEntity.AttenuationRadius, lightEntity.SphereRadius);
                }
                else
                {
                    // modify existing light
                    screen.ModifyLight(lightEntity.LightId, transform, color, lightEntity.Intensity, lightEntity.AttenuationRadius, lightEntity.SphereRadius);
                }
            }
            else if (e.Item.Name == "LightProbeTexture")
            {
                EbxAssetEntry entry = App.AssetManager.GetEbxEntry(previewSettings.LightProbeTexture.External.FileGuid);
                screen.SetDistantLightProbeTexture(entry);
            }
            else if (e.Item.Name == "LightProbeIntensity")
            {
                screen.LightProbeIntensity = previewSettings.LightProbeIntensity;
            }
            else if (e.Item.Name == "PreviewMeshes")
            {
                if (e.NewValue == null)
                {
                    // mesh being removed
                    PreviewMeshData meshData = e.OldValue as PreviewMeshData;
                    if (meshData.MeshId != -1)
                        screen.RemoveMesh(meshData.MeshId);
                }
                else if (e.NewValue is List<PreviewMeshData> list)
                {
                    if (list.Count == 0)
                        screen.ClearMeshes();
                }
            }
            else
            {
                string path = e.Item.Path;
                int idx = getArrayIndexFromPath("PreviewMeshes", path);

                if (idx == -1)
                    return;

                PreviewMeshData meshData = previewSettings.PreviewMeshes[idx];
                if (e.Item.Name == "Mesh")
                {
                    if (meshData.MeshId != -1)
                    {
                        // remove old mesh
                        screen.RemoveMesh(meshData.MeshId);
                    }

                    // load new mesh if specified
                    if (meshData.Mesh.Type != PointerRefType.Null)
                    {
                        EbxAssetEntry ebxEntry = App.AssetManager.GetEbxEntry(meshData.Mesh.External.FileGuid);
                        meshData.Asset = App.AssetManager.GetEbx(ebxEntry);

                        ulong resRid = ((dynamic)meshData.Asset.RootObject).MeshSetResource;
                        MeshSet previewMeshSet = App.AssetManager.GetResAs<MeshSet>(App.AssetManager.GetResEntry(resRid));
                        Matrix transform = SharpDXUtils.FromLinearTransform(meshData.Transform);

                        // add to renderer
                        meshData.MeshId = screen.AddMesh(previewMeshSet, new MeshMaterialCollection(meshData.Asset, meshData.Variation), /*Matrix.Scaling(1, 1, -1) **/ transform, LoadPose(ebxEntry.Filename, meshData.Asset));
                    }
                }
                else if (e.Item.Name == "Variation")
                {
                    // load variation
                    if (meshData.MeshId != -1)
                        screen.LoadMaterials(meshData.MeshId, new MeshMaterialCollection(meshData.Asset, meshData.Variation));
                }
                else if (e.Item.Path.Contains("Transform"))
                {
                    // change transform
                    if (meshData.MeshId != -1)
                    {
                        Matrix transform = SharpDXUtils.FromLinearTransform(meshData.Transform);
                        screen.SetTransform(meshData.MeshId, /*Matrix.Scaling(1, 1, -1) **/ transform);
                    }
                }
            }
        }

        private void VariationsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedVariationsIndex = variationsComboBox.SelectedIndex;
            MeshSetVariationDetails variation = variations[selectedVariationsIndex];
            pgDetails.SetClass(variation);
        }

        private void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            FrostySaveFileDialog sfd = new FrostySaveFileDialog("Save material info", "*.xml (XML Files)|*.xml", "MaterialInfo");
            if (sfd.ShowDialog() == true)
            {
                MeshMaterialCollection materials = GetVariation(selectedPreviewIndex);
                FrostyTaskWindow.Show("Extracting Material Info", "", (task) =>
                {
                    int lodindex = 0;
                    foreach (MeshSetLod lod in meshSet.Lods)
                    {
                        using (NativeWriter writer = new NativeWriter(new FileStream(sfd.FileName.Replace(".xml", $"_lod{lodindex}.xml"), FileMode.Create)))
                        {
                            int index = 0;
                            foreach (MeshMaterial material in materials)
                            {
                                MeshSetSection section = meshSet.Lods[lodindex].Sections.Find((MeshSetSection a) => a.MaterialId == index);
                                if (section == null)
                                    continue;

                                EbxAssetEntry shaderEntry = App.AssetManager.GetEbxEntry(material.Shader.External.FileGuid);
                                if (shaderEntry == null)
                                    continue;

                                writer.WriteLine("<!-- " + shaderEntry.Name + " -->");
                                writer.WriteLine("<shader profile=\"" + ProfilesLibrary.ProfileName + "\">");
                                writer.WriteLine("\t<permutations>");
                                writer.WriteLine("\t\t<permutation>");
                                writer.WriteLine("\t\t\t<vertexshader>");
                                writer.WriteLine("\t\t\t\t<vertexlayout>");
                                foreach (GeometryDeclarationDesc.Element elem in section.GeometryDeclDesc[0].Elements)
                                {
                                    if (elem.Usage == VertexElementUsage.Unknown)
                                        continue;

                                    string line = "\t\t\t\t\t<layoutelement usage=\"" + elem.Usage + "\" format=\"" + elem.Format + "\" ";
                                    if (section.GeometryDeclDesc[0].StreamCount > 1)
                                        line += "stream=\"" + elem.StreamIndex + "\"";
                                    line += "/>";
                                    writer.WriteLine(line);
                                }
                                writer.WriteLine("\t\t\t\t</vertexlayout>");
                                writer.WriteLine("\t\t\t</vertexshader>");
                                writer.WriteLine("\t\t\t<pixelshader>");
                                writer.WriteLine("\t\t\t\t<parameters>");
                                foreach (dynamic boolParam in material.BoolParameters)
                                {
                                    string paramName = boolParam.ParameterName;
                                    writer.WriteLine("\t\t\t\t\t<parameter name=\"" + paramName + "\" type=\"Bool\"/>");
                                }
                                foreach (dynamic vecParam in material.VectorParameters)
                                {
                                    string paramName = vecParam.ParameterName;
                                    writer.WriteLine("\t\t\t\t\t<parameter name=\"" + paramName + "\" type=\"Float4\"/>");
                                }
                                writer.WriteLine("\t\t\t\t</parameters>");
                                writer.WriteLine("\t\t\t\t<textures>");
                                foreach (dynamic texParam in material.TextureParameters)
                                {
                                    string paramName = texParam.ParameterName;
                                    writer.WriteLine("\t\t\t\t\t<texture name=\"" + paramName + "\" type=\"<Replace>\"/>");
                                }
                                writer.WriteLine("\t\t\t\t</textures>");
                                writer.WriteLine("\t\t\t</pixelshader>");
                                writer.WriteLine("\t\t</permutation>");
                                writer.WriteLine("\t</permutation>");
                                writer.WriteLine("</shader>");
                            }
                        }
                        lodindex++;
                    }
                });
            }
        }

        private void PgDetails_OnModified(object sender, ItemModifiedEventArgs e)
        {
            if (pgDetails.SelectedClass is MeshSetVariationDetails variation && variation.Preview)
                screen.LoadMaterials(0, GetVariation(variation));
        }

        protected override void InvokeOnAssetModified()
        {
            base.InvokeOnAssetModified();
            if (selectedPreviewIndex != -1)
                screen.LoadMaterials(0, GetVariation(selectedPreviewIndex));
        }

        private void RenderModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (screen == null)
                return;
            screen.RenderMode = (DebugRenderMode)renderModeComboBox.SelectedIndex;
        }

        private void FrostyMeshSetEditor_Loaded(object sender, RoutedEventArgs e)
        {
            if (firstTimeLoad)
            {
                ulong resRid = ((dynamic)RootObject).MeshSetResource;
                ResAssetEntry rEntry = App.AssetManager.GetResEntry(resRid);

                meshSet = App.AssetManager.GetResAs<MeshSet>(rEntry);
                /*if (meshSet.Lods[0].SectionCount == 2)
                {
                    ResAssetEntry meshset = App.AssetManager.GetResEntry(
                        "worlds/themepark/vegetation/themepark_rome_busha_indestructable_mesh");
                    MeshSet donorMeshSet = App.AssetManager.GetResAs<MeshSet>(meshset);

                    for (int i = 0; i < meshSet.Lods.Count; i++)
                    {
                        MeshSetLod lod = meshSet.Lods[i];
                        MeshSetLod donorLod = donorMeshSet.Lods[0];

                        lod.SectionCount++;
                        MeshSetSection donorSection = donorLod.Sections[1];
                        lod.Sections.Add(donorSection);
                    }
                    App.AssetManager.ModifyRes(meshSet.ResourceId, meshSet.SaveBytes(), meshSet.ResourceMeta);
                }*/
                if (ProfilesLibrary.DataVersion == (int)ProfileVersion.Madden19 || ProfilesLibrary.DataVersion == (int)ProfileVersion.Madden20)
                    meshSet.TangentSpaceCompressionType = (TangentSpaceCompressionType)((dynamic)RootObject).TangentSpaceCompressionType;

                meshGuid = App.AssetManager.GetEbxEntry(meshSet.FullName).Guid;

                if (!MeshVariationDb.IsLoaded)
                {
                    FrostyTaskWindow.Show("Loading Variations", "", MeshVariationDb.LoadVariations);
                }
                MeshVariationDb.LoadModifiedVariations();
                variations = LoadVariations();
                variations.Sort((MeshSetVariationDetails a, MeshSetVariationDetails b) =>
                {
                    if (a.Name.Equals("Default"))
                        return -1;

                    return b.Name.Equals("Default") ? 1 : a.Name.CompareTo(b.Name);
                });

                UpdateMeshSettings();

                pgPreviewSettings.SetClass(previewSettings);
                screen.AddMesh(meshSet, GetVariation(selectedPreviewIndex), Matrix.Identity /*Matrix.Scaling(1,1,-1)*/, LoadPose(AssetEntry.Filename, asset));

                if (ProfilesLibrary.DataVersion == (int)ProfileVersion.StarWarsBattlefrontII)
                {
                    string path = AssetEntry.Name.ToLower();
                    foreach (ResAssetEntry resEntry in App.AssetManager.EnumerateRes(resType: (uint)ResourceType.ShaderBlockDepot))
                    {
                        if (resEntry.Name.StartsWith(path))
                        {
                            shaderBlockDepots.Add(App.AssetManager.GetResAs<ShaderBlockDepot>(resEntry));
                        }
                    }

                    MeshSetVariationDetails variation = variations[0];
                    int index = 0;

                    dynamic ebxData = RootObject;
                    foreach (PointerRef pr in ebxData.Materials)
                    {
                        dynamic material = pr.Internal;
                        if (material.Shader.TextureParameters.Count == 0)
                        {
                            MeshMaterial varMaterial = variation.MaterialCollection.Materials[index++];
                            foreach (dynamic param in varMaterial.TextureParameters)
                                material.Shader.TextureParameters.Add(param);
                        }
                    }
                }

                firstTimeLoad = false;
            }

            UpdateControls();
        }

        private void UpdateMeshSettings()
        {
            meshSettings = new MeshSetMeshSettings();
            meshSettings.BoundingBox.Minimum = meshSet.BoundingBox.min;
            meshSettings.BoundingBox.Maximum = meshSet.BoundingBox.max;
            dynamic materials = ((dynamic)RootObject).Materials;

            foreach (MeshSetLod lod in meshSet.Lods)
            {
                PreviewMeshLodData lodData = new PreviewMeshLodData() { Name = lod.ShortName };
                foreach (MeshSetSection section in lod.Sections)
                {
                    if (lod.IsSectionRenderable(section) && section.PrimitiveCount > 0)
                    {
                        dynamic material = materials[section.MaterialId].Internal;
                        material.__Id = section.Name;

                        PreviewMeshSectionData sectionData = new PreviewMeshSectionData() { Visible = true, MaterialId = materials[section.MaterialId], MaxBonesPerVertex = section.BonesPerVertex };
                        lodData.Sections.Add(sectionData);

                        foreach (var elem in section.GeometryDeclDesc[0].Elements)
                        {
                            if (elem.Usage >= VertexElementUsage.TexCoord0 && elem.Usage <= VertexElementUsage.TexCoord7)
                                sectionData.NumUVChannels++;
                            else if (elem.Usage >= VertexElementUsage.Color0 && elem.Usage <= VertexElementUsage.Color1)
                                sectionData.NumColorChannels++;
                            else
                            {
                                switch (elem.Usage)
                                {
                                    case VertexElementUsage.Pos:
                                    case VertexElementUsage.Normal:
                                    case VertexElementUsage.Binormal:
                                    case VertexElementUsage.BinormalSign:
                                    case VertexElementUsage.Tangent:
                                    case VertexElementUsage.TangentSpace:
                                    case VertexElementUsage.BoneIndices:
                                    case VertexElementUsage.BoneIndices2:
                                    case VertexElementUsage.BoneWeights:
                                    case VertexElementUsage.BoneWeights2:
                                    case VertexElementUsage.Unknown:
                                        break;

                                    default:
                                        sectionData.AdditionalChannels.Add(elem.Usage.ToString());
                                        break;
                                }
                            }
                        }
                    }
                }
                meshSettings.Lods.Add(lodData);
            }

            pgMeshSettings.SetClass(meshSettings);
        }

        #region -- Animation --
        private MeshRenderAnim LoadAnim(string name)
        {
            MeshRenderAnim anim = null;
            if (File.Exists(name))
            {
                using (NativeReader reader = new NativeReader(new FileStream(name, FileMode.Open, FileAccess.Read)))
                {
                    int frameCount = reader.ReadInt();
                    int keyFrameCount = reader.ReadInt();

                    anim = new MeshRenderAnim(frameCount);
                    Dictionary<string, MeshRenderAnim.Bone> bones = new Dictionary<string, MeshRenderAnim.Bone>();

                    for (int i = 0; i < keyFrameCount; i++)
                    {
                        int frameTime = reader.ReadInt();
                        int boneCount = reader.ReadInt();

                        for (int j = 0; j < boneCount; j++)
                        {
                            string boneName = reader.ReadNullTerminatedString().ToLower();
                            int type = reader.ReadInt();

                            if (!bones.ContainsKey(boneName))
                                bones.Add(boneName, new MeshRenderAnim.Bone() { NameHash = Fnv1.HashString(boneName) });

                            switch (type)
                            {
                                case 0x0E:
                                    {
                                        Quaternion q = new Quaternion()
                                        {
                                            X = reader.ReadFloat(),
                                            Y = reader.ReadFloat(),
                                            Z = reader.ReadFloat(),
                                            W = reader.ReadFloat()
                                        };
                                        bones[boneName].Rotations.Add(new MeshRenderAnim.Keyframe<Quaternion>() { FrameTime = frameTime, Value = q });
                                    }
                                    break;

                                case 0x0F:
                                    reader.ReadFloat();
                                    break;

                                case 0x7a2e5497:
                                    {
                                        Vector3 t = new Vector3()
                                        {
                                            X = reader.ReadFloat(),
                                            Y = reader.ReadFloat(),
                                            Z = reader.ReadFloat()
                                        };
                                        bones[boneName].Translations.Add(new MeshRenderAnim.Keyframe<Vector3>() { FrameTime = frameTime, Value = t });
                                    }
                                    break;

                                case 0x7a2e53c6:
                                    {
                                        Vector3 s = new Vector3()
                                        {
                                            X = reader.ReadFloat(),
                                            Y = reader.ReadFloat(),
                                            Z = reader.ReadFloat()
                                        };
                                        bones[boneName].Scales.Add(new MeshRenderAnim.Keyframe<Vector3>() { FrameTime = frameTime, Value = s });
                                    }
                                    break;
                            }
                        }
                    }

                    anim.AddBones(bones.Values);
                }
            }
            return anim;
        }

        private MeshRenderSkeleton LoadPose(string name, EbxAsset meshAsset)
        {
            MeshRenderSkeleton skeleton = new MeshRenderSkeleton();
            Dictionary<string, Tuple<Vector3, Vector3, Quaternion>> facePose = new Dictionary<string, Tuple<Vector3, Vector3, Quaternion>>();

            if (File.Exists("Faces/" + name + ".bin"))
            {
                string skeletonName = "";
                using (NativeReader poseReader = new NativeReader(new FileStream("Faces/" + name + ".bin", FileMode.Open, FileAccess.Read)))
                {
                    byte b = poseReader.ReadByte();
                    if (b == 0x00)
                    {
                        skeletonName = poseReader.ReadNullTerminatedString();
                    }
                    else
                        poseReader.Position--;

                    while (poseReader.Position < poseReader.Length)
                    {
                        string str = poseReader.ReadNullTerminatedString().ToLower();
                        uint type = poseReader.ReadUInt();

                        Quaternion q = new Quaternion(0, 0, 0, float.MaxValue);
                        Vector3 v = new Vector3(float.MaxValue, 0, 0);
                        Vector3 s = new Vector3(float.MaxValue, 0, 0);

                        if (!facePose.ContainsKey(str))
                            facePose.Add(str, new Tuple<Vector3, Vector3, Quaternion>(s, v, q));

                        if (type == 0xE)
                        {
                            q.X = poseReader.ReadFloat();
                            q.Y = poseReader.ReadFloat();
                            q.Z = poseReader.ReadFloat();
                            q.W = poseReader.ReadFloat();
                            facePose[str] = new Tuple<Vector3, Vector3, Quaternion>(facePose[str].Item1, facePose[str].Item2, q);
                        }
                        else if (type == 0x7a2e5497)
                        {
                            v.X = poseReader.ReadFloat();
                            v.Y = poseReader.ReadFloat();
                            v.Z = poseReader.ReadFloat();
                            facePose[str] = new Tuple<Vector3, Vector3, Quaternion>(facePose[str].Item1, v, facePose[str].Item3);
                        }
                        else if (type == 0x7a2e53c6)
                        {
                            s.X = poseReader.ReadFloat();
                            s.Y = poseReader.ReadFloat();
                            s.Z = poseReader.ReadFloat();
                            facePose[str] = new Tuple<Vector3, Vector3, Quaternion>(s, facePose[str].Item2, facePose[str].Item3);
                        }
                    }
                }

                if (skeletonName == "")
                {
                    switch (ProfilesLibrary.DataVersion)
                    {
                        case (int)ProfileVersion.StarWarsBattlefrontII: skeletonName = "Characters/Rigs/Humanoids/Walrus_HumanMale"; break;
                        case (int)ProfileVersion.Battlefield5: skeletonName = "Characters/skeletons/1P_MaleSoldier_FB"; break;
                        case (int)ProfileVersion.StarWarsBattlefront: skeletonName = "Animations/Rigs/Humanoids/HumanMale"; break;
                        case (int)ProfileVersion.MirrorsEdgeCatalyst: skeletonName = "Characters/Skeletons/Skeleton_Female"; break;
                        case (int)ProfileVersion.MassEffectAndromeda: skeletonName = "Game/characters/_Skeletons/bHMF_skeleton"; break;
                        case (int)ProfileVersion.Battlefield1: skeletonName = "Characters/skeletons/Character/3pAntSkeleton"; break;
                        case (int)ProfileVersion.Anthem: skeletonName = "Animation/HMM/HMM_Skeleton"; break;
                        case (int)ProfileVersion.DragonAgeInquisition: skeletonName = "DA3/Animation/Humanoid/Human/AdultMale/hm_skeleton"; break;
                    }
                }

                if (skeletonName != "")
                {
                    EbxAssetEntry skeletonAssetEntry = App.AssetManager.GetEbxEntry(skeletonName);
                    dynamic skeletonAsset = App.AssetManager.GetEbx(skeletonAssetEntry).RootObject;
                    dynamic boneNames = skeletonAsset.BoneNames;
                    dynamic pose = skeletonAsset.ModelPose;
                    dynamic localPose = skeletonAsset.LocalPose;

                    for (int boneIdx = 0; boneIdx < boneNames.Count; boneIdx++)
                    {
                        string boneName = boneNames[boneIdx];
                        if (ProfilesLibrary.DataVersion == (int)ProfileVersion.MassEffectAndromeda)
                            boneName = Murmur2.HashString64(boneName, 0x4eb23).ToString("X16");

                        boneName = boneName.ToLower();
                        Matrix boneMatrix = SharpDXUtils.FromLinearTransform(localPose[boneIdx]);

                        if (facePose.ContainsKey(boneName))
                        {
                            Vector3 scale = Vector3.One;
                            Vector3 trans = boneMatrix.TranslationVector;
                            Quaternion rot = Quaternion.RotationMatrix(boneMatrix);
                            Vector3 euler = Vector3.Zero;

                            boneMatrix.Decompose(out scale, out rot, out trans);
                            euler = SharpDXUtils.ExtractEulerAngles(boneMatrix);

                            if (facePose[boneName].Item1.X < float.MaxValue)
                                scale = facePose[boneName].Item1;
                            if (facePose[boneName].Item2.X < float.MaxValue)
                                trans = facePose[boneName].Item2;
                            if (facePose[boneName].Item3.W < float.MaxValue)
                                rot = facePose[boneName].Item3;

                            boneMatrix = Matrix.Scaling(scale) * Matrix.RotationQuaternion(rot) * Matrix.Translation(trans);
                        }

                        Matrix mp = SharpDXUtils.FromLinearTransform(pose[boneIdx]);
                        mp.Invert();

                        boneName = boneNames[boneIdx];
                        boneName = boneName.ToLower();

                        skeleton.AddBone(new MeshRenderSkeleton.Bone()
                        {
                            NameHash = Fnv1.HashString(boneName),
                            ModelPose = mp,
                            LocalPose = boneMatrix,
                            ParentBoneId = skeletonAsset.Hierarchy[boneIdx]
                        });
                    }

                    if (ProfilesLibrary.DataVersion == (int)ProfileVersion.StarWarsBattlefrontII || ProfilesLibrary.DataVersion == (int)ProfileVersion.StarWarsSquadrons)
                    {
                        if (TypeLibrary.IsSubClassOf(meshAsset.RootObject, "SkinnedMeshAsset"))
                        {
                            int boneId = 0;

                            // SWBF2 procedural animation bones
                            foreach (dynamic bone in ((dynamic)meshAsset.RootObject).SkinnedProceduralAnimation.Bones)
                            {
                                Matrix mp = SharpDXUtils.FromLinearTransform(bone.LocalPose);
                                Matrix boneMatrix = SharpDXUtils.FromLinearTransform(bone.Pose);
                                mp.Invert();

                                skeleton.AddBone(new MeshRenderSkeleton.Bone()
                                {
                                    NameHash = Fnv1.HashString("PROC_" + boneId++),
                                    ModelPose = mp,
                                    LocalPose = boneMatrix,
                                    ParentBoneId = bone.ParentIndex,
                                    IsProcedural = true
                                });
                            }

                            foreach (dynamic rootPose in ((dynamic)meshAsset.RootObject).SkinnedProceduralAnimation.RootPoses)
                            {
                                int index = rootPose.Index;
                                Matrix mp = SharpDXUtils.FromLinearTransform(rootPose.LocalPose);
                                mp.Invert();

                                skeleton.UpdateBone(index, modelPose: mp);
                            }

#if FROSTY_DEVELOPER
                            foreach (dynamic expression in ((dynamic)meshAsset.RootObject).SkinnedProceduralAnimation.Expressions)
                            {


                                EbxAssetEntry exprEntry = App.AssetManager.GetEbxEntry(expression.Graph.External.FileGuid);
                                if (exprEntry.Filename == "RollBone")
                                {
                                    int matrix1 = -1;
                                    int matrix2 = -1;
                                    int matrix3 = -1;
                                    Vector3 v1 = new Vector3(1, 1, 1);
                                    float f1 = 0.0f;

                                    List<dynamic> runtimeParams = new List<dynamic>();
                                    runtimeParams.AddRange(expression.RuntimeParameters);

                                    dynamic param = runtimeParams.Find((dynamic a) => a.NodeHash == 242850164);
                                    matrix1 = param.IntValue;

                                    param = runtimeParams.Find((dynamic a) => a.NodeHash == -1508053196);
                                    matrix2 = param.IntValue;

                                    param = runtimeParams.Find((dynamic a) => a.NodeHash == -72088988);
                                    matrix3 = param.IntValue;

                                    param = runtimeParams.Find((dynamic a) => a.NodeHash == -612073924);
                                    v1.X = param.FloatValue;

                                    param = runtimeParams.Find((dynamic a) => a.NodeHash == -300227826);
                                    v1.Y = param.FloatValue;

                                    param = runtimeParams.Find((dynamic a) => a.NodeHash == 1831777758);
                                    v1.Z = param.FloatValue;

                                    param = runtimeParams.Find((dynamic a) => a.NodeHash == -382567928);
                                    f1 = param.FloatValue;

                                    MeshRenderSkeleton.TestRollBoneExpression expr = new MeshRenderSkeleton.TestRollBoneExpression(
                                        new MeshRenderSkeleton.ExpressionValue<Vector3>(v1),
                                        new MeshRenderSkeleton.ExpressionValue<float>(f1),
                                        new MeshRenderSkeleton.BoneQueryExpressionValue(skeleton, matrix1),
                                        new MeshRenderSkeleton.BoneQueryExpressionValue(skeleton, matrix2),
                                        new MeshRenderSkeleton.BoneQueryExpressionValue(skeleton, matrix3)
                                    );
                                    skeleton.AddExpression(skeleton.BoneCount + expression.BoneIndices[0], expr);
                                }
                            }
#endif
                        }
                    }
                }
            }

            return skeleton;
        } 
        #endregion

        private void UpdateControls()
        {
            lodComboBox.Items.Clear();
            for (int i = 0; i < meshSet.Lods.Count; i++)
                lodComboBox.Items.Add(i);
            lodComboBox.SelectedIndex = 0;

            variationsComboBox.ItemsSource = variations;
            variationsComboBox.SelectedIndex = selectedVariationsIndex;
        }

        private MeshMaterialCollection GetVariation(int index)
        {
            if (index >= variations.Count)
                return null;
            return GetVariation(variations[index]);
        }

        private MeshMaterialCollection GetVariation(MeshSetVariationDetails variation)
        {
            int newSelectedIndex = -1;
            int i = 0;

            foreach (object objClass in variations)
            {
                if (i == selectedPreviewIndex && objClass != variation)
                {
                    MeshSetVariationDetails mvd = objClass as MeshSetVariationDetails;
                    mvd.Preview = false;
                }
                if (objClass == variation)
                {
                    newSelectedIndex = i;
                }
                i++;
            }

            selectedPreviewIndex = newSelectedIndex;
            return new MeshMaterialCollection(asset, variation.Variation);
        }

        private List<MeshSetVariationDetails> LoadVariations()
        {
            //if (ProfilesLibrary.DataVersion != (int)ProfileVersion.Fifa19)
            {
                dynamic ebxData = RootObject;
                MeshVariationDbEntry mvEntry = MeshVariationDb.GetVariations((AssetEntry as EbxAssetEntry).Guid);

                if (mvEntry != null)
                {
                    if (objectVariationMapping.Count == 0)
                    {
                        foreach (EbxAssetEntry varEntry in App.AssetManager.EnumerateEbx(type: "ObjectVariation"))
                            objectVariationMapping.Add((uint)Fnv1.HashString(varEntry.Name.ToLower()), varEntry);
                    }

                    List<MeshSetVariationDetails> detailsList = new List<MeshSetVariationDetails>();

                    List<MeshVariation> mVariations = mvEntry.Variations.Values.ToList();
                    mVariations.Sort((MeshVariation a, MeshVariation b) => { return a.AssetNameHash.CompareTo(b.AssetNameHash); });

                    foreach (MeshVariation mv in mVariations)
                    {
                        MeshSetVariationDetails variationDetails = new MeshSetVariationDetails {Name = "Default"};

                        if (objectVariationMapping.ContainsKey(mv.AssetNameHash))
                        {
                            EbxAsset asset = App.AssetManager.GetEbx(objectVariationMapping[mv.AssetNameHash]);
                            AssetClassGuid guid = ((dynamic)asset.RootObject).GetInstanceGuid();

                            variationDetails.Name = objectVariationMapping[mv.AssetNameHash].Filename;
                            variationDetails.Variation = new PointerRef(new EbxImportReference()
                            {
                                FileGuid = objectVariationMapping[mv.AssetNameHash].Guid,
                                ClassGuid = guid.ExportedGuid
                            });
                        }
                        else if (mv.AssetNameHash != 0)
                            continue;

                        for (int i = 0; i < ebxData.Materials.Count; i++)
                        {
                            dynamic material = ebxData.Materials[i].Internal;
                            AssetClassGuid guid = material.GetInstanceGuid();

                            MeshVariationMaterial varMaterial = mv.GetMaterial(guid.ExportedGuid);
                            if (varMaterial == null)
                            {
                                continue;
                            }
                            MeshSetMaterialDetails details = new MeshSetMaterialDetails();
                            variationDetails.MaterialCollection = new MeshMaterialCollection.Container(new MeshMaterialCollection(asset, new PointerRef(varMaterial.MaterialVariationAssetGuid)));
                        }

                        foreach (Tuple<EbxImportReference, int> dbEntries in mv.DbLocations)
                            variationDetails.MeshVariationDbs.Add(new MeshSetVariationEntryDetails()
                            {
                                VariationDb = new PointerRef(dbEntries.Item1),
                                Index = dbEntries.Item2
                            });
                        detailsList.Add(variationDetails);
                        if (detailsList.Count == 1)
                            variationDetails.Preview = true;
                    }

                    return detailsList;
                }
                else
                {
                    List<MeshSetVariationDetails> detailsList = new List<MeshSetVariationDetails>();
                    MeshSetVariationDetails variationDetails = new MeshSetVariationDetails {Name = "Default"};

                    for (int i = 0; i < ebxData.Materials.Count; i++)
                    {
                        dynamic material = ebxData.Materials[i].Internal;
                        if (material == null)
                            continue;
                        MeshSetMaterialDetails details = new MeshSetMaterialDetails();
                        variationDetails.MaterialCollection = new MeshMaterialCollection.Container(new MeshMaterialCollection(asset, new PointerRef()));
                    }

                    detailsList.Add(variationDetails);
                    variationDetails.Preview = true;

                    return detailsList;
                }
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            viewport.SetPaused(true);

            MeshExportSettings settings = (AssetEntry.Type == "SkinnedMeshAsset")
                ? new SkinnedMeshExportSettings()
                : new MeshExportSettings();

            // load settings
            string Version = Config.Get<string>("MeshSetExportVersion", "FBX_2012", ConfigScope.Game);
            string Scale = Config.Get<string>("MeshSetExportScale", "Centimeters", ConfigScope.Game);
            bool flattenHierarchy = Config.Get<bool>("MeshSetExportFlattenHierarchy", false, ConfigScope.Game);
            bool exportSingleLod = Config.Get<bool>("MeshSetExportExportSingleLod", false, ConfigScope.Game);
            bool exportAdditionalMeshes = Config.Get<bool>("MeshSetExportExportAdditionalMeshes", false, ConfigScope.Game);
            string skeleton = Config.Get<string>("MeshSetExportSkeleton", "", ConfigScope.Game);

            //string Version = Config.Get<string>("MeshSetExport", "Version", "FBX_2012");
            //string Scale = Config.Get<string>("MeshSetExport", "Scale", "Centimeters");
            //bool flattenHierarchy = Config.Get<bool>("MeshSetExport", "FlattenHierarchy", false);
            //bool exportAdditionalMeshes = Config.Get<bool>("MeshSetExport", "ExportAdditionalMeshes", false);
            //string skeleton = Config.Get<string>("MeshSetExport", "Skeleton", "");

            settings.Version = (MeshExportVersion)Enum.Parse(typeof(MeshExportVersion), Version);
            settings.Scale = (MeshExportScale)Enum.Parse(typeof(MeshExportScale), Scale);
            settings.FlattenHierarchy = flattenHierarchy;
            settings.ExportSingleLod = exportSingleLod;
            settings.ExportAdditionalMeshes = exportAdditionalMeshes;

            if (settings is SkinnedMeshExportSettings exportSettings)
                exportSettings.SkeletonAsset = skeleton;

            // show settings box
            if (FrostyImportExportBox.Show<MeshExportSettings>("Mesh Export Settings", FrostyImportExportType.Export, settings) == MessageBoxResult.OK)
            {
                string filter = "*.fbx (FBX Binary File)|*.fbx|*.fbx (FBX ASCII File)|*.fbx";
                if (!(settings is SkinnedMeshExportSettings) || ((SkinnedMeshExportSettings)settings).SkeletonAsset == "")
                    filter += "|*.obj (OBJ File)|*.obj";

                FrostySaveFileDialog sfd = new FrostySaveFileDialog("Save MeshSet", filter, "Mesh", AssetEntry.Filename);
                if (sfd.ShowDialog())
                {
                    if (meshSet.Type == MeshType.MeshType_Skinned)
                        skeleton = ((SkinnedMeshExportSettings)settings).SkeletonAsset;

                    EbxAssetEntry entry = App.AssetManager.GetEbxEntry(((dynamic)RootObject).Name);

                    List<MeshSet> meshSets = new List<MeshSet> {meshSet};

                    if (settings.ExportAdditionalMeshes)
                    {
                        // collect all additional meshes added to the viewport
                        meshSets = screen.GetAllMeshes(meshSets);
                    }

                    // fbx/obj exporting
                    string[] fileTypes = new string[] { "binary", "ascii", "obj" };
                    FrostyTaskWindow.Show("Exporting MeshSet", "", (task) =>
                    {
                        FBXExporter exporter = new FBXExporter(task);
                        exporter.ExportFBX(RootObject, sfd.FileName, settings.Version.ToString().Replace("FBX_", ""), settings.Scale.ToString(), settings.FlattenHierarchy, settings.ExportSingleLod, skeleton, fileTypes[sfd.FilterIndex - 1], meshSets.ToArray());
                    });

                    logger.Log("Exported {0} to {1}", entry.Name, sfd.FileName);

                    // save settings
                    Config.Add("MeshSetExportVersion", settings.Version.ToString(), ConfigScope.Game);
                    Config.Add("MeshSetExportScale", settings.Scale.ToString(), ConfigScope.Game);
                    Config.Add("MeshSetExportFlattenHierarchy", settings.FlattenHierarchy, ConfigScope.Game);
                    Config.Add("MeshSetExportExportSingleLod", settings.ExportSingleLod, ConfigScope.Game);
                    Config.Add("MeshSetExportExportAdditionalMeshes", settings.ExportAdditionalMeshes, ConfigScope.Game);

                    //Config.Add("MeshSetExport", "Version", settings.Version.ToString());
                    //Config.Add("MeshSetExport", "Scale", settings.Scale.ToString());
                    //Config.Add("MeshSetExport", "FlattenHierarchy", settings.FlattenHierarchy);
                    //Config.Add("MeshSetExport", "ExportAdditionalMeshes", settings.ExportAdditionalMeshes);

                    if (settings is SkinnedMeshExportSettings meshExportSettings)
                        Config.Add("MeshSetExportSkeleton", meshExportSettings.SkeletonAsset, ConfigScope.Game);
                    //Config.Add("MeshSetExport", "Skeleton", meshExportSettings.SkeletonAsset);

                    Config.Save();
                }
            }

            viewport.SetPaused(false);
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            viewport.SetPaused(true);

            FrostyOpenFileDialog ofd = new FrostyOpenFileDialog("Import MeshSet", "*.fbx (FBX Files)|*.fbx", "Mesh");
            if (ofd.ShowDialog())
            {
                MeshImportSettings settings = null;
                bool bOk = false;

                if (meshSet.Type == MeshType.MeshType_Skinned)
                {
                    settings = new MeshImportSettings { SkeletonAsset = Config.Get<string>("MeshSetImportSkeleton", "", ConfigScope.Game) };
                    //settings = new FrostyMeshImportSettings {SkeletonAsset = Config.Get<string>("MeshSetImport", "Skeleton", "")};

                    if (FrostyImportExportBox.Show<MeshImportSettings>("Import Skinned Mesh", FrostyImportExportType.Import, settings) == MessageBoxResult.OK)
                    {
                        bOk = true;
                        Config.Add("MeshSetImportSkeleton", settings.SkeletonAsset, ConfigScope.Game);
                        //Config.Add("MeshSetImport", "Skeleton", settings.SkeletonAsset);
                    }
                }
                else
                    bOk = true;

                if (bOk)
                {
                    EbxAsset localAsset = asset;
                    EbxAssetEntry localEntry = AssetEntry as EbxAssetEntry;
                    //List<ShaderBlockEntry> tmpShaderBlockEntries = new List<ShaderBlockEntry>();

                    FrostyTaskWindow.Show("Importing", "", (task) =>
                    {
                        try
                        {
                            // import
                            FBXImporter importer = new FBXImporter(logger);
                            importer.ImportFBX(ofd.FileName, meshSet, localAsset, localEntry, settings);
                            lastImportedMesh = ofd.FileName;
                        }
                        catch (Exception exp)
                        {
                            if (!localEntry.IsAdded)
                            {
                                App.AssetManager.RevertAsset(localEntry);
                            }
                            logger.LogError(exp.Message);
                        }
                    });

                    // @todo: Reload the main mesh shader block depot

                    // update UI
                    screen.ClearMeshes(clearAll: true);
                    screen.AddMesh(meshSet, GetVariation(selectedPreviewIndex), Matrix.Identity /*Matrix.Scaling(1,1,-1)*/, LoadPose(AssetEntry.Filename, asset));

                    UpdateMeshSettings();
                    UpdateControls();

                    InvokeOnAssetModified();
                }
            }

            viewport.SetPaused(false);
        }

        private void ReimportButton_Click(object sender, RoutedEventArgs e)
        {
            if (lastImportedMesh == "")
            {
                ImportButton_Click(sender, e);
                return;
            }
            else if (!File.Exists(lastImportedMesh))
            {
                logger.LogError($"File to reimport can't be found, Did you move or rename it?");
                logger.LogError($"Expected: {lastImportedMesh}");
                return;
            }

            viewport.SetPaused(true);

            MeshImportSettings settings = null;
            if (meshSet.Type == MeshType.MeshType_Skinned)
            {
                settings = new MeshImportSettings { SkeletonAsset = Config.Get<string>("MeshSetImportSkeleton", "", ConfigScope.Game) };
            }

            EbxAsset localAsset = asset;
            EbxAssetEntry localEntry = AssetEntry as EbxAssetEntry;

            FrostyTaskWindow.Show("Importing", "", (task) =>
            {
                try
                {
                    // import
                    FBXImporter importer = new FBXImporter(logger);
                    importer.ImportFBX(lastImportedMesh, meshSet, localAsset, localEntry, settings);
                }
                catch (Exception exp)
                {
                    if (!localEntry.IsAdded)
                    {
                        App.AssetManager.RevertAsset(localEntry);
                    }
                    logger.LogError(exp.Message);
                }
            });

            screen.ClearMeshes(clearAll: true);
            screen.AddMesh(meshSet, GetVariation(selectedPreviewIndex), Matrix.Identity /*Matrix.Scaling(1,1,-1)*/, LoadPose(AssetEntry.Filename, asset));

            UpdateMeshSettings();
            UpdateControls();

            InvokeOnAssetModified();

            viewport.SetPaused(false);
        }

        private void LodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lodComboBox.SelectedIndex == -1)
                return;
            screen.CurrentLOD = lodComboBox.SelectedIndex;
        }

        public override void Closed()
        {
            viewport.Shutdown();
        }

        //private ShaderBlockEntry FindShaderBlockEntry(int lodIndex)
        //{
        //    foreach (ShaderBlockEntry entry in shaderBlockEntries)
        //    {
        //        MeshParamDbBlock meshBlock = entry.GetMeshParams(0);
        //        if (meshBlock.LodIndex == lodIndex)
        //            return entry;
        //    }
        //    return null;
        //}
    }
}
