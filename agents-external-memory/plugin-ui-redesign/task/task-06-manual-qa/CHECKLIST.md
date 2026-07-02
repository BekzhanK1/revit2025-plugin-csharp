# Checklist — task-06-manual-qa

## Static verification (task-06 agent pass)

- [x] SPEC requirements implemented (Home + Hub code review)
- [x] No leftover `DsTkChangeButton`, `ByClientRequestIdRadio`, `ContinueButton`, `HasSurfacesRvtUrl` in `SBS/`
- [x] Edge cases verified in code: invalid ID → error + no hub; Cancel → `Result.Cancelled`; Logout → `AuthService.Logout()` clears session; null `RemontId` → hero shows `Ремонт #—`
- [x] `dotnet build SBS.sln -c Release` — 0 errors, 0 warnings (2026-07-02)

## Flow

> _requires manual Revit run_

- [ ] Revit → Smart Remont → логин OK
- [ ] Home: ввод `21642` → loader → результат → **один клик** → hub
- [ ] Hub: hero показывает Remont + Заявка
- [ ] «Синхронизация материалов из Revit» → окно открывается
- [ ] «ДС на изменение квадратуры» → окно открывается
- [ ] «Замеры комнат (из спецификаций)» → окно открывается
- [ ] «ДС на изменение ТК» → `RoomMaterialsWindow`
- [ ] Скрытые пункты **не видны** в UI

## Edge cases

> _requires manual Revit run (behavior confirmed in code review above)_

- [ ] Неверный ID → сообщение об ошибке, hub не открывается
- [ ] Cancel на Home → команда cancelled
- [ ] Logout на Home → сессия сброшена
- [ ] Remont без `RemontId` (только заявка) — поведение задокументировано / не ломает UI

## Build

- [x] `dotnet build SBS.sln -c Release`
- [ ] `dotnet build SBS.sln -c Release -p:DeployToRevit=true` (Revit закрыт) — _requires manual Revit run_
- [ ] Smoke в Revit 2025 — _requires manual Revit run_
