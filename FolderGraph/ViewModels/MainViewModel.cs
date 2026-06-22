using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
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
    /// Phase 1: 경로 입력 → 비동기 스캔 → 노드/엣지 생성 → 격자 배치까지.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private readonly IFolderScanner _scanner;
        private readonly IGraphLayoutEngine _layout;

        private string _rootPath;
        private int _depth;
        private bool _includeHidden;
        private bool _isBusy;
        private string _statusText;
        private CancellationTokenSource _cts;

        public MainViewModel(IFolderScanner scanner, IGraphLayoutEngine layout)
        {
            if (scanner == null) throw new ArgumentNullException("scanner");
            if (layout == null) throw new ArgumentNullException("layout");

            _scanner = scanner;
            _layout = layout;

            _depth = 3;
            _includeHidden = false;
            _statusText = "폴더 경로를 입력하고 불러오기를 누르세요.";

            Nodes = new ObservableCollection<NodeViewModel>();
            Edges = new ObservableCollection<EdgeViewModel>();

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
        /// 스캔 결과로부터 NodeViewModel/EdgeViewModel을 만들고 배치한다.
        /// </summary>
        private void BuildGraph(GraphData data)
        {
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

            // 배치(임시 격자) — 영역 크기는 노드 수에 따라 동적으로
            double area = Math.Max(600, Math.Sqrt(nodeList.Count) * 90);
            _layout.Arrange(nodeList, area, area);

            // 노드 등록
            foreach (NodeViewModel vm in nodeList)
            {
                Nodes.Add(vm);
            }

            // 엣지 생성: 부모가 있는 노드마다 부모-자식 선
            foreach (FileNodeModel model in data.AllNodes)
            {
                if (model.Parent != null && map.ContainsKey(model.Parent))
                {
                    NodeViewModel parentVm = map[model.Parent];
                    NodeViewModel childVm = map[model];
                    Edges.Add(new EdgeViewModel(parentVm, childVm));
                }
            }
        }
    }
}
