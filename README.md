# Hệ thống Giám sát USB và File Cục bộ - UsbDlpAgent

Dự án này xây dựng một công cụ giám sát các hoạt động ghi dữ liệu ra thiết bị lưu trữ ngoài (USB, ổ cứng rời) và các thao tác file trên thư mục cục bộ được chỉ định, nhằm phục vụ công tác bảo vệ dữ liệu và phát hiện các hành vi tiềm ẩn nguy cơ. Hệ thống bao gồm **UsbDlpAgent Service** (Worker Service .NET chạy nền) và **UsbDlpAgent.UI** (ứng dụng WPF Desktop), giao tiếp qua SignalR.

---

## Tính năng chính

* Phát hiện cắm/rút thiết bị USB
* Giám sát tạo mới, xóa, sửa đổi, đổi tên file/thư mục trên USB và các thư mục cục bộ được cấu hình
* Phát hiện di chuyển file từ thư mục cục bộ sang USB
* Lưu trữ lịch sử sự kiện vào file JSON cục bộ (NDJSON)
* Giao diện WPF hiển thị sự kiện real-time và cho phép xem lại lịch sử

---

## Yêu cầu Hệ thống

* **Hệ điều hành**: Windows 10 (phiên bản 1607+) hoặc Windows 11
* **.NET Runtime**: .NET 8 Runtime (hoặc tương ứng). [Tải về tại đây](https://dotnet.microsoft.com/download/dotnet/8.0)
* **Quyền Administrator**: Cần thiết để cài đặt Service

---

## Cấu trúc Dự án (Ví dụ)

```
.
├── UsbDlpAgent/             # Project Worker Service
│   ├── appsettings.json     # Cấu hình Service
│   └── scripts/             # Chứa install-service.ps1
├── UsbDlpAgent.SharedModels/ # Models dùng chung giữa Service và UI
├── UsbDlpAgent.UI/          # Project WPF Desktop UI
├── .gitignore
└── YourSolutionName.sln     # File Solution
```

---

## Hướng dẫn Cài đặt và Chạy (Chế độ Phát triển)

### Bước 1: Clone Repository

```bash
git clone https://github.com/YOUR_USERNAME/YOUR_REPOSITORY_NAME.git
cd YOUR_REPOSITORY_NAME
```

### Bước 2: Build Dự án

```bash
dotnet build YourSolutionName.sln
```

### Bước 3: Cấu hình Service

* Mở file `UsbDlpAgent/appsettings.json`
* Cấu hình mục `DlpSettings:LocalPathsToMonitor` với các thư mục cục bộ muốn giám sát, ví dụ:

  ```json
  "DlpSettings": {
    "LocalPathsToMonitor": [
      "C:\\Users\\YourUserName\\Documents",
      "D:\\SensitiveData"
    ]
  }
  ```
* (Tùy chọn) Kiểm tra `Kestrel:Endpoints:Http:Url` (mặc định `http://localhost:5123`)

> **Lưu ý**: Không giám sát toàn bộ ổ đĩa như `C:\` nếu không cần thiết.

### Bước 4: Chạy UsbDlpAgent Service

```bash
cd UsbDlpAgent
dotnet run
```

* Theo dõi log trên console. Service sẽ lắng nghe SignalR Hub trên port đã cấu hình.

### Bước 5: Chạy UsbDlpAgent UI

```bash
cd ../UsbDlpAgent.UI
dotnet run
```

* Cửa sổ WPF sẽ xuất hiện và tự động kết nối đến Service. Sự kiện real-time sẽ được hiển thị.

---

## Triển khai dưới dạng Windows Service (Môi trường Production)

1. **Publish Service**

   ```bash
   dotnet publish -c Release -r win-x64 -o ./publish_service
   ```
2. **Cài đặt Service**

   * Đảm bảo đã chạy PowerShell với quyền **Administrator**
   * Di chuyển vào thư mục `publish_service/scripts`
   * Thực thi:

     ```powershell
     .\install-service.ps1 -ServiceExePath "C:\Program Files\UsbDlpAgentService\UsbDlpAgent.exe"
     ```
3. **Quản lý Service**

   * Mở `services.msc`, tìm **USB DLP Agent Service**
   * Start service và đặt **Startup Type** là **Automatic**

---

## Gỡ lỗi (Troubleshooting)

* **Address already in use**: Port đã có tiến trình khác chiếm dụng. Đóng tiến trình hoặc thay đổi port trong `appsettings.json` (Service) và `MainWindow.xaml.cs` (UI).
* **UI không kết nối được**: Kiểm tra Service đang chạy, URL/port SignalR Hub khớp, tường lửa không chặn.
* **Không thấy sự kiện**: Kiểm tra log Service, đảm bảo quyền Administrator, kiểm tra cấu hình `LocalPathsToMonitor`.

---

## Đóng góp (Contributing)

* Mọi góp ý vui lòng mở Issue hoặc Pull Request trên GitHub.

---

## Giấy phép (License)

* [MIT License](LICENSE)
