using FolderGraph.Core;

namespace FolderGraph.ViewModels
{
    /// <summary>
    /// 부모-자식 연결선 하나의 화면 표현.
    /// 두 노드의 좌표 변화에 따라 선 끝점이 갱신된다.
    /// </summary>
    public class EdgeViewModel : ObservableObject
    {
        public EdgeViewModel(NodeViewModel parent, NodeViewModel child)
        {
            Parent = parent;
            Child = child;

            // 부모/자식의 좌표가 바뀌면 선 끝점도 갱신
            Parent.PropertyChanged += OnEndpointChanged;
            Child.PropertyChanged += OnEndpointChanged;
        }

        public NodeViewModel Parent { get; private set; }
        public NodeViewModel Child { get; private set; }

        private double _opacity = 1.0;

        /// <summary>엣지 불투명도. 선택 시 무관한 엣지를 흐리게 한다.</summary>
        public double Opacity
        {
            get { return _opacity; }
            set { SetProperty(ref _opacity, value); }
        }

        public double X1 { get { return Parent.X; } }
        public double Y1 { get { return Parent.Y; } }
        public double X2 { get { return Child.X; } }
        public double Y2 { get { return Child.Y; } }

        private void OnEndpointChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "X" || e.PropertyName == "Y")
            {
                OnPropertyChanged("X1");
                OnPropertyChanged("Y1");
                OnPropertyChanged("X2");
                OnPropertyChanged("Y2");
            }
        }
    }
}
