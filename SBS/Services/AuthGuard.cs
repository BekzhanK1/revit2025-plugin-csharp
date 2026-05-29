using SmartRemont.ExportRooms.Views;
using System.Windows;

namespace SmartRemont.ExportRooms.Services
{
    public static class AuthGuard
    {
        /// <summary>
        /// Проверяет сохранённую сессию или показывает окно входа.
        /// </summary>
        public static bool EnsureAuthenticated()
        {
            var session = AuthService.RestoreSession();
            if (session != null)
                return true;

            var loginWindow = new AuthLoginWindow();
            var result = loginWindow.ShowDialog();
            return result == true && ExportRoomsApplication.CurrentSession != null;
        }
    }
}
