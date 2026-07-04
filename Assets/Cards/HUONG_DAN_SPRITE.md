# Hướng dẫn sprite lá bài (Assets\Cards\)

Game tự động nạp ảnh theo quy tắc đặt tên dưới đây. **Thiếu file nào thì lá đó tự
động vẽ fallback bằng chữ+ký hiệu (♠♥♦♣)** nên game vẫn chạy được ngay cả khi bạn
chưa có đủ 53 ảnh — cứ thả dần ảnh vào là game tự nhận, không cần build lại.

## Quy tắc đặt tên file (bắt buộc, chữ HOA)
`{RANK}{SUIT}.png`

- RANK: `2 3 4 5 6 7 8 9 10 J Q K A`
- SUIT: `C` = Chuồn/Tép (♣), `D` = Rô (♦), `H` = Cơ (♥), `S` = Bích (♠)
- Ví dụ: `AS.png` (Át Bích), `10H.png` (10 Cơ), `2C.png` (2 Chuồn)
- Mặt sau lá bài (khi úp): `back.png`

→ Tổng cộng 53 file, để hết vào thư mục `Assets\Cards\` (cùng cấp với file `.exe`
sau khi build).

Kích thước khuyến nghị: khổ dọc, tỉ lệ ~ **5:7** (ví dụ 500×700px), nền trong suốt
hoặc bo góc trắng đều được — game tự co giãn ảnh vào từng ô bài.

## Prompt tạo ảnh (dùng lại cho từng lá, chỉ đổi RANK + SUIT)

Prompt khung sườn — thay `{RANK}` và `{SUIT_NAME}` (ví dụ "spades / black" hay
"hearts / red") rồi tạo từng lá:

```
A clean, modern playing card face for "{RANK} of {SUIT_NAME}", portrait orientation,
5:7 aspect ratio, minimalist flat-design style, white card background with rounded
corners and a thin gray border, large rank symbol "{RANK}" in the top-left and
bottom-right corners, a bold centered suit icon, red ink for hearts/diamonds and
black ink for clubs/spades, no other text, no watermark, no photorealistic texture,
crisp vector-like line art, subtle drop shadow.
```

Prompt cho mặt sau lá bài (`back.png`):

```
A card back design for a playing card, portrait orientation, 5:7 aspect ratio,
deep navy blue background with a symmetrical golden geometric pattern, thin gold
border, elegant and minimal casino-style look, no text, no logo, no people, no
photorealistic texture, flat vector illustration style.
```

## Danh sách 52 lá cần tạo (RANK × SUIT)

Rank: `2, 3, 4, 5, 6, 7, 8, 9, 10, J, Q, K, A`
Suit: `Chuồn/Tép (C, black)`, `Rô (D, red)`, `Cơ (H, red)`, `Bích (S, black)`

Bạn có thể tạo từng lá một, hoặc nếu công cụ tạo ảnh hỗ trợ ghép nhiều ảnh trong 1
lần, có thể yêu cầu tạo cả bộ theo lưới 13×4 rồi cắt ra 52 file riêng theo đúng tên
ở trên (cắt bằng Paint.NET / Photoshop / GIMP đều được).




Mặt sau :

A premium luxury playing card back design, portrait orientation, 5:7 aspect ratio.

Deep navy blue background with a thin elegant gold border and perfectly symmetrical Art Deco-inspired geometric ornamentation radiating from the center.

The design consists only of refined geometric patterns, filigree linework, concentric shapes, ornamental frames, and decorative suit-inspired motifs integrated into the geometry. Every element is perfectly mirrored along both the vertical and horizontal axes.

Maintain generous negative space and balanced proportions for a clean, premium casino-quality appearance.

The card back must be fully reversible with no top or bottom orientation.

Do not include any human figure, portrait, face, animal, object, weapon, building, landscape, crown, shield, logo, letter, number, text, watermark, or recognizable symbol.

Flat luxury vector illustration with crisp clean line art, high resolution, no gradients, no photorealistic texture, no 3D rendering.



2 đến 10 

A premium modern playing card face representing the "{2} of {Cơ}", portrait orientation, 5:7 aspect ratio.

White card background with rounded corners, a thin soft-gray border, and a subtle drop shadow.

Large "{RANK}" with the matching {SUIT} symbol in the top-left corner, mirrored in the bottom-right corner.

The center contains only correctly arranged {SUIT} symbols following the authentic French playing card pip layout for the specified rank. Each suit symbol is identical in size and style, evenly spaced, perfectly symmetrical, and proportionally balanced.

Do not include any human figure, portrait, face, royal character, animal, object, decorative illustration, landscape, or scene. Do not replace or decorate the pip symbols with artistic elements.

Maintain generous white space and consistent visual scale across the entire deck.

Modern luxury vector illustration, crisp clean line art, balanced proportions, premium casino-quality design.

Red ink for Hearts and Diamonds, black ink for Clubs and Spades.

No watermark, no extra text, no photorealistic texture, no 3D rendering.


Quân A 

A premium modern playing card face for the "{A} of {Cơ}", portrait orientation, 5:7 aspect ratio.

Elegant luxury playing card design with a white background, rounded corners, a thin soft-gray border, and a subtle realistic drop shadow.

Large "{RANK}" with the matching {SUIT} symbol in the top-left corner, mirrored in the bottom-right corner.

A single ornate central {SUIT} emblem, occupying approximately 30–35% of the card height. Decorate the emblem with intricate filigree, elegant geometric motifs, and refined ornamental details while preserving the unmistakable silhouette of the suit. The ornamentation should enhance the symbol without making it appear oversized or visually heavy.

Perfectly centered with generous surrounding white space, creating a balanced, elegant composition. The Ace should stand out because of its craftsmanship and decorative detail, not because of its size. Maintain consistent visual scale with the numbered cards throughout the deck.

Modern luxury vector illustration, perfectly symmetrical, crisp clean line art, premium casino-quality design, high-resolution, balanced proportions.

Red ink for Hearts and Diamonds, black ink for Clubs and Spades.

No watermark, no extra text, no texture, no photorealistic elements.


Quân JQK 
  
  
  
  A premium modern playing card face representing the "{K} of {Cơ}", portrait orientation, 5:7 aspect ratio.

White card background with rounded corners, a thin soft-gray border, and a subtle drop shadow.

Large "{RANK}" with the matching {SUIT} symbol in the top-left corner, mirrored in the bottom-right corner.

A single elegant decorative {SUIT} emblem centered on the card, occupying approximately 30–35% of the card height. Enhance the emblem with intricate filigree, refined geometric ornamentation, and subtle Art Deco-inspired details while preserving the classic silhouette of the suit.

The center of the card must contain only the decorative {SUIT} emblem. Do not include any human figure, portrait, face, royal character, animal, object, landscape, or scene.

Perfect vertical centering and symmetry with generous white space for a refined premium appearance. Maintain consistent visual scale with the numbered cards throughout the deck.

Modern luxury vector illustration, crisp clean line art, balanced proportions, casino-quality design.

Red ink for Hearts and Diamonds, black ink for Clubs and Spades.

No watermark, no extra text, no photorealistic texture, no 3D rendering.