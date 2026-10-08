using System.Threading.Tasks;
using System.Windows.Threading;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// Даёт окну WPF перерисоваться и обработать клики между тяжёлыми синхронными вызовами
    /// Revit API (открытие RFA, LoadFamily). Продолжение возвращается в тот же UI-поток Revit,
    /// поэтому вызывать Revit API после await можно. Внутри открытой Transaction не вызывать.
    /// </summary>
    internal static class UiYield
    {
        public static async Task ToUiAsync()
        {
            var dispatcher = Dispatcher.FromThread(System.Threading.Thread.CurrentThread);
            if (dispatcher == null)
                return;

            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }
}
