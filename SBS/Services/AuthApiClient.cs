using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// HTTP к Smart Remont API с автоматическим refresh JWT при 401.
    /// </summary>
    public static class AuthApiClient
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        static readonly SemaphoreSlim RefreshLock = new SemaphoreSlim(1, 1);

        public static async Task<HttpResponseMessage> SendAsync(
            Func<HttpRequestMessage> requestFactory,
            CancellationToken cancellationToken = default)
        {
            if (requestFactory == null)
                throw new ArgumentNullException(nameof(requestFactory));

            var response = await SendOnceAsync(requestFactory, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return response;

            response.Dispose();

            if (!await TryRefreshTokenAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Сессия истекла. Выйдите и войдите снова.");

            return await SendOnceAsync(requestFactory, cancellationToken).ConfigureAwait(false);
        }

        static async Task<HttpResponseMessage> SendOnceAsync(
            Func<HttpRequestMessage> requestFactory,
            CancellationToken cancellationToken)
        {
            var request = requestFactory();
            if (request == null)
                throw new InvalidOperationException("HTTP request factory returned null.");

            var session = ExportRoomsApplication.CurrentSession;
            if (session != null && !string.IsNullOrWhiteSpace(session.AccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }

        static async Task<bool> TryRefreshTokenAsync(CancellationToken cancellationToken)
        {
            await RefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await AuthService.TryRefreshTokenCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                RefreshLock.Release();
            }
        }
    }
}
