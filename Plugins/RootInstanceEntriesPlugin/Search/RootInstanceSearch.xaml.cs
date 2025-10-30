using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Windows;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RootInstanceEntiresPlugin.Search
{
    // Derived from BlueprintEditorPlugin by @ywingpilot2
    public partial class RootInstanceSearch : FrostyDockableWindow
    {
        public RootInstanceSearch()
        {
            InitializeComponent();
            if (!RootInstanceEbxEntryDb.IsLoaded)
            {
                FrostyTaskWindow.Show("Refreshing Root Instance Ebx Entries", "", RootInstanceEbxEntryDb.LoadEbxRootInstanceEntries);
            }
        }

        private void InstText_OnKeyUp(object sender, KeyEventArgs e)
        {
            if (Guid.TryParse(InstText.Text,
                    out Guid guid))
            {
                PartText.WatermarkText = "";
                PartText.Text = RootInstanceEbxEntryDb.GetEbxEntryByRootInstanceGuid(guid)?.Guid.ToString() ?? Guid.Empty.ToString();
            }
        }

        private void PartText_OnKeyUp(object sender, KeyEventArgs e)
        {
            if (Guid.TryParse(PartText.Text,
                    out Guid guid))
            {
                InstText.WatermarkText = "";
                var entry = App.AssetManager.GetEbxEntry(guid);
                InstText.Text = (entry is null) ? "" :
                    App.AssetManager.GetEbx(entry).RootInstanceGuid.ToString();
            }
        }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }


    public class RootInstanceSearchMenuExtension : MenuExtension
    {
        public override string TopLevelMenuName => "Tools";

        public override string SubLevelMenuName => "Root Instance Cache";

        public override string MenuItemName => "Search";

        public override ImageSource Icon => new ImageSourceConverter().ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/Compile.png") as ImageSource;

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            RootInstanceSearch window = new RootInstanceSearch();
            window.Show();
        });
    }

}