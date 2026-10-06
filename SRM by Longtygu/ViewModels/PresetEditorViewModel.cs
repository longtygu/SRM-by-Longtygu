using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.ViewModels
{
    public class PresetEditorViewModel : ViewModelBase
    {
        public int PresetId { get; set; }

        private string _presetName = "";
        public string PresetName { get => _presetName; set => SetProperty(ref _presetName, value); }

        private string _presetIconPath = "";
        public string PresetIconPath { get => _presetIconPath; set => SetProperty(ref _presetIconPath, value); }

        // Chế độ xem: True = Grid (Ô vuông), False = List (DataGrid)
        private bool _isGridView = true;
        public bool IsGridView { get => _isGridView; set => SetProperty(ref _isGridView, value); }

        // Biến điều khiển việc gộp nhóm (Theo Danh mục hoặc Nhà phát triển)
        private string _selectedGroupBy = "Category";
        public string SelectedGroupBy
        {
            get => _selectedGroupBy;
            set
            {
                if (SetProperty(ref _selectedGroupBy, value))
                {
                    ApplyGrouping(); // Gọi hàm gộp nhóm khi đổi lựa chọn
                }
            }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    AvailableSoftwaresView.Refresh();
            }
        }

        public ObservableCollection<Software> AvailableSoftwares { get; set; } = new ObservableCollection<Software>();
        public ICollectionView AvailableSoftwaresView { get; }
        public ObservableCollection<Software> SelectedSoftwares { get; set; } = new ObservableCollection<Software>();

        public ICommand ToggleViewCommand { get; }
        public ICommand SelectIconCommand { get; }
        public ICommand AddSoftwareCommand { get; }
        public ICommand RemoveSoftwareCommand { get; }

        public PresetEditorViewModel(Preset existingPreset = null, System.Collections.Generic.List<Software> allSoftwares = null)
        {
            AvailableSoftwaresView = CollectionViewSource.GetDefaultView(AvailableSoftwares);

            // Thuật toán lọc tìm kiếm
            AvailableSoftwaresView.Filter = item =>
            {
                if (string.IsNullOrWhiteSpace(SearchText)) return true;
                var sw = item as Software;
                return sw != null && (sw.Name.ToLower().Contains(SearchText.ToLower()) ||
                                      sw.Category.ToLower().Contains(SearchText.ToLower()));
            };

            ToggleViewCommand = new RelayCommand(_ => IsGridView = !IsGridView);
            SelectIconCommand = new RelayCommand(_ => ChooseIcon());

            // Lệnh Thêm & Xóa (Chống trùng lặp)
            AddSoftwareCommand = new RelayCommand(param =>
            {
                if (param is Software sw)
                {
                    if (SelectedSoftwares.Any(x => x.Id == sw.Id))
                    {
                        MessageBox.Show($"Phần mềm '{sw.Name}' đã tồn tại trong gói này!", "Cảnh báo trùng lặp", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    SelectedSoftwares.Add(sw);
                }
            });

            RemoveSoftwareCommand = new RelayCommand(param =>
            {
                if (param is Software sw && SelectedSoftwares.Contains(sw)) SelectedSoftwares.Remove(sw);
            });

            if (allSoftwares != null)
            {
                foreach (var s in allSoftwares) AvailableSoftwares.Add(s);
            }

            if (existingPreset != null)
            {
                PresetId = existingPreset.Id;
                PresetName = existingPreset.Name;
                PresetIconPath = existingPreset.IconPath;
                if (existingPreset.Softwares != null)
                {
                    foreach (var s in existingPreset.Softwares) SelectedSoftwares.Add(s);
                }
            }

            // Kích hoạt phân nhóm mặc định
            ApplyGrouping();
        }

        private void ApplyGrouping()
        {
            AvailableSoftwaresView.GroupDescriptions.Clear();
            AvailableSoftwaresView.GroupDescriptions.Add(new PropertyGroupDescription(SelectedGroupBy));
        }

        private void ChooseIcon()
        {
            string initDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "PresetIcons");
            if (!Directory.Exists(initDir)) Directory.CreateDirectory(initDir);

            OpenFileDialog dialog = new OpenFileDialog { Title = "Chọn Icon", Filter = "Image Files|*.png;*.jpg;*.jpeg;*.ico", InitialDirectory = initDir };
            if (dialog.ShowDialog() == true) PresetIconPath = dialog.FileName;
        }
    }
}