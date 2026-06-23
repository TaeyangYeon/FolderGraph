using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using FolderGraph.Core;
using FolderGraph.Graph.Abstractions;
using FolderGraph.Helpers;
using FolderGraph.Models;
using FolderGraph.Services.Abstractions;

namespace FolderGraph.ViewModels
{
    /// <summary>
    /// 메인 화면의 상태와 동작을 담당하는 ViewModel.
    /// 의존성은 생성자로 주입받는다 (DIP, App.xaml.cs에서 수동 조립).
    /// Phase 2: 비동기 스캔 + Force-Directed 애니메이션(타이머 구동).
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private readonly IFolderScanner _scanner;
        private readonly IGraphLayoutEngine _layout;
        private readonly IForceDirectedSimulation _simulation;
        private readonly DispatcherTimer _simTimer;

        private string _rootPath;
        private int _depth;
        private bool _includeHidden;
        private bool _isBusy;
        private string _statusText;
        private CancellationTokenSource _cts;

        public MainViewModel(IFolderScanner scanner,
                             IGraphLayoutEngine layout,
                             IForceDirectedSimulation simulation)
        {
            if (scanner == null) throw new ArgumentNullException("scanner");
            if (layout == null) throw new ArgumentNullException("layout");
            if (simulation == null) throw new ArgumentNullException("simulation");

            _scanner = scanner;
            _layout = layout;
            _simulation = simulation;

            _depth = 3;
            _includeHidden = false;
            _statusText = "폴더 경로를 입력하고 불러오기를 누르세요.";

            Nodes = new ObservableCollection<NodeViewModel>();
            Edges = new ObservableCollection<EdgeViewModel>();

            // 레이아웃 애니메이션용 타이머(약 60fps)
            _simTimer = new DispatcherTimer(DispatcherPriority.Background);
            _simTimer.Interval = TimeSpan.FromMilliseconds(16);
            _simTimer.Tick += OnSimulationTick;

            LoadCommand = new RelayCommand(async () => await LoadAsync(), CanLoad);
        }

        // ── 바인딩 속성 ──────────────────────────────

        public ObservableCollection<NodeViewModel> Nodes { get; private set; }
        public ObservableCollection<EdgeViewModel> Edges { get; private set; }

        public string RootPath
        {
            get { return _rootPath; }
            set { SetProperty(ref _rootPath, value); }
        }

        public int Depth
        {
            get { return _depth; }
            set
            {
                int clamped = value;
                if (clamped < AppConstants.MinDepth) clamped = AppConstants.MinDepth;
                if (clamped > AppConstants.MaxDepth) clamped = AppConstants.MaxDepth;
                SetProperty(ref _depth, clamped);
            }
        }

        public bool IncludeHidden
        {
            get { return _includeHidden; }
            set { SetProperty(ref _includeHidden, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set { SetProperty(ref _isBusy, value); }
        }

        public string StatusText
        {
            get { return _statusText; }
            set { SetProperty(ref _statusText, value); }
        }

        public int MinDepth { get { return AppConstants.MinDepth; } }
        public int MaxDepth { get { return AppConstants.MaxDepth; } }

        public ICommand LoadCommand { get; private set; }

        // ── 동작 ────────────────────────────────────

        private bool CanLoad()
        {
            return !_isBusy && !string.IsNullOrWhiteSpace(_rootPath);
        }

        private async Task LoadAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _cts = new CancellationTokenSource();

            IsBusy = true;
            StatusText = "스캔 중...";

            try
            {
                GraphData data = await _scanner.ScanAsync(
                    _rootPath, _depth, _includeHidden, _cts.Token);

                BuildGraph(data);

                StatusText = data.TruncatedByLimit
                    ? string.Format("노드 {0}개 (최대치 {1} 도달로 일부 생략)",
                                    data.NodeCount, AppConstants.MaxNodes)
                    : string.Format("노드 {0}개", data.NodeCount);
            }
            catch (OperationCanceledException)
            {
                StatusText = "취소되었습니다.";
            }
            catch (Exception ex)
            {
                // Phase 1에서는 상태바 표기. (Phase 6에서 IDialogService 팝업으로 대체)
                StatusText = "오류: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 스캔 결과로부터 NodeViewModel/EdgeViewModel을 만들고,
        /// 힘-기반 시뮬레이션을 초기화한 뒤 애니메이션을 시작한다.
        /// </summary>
        private void BuildGraph(GraphData data)
        {
            // 진행 중 시뮬레이션 정지
            _simTimer.Stop();

            Nodes.Clear();
            Edges.Clear();

            // 모델 → 노드VM 매핑
            var map = new Dictionary<FileNodeModel, NodeViewModel>();
            var nodeList = new List<NodeViewModel>();

            foreach (FileNodeModel model in data.AllNodes)
            {
                var vm = new NodeViewModel(model);
                map[model] = vm;
                nodeList.Add(vm);
            }

            // 초기 시드 배치(격자) — 시뮬레이션이 여기서부터 펼친다
            double area = Math.Max(600, Math.Sqrt(nodeList.Count) * 90);
            _layout.Arrange(nodeList, area, area);

            // 노드 등록
            foreach (NodeViewModel vm in nodeList)
            {
                Nodes.Add(vm);
            }

            // 엣지 생성 + 시뮬레이션 링크 구성
            var links = new List<GraphLink>();
            foreach (FileNodeModel model in data.AllNodes)
            {
                if (model.Parent != null && map.ContainsKey(model.Parent))
                {
                    NodeViewModel parentVm = map[model.Parent];
                    NodeViewModel childVm = map[model];
                    Edges.Add(new EdgeViewModel(parentVm, childVm));
                    links.Add(new GraphLink(parentVm, childVm));
                }
            }

            // 시뮬레이션 초기화 후 시작
            if (nodeList.Count > 0)
            {
                var bodies = new List<IPhysicsBody>(nodeList.Count);
                foreach (NodeViewModel vm in nodeList)
                {
                    bodies.Add(vm);
                }

                double cx = area / 2.0;
                double cy = area / 2.0;
                _simulation.Initialize(bodies, links, cx, cy);
                _simTimer.Start();
            }
        }

        /// <summary>매 틱 한 스텝 진행. 안정되면 타이머를 멈춰 CPU를 아낀다.</summary>
        private void OnSimulationTick(object sender, EventArgs e)
        {
            bool active = _simulation.Step();
            if (!active)
            {
                _simTimer.Stop();
            }
        }

        /// <summary>
        /// 노드 드래그 등 사용자 상호작용 시 호출. 시뮬레이션 온도를 올리고
        /// 멈춰 있던 타이머를 다시 돌려 주변 노드가 재배치되게 한다.
        /// </summary>
        public void ReheatSimulation()
        {
            _simulation.Reheat();
            if (!_simTimer.IsEnabled)
            {
                _simTimer.Start();
            }
        }
    }
}
