using System.Windows.Media;
using FolderGraph.Core;
using FolderGraph.Helpers;
using FolderGraph.Models;

namespace FolderGraph.ViewModels
{
    /// <summary>
    /// 화면에 그려지는 노드 하나의 표현(좌표·색·선택 상태 등).
    /// 데이터 모델(FileNodeModel)을 감싸고, 화면 관련 상태를 더한다.
    /// Phase 1에서는 좌표/선택/색의 골격만 둔다.
    /// </summary>
    public class NodeViewModel : ObservableObject
    {
        private double _x;
        private double _y;
        private double _radius;
        private bool _isSelected;
        private bool _isHighlighted;
        private Brush _fill;

        public NodeViewModel(FileNodeModel model)
        {
            Model = model;
            _radius = AppConstants.DefaultNodeRadius;
            _fill = Brushes.Gray; // 색 미지정 기본값 = 회색
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

        public Brush Fill
        {
            get { return _fill; }
            set { SetProperty(ref _fill, value); }
        }

        /// <summary>X/Y가 바뀌면 Left/Top/Diameter도 갱신 알림.</summary>
        public void NotifyPositionChanged()
        {
            OnPropertyChanged("Left");
            OnPropertyChanged("Top");
        }
    }
}
