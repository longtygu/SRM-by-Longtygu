# SRM by Longtygu [ VIETNAMESE ]

Bộ công cụ thư viện phần mềm, quản lý triển khai ứng dụng và bộ công cụ / tài nguyên phần mềm tệp tin đính kèm siêu đơn giản, có thể chạy cục bộ trên USB / ổ cứng rời !
<img width="1452" height="804" alt="image" src="https://github.com/user-attachments/assets/0993500f-4d1e-4b6f-897c-092ebf9df4fb" />

## Tính năng

- Thư viện phần mềm tích hợp quản lý phiên bản.
- Tạo bộ cài đặt tùy chỉnh (Preset) và triển khai nhiều phần mềm chỉ với một cú nhấp chuột.
- Trình quản lý gỡ cài đặt phần mềm.
- Trình quét cập nhật phàn mềm tự động.
- Tích hợp các công cụ hệ thống :
  - Kiểm tra tình trạng ổ đĩa (Disk Health).
  - Xem thông tin phần cứng (Hardware Info).
  - Tính toán mã băm (Hash Calculator).
  - Kiểm tra RAM (RAM Test).
  - Kiểm tra tình trạng Windows (Windows Health Check).
- Giao diện hỗ trợ tiếng Anh và tiếng Việt ( sẽ có sắp tới )
- Tự động kiểm tra bản cập nhật bản thân thông qua GitHub Releases.

## Yêu cầu hệ thống

- Windows 10 trở lên (không hỗ trợ Windows 8.1 trở xuống / không chạy được với linux ).
- ổ ứng 4GB hoặc cao hơn [ chuẩn USB 3.1 hoặc cao hơn ] , ram tối thiểu 8GB, chạy mượt trên bất kỳ cpu nào từ 4 lõi trở lên ;)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- Có thể cần quyền quản trị viên để đọc dữ liệu từ một số cảm biến phần cứng.

## Cài đặt

1. Truy cập trang [Releases](https://github.com/longtygu/SRM-by-Longtygu/releases).
2. Tải xuống tệp `.zip` của phiên bản mới nhất và giải nén.
3. Chạy tệp `SRM by Longtygu.exe`.

## Biên dịch từ mã nguồn

```bash
git clone https://github.com/longtygu/SRM-by-Longtygu.git
cd SRM-by-Longtygu
dotnet build -c Release
```

## Giấy phép

Phần mềm được phát hành theo giấy phép MIT. Xem chi tiết tại [LICENSE.txt](LICENSE.txt).


# SRM by Longtygu [ ENGLISH ]

A lightweight software library, deployment manager, and toolkit for managing software resources and file attachments. Designed to run locally from a USB drive or external hard drive, with a simple and portable workflow!


## Features

- Software library with integrated version management.
- Custom software presets and one-click deployment of multiple applications.
- Software uninstallation manager.
- Automatic software update scanner.
- Built-in system tools:
  - Disk Health Checker.
  - Hardware Information.
  - Hash Calculator.
  - RAM Test.
  - Windows Health Check.
- English and Vietnamese interface support (Vietnamese translation coming soon).
- Automatic self-update checks via GitHub Releases.

## System Requirements

- Windows 10 or later (Windows 8.1 and earlier are not supported; Linux is not supported).
- At least 4 GB of available storage space (USB 3.1 or faster recommended).
- At least 8 GB of RAM.
- Runs smoothly on most CPUs with 4 or more cores ;)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- Administrator privileges may be required to access certain hardware sensor data.

## Installation

1. Visit the [Releases](https://github.com/longtygu/SRM-by-Longtygu/releases) page.
2. Download the latest `.zip` file and extract it.
3. Run `SRM by Longtygu.exe`.

## Build from Source

```bash
git clone https://github.com/longtygu/SRM-by-Longtygu.git
cd SRM-by-Longtygu
dotnet build -c Release
```

## License

This project is released under the MIT License. See [LICENSE.txt](LICENSE.txt) for details.
