using System.Windows.Media;

namespace FolderGraph.ViewModels
{
    /// <summary>
    /// 색상 팔레트의 한 칸. 적용에 쓸 Color와 스와치 표시에 쓸 Brush를 함께 들고 있다.
    /// </summary>
    public class PaletteColor
    {
        public Color Color { get; private set; }
        public Brush Brush { get; private set; }

        public PaletteColor(Color color)
        {
            Color = color;
            var b = new SolidColorBrush(color);
            b.Freeze();
            Brush = b;
        }
    }
}
