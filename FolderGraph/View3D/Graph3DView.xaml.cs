using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using FolderGraph.ViewModels;

namespace FolderGraph.View3D
{
    /// <summary>
    /// 3D 그래프 뷰.
    /// 3D-1: 렌더링 + CAD 카메라 / 3D-2: 물리 3축 / 3D-3: 레이캐스팅 인터랙션.
    /// - 좌클릭 드래그: 회전, 휠 버튼 드래그: 팬, 휠: 줌
    /// - 노드 클릭: 선택(+자손 하이라이트), 빈 곳 클릭: 선택 해제
    /// - 노드 호버: 이름 툴팁 + 강조, 더블클릭: 파일 열기
    /// </summary>
    public partial class Graph3DView : UserControl
    {
        private OrbitCameraController _orbit;
        private MeshGeometry3D _unitSphere;

        private readonly Dictionary<NodeViewModel, GeometryModel3D> _nodeModels =
            new Dictionary<NodeViewModel, GeometryModel3D>();
        private readonly Dictionary<NodeViewModel, TranslateTransform3D> _nodeTransforms =
            new Dictionary<NodeViewModel, TranslateTransform3D>();
        private readonly Dictionary<GeometryModel3D, NodeViewModel> _modelToNode =
            new Dictionary<GeometryModel3D, NodeViewModel>();
        private readonly Dictionary<NodeViewModel, DisplayState> _displayCache =
            new Dictionary<NodeViewModel, DisplayState>();

        private readonly Model3DGroup _nodeGroup = new Model3DGroup();
        private readonly Model3DGroup _edgeGroup = new Model3DGroup();
        private MeshGeometry3D _edgeMesh;

        private MainViewModel _vm;

        // 마우스 상태
        private bool _orbiting;
        private bool _panning;
        private Point _lastMouse;
        private Point _leftPressPoint;
        private bool _leftPressed;
        private bool _suppressClick;
        private NodeViewModel _hoveredNode;

        // 노드 드래그 상태
        private NodeViewModel _pressedNode;     // 좌클릭으로 누른 노드(있으면)
        private bool _draggingNode;
        private Vector3D _dragPlaneNormal;      // 드래그 평면 법선(카메라 정면)
        private Vector3D _grabOffset3D;         // 노드 중심과 잡은 지점의 3D 차이
        private bool _dragFreezeOthers;

        private const double ClickThreshold = 4.0;

        // 선택/호버 발광 색
        private static readonly Color SelectedGlow = Color.FromRgb(0x60, 0x40, 0x00);
        private static readonly Color HoverGlow = Color.FromRgb(0x2A, 0x2A, 0x2A);

        public Graph3DView()
        {
            InitializeComponent();

            _unitSphere = SphereMeshFactory.CreateUnitSphere(12, 16);
            _orbit = new OrbitCameraController(Camera);

            NodeContainer.Content = _nodeGroup;
            EdgeContainer.Content = _edgeGroup;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private MainViewModel ViewModel { get { return DataContext as MainViewModel; } }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HookViewModel();
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            UnhookViewModel();
        }

        private void HookViewModel()
        {
            if (_vm == ViewModel)
            {
                return;
            }
            UnhookViewModel();
            _vm = ViewModel;
            if (_vm != null)
            {
                _vm.Nodes.CollectionChanged += OnNodesChanged;
                _vm.Edges.CollectionChanged += OnEdgesChanged;
                RebuildAll();
            }
        }

        private void UnhookViewModel()
        {
            if (_vm != null)
            {
                _vm.Nodes.CollectionChanged -= OnNodesChanged;
                _vm.Edges.CollectionChanged -= OnEdgesChanged;
                _vm = null;
            }
        }

        private void OnNodesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _hoveredNode = null;
            HoverTip.Visibility = Visibility.Collapsed;
            RebuildNodes();
        }

