using System.Windows.Media;
using FolderGraph.Helpers.Abstractions;

namespace FolderGraph.Helpers
{
    /// <summary>
    /// depth가 깊어질수록 기준 색에 흰색을 더 섞어 밝게 만드는 색 계산기.
    /// - depth 0 = 원색
    /// - depth 1~MaxColorDepth = 단계적으로 흰색 혼합
    /// - depth > MaxColorDepth = MaxColorDepth와 동일(더 옅어지지 않음)
    ///
    /// 흰색 혼합(명도 ↑) 방식이며, 회색으로 빠지는 채도 방식이 아니다.
    /// </summary>
    public class DepthColorCalculator : INodeColorCalculator
    {
        /// <summary>
        /// 가장 옅은 단계(MaxColorDepth)에서 흰색을 섞는 비율.
        /// 1.0이면 완전 흰색이 되어 안 보이므로 0~1 사이로 둔다.
        /// </summary>
        private const double MaxLighten = 0.72;

        public Color GetShade(Color baseColor, int relativeDepth)
        {
            int maxDepth = AppConstants.MaxColorDepth; // 4

            int d = relativeDepth;
            if (d < 0) d = 0;
            if (d > maxDepth) d = maxDepth; // 그 아래로는 동일 톤 유지

            // 0(원색) ~ MaxLighten(가장 옅음) 사이의 혼합 비율
            double t = (maxDepth == 0) ? 0.0 : ((double)d / maxDepth) * MaxLighten;

            byte r = MixWithWhite(baseColor.R, t);
            byte g = MixWithWhite(baseColor.G, t);
            byte b = MixWithWhite(baseColor.B, t);

            return Color.FromRgb(r, g, b);
        }

        /// <summary>채널 값을 흰색(255)과 t 비율로 선형 혼합한다.</summary>
        private static byte MixWithWhite(byte channel, double t)
        {
            double mixed = channel + (255.0 - channel) * t;
            if (mixed < 0) mixed = 0;
            if (mixed > 255) mixed = 255;
            return (byte)(mixed + 0.5);
        }
    }
}
