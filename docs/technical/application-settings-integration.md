# Application settings and desktop integration

The ShareX parity work is on `codex/application-settings-integration`. UI translations and Steam/Microsoft Store distribution are outside this change. Linux desktop integration is the implemented platform; Windows and macOS registration and printing backends remain deferred. ShareX has no text printing that a user can reach (its `PrintHelper(string)` constructor has no callers), so none was added.

## Settings

- **Export / Import:** ShareX's "Settings" and "History" check boxes, both on by default; Export is unavailable with neither. Settings is the existing XerahS portable backup (application, workflow, destination, custom uploader, and credential settings); restore validates the archive and re-encrypts credentials on the receiving machine. History is the SQLite history database, copied with SQLite's backup function so the copy is consistent while the database is open, and written back the same way; the archive's copy is checked with `PRAGMA integrity_check` and for the `History` table first, and the current history is put back if a later step fails. Import restores whatever the archive contains. The archive keeps the `.xsbak` extension: ShareX's `.sxb` archives use a different schema and credential store, and neither app reads the other's. ShareX destination configuration migration remains available in Destination Settings.
- **Export / Import → Automatic cleanup:** cleanup runs at startup. Both switches default to off, and the retained count defaults to 10, as in ShareX. The count applies separately to daily settings ZIPs, weekly settings ZIPs, legacy configuration backup types, and log types (main, error, and network monitor logs). Monthly archive directories are supported; unrelated files, symlinks, and the active log are excluded. The application no longer applies the old unconditional 90-day settings-backup pruning alongside these switches.
- **Advanced → Application Configuration:** “Save settings after task completed” defaults to off. When enabled, all three settings configurations are saved after the last pending task finishes, including failed or cancelled tasks. Serialization runs on the Avalonia thread when a UI is present, because editor preferences contain UI-owned objects.
- **Paths:** the secondary screenshots folder follows ShareX's directory-existence rule. With custom paths enabled, an existing primary folder wins. If the primary is missing, an existing secondary folder wins. If both are missing, the normal screenshots/screencasts root is used. A primary folder may still be created when no secondary is configured. Subfolder patterns and task-specific folder overrides keep their existing behavior.
- **Advanced → Application Configuration:** a custom browser executable receives web links (http and https, including local addresses) as one argument; other links, such as `mailto:`, stay with the desktop's opener. An empty path uses the system browser. Linux native and Wayland-portal callers honor the setting. Flatpak and Snap cannot launch an arbitrary host executable, so the field is disabled and links continue through the desktop portal.
- **General → Updates:** XerahS already had the Release/PreRelease channel and the pre-release source. As in ShareX, they are greyed out while automatic checks are off.

## Linux file managers and associations

Application Settings → Integration offers upload, image editing, Send To, `.sxcu`, `.sxie`, Chrome-family browser, and Firefox integration switches. Existing `.xsdp` support is retained.

| Surface | Integration |
|---|---|
| Dolphin | Executable service-menu files in `$XDG_DATA_HOME/kio/servicemenus`; image editing appears for images, upload for files/folders, and the existing Send To submenu is retained. |
| Thunar | Upload/edit custom actions in `$XDG_CONFIG_HOME/Thunar/uca.xml`, preserving other actions; Send To in `$XDG_DATA_HOME/Thunar/sendto`. Restart Thunar after changing custom actions. |
| Nautilus, Nemo, Caja | Upload/edit entries in the file manager's Scripts submenu; argument and environment-based selections are supported. Their Scripts interface does not provide Dolphin's image-only menu filtering. |
| MIME associations | Per-user MIME definitions, desktop handlers, and `mimeapps.list` entries, followed by MIME/desktop cache refresh. Removing an association preserves other handlers. |

Launchers refer to the persistent AppImage file, not its temporary mount. Installed entries are refreshed in the background at startup after an executable move or upgrade; only changed files are rewritten, and the MIME and desktop caches are rebuilt only after a change. Refresh does not enable new integrations or reset MIME defaults/native manifests changed outside XerahS. Flatpak and Snap retain the existing unsupported shell-integration backend.

