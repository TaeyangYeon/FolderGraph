namespace FolderGraph.Graph.Abstractions
{
    /// <summary>
    /// 두 물리 입자 사이의 연결(엣지). 스프링(인력) 계산에 사용된다.
    /// </summary>
    public class GraphLink
    {
        public IPhysicsBody A { get; private set; }
        public IPhysicsBody B { get; private set; }

        public GraphLink(IPhysicsBody a, IPhysicsBody b)
        {
            A = a;
            B = b;
        }
    }
}
