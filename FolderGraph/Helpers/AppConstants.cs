namespace FolderGraph.Helpers
{
    /// <summary>
    /// 앱 전역에서 사용하는 조절용 상수.
    /// 성능에 따라 여기 값만 바꿔 튜닝한다 (PLAN.md 성능 방침).
    /// </summary>
    public static class AppConstants
    {
        /// <summary>
        /// 렌더링할 최대 노드 수. 스캔이 이 개수를 넘으면 중단한다.
        /// 성능을 보며 조정할 것.
        /// </summary>
        public const int MaxNodes = 2000;

        /// <summary>색상 톤 그라데이션이 적용되는 최대 깊이 단계(0~4 → 5단계).</summary>
        public const int MaxColorDepth = 4;

        /// <summary>노드 기본 반지름(픽셀).</summary>
        public const double DefaultNodeRadius = 14.0;

        /// <summary>탐색 깊이 슬라이더의 최소/최대.</summary>
        public const int MinDepth = 1;
        public const int MaxDepth = 10;
    }
}
