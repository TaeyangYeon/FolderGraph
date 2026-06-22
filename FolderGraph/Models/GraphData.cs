using System.Collections.Generic;

namespace FolderGraph.Models
{
    /// <summary>
    /// 폴더 스캔 결과를 담는 컨테이너.
    /// 평탄화된 전체 노드 목록과, 루트 직속(최상위) 노드 목록을 함께 제공한다.
    /// 엣지는 각 노드의 Parent/Children 관계로 표현되므로 별도 목록을 두지 않는다.
    /// </summary>
    public class GraphData
    {
        /// <summary>스캔된 모든 노드(평탄화).</summary>
        public List<FileNodeModel> AllNodes { get; private set; }

        /// <summary>루트 바로 아래의 최상위 노드들(Depth 0).</summary>
        public List<FileNodeModel> RootNodes { get; private set; }

        /// <summary>스캔 기준이 된 루트 폴더 경로(노드로는 그리지 않음).</summary>
        public string RootPath { get; set; }

        /// <summary>MAX_NODES 한계로 스캔이 중단되었는지 여부.</summary>
        public bool TruncatedByLimit { get; set; }

        public GraphData()
        {
            AllNodes = new List<FileNodeModel>();
            RootNodes = new List<FileNodeModel>();
        }

        public int NodeCount
        {
            get { return AllNodes.Count; }
        }
    }
}
