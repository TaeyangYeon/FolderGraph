namespace FolderGraph.Graph.Abstractions
{
    /// <summary>
    /// 힘-기반 시뮬레이션이 위치를 갱신하는 대상의 추상화.
    /// NodeViewModel이 이를 구현한다. 시뮬레이션은 ViewModel을 모르고
    /// 이 인터페이스(좌표 + 고정 여부)만 의존한다 (DIP).
    /// </summary>
    public interface IPhysicsBody
    {
        /// <summary>중심 X 좌표(캔버스 로컬 좌표).</summary>
        double X { get; set; }

        /// <summary>중심 Y 좌표(캔버스 로컬 좌표).</summary>
        double Y { get; set; }

        /// <summary>중심 Z 좌표(3D 깊이).</summary>
        double Z { get; set; }

        /// <summary>
        /// 고정 여부. 사용자가 드래그로 잡았거나 위치를 고정한 노드는 true가 되어
        /// 시뮬레이션이 위치를 바꾸지 않는다(다른 노드에 힘은 계속 행사).
        /// </summary>
        bool IsPinned { get; set; }
    }
}
