using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
        private readonly IFileOperationService _fileOps;
        private readonly IDialogService _dialog;
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

        // 검색 / 하이라이트 모드
        private string _searchText;
        private int _matchCount;
        private HighlightMode _highlightMode = HighlightMode.None;
        private readonly DispatcherTimer _searchDebounce;

        // 색상 팔레트 상태
        private NodeViewModel _colorTargetNode;
        private bool _isPaletteOpen;
        private static readonly Brush GrayBrush = MakeFrozenGray();

        // 선택 시 흐리게 할 불투명도
        private const double DimOpacity = 0.22;
        private const double DimEdgeOpacity = 0.10;

        /// <summary>현재 하이라이트 소스. 선택과 검색은 상호 배타적이며 마지막 이벤트가 이긴다.</summary>
        private enum HighlightMode { None, Selection, Search }

        public MainViewModel(IFolderScanner scanner,
                             IGraphLayoutEngine layout,
                             IForceDirectedSimulation simulation,
                             INodeColorCalculator colorCalculator,
                             IFileOperationService fileOps,
                             IDialogService dialog)
        {
            if (scanner == null) throw new ArgumentNullException("scanner");
            if (layout == null) throw new ArgumentNullException("layout");
            if (simulation == null) throw new ArgumentNullException("simulation");
            if (colorCalculator == null) throw new ArgumentNullException("colorCalculator");
            if (fileOps == null) throw new ArgumentNullException("fileOps");
            if (dialog == null) throw new ArgumentNullException("dialog");

            _scanner = scanner;
            _layout = layout;
            _simulation = simulation;
            _colorCalculator = colorCalculator;
            _fileOps = fileOps;
            _dialog = dialog;

            _depth = 3;
            _includeHidden = false;
            _statusText = "폴더 경로를 입력하고 불러오기를 누르세요.";

            Nodes = new BulkObservableCollection<NodeViewModel>();
            Edges = new BulkObservableCollection<EdgeViewModel>();
            ColorStats = new ObservableCollection<ColorStat>();
            PaletteColors = BuildPalette();

            // 레이아웃 애니메이션용 타이머(약 60fps)
            _simTimer = new DispatcherTimer(DispatcherPriority.Background);
            _simTimer.Interval = TimeSpan.FromMilliseconds(16);
            _simTimer.Tick += OnSimulationTick;

            // 깊이 슬라이더 디바운스(끌고 있는 동안 연속 재스캔 방지)
            _depthDebounce = new DispatcherTimer();
            _depthDebounce.Interval = TimeSpan.FromMilliseconds(300);
            _depthDebounce.Tick += OnDepthDebounceTick;

            // 검색 디바운스(100ms)
            _searchDebounce = new DispatcherTimer();
            _searchDebounce.Interval = TimeSpan.FromMilliseconds(100);
            _searchDebounce.Tick += OnSearchDebounceTick;

            LoadCommand = new RelayCommand(async () => await LoadAsync(false), CanLoad);
            PickColorCommand = new RelayCommand<PaletteColor>(OnPickColor);
            ResetColorCommand = new RelayCommand(OnResetColor);
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            ToggleMinimapCommand = new RelayCommand(() => ShowMinimap = !ShowMinimap);
            ExportImageCommand = new RelayCommand(OnExportImage);
        }

        private static Brush MakeFrozenGray()
        {
            var b = new SolidColorBrush(Colors.Gray);
            b.Freeze();
            return b;
        }

        // ── 바인딩 속성 ──────────────────────────────

        public BulkObservableCollection<NodeViewModel> Nodes { get; private set; }
        public BulkObservableCollection<EdgeViewModel> Edges { get; private set; }

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
            set
            {
                if (SetProperty(ref _includeHidden, value) && _hasLoaded)
                {
                    // 체크 즉시 재스캔(위치 보존). fire-and-forget(이전 스캔은 내부에서 취소).
                    _ = LoadAsync(true);
                }
            }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    OnPropertyChanged("HasSearchText");
                    // 입력 즉시가 아니라 100ms 디바운스 후 적용
                    _searchDebounce.Stop();
                    _searchDebounce.Start();
                }
            }
        }

        /// <summary>현재 검색어에 매칭된 노드 수.</summary>
        public int MatchCount
        {
            get { return _matchCount; }
            set { SetProperty(ref _matchCount, value); }
        }

        /// <summary>검색어가 비어있지 않은지(매칭 수 표시 여부).</summary>
        public bool HasSearchText
        {
            get { return !string.IsNullOrEmpty(_searchText); }
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
        public ICommand ClearSearchCommand { get; private set; }
        public ICommand ToggleMinimapCommand { get; private set; }
        public ICommand ExportImageCommand { get; private set; }

        /// <summary>PNG 내보내기 요청. View가 구독해 실제 렌더링/저장을 수행한다.</summary>
        public event EventHandler ExportImageRequested;

        private bool _showMinimap;
        /// <summary>미니맵 표시 여부.</summary>
        public bool ShowMinimap
        {
            get { return _showMinimap; }
            set { SetProperty(ref _showMinimap, value); }
        }

        /// <summary>우클릭 팔레트에 표시할 기준 색 목록.</summary>
        public IReadOnlyList<PaletteColor> PaletteColors { get; private set; }

        /// <summary>색상별 노드 집계(좌상단 표시). 색을 바꿀 때마다 갱신.</summary>
        public ObservableCollection<ColorStat> ColorStats { get; private set; }

        /// <summary>집계에 표시할 항목이 있는지(패널 표시 여부).</summary>
        public bool HasColorStats
        {
            get { return ColorStats != null && ColorStats.Count > 0; }
        }

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

                // 재스캔으로 노드가 새로 생성되었으므로, 검색어가 있으면 다시 적용
                ApplySearchOrClear();

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
                            Z = old.Z,
                            IsPinned = old.IsPinned,
                            Fill = old.Fill,
                            IsColored = old.IsColored,
                            ColorBase = old.ColorBase,
                            ColorDepth = old.ColorDepth,
                            ColorRootName = old.ColorRootName
                        };
                    }
                }
            }

            // 선택/하이라이트 상태는 재구성 시 해제(이후 LoadAsync에서 검색 재적용)
            _selectedNode = null;
            _highlightMode = HighlightMode.None;

            // (Nodes/Edges는 아래에서 Reset으로 일괄 교체 → 개별 Clear 불필요)

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

            // 3D 입체감: 깊이에 따라 Z를 분산(물리 3축 확장 전 임시 분포).
            // 같은 깊이라도 약간씩 흩어지게 해 평면이 아닌 공간감을 준다.
            var zrnd = new Random(20240624);
            foreach (NodeViewModel vm in nodeList)
            {
                double layer = vm.Depth * 140.0;                 // 깊이별 층
                double jitter = (zrnd.NextDouble() - 0.5) * 120.0; // 층 내 흩뜨림
                vm.Z = layer + jitter;
            }

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
                        vm.Z = s.Z;
                        vm.IsPinned = s.IsPinned;
                        vm.Fill = s.Fill;
                        vm.IsColored = s.IsColored;
                        vm.ColorBase = s.ColorBase;
                        vm.ColorDepth = s.ColorDepth;
                        vm.ColorRootName = s.ColorRootName;
                    }
                }
            }

            // 노드 일괄 등록(알림 1회 → 뷰가 한 번만 재빌드)
            Nodes.Reset(nodeList);

            // 엣지 + 링크 + VM 자식 트리 구성
            var links = new List<GraphLink>();
            var edgeList = new List<EdgeViewModel>();
            foreach (FileNodeModel model in data.AllNodes)
            {
                if (model.Parent != null && map.ContainsKey(model.Parent))
                {
                    NodeViewModel parentVm = map[model.Parent];
                    NodeViewModel childVm = map[model];
                    edgeList.Add(new EdgeViewModel(parentVm, childVm));
                    links.Add(new GraphLink(parentVm, childVm));
                    parentVm.Children.Add(childVm); // 선택 시 자손 순회용
                }
            }
            Edges.Reset(edgeList);

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

            // 색상 집계 갱신(재스캔/이동 후 색 상태 반영)
            UpdateColorStats();
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

        /// <summary>
        /// 시뮬레이션을 즉시 멈춘다. 노드를 드래그하는 동안 다른 노드가 들썩이지 않게 하여
        /// 대상 폴더가 도망가지 않도록 한다(드롭 후 ReheatSimulation으로 다시 정리).
        /// </summary>
        public void PauseSimulation()
        {
            _simTimer.Stop();
        }

        /// <summary>PNG 내보내기 요청을 View로 전달한다(렌더링은 View가 수행).</summary>
        private void OnExportImage()
        {
            var handler = ExportImageRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        // ── 선택 / 검색 하이라이트 (마지막 이벤트 우선) ──

        /// <summary>
        /// 노드를 선택한다(클릭). 선택 모드로 전환하며, 그 노드와 모든 하위 자손을
        /// 강조하고 나머지는 흐리게 한다. 검색 하이라이트가 있었다면 덮어쓴다.
        /// </summary>
        public void SelectNode(NodeViewModel node)
        {
            if (node == null)
            {
                ClearSelection();
                return;
            }

            _highlightMode = HighlightMode.Selection;
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

        /// <summary>
        /// 빈 캔버스 클릭 시 호출. 선택을 해제하되, 검색어가 남아 있으면
        /// 검색 하이라이트로 복귀하고, 없으면 완전히 해제한다.
        /// </summary>
        public void ClearSelection()
        {
            string q = (_searchText ?? string.Empty).Trim();
            if (q.Length > 0)
            {
                ApplySearch(q); // 검색 하이라이트로 복귀
            }
            else
            {
                ClearAllHighlight();
            }
        }

        /// <summary>모든 강조/흐림을 원래대로 되돌린다(하이라이트 없음).</summary>
        private void ClearAllHighlight()
        {
            _highlightMode = HighlightMode.None;
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

        private void OnSearchDebounceTick(object sender, EventArgs e)
        {
            _searchDebounce.Stop();
            ApplySearchOrClear();
        }

        /// <summary>검색어가 있으면 검색 하이라이트, 없으면(검색 모드였다면) 해제.</summary>
        private void ApplySearchOrClear()
        {
            string q = (_searchText ?? string.Empty).Trim();
            if (q.Length == 0)
            {
                MatchCount = 0;
                // 검색으로 켜둔 하이라이트만 끈다(선택 모드면 건드리지 않음).
                if (_highlightMode == HighlightMode.Search)
                {
                    ClearAllHighlight();
                }
                return;
            }
            ApplySearch(q);
        }

        /// <summary>
        /// 검색 하이라이트를 적용한다. 매칭된 노드 "자기 자신만" 강조하고(자손 미포함),
        /// 나머지 노드와 모든 엣지는 흐리게 한다. 검색 모드로 전환된다.
        /// </summary>
        private void ApplySearch(string query)
        {
            _highlightMode = HighlightMode.Search;
            _selectedNode = null;

            int count = 0;
            foreach (NodeViewModel n in Nodes)
            {
                bool match = n.Name != null &&
                             n.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                n.IsHighlighted = match;
                n.IsSelected = false;

                if (match)
                {
                    n.ApplySearchStyle();
                    n.Opacity = 1.0;
                    count++;
                }
                else
                {
                    n.ResetStyle();
                    n.Opacity = DimOpacity;
                }
            }

            // 매칭은 노드 자신만이므로 엣지는 모두 흐리게
            foreach (EdgeViewModel ed in Edges)
            {
                ed.Opacity = DimEdgeOpacity;
            }

            MatchCount = count;
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
                UpdateColorStats();
            }
            IsPaletteOpen = false;
        }

        private void OnResetColor()
        {
            if (_colorTargetNode != null)
            {
                ResetSubtreeColor(_colorTargetNode);
                UpdateColorStats();
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
                // 색 상태 저장(이동 시 자손 색 재지정/스냅샷 복원에 사용)
                nd.Node.IsColored = true;
                nd.Node.ColorBase = baseColor;
                nd.Node.ColorDepth = nd.Depth;
                nd.Node.ColorRootName = root.Name;   // 색칠 출발점 이름(집계용)

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
                n.IsColored = false;
                n.ColorRootName = null;
            }
        }

        /// <summary>
        /// 색상별 노드 집계를 다시 계산한다. 칠해진 노드는 기준색(ColorBase)별로 묶고,
        /// 라벨은 그 색을 칠한 최상위 노드 이름(여럿이면 "이름 외 N")으로 표시한다.
        /// 칠하지 않은 노드는 "회색"으로 묶는다. 색을 바꿀 때마다 호출.
        /// </summary>
        private void UpdateColorStats()
        {
            int total = Nodes.Count;
            ColorStats.Clear();

            if (total == 0)
            {
                OnPropertyChanged("HasColorStats");
                return;
            }

            // 기준색(RGB) → 집계 정보(개수 + 루트 이름들)
            var byColor = new Dictionary<int, ColorAccum>();
            var order = new List<int>();      // 등장 순서 유지
            int grayCount = 0;

            foreach (NodeViewModel n in Nodes)
            {
                if (!n.IsColored)
                {
                    grayCount++;
                    continue;
                }
                Color c = n.ColorBase;
                int key = (c.R << 16) | (c.G << 8) | c.B;

                ColorAccum acc;
                if (!byColor.TryGetValue(key, out acc))
                {
                    acc = new ColorAccum();
                    byColor[key] = acc;
                    order.Add(key);
                }
                acc.Count++;

                string rootName = string.IsNullOrEmpty(n.ColorRootName) ? "(이름 없음)" : n.ColorRootName;
                if (!acc.RootSet.Contains(rootName))
                {
                    acc.RootSet.Add(rootName);
                    acc.RootOrder.Add(rootName);   // 처음 등장한 순서 보존
                }
            }

            // 개수 많은 순 정렬
            order.Sort(delegate (int a, int b) { return byColor[b].Count.CompareTo(byColor[a].Count); });

            foreach (int key in order)
            {
                ColorAccum acc = byColor[key];
                byte r = (byte)((key >> 16) & 0xFF);
                byte g = (byte)((key >> 8) & 0xFF);
                byte b = (byte)(key & 0xFF);
                var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                brush.Freeze();

                // 라벨: 첫 루트 이름 (+ 외 N)
                string first = acc.RootOrder.Count > 0 ? acc.RootOrder[0] : "(이름 없음)";
                int others = acc.RootOrder.Count - 1;
                string label = others > 0 ? string.Format("{0} 외 {1}", first, others) : first;

                double pct = 100.0 * acc.Count / total;
                ColorStats.Add(new ColorStat(brush, label, acc.Count, pct));
            }

            // 회색(미착색)은 맨 아래에
            if (grayCount > 0)
            {
                double pct = 100.0 * grayCount / total;
                ColorStats.Add(new ColorStat(GrayBrush, "-", grayCount, pct));
            }

            OnPropertyChanged("HasColorStats");
        }

        /// <summary>색별 집계용 임시 누적(개수 + 루트 이름 집합).</summary>
        private class ColorAccum
        {
            public int Count;
            public readonly HashSet<string> RootSet = new HashSet<string>();
            public readonly List<string> RootOrder = new List<string>();
        }

        // ── 파일 열기 / 이동 ─────────────────────────

        /// <summary>파일 노드를 OS 기본 앱으로 연다(더블클릭).</summary>
        public void OpenFile(NodeViewModel node)
        {
            if (node == null || node.Type != NodeType.File)
            {
                return; // 폴더는 열지 않음
            }
            try
            {
                _fileOps.OpenWithDefaultApp(node.FullPath);
            }
            catch (Exception ex)
            {
                _dialog.ShowError("파일을 열 수 없습니다.\n" + ex.Message, "오류");
            }
        }

        /// <summary>
        /// 노드를 대상 폴더로 이동한다(드래그→폴더 드롭).
        /// 확인 팝업 → 실제 이동 → 재스캔 → 새 부모 기준 색 재지정 순서.
        /// </summary>
        public async void RequestMoveAsync(NodeViewModel dragged, NodeViewModel targetFolder)
        {
            if (dragged == null || targetFolder == null || dragged == targetFolder)
            {
                return;
            }

            // 자기 자신/자손으로는 이동 불가(폴더를 자기 하위로)
            var sub = new HashSet<NodeViewModel>();
            CollectSubtree(dragged, sub);
            if (sub.Contains(targetFolder))
            {
                return;
            }

            // 이미 그 폴더 안에 있으면(동일 폴더) 무시
            string sourceParent = Path.GetDirectoryName(dragged.FullPath);
            if (string.Equals(sourceParent, targetFolder.FullPath,
                              StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string name = dragged.Name;
            bool isFolder = dragged.Type == NodeType.Folder;
            string targetPath = targetFolder.FullPath;

            // 확인 팝업
            string message = string.Format("'{0}'을(를)\n'{1}'(으)로 이동하시겠습니까?",
                                            name, targetFolder.Name);
            if (!_dialog.Confirm(message, "파일 이동"))
            {
                return;
            }

            // 실제 이동
            try
            {
                _fileOps.Move(dragged.FullPath, targetPath, isFolder);
            }
            catch (Exception ex)
            {
                _dialog.ShowError("이동에 실패했습니다.\n" + ex.Message, "오류");
                return; // 그래프 상태 그대로 유지
            }

            // 성공 → 위치 보존 재스캔
            await LoadAsync(true);

            // 이동한 노드를 새 경로로 찾아 새 부모 기준 색 재지정 + 위치 정돈
            string newPath = Path.Combine(targetPath, name);
            NodeViewModel movedNode = FindByPath(newPath);
            NodeViewModel newParent = FindByPath(targetPath);

            if (movedNode != null)
            {
                RecolorMovedSubtree(movedNode, newParent);
                if (newParent != null)
                {
                    movedNode.X = newParent.X + 30;
                    movedNode.Y = newParent.Y + 30;
                }
                ReheatSimulation();
                UpdateColorStats(); // 이동 노드 재색칠 반영
            }

            StatusText = string.Format("'{0}' 이동 완료", name);
        }

        /// <summary>
        /// 이동한 노드 서브트리를 새 부모의 색 계열로 칠한다.
        /// 새 부모가 색칠돼 있으면 그 기준색 + (부모 깊이+1)부터의 명도, 회색이면 그대로 둔다.
        /// </summary>
        private void RecolorMovedSubtree(NodeViewModel moved, NodeViewModel newParent)
        {
            if (newParent == null || !newParent.IsColored)
            {
                return; // 새 부모가 회색이면 이동 노드도 회색(재스캔 기본값 유지)
            }

            Color baseColor = newParent.ColorBase;
            int startDepth = newParent.ColorDepth + 1;
            string colorRootName = !string.IsNullOrEmpty(newParent.ColorRootName)
                ? newParent.ColorRootName : newParent.Name;

            var queue = new Queue<NodeDepth>();
            var visited = new HashSet<NodeViewModel>();
            queue.Enqueue(new NodeDepth(moved, startDepth));

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
                nd.Node.IsColored = true;
                nd.Node.ColorBase = baseColor;
                nd.Node.ColorDepth = nd.Depth;
                nd.Node.ColorRootName = colorRootName;

                foreach (NodeViewModel child in nd.Node.Children)
                {
                    queue.Enqueue(new NodeDepth(child, nd.Depth + 1));
                }
            }
        }

        private NodeViewModel FindByPath(string fullPath)
        {
            foreach (NodeViewModel n in Nodes)
            {
                if (string.Equals(n.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return n;
                }
            }
            return null;
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
            public double Z;
            public bool IsPinned;
            public System.Windows.Media.Brush Fill;
            public bool IsColored;
            public Color ColorBase;
            public int ColorDepth;
            public string ColorRootName;
        }
    }
}
