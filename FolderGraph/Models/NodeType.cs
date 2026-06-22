namespace FolderGraph.Models
{
    /// <summary>
    /// 노드가 폴더인지 파일인지 구분.
    /// (화면상 시각 구분은 하지 않지만, 파일 이동·열기 동작 분기에 필요)
    /// </summary>
    public enum NodeType
    {
        Folder,
        File
    }
}
