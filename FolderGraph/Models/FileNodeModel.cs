using System.Collections.Generic;

namespace FolderGraph.Models
{
    /// <summary>
    /// 파일 시스템의 파일/폴더 하나를 나타내는 순수 데이터 모델.
    /// 좌표·색상 등 화면 표현은 포함하지 않는다 (그건 NodeViewModel의 책임).
    /// </summary>
    public class FileNodeModel
    {
        /// <summary>표시 이름 (파일명 또는 폴더명).</summary>
        public string Name { get; set; }

        /// <summary>전체 경로.</summary>
        public string FullPath { get; set; }

        /// <summary>폴더/파일 구분.</summary>
        public NodeType Type { get; set; }

        /// <summary>
        /// 루트(사용자가 입력한 경로) 기준 깊이.
        /// 루트 폴더 자체는 노드로 그리지 않으므로, 루트 바로 아래 항목이 0.
        /// </summary>
        public int Depth { get; set; }

        /// <summary>파일 크기(바이트). 폴더는 0.</summary>
        public long SizeBytes { get; set; }

        /// <summary>부모 노드. 루트 바로 아래 항목은 null.</summary>
        public FileNodeModel Parent { get; set; }

        /// <summary>자식 노드 목록. 파일이면 비어 있다.</summary>
        public List<FileNodeModel> Children { get; private set; }

        public FileNodeModel()
        {
            Children = new List<FileNodeModel>();
        }

        /// <summary>자식이 하나라도 있으면 폴더로 간주할 수 있다.</summary>
        public bool HasChildren
        {
            get { return Children.Count > 0; }
        }

        /// <summary>
        /// 파일 확장자(소문자, 점 포함). 폴더이거나 확장자가 없으면 빈 문자열.
        /// 예: ".txt", ".png"
        /// </summary>
        public string Extension
        {
            get
            {
                if (Type != NodeType.File)
                {
                    return string.Empty;
                }
                int dot = Name.LastIndexOf('.');
                if (dot < 0)
                {
                    return string.Empty;
                }
                return Name.Substring(dot).ToLowerInvariant();
            }
        }
    }
}
