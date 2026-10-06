using Microsoft.Win32;
using SRM_by_Longtygu.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SRM_by_Longtygu.Tools.HashCalculator
{
    public class HashCalculatorViewModel : ViewModels.ViewModelBase
    {
        private readonly Queue<HashResultItem> _pendingQueue = new Queue<HashResultItem>();
        private bool _isWorkerRunning;
        private CancellationTokenSource _cts;

        public ObservableCollection<HashResultItem> Items { get; } = new ObservableCollection<HashResultItem>();

        private string _statusText = "Kéo thả file vào đây — chỉ đọc trực tiếp, không lưu trữ file nội bộ";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isProcessingQueue;
        public bool IsProcessingQueue
        {
            get => _isProcessingQueue;
            set => SetProperty(ref _isProcessingQueue, value);
        }

        private string _compareHash = "";
        public string CompareHash
        {
            get => _compareHash;
            set
            {
                SetProperty(ref _compareHash, value);
                // Đồng bộ hash đối chiếu ra toàn bộ danh sách — mỗi item tự tính khớp/không khớp
                foreach (var item in Items) item.CompareHash = value;
            }
        }

        public ICommand BrowseFilesCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand CopyHashCommand { get; }

        public HashCalculatorViewModel()
        {
            BrowseFilesCommand = new RelayCommand(_ => BrowseFiles());

            ClearAllCommand = new RelayCommand(_ =>
            {
                _cts?.Cancel();
                _pendingQueue.Clear();
                Items.Clear();
                StatusText = "Kéo thả file vào đây — chỉ đọc trực tiếp, không lưu trữ file nội bộ";
            });

            CancelCommand = new RelayCommand(_ => _cts?.Cancel());

            RemoveItemCommand = new RelayCommand(param =>
            {
                if (param is HashResultItem item) Items.Remove(item);
            });

            CopyHashCommand = new RelayCommand(param =>
            {
                if (param is string hash && !string.IsNullOrEmpty(hash))
                {
                    try { Clipboard.SetText(hash); } catch { /* bỏ qua lỗi clipboard */ }
                }
            });
        }

        private void BrowseFiles()
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Chọn file để tính hash"
            };

            if (dialog.ShowDialog() == true)
            {
                AddFiles(dialog.FileNames);
            }
        }

        // Được gọi từ code-behind khi người dùng kéo thả file vào view, hoặc từ BrowseFiles ở trên.
        public void AddFiles(IEnumerable<string> filePaths)
        {
            var addedAny = false;

            foreach (var path in filePaths)
            {
                if (!File.Exists(path)) continue;

                // Tránh thêm trùng file đã có trong danh sách
                if (Items.Any(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase))) continue;

                FileInfo fi;
                try { fi = new FileInfo(path); }
                catch { continue; }

                var item = new HashResultItem
                {
                    FileName = fi.Name,
                    FilePath = path,
                    SizeDisplay = HashCalculatorHelper.FormatSize(fi.Length),
                    IsProcessing = true
                };
                item.CompareHash = CompareHash;

                Items.Insert(0, item); // file mới thả vào hiển thị lên đầu danh sách
                _pendingQueue.Enqueue(item);
                addedAny = true;
            }

            if (addedAny) _ = RunWorkerAsync();
        }

        // Xử lý hàng đợi TUẦN TỰ từng file một — tránh nhiều luồng đọc đĩa cùng lúc làm chậm nhau
        // và làm sai lệch % tiến trình hiển thị.
        private async Task RunWorkerAsync()
        {
            if (_isWorkerRunning) return;
            _isWorkerRunning = true;
            IsProcessingQueue = true;
            _cts = new CancellationTokenSource();

            try
            {
                while (_pendingQueue.Count > 0)
                {
                    if (_cts.IsCancellationRequested)
                    {
                        while (_pendingQueue.Count > 0)
                        {
                            var skipped = _pendingQueue.Dequeue();
                            skipped.IsProcessing = false;
                            skipped.SetError("Đã hủy");
                        }
                        break;
                    }

                    var item = _pendingQueue.Dequeue();
                    try
                    {
                        await HashCalculatorHelper.ComputeHashesAsync(item, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        item.IsProcessing = false;
                        item.SetError("Đã hủy");
                    }
                }
            }
            finally
            {
                _isWorkerRunning = false;
                IsProcessingQueue = false;
                StatusText = Items.Count > 0
                    ? $"Hoàn tất — {Items.Count} file"
                    : "Kéo thả file vào đây — chỉ đọc trực tiếp, không lưu trữ file nội bộ";
            }
        }
    }
}
