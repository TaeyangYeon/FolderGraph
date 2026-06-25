namespace FolderGraph.Services.Abstractions
{
    /// <summary>
    /// 확인/에러 대화상자를 추상화한다. ViewModel이 MessageBox(View 계층)에
    /// 직접 의존하지 않게 하여 테스트 가능하게 한다(DIP).
    /// </summary>
    public interface IDialogService
    {
        /// <summary>예/아니오 확인. 사용자가 '예'를 누르면 true.</summary>
        bool Confirm(string message, string title);

        /// <summary>오류 메시지를 표시한다.</summary>
        void ShowError(string message, string title);
    }
}
