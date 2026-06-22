using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FolderGraph.Core
{
    /// <summary>
    /// INotifyPropertyChanged를 구현한 ViewModel/관찰 가능 객체의 베이스 클래스.
    /// CommunityToolkit.Mvvm 대신 직접 구현 (VS 2017 / C# 7.3 호환).
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 속성 변경 알림을 발생시킨다. [CallerMemberName] 덕분에 호출부에서
        /// 속성 이름을 생략하면 자동으로 채워진다.
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// 필드 값이 실제로 바뀐 경우에만 대입 + 변경 알림을 수행한다.
        /// 변경되었으면 true를 반환.
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
