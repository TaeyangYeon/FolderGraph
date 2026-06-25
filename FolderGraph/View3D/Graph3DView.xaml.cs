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
    /// 3D 그래프 뷰(3D-1: 렌더링 + CAD 카메라).
    /// 노드는 구체, 엣지는 선분으로 Viewport3D에 그린다.
    /// 카메라는 OrbitCameraController로 회전/팬/줌 한다.
    /// (노드 선택/색상/이동 등 인터랙션은 이후 단계 3D-3/3D-4에서 추가)
    /// </summary>
    public partial class Graph3DView : UserControl
    {
        private OrbitCameraController _orbit;
        private MeshGeometry3D _unitSphere;

        // 노드VM ↔ 3D 모델 매핑
        private readonly Dictionary<NodeViewModel, GeometryModel3D> _nodeModels =
            new Dictionary<NodeViewModel, GeometryModel3D>();
        private readonly Dictionary<NodeViewModel, TranslateTransform3D> _nodeTransforms =
            new Dictionary<NodeViewModel, TranslateTransform3D>();
        private readonly Model3DGroup _nodeGroup = new Model3DGroup();
        private readonly Model3DGroup _edgeGroup = new Model3DGroup();

        private MainViewModel _vm;

        // 마우스 상태
        private bool _orbiting;
        private bool _panning;
        private Point _lastMouse;

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
            var material = new DiffuseMaterial(ToBrush(node.Fill));

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
            return model;
        }

        private void RebuildEdges()
        {
            _edgeGroup.Children.Clear();
            if (_vm == null)
            {
                return;
            }

            // 모든 엣지를 하나의 메시로(선을 가는 사각기둥 대신 얇은 삼각형 라인으로)
            // 단순화를 위해 엣지는 매 프레임 Positions만 갱신하는 단일 메시로 만든다.
            var mesh = new MeshGeometry3D();
            BuildEdgeMesh(mesh);

            var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)));
            var model = new GeometryModel3D(mesh, material);
            model.BackMaterial = material;
            _edgeGroup.Children.Add(model);
            _edgeMesh = mesh;
        }

        private MeshGeometry3D _edgeMesh;

        /// <summary>엣지들을 얇은 삼각형 띠로 채운 메시를 만든다(카메라를 향하지 않는 단순 라인).</summary>
        private void BuildEdgeMesh(MeshGeometry3D mesh)
        {
            mesh.Positions.Clear();
            mesh.TriangleIndices.Clear();

            if (_vm == null)
            {
                return;
            }

            const double half = 1.2; // 선 두께 절반
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
                // 선에 수직인 한 방향(대충 위쪽과의 외적)
                Vector3D side = Vector3D.CrossProduct(dir, new Vector3D(0, 0, 1));
                if (side.Length < 1e-6)
                {
                    side = Vector3D.CrossProduct(dir, new Vector3D(0, 1, 0));
                }
                side.Normalize();
                side *= half;

                Point3D p0 = a + side;
                Point3D p1 = a - side;
                Point3D p2 = b - side;
                Point3D p3 = b + side;

                mesh.Positions.Add(p0);
                mesh.Positions.Add(p1);
                mesh.Positions.Add(p2);
                mesh.Positions.Add(p3);

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

            // 노드 위치/색 동기화
            foreach (KeyValuePair<NodeViewModel, TranslateTransform3D> kv in _nodeTransforms)
            {
                NodeViewModel node = kv.Key;
                TranslateTransform3D t = kv.Value;
                t.OffsetX = node.X;
                t.OffsetY = -node.Y;
                t.OffsetZ = node.Z;

                GeometryModel3D model;
                if (_nodeModels.TryGetValue(node, out model))
                {
                    var mat = model.Material as DiffuseMaterial;
                    if (mat != null)
                    {
                        var scb = mat.Brush as SolidColorBrush;
                        Color want = ToColor(node.Fill);
                        if (scb == null || scb.Color != want)
                        {
                            var nb = new SolidColorBrush(want);
                            model.Material = new DiffuseMaterial(nb);
                            model.BackMaterial = model.Material;
                        }
                    }
                }
            }

            // 엣지는 위치가 계속 바뀌므로 메시 좌표만 다시 채운다
            if (_edgeMesh != null)
            {
                BuildEdgeMesh(_edgeMesh);
            }
        }

        // ── 카메라 마우스 조작 ───────────────────────

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _orbiting = true;
            _lastMouse = e.GetPosition(Root);
            Root.CaptureMouse();
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _orbiting = false;
            ReleaseIfIdle();
        }

        private void Root_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 휠(가운데) 버튼 → 팬
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
            if (!_orbiting && !_panning)
            {
                return;
            }
            Point p = e.GetPosition(Root);
            double dx = p.X - _lastMouse.X;
            double dy = p.Y - _lastMouse.Y;
            _lastMouse = p;

            if (_orbiting)
            {
                _orbit.Orbit(dx, dy);
            }
            else if (_panning)
            {
                _orbit.Pan(dx, dy);
            }
        }

        private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _orbit.Zoom(e.Delta);
        }

        private void ReleaseIfIdle()
        {
            if (!_orbiting && !_panning)
            {
                Root.ReleaseMouseCapture();
            }
        }

        // ── 카메라 자동 맞춤 ─────────────────────────

        /// <summary>그래프 전체가 보이도록 카메라 타깃/거리를 맞춘다.</summary>
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
            double spanX = maxX - minX;
            double spanY = maxY - minY;
            double spanZ = maxZ - minZ;
            double span = Math.Max(spanX, Math.Max(spanY, spanZ));
            if (span < 100) span = 100;

            _orbit.SetView(center, span * 1.6);
        }

        // ── 색 변환 ──────────────────────────────────

        private static Brush ToBrush(Brush fill)
        {
            return new SolidColorBrush(ToColor(fill));
        }

        private static Color ToColor(Brush fill)
        {
            var scb = fill as SolidColorBrush;
            if (scb != null)
            {
                return scb.Color;
            }
            return Colors.Gray;
        }
    }
}
