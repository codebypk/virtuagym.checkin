using System;

namespace Jablotron.API.Services
{
    /// <summary>
    /// Manages a long-lived JablotronCloudService instance with automatic session handling.
    /// The underlying JablotronCloudApiClient already performs transparent re-login on 401,
    /// so explicit PerformLogin() is only needed once for the initial session.
    /// Thread-safe: uses lock for instance creation/recreation.
    /// </summary>
    public sealed class JablotronCloudServiceFactory : IDisposable
    {
        private readonly string _apiBaseUrl;
        private readonly string _username;
        private readonly string _password;
        private readonly object _lock = new();
        private JablotronCloudService _instance;
        private bool _disposed;

        public JablotronCloudServiceFactory(string apiBaseUrl, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl)) throw new ArgumentException("Required.", nameof(apiBaseUrl));
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Required.", nameof(username));
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Required.", nameof(password));

            _apiBaseUrl = apiBaseUrl;
            _username = username;
            _password = password;
        }

        /// <summary>
        /// Returns a shared, login-ready JablotronCloudService instance.
        /// Initial login is performed lazily on first call.
        /// Subsequent calls reuse the session; auto-relogin handles expiry.
        /// </summary>
        public JablotronCloudService GetClient()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(JablotronCloudServiceFactory));

            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new JablotronCloudService(_apiBaseUrl, _username, _password);
                    _instance.PerformLogin();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Forces a fresh client (e.g. after credential changes).
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _instance?.Dispose();
                _instance = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
            {
                _instance?.Dispose();
                _instance = null;
            }
        }
    }
}
