# Inno Setup installer for designers

## Что сделано

- `deploy/SmartRemont.ExportRooms.iss` — админ-установщик в `ProgramData\…\Addins\2025\` (DLL + `.addin`).
- `deploy/pack-installer.ps1` — Release-сборка → payload → `ISCC`.
- Если Revit.exe запущен — установка прерывается.
- Иконки: `SBS/Resources`, иначе `SmartRemont.ExportSpecifications/Resources`.

## Сборка

```powershell
powershell -ExecutionPolicy Bypass -File deploy\pack-installer.ps1
```

Нужен [Inno Setup 6](https://jrsoftware.org/isinfo.php). Готовый файл: `deploy/out/SmartRemont-Revit-2025-Setup.exe`.
