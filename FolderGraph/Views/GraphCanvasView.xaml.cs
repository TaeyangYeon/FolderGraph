using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FolderGraph.ViewModels;

namespace FolderGraph.Views
{
    /// <summary>
    /// 그래프 캔버스. Phase 3:
    /// - 클릭과 드래그를 임계값으로 구분
    /// - 노드 클릭 → 선택(+ 모든 하위 자손 하이라이트)
    /// - 노드 드래그 → 위치 이동(놓으면 고정 해제)
    /// - 빈 공간 클릭 → 선택 해제 / 빈 공간 드래그 → Pan
    /// - 휠 → 커서 기준 Zoom
    /// </summary>
    public partial class GraphCanvasView : UserControl
    {
        private const double MinZoom = 0.2;
        private const double MaxZoom = 4.0;
        private const double ZoomStep = 1.1;
        private const double DragThreshold = 4.0; // 이 거리 이상 움직이면 드래그로 간주

        // 노드 누름 상태
        private bool _mouseDownOnNode;
        private NodeViewModel _pressedNode;
        private bool _nodeDragStarted;
        private Vector _grabOffset;

        // 빈 공간(패닝) 상태
        private bool _mouseDownOnEmpty;

        private Point _pressScreenPoint;  // 누른 지점(Viewport 기준) — 클릭/드래그 판정
        private Point _lastScreenPoint;   // 패닝 델타용

        public GraphCanvasView()
        {
            InitializeComponent();
        }

        private MainViewModel ViewModel
        {
            get { return DataContext as MainViewModel; }
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressScreenPoint = e.GetPosition(Viewport);
            _lastScreenPoint = _pressScreenPoint;

            NodeViewModel node = FindNode(e.OriginalSource as DependencyObject);

            if (node != null)
            {
                _mouseDownOnNode = true;
                _mouseDownOnEmpty = false;
                _pressedNode = node;
                _nodeDragStarted = false;
            }
            else
            {
                _mouseDownOnNode = false;
                _mouseDownOnEmpty = true;
                _pressedNode = null;
            }

            Viewport.CaptureMouse();
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            Point current = e.GetPosition(Viewport);

            if (_mouseDownOnNode && _pressedNode != null)
            {
                if (!_nodeDragStarted)
                {
                    // 임계값을 넘으면 그때부터 드래그 시작
                    if ((current - _pressScreenPoint).Length > DragThreshold)
                    {
                        _nodeDragStarted = true;
                        _pressedNode.IsPinned = true;

                        Point cp = e.GetPosition(RootCanvas);
                        _grabOffset = new Vector(_pressedNode.X - cp.X, _pressedNode.Y - cp.Y);

                        if (ViewModel != null)
                        {
                            ViewModel.ReheatSimulation();
                        }
                    }
                }

                if (_nodeDragStarted)
                {
                    Point cp = e.GetPosition(RootCanvas);
                    _pressedNode.X = cp.X + _grabOffset.X;
                    _pressedNode.Y = cp.Y + _grabOffset.Y;

                    // 이동 중 온도 유지 → 자식(폴더 포함)이 따라온다
                    if (ViewModel != null)
                    {
                        ViewModel.ReheatSimulation();
                    }
                }
            }
            else if (_mouseDownOnEmpty)
            {
                // 빈 공간 드래그 → 패닝
                Vector delta = current - _lastScreenPoint;
                PanTransform.X += delta.X;
                PanTransform.Y += delta.Y;
                _lastScreenPoint = current;
            }
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Point current = e.GetPosition(Viewport);
            double moved = (current - _pressScreenPoint).Length;

            if (_mouseDownOnNode && _pressedNode != null)
            {
                if (_nodeDragStarted)
                {
                    // 드래그 종료 → 고정 해제(이후 부모 이동 시 따라오도록)
                    _pressedNode.IsPinned = false;
                    if (ViewModel != null)
                    {
                        ViewModel.ReheatSimulation();
                    }
                }
                else
                {
                    // 거의 안 움직였으면 클릭 → 선택
                    if (ViewModel != null)
                    {
                        ViewModel.SelectNode(_pressedNode);
                    }
                }
            }
            else if (_mouseDownOnEmpty)
            {
                // 빈 공간을 거의 안 움직이고 뗐으면 클릭 → 선택 해제
                if (moved < DragThreshold && ViewModel != null)
                {
                    ViewModel.ClearSelection();
                }
            }

            _mouseDownOnNode = false;
            _mouseDownOnEmpty = false;
            _nodeDragStarted = false;
            _pressedNode = null;
            Viewport.ReleaseMouseCapture();
        }

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

            double canvasX = (screen.X - PanTransform.X) / oldScale;
            double canvasY = (screen.Y - PanTransform.Y) / oldScale;

            ZoomTransform.ScaleX = newScale;
            ZoomTransform.ScaleY = newScale;

            PanTransform.X = screen.X - canvasX * newScale;
            PanTransform.Y = screen.Y - canvasY * newScale;
        }

        /// <summary>
        /// 클릭된 비주얼에서 위로 올라가며 DataContext가 NodeViewModel인 요소를 찾는다.
        /// 빈 공간이면 null.
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
