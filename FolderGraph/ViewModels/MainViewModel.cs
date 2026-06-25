using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FolderGraph.Core;
using FolderGraph.Graph.Abstractions;
using FolderGraph.Helpers;
using FolderGraph.Helpers.Abstractions;
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
        private readonly INodeColorCalculator _colorCalculator;
        private readonly DispatcherTimer _simTimer;
        private readonly DispatcherTimer _depthDebounce;

        private string _rootPath;
        private int _depth;
        private bool _includeHidden;
        private bool _isBusy;
        private string _statusText;
        private CancellationTokenSource _cts;
        private bool _hasLoaded;
        private NodeViewModel _selectedNode;

        // 색상 팔레트 상태
        private NodeViewModel _colorTargetNode;
        private bool _isPaletteOpen;
        private static readonly Brush GrayBrush = MakeFrozenGray();

        // 선택 시 흐리게 할 불투명도
        private const double DimOpacity = 0.22;
        private const double DimEdgeOpacity = 0.10;

        public MainViewModel(IFolderScanner scanner,
                             IGraphLayoutEngine layout,
                             IForceDirectedSimulation simulation,
                             INodeColorCalculator colorCalculator)
        {
            if (scanner == null) throw new ArgumentNullException("scanner");
            if (layout == null) throw new ArgumentNullException("layout");
            if (simulation == null) throw new ArgumentNullException("simulation");
            if (colorCalculator == null) throw new ArgumentNullException("colorCalculator");

            _scanner = scanner;
            _layout = layout;
            _simulation = simulation;
            _colorCalculator = colorCalculator;

            _depth = 3;
            _includeHidden = false;
            _statusText = "폴더 경로를 입력하고 불러오기를 누르세요.";

            Nodes = new ObservableCollection<NodeViewModel>();
            Edges = new ObservableCollection<EdgeViewModel>();
            PaletteColors = BuildPalette();

            // 레이아웃 애니메이션용 타이머(약 60fps)
            _simTimer = new DispatcherTimer(DispatcherPriority.Background);
            _simTimer.Interval = TimeSpan.FromMilliseconds(16);
            _simTimer.Tick += OnSimulationTick;

            // 깊이 슬라이더 디바운스(끌고 있는 동안 연속 재스캔 방지)
            _depthDebounce = new DispatcherTimer();
            _depthDebounce.Interval = TimeSpan.FromMilliseconds(300);
            _depthDebounce.Tick += OnDepthDebounceTick;

            LoadCommand = new RelayCommand(async () => await LoadAsync(false), CanLoad);
            PickColorCommand = new RelayCommand<PaletteColor>(OnPickColor);
            ResetColorCommand = new RelayCommand(OnResetColor);
        }

        private static Brush MakeFrozenGray()
        {
            var b = new SolidColorBrush(Colors.Gray);
            b.Freeze();
            return b;
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
                if (SetProperty(ref _depth, clamped) && _hasLoaded)
                {
                    // 한 번 로드된 뒤 깊이를 바꾸면, 잠시 후 재스캔(위치 보존).
                    _depthDebounce.Stop();
                    _depthDebounce.Start();
                }
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
        public ICommand PickColorCommand { get; private set; }
        public ICommand ResetColorCommand { get; private set; }

        /// <summary>우클릭 팔레트에 표시할 기준 색 목록.</summary>
        public IReadOnlyList<PaletteColor> PaletteColors { get; private set; }

        /// <summary>색상 팔레트 팝업 열림 여부(View의 Popup.IsOpen과 양방향 바인딩).</summary>
        public bool IsPaletteOpen
        {
            get { return _isPaletteOpen; }
            set { SetProperty(ref _isPaletteOpen, value); }
        }

        // ── 동작 ────────────────────────────────────

        private bool CanLoad()
        {
            return !_isBusy && !string.IsNullOrWhiteSpace(_rootPath);
        }

        private async Task LoadAsync(bool preservePositions)
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

                BuildGraph(data, preservePositions);
                _hasLoaded = true;

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

        private async void OnDepthDebounceTick(object sender, EventArgs e)
        {
            _depthDebounce.Stop();
            if (_hasLoaded && !string.IsNullOrWhiteSpace(_rootPath))
            {
                await LoadAsync(true); // 깊이 변경 → 위치 보존 재스캔
            }
        }

        /// <summary>
        /// 스캔 결과로부터 NodeViewModel/EdgeViewModel을 만들고,
        /// 힘-기반 시뮬레이션을 초기화한 뒤 애니메이션을 시작한다.
        /// </summary>
        private void BuildGraph(GraphData data, bool preservePositions)
        {
            // 진행 중 시뮬레이션 정지
            _simTimer.Stop();

            // 위치 보존: 기존 노드의 경로별 상태(좌표/고정/색)를 스냅샷
            var snapshot = new Dictionary<string, NodeSnapshot>();
            if (preservePositions)
            {
                foreach (NodeViewModel old in Nodes)
                {
                    if (!snapshot.ContainsKey(old.FullPath))
                    {
                        snapshot[old.FullPath] = new NodeSnapshot
                        {
                            X = old.X,
                            Y = old.Y,
                            IsPinned = old.IsPinned,
                            Fill = old.Fill
                        };
                    }
                }
            }

            // 선택 상태는 재구성 시 해제
            _selectedNode = null;

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

            // 초기 시드 배치(격자) — 모든 노드에 좌표 부여
            double area = Math.Max(600, Math.Sqrt(nodeList.Count) * 90);
            _layout.Arrange(nodeList, area, area);

            // 보존 대상은 기존 좌표로 덮어쓰기(새 노드는 시드 좌표 유지)
            if (preservePositions)
            {
                foreach (NodeViewModel vm in nodeList)
                {
                    NodeSnapshot s;
                    if (snapshot.TryGetValue(vm.FullPath, out s))
                    {
                        vm.X = s.X;
                        vm.Y = s.Y;
                        vm.IsPinned = s.IsPinned;
                        vm.Fill = s.Fill;
                    }
                }
            }

            // 노드 등록
            foreach (NodeViewModel vm in nodeList)
            {
                Nodes.Add(vm);
            }

            // 엣지 + 링크 + VM 자식 트리 구성
            var links = new List<GraphLink>();
            foreach (FileNodeModel model in data.AllNodes)
            {
                if (model.Parent != null && map.ContainsKey(model.Parent))
                {
                    NodeViewModel parentVm = map[model.Parent];
                    NodeViewModel childVm = map[model];
                    Edges.Add(new EdgeViewModel(parentVm, childVm));
                    links.Add(new GraphLink(parentVm, childVm));
                    parentVm.Children.Add(childVm); // 선택 시 자손 순회용
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
                // 위치 보존이면 낮은 에너지로 시작(기존 배치 유지), 신규면 완전 확산
                double energy = preservePositions ? 0.12 : 1.0;
                _simulation.Initialize(bodies, links, cx, cy, energy);
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

        // ── 선택 / 하이라이트 ────────────────────────

        /// <summary>
        /// 노드를 선택한다. 그 노드와 모든 하위 자손을 강조하고,
        /// 나머지 노드/엣지는 흐리게 처리한다.
        /// </summary>
        public void SelectNode(NodeViewModel node)
        {
            if (node == null)
            {
                ClearSelection();
                return;
            }

            _selectedNode = node;

            // 선택 노드 + 모든 하위 자손 수집
            var set = new HashSet<NodeViewModel>();
            CollectSubtree(node, set);

            foreach (NodeViewModel n in Nodes)
            {
                bool inSet = set.Contains(n);
                n.IsHighlighted = inSet;
                n.IsSelected = (n == node);

                if (n == node)
                {
                    n.ApplySelectedStyle();
                }
                else
                {
                    n.ResetStyle();
                }
                // ResetStyle/ApplySelectedStyle 이후에 흐림 적용
                n.Opacity = inSet ? 1.0 : DimOpacity;
            }

            foreach (EdgeViewModel ed in Edges)
            {
                bool both = set.Contains(ed.Parent) && set.Contains(ed.Child);
                ed.Opacity = both ? 1.0 : DimEdgeOpacity;
            }
        }

        /// <summary>선택을 해제하고 모든 강조/흐림을 원래대로 되돌린다.</summary>
        public void ClearSelection()
        {
            _selectedNode = null;

            foreach (NodeViewModel n in Nodes)
            {
                n.IsHighlighted = false;
                n.IsSelected = false;
                n.ResetStyle(); // 테두리/불투명도 원복
            }

            foreach (EdgeViewModel ed in Edges)
            {
                ed.Opacity = 1.0;
            }
        }

        private void CollectSubtree(NodeViewModel root, HashSet<NodeViewModel> set)
        {
            if (!set.Add(root))
            {
                return; // 이미 방문(순환 방지)
            }
            foreach (NodeViewModel child in root.Children)
            {
                CollectSubtree(child, set);
            }
        }

        // ── 색상 팔레트 ──────────────────────────────

        /// <summary>우클릭한 노드를 색상 대상에 두고 팔레트를 연다(View에서 호출).</summary>
        public void OpenPaletteFor(NodeViewModel node)
        {
            if (node == null)
            {
                return;
            }
            _colorTargetNode = node;
            IsPaletteOpen = true;
        }

        private void OnPickColor(PaletteColor pick)
        {
            if (_colorTargetNode != null && pick != null)
            {
                ApplyColorToSubtree(_colorTargetNode, pick.Color);
            }
            IsPaletteOpen = false;
        }

        private void OnResetColor()
        {
            if (_colorTargetNode != null)
            {
                ResetSubtreeColor(_colorTargetNode);
            }
            IsPaletteOpen = false;
        }

        /// <summary>
        /// 대상 노드(depth 0)와 모든 하위 자손에 기준 색을 적용한다.
        /// 자손은 상대 깊이에 따라 흰색을 더 섞어 옅어진다. 기존 색은 덮어쓴다.
        /// </summary>
        private void ApplyColorToSubtree(NodeViewModel root, Color baseColor)
        {
            var queue = new Queue<NodeDepth>();
            var visited = new HashSet<NodeViewModel>();
            queue.Enqueue(new NodeDepth(root, 0));

            while (queue.Count > 0)
            {
                NodeDepth nd = queue.Dequeue();
                if (!visited.Add(nd.Node))
                {
                    continue;
                }

                Color shade = _colorCalculator.GetShade(baseColor, nd.Depth);
                var brush = new SolidColorBrush(shade);
                brush.Freeze();
                nd.Node.Fill = brush;

                foreach (NodeViewModel child in nd.Node.Children)
                {
                    queue.Enqueue(new NodeDepth(child, nd.Depth + 1));
                }
            }
        }

        /// <summary>대상 노드와 모든 하위 자손을 기본 회색으로 되돌린다.</summary>
        private void ResetSubtreeColor(NodeViewModel root)
        {
            var set = new HashSet<NodeViewModel>();
            CollectSubtree(root, set);
            foreach (NodeViewModel n in set)
            {
                n.Fill = GrayBrush;
            }
        }

        private static IReadOnlyList<PaletteColor> BuildPalette()
        {
            // 현대적인 톤의 기준 색 모음(depth 0의 색).
            var colors = new List<PaletteColor>
            {
                new PaletteColor(Color.FromRgb(0xEF, 0x44, 0x44)), // red
                new PaletteColor(Color.FromRgb(0xF9, 0x73, 0x16)), // orange
                new PaletteColor(Color.FromRgb(0xF5, 0x9E, 0x0B)), // amber
                new PaletteColor(Color.FromRgb(0xEA, 0xB3, 0x08)), // yellow
                new PaletteColor(Color.FromRgb(0x84, 0xCC, 0x16)), // lime
                new PaletteColor(Color.FromRgb(0x22, 0xC5, 0x5E)), // green
                new PaletteColor(Color.FromRgb(0x10, 0xB9, 0x81)), // emerald
                new PaletteColor(Color.FromRgb(0x14, 0xB8, 0xA6)), // teal
                new PaletteColor(Color.FromRgb(0x06, 0xB6, 0xD4)), // cyan
                new PaletteColor(Color.FromRgb(0x3B, 0x82, 0xF6)), // blue
                new PaletteColor(Color.FromRgb(0x63, 0x66, 0xF1)), // indigo
                new PaletteColor(Color.FromRgb(0x8B, 0x5C, 0xF6)), // violet
                new PaletteColor(Color.FromRgb(0xA8, 0x55, 0xF7)), // purple
                new PaletteColor(Color.FromRgb(0xEC, 0x48, 0x99)), // pink
                new PaletteColor(Color.FromRgb(0xF4, 0x3F, 0x5E)), // rose
                new PaletteColor(Color.FromRgb(0x64, 0x74, 0x8B))  // slate
            };
            return colors;
        }

        /// <summary>색상 적용 BFS에서 쓰는 (노드, 상대깊이) 쌍.</summary>
        private struct NodeDepth
        {
            public readonly NodeViewModel Node;
            public readonly int Depth;
            public NodeDepth(NodeViewModel node, int depth)
            {
                Node = node;
                Depth = depth;
            }
        }

        /// <summary>위치 보존용 스냅샷.</summary>
        private class NodeSnapshot
        {
            public double X;
            public double Y;
            public bool IsPinned;
            public System.Windows.Media.Brush Fill;
        }
    }
}
