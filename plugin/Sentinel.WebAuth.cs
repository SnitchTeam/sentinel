using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Oxide.Plugins
{
    public class SentinelWebAuth
    {
        private readonly string _authToken;

        public SentinelWebAuth(string authToken)
        {
            _authToken = authToken ?? "";
        }

        public bool IsAuthenticated(HttpListenerRequest request)
        {
            var authHeader = request.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader))
            {
                return false;
            }

            const string bearerPrefix = "Bearer ";
            if (!authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var token = authHeader.Substring(bearerPrefix.Length);
            return SecureCompareTokens(token, _authToken);
        }

        public static bool SecureCompareTokens(string a, string b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            var bytesA = Encoding.UTF8.GetBytes(a);
            var bytesB = Encoding.UTF8.GetBytes(b);
            return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
        }
    }
}
