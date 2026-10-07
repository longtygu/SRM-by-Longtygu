using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace SRM_by_Longtygu.Services
{
    /// <summary>
    /// Trạng thái "đang bận" dùng chung toàn ứng dụng. Khi có tiến trình dài đang chạy (sao lưu, phục hồi, cài đặt...),
    /// các nút chuyển tab bị khóa và cửa sổ hỏi xác nhận trước khi đóng, tránh làm gián đoạn tiến trình.
    /// </summary>
    public interface IBusyService : INotifyPropertyChanged
    {
        /// <summary>True nếu có ít nhất một tiến trình đang giữ khóa.</summary>
        bool IsBusy { get; }

        /// <summary>Tên tiến trình mới nhất đang chạy (để hiển thị), rỗng nếu không bận.</summary>
        string CurrentOperation { get; }

        /// <summary>
        /// Bắt đầu giữ khóa. Luôn gọi Dispose() của giá trị trả về khi xong (nên đặt trong finally hoặc dùng using)
        /// để khóa được nhả kể cả khi tiến trình lỗi. Dispose nhiều lần không sao.
        /// </summary>
        IDisposable Begin(string operationName);
    }

    public class BusyService : IBusyService
    {
        private readonly object _sync = new object();
        private readonly List<Lease> _leases = new List<Lease>();
        private bool _isBusy;
        private string _currentOperation = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsBusy
        {
            get { lock (_sync) { return _isBusy; } }
        }

        public string CurrentOperation
        {
            get { lock (_sync) { return _currentOperation; } }
        }

        public IDisposable Begin(string operationName)
        {
            var lease = new Lease(this, string.IsNullOrWhiteSpace(operationName) ? "Tiến trình đang chạy" : operationName);
            lock (_sync)
            {
                _leases.Add(lease);
            }
            Refresh();
            return lease;
        }

        private void End(Lease lease)
        {
            lock (_sync)
            {
                _leases.Remove(lease);
            }
            Refresh();
        }

        private void Refresh()
        {
            bool busyChanged;
            bool operationChanged;

            lock (_sync)
            {
                bool busy = _leases.Count > 0;
                string operation = busy ? _leases[_leases.Count - 1].Name : string.Empty;

                busyChanged = busy != _isBusy;
                operationChanged = operation != _currentOperation;

                _isBusy = busy;
                _currentOperation = operation;
            }

            if (!busyChanged && !operationChanged) return;

            void Raise()
            {
                if (busyChanged) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
                if (operationChanged) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentOperation)));

                // Buộc các nút (Command.CanExecute) cập nhật trạng thái bật/tắt ngay, không đợi người dùng thao tác chuột/phím
                CommandManager.InvalidateRequerySuggested();
            }

            // PropertyChanged phải phát trên UI thread (Binding của WPF)
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                Raise();
            else
                dispatcher.BeginInvoke((Action)Raise);
        }

        private sealed class Lease : IDisposable
        {
            private BusyService? _owner;

            public string Name { get; }

            public Lease(BusyService owner, string name)
            {
                _owner = owner;
                Name = name;
            }

            public void Dispose()
            {
                // Chỉ nhả đúng một lần dù Dispose bị gọi nhiều lần
                var owner = Interlocked.Exchange(ref _owner, null);
                owner?.End(this);
            }
        }
    }
}
