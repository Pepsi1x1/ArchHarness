using System.Text;
using ArchHarness.App.SourceControl;

namespace ArchHarness.App.Tests.Core;

public sealed partial class FileSystemGlobalSettingsCatalogTests
{
    private sealed class TestPersonalAccessTokenProtector : IPersonalAccessTokenProtector
    {
        private readonly bool _canProtect;

        public TestPersonalAccessTokenProtector(bool canProtect)
        {
            this._canProtect = canProtect;
        }

        public bool CanProtect => this._canProtect;

        public string? UnavailableReason => this._canProtect
            ? null
            : "Secure token storage is unavailable in this test instance.";

        public string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null)
        {
            if (!this._canProtect)
            {
                throw new PlatformNotSupportedException(this.UnavailableReason);
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"protected::{personalAccessToken}"));
        }

        public string Unprotect(string protectedPersonalAccessToken)
        {
            string value = Encoding.UTF8.GetString(Convert.FromBase64String(protectedPersonalAccessToken));
            return value["protected::".Length..];
        }
    }
}
