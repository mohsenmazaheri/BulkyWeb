namespace Bulky.Utility
{
    /// <summary>
    /// The first admin account, which DbInitializer creates on an empty database.
    /// Bound from the "AdminUser" configuration section. The password is never committed:
    /// set it with user secrets or the AdminUser__Password environment variable.
    /// </summary>
    public class AdminUserSettings
    {
        public const string SectionName = "AdminUser";

        public string Email { get; set; } = "admin@bulky.com";
        public string? Password { get; set; }
    }
}
