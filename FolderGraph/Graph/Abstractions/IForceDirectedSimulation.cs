using System.Collections.Generic;

namespace FolderGraph.Graph.Abstractions
{
    /// <summary>
    /// 한 틱씩(Step) 진행되는 힘-기반 레이아웃 시뮬레이션의 추상화.
    /// DispatcherTimer가 매 프레임 Step()을 호출해 애니메이션을 만든다.
    /// 다른 레이아웃 알고리즘으로 교체 가능하도록 인터페이스로 분리(OCP).
    /// </summary>
    public interface IForceDirectedSimulation
    {
        /// <summary>
        /// 시뮬레이션 대상을 설정하고 온도를 초기화한다.
        /// </summary>
        /// <param name="bodies">위치를 갱신할 입자들.</param>
        /// <param name="links">스프링(인력)으로 작용할 연결들.</param>
        /// <param name="centerX">중심 인력 기준 X.</param>
        /// <param name="centerY">중심 인력 기준 Y.</param>
        void Initialize(IList<IPhysicsBody> bodies, IList<GraphLink> links,
                        double centerX, double centerY);

        /// <summary>
        /// 한 틱 진행한다. 아직 움직이는 중이면 true,
        /// 충분히 안정(settled)되어 멈춰도 되면 false를 반환.
        /// </summary>
        bool Step();

        /// <summary>드래그 등 상호작용 시 온도를 다시 올려 시뮬레이션을 깨운다.</summary>
        void Reheat();

        /// <summary>현재 안정 상태인지 여부.</summary>
        bool IsSettled { get; }
    }
}
