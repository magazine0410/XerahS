# FFmpeg in XerahS

XerahS uses FFmpeg as a recording backend across all supported platforms. The role FFmpeg plays differs per OS — on some platforms it is the primary recorder, on others it is a fallback behind a native API.

---

## Windows

### Installation

XerahS can download FFmpeg automatically from **Workflows > Edit workflow > Video Settings > FFmpeg Tools > Download FFmpeg...**. It fetches the latest build from the [ShareX/FFmpeg](https://github.com/ShareX/FFmpeg) GitHub release and extracts it to the app's `Tools` folder.

To use your own binary, open **Workflows > Edit workflow > Video Settings > FFmpeg Tools > Configure FFmpeg...**, enable **Override FFmpeg executable path**, and choose the binary there.

### Capture devices

On Windows, XerahS uses the following FFmpeg input devices:

| Device | Flag | Notes |
|---|---|---|
| GDI grab | `-f gdigrab` | Default fallback. Works on all Windows versions. Lower performance. |
| Desktop Duplication API | `-f ddagrab` | Hardware-accelerated. Requires Windows 8+. |
| DirectShow (screen-capture-recorder) | `-f dshow -i video="screen-capture-recorder"` | Third-party virtual device. |
| DirectShow (virtual-audio-capturer) | `-f dshow -i audio="virtual-audio-capturer"` | Third-party virtual audio device. |

The primary recording path on Windows uses **Windows.Graphics.Capture** (native API). FFmpeg with `gdigrab` is the fallback for systems that do not support it.

### Audio

System audio is captured via DirectShow (`-f dshow`) using **Stereo Mix**. This requires Stereo Mix to be enabled in Windows Sound settings (right-click the speaker icon > Sounds > Recording tab > Show Disabled Devices).

Microphone capture also uses DirectShow with the selected device ID.

### Example command (Windows fallback)

```
ffmpeg -f gdigrab -framerate 30 -draw_mouse 1 -i desktop -f dshow -i audio="Stereo Mix" -map 0:v -map 1:a -c:v libx264 -preset ultrafast -b:v 5000k -c:a aac -b:a 192k -pix_fmt yuv420p -y output.mp4
```

For a region:

```
ffmpeg -f gdigrab -framerate 30 -draw_mouse 1 -offset_x 100 -offset_y 50 -video_size 1280x720 -i desktop -c:v libx264 -preset ultrafast -b:v 5000k -pix_fmt yuv420p -y output.mp4
```

---

## macOS

### Installation

FFmpeg is not bundled. Install it via Homebrew:

```sh
brew install ffmpeg
```

XerahS searches `PATH` and common locations automatically. You can also set a custom path from **Workflows > Edit workflow > Video Settings > FFmpeg Tools > Configure FFmpeg...** by enabling **Override FFmpeg executable path**.

### Recording backends

XerahS prefers the native **ScreenCaptureKit** (AVAssetWriter) backend on macOS 12.3+. FFmpeg is used as a fallback when ScreenCaptureKit is unavailable.

| Backend | Condition |
|---|---|
| Native ScreenCaptureKit (primary) | macOS 12.3+ |
| FFmpeg `avfoundation` (fallback) | Older macOS or when native backend fails |

### Capture device

The `avfoundation` input device is used:

```
-f avfoundation -framerate 30 -i "1"
```

The input index `"1"` refers to the main display. Audio uses `-f avfoundation -i ":0"` for the default audio device.

Region capture is implemented as a crop filter applied post-capture, since `avfoundation` does not natively support offset/size arguments:

```
-vf "crop=1280:720:100:50"
```

### Example command (macOS fallback)

```sh
ffmpeg -f avfoundation -framerate 30 -capture_cursor 1 -i "1" -f avfoundation -i ":0" -map 0:v -map 1:a -c:v libx264 -preset ultrafast -b:v 5000k -c:a aac -b:a 192k -pix_fmt yuv420p -y output.mp4
```

---

## Linux

### Installation

Workflow recordings apply the configured FFmpeg options on Linux:

| Session / condition | Backend |
|---|---|
| Wayland with GStreamer `pipewiresrc` and FFmpeg | XDG ScreenCast portal → GStreamer raw frames → FFmpeg with the requested encoder/options |
| Wayland without GStreamer, but with an FFmpeg `pipewire` input | XDG ScreenCast portal + FFmpeg |
| X11 session | FFmpeg `x11grab` |
| Audio-only | FFmpeg PulseAudio input; no ScreenCast portal |

The older native GStreamer and wlroots `wf-recorder` routes remain available to callers without explicit FFmpeg options. Workflow recordings use FFmpeg encoding to honor detailed codec settings; missing encoders are reported instead of silently changing the output format.

If you want to use the FFmpeg Wayland path specifically, verify the actual binary first:

```sh
ffmpeg -devices 2>&1 | grep pipewire
```

If a `pipewire` line appears in the output, your FFmpeg binary exposes that input. If not, do not assume a newer distro release will add it automatically. XerahS can capture with GStreamer and feed raw frames to FFmpeg instead.

**Fedora / RHEL (RPM Fusion)**

RPM Fusion's build includes PipeWire. Enable it first if you haven't:

```sh
sudo dnf install https://mirrors.rpmfusion.org/free/fedora/rpmfusion-free-release-$(rpm -E %fedora).noarch.rpm
sudo dnf install ffmpeg
```

**Ubuntu / Debian**

The default `ffmpeg` package does **not** provide a reliable PipeWire guarantee across current Ubuntu and Debian releases. Install it if you need FFmpeg generally, but always verify the actual binary:

```sh
sudo apt update
sudo apt install ffmpeg
```

After installing, verify PipeWire support again:

```sh
ffmpeg -devices 2>&1 | grep pipewire
```

- If you now see a `pipewire` device, you can use the FFmpeg Wayland path.
- If you still do **not** see `pipewire`, use one of these supported alternatives instead:
  - Install GStreamer PipeWire support and let XerahS use the portal + `pipewiresrc` fallback.
  - Point XerahS at a custom FFmpeg binary that actually exposes the `pipewire` input device.

On Ubuntu, if you are only missing some codecs (e.g. H.264/MP3 playback) rather than PipeWire itself, you can optionally install:

```sh
sudo apt install ubuntu-restricted-extras
```

**Arch Linux**

Package contents change over time, so verify the actual binary instead of assuming support from the repo name alone:

```sh
sudo pacman -S ffmpeg
```

**NixOS**

Use `ffmpeg-full`, then verify the binary:

```nix
environment.systemPackages = [ pkgs.ffmpeg-full ];
```

**Static builds**

Do not assume a static FFmpeg build will provide Wayland portal capture support. Verify the binary with `ffmpeg -devices` before using it in XerahS.

GStreamer with PipeWire plugins supplies raw frames to the configured FFmpeg encoder:

```sh
# GStreamer PipeWire plugins (fallback)
sudo apt install gstreamer1.0-pipewire          # Debian/Ubuntu
sudo dnf install gstreamer1-plugin-pipewire     # Fedora
sudo pacman -S gst-plugin-pipewire              # Arch
```

### How screen recording works on Linux

When XerahS uses a portal-backed Wayland recorder, it talks to the **XDG ScreenCast portal** (`org.freedesktop.portal.ScreenCast`) to obtain a PipeWire stream from the compositor.

The flow is fully automatic:

1. XerahS opens a D-Bus session with the portal.
2. The compositor displays its own native source picker — the user selects a monitor or window.
3. The portal returns a PipeWire node ID for the selected source.
4. XerahS passes that node ID to the selected recorder integration.

“Show cursor in recording” selects the portal's embedded or hidden cursor mode, using the values defined by the [ScreenCast API](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html#org-freedesktop-portal-screencast-availablecursormodes).

**The user never needs to know or configure a PipeWire node ID.** It is resolved automatically per recording session.

### Session controls

**Task settings → Video Settings** contains the ShareX recording preferences: FPS/GIF FPS, cursor/highlighter, timer/button labels, automatic start and delay, fixed duration, lossless first-stage encoding, and abort confirmation. Defaults match ShareX.

Controls also open for recording hotkeys. As in ShareX, they sit below the recorded area when XerahS knows it (a region, or a window on X11); drag the timer to move them. Manual mode waits for Start; automatic mode counts the configured delay down in the timer. Fixed duration counts the time left, excluding pauses. While the final file is encoded, the controls are hidden and the tray icon and tooltip show the progress.

On Wayland, the ScreenCast source picker opens first, before the controls, the start delay and the manual start, so the countdown follows the choice of the recorded area as it does in ShareX. Closing the picker ends the recording quietly, like a cancelled region selection. Restart discards the take but keeps the chosen source, so the picker does not open again. Pause also keeps the portal session, and resume records another segment from the same stream; after stopping, FFmpeg joins the segments. X11 pause uses recording segments too.

Abort discards the take. With "Ask for confirmation when aborting", the Abort button, the tray's Abort and the recording page's Abort ask in the controls themselves; the Abort screen recording hotkey aborts without asking, as in ShareX. Restart discards without an abort prompt.

“Record losslessly first, then apply encoding options” follows ShareX's two-stage behavior: capture to lossless H.264, then encode the final file after stop. GIF, animated WebP and APNG always use this path. Source segments survive an encoding failure, and the error notification names them. Formats whose encoder needs even dimensions crop an odd-sized window or portal stream by one pixel, as ShareX's EvenRectangleSize does.

On X11 the recording highlighter uses the shared configurable overlay. On KDE Wayland it uses KWin's Mouse Click Animation, restoring the prior enabled/loaded state afterward. This appearance difference was explicitly chosen for Wayland, where passive global click events are unavailable to the shared overlay. On other Wayland desktops, a notification says that highlighting is not supported there, and the recording continues without it. The KWin integration uses its [Effects D-Bus interface](https://github.com/KDE/kwin/blob/master/src/org.kde.kwin.Effects.xml) and [mouseclick shortcut](https://github.com/KDE/kwin/blob/master/src/plugins/mouseclick/mouseclick.cpp).

### Custom commands

Extra FFmpeg arguments are appended before the generated output encoding options. Enabling a custom command replaces the generated command, except during lossless first-stage capture and the GIF conversion, which, as in ShareX, uses its own palette command without extra arguments. The supported ShareX placeholders are `$fps$`, `$area_x$`, `$area_y$`, `$area_width$`, `$area_height$`, `$cursor$`, `$duration$` and `$output$` (case-insensitive). Quote path placeholders in the command.

XerahS also supports `$input$`: it is `pipe:0` for the Wayland raw-frame bridge and the captured file during final encoding. A custom Wayland bridge command must consume the supplied raw stream with its format, even dimensions and frame rate. A custom final conversion command must read `$input$` and write `$output$`.

### Verify your setup

Run the built-in diagnostic from the app: **Workflows > Edit workflow > Video Settings > Linux Recording Diagnostics**. It reports:

- Whether FFmpeg has PipeWire input
- Whether GStreamer has PipeWire plugins
- The recommended backend for your session type
- Any missing dependencies with install suggestions

### Audio on Linux

System audio uses PulseAudio's default-output monitor (`@DEFAULT_MONITOR@`). Microphone capture uses the selected device or `default`. When both are enabled they are mixed into one audio track. The Wayland bridge ends live audio when its video stream closes, so stopping can finalize the file.

For audio-only recording, choose **Video source: None** and **System audio** or **Default microphone** in FFmpeg options. The selected AAC, Opus, Vorbis or MP3 encoder determines the `.m4a`, `.opus`, `.ogg` or `.mp3` extension. As in ShareX, the workflow still asks for its area first; the audio is recorded without the ScreenCast portal, and the video editor is skipped. When both sources are None, the recording does not start and a notification says so, as ShareX's message does.

### Example command (Wayland, FFmpeg + PipeWire)

Use this only when your FFmpeg build actually lists a `pipewire` input device:

```sh
ffmpeg \
  -f pipewire -framerate 30 -i <node_id> \
  -f pulse -i alsa_output.pci-0000_00_1f.3.analog-stereo.monitor \
  -map 0:v -map 1:a \
  -c:v libx264 -preset ultrafast -b:v 5000k \
  -c:a aac -b:a 192k \
  -pix_fmt yuv420p \
  -y output.mp4
```

With region crop:

```sh
ffmpeg \
  -f pipewire -framerate 30 -i <node_id> \
  -vf "crop=1280:720:0:0" \
  -c:v libx264 -preset ultrafast -b:v 5000k \
  -pix_fmt yuv420p \
  -y output.mp4
```

`<node_id>` is the integer PipeWire node provided by the portal — XerahS fills this in automatically.

### Example command (X11 fallback)

```sh
ffmpeg -f x11grab -framerate 30 -draw_mouse 1 -i :0.0 -c:v libx264 -preset ultrafast -b:v 5000k -pix_fmt yuv420p -y output.mp4
```

For a specific region on X11:

```sh
ffmpeg -f x11grab -framerate 30 -draw_mouse 1 -video_size 1280x720 -i :0.0+100,50 -c:v libx264 -preset ultrafast -b:v 5000k -pix_fmt yuv420p -y output.mp4
```

---

## Detailed FFmpeg codec options on Linux

| Codec | Options |
|---|---|
| H.264 / HEVC (`libx264` / `libx265`) | Preset and CRF, or bitrate |
| VP8 / VP9 (`libvpx` / `libvpx-vp9`) | Bitrate |
| Xvid (`libxvid`) | Quality scale; AVI output |
| NVENC | Preset, tune and bitrate |
| AMF | Usage, quality and bitrate |
| QSV | Preset and bitrate |
| GIF | FPS, maximum width, palette statistics and dithering |
| Animated WebP / APNG | Animated output after lossless capture |
| AAC / Opus | Audio bitrate |
| Vorbis / MP3 | Audio quality scale |

The existing AV1 choice uses `libaom-av1`. Hardware codecs require support in both the installed FFmpeg build and the system's GPU/driver. Encoder failures are reported and do not proceed to upload. As in ShareX, only **Upload image to host** enables upload; choosing after-upload actions alone does not.

The detailed capture options above are implemented for Linux. Windows/macOS native recording paths retain their existing platform behavior; those platform-specific settings integrations are deferred.

---

## Custom FFmpeg path

If XerahS cannot find FFmpeg automatically, open **Workflows > Edit workflow > Video Settings > FFmpeg Tools > Configure FFmpeg...**, enable **Override FFmpeg executable path**, and set the binary path there. The app checks (in order):

1. Explicitly configured path
2. `Options.CLIPath` if override is enabled
3. `PATH` environment variable and common install locations (`Tools/`, `Program Files/FFmpeg/bin/`, etc.)
