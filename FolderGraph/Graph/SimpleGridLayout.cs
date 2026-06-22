using System;
using System.Collections.Generic;
using FolderGraph.Graph.Abstractions;
using FolderGraph.ViewModels;

namespace FolderGraph.Graph
{
    /// <summary>
    /// Phase 1용 임시 레이아웃. 노드를 격자 형태로 단순 배치한다.
    /// Phase 2에서 ForceDirectedLayout으로 교체된다 (IGraphLayoutEngine 구현 교체).
    /// </summary>
    public class SimpleGridLayout : IGraphLayoutEngine
    {
        private const double Spacing = 70.0;
        private const double Margin = 50.0;

        public void Arrange(IList<NodeViewModel> nodes, double areaWidth, double areaHeight)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            double usableWidth = Math.Max(areaWidth - Margin * 2, Spacing);
            int columns = Math.Max(1, (int)(usableWidth / Spacing));

            for (int i = 0; i < nodes.Count; i++)
            {
                int row = i / columns;
                int col = i % columns;

                nodes[i].X = Margin + col * Spacing;
                nodes[i].Y = Margin + row * Spacing;
            }
        }
    }
}
