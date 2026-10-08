using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>Как выглядит этап процесса в хабе.</summary>
    public enum HubStepState
    {
        /// <summary>Предыдущий этап не пройден — этап закрыт.</summary>
        Locked,
        /// <summary>Этап, на котором сейчас работает проектировщик.</summary>
        Current,
        /// <summary>Действие сделано, ждём решения в MySpace (согласование ДС).</summary>
        Waiting,
        Done,
        /// <summary>Этап не нужен (площади не менялись) — считается пройденным.</summary>
        Skipped,
        /// <summary>Этап пройден, но есть проблема, которую решают в MySpace.</summary>
        Warning,
        /// <summary>Дальше не пройти без решения в MySpace (отказ, нет ремонта, нет прав, ошибка загрузки).</summary>
        Blocked
    }

    public sealed class HubStepStatus
    {
        public HubStepState State { get; init; }
        /// <summary>Короткий статус на плашке: «Утверждена №1042».</summary>
        public string Chip { get; init; }
        /// <summary>Одна-две строки: что сделать или почему этап закрыт.</summary>
        public string Hint { get; init; }
        /// <summary>Текст основной кнопки; null — кнопки нет.</summary>
        public string ActionText { get; init; }
        /// <summary>Открывает ли этап следующий.</summary>
        public bool Passed { get; init; }

        public bool HasAction => !string.IsNullOrWhiteSpace(ActionText);
    }

    public sealed class HubProcessInput
    {
        public bool IsInitialized { get; init; }
        public string ProjectPath { get; init; }
        public int? RemontId { get; init; }
        public bool HasDsAddGrant { get; init; }
        public bool HasDsEditGrant { get; init; }

        public bool DsLoaded { get; init; }
        public DsRoomChangeSnapshot Ds { get; init; }
        public string DsError { get; init; }

        public bool MeasuresLoaded { get; init; }
        public IReadOnlyList<MeasureRoomInfoDto> MeasureRooms { get; init; }
        public string MeasuresError { get; init; }

        /// <summary>Помещения модели (фаза «Новая конструкция»); null — не прочитаны.</summary>
        public IReadOnlyList<RoomAreaItem> RevitRooms { get; init; }

        public bool TkLoaded { get; init; }
        public DsTkChangeBindState Tk { get; init; }
        public string TkError { get; init; }
    }

    public sealed class HubProcess
    {
        public HubStepStatus Project { get; init; }
        public HubStepStatus DsArea { get; init; }
        public HubStepStatus Measures { get; init; }
        public HubStepStatus DsTk { get; init; }

        /// <summary>Номер этапа, на котором сейчас работа (1–4), для подписи «Этап 2 из 4».</summary>
        public int CurrentStepNumber
        {
            get
            {
                var steps = new[] { Project, DsArea, Measures, DsTk };
                for (var i = 0; i < steps.Length; i++)
                {
                    if (steps[i] != null && !steps[i].Passed)
                        return i + 1;
                }

                return steps.Length;
            }
        }

        public bool AllPassed => Project?.Passed == true && DsArea?.Passed == true
                                 && Measures?.Passed == true && DsTk?.Passed == true;
    }

    /// <summary>
    /// Процесс по заявке: проект → ДС на изменение квадратуры (утверждена) → замеры
    /// (подтверждены) → ДС на изменение ТК. Следующий этап открывается только после предыдущего.
    /// </summary>
    public static class HubProcessService
    {
        const int MaxListedRooms = 3;

        public static HubProcess Build(HubProcessInput input)
        {
            var project = BuildProject(input);
            var dsArea = BuildDsArea(input, project.Passed);
            var measures = BuildMeasures(input, dsArea.Passed);
            var dsTk = BuildDsTk(input, measures.Passed);

            return new HubProcess
            {
                Project = project,
                DsArea = dsArea,
                Measures = measures,
                DsTk = dsTk
            };
        }

        static HubStepStatus BuildProject(HubProcessInput input)
        {
            if (!input.IsInitialized)
            {
                return new HubStepStatus
                {
                    State = HubStepState.Current,
                    Chip = "Не инициализирован",
                    Hint = "Создайте файл проекта из шаблона заявки — материалы ТК загрузятся автоматически.",
                    ActionText = "Инициализировать проект"
                };
            }

            return new HubStepStatus
            {
                State = HubStepState.Done,
                Chip = "Инициализирован",
                Hint = string.IsNullOrWhiteSpace(input.ProjectPath)
                    ? "Заявка привязана к открытому файлу."
                    : "Файл: " + System.IO.Path.GetFileName(input.ProjectPath),
                Passed = true
            };
        }

        static HubStepStatus BuildDsArea(HubProcessInput input, bool previousPassed)
        {
            if (!previousPassed)
                return Locked("Откроется после инициализации проекта.");

            if (input.RemontId is not > 0)
                return Blocked("Нет ремонта", "По заявке ещё не создан ремонт — ДС недоступна.");

            if (!input.DsLoaded)
                return Blocked("Не загрузилось", "Не удалось получить ДС: " + (input.DsError ?? "ошибка") + ". Нажмите «Обновить».");

            var ds = input.Ds;
            var diff = CompareAreas(input.RevitRooms, input.MeasureRooms);

            if (ds?.DsId != null)
            {
                var number = $"№{ds.DsId}";
                var header = ds.Header;

                if (header?.IsAccept == 1)
                {
                    if (diff.Known && diff.Differences.Count > 0)
                    {
                        return new HubStepStatus
                        {
                            State = HubStepState.Warning,
                            Chip = $"Утверждена {number} · площади изменились",
                            Hint = "После утверждения площади в модели изменились: " + diff.Describe()
                                   + ". Решите в MySpace.",
                            ActionText = "Открыть",
                            Passed = true
                        };
                    }

                    return new HubStepStatus
                    {
                        State = HubStepState.Done,
                        Chip = $"Утверждена {number}",
                        ActionText = "Открыть",
                        Passed = true
                    };
                }

                if (header?.IsAccept == 2)
                    return Blocked($"Отказана {number}", "ДС отказана. Решите в MySpace, затем нажмите «Обновить».");

                if (header?.CardId != null)
                {
                    return new HubStepStatus
                    {
                        State = HubStepState.Waiting,
                        Chip = $"На согласовании {number}",
                        Hint = "Ждём утверждения в MySpace. После решения нажмите «Обновить»."
                    };
                }

                if (!input.HasDsEditGrant)
                    return Blocked($"Черновик {number}", "Нет прав на изменение ДС.");

                return new HubStepStatus
                {
                    State = HubStepState.Current,
                    Chip = $"Черновик {number}",
                    Hint = "Отправьте ДС на согласование в MySpace. Если площади в модели поменялись — обновите их.",
                    ActionText = "Обновить площади"
                };
            }

            if (diff.Known && diff.Differences.Count == 0)
            {
                return new HubStepStatus
                {
                    State = HubStepState.Skipped,
                    Chip = "Не требуется",
                    Hint = "Площади помещений в модели совпадают с системой.",
                    Passed = true
                };
            }

            if (!input.HasDsAddGrant)
                return Blocked("Нет прав", "Нет прав на создание ДС.");

            return new HubStepStatus
            {
                State = HubStepState.Current,
                Chip = "Нужна ДС",
                Hint = diff.Known
                    ? "Площади отличаются от системы: " + diff.Describe() + "."
                    : "Создайте ДС с площадями помещений из модели и отправьте на утверждение.",
                ActionText = "Создать ДС"
            };
        }

        static HubStepStatus BuildMeasures(HubProcessInput input, bool previousPassed)
        {
            if (!previousPassed)
                return Locked("Откроется после утверждения ДС на изменение квадратуры.");

            if (!input.MeasuresLoaded)
            {
                var error = input.MeasuresError ?? "ошибка";
                if (error.IndexOf("планировк", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Blocked("Нет планировки", "У заявки нет планировки — замеры отправить некуда.");

                return Blocked("Не загрузилось", "Не удалось получить замеры: " + error + ". Нажмите «Обновить».");
            }

            var rooms = (input.MeasureRooms ?? Array.Empty<MeasureRoomInfoDto>())
                .Where(r => r != null && r.PlanirovkaRoomId > 0)
                .ToList();
            var parameters = rooms.SelectMany(r => r.CurrentParameters ?? new List<MeasureApplyParamDto>()).ToList();
            var filled = parameters.Count(p => !string.IsNullOrWhiteSpace(p?.ParamValue));
            // is_measure_confirm — флаг всей планировки, приходит одинаковым в каждой комнате.
            var confirmed = input.MeasureRooms?.Any(r => r?.IsMeasureConfirm == 1) ?? false;

            if (confirmed)
            {
                return new HubStepStatus
                {
                    State = HubStepState.Done,
                    Chip = "Подтверждены",
                    ActionText = "Открыть",
                    Passed = true
                };
            }

            var hint = "Отправьте замеры из ведомостей. MySpace подтвердит их сам, когда заполнены все параметры.";
            var notInPlan = CountRevitRoomsNotInPlan(input.RevitRooms, input.MeasureRooms);
            if (notInPlan > 0)
                hint += $" В планировке нет {notInPlan} помещ. модели.";

            return new HubStepStatus
            {
                State = HubStepState.Current,
                Chip = parameters.Count > 0 ? $"Заполнено {filled} из {parameters.Count}" : "Не отправлены",
                Hint = hint,
                ActionText = "Отправить замеры"
            };
        }

        static HubStepStatus BuildDsTk(HubProcessInput input, bool previousPassed)
        {
            if (!previousPassed)
                return Locked("Откроется после подтверждения замеров.");

            if (!input.TkLoaded)
            {
                // Список office DS часто недоступен без грантов MySpace — окно сверки не блокируем.
                return new HubStepStatus
                {
                    State = HubStepState.Current,
                    Chip = "Сверка",
                    Hint = "Список ДС не загрузился" + (string.IsNullOrWhiteSpace(input.TkError) ? "" : ": " + input.TkError)
                           + ". Сверка с ТК доступна.",
                    ActionText = "Открыть"
                };
            }

            var items = input.Tk?.Items ?? new List<DsTkChangeItem>();
            if (items.Count == 0)
            {
                return new HubStepStatus
                {
                    State = HubStepState.Current,
                    Chip = "Не создана",
                    Hint = "Сверьте объёмы модели с ТК и отправьте изменения.",
                    ActionText = "Открыть сверку"
                };
            }

            var editable = items.Count(i => i.CanEdit);
            if (editable > 1)
            {
                return new HubStepStatus
                {
                    State = HubStepState.Current,
                    Chip = $"{editable} черновика",
                    Hint = "Несколько черновиков ДС ТК — выберите нужный в окне сверки.",
                    ActionText = "Открыть сверку"
                };
            }

            var item = DsTkChangeBindState.PreferHubBadge(items);
            if (item?.IsAccept == 1)
            {
                return new HubStepStatus
                {
                    State = HubStepState.Done,
                    Chip = $"Утверждена №{item.DsId}",
                    ActionText = "Открыть",
                    Passed = true
                };
            }

            if (item?.IsAccept == 2)
            {
                return new HubStepStatus
                {
                    State = HubStepState.Blocked,
                    Chip = $"Отказана №{item.DsId}",
                    Hint = "ДС отказана. Решите в MySpace или создайте новую в окне сверки.",
                    ActionText = "Открыть сверку"
                };
            }

            if (item?.CardId != null)
            {
                return new HubStepStatus
                {
                    State = HubStepState.Waiting,
                    Chip = $"На согласовании №{item.DsId}",
                    Hint = "Ждём утверждения в MySpace. Правки — только после решения.",
                    ActionText = "Открыть"
                };
            }

            return new HubStepStatus
            {
                State = HubStepState.Current,
                Chip = item != null ? $"Черновик №{item.DsId}" : "Не создана",
                Hint = "Сверьте объёмы с ТК, отправьте изменения и передайте ДС на согласование в MySpace.",
                ActionText = "Открыть сверку"
            };
        }

        static HubStepStatus Locked(string hint) => new()
        {
            State = HubStepState.Locked,
            Hint = hint
        };

        static HubStepStatus Blocked(string chip, string hint) => new()
        {
            State = HubStepState.Blocked,
            Chip = chip,
            Hint = hint
        };

        sealed class AreaDiff
        {
            /// <summary>false — сравнить нельзя (модель не прочитана или бэкенд без room_area).</summary>
            public bool Known { get; init; }
            public List<string> Differences { get; } = new();

            public string Describe()
            {
                var shown = Differences.Take(MaxListedRooms).ToList();
                var text = string.Join("; ", shown);
                if (Differences.Count > shown.Count)
                    text += $" и ещё {Differences.Count - shown.Count}";
                return text;
            }
        }

        /// <summary>
        /// Площади модели против FLOOR_AREA текущей планировки. После утверждения ДС планировка
        /// пересоздаётся с новыми площадями, поэтому это же сравнение ловит правки после утверждения.
        /// </summary>
        static AreaDiff CompareAreas(IReadOnlyList<RoomAreaItem> revitRooms, IReadOnlyList<MeasureRoomInfoDto> measureRooms)
        {
            if (revitRooms == null || revitRooms.Count == 0 || measureRooms == null)
                return new AreaDiff { Known = false };

            var planRooms = measureRooms.Where(r => r != null && r.PlanirovkaRoomId > 0).ToList();
            if (planRooms.Count == 0 || planRooms.All(r => r.RoomArea == null))
                return new AreaDiff { Known = false };

            var systemByKey = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            foreach (var room in planRooms)
            {
                var key = DsAreaCompareService.GetRoomCompareKey(room.RoomName);
                if (!string.IsNullOrEmpty(key) && !systemByKey.ContainsKey(key))
                    systemByKey[key] = room.RoomArea is > 0d ? Math.Round(room.RoomArea.Value, 2) : null;
            }

            // Как в окне ДС квадратуры: каждое помещение модели сравниваем с комнатой системы,
            // комнаты системы без помещения в модели — только с площадью > 0.
            var diff = new AreaDiff { Known = true };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var revitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var room in revitRooms)
            {
                var key = DsAreaCompareService.GetRoomCompareKey(room?.RoomName);
                if (string.IsNullOrEmpty(key))
                    continue;

                revitKeys.Add(key);
                var revit = room.AreaM2 > 0d ? Math.Round(room.AreaM2, 2) : (double?)null;
                systemByKey.TryGetValue(key, out var system);

                string text = DsAreaCompareService.CompareValues(system, revit) switch
                {
                    DsAreaCompareStatus.Mismatch => $"{key} {Format(system)} → {Format(revit)} м²",
                    DsAreaCompareStatus.RevitOnly => $"{key} — нет в системе",
                    DsAreaCompareStatus.SystemOnly => $"{key} — нет площади в модели",
                    _ => null
                };

                if (text != null && seen.Add(text))
                    diff.Differences.Add(text);
            }

            foreach (var pair in systemByKey)
            {
                if (pair.Value.HasValue && !revitKeys.Contains(pair.Key))
                    diff.Differences.Add($"{pair.Key} — нет в модели");
            }

            return diff;
        }

        static int CountRevitRoomsNotInPlan(IReadOnlyList<RoomAreaItem> revitRooms, IReadOnlyList<MeasureRoomInfoDto> measureRooms)
        {
            if (revitRooms == null || measureRooms == null)
                return 0;

            var planKeys = new HashSet<string>(
                measureRooms
                    .Where(r => r != null && r.PlanirovkaRoomId > 0)
                    .Select(r => RoomNameMatcher.GetBaseName(r.RoomName)),
                StringComparer.OrdinalIgnoreCase);

            return revitRooms
                .Select(r => RoomNameMatcher.GetBaseName(r?.RoomName))
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(k => !planKeys.Contains(k));
        }

        static string Format(double? value) =>
            value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "—";
    }
}
