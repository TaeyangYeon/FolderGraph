using System.Collections.Generic;
using System.Windows.Media;
using FolderGraph.Core;
using FolderGraph.Graph.Abstractions;
using FolderGraph.Helpers;
using FolderGraph.Models;

namespace FolderGraph.ViewModels
{
    /// <summary>
    /// 화면에 그려지는 노드 하나의 표현(좌표·색·선택 상태 등).
    /// 데이터 모델(FileNodeModel)을 감싸고, 화면 관련 상태를 더한다.
    /// IPhysicsBody를 구현해 힘-기반 시뮬레이션의 대상이 된다.
    /// </summary>
    public class NodeViewModel : ObservableObject, IPhysicsBody
    {
        // 선택 시 강조 테두리(주황) / 검색 매칭 테두리(하늘색) / 기본 테두리
        private static readonly Brush DefaultStroke = MakeFrozen(Color.FromRgb(0x1E, 0x29, 0x3B));
        private static readonly Brush SelectedStroke = MakeFrozen(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private static readonly Brush SearchStroke = MakeFrozen(Color.FromRgb(0x38, 0xBD, 0xF8));

        private double _x;
        private double _y;
        private double _radius;
        private bool _isSelected;
        private bool _isHighlighted;
        private bool _isPinned;
        private bool _isDropTarget;
        private double _opacity;
        private Brush _fill;
        private Brush _strokeBrush;
        private double _strokeThickness;

        public NodeViewModel(FileNodeModel model)
        {
            Model = model;
            _radius = AppConstants.DefaultNodeRadius;
            _fill = Brushes.Gray; // 색 미지정 기본값 = 회색
            _opacity = 1.0;
            _strokeBrush = DefaultStroke;
            _strokeThickness = 1.0;
            Children = new List<NodeViewModel>();
        }

        /// <summary>VM 레벨 자식 노드 목록(선택 시 자손 하이라이트 순회에 사용).</summary>
        public List<NodeViewModel> Children { get; private set; }

        private static Brush MakeFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }


        /// <summary>감싸고 있는 데이터 모델.</summary>
        public FileNodeModel Model { get; private set; }

        public string Name { get { return Model.Name; } }
        public string FullPath { get { return Model.FullPath; } }
        public NodeType Type { get { return Model.Type; } }
        public int Depth { get { return Model.Depth; } }

        /// <summary>호버 툴팁에 쓸 표시 텍스트.</summary>
        public string DisplayLabel
        {
            get { return Model.Name; }
        }

        public double X
        {
            get { return _x; }
            set
            {
                if (SetProperty(ref _x, value))
                {
                    OnPropertyChanged("Left");
                }
            }
        }

        public double Y
        {
            get { return _y; }
            set
            {
                if (SetProperty(ref _y, value))
                {
                    OnPropertyChanged("Top");
                }
            }
        }

        public double Radius
        {
            get { return _radius; }
            set { SetProperty(ref _radius, value); }
        }

        /// <summary>Canvas 배치용 좌상단 좌표(중심 - 반지름).</summary>
        public double Left
        {
            get { return _x - _radius; }
        }

        public double Top
        {
            get { return _y - _radius; }
        }

        public double Diameter
        {
            get { return _radius * 2.0; }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        public bool IsHighlighted
        {
            get { return _isHighlighted; }
            set { SetProperty(ref _isHighlighted, value); }
        }

        /// <summary>
        /// 시뮬레이션 고정 여부. 드래그 중이거나 사용자가 배치한 노드는 true.
        /// (IPhysicsBody 구현)
        /// </summary>
        public bool IsPinned
        {
            get { return _isPinned; }
            set { SetProperty(ref _isPinned, value); }
        }

        /// <summary>드래그 중 이 노드가 드롭 대상 폴더로 지목되었는지(파란 점선 강조).</summary>
        public bool IsDropTarget
        {
            get { return _isDropTarget; }
            set { SetProperty(ref _isDropTarget, value); }
        }

        // ── 색 상태(이동 시 색 재지정/스냅샷 복원에 사용; 바인딩 대상 아님) ──

        /// <summary>사용자 지정 색이 적용되었는지 여부(미적용=회색).</summary>
        public bool IsColored { get; set; }

        /// <summary>적용된 색 그라데이션의 기준 색(depth 0의 색).</summary>
        public Color ColorBase { get; set; }

        /// <summary>색 그라데이션 기준에서 이 노드의 상대 깊이(이동 시 자손 깊이 계산에 사용).</summary>
        public int ColorDepth { get; set; }

        public Brush Fill
        {
            get { return _fill; }
            set { SetProperty(ref _fill, value); }
        }

        /// <summary>노드 불투명도. 선택 시 비강조 노드를 흐리게 하는 데 사용.</summary>
        public double Opacity
        {
            get { return _opacity; }
            set { SetProperty(ref _opacity, value); }
        }

        /// <summary>테두리 색. 선택된 노드는 주황으로 강조.</summary>
        public Brush StrokeBrush
        {
            get { return _strokeBrush; }
            set { SetProperty(ref _strokeBrush, value); }
        }

        /// <summary>테두리 두께. 선택된 노드는 두껍게.</summary>
        public double StrokeThicknessValue
        {
            get { return _strokeThickness; }
            set { SetProperty(ref _strokeThickness, value); }
        }

        /// <summary>선택 강조 적용(자기 자신이 선택된 노드일 때).</summary>
        public void ApplySelectedStyle()
        {
            StrokeBrush = SelectedStroke;
            StrokeThicknessValue = 2.5;
        }

        /// <summary>검색 매칭 강조 적용(하늘색 테두리).</summary>
        public void ApplySearchStyle()
        {
            StrokeBrush = SearchStroke;
            StrokeThicknessValue = 2.5;
        }

        /// <summary>강조 스타일 초기화.</summary>
        public void ResetStyle()
        {
            StrokeBrush = DefaultStroke;
            StrokeThicknessValue = 1.0;
            Opacity = 1.0;
        }

        /// <summary>X/Y가 바뀌면 Left/Top/Diameter도 갱신 알림.</summary>
        public void NotifyPositionChanged()
        {
            OnPropertyChanged("Left");
            OnPropertyChanged("Top");
        }
    }
}
