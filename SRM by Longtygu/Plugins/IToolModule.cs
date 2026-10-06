using System;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Plugins
{
    // Interface bắt buộc mọi "Công cụ mở rộng" (Tool Module) phải implement.
    // Các project Tool riêng chỉ cần Add Reference tới file .exe của SRM
    // (Visual Studio cho phép reference thẳng vào .exe) là dùng được interface này,
    // không cần tạo project "contract" riêng — giữ mọi thứ đơn giản như bạn muốn.
    public interface IToolModule
    {
        // Mã định danh duy nhất, tránh trùng khi có nhiều DLL
        string Id { get; }

        // Tên hiển thị trên card
        string Name { get; }

        // Mô tả ngắn hiển thị dưới tên
        string Description { get; }

        // Version của tool, hiển thị cho người dùng biết đang chạy bản nào
        string Version { get; }

        // Tên icon theo bộ iconPacks:PackIconMaterial đang dùng trong app (VD: "Wrench", "Speedometer"...)
        string IconKind { get; }

        // Trả về giao diện (UserControl) để nhúng vào tab.
        // serviceProvider cho phép tool GetService<T>() lấy Repository/Service của app NẾU cần dùng tới.
        UserControl CreateView(IServiceProvider serviceProvider);
    }
}