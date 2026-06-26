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
        public const int MaxNodes = 5000;

        /// <summary>색상 톤 그라데이션이 적용되는 최대 깊이 단계(0~4 → 5단계).</summary>
        public const int MaxColorDepth = 4;

        /// <summary>노드 기본 반지름(픽셀). (깊이별 크기 비활성 시 사용)</summary>
        public const double DefaultNodeRadius = 14.0;

        /// <summary>깊이별 크기 단계 수(0~4 → 5단계). 이 깊이 이상은 최소 크기로 통일.</summary>
        public const int MaxSizeDepth = 4;

        /// <summary>최상위(depth 0) 노드 반지름.</summary>
        public const double MaxNodeRadius = 30.0;

        /// <summary>가장 깊은(depth 4 이상) 노드 반지름.</summary>
        public const double MinNodeRadius = 9.0;

        /// <summary>
        /// 깊이에 따른 노드 반지름을 5단계로 계산한다.
        /// depth 0(최상위)이 가장 크고, 깊어질수록 선형으로 작아지며,
        /// depth 4 이상은 모두 최소 크기로 통일한다.
        /// </summary>
        public static double RadiusForDepth(int depth)
        {
            if (depth < 0) depth = 0;
            if (depth >= MaxSizeDepth) return MinNodeRadius;

            // depth 0..MaxSizeDepth 구간을 Max→Min으로 선형 보간
            double t = (double)depth / MaxSizeDepth;     // 0 .. 1
            return MaxNodeRadius + (MinNodeRadius - MaxNodeRadius) * t;
        }

        /// <summary>탐색 깊이 슬라이더의 최소/최대.</summary>
        public const int MinDepth = 1;
        public const int MaxDepth = 10;
    }
}
