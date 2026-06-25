namespace FolderGraph.Services.Abstractions
{
    /// <summary>
    /// 파일/폴더 이동, 기본 앱으로 열기 등 파일시스템 부작용을 감싸는 추상화.
    /// ViewModel이 System.IO / System.Diagnostics에 직접 의존하지 않게 하여
    /// 테스트와 교체가 가능하도록 한다(DIP).
    /// </summary>
    public interface IFileOperationService
    {
        /// <summary>
        /// 항목을 대상 폴더로 이동한다. 실패 시 예외를 던진다(호출부에서 처리).
        /// </summary>
        /// <param name="sourcePath">이동할 파일/폴더의 전체 경로.</param>
        /// <param name="destinationDirectory">대상 폴더 경로.</param>
        /// <param name="isFolder">대상이 폴더이면 true.</param>
        void Move(string sourcePath, string destinationDirectory, bool isFolder);

        /// <summary>운영체제 기본 앱으로 파일을 연다. 실패 시 예외를 던진다.</summary>
        void OpenWithDefaultApp(string path);
    }
}
