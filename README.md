# GamePvP-XiTo5La — Xì Tố 5 Lá Ăn Điểm

Chuyển thể từ kiến trúc mạng của "Vòng Quay Rồng" sang game bài kiểu "3 cây ăn điểm"
nhưng dùng **5 lá bài** và xếp hạng theo luật Xì Tố (Mậu thầu → Đôi → Thú → Sám cô →
Sảnh → Thùng → Cù lũ → Tứ quý → Thùng phá sảnh).

## Cách chơi 1 ván
1. Host bấm **"Bắt đầu ván mới"** → mọi người có 20 giây để chọn số điểm cược (50–200)
   rồi bấm **"Khoá cược"**.
2. Hết giờ (hoặc Host bấm **"Chia bài & So bài"** sớm), Host tự động chia ngẫu nhiên
   5 lá cho từng người đã cược và lật cùng lúc.
3. Mọi người đã cược được so bài theo từng cặp (kiểu "ăn điểm"): ai bài mạnh hơn ăn
   của người kia đúng bằng **mức cược nhỏ hơn** trong 2 người; hoà thì không đổi điểm
   (gần như không xảy ra vì mỗi lá bài là độc nhất trong bộ 52 lá).
4. Điểm được cộng/trừ ngay, Host có thể bắt đầu ván mới.

Host có thể bấm chuột phải vào thẻ 1 người chơi để nạp/trừ điểm thủ công (đổi ngược
điểm của Host) — dùng khi cần "đổi tiền mặt" ngoài đời.

## File trong dự án
- `XiTo5LaGame.vb` — logic thuần: bộ bài, xếp hạng 5 lá, tính điểm ăn/thua.
- `Form1.vb` — giao diện, state machine ván đấu, giao thức mạng (`XT5_...`).
- `NetworkHub.vb` / `NetworkPeer.vb` — tái sử dụng nguyên vẹn từ dự án Vòng Quay Rồng.
- `Program.vb` — điểm khởi động ứng dụng.
- `buildexe_xito5la.bat` — build bằng `vbc.exe` (không cần Visual Studio).
- `Assets\Cards\` — nơi đặt sprite lá bài (xem `Assets\Cards\HUONG_DAN_SPRITE.md`).

## Build
Chạy `buildexe_xito5la.bat`. File `.exe` sinh ra cần được đặt cùng thư mục với
`Assets\Cards\` (nếu muốn hiện ảnh lá bài thật thay vì hình vẽ chữ+ký hiệu mặc định).
