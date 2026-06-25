using System.Windows;
using FolderGraph.Graph;
using FolderGraph.Graph.Abstractions;
using FolderGraph.Helpers;
using FolderGraph.Helpers.Abstractions;
using FolderGraph.Services;
using FolderGraph.Services.Abstractions;
using FolderGraph.ViewModels;

namespace FolderGraph
{
    /// <summary>
    /// 앱 진입점. 의존성을 수동으로 조립(poor man's DI)해 MainViewModel을 만들고
    /// MainWindow에 주입한다. 나중에 DI 컨테이너로 바꾸려면 이 부분만 교체.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── 의존성 조립 ──
            IFolderScanner scanner = new FolderScanner();
            IGraphLayoutEngine layout = new SimpleGridLayout();          // 초기 시드 배치
            IForceDirectedSimulation simulation = new ForceDirectedSimulation(); // 애니메이션
            INodeColorCalculator colorCalculator = new DepthColorCalculator();   // 명도 그라데이션

            var mainViewModel = new MainViewModel(scanner, layout, simulation, colorCalculator);

            var window = new MainWindow
            {
                DataContext = mainViewModel
            };
            window.Show();
        }
    }
}
