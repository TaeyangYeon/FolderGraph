using System.Windows.Media;

namespace FolderGraph.ViewModels
{
    /// <summary>색상별 노드 집계 한 줄(스와치 색, 개수, 비율).</summary>
    public class ColorStat
    {
        public ColorStat(Brush swatch, string label, int count, double percent)
        {
            Swatch = swatch;
            Label = label;
            Count = count;
            Percent = percent;
        }

        /// <summary>표시용 색 스와치.</summary>
        public Brush Swatch { get; private set; }

        /// <summary>색 설명(예: "회색", "#3B82F6").</summary>
        public string Label { get; private set; }

        /// <summary>이 색 노드 수.</summary>
        public int Count { get; private set; }

        /// <summary>전체 대비 비율(0~100).</summary>
        public double Percent { get; private set; }

        /// <summary>"123 (45.6%)" 형태의 표시 문자열.</summary>
        public string Display
        {
            get { return string.Format("{0} ({1:0.0}%)", Count, Percent); }
        }
    }
}