        private void OnEdgesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildEdges();
        }

        // ── 모델 빌드 ────────────────────────────────

        private void RebuildAll()
        {
            RebuildNodes();
            RebuildEdges();
            FitCameraToGraph();
        }

        private void RebuildNodes()
        {
            _nodeGroup.Children.Clear();
            _nodeModels.Clear();
            _nodeTransforms.Clear();
            _modelToNode.Clear();
            _displayCache.Clear();

            if (_vm == null)
            {
                return;
            }

            foreach (NodeViewModel node in _vm.Nodes)
            {
                GeometryModel3D model = CreateNodeModel(node);
                _nodeGroup.Children.Add(model);
            }
        }

        private GeometryModel3D CreateNodeModel(NodeViewModel node)
        {
            var material = new DiffuseMaterial(new SolidColorBrush(ToColor(node.Fill)));

            var translate = new TranslateTransform3D(node.X, -node.Y, node.Z);
            double r = node.Radius;
            var scale = new ScaleTransform3D(r, r, r);

            var group = new Transform3DGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);

            var model = new GeometryModel3D(_unitSphere, material);
            model.BackMaterial = material;
            model.Transform = group;

            _nodeModels[node] = model;
            _nodeTransforms[node] = translate;
            _modelToNode[model] = node;
            return model;
        }

        private void RebuildEdges()
        {
            _edgeGroup.Children.Clear();
            if (_vm == null)
            {
                return;
            }

            var mesh = new MeshGeometry3D();
            BuildEdgeMesh(mesh);

            var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)));
            var model = new GeometryModel3D(mesh, material);
            model.BackMaterial = material;
            _edgeGroup.Children.Add(model);
            _edgeMesh = mesh;
        }

        private void BuildEdgeMesh(MeshGeometry3D mesh)
        {
            mesh.Positions.Clear();
            mesh.TriangleIndices.Clear();

            if (_vm == null)
            {
                return;
            }

            const double half = 1.2;
            int idx = 0;
            foreach (EdgeViewModel edge in _vm.Edges)
            {
                Point3D a = new Point3D(edge.Parent.X, -edge.Parent.Y, edge.Parent.Z);
                Point3D b = new Point3D(edge.Child.X, -edge.Child.Y, edge.Child.Z);

                Vector3D dir = b - a;
                if (dir.Length < 1e-6)
                {
                    continue;
                }
                dir.Normalize();
                Vector3D side = Vector3D.CrossProduct(dir, new Vector3D(0, 0, 1));
                if (side.Length < 1e-6)
                {
                    side = Vector3D.CrossProduct(dir, new Vector3D(0, 1, 0));
                }
                side.Normalize();
                side *= half;

                mesh.Positions.Add(a + side);
                mesh.Positions.Add(a - side);
                mesh.Positions.Add(b - side);
                mesh.Positions.Add(b + side);

                mesh.TriangleIndices.Add(idx);
                mesh.TriangleIndices.Add(idx + 1);
                mesh.TriangleIndices.Add(idx + 2);
                mesh.TriangleIndices.Add(idx);
                mesh.TriangleIndices.Add(idx + 2);
                mesh.TriangleIndices.Add(idx + 3);
                idx += 4;
            }
        }

        // ── 매 프레임 동기화 ─────────────────────────

        private void OnRendering(object sender, EventArgs e)
        {
            if (_vm == null)
            {
                return;
            }

            foreach (KeyValuePair<NodeViewModel, TranslateTransform3D> kv in _nodeTransforms)
            {
                NodeViewModel node = kv.Key;
                TranslateTransform3D t = kv.Value;
                t.OffsetX = node.X;
                t.OffsetY = -node.Y;
                t.OffsetZ = node.Z;

                UpdateNodeAppearance(node);
            }

            if (_edgeMesh != null)
            {
                BuildEdgeMesh(_edgeMesh);
            }
        }

        /// <summary>
        /// 노드의 표시 상태(색·흐림·선택·호버)가 바뀌었을 때만 머티리얼을 다시 만든다.
        /// 흐림은 색을 어둡게(불투명도 곱), 선택/호버는 발광(Emissive)으로 표현.
        /// </summary>
        private void UpdateNodeAppearance(NodeViewModel node)
        {
            GeometryModel3D model;
            if (!_nodeModels.TryGetValue(node, out model))
            {
                return;
            }

            Color baseC = ToColor(node.Fill);
            double op = node.Opacity;            // 1.0(강조) ~ 0.22(흐림)
            Color disp = Scale(baseC, op);
            bool selected = node.IsSelected;
            bool hover = (node == _hoveredNode);

            DisplayState prev;
            if (_displayCache.TryGetValue(node, out prev) &&
                prev.Color == disp && prev.Selected == selected && prev.Hover == hover)
            {
                return; // 변화 없음 → 머티리얼 유지
            }

            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(disp)));
            if (selected)
            {
                group.Children.Add(new EmissiveMaterial(new SolidColorBrush(SelectedGlow)));
            }
            else if (hover)
            {
                group.Children.Add(new EmissiveMaterial(new SolidColorBrush(HoverGlow)));
            }

            model.Material = group;
            model.BackMaterial = group;

            _displayCache[node] = new DisplayState
            {
                Color = disp,
                Selected = selected,
                Hover = hover
            };
        }

        // ── 레이캐스팅(히트테스트) ───────────────────

        /// <summary>화면 좌표에서 광선을 쏴 가장 앞쪽에 맞은 노드를 반환(없으면 null).</summary>
        private NodeViewModel HitTestNode(Point screenPoint)
        {
            NodeViewModel found = null;

            VisualTreeHelper.HitTest(
                Viewport,
                null,
                delegate (HitTestResult hr)
                {
                    var rayResult = hr as RayMeshGeometry3DHitTestResult;
                    if (rayResult != null)
                    {
                        var gm = rayResult.ModelHit as GeometryModel3D;
                        NodeViewModel nv;
                        if (gm != null && _modelToNode.TryGetValue(gm, out nv))
                        {
                            found = nv;
                            return HitTestResultBehavior.Stop; // 가장 앞쪽 노드에서 멈춤
                        }
                    }
                    return HitTestResultBehavior.Continue;
                },
                new PointHitTestParameters(screenPoint));

            return found;
        }

        // ── 마우스 ───────────────────────────────────

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 더블클릭 → 파일 열기(회전/선택/드래그 안 함)
            if (e.ClickCount == 2)
            {
                NodeViewModel dbl = HitTestNode(e.GetPosition(Root));
                if (dbl != null && ViewModel != null)
                {
                    ViewModel.OpenFile(dbl);
                }
                _suppressClick = true;
                return;
            }

            _suppressClick = false;
            _leftPressed = true;
            _leftPressPoint = e.GetPosition(Root);
            _lastMouse = _leftPressPoint;

            // 노드를 눌렀으면 노드 드래그 후보, 아니면 카메라 회전
            _pressedNode = HitTestNode(_leftPressPoint);
            _draggingNode = false;
            if (_pressedNode == null)
            {
                _orbiting = true;
            }

            Root.CaptureMouse();
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _orbiting = false;

            if (_leftPressed && !_suppressClick)
            {
                if (_draggingNode && _pressedNode != null)
                {
                    // 노드 드래그 종료 → 고정 해제(이후 주변과 함께 정리되도록)
                    _pressedNode.IsPinned = false;
                    if (ViewModel != null)
                    {
                        ViewModel.ReheatSimulation();
                    }
                }
                else
                {
                    double moved = (e.GetPosition(Root) - _leftPressPoint).Length;
                    if (moved < ClickThreshold && ViewModel != null)
                    {
                        // 거의 안 움직였으면 클릭 → 선택/해제
                        NodeViewModel node = HitTestNode(e.GetPosition(Root));
                        if (node != null)
                        {
                            ViewModel.SelectNode(node);
                        }
                        else
                        {
                            ViewModel.ClearSelection();
                        }
                    }
                }
            }

            _leftPressed = false;
            _draggingNode = false;
            _pressedNode = null;
            ReleaseIfIdle();
        }

        private void Root_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _panning = true;
                _lastMouse = e.GetPosition(Root);
                Root.CaptureMouse();
            }
        }

        private void Root_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _panning = false;
                ReleaseIfIdle();
            }
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(Root);

            // 노드 드래그 후보가 있으면: 임계값 넘으면 드래그 시작/진행
            if (_leftPressed && _pressedNode != null)
            {
                if (!_draggingNode)
                {
                    if ((p - _leftPressPoint).Length > ClickThreshold)
                    {
                        BeginNodeDrag(_leftPressPoint);
                    }
                }
                if (_draggingNode)
                {
                    DragNodeTo(p);
                    return;
                }
            }

            if (_orbiting || _panning)
            {
                double dx = p.X - _lastMouse.X;
                double dy = p.Y - _lastMouse.Y;
                _lastMouse = p;

                if (_orbiting)
                {
                    _orbit.Orbit(dx, dy);
                }
                else
                {
                    _orbit.Pan(dx, dy);
                }

                if (_hoveredNode != null)
                {
                    _hoveredNode = null;
                    HoverTip.Visibility = Visibility.Collapsed;
                }
                return;
            }

            // 유휴 상태 → 호버 처리
            NodeViewModel node = HitTestNode(p);
            UpdateHover(node, p);
        }

        /// <summary>노드 드래그 시작: 카메라 정면을 법선으로 하는 평면을 잡는다.</summary>
        private void BeginNodeDrag(Point screen)
        {
            _draggingNode = true;
            _pressedNode.IsPinned = true;

            _dragPlaneNormal = _orbit.Forward;
            Point3D nodePos = new Point3D(_pressedNode.X, -_pressedNode.Y, _pressedNode.Z);

            Point3D hit;
            if (_orbit.ScreenToPlane(screen.X, screen.Y, Root.ActualWidth, Root.ActualHeight,
                                     nodePos, _dragPlaneNormal, out hit))
            {
                _grabOffset3D = nodePos - hit; // 잡은 지점과 노드 중심의 차이
            }
            else
            {
                _grabOffset3D = new Vector3D(0, 0, 0);
            }

            _dragFreezeOthers = _pressedNode.IsSelected;
            if (ViewModel != null)
            {
                ViewModel.ReheatSimulation(); // 주변 노드가 따라오도록
            }
        }

        /// <summary>드래그 중: 노드를 화면 평면 위 마우스 위치로 옮긴다(주변은 시뮬레이션 반응).</summary>
        private void DragNodeTo(Point screen)
        {
            // 평면은 노드의 '현재' 위치를 지나도록 갱신(깊이 유지)
            Point3D nodePos = new Point3D(_pressedNode.X, -_pressedNode.Y, _pressedNode.Z);

            Point3D hit;
            if (_orbit.ScreenToPlane(screen.X, screen.Y, Root.ActualWidth, Root.ActualHeight,
                                     nodePos, _dragPlaneNormal, out hit))
            {
                Point3D target = hit + _grabOffset3D;
                // 3D → 노드 좌표(화면 Y는 -Y로 저장했으므로 되돌림)
                _pressedNode.X = target.X;
                _pressedNode.Y = -target.Y;
                _pressedNode.Z = target.Z;

                if (ViewModel != null)
                {
                    ViewModel.ReheatSimulation();
                }
            }
        }

        private void Root_MouseLeave(object sender, MouseEventArgs e)
        {
            _hoveredNode = null;
            HoverTip.Visibility = Visibility.Collapsed;
        }

        private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _orbit.Zoom(e.Delta);
        }

        private void UpdateHover(NodeViewModel node, Point pos)
        {
            _hoveredNode = node;

            if (node != null)
            {
                HoverTipText.Text = node.Name;
                double x = pos.X + 14;
                double y = pos.Y + 14;
                HoverTip.Margin = new Thickness(x, y, 0, 0);
                HoverTip.Visibility = Visibility.Visible;
                Cursor = Cursors.Hand;
            }
            else
            {
                HoverTip.Visibility = Visibility.Collapsed;
                Cursor = Cursors.Arrow;
            }
        }

        private void ReleaseIfIdle()
        {
            if (!_orbiting && !_panning)
            {
                Root.ReleaseMouseCapture();
            }
        }

        // ── 카메라 자동 맞춤 ─────────────────────────

        private void FitCameraToGraph()
        {
            if (_vm == null || _vm.Nodes.Count == 0)
            {
                return;
            }

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (NodeViewModel n in _vm.Nodes)
            {
                double x = n.X, y = -n.Y, z = n.Z;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }

            var center = new Point3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            double span = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            if (span < 100) span = 100;

            _orbit.SetView(center, span * 1.6);
        }

        // ── 색 변환 ──────────────────────────────────

        private static Color ToColor(Brush fill)
        {
            var scb = fill as SolidColorBrush;
            if (scb != null)
            {
                return scb.Color;
            }
            return Colors.Gray;
        }

        /// <summary>색 RGB를 비율만큼 어둡게(흐림 표현).</summary>
        private static Color Scale(Color c, double f)
        {
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            return Color.FromRgb(
                (byte)(c.R * f),
                (byte)(c.G * f),
                (byte)(c.B * f));
        }

        private struct DisplayState
        {
            public Color Color;
            public bool Selected;
            public bool Hover;
        }
    }
}
