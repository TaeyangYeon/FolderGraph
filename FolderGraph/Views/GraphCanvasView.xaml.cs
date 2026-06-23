using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FolderGraph.ViewModels;

namespace FolderGraph.Views
{
    /// <summary>
    /// 그래프 캔버스. Phase 2:
    /// - 빈 공간 드래그 → Pan(이동)
    /// - 마우스 휠 → Zoom(커서 기준 확대/축소)
    /// - 노드 드래그 → 해당 노드 위치 이동(드래그 동안/이후 고정)
    /// 저수준 마우스 처리라 코드비하인드에 둔다(MVVM에서 허용되는 영역).
    /// </summary>
    public partial class GraphCanvasView : UserControl
    {
        private const double MinZoom = 0.2;
        private const double MaxZoom = 4.0;
        private const double ZoomStep = 1.1;

        private bool _isPanning;
        private bool _isDraggingNode;
        private NodeViewModel _draggedNode;
        private Point _lastScreenPoint;   // 패닝 델타 계산용(Viewport 기준)
        private Vector _grabOffset;       // 노드 중심과 잡은 지점의 차이(RootCanvas 기준)

        public GraphCanvasView()
        {
            InitializeComponent();
        }

        private MainViewModel ViewModel
        {
            get { return DataContext as MainViewModel; }
        }

        // ── 마우스 다운: 노드면 노드 드래그, 빈 공간이면 패닝 ──
        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            NodeViewModel node = FindNode(e.OriginalSource as DependencyObject);

            if (node != null)
            {
                // 노드 드래그 시작 — 시뮬레이션이 건드리지 않도록 고정
                _isDraggingNode = true;
                _draggedNode = node;
                node.IsPinned = true;

                Point canvasPoint = e.GetPosition(RootCanvas);
                _grabOffset = new Vector(node.X - canvasPoint.X, node.Y - canvasPoint.Y);

                if (ViewModel != null)
                {
                    ViewModel.ReheatSimulation(); // 주변 노드가 반응하도록
                }
            }
            else
            {
                // 빈 공간 → 패닝 시작
                _isPanning = true;
                _lastScreenPoint = e.GetPosition(Viewport);
            }

            Viewport.CaptureMouse();
        }

        // ── 마우스 이동: 드래그 중인 동작 수행 ──
        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingNode && _draggedNode != null)
            {
                Point canvasPoint = e.GetPosition(RootCanvas);
                _draggedNode.X = canvasPoint.X + _grabOffset.X;
                _draggedNode.Y = canvasPoint.Y + _grabOffset.Y;

                // 이동 중에도 시뮬레이션 온도를 유지해야 자식 노드(특히 또 다른
                // 자식을 가진 폴더 노드)가 끌려온다. 시작 때 한 번만 깨우면
                // 온도가 식어 폴더 노드가 이동량 제한에 막혀 안 따라온다.
                if (ViewModel != null)
                {
                    ViewModel.ReheatSimulation();
                }
            }
            else if (_isPanning)
            {
                Point current = e.GetPosition(Viewport);
                Vector delta = current - _lastScreenPoint;
                PanTransform.X += delta.X;
                PanTransform.Y += delta.Y;
                _lastScreenPoint = current;
            }
        }

        // ── 마우스 업: 드래그 종료 ──
        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingNode && _draggedNode != null)
            {
                _draggedNode.IsPinned = false;
                // 드롭한 자리에 고정 유지(사용자가 배치한 위치 보존).
                // 주변 노드가 한 번 더 정리되도록 reheat.
                if (ViewModel != null)
                {
                    ViewModel.ReheatSimulation();
                }
            }


            _isDraggingNode = false;
            _draggedNode = null;
            _isPanning = false;

            Viewport.ReleaseMouseCapture();
        }

        // ── 마우스 휠: 커서 기준 줌 ──
        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point screen = e.GetPosition(Viewport);

            double oldScale = ZoomTransform.ScaleX;
            double factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
            double newScale = Clamp(oldScale * factor, MinZoom, MaxZoom);
            if (newScale == oldScale)
            {
                return;
            }

            // 커서 아래의 캔버스 점이 그대로 유지되도록 Pan 보정
            double canvasX = (screen.X - PanTransform.X) / oldScale;
            double canvasY = (screen.Y - PanTransform.Y) / oldScale;

            ZoomTransform.ScaleX = newScale;
            ZoomTransform.ScaleY = newScale;

            PanTransform.X = screen.X - canvasX * newScale;
            PanTransform.Y = screen.Y - canvasY * newScale;
        }

        /// <summary>
        /// 클릭된 비주얼에서 위로 올라가며 DataContext가 NodeViewModel인 요소를 찾는다.
        /// 빈 공간 클릭이면 null.
        /// </summary>
        private NodeViewModel FindNode(DependencyObject source)
        {
            while (source != null)
            {
                FrameworkElement fe = source as FrameworkElement;
                if (fe != null && fe.DataContext is NodeViewModel node)
                {
                    return node;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}