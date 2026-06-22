using System.Collections.Generic;
using FolderGraph.ViewModels;

namespace FolderGraph.Graph.Abstractions
{
    /// <summary>
    /// 노드들의 화면 좌표(X, Y)를 계산하는 레이아웃 엔진의 추상화.
    /// Phase 1은 단순 격자, Phase 2는 Force-Directed로 교체 예정 (OCP).
    /// </summary>
    public interface IGraphLayoutEngine
    {
        /// <summary>
        /// 주어진 노드들의 X/Y 좌표를 계산해 설정한다.
        /// </summary>
        /// <param name="nodes">배치할 노드 목록.</param>
        /// <param name="areaWidth">배치 영역 너비.</param>
        /// <param name="areaHeight">배치 영역 높이.</param>
        void Arrange(IList<NodeViewModel> nodes, double areaWidth, double areaHeight);
    }
}
