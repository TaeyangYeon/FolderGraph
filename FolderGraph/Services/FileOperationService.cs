using System.Diagnostics;
using System.IO;
using FolderGraph.Services.Abstractions;

namespace FolderGraph.Services
{
    /// <summary>
    /// System.IO / Process 기반 파일 작업 구현.
    /// </summary>
    public class FileOperationService : IFileOperationService
    {
        public void Move(string sourcePath, string destinationDirectory, bool isFolder)
        {
            string name = isFolder
                ? new DirectoryInfo(sourcePath).Name
                : Path.GetFileName(sourcePath);

            string destination = Path.Combine(destinationDirectory, name);

            // 대상에 같은 이름이 이미 있으면 Move가 예외를 던진다(호출부에서 에러 표시).
            if (isFolder)
            {
                Directory.Move(sourcePath, destination);
            }
            else
            {
                File.Move(sourcePath, destination);
            }
        }

        public void OpenWithDefaultApp(string path)
        {
            // UseShellExecute=true 라야 확장자에 연결된 기본 앱으로 열린다(.NET Framework).
            var psi = new ProcessStartInfo(path);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
    }
}
