# Meinya Tablet Booster

<p align="center">
  <img src="assets/meinya_mascot.png" alt="Meinya" width="200" />
</p>

<p align="center">
  <b>Tác giả: <a href="https://github.com/Elainabaka">Elainabaka</a></b><br/>
  <i>Công cụ Windows nhỏ cho người dùng bảng vẽ: xem nhịp sự kiện chuột, thử làm mượt con trỏ bằng phần mềm, chỉnh vài khóa có sẵn trong cấu hình driver Wacom.</i>
</p>

[English below](#english)

## Bắt đầu nhanh

1. Vào mục [**Releases**](https://github.com/Elainabaka/meinya-tablet-booster/releases) của repo.
2. Tải `MeinyaTabletBooster.exe` (so mã SHA-256 ghi trong ghi chú release nếu muốn chắc file không bị đổi).
3. Bấm đúp để chạy. Cần Windows 10/11 có .NET Framework 4.8 (thường có sẵn).

> **Trước khi chạy, nên biết:**
> - File exe **chưa ký số**, nên Windows SmartScreen có thể cảnh báo: bấm **More info → Run anyway** nếu bạn tin nguồn này (hoặc tự biên dịch, mục dưới).
> - App cài **hook chuột** và gửi chuyển động qua **SendInput**, nên vài phần mềm diệt virus có thể nghi ngờ. Mã nguồn đầy đủ ở repo này để bạn tự xem.
> - App **không tăng report rate thật** của bảng vẽ; dữ liệu bút **WinTab / Windows Ink không đi qua** bộ làm mượt; mọi số Hz trong app là **ước lượng**.

## Công cụ làm gì, không làm gì

| | |
|---|---|
| **Làm** | Lắng nghe `WH_MOUSE_LL`, ưu tiên nhận diện tín hiệu bút qua chữ ký Windows Ink (`MI_WP_SIGNATURE` `0xFF515700`, mặt nạ `0xFFFFFF00`) và các gói do driver bảng vẽ user-mode (như Wacom Driver qua `InjectSyntheticPointerInput` / `SendInput`) phát ra, đồng thời lọc bỏ chuột USB vật lý xen vào trong vòng 2,5 giây (tự động chặn chữ ký `0x4D45494E` của chính app để không tự lặp). Giữa hai mẫu bút thật, ước lượng bước tiếp theo có kẹp ngân sách dịch chuyển (chống văng lố) rồi gửi qua `SendInput`. Trong ứng dụng đọc con trỏ chuột (ví dụ osu! ở chế độ chuột), con trỏ có thể trông mượt hơn trên màn tần số cao. Hãy tự thử để biết có hợp với bạn không. |
| **Không làm** | Không tăng tần số báo cáo thật của bảng vẽ, không sửa firmware. Tọa độ và lực nhấn đi qua **WinTab / Windows Ink** không đi qua bộ làm mượt này, nên phần mềm vẽ nhận bút qua các API đó không được gì thêm. Mẫu bút thật kế tiếp luôn kéo con trỏ về đúng chỗ đầu bút. |
| **Cần biết** | Bước tương đối chịu ảnh hưởng của tốc độ chuột và tùy chọn *Enhance pointer precision* của Windows. Khi đang dùng bút (Windows Ink hoặc driver user-mode) trong vòng 2,5 giây, tín hiệu chuột rời thông thường sẽ bị lọc tự động; nếu quá 2,5 giây không có gói Ink/driver, app chấp nhận cả tín hiệu con trỏ HID cơ bản để vẫn hoạt động khi hover hoặc chạy không cần Windows Ink. |

## Tính năng

1. **Nhận diện bảng vẽ & Quét lại USB**: quét USB/HID (`pnputil`) kết hợp Windows Pointer API (`GetPointerDevices`), đối chiếu với [`data/tablets_db.json`](data/tablets_db.json) (17 hồ sơ Wacom, XP-Pen, Huion, Gaomon, Veikk, Xencelabs), phân biệt được một số model dùng chung VID/PID theo tên thiết bị và có nút **Quét lại**.
2. **Nhịp mục tiêu tham khảo**: đọc tần số màn hình, gợi ý `1x` và `2x` (màn 165 Hz → 165 / 330 Hz). Đây là gợi ý để thử, không phải chuẩn kỹ thuật, không cam kết độ trễ.
3. **Cảnh báo ba màu** (xanh, vàng, đỏ) theo ngưỡng tham khảo của hồ sơ. Màu không chứng minh giới hạn phần cứng hay độ an toàn.
4. **Cấu hình driver Wacom** (`%APPDATA%\WTablet\Wacom_Tablet.dat`): chỉ sửa khóa đã có đúng dạng (`PacketRate`, `MaxDataRateOn`, `WinUseInk`, `DoubleClickDistance`). Khi chạy với quyền Admin, dịch vụ `WTabletServicePro` được **dừng trước khi ghi** và bật lại sau khi ghi xong để tránh driver ghi đè cấu hình cũ lúc tắt. Thiếu khóa thì báo và không tự chèn. Lần sửa đầu sao lưu thành `.original_backup` (không ghi đè về sau); mỗi lần ghi qua tệp tạm rồi tráo nguyên tử; không có gì đổi thì không ghi.
5. **Bộ nhớ lịch sử cố định**: bộ đệm vòng 256 ô cho khoảng thời gian, 512 điểm cho canvas; có nút dọn.

## Dữ liệu và nguồn

- Mọi số Hz và ngưỡng trong hồ sơ đều là **ước lượng** (`"hz_nguon": "uoc-luong"`, app ghi "ước lượng" cạnh số): hãng không công bố số này cho từng model.
- PID có nguồn được ghi ở trường `pid_nguon` (libwacom, OpenTabletDriver): Intuos CTL-680 `0323`, CTH-680 `0303`, Intuos Pro gen 3 `03F5` / `03F7` / `03F9`, Xencelabs Small `5204`, Medium `5201`. PID của các hồ sơ khác chưa có nguồn kèm theo.
- `PacketRate` là giá trị trong cấu hình driver Wacom; hãng không công bố đơn vị, đừng coi nó là Hz.
- Thấy số sai? Mở issue kèm nguồn (trang hãng, libwacom, OpenTabletDriver).

## Tự biên dịch từ mã nguồn

Tải mã nguồn (Code → Download ZIP, hoặc `git clone`), giải nén, rồi **bấm đúp `build.bat`**. Thiếu .NET Framework 4.x thì nó báo cách sửa; build xong nó tự mở app.

`build.bat` gọi `build.ps1`: tạo icon nếu thiếu, biên dịch bằng `csc.exe` của .NET Framework ra `MeinyaTabletBooster.exe`, rồi chạy `--self-test` (dữ liệu giả, chỉ ghi tệp trong thư mục tạm) và `--ui-smoke-test` (mở form và cài hook chuột trong giây lát). Script dừng tiến trình `MeinyaTabletBooster` đang chạy và xóa bản build cũ trong thư mục dự án. Muốn chạy tay: `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`.

Kiến trúc và các quy tắc bất biến của code: [MINDMAP.md](MINDMAP.md).

## Giấy phép

Code theo giấy phép MIT ([LICENSE](LICENSE)). Tên **Meinya**, hình Meinya trong `assets/` và tên **Elainabaka** không thuộc giấy phép code: không dùng chúng làm thương hiệu cho bản phái sinh.

Thấy công cụ có ích thì tặng repo một ⭐ để ủng hộ Elainabaka nhé.

---

## English

**Meinya Tablet Booster** is a small Windows tool for pen-tablet users: it shows the mouse-event rate, tries software cursor smoothing, and edits a few existing keys in the Wacom driver configuration.

**Quick start.** 1) Open the repo's [**Releases**](https://github.com/Elainabaka/meinya-tablet-booster/releases). 2) Download `MeinyaTabletBooster.exe` (check the SHA-256 in the release notes if you like). 3) Double-click it. Needs Windows 10/11 with .NET Framework 4.8 (usually built in).

**Before you run it.** The exe is **not code-signed**, so Windows SmartScreen may warn you: choose **More info → Run anyway** if you trust this source, or build it yourself. The app installs a **mouse hook** and sends moves through **SendInput**, so some antivirus tools may flag it; the full source is here to read. It does **not** raise the tablet's real report rate, **WinTab / Windows Ink** pen data does not pass through the smoother, and every Hz value in the app is an **estimate**.

**What it does.** Listens on `WH_MOUSE_LL`, recognizes pen signals via Windows Ink signatures (`MI_WP_SIGNATURE` `0xFF515700` with `0xFFFFFF00` mask) and user-mode tablet driver injection (such as Wacom Driver's `InjectSyntheticPointerInput` / `SendInput`), filters out physical USB mouse packets (`flags == 0`) within 2.5 seconds of active tablet input while ignoring its own `0x4D45494E` signature, and falls back to basic HID pointer input after 2.5 seconds of idle so desktop hover and non-Ink modes still work. Between two real samples it predicts the next step within an anti-overshoot distance budget and injects a **relative** move with `SendInput`. In apps that read the mouse cursor (for example osu! in mouse mode) the cursor may look smoother on a high-refresh display. Try it and judge for yourself.

**What it does not do.** It does not raise the tablet's real report rate and does not touch firmware. Pen coordinates and pressure delivered through **WinTab / Windows Ink** do not pass through this smoother, so drawing apps that use those APIs gain nothing. The next real pen sample always pulls the cursor back to the pen tip. Relative steps are affected by Windows pointer speed and *Enhance pointer precision*. While tablet Ink/driver events are active (within 2.5 seconds), regular physical mouse moves are automatically ignored; after 2.5 seconds without tablet-specific events, regular HID pointer input is accepted as a fallback for desktop hover or testing.

**Features.** Tablet detection against [`data/tablets_db.json`](data/tablets_db.json) (17 profiles; disambiguates shared VID/PID devices by device name when available, plus a manual rescan button). Reference target rates of 1x and 2x the display refresh, which are suggestions to try, not a standard or a latency promise. Three-color advisory against each profile's reference thresholds. Wacom driver config (`%APPDATA%\WTablet\Wacom_Tablet.dat`): when running elevated, `WTabletServicePro` is stopped before writing and restarted afterwards so the service does not overwrite the file on shutdown; only keys already present in the expected form are changed; a missing key is reported and never inserted; the first edit keeps a `.original_backup`; writes go through a temp file and an atomic replace, and nothing is written when nothing changed. Fixed-size history buffers (256 intervals, 512 canvas points).

**Data.** Every Hz value and threshold in the profiles is an **estimate** (`"hz_nguon": "uoc-luong"`; the app labels it). PIDs with a source carry `pid_nguon` links to libwacom or OpenTabletDriver; other PIDs have no source attached yet. `PacketRate` is a Wacom driver config value with no published unit; do not read it as Hz. Corrections with a source are welcome as issues.

**Build from source.** Download the code, then **double-click `build.bat`** (it explains what to do if .NET Framework 4.x is missing and opens the app when done). It runs `build.ps1`, which builds `MeinyaTabletBooster.exe` with the framework's `csc.exe`, runs `--self-test` (fake data, temp folder only) and `--ui-smoke-test` (briefly opens the form and installs the mouse hook). The script stops a running `MeinyaTabletBooster` process and deletes old builds in the project folder. Architecture notes: [MINDMAP.md](MINDMAP.md) (Vietnamese).

**License.** Code under MIT ([LICENSE](LICENSE)). The **Meinya** name and artwork in `assets/` and the **Elainabaka** name are not covered by the code license; do not use them to brand derivative works.
