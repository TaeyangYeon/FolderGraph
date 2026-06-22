using MahApps.Metro.Controls;

namespace FolderGraph
{
    /// <summary>
    /// 앱의 메인 창. MahApps.Metro의 MetroWindow를 상속해 현대적 외관을 적용.
    /// DataContext(MainViewModel)는 App.OnStartup에서 주입된다.
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
