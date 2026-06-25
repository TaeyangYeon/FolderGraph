using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderGraph.Models;
using FolderGraph.ViewModels;

namespace FolderGraph.Views
{
    /// <summary>
    /// 그래프 캔버스. Phase 6까지:
    /// - 클릭/드래그 구분, 선택, Pan/Zoom (Phase 2~3)
    /// - 노드 더블클릭 → 파일 열기
    /// - 노드 드래그를 폴더 노드 위에 드롭 → 파일 이동(드롭 대상 파란 점선 강조)
    /// </summary>
    public partial class GraphCanvasView : UserControl
    {
        private const double MinZoom = 0.2;
        private const double MaxZoom = 4.0;
        private const double ZoomStep = 1.1;
        private const double DragThreshold = 4.0;

        // 노드 누름 상태
        private bool _mouseDownOnNode;
        private NodeViewModel _pressedNode;
        private bool _nodeDragStarted;
        private bool _dragFreezesOthers;  // 선택된 노드 드래그 → 나머지 고정(시뮬레이션 정지)
        private Vector _grabOffset;
        private HashSet<NodeViewModel> _draggedSubtree;  // 드래그 노드 자신+자손(드롭 제외용)
        private NodeViewModel _dropTarget;               // 현재 지목된 드롭 대상 폴더

        // 빈 공간(패닝) 상태
        private bool _mouseDownOnEmpty;

        private Point _pressScreenPoint;
        private Point _lastScreenPoint;

        // 미니맵 상태
        private bool _draggingMinimap;
        private MainViewModel _hookedVm;  // ExportImageRequested 구독 대상

        public GraphCanvasView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 미니맵 뷰포트 표시를 매 프레임 갱신(미니맵이 보일 때만 실제 계산)
            CompositionTarget.Rendering += OnRendering;

            // ViewModel의 내보내기 요청 구독
            HookViewModel();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            if (_hookedVm != null)
            {
                _hookedVm.ExportImageRequested -= OnExportImageRequested;
                _hookedVm = null;
            }
        }

        private void HookViewModel()
        {
            if (_hookedVm != ViewModel)
            {
                if (_hookedVm != null)
                {
                    _hookedVm.ExportImageRequested -= OnExportImageRequested;
                }
                _hookedVm = ViewModel;
                if (_hookedVm != null)
                {
                    _hookedVm.ExportImageRequested += OnExportImageRequested;
                }
            }
        }

        private MainViewModel ViewModel
        {
            get { return DataContext as MainViewModel; }
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 더블클릭 → 파일 열기(드래그/선택은 시작하지 않음)
            if (e.ClickCount == 2)
            {
                NodeViewModel dbl = FindNode(e.OriginalSource as DependencyObject);
                if (dbl != null && ViewModel != null)
                {
                    ViewModel.OpenFile(dbl);
                }
                return;
            }

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
                    if ((current - _pressScreenPoint).Length > DragThreshold)
                    {
                        _nodeDragStarted = true;
                        _pressedNode.IsPinned = true;

                        // 드래그 노드 자신+자손 집합(드롭 대상에서 제외)
                        _draggedSubtree = new HashSet<NodeViewModel>();
                        CollectSubtree(_pressedNode, _draggedSubtree);

                        Point cp0 = e.GetPosition(RootCanvas);
                        _grabOffset = new Vector(_pressedNode.X - cp0.X, _pressedNode.Y - cp0.Y);

                        // 선택해서 하이라이트된 노드를 드래그하는 경우에만 시뮬레이션을 멈춰
                        // 나머지 노드를 고정한다(대상 폴더가 도망가지 않게). 그 외 일반
                        // 드래그는 기존대로 시뮬레이션이 돌아 주변이 반응한다.
                        _dragFreezesOthers = _pressedNode.IsSelected;
                        if (ViewModel != null)
                        {
                            if (_dragFreezesOthers)
                            {
                                ViewModel.PauseSimulation();
                            }
                            else
                            {
                                ViewModel.ReheatSimulation();
                            }
                        }
                    }
                }

