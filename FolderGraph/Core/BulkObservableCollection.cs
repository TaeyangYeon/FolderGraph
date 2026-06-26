using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace FolderGraph.Core
{
    /// <summary>
    /// 대량 추가/교체 시 CollectionChanged를 매 항목마다 발생시키지 않고,
    /// 끝에 Reset 한 번만 발생시키는 ObservableCollection.
    /// 노드/엣지를 수천 개 채울 때 뷰가 매번 전체 재빌드하는 것을 막아 성능을 크게 높인다.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppress;

        /// <summary>기존 내용을 비우고 새 항목들로 교체한 뒤, 알림을 1회만 발생시킨다.</summary>
        public void Reset(IEnumerable<T> items)
        {
            _suppress = true;
            try
            {
                Items.Clear();
                foreach (T item in items)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppress = false;
            }

            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>여러 항목을 추가한 뒤 알림을 1회만 발생시킨다.</summary>
        public void AddRange(IEnumerable<T> items)
        {
            _suppress = true;
            try
            {
                foreach (T item in items)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppress = false;
            }

            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppress)
            {
                base.OnCollectionChanged(e);
            }
        }
    }
}
