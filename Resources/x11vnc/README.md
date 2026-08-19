# x11vnc 静态编译二进制存放说明

本目录用于存放 x11vnc 的静态编译二进制，由 KylinOS 运维策略的「VNC Server」Tab 在部署时读取（按目标机架构自动选择）：

- `x11vnc_x86_64` —— x86_64 架构（约 2.5MB）
- `x11vnc_aarch64` —— aarch64/arm64 架构（约 2.5MB）

## 编译方法（在联网 Linux 构建机上执行，一次性）

```bash
cd x11vnc
autoreconf -fiv
./configure \
    --without-avahi --without-cairo \
    --without-xdamage --without-xfixes \
    --without-xrandr --without-xinerama \
    --without-xrecord --without-xcomposite \
    --without-xkeyboard --without-xtrap \
    --without-fbpm --without-dpms \
    --without-v4l --without-fbdev --without-uinput
make -j$(nproc) LDFLAGS="-static -lssl -lcrypto -ljpeg -lz -lvncserver -lvncclient"

# 验证
ldd src/x11vnc
# 预期仅含: libX11.so.6, libXext.so.6, libXtst.so.6, libc.so.6

# 复制到项目
cp src/x11vnc /path/to/ToolHelper/Resources/x11vnc/x11vnc_x86_64
```

## 说明

- 二进制缺失时，「部署」操作会提示「x11vnc 二进制文件未找到」，不影响其他功能。
- 本 README.md 不随发布复制（csproj 已排除 *.md）。
