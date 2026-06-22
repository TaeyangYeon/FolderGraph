using System.Windows.Controls;

namespace FolderGraph.Views
{
    /// <summary>
    /// 그래프(노드+엣지)를 그리는 캔버스 영역.
    /// Phase 1: 정적 렌더링 + 호버 툴팁.
    /// Phase 2 이후: Pan/Zoom, 노드 드래그 등을 코드비하인드/동작에 추가.
    /// </summary>
    public partial class GraphCanvasView : UserControl
    {
        public GraphCanvasView()
        {
            InitializeComponent();
        }
    }
}