                if (_nodeDragStarted)
                {
                    Point cp = e.GetPosition(RootCanvas);
                    _pressedNode.X = cp.X + _grabOffset.X;
                    _pressedNode.Y = cp.Y + _grabOffset.Y;

                    // 커서 아래의 폴더 노드를 드롭 대상으로 지목(파란 점선)
                    UpdateDropTarget(cp);

                    // 선택 노드 드래그는 정지 유지(나머지 고정).
                    // 일반 드래그는 온도를 유지해 자식(폴더 포함)이 따라오게 한다.
                    if (!_dragFreezesOthers && ViewModel != null)
                    {
                        ViewModel.ReheatSimulation();
                    }
                }
            }
            else if (_mouseDownOnEmpty)
            {
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
                    if (_dropTarget != null)
                    {
                        // 폴더 위에 드롭 → 파일 이동 요청
                        NodeViewModel target = _dropTarget;
                        ClearDropTarget();
                        NodeViewModel dragged = _pressedNode;

                        // 정리 후 이동(이동은 재스캔하여 그래프를 다시 만든다)
                        ResetDragState();
                        Viewport.ReleaseMouseCapture();

                        if (ViewModel != null)
                        {
                            ViewModel.RequestMoveAsync(dragged, target);
                        }
                        return;
                    }

                    // 빈 곳에 드롭 → 단순 위치 이동(고정 해제)
                    _pressedNode.IsPinned = false;
                    // 선택 노드 드래그는 드롭 후에도 정지를 유지(나머지 가만히).
                    // 일반 드래그만 시뮬레이션을 돌려 자식이 따라오게 한다.
                    if (!_dragFreezesOthers && ViewModel != null)
                    {
                        ViewModel.ReheatSimulation();
                    }
                }
                else
                {
                    // 클릭 → 선택
                    if (ViewModel != null)
                    {
                        ViewModel.SelectNode(_pressedNode);
                    }
                }
            }
            else if (_mouseDownOnEmpty)
            {
                if (moved < DragThreshold && ViewModel != null)
                {
                    ViewModel.ClearSelection();
                }
            }

            ClearDropTarget();
            ResetDragState();
            Viewport.ReleaseMouseCapture();
        }

        // ── 우클릭: 색상 팔레트 ──
        private void Viewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            NodeViewModel node = FindNode(e.OriginalSource as DependencyObject);
            if (node == null || ViewModel == null)
            {
                return;
            }

            Point p = e.GetPosition(Viewport);
            PalettePopup.HorizontalOffset = p.X;
            PalettePopup.VerticalOffset = p.Y;

            ViewModel.OpenPaletteFor(node);
            e.Handled = true;
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
        /// 커서(캔버스 좌표) 아래의 폴더 노드를 찾아 드롭 대상으로 지목한다.
        /// 드래그 노드 자신/자손은 제외. 기하적으로(중심과의 거리) 판정한다.
        /// </summary>
        private void UpdateDropTarget(Point canvasPoint)
        {
            if (ViewModel == null)
            {
                return;
            }

            NodeViewModel found = null;
            foreach (NodeViewModel n in ViewModel.Nodes)
            {
                if (n.Type != NodeType.Folder)
                {
                    continue; // 폴더만 드롭 대상
                }
                if (_draggedSubtree != null && _draggedSubtree.Contains(n))
                {
                    continue; // 자기 자신/자손 제외
                }

                double dx = canvasPoint.X - n.X;
                double dy = canvasPoint.Y - n.Y;
                double hit = n.Radius + 4.0; // 약간 여유
                if (dx * dx + dy * dy <= hit * hit)
                {
                    found = n;
                    break;
                }
            }

            if (found != _dropTarget)
            {
                if (_dropTarget != null)
                {
                    _dropTarget.IsDropTarget = false;
                }
                _dropTarget = found;
                if (_dropTarget != null)
                {
                    _dropTarget.IsDropTarget = true;
                }
            }
        }

        private void ClearDropTarget()
        {
            if (_dropTarget != null)
            {
                _dropTarget.IsDropTarget = false;
                _dropTarget = null;
            }
        }

        private void ResetDragState()
        {
            _mouseDownOnNode = false;
            _mouseDownOnEmpty = false;
            _nodeDragStarted = false;
            _dragFreezesOthers = false;
            _pressedNode = null;
            _draggedSubtree = null;
        }

        private void CollectSubtree(NodeViewModel root, HashSet<NodeViewModel> set)
        {
            if (!set.Add(root))
            {
                return;
            }
            foreach (NodeViewModel child in root.Children)
            {
                CollectSubtree(child, set);
            }
        }

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

        // ── 미니맵 ───────────────────────────────────

        /// <summary>매 프레임: 미니맵이 보이면 현재 보이는 영역 사각형을 갱신한다.</summary>
        private void OnRendering(object sender, EventArgs e)
        {
            if (ViewModel == null || !ViewModel.ShowMinimap)
            {
                return;
            }
            UpdateMinimapViewport();
        }

        /// <summary>
        /// 콘텐츠 경계 → 미니맵 박스 매핑(Uniform)을 구해, 현재 보이는 캔버스 영역을
        /// 미니맵 좌표의 사각형으로 그린다.
        /// </summary>
        private void UpdateMinimapViewport()
        {
            Rect content = VisualTreeHelper.GetDescendantBounds(GraphLayer);
            double mmW = MinimapContent.ActualWidth;
            double mmH = MinimapContent.ActualHeight;

            if (content.IsEmpty || content.Width <= 0 || content.Height <= 0 ||
                mmW <= 0 || mmH <= 0)
            {
                MinimapViewport.Visibility = Visibility.Collapsed;
                return;
            }

            double s = Math.Min(mmW / content.Width, mmH / content.Height);
            double ox = (mmW - content.Width * s) / 2.0;
            double oy = (mmH - content.Height * s) / 2.0;

            // 현재 보이는 캔버스 영역
            double scale = ZoomTransform.ScaleX;
            double visLeft = (0 - PanTransform.X) / scale;
            double visTop = (0 - PanTransform.Y) / scale;
            double visW = Viewport.ActualWidth / scale;
            double visH = Viewport.ActualHeight / scale;

            double rx = ox + (visLeft - content.X) * s;
            double ry = oy + (visTop - content.Y) * s;
            double rw = visW * s;
            double rh = visH * s;

            MinimapViewport.Visibility = Visibility.Visible;
            Canvas.SetLeft(MinimapViewport, rx);
            Canvas.SetTop(MinimapViewport, ry);
            MinimapViewport.Width = Math.Max(2, rw);
            MinimapViewport.Height = Math.Max(2, rh);
        }

        private void Minimap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggingMinimap = true;
            MinimapContent.CaptureMouse();
            CenterViewOnMinimapPoint(e.GetPosition(MinimapContent));
            e.Handled = true;
        }

        private void Minimap_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingMinimap)
            {
                CenterViewOnMinimapPoint(e.GetPosition(MinimapContent));
            }
        }

        private void Minimap_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggingMinimap = false;
            MinimapContent.ReleaseMouseCapture();
            e.Handled = true;
        }

        /// <summary>미니맵의 한 점을 화면 중앙으로 가져오도록 Pan을 조정한다.</summary>
        private void CenterViewOnMinimapPoint(Point mmPoint)
        {
            Rect content = VisualTreeHelper.GetDescendantBounds(GraphLayer);
            double mmW = MinimapContent.ActualWidth;
            double mmH = MinimapContent.ActualHeight;
            if (content.IsEmpty || content.Width <= 0 || mmW <= 0)
            {
                return;
            }

            double s = Math.Min(mmW / content.Width, mmH / content.Height);
            double ox = (mmW - content.Width * s) / 2.0;
            double oy = (mmH - content.Height * s) / 2.0;

            // 미니맵 좌표 → 캔버스 좌표
            double canvasX = content.X + (mmPoint.X - ox) / s;
            double canvasY = content.Y + (mmPoint.Y - oy) / s;

            // 그 점을 화면 중앙으로
            double scale = ZoomTransform.ScaleX;
            PanTransform.X = Viewport.ActualWidth / 2.0 - scale * canvasX;
            PanTransform.Y = Viewport.ActualHeight / 2.0 - scale * canvasY;
        }

        // ── PNG 내보내기 ─────────────────────────────

        private void OnExportImageRequested(object sender, EventArgs e)
        {
            ExportToPng();
        }

        private void ExportToPng()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG 이미지 (*.png)|*.png",
                FileName = "FolderGraph.png",
                DefaultExt = ".png"
            };
            bool? ok = dlg.ShowDialog();
            if (ok != true)
            {
                return;
            }

            // 캡처 동안 미니맵은 잠시 숨겨 결과 이미지에 들어가지 않게 한다
            Visibility savedMinimap = MinimapPanel.Visibility;
            MinimapPanel.Visibility = Visibility.Collapsed;
            try
            {
                Viewport.UpdateLayout();

                int w = (int)Math.Ceiling(Viewport.ActualWidth);
                int h = (int)Math.Ceiling(Viewport.ActualHeight);
                if (w <= 0 || h <= 0)
                {
                    return;
                }

                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(Viewport);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                using (FileStream fs = File.Create(dlg.FileName))
                {
                    encoder.Save(fs);
                }

                if (ViewModel != null)
                {
                    ViewModel.StatusText = "이미지를 저장했습니다: " + dlg.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("이미지 저장에 실패했습니다.\n" + ex.Message,
                                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                MinimapPanel.Visibility = savedMinimap;
            }
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
