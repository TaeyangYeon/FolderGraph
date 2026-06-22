using System.Threading;
using System.Threading.Tasks;
using FolderGraph.Models;

namespace FolderGraph.Services.Abstractions
{
    /// <summary>
    /// 폴더 경로를 비동기로 스캔해 그래프 데이터를 생성하는 서비스의 추상화.
    /// 구현을 교체(테스트용 가짜 등)할 수 있도록 인터페이스로 분리한다 (DIP).
    /// </summary>
    public interface IFolderScanner
    {
        /// <summary>
        /// 지정한 루트 경로 아래를 스캔한다. 루트 폴더 자체는 노드로 만들지 않고,
        /// 그 안의 자식부터 노드로 만든다.
        /// </summary>
        /// <param name="rootPath">스캔 기준 루트 폴더 경로.</param>
        /// <param name="maxDepth">렌더링 깊이 제한.</param>
        /// <param name="includeHidden">숨김 파일/폴더 포함 여부.</param>
        /// <param name="cancellationToken">취소 토큰.</param>
        Task<GraphData> ScanAsync(
            string rootPath,
            int maxDepth,
            bool includeHidden,
            CancellationToken cancellationToken);
    }
}