Opening `.sxcu` imports a custom uploader, with ShareX's add/activate/cancel choices. Opening `.sxie` opens the image effects window with the imported preset, as ShareX's `ImportImageEffect` does, and then offers to enable the image-effects after-capture task. ShareX adds the preset to a list; XerahS keeps one preset per workflow, so it asks before replacing a preset that has effects. Unsupported effects are reported. Neither definition is uploaded as a file when passed directly to XerahS. Explicit `-CustomUploader`, `-ImageEffect`, and `-ImageEditor` arguments are supported; `--image-editor` is also accepted.

As in ShareX, a second start without arguments brings the main window forward, and one with arguments (a file-manager or browser upload) activates it only when it is already shown. Ordinary folder arguments recursively expand to files. Duplicate paths are removed and directory symlinks are not followed, preventing recursive loops. ShareX's bulk-upload confirmation applies above ten files when enabled. Existing XerahS Send To choices and remembered defaults remain available.

## Browser extension

The main application has a `--native-messaging-host` entry point that runs before logging, single-instance initialization, or Avalonia startup. Registration writes an executable wrapper plus manifests for Chrome, Chromium, Brave, Edge, and Firefox using ShareX's existing host names and extension IDs.

Messages use native-endian 32-bit lengths and UTF-8 JSON. The host handles fragmented reads, rejects invalid/truncated or oversized messages, and supports multiple requests. Input is limited to 64 MiB. ShareX's request echo is retained below the browser's 1 MiB response limit; larger requests receive a small acknowledgement, correcting ShareX's oversized-echo failure.

The host forwards through a private inbox (directory mode 0700, files 0600), launches the normal application with `-NativeMessagingInput`, and isolates the GUI's stdout from the browser protocol. The application consumes and removes its own inbox files. An explicitly supplied JSON file outside that inbox is not deleted.

Image data URLs run the normal image workflow. Remote image processing follows `ProcessImagesDuringExtensionUpload`; video/audio URLs use download-and-upload, text uses text upload, and URL shortening uses the configured shortener. Remote URLs must use HTTP or HTTPS. Existing extension permissions still control access to the native host. Sandboxed browsers may require their distributor's host bridge; host installation inside Flatpak/Snap XerahS is unavailable.

## Verification

The full desktop solution builds with zero warnings and zero errors, and all tests in `tests/XerahS.Tests` pass. Regression tests cover maintenance defaults and retention (including network monitor logs), active-log preservation, primary/fallback folder selection, task queue completion saves, custom browser arguments and link routing, settings and history backups (history only, both, neither, a damaged history database, and a history kept open while it is restored), shell registration/removal/refresh (including a refresh that finds nothing changed), preservation of unrelated desktop settings, literal filename forwarding through scripts and GIO, custom-uploader add/activate/cancel behavior and invalid-definition rejection, native-message framing/errors/limits, and browser action dispatch.

In a temporary home, the generated `.sxcu` handler passed `desktop-file-validate` and was launched through `gio launch` and KDE's `kioclient exec` with an AppImage path containing spaces and `%`; both passed the flag and the file as separate arguments. The Nautilus script forwarded files given as arguments and through `NAUTILUS_SCRIPT_SELECTED_FILE_PATHS`. The built host binary answered rejected requests with correctly framed replies and wrote nothing else to stdout.

Remaining interactive checks are a live browser extension, file-manager menu visibility across desktop environments, the image editor and image-effect import windows launched from those menus, and settings and history import in the running application. These are tracked as not yet tested in `local-notes/SHAREX_FEATURE_GAPS.md`.

## References

Behavior was checked against the local ShareX sources: `ApplicationConfig.cs`, `CleanupManager.cs`, `TaskManager.cs`, `Infrastructure/AppPaths.cs`, `SettingManager.cs`, `IntegrationHelpers.cs`, `ShareXCLIManager.cs`, `TaskHelpers.cs`, `UploadManager.cs`, and the native host/manifests.

Platform references: [desktop launcher arguments](https://specifications.freedesktop.org/desktop-entry-spec/latest/exec-variables.html), [Thunar custom actions](https://docs.xfce.org/xfce/thunar/custom-actions), [KDE service menus](https://develop.kde.org/docs/apps/dolphin/service-menus/), [Chrome native messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging), and [Mozilla native manifests](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_manifests).
