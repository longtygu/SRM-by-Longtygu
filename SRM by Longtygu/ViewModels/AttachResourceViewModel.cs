using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;

namespace SRM_by_Longtygu.ViewModels
{
    // MỚI: Bọc 1 ResourceFile + trạng thái tích chọn (checkbox) để hiển thị trong DataGrid nhiều lựa chọn
    public class SelectableResourceItem : ViewModelBase
    {
        public ResourceFile Resource { get; }

        public string Name => Resource.Name;
        public string Category => Resource.Category;
        public string Size => Resource.Size;
        public string IconPath => Resource.IconPath;
        public string FolderPath => Resource.FolderPath;
        public string FileName => Resource.FileName;

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        public SelectableResourceItem(ResourceFile resource)
        {
            Resource = resource;
        }
    }

    // MỚI: ViewModel cho cửa sổ "Đính kèm tài nguyên" — tìm kiếm + chọn nhiều tài nguyên để gắn vào 1 phần mềm
    public class AttachResourceViewModel : ViewModelBase
    {
        private readonly IResourceFileRepository _resourceRepo;
        private readonly ISoftwareResourceRepository _mappingRepo;
        private readonly Software _software;

        public string SoftwareName => _software?.Name;

        public ObservableCollection<SelectableResourceItem> Items { get; } = new ObservableCollection<SelectableResourceItem>();
        public ICollectionView ItemsView { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ItemsView.Refresh();
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            set => SetProperty(ref _selectedCount, value);
        }

        public ICommand AttachSelectedCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearSelectionCommand { get; }

        // Cửa sổ tự đóng khi bấm "Đính kèm" hoặc "Hủy"
        public event Action CloseWindow;

        public AttachResourceViewModel(IResourceFileRepository resourceRepo, ISoftwareResourceRepository mappingRepo, Software software)
        {
            _resourceRepo = resourceRepo;
            _mappingRepo = mappingRepo;
            _software = software;

            ItemsView = CollectionViewSource.GetDefaultView(Items);
            ItemsView.Filter = FilterItem;

            AttachSelectedCommand = new RelayCommand(_ => _ = ExecuteAttachAsync(), _ => SelectedCount > 0 && !IsLoading);
            CancelCommand = new RelayCommand(_ => CloseWindow?.Invoke());
            SelectAllCommand = new RelayCommand(_ => SetAllChecked(true));
            ClearSelectionCommand = new RelayCommand(_ => SetAllChecked(false));

            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            IsLoading = true;
            try
            {
                // Những tài nguyên đã đính kèm rồi thì không cho chọn lại nữa
                var attachedIds = (await _mappingRepo.GetAttachedResourceIdsAsync(_software.Id)).ToHashSet();
                var all = _resourceRepo.GetAll();

                Items.Clear();
                foreach (var resource in all)
                {
                    if (attachedIds.Contains(resource.Id)) continue;

                    var item = new SelectableResourceItem(resource);
                    item.PropertyChanged += Item_PropertyChanged;
                    Items.Add(item);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableResourceItem.IsChecked))
            {
                SelectedCount = Items.Count(i => i.IsChecked);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void SetAllChecked(bool value)
        {
            // ICollectionView không có sẵn PassesFilter công khai -> tự lọc lại bằng đúng
            // logic FilterItem() đang gán cho ItemsView.Filter, để chỉ chọn/bỏ chọn các dòng đang hiển thị.
            foreach (var item in Items.Where(i => FilterItem(i)))
            {
                item.IsChecked = value;
            }
        }

        private bool FilterItem(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (!(obj is SelectableResourceItem item)) return false;

            string keyword = SearchText.Trim();
            return Contains(item.Name, keyword) || Contains(item.Category, keyword) || Contains(item.FileName, keyword);
        }

        private static bool Contains(string source, string keyword)
        {
            if (string.IsNullOrEmpty(source)) return false;
            return source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task ExecuteAttachAsync()
        {
            var selectedIds = Items.Where(i => i.IsChecked).Select(i => i.Resource.Id).ToList();
            if (selectedIds.Count == 0) return;

            IsLoading = true;
            try
            {
                await _mappingRepo.AttachManyAsync(_software.Id, selectedIds);
            }
            finally
            {
                IsLoading = false;
            }

            CloseWindow?.Invoke();
        }
    }
}