using System.Windows.Media;

namespace FolderGraph.Helpers.Abstractions
{
    /// <summary>
    /// 기준 색과 (선택 노드 기준) 상대 깊이로부터 노드 색을 계산하는 추상화.
    /// 지금은 "흰색 혼합으로 명도 높이기" 방식이지만, 색 계산은 가장 바뀌기 쉬운
    /// 부분이라 인터페이스로 분리해 교체 가능하게 둔다(OCP/DIP).
    /// </summary>
    public interface INodeColorCalculator
    {
        /// <summary>
        /// 기준 색에 상대 깊이만큼 흰색을 섞어 옅게 만든 색을 반환한다.
        /// depth 0 = 원색, 깊어질수록 밝아지며, 일정 깊이(MaxColorDepth) 이후로는
        /// 더 밝아지지 않고 동일 톤을 유지한다.
        /// </summary>
        /// <param name="baseColor">우클릭으로 고른 기준 색(depth 0의 색).</param>
        /// <param name="relativeDepth">색을 지정한 노드 기준 상대 깊이(0,1,2,...).</param>
        Color GetShade(Color baseColor, int relativeDepth);
    }
}
