# 生成 RayScreen 图标 assets/app.ico（深蓝底 + 关屏的显示器 + 月牙）
import os
from PIL import Image, ImageDraw

SS = 1024
img = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))

# 背景：圆角矩形竖直渐变
bg = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))
grad = Image.new("RGBA", (1, SS))
top, bottom = (66, 128, 245), (27, 74, 178)
for y in range(SS):
    t = y / (SS - 1)
    grad.putpixel((0, y), (
        int(top[0] + (bottom[0] - top[0]) * t),
        int(top[1] + (bottom[1] - top[1]) * t),
        int(top[2] + (bottom[2] - top[2]) * t),
        255,
    ))
grad = grad.resize((SS, SS))
mask = Image.new("L", (SS, SS), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, SS - 1, SS - 1], radius=int(SS * 0.22), fill=255)
bg.paste(grad, (0, 0), mask)
img = Image.alpha_composite(img, bg)

d = ImageDraw.Draw(img)

# 显示器外框（白色）
x0, y0, x1, y1 = int(SS * 0.18), int(SS * 0.24), int(SS * 0.82), int(SS * 0.64)
w = int(SS * 0.055)
r = int(SS * 0.05)
d.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=(255, 255, 255, 255))
inner = [x0 + w, y0 + w, x1 - w, y1 - w]
d.rounded_rectangle(inner, radius=max(1, r - w // 2), fill=(22, 30, 52, 255))

# 支架
sw = int(SS * 0.10)
cx = SS // 2
d.rounded_rectangle([cx - sw // 2, y1 - 2, cx + sw // 2, int(SS * 0.76)], radius=int(SS * 0.015), fill=(255, 255, 255, 255))
d.rounded_rectangle([cx - int(SS * 0.16), int(SS * 0.755), cx + int(SS * 0.16), int(SS * 0.80)],
                    radius=int(SS * 0.02), fill=(255, 255, 255, 255))

# 屏幕里的月牙（关屏 / 夜间）
mr = int(SS * 0.105)
mcx, mcy = int(SS * 0.50), int(SS * 0.44)
moon = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))
md = ImageDraw.Draw(moon)
md.ellipse([mcx - mr, mcy - mr, mcx + mr, mcy + mr], fill=(255, 255, 255, 235))
md.ellipse([mcx - mr + int(mr * 0.55), mcy - mr - int(mr * 0.28),
            mcx + mr + int(mr * 0.55), mcy + mr - int(mr * 0.28)], fill=(22, 30, 52, 255))
img = Image.alpha_composite(img, moon)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets")
os.makedirs(out, exist_ok=True)
ico = os.path.join(out, "app.ico")
img.resize((256, 256), Image.LANCZOS).save(
    ico, format="ICO",
    sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)],
)
print("written", os.path.normpath(ico))
