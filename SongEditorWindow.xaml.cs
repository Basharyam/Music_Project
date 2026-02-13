using System.Windows;
using Telhai.DotNet.PlayerProject.ViewModels;

namespace Telhai.DotNet.PlayerProject
{
    public partial class SongEditorWindow : Window
    {
        public SongEditorWindow(SongEditorViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            vm.RequestClose += () => this.Close();
        }
    }
}
