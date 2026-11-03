# Meinya Tablet Booster

<p align="center">
  <img src="assets/meinya_mascot.png" alt="Meinya" width="200" />
</p>

<p align="center">
  <b>Tác giả: <a href="https://github.com/Elainabaka">Elainabaka</a></b><br/>
  <i>Công cụ Windows nhỏ cho người dùng bảng vẽ: xem nhịp sự kiện chuột, thử làm mượt con trỏ bằng phần mềm, chỉnh vài khóa có sẵn trong cấu hình driver Wacom.</i>
</p>

[English below](#english)

## Công cụ làm gì, không làm gì

| | |
|---|---|
| **Làm** | Giữa hai mẫu chuột thật, ước lượng bước tiếp theo từ vận tốc vừa đo rồi gửi chuyển động **tương đối** qua `SendInput`. Trong ứng dụng đọc con trỏ chuột (ví dụ osu! ở chế độ chuột), con trỏ có thể trông mượt hơn trên màn tần số cao. Hãy tự thử để biết có hợp với bạn không. |
| **Không làm** | Không tăng tần số báo cáo thật của bảng vẽ, không sửa firmware. Tọa độ và lực nhấn đi qua **WinTab / Windows Ink** không đi qua bộ làm mượt này, nên phần mềm vẽ nhận bút qua các API đó không được gì thêm. Mẫu bút thật kế tiếp luôn kéo con trỏ về đúng chỗ đầu bút. |
| **Cần biết** | Bước tương đối chịu ảnh hưởng của tốc độ chuột và tùy chọn *Enhance pointer precision* của Windows. Số đo trong app là nhịp sự kiện chuột (có thể lẫn chuột thường, touchpad), không phải đo trực tiếp report rate USB. |

## Tính năng

1. **Nhận diện bảng vẽ**: quét USB/HID, đối chiếu với [`data/tablets_db.json`](data/tablets_db.json) (17 hồ sơ Wacom, XP-Pen, Huion, Gaomon, Veikk, Xencelabs). Vài hãng dùng chung VID/PID cho nhiều model nên có thể không phân biệt được từng model.
2. **Nhịp mục tiêu tham khảo**: đọc tần số màn hình, gợi ý `1x` và `2x` (màn 165 Hz → 165 / 330 Hz). Đây là gợi ý để thử, không phải chuẩn kỹ thuật, không cam kết độ trễ.
3. **Cảnh báo ba màu** (xanh, vàng, đỏ) theo ngưỡng tham khảo của hồ sơ. Màu không chứng minh giới hạn phần cứng hay độ an toàn.
4. **Cấu hình driver Wacom** (`%APPDATA%\WTablet\Wacom_Tablet.dat`): chỉ sửa khóa đã có đúng dạng (`PacketRate`, `MaxDataRateOn`, `WinUseInk`, `DoubleClickDistance`). Thiếu khóa thì báo "driver này không có khóa X, không sửa", không tự chèn. Lần sửa đầu sao lưu thành `.original_backup` (không ghi đè về sau); mỗi lần ghi qua tệp tạm rồi tráo nguyên tử; không có gì đổi thì không ghi.
5. **Bộ nhớ lịch sử cố định**: bộ đệm vòng 256 ô cho khoảng thời gian, 512 điểm cho canvas; có nút dọn.

## Dữ liệu và nguồn

- Mọi số Hz và ngưỡng trong hồ sơ đều là **ước lượng** (`"hz_nguon": "uoc-luong"`, app ghi "ước lượng" cạnh số): hãng không công bố số này cho từng model.
- PID có nguồn được ghi ở trường `pid_nguon` (libwacom, OpenTabletDriver): Intuos CTL-680 `0323`, CTH-680 `0303`, Intuos Pro gen 3 `03F5` / `03F7` / `03F9`, Xencelabs Small `5204`, Medium `5201`. PID của các hồ sơ khác chưa có nguồn kèm theo.
- `PacketRate` là giá trị trong cấu hình driver Wacom; hãng không công bố đơn vị, đừng coi nó là Hz.
- Thấy số sai? Mở issue kèm nguồn (trang hãng, libwacom, OpenTabletDriver).

## Chạy

Cần Windows và .NET Framework 4.x (có sẵn trên Windows 10/11).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Script tạo icon nếu thiếu, biên dịch bằng `csc.exe` của .NET Framework ra `MeinyaTabletBooster.exe`, rồi chạy `--self-test` (dữ liệu giả, chỉ ghi tệp trong thư mục tạm) và `--ui-smoke-test` (mở form và cài hook chuột trong giây lát). Script dừng tiến trình `MeinyaTabletBooster` đang chạy và xóa bản build cũ trong thư mục dự án. Sau đó mở `MeinyaTabletBooster.exe`.

Kiến trúc và các quy tắc bất biến của code: [MINDMAP.md](MINDMAP.md).

## Giấy phép

Code theo giấy phép MIT ([LICENSE](LICENSE)). Tên **Meinya**, hình Meinya trong `assets/` và tên **Elainabaka** không thuộc giấy phép code: không dùng chúng làm thương hiệu cho bản phái sinh.

Thấy công cụ có ích thì tặng repo một ⭐ để ủng hộ Elainabaka nhé.

---

## English

**Meinya Tablet Booster** is a small Windows tool for pen-tablet users: it shows the mouse-event rate, tries software cursor smoothing, and edits a few existing keys in the Wacom driver configuration.

**What it does.** Between two real mouse samples it predicts the next step from the measured velocity and injects a **relative** move with `SendInput`. In apps that read the mouse cursor (for example osu! in mouse mode) the cursor may look smoother on a high-refresh display. Try it and judge for yourself.

**What it does not do.** It does not raise the tablet's real report rate and does not touch firmware. Pen coordinates and pressure delivered through **WinTab / Windows Ink** do not pass through this smoother, so drawing apps that use those APIs gain nothing. The next real pen sample always pulls the cursor back to the pen tip. Relative steps are affected by Windows pointer speed and *Enhance pointer precision*. The in-app rate is the mouse-event rate (it can include other mice or a touchpad), not a direct USB report-rate measurement.

**Features.** Tablet detection against [`data/tablets_db.json`](data/tablets_db.json) (17 profiles; some brands share VID/PID across models). Reference target rates of 1x and 2x the display refresh, which are suggestions to try, not a standard or a latency promise. Three-color advisory against each profile's reference thresholds. Wacom driver config (`%APPDATA%\WTablet\Wacom_Tablet.dat`): only keys already present in the expected form are changed; a missing key is reported and never inserted; the first edit keeps a `.original_backup`; writes go through a temp file and an atomic replace, and nothing is written when nothing changed. Fixed-size history buffers (256 intervals, 512 canvas points).

**Data.** Every Hz value and threshold in the profiles is an **estimate** (`"hz_nguon": "uoc-luong"`; the app labels it). PIDs with a source carry `pid_nguon` links to libwacom or OpenTabletDriver; other PIDs have no source attached yet. `PacketRate` is a Wacom driver config value with no published unit; do not read it as Hz. Corrections with a source are welcome as issues.

**Build and run.** Windows with .NET Framework 4.x. Run `powershell -ExecutionPolicy Bypass -File .\build.ps1`: it builds `MeinyaTabletBooster.exe` with the framework's `csc.exe`, runs `--self-test` (fake data, temp folder only) and `--ui-smoke-test` (briefly opens the form and installs the mouse hook). The script stops a running `MeinyaTabletBooster` process and deletes old builds in the project folder. Architecture notes: [MINDMAP.md](MINDMAP.md) (Vietnamese).

**License.** Code under MIT ([LICENSE](LICENSE)). The **Meinya** name and artwork in `assets/` and the **Elainabaka** name are not covered by the code license; do not use them to brand derivative works.
